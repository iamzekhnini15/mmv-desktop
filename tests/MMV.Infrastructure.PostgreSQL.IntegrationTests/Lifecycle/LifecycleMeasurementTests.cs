using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.DatabaseManager.Locking;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// P4-6B §3.4 — mesures du verrou sur EF Core 10.0.12 / Npgsql 10.0.3 / PostgreSQL 17.10, figées en tests pour
/// qu'une montée de version qui les change soit visible. M-5 est porté par I-3 / I-8, M-6 par I-11, M-7 par I-5,
/// M-8 par la sonde de I-3 (exécutée sous le rôle migrateur).
/// </summary>
public sealed class LifecycleMeasurementTests : LifecycleTestBase
{
    [PostgreSqlFact]
    public async Task M1_native_EF_lock_release_behavior_on_Npgsql()
    {
        await using var context = Db.Context<ChainS1>(Db.MigratorConnectionString);

        var behavior = context.GetService<IHistoryRepository>().LockReleaseBehavior;

        behavior.Should().Be(LockReleaseBehavior.Transaction,
            "le verrou natif (LOCK TABLE) suit la transaction : il ne couvre pas une montée à plusieurs migrations");
    }

    [PostgreSqlFact]
    public async Task M3_session_lock_survives_a_connection_returned_to_the_pool()
    {
        await using (var pooled = new NpgsqlConnection(Db.MigratorConnectionString))
        {
            await pooled.OpenAsync();
            await using var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({PostgreSqlAdvisoryMigrationLock.LockKey})", pooled);
            ((bool)(await command.ExecuteScalarAsync())!).Should().BeTrue();
        } // rendue au pool SANS pg_advisory_unlock

        (await Db.AdvisoryHoldersAsync()).Should().Be(1,
            "la session physique reste ouverte dans le pool : la libération explicite en finally est OBLIGATOIRE");

        NpgsqlConnection.ClearPool(new NpgsqlConnection(Db.MigratorConnectionString));
        await LifecycleDatabase.WaitUntilAsync(async () => await Db.AdvisoryHoldersAsync() == 0,
            TimeSpan.FromSeconds(10), "libération en fin de session");
    }

    /// <summary>
    /// Mesure : sur base vide, <c>Migrate()</c> dans une transaction utilisateur n'est pas refusé par
    /// l'avertissement EF traité en erreur (hypothèse de §3.1.1, non observée) ; il échoue côté PostgreSQL, la
    /// transaction englobante étant avortée (<c>25P02</c>) par une erreur interne au chemin de migration. La
    /// conséquence pour l'outil est la même : <c>Migrate()</c> n'est jamais enveloppé dans une transaction.
    /// </summary>
    [PostgreSqlFact]
    public async Task M9_Migrate_inside_a_user_transaction_fails_on_PostgreSQL()
    {
        await using var context = Db.Context<ChainS1>(Db.MigratorConnectionString);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var act = () => context.Database.MigrateAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25P02");
    }
}
