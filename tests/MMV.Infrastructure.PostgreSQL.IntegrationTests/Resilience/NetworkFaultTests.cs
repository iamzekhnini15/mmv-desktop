using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// P4-10 — perte réseau réelle entre un poste et le serveur (proxy TCP qui coupe les sockets), contre les
/// implémentations de PRODUCTION (<see cref="EfTransactionRunner"/>, <see cref="PersistenceErrorMapper"/>). Chaque
/// état final est relu par une connexion directe, hors proxy.
/// </summary>
public sealed class NetworkFaultTests : PostgreSqlTestBase
{
    private OpticDbContext ContextVia(TcpFaultProxy proxy)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = proxy.Route(Database.ConnectionString)
        });
        return new OpticDbContext(builder.Options);
    }

    private async Task<int> SuppliersNamedAsync(string name)
    {
        await using var verify = NewContext();
        return await verify.Suppliers.CountAsync(s => s.Name == name);
    }

    private static Task SaveSupplierAsync(OpticDbContext context, string name) =>
        new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Suppliers.Add(new Supplier { Name = name });
            await context.SaveChangesAsync(ct);
        });

    [PostgreSqlFact]
    public async Task Lost_commit_acknowledgement_is_reported_as_an_unknown_outcome_never_as_nothing_kept()
    {
        await using var proxy = new TcpFaultProxy(Database.ConnectionString);
        await using var context = ContextVia(proxy);
        // Texte de requête terminé par NUL (protocole PostgreSQL) : « BEGIN … READ COMMITTED » ne déclenche pas.
        proxy.CutAfterClientSends("COMMIT\0");

        var act = () => SaveSupplierAsync(context, "Validation perdue");

        var thrown = (await act.Should().ThrowAsync<PersistenceException>()).Which;
        (await SuppliersNamedAsync("Validation perdue")).Should().Be(1, "le serveur a reçu et exécuté COMMIT");
        thrown.Message.Should().NotContain("Aucune modification n'a été conservée",
            "affirmer qu'aucune modification n'a été conservée serait faux : elle l'a été");
        thrown.Category.Should().Be(PersistenceErrorCategory.CommitOutcomeUnknown);
        thrown.Message.Should().Contain("Vérifiez avant de le saisir à nouveau");
        proxy.Accepted.Should().Be(1, "aucune nouvelle tentative automatique (O13)");
        AssertNoConnectionDetail(thrown.Message, proxy);
    }

    [PostgreSqlFact]
    public async Task Network_loss_inside_a_transaction_keeps_nothing_and_is_reported_once_without_retry()
    {
        await using var proxy = new TcpFaultProxy(Database.ConnectionString);
        await using var context = ContextVia(proxy);

        var act = () => new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Suppliers.Add(new Supplier { Name = "Coupé avant validation" });
            await context.SaveChangesAsync(ct);
            proxy.Down();
            context.Suppliers.Add(new Supplier { Name = "Coupé avant validation (2)" });
            await context.SaveChangesAsync(ct);
        });

        var thrown = (await act.Should().ThrowAsync<PersistenceException>()).Which;
        thrown.Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
        thrown.Message.Should().Contain("Aucune modification n'a été conservée", "avant COMMIT, le serveur annule la transaction");
        (await SuppliersNamedAsync("Coupé avant validation")).Should().Be(0);
        proxy.Accepted.Should().Be(1, "aucune nouvelle tentative automatique (O13)");
        AssertNoConnectionDetail(thrown.Message, proxy);
    }

    [PostgreSqlFact]
    public async Task After_the_network_comes_back_the_next_operation_succeeds_without_restarting_the_workstation()
    {
        await using var proxy = new TcpFaultProxy(Database.ConnectionString);
        await using var context = ContextVia(proxy);
        await SaveSupplierAsync(context, "Avant la panne");

        proxy.Down();
        var duringOutage = () => SaveSupplierAsync(context, "Pendant la panne");
        (await duringOutage.Should().ThrowAsync<PersistenceException>()).Which.Category
            .Should().Be(PersistenceErrorCategory.ConnectionFailure, "réseau coupé : échec franc, rien n'est conservé");

        proxy.Restore();
        await SaveSupplierAsync(context, "Après la panne");

        (await SuppliersNamedAsync("Pendant la panne")).Should().Be(0);
        (await SuppliersNamedAsync("Après la panne")).Should().Be(1, "même contexte, même poste : une connexion neuve suffit");
    }

    [PostgreSqlFact]
    public async Task Server_unreachable_from_the_first_operation_fails_fast_as_connection_failure()
    {
        await using var proxy = new TcpFaultProxy(Database.ConnectionString);
        proxy.Down();
        await using var context = ContextVia(proxy);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var act = () => SaveSupplierAsync(context, "Jamais");

        (await act.Should().ThrowAsync<PersistenceException>()).Which.Category.Should().Be(PersistenceErrorCategory.ConnectionFailure);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "connexion refusée : aucune attente prolongée ni nouvelle tentative");
    }

    [PostgreSqlFact]
    public async Task Statement_blocked_beyond_the_command_timeout_is_reported_busy_and_keeps_nothing()
    {
        long supplierId;
        await using (var setup = NewContext())
        {
            supplierId = await Arrange.SupplierAsync(setup, "Verrouillé");
        }

        // Un autre poste tient la ligne (transaction ouverte, verrou de ligne).
        await using var holder = new Npgsql.NpgsqlConnection(Database.ConnectionString);
        await holder.OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        await using (var lockRow = new Npgsql.NpgsqlCommand(
                         $"SELECT 1 FROM \"Suppliers\" WHERE \"SupplierId\" = {supplierId} FOR UPDATE", holder, holding))
        {
            await lockRow.ExecuteNonQueryAsync();
        }

        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(Database.ConnectionString) { CommandTimeout = 2 }.ConnectionString
        });
        await using var context = new OpticDbContext(builder.Options);

        var act = () => new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Suppliers.Add(new Supplier { Name = "Écrit avant le blocage" });
            await context.SaveChangesAsync(ct);
            await context.Suppliers.Where(s => s.SupplierId == supplierId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Écrasé"), ct);
        });

        (await act.Should().ThrowAsync<PersistenceException>()).Which.Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
        await holding.RollbackAsync();
        (await SuppliersNamedAsync("Écrit avant le blocage")).Should().Be(0, "le délai dépassé annule toute la transaction");
        (await SuppliersNamedAsync("Verrouillé")).Should().Be(1);
    }

    [PostgreSqlFact]
    public void No_automatic_retry_strategy_is_configured_for_the_server_provider()
    {
        using var context = NewContext();

        context.Database.CreateExecutionStrategy().RetriesOnFailure
            .Should().BeFalse("O13 : aucune nouvelle tentative sans clé d'idempotence — stock et numérotation non idempotents");
    }

    private void AssertNoConnectionDetail(string message, TcpFaultProxy proxy)
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Database.ConnectionString);
        message.Should().NotContain(target.Host!).And.NotContain(target.Username!).And.NotContain(target.Password!)
            .And.NotContain(proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)).And.NotContain(target.Database!);
    }
}
