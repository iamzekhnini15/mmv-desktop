using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

/// <summary>
/// P4-8 — serveur TLS de test (<c>Tls/start-tls-servers.sh</c>) : PostgreSQL 17.10, <c>ssl = on</c>, SCRAM-SHA-256,
/// <c>pg_hba</c> en <c>hostssl</c>, certificat valide pour <c>localhost</c> seulement. Les noms produits ici sont
/// uniques par test et purement de test : <b>aucun nom de production</b> (les noms sont des paramètres, DP-5).
/// </summary>
public sealed class TlsServer
{
    /// <summary>Secrets de test éphémères, conformes à <see cref="RoleSecretPolicy"/>. Non secrets : serveur jetable.</summary>
    public const string MigratorSecret = "it-migrator-Secret-0123456789";
    public const string AppSecret = "it-application-Secret-0123456789";
    public const string BackupSecret = "it-backup-Secret-0123456789abc";

    public TlsServer()
    {
        Admin = new NpgsqlConnectionStringBuilder(
            PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.TlsConnectionStringVariableName));
    }

    /// <summary>Chaîne administrateur (superutilisateur du conteneur), VerifyFull + autorité de test.</summary>
    public NpgsqlConnectionStringBuilder Admin { get; }

    public string Host => Admin.Host!;
    public int Port => Admin.Port;
    public string RootCertificate => Admin.RootCertificate!;

    public static string Suffix() => Guid.NewGuid().ToString("N")[..10];

    public ProvisioningRequest Request(string suffix, Action<RequestNames>? names = null)
    {
        var n = new RequestNames($"mmv_it_db_{suffix}", $"mmv_it_mig_{suffix}", $"mmv_it_app_{suffix}", $"mmv_it_bkp_{suffix}");
        names?.Invoke(n);
        return new ProvisioningRequest
        {
            Host = Host,
            Port = Port,
            RootCertificatePath = RootCertificate,
            AdminUser = Admin.Username!,
            AdminPassword = Admin.Password!,
            AdminDatabase = "postgres",
            Database = n.Database,
            MigratorRole = n.Migrator,
            MigratorPassword = MigratorSecret,
            AppRole = n.App,
            AppPassword = AppSecret,
            BackupRole = n.Backup,
            BackupPassword = BackupSecret,
            Operator = "OP-IT"
        };
    }

    /// <summary>Chaîne conforme (VerifyFull, SCRAM) pour un rôle MMV sur la base provisionnée.</summary>
    public string ConnectionString(string database, string user, string secret) =>
        PostgreSqlConnectionSecurity.BuildConnectionString(
            new PostgreSqlConnectionSettings(Host, Port, database, user, secret, RootCertificate));

    public string AdminOn(string database) =>
        new NpgsqlConnectionStringBuilder(Admin.ConnectionString) { Database = database }.ConnectionString;

    /// <summary>Applique la chaîne de production avec l'outil réel ; seule la sauvegarde est un double (P4-9).</summary>
    public async Task<MigrationRunResult> MigrateAsync(ProvisioningRequest request)
    {
        await using var session = PostgreSqlMigrationSession.Create(
            builder => DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = ConnectionString(request.Database, request.MigratorRole, request.MigratorPassword)
            }),
            MigrationRunner.DefaultLockTimeout);
        var runner = new MigrationRunner(session.Ports, new AcceptingBackupVerification(), new MigrationJournal());
        return await runner.RunAsync(new MigrationRunRequest(
            MigrationRunKind.Migrate, "OP-IT", "dump-it", request.AppRole, TimeSpan.Zero, ApplicationVersion.Current));
    }

    public OpticDbContext Context(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = connectionString
        });
        return new OpticDbContext(builder.Options);
    }

    public Task ExecuteAsync(string database, params string[] statements) =>
        LifecycleDatabase.ExecuteAsync(AdminOn(database), statements);

    public Task<T?> ScalarAsync<T>(string database, string sql) => LifecycleDatabase.ScalarAsync<T>(AdminOn(database), sql);

    /// <summary>SQLSTATE d'une instruction exécutée sous <paramref name="connectionString"/> ; « 00000 » si elle passe.</summary>
    public static async Task<string> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await LifecycleDatabase.ExecuteAsync(connectionString, sql);
            return "00000";
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    /// <summary>Supprime la base et les rôles d'un test (idempotent).</summary>
    /// <remarks>
    /// Jamais <c>ClearAllPools</c> : il fermerait les sessions inactives d'AUTRES tests exécutés en parallèle (M3
    /// garde volontairement un verrou consultatif dans une session du pool). <c>WITH (FORCE)</c> termine les
    /// sessions de la base du test, et seulement elles.
    /// </remarks>
    public async Task DropAsync(ProvisioningRequest request, params string[] extraRoles)
    {
        var statements = new List<string> { $"DROP DATABASE IF EXISTS {Quote(request.Database)} WITH (FORCE)" };
        foreach (var role in new[] { request.AppRole, request.BackupRole, request.MigratorRole }.Concat(extraRoles))
        {
            statements.Add($"DROP ROLE IF EXISTS {Quote(role)}");
        }

        await ExecuteAsync("postgres", statements.ToArray());
    }

    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}

/// <summary>Noms d'un test, modifiables avant construction de la demande.</summary>
public sealed class RequestNames(string database, string migrator, string app, string backup)
{
    public string Database { get; set; } = database;
    public string Migrator { get; set; } = migrator;
    public string App { get; set; } = app;
    public string Backup { get; set; } = backup;
}
