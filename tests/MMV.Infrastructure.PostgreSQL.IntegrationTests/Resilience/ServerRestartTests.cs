using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// P4-10 — critère de sortie 10 : <b>redémarrage réel</b> du serveur (arrêt rapide puis démarrage du conteneur dédié,
/// comme un redémarrage du service Windows), pendant qu'un poste a une transaction en cours et garde son contexte
/// (l'application compose un seul contexte pour la session). Aucune nouvelle tentative automatique (O13) : la
/// reprise est le fait de l'opération suivante de l'utilisateur.
/// </summary>
public sealed class ServerRestartTests : IAsyncLifetime
{
    private string _server = null!;
    private string _container = null!;
    private string _database = null!;

    private const string AppSecret = "mmv_it_restart_app_secret";
    private string _appRole = null!;

    /// <summary>Connexion du <b>poste</b> : rôle applicatif non privilégié (la sonde D-15 refuse toute autre identité).</summary>
    private string Cs => new NpgsqlConnectionStringBuilder(_server) { Database = _database, Username = _appRole, Password = AppSecret }.ConnectionString;

    private string AdminOnDatabase => new NpgsqlConnectionStringBuilder(_server) { Database = _database }.ConnectionString;

    public async Task InitializeAsync()
    {
        _server = PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.RestartConnectionStringVariableName);
        _container = PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.RestartContainerVariableName);
        var suffix = Guid.NewGuid().ToString("N");
        _database = "mmv_it_restart_" + suffix;
        _appRole = "mmv_it_restart_app_" + suffix[..12];
        await ExecuteAsync(_server, $"CREATE DATABASE \"{_database}\"");
        await ExecuteAsync(_server, $"CREATE ROLE \"{_appRole}\" LOGIN PASSWORD '{AppSecret}'");
        await using (var context = Context(AdminOnDatabase))
        {
            await context.Database.MigrateAsync();
        }

        await ExecuteAsync(AdminOnDatabase, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{_appRole}\"");
        await ExecuteAsync(AdminOnDatabase, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO \"{_appRole}\"");
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(Cs));
        await ExecuteAsync(_server, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
        await ExecuteAsync(_server, $"DROP ROLE IF EXISTS \"{_appRole}\"");
    }

    private OpticDbContext Context(string? connectionString = null)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = connectionString ?? Cs
        });
        return new OpticDbContext(builder.Options);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> SuppliersNamedAsync(string name)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Cs) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM \"Suppliers\" WHERE \"Name\" = @n", connection);
        command.Parameters.AddWithValue("n", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task SaveSupplierAsync(OpticDbContext context, string name) =>
        new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Suppliers.Add(new Supplier { Name = name });
            await context.SaveChangesAsync(ct);
        });

    /// <summary>Arrêt rapide (SIGINT au postmaster : transactions en cours annulées), puis démarrage et attente.</summary>
    private async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    /// <summary>Arrêt rapide, attendu jusqu'à ce que le conteneur soit réellement arrêté.</summary>
    private async Task StopAsync()
    {
        await DockerAsync("kill", "--signal=SIGINT", _container);
        var clock = Stopwatch.StartNew();
        while (await IsRunningAsync())
        {
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "arrêt du serveur");
            await Task.Delay(200);
        }
    }

    private async Task<bool> IsRunningAsync() =>
        (await DockerAsync("inspect", "-f", "{{.State.Running}}", _container)).Trim() == "true";

    /// <summary>Démarrage d'un serveur ARRÊTÉ (jamais un second signal d'arrêt), puis attente qu'il réponde.</summary>
    private async Task StartAsync()
    {
        var clock = Stopwatch.StartNew();
        while (await IsRunningAsync())
        {
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "fin de l'arrêt en cours");
            await Task.Delay(200);
        }

        await DockerAsync("start", _container);
        while (true)
        {
            try
            {
                await ExecuteAsync(_server, "SELECT 1");
                return;
            }
            catch (Exception) when (clock.Elapsed < TimeSpan.FromSeconds(90))
            {
                await Task.Delay(250);
            }
        }
    }

    private static async Task<string> DockerAsync(params string[] args)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, $"docker {string.Join(' ', args)} : {error}");
        return output;
    }

    [PostgreSqlRestartFact]
    public async Task Server_restart_aborts_the_in_flight_transaction_and_the_same_workstation_resumes_without_restarting()
    {
        await using var context = Context();
        await SaveSupplierAsync(context, "Avant le redémarrage");

        var inTransaction = new TaskCompletionSource();
        var resume = new TaskCompletionSource();
        var inFlight = new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Suppliers.Add(new Supplier { Name = "En vol" });
            await context.SaveChangesAsync(ct);
            inTransaction.SetResult();
            await resume.Task;
            context.Suppliers.Add(new Supplier { Name = "En vol (suite)" });
            await context.SaveChangesAsync(ct);
        });
        await inTransaction.Task;

        await RestartAsync();
        resume.SetResult();

        var aborted = (await FluentActions.Awaiting(() => inFlight).Should().ThrowAsync<PersistenceException>()).Which;
        aborted.Message.Should().Contain("Aucune modification n'a été conservée", "avant COMMIT, l'arrêt du serveur annule la transaction");

        // Reprise : chaque opération de l'utilisateur est tentée UNE fois ; les connexions du pool mortes avec
        // l'ancien serveur sont écartées une à une. Mesure du nombre d'échecs avant la première réussite.
        var failures = 0;
        while (true)
        {
            try
            {
                await SaveSupplierAsync(context, "Après le redémarrage");
                break;
            }
            catch (PersistenceException failure)
            {
                failure.Category.Should().BeOneOf(PersistenceErrorCategory.ConnectionFailure, PersistenceErrorCategory.Unknown);
                (++failures).Should().BeLessThan(3, "le poste reprend sans redémarrage de l'application");
            }
        }

        await PostgreSqlConnectivityProbe.EnsureAvailableAsync(Cs);
        (await SuppliersNamedAsync("Avant le redémarrage")).Should().Be(1, "les données validées survivent au redémarrage");
        (await SuppliersNamedAsync("En vol")).Should().Be(0);
        (await SuppliersNamedAsync("Après le redémarrage")).Should().Be(1, "jamais en double malgré les échecs de reprise");
        Console.WriteLine($"P4-10 mesure : {failures} échec(s) avant la reprise après redémarrage.");
    }

    [PostgreSqlRestartFact]
    public async Task Workstation_starting_while_the_server_is_down_is_refused_then_starts_once_it_is_back()
    {
        await StopAsync();
        try
        {
            var probe = () => PostgreSqlConnectivityProbe.EnsureAvailableAsync(Cs);
            (await probe.Should().ThrowAsync<DatabaseUnavailableException>()).Which.Message.Should().NotContain(
                AppSecret, "aucun secret dans le message de blocage (D-15)");
        }
        finally
        {
            await StartAsync();
        }

        await PostgreSqlConnectivityProbe.EnsureAvailableAsync(Cs);
        await using var context = Context();
        await SaveSupplierAsync(context, "Premier démarrage après retour");
        (await SuppliersNamedAsync("Premier démarrage après retour")).Should().Be(1);
    }
}
