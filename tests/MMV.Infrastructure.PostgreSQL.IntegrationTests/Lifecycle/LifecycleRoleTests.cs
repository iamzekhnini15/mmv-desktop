using FluentAssertions;
using MMV.DatabaseManager;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// P4-6B — rôles conformes à DP-5 sur un vrai serveur (S-3 ; H5, Q-23). I-5 (aucun DDL pour le rôle
/// applicatif, lecture de l'historique et de la métadonnée), I-6 (journal illisible), I-12 (GRANT idempotent
/// et obligatoire).
/// </summary>
public sealed class LifecycleRoleTests : LifecycleTestBase
{
    private const string InsufficientPrivilege = "42501";

    private async Task<string> AppSqlStateAsync(string sql)
    {
        try
        {
            await LifecycleDatabase.ExecuteAsync(Db.AppConnectionString, sql);
            return "00000";
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    private async Task MigratedAsync()
    {
        var result = await Db.RunAsync<ChainS1>("1.0.0");
        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        await Db.GrantApplicationDataAccessAsync();
    }

    [PostgreSqlFact]
    public async Task I5_application_role_cannot_run_DDL()
    {
        await MigratedAsync();

        foreach (var ddl in new[]
                 {
                     "CREATE TABLE public.it_app_ddl (id integer)",
                     "ALTER TABLE \"Customers\" ADD COLUMN it_app_ddl integer",
                     "DROP TABLE \"Customers\"",
                     "CREATE SCHEMA it_app_schema",
                     "CREATE TABLE mmv_meta.it_app_ddl (id integer)",
                     "ALTER TABLE mmv_meta.schema_compatibility ADD COLUMN x integer",
                     "CREATE INDEX it_app_index ON \"Customers\" (\"LastName\")",
                     "TRUNCATE \"Customers\""
                 })
        {
            (await AppSqlStateAsync(ddl)).Should().Be(InsufficientPrivilege, ddl);
        }
    }

    [PostgreSqlFact]
    public async Task I5_application_role_reads_the_EF_history_and_the_metadata_but_never_writes_them()
    {
        await MigratedAsync();

        (await LifecycleDatabase.ScalarAsync<long>(Db.AppConnectionString,
            "SELECT count(*) FROM \"__EFMigrationsHistory\"")).Should().Be(1);
        (await LifecycleDatabase.ScalarAsync<string>(Db.AppConnectionString,
            "SELECT schema_version FROM mmv_meta.schema_compatibility")).Should().Be("1.0.0");

        (await AppSqlStateAsync("UPDATE mmv_meta.schema_compatibility SET maintenance_started_at = now()"))
            .Should().Be(InsufficientPrivilege, "un poste ne pose jamais la maintenance");
        (await AppSqlStateAsync("DELETE FROM \"__EFMigrationsHistory\"")).Should().Be(InsufficientPrivilege);
    }

    [PostgreSqlFact]
    public async Task I6_application_role_cannot_read_the_migration_journal()
    {
        await MigratedAsync();

        (await AppSqlStateAsync("SELECT * FROM mmv_meta.migration_run")).Should().Be(InsufficientPrivilege);
        (await LifecycleDatabase.ScalarAsync<bool>(Db.AdminConnectionString,
            $"SELECT has_table_privilege('{Db.AppRole}', 'mmv_meta.migration_run', 'SELECT, INSERT, UPDATE, DELETE')"))
            .Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task I12_grant_is_idempotent_and_mandatory_for_success()
    {
        await MigratedAsync();
        var aclBefore = await AclAsync();

        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await AclAsync()).Should().Be(aclBefore, "rejouer migrate laisse les droits identiques");

        // Un rôle privé du GRANT entre l'étape 4 et l'étape 9 : jamais de succès.
        await Db.SetParameterAsync("mmv_it.app_role", Db.AppRole);
        var revoked = await Db.RunAsync<ChainS1ThenRevoke>("1.1.0");

        revoked.ExitCode.Should().Be(MigrationExitCode.VerificationFailed, revoked.Message);
        (await Db.AdminScalarAsync<string>(
            $"SELECT outcome FROM mmv_meta.migration_run WHERE run_id = '{revoked.RunId}'")).Should().Be("failure");
        (await Db.AdminScalarAsync<string>(
            $"SELECT failure_cause FROM mmv_meta.migration_run WHERE run_id = '{revoked.RunId}'")).Should().Contain("Q-23");
        var row = await Db.CompatibilityRowAsync();
        (row.Schema, row.Minimum).Should().Be(("1.0.0", "1.0.0"), "la métadonnée n'avance pas sans GRANT assuré");
        row.Maintenance.Should().BeNull();

        // Relance (scénario 2) : l'étape 4 rétablit le droit, l'étape 9 le constate ; l'état physique vérifié
        // diffère de la dernière ancre ⇒ nouvelle ancre, produite par la release 1.1.0 (§4.3 amendé).
        (await Db.RunAsync<ChainS1ThenRevoke>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await LifecycleDatabase.ScalarAsync<string>(Db.AppConnectionString,
            "SELECT schema_version FROM mmv_meta.schema_compatibility")).Should().Be("1.1.0");
        var relaunched = await Db.CompatibilityRowAsync();
        (relaunched.Schema, relaunched.Minimum, relaunched.Maintenance).Should().Be(("1.1.0", "1.0.0", (DateTime?)null));
    }

    [PostgreSqlFact]
    public async Task I12_unknown_app_role_is_code_10_before_any_write()
    {
        await using var session = Db.Session<ChainS1>();
        var runner = new MigrationRunner(session.Ports, new AcceptingBackupVerification(), new MMV.Infrastructure.Data.MigrationJournal());

        var result = await runner.RunAsync(new MigrationRunRequest(
            MMV.DatabaseManager.Journal.MigrationRunKind.Migrate, "OP-IT", "dump", "x\"; DROP TABLE \"Customers\"; --",
            TimeSpan.Zero, "1.0.0"));

        result.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        (await Db.AdminScalarAsync<bool>("SELECT to_regnamespace('mmv_meta') IS NULL")).Should().BeTrue();
        (await Db.AdminScalarAsync<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NULL")).Should().BeTrue();
    }

    [PostgreSqlFact]
    public async Task I12_hostile_but_existing_role_name_is_quoted_by_the_server()
    {
        const string hostile = "mmv it \"app\"; DROP TABLE x; --";
        await LifecycleDatabase.ExecuteAsync(Db.AdminConnectionString,
            $"CREATE ROLE \"{hostile.Replace("\"", "\"\"")}\" NOLOGIN");
        try
        {
            await using var session = Db.Session<ChainS1>();
            var runner = new MigrationRunner(session.Ports, new AcceptingBackupVerification(), new MMV.Infrastructure.Data.MigrationJournal());

            var result = await runner.RunAsync(new MigrationRunRequest(
                MMV.DatabaseManager.Journal.MigrationRunKind.Migrate, "OP-IT", "dump", hostile, TimeSpan.Zero, "1.0.0"));

            result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
            (await Db.AdminScalarAsync<bool>(
                $"SELECT has_table_privilege('{hostile.Replace("'", "''")}', 'mmv_meta.schema_compatibility', 'SELECT')"))
                .Should().BeTrue("le GRANT vise exactement ce rôle, cité par le serveur");
        }
        finally
        {
            await LifecycleDatabase.ExecuteAsync(Db.AdminConnectionString,
                $"DROP OWNED BY \"{hostile.Replace("\"", "\"\"")}\"",
                $"DROP ROLE \"{hostile.Replace("\"", "\"\"")}\"");
        }
    }

    private Task<string?> AclAsync() => Db.AdminScalarAsync<string>(
        "SELECT (SELECT nspacl::text FROM pg_namespace WHERE nspname = 'mmv_meta') || ' | ' || " +
        "(SELECT relacl::text FROM pg_class WHERE oid = 'mmv_meta.schema_compatibility'::regclass) || ' | ' || " +
        "coalesce((SELECT relacl::text FROM pg_class WHERE oid = 'mmv_meta.migration_run'::regclass), 'NULL')");
}
