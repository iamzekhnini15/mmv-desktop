using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// Base <b>vide</b> jetable et ses deux rôles de test (P4-6B, §7.3) : un <b>migrateur</b> propriétaire de la
/// base (DDL autorisé) et un rôle <b>applicatif</b> sans aucun droit DDL. Noms uniques, créés par le test avec
/// l'utilisateur éphémère du serveur, supprimés en fin de test. Les noms réels des rôles appartiennent à P4-8 :
/// rien ici n'est un nom de production.
/// </summary>
public sealed class LifecycleDatabase : IAsyncDisposable
{
    private const string Password = "mmv_it_ephemeral_role";

    private readonly string _adminServerConnectionString;

    private LifecycleDatabase(string adminServerConnectionString, string suffix)
    {
        _adminServerConnectionString = adminServerConnectionString;
        Name = "mmv_it_lc_" + suffix;
        MigratorRole = "mmv_it_mig_" + suffix;
        AppRole = "mmv_it_app_" + suffix;
        AdminConnectionString = With(adminServerConnectionString, Name, null, null);
        MigratorConnectionString = With(adminServerConnectionString, Name, MigratorRole, Password);
        AppConnectionString = With(adminServerConnectionString, Name, AppRole, Password);
    }

    public string Name { get; }
    public string MigratorRole { get; }
    public string AppRole { get; }
    public string AdminConnectionString { get; }
    public string MigratorConnectionString { get; }
    public string AppConnectionString { get; }

    /// <summary>Base vide, propriété du migrateur ; rôle applicatif avec les seuls droits par défaut de PUBLIC.</summary>
    /// <param name="adminServerConnectionString">
    /// Serveur d'accueil ; <c>null</c> ⇒ serveur de test principal. P4-8 : le serveur TLS pour tout test qui passe
    /// par le point d'entrée réel de l'outil, qui impose TLS VerifyFull (D-14).
    /// </param>
    public static async Task<LifecycleDatabase> CreateAsync(string? adminServerConnectionString = null)
    {
        var database = new LifecycleDatabase(
            adminServerConnectionString ?? PostgreSqlTestEnvironment.GetRequiredConnectionString(),
            Guid.NewGuid().ToString("N")[..12]);
        await database.ExecuteOnServerAsync(
            $"CREATE ROLE \"{database.MigratorRole}\" LOGIN PASSWORD '{Password}'",
            $"CREATE ROLE \"{database.AppRole}\" LOGIN PASSWORD '{Password}'",
            $"CREATE DATABASE \"{database.Name}\" OWNER \"{database.MigratorRole}\"");
        return database;
    }

    /// <summary>Contexte sur la chaîne <typeparamref name="TChain"/> (production + migrations de test).</summary>
    public OpticDbContext Context<TChain>(string connectionString) where TChain : ILifecycleChain, new()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        ConfigureChain<TChain>(builder, connectionString);
        return new OpticDbContext(builder.Options);
    }

    private static void ConfigureChain<TChain>(DbContextOptionsBuilder<OpticDbContext> builder, string connectionString)
        where TChain : ILifecycleChain, new()
    {
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = connectionString
        });
        builder.ReplaceService<IMigrationsAssembly, LifecycleMigrationsAssembly<TChain>>();
    }

    /// <summary>Session de l'outil réel (verrou, journal, métadonnée, GRANT, vérification) sous le rôle migrateur.</summary>
    public PostgreSqlMigrationSession Session<TChain>(TimeSpan? lockTimeout = null) where TChain : ILifecycleChain, new() =>
        PostgreSqlMigrationSession.Create(builder => ConfigureChain<TChain>(builder, MigratorConnectionString),
            lockTimeout ?? MigrationRunner.DefaultLockTimeout);

    /// <summary>
    /// Exécute l'outil réel. Seule la vérification de sauvegarde est un double (§6.2 : injecté dans le
    /// constructeur du runner, jamais via <c>Program.cs</c>) ; tout le reste est le code de production.
    /// </summary>
    public async Task<MigrationRunResult> RunAsync<TChain>(
        string version,
        MigrationRunKind kind = MigrationRunKind.Migrate,
        TimeSpan? wait = null,
        TimeSpan? lockTimeout = null,
        IMigrationJournal? trace = null,
        Func<MigrationPorts, MigrationPorts>? decorate = null) where TChain : ILifecycleChain, new()
    {
        await using var session = Session<TChain>(lockTimeout);
        var ports = decorate is null ? session.Ports : decorate(session.Ports);
        var runner = new MigrationRunner(ports, new AcceptingBackupVerification(), trace ?? new MigrationJournal());
        return await runner.RunAsync(new MigrationRunRequest(
            kind, "OP-IT", "dump-it-" + version, AppRole, wait ?? TimeSpan.Zero, version));
    }

    /// <summary>
    /// Émulation de <b>P4-8</b> (hors périmètre P4-6B) : droits de données du rôle applicatif sur les tables
    /// applicatives, lecture seule de l'historique EF. Aucun droit DDL.
    /// </summary>
    public Task GrantApplicationDataAccessAsync() => ExecuteAsync(MigratorConnectionString,
        $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{AppRole}\"",
        $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO \"{AppRole}\"",
        $"REVOKE INSERT, UPDATE, DELETE ON \"{HistoryRepository.DefaultTableName}\" FROM \"{AppRole}\"");

    /// <summary>
    /// Paramètre de base lu par les migrations de test. Il ne s'applique qu'aux <b>nouvelles</b> sessions : le
    /// pool du migrateur est vidé pour que la prochaine exécution ne réutilise pas une session antérieure.
    /// </summary>
    public async Task SetParameterAsync(string name, string value)
    {
        await ExecuteOnServerAsync($"ALTER DATABASE \"{Name}\" SET {name} = '{value}'");
        NpgsqlConnection.ClearPool(new NpgsqlConnection(MigratorConnectionString));
    }

    public static async Task ExecuteAsync(string connectionString, params string[] statements)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public Task<T?> AdminScalarAsync<T>(string sql) => ScalarAsync<T>(AdminConnectionString, sql);

    /// <summary>Ligne de compatibilité lue par l'administrateur.</summary>
    public async Task<(string? Schema, string? Minimum, DateTime? Maintenance)> CompatibilityRowAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT schema_version, minimum_supported_version, maintenance_started_at FROM mmv_meta.schema_compatibility WHERE id = 1",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Ligne de compatibilité absente.");
        }

        return (reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }

    /// <summary>Nombre de sessions tenant le verrou de l'outil.</summary>
    public Task<long> AdvisoryHoldersAsync() => AdminScalarAsync<long>(
        "SELECT count(*) FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND granted " +
        "AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) " +
        $"AND classid::bigint = {MMV.DatabaseManager.Locking.PostgreSqlAdvisoryMigrationLock.KeyHigh} " +
        $"AND objid::bigint = {MMV.DatabaseManager.Locking.PostgreSqlAdvisoryMigrationLock.KeyLow} AND objsubid = 1");

    /// <summary>Attend une condition serveur, bornée.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.Elapsed > timeout)
            {
                throw new TimeoutException($"Condition non atteinte en {timeout} : {what}.");
            }

            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Ses propres pools seulement (P4-8) : ClearAllPools fermait aussi les sessions inactives des tests exécutés
        // en parallèle — dont celle où M3 garde volontairement un verrou consultatif (échec intermittent de M3).
        foreach (var connectionString in new[] { AdminConnectionString, MigratorConnectionString, AppConnectionString })
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        }

        await ExecuteOnServerAsync(
            $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)",
            $"DROP ROLE IF EXISTS \"{AppRole}\"",
            $"DROP ROLE IF EXISTS \"{MigratorRole}\"");
    }

    private Task ExecuteOnServerAsync(params string[] statements) => ExecuteAsync(_adminServerConnectionString, statements);

    private static string With(string connectionString, string database, string? username, string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        if (username is not null)
        {
            builder.Username = username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}

/// <summary>
/// Double de test qui <b>accepte</b> la sauvegarde (§6.2 : seam de test). N'existe que dans ce projet de tests ;
/// la production ne construit que <see cref="RefusingBackupVerification"/>.
/// </summary>
public sealed class AcceptingBackupVerification : IBackupVerification
{
    public Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken) =>
        Task.FromResult(BackupVerificationResult.Verified());
}

/// <summary>Base de test de la famille Lifecycle : une base vide neuve et ses rôles PAR TEST.</summary>
public abstract class LifecycleTestBase : IAsyncLifetime
{
    protected LifecycleDatabase Db { get; private set; } = null!;

    public async Task InitializeAsync() => Db = await LifecycleDatabase.CreateAsync();

    public async Task DisposeAsync() => await Db.DisposeAsync();
}
