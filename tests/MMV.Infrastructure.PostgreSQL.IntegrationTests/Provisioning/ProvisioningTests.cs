using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

/// <summary>
/// P4-8 (ADR-PROD-DB-010 ; D-09, DP-5, D-14) — provisioning réel sur le serveur TLS : création de la base et des
/// trois rôles, permissions, idempotence, refus d'un serveur ou d'un état non conformes avant toute écriture.
/// </summary>
public sealed class ProvisioningTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";

    private readonly TlsServer _server = new();
    private readonly List<(ProvisioningRequest Request, string[] Extra)> _cleanup = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var (request, extra) in _cleanup)
        {
            await _server.DropAsync(request, extra);
        }
    }

    private ProvisioningRequest Track(ProvisioningRequest request, params string[] extraRoles)
    {
        _cleanup.Add((request, extraRoles));
        return request;
    }

    private static Task<AdministrationResult> ProvisionAsync(ProvisioningRequest request) =>
        new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(request);

    private async Task<ProvisioningRequest> ProvisionedAndMigratedAsync()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        var migrated = await _server.MigrateAsync(request);
        migrated.ExitCode.Should().Be(MigrationExitCode.Success, migrated.Message);
        return request;
    }

    [PostgreSqlTlsFact]
    public async Task Provision_creates_the_database_owned_by_the_migrator_and_three_unprivileged_scram_roles()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        (await _server.ScalarAsync<string>("postgres",
            $"SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}"))
            .Should().Be(request.MigratorRole);
        (await _server.ScalarAsync<string>("postgres",
            $"SELECT pg_encoding_to_char(encoding) FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}"))
            .Should().Be("UTF8");

        foreach (var role in new[] { request.MigratorRole, request.AppRole, request.BackupRole })
        {
            (await _server.ScalarAsync<bool>("postgres",
                "SELECT rolcanlogin AND NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit) " +
                $"FROM pg_roles WHERE rolname = {TlsServer.Literal(role)}")).Should().BeTrue(role);
            (await _server.ScalarAsync<string>("postgres",
                $"SELECT rolpassword FROM pg_authid WHERE rolname = {TlsServer.Literal(role)}"))
                .Should().StartWith("SCRAM-SHA-256$4096:", "vérificateur SCRAM calculé par l'outil — jamais MD5 (D-14)");
            (await _server.ScalarAsync<long>("postgres",
                $"SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member WHERE r.rolname = {TlsServer.Literal(role)}"))
                .Should().Be(0, "aucune élévation implicite par appartenance");
        }
    }

    [PostgreSqlTlsFact]
    public async Task Database_is_closed_to_PUBLIC_and_opened_to_the_application_and_backup_roles_only()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        var db = TlsServer.Literal(request.Database);

        (await _server.ScalarAsync<bool>("postgres", $"SELECT has_database_privilege('public', {db}, 'CONNECT')")).Should().BeFalse();
        (await _server.ScalarAsync<bool>("postgres", $"SELECT has_database_privilege('public', {db}, 'TEMPORARY')")).Should().BeFalse();
        foreach (var role in new[] { request.AppRole, request.BackupRole })
        {
            (await _server.ScalarAsync<bool>("postgres",
                $"SELECT has_database_privilege({TlsServer.Literal(role)}, {db}, 'CONNECT')")).Should().BeTrue();
            (await _server.ScalarAsync<bool>("postgres",
                $"SELECT has_database_privilege({TlsServer.Literal(role)}, {db}, 'CREATE')")).Should().BeFalse(role);
        }

        (await _server.ScalarAsync<bool>(request.Database,
            $"SELECT has_schema_privilege({TlsServer.Literal(request.AppRole)}, 'public', 'CREATE')")).Should().BeFalse();
        (await _server.ScalarAsync<string>("postgres",
            "SELECT array_to_string(s.setconfig, ',') FROM pg_db_role_setting s " +
            $"WHERE s.setrole = (SELECT oid FROM pg_roles WHERE rolname = {TlsServer.Literal(request.AppRole)}) " +
            $"AND s.setdatabase = (SELECT oid FROM pg_database WHERE datname = {db})")).Should().Be("search_path=public");
    }

    [PostgreSqlTlsFact]
    public async Task EF_history_is_created_empty_by_the_migrator_and_read_only_for_the_application()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        var history = TlsServer.Literal("public.\"" + HistoryRepository.DefaultTableName + "\"");

        (await _server.ScalarAsync<string>(request.Database,
            $"SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = {history}::regclass")).Should().Be(request.MigratorRole);
        var app = _server.ConnectionString(request.Database, request.AppRole, request.AppPassword);
        (await LifecycleDatabase.ScalarAsync<long>(app, $"SELECT count(*) FROM \"{HistoryRepository.DefaultTableName}\"")).Should().Be(0);
        (await TlsServer.SqlStateAsync(app,
            $"INSERT INTO \"{HistoryRepository.DefaultTableName}\" VALUES ('x', 'y')")).Should().Be(InsufficientPrivilege);
    }

    [PostgreSqlTlsFact]
    public async Task After_migrate_the_application_role_has_DML_only_and_never_DDL()
    {
        var request = await ProvisionedAndMigratedAsync();
        var app = _server.ConnectionString(request.Database, request.AppRole, request.AppPassword);

        // DML réel par EF, y compris les colonnes d'identité, sous le rôle applicatif.
        await using (var context = _server.Context(app))
        {
            var supplier = await Arrange.SupplierAsync(context);
            var product = await Arrange.ProductAsync(context, supplier);
            product.Should().BePositive();
        }

        foreach (var ddl in new[]
                 {
                     "CREATE TABLE public.it_app_ddl (id integer)",
                     "ALTER TABLE \"Customers\" ADD COLUMN it_app_ddl integer",
                     "DROP TABLE \"Customers\"",
                     "CREATE SCHEMA it_app_schema",
                     "CREATE INDEX it_app_index ON \"Customers\" (\"LastName\")",
                     "TRUNCATE \"Customers\"",
                     $"DELETE FROM \"{HistoryRepository.DefaultTableName}\"",
                     "UPDATE mmv_meta.schema_compatibility SET maintenance_started_at = now()",
                     "SELECT * FROM mmv_meta.migration_run"
                 })
        {
            (await TlsServer.SqlStateAsync(app, ddl)).Should().Be(InsufficientPrivilege, ddl);
        }

        (await LifecycleDatabase.ScalarAsync<long>(app, $"SELECT count(*) FROM \"{HistoryRepository.DefaultTableName}\"")).Should().Be(1);
        (await LifecycleDatabase.ScalarAsync<string>(app, "SELECT schema_version FROM mmv_meta.schema_compatibility"))
            .Should().NotBeNullOrEmpty();
    }

    [PostgreSqlTlsFact]
    public async Task Backup_role_reads_every_table_and_writes_none()
    {
        var request = await ProvisionedAndMigratedAsync();
        var backup = TlsServer.Literal(request.BackupRole);

        var tables = await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM pg_tables WHERE schemaname IN ('public', 'mmv_meta')");
        tables.Should().BeGreaterThan(20);
        (await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM pg_tables WHERE schemaname IN ('public', 'mmv_meta') " +
            $"AND has_table_privilege({backup}, format('%I.%I', schemaname, tablename), 'SELECT')")).Should().Be(tables);
        (await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM pg_tables WHERE schemaname IN ('public', 'mmv_meta') " +
            $"AND has_table_privilege({backup}, format('%I.%I', schemaname, tablename), 'INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')"))
            .Should().Be(0);
        // OFFSET 0 : barrière d'optimisation — has_sequence_privilege ne doit voir que des séquences.
        (await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE c.relkind = 'S' AND n.nspname IN ('public', 'mmv_meta') OFFSET 0) s " +
            $"WHERE NOT has_sequence_privilege({backup}, s.oid, 'SELECT')"))
            .Should().Be(0);

        var connection = _server.ConnectionString(request.Database, request.BackupRole, request.BackupPassword);
        (await TlsServer.SqlStateAsync(connection, "CREATE TABLE public.it_backup_ddl (id integer)")).Should().Be(InsufficientPrivilege);
        (await TlsServer.SqlStateAsync(connection, "DELETE FROM \"Customers\"")).Should().Be(InsufficientPrivilege);
    }

    [PostgreSqlTlsFact]
    public async Task Migrator_owns_every_schema_object_and_the_application_never_owns_any()
    {
        var request = await ProvisionedAndMigratedAsync();

        (await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM pg_tables WHERE schemaname IN ('public', 'mmv_meta') " +
            $"AND tableowner <> {TlsServer.Literal(request.MigratorRole)}")).Should().Be(0);
        (await _server.ScalarAsync<long>(request.Database,
            "SELECT count(*) FROM pg_class c JOIN pg_roles r ON r.oid = c.relowner " +
            $"WHERE r.rolname IN ({TlsServer.Literal(request.AppRole)}, {TlsServer.Literal(request.BackupRole)})")).Should().Be(0);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_is_idempotent_and_converges_after_migrations()
    {
        var request = await ProvisionedAndMigratedAsync();
        var before = await AclSnapshotAsync(request);

        var again = await ProvisionAsync(request);

        again.ExitCode.Should().Be(MigrationExitCode.Success, again.Message);
        (await AclSnapshotAsync(request)).Should().Be(before, "une relance ne change aucun droit");
        (await _server.ScalarAsync<long>(request.Database, "SELECT count(*) FROM \"__EFMigrationsHistory\"")).Should().Be(1);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_role_allowed_md5_by_pg_hba_before_any_write()
    {
        // pg_hba du serveur de test : « hostssl all mmv_tls_insecure_md5 all md5 ».
        var request = Track(_server.Request(TlsServer.Suffix(), n => n.Migrator = "mmv_tls_insecure_md5"));

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("md5");
        await NothingWrittenAsync(request);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_role_reachable_without_TLS_before_any_write()
    {
        // pg_hba du serveur de test : « host all mmv_tls_insecure_nossl all scram-sha-256 ».
        var request = Track(_server.Request(TlsServer.Suffix(), n => n.App = "mmv_tls_insecure_nossl"));

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("sans TLS");
        await NothingWrittenAsync(request);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_an_existing_privileged_or_member_role_before_any_write()
    {
        var suffix = TlsServer.Suffix();
        var privileged = Track(_server.Request(suffix));
        await _server.ExecuteAsync("postgres", $"CREATE ROLE {TlsServer.Quote(privileged.AppRole)} LOGIN CREATEDB");

        var result = await ProvisionAsync(privileged);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain(privileged.AppRole);
        (await _server.ScalarAsync<bool>("postgres",
            $"SELECT rolcreatedb FROM pg_roles WHERE rolname = {TlsServer.Literal(privileged.AppRole)}")).Should().BeTrue("aucune écriture : rien n'est « corrigé » en silence");
        await NothingWrittenAsync(privileged, exceptApp: true);

        var member = Track(_server.Request(TlsServer.Suffix()), "mmv_it_group_" + suffix);
        await _server.ExecuteAsync("postgres",
            $"CREATE ROLE {TlsServer.Quote("mmv_it_group_" + suffix)} NOLOGIN",
            $"CREATE ROLE {TlsServer.Quote(member.BackupRole)} LOGIN",
            $"GRANT {TlsServer.Quote("mmv_it_group_" + suffix)} TO {TlsServer.Quote(member.BackupRole)}");

        (await ProvisionAsync(member)).ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_database_owned_by_another_role()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        await _server.ExecuteAsync("postgres", $"CREATE DATABASE {TlsServer.Quote(request.Database)}");

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_roles WHERE rolname = {TlsServer.Literal(request.MigratorRole)}")).Should().Be(0);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_schema_named_after_a_MMV_role()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        await _server.ExecuteAsync(request.Database, $"CREATE SCHEMA {TlsServer.Quote(request.AppRole)}");
        var before = await ServerStateAsync(request);

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("D-08").And.Contain("aucune écriture");
        (await ServerStateAsync(request)).Should().Be(before,
            "M3 : refusé au préflight — aucun secret reposé, aucun droit rejoué (vérificateurs SCRAM inclus)");
    }

    /// <summary>
    /// M1 : un rôle <b>membre</b> d'un rôle MMV ou de l'administrateur hériterait de ses droits (INHERIT), pourrait
    /// l'endosser (SET) ou en transmettre l'appartenance (ADMIN). Refus au préflight, quelle que soit l'option ;
    /// l'appartenance n'est jamais retirée par l'outil, et rien d'autre n'est écrit (M3).
    /// </summary>
    [PostgreSqlTlsTheory]
    [InlineData("migrator", "INHERIT TRUE, SET FALSE, ADMIN FALSE", "INHERIT")]
    [InlineData("migrator", "INHERIT FALSE, SET TRUE, ADMIN FALSE", "SET")]
    [InlineData("migrator", "INHERIT FALSE, SET FALSE, ADMIN TRUE", "ADMIN")]
    [InlineData("migrator", "INHERIT FALSE, SET FALSE, ADMIN FALSE", "aucune option")]
    [InlineData("app", "INHERIT TRUE, SET TRUE, ADMIN FALSE", "INHERIT, SET")]
    [InlineData("backup", "INHERIT TRUE, SET TRUE, ADMIN TRUE", "INHERIT, SET, ADMIN")]
    [InlineData("admin", "INHERIT TRUE, SET TRUE, ADMIN FALSE", "INHERIT, SET")]
    public async Task Provision_refuses_any_member_of_a_MMV_role_or_of_the_administrator_and_writes_nothing(
        string target, string options, string reported)
    {
        var suffix = TlsServer.Suffix();
        var administrator = "mmv_it_su_" + suffix;
        var evil = "mmv_it_evil_" + suffix;
        await _server.ExecuteAsync("postgres",
            $"CREATE ROLE {TlsServer.Quote(administrator)} LOGIN SUPERUSER PASSWORD {TlsServer.Literal(TlsServer.AdministratorSecret)}",
            $"CREATE ROLE {TlsServer.Quote(evil)} NOLOGIN");
        var request = Track(_server.Request(suffix, administrator: administrator), evil, administrator);
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        var granted = target switch
        {
            "migrator" => request.MigratorRole,
            "app" => request.AppRole,
            "backup" => request.BackupRole,
            _ => administrator
        };
        await _server.ExecuteAsync("postgres", $"GRANT {TlsServer.Quote(granted)} TO {TlsServer.Quote(evil)} WITH {options}");
        var before = await ServerStateAsync(request, evil);

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("aucune écriture").And.Contain($"'{evil}' est membre de '{granted}' ({reported})");
        (await ServerStateAsync(request, evil)).Should().Be(before, "refus au préflight : l'état du serveur est inchangé");
        (await _server.ScalarAsync<long>("postgres",
            "SELECT count(*) FROM pg_auth_members m JOIN pg_roles g ON g.oid = m.roleid JOIN pg_roles u ON u.oid = m.member " +
            $"WHERE g.rolname = {TlsServer.Literal(granted)} AND u.rolname = {TlsServer.Literal(evil)}"))
            .Should().Be(1, "l'outil ne retire aucune appartenance sans décision explicite");
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_pre_existing_role_that_has_members_before_any_write()
    {
        var suffix = TlsServer.Suffix();
        var evil = "mmv_it_evil_" + suffix;
        var request = Track(_server.Request(suffix), evil);
        await _server.ExecuteAsync("postgres",
            $"CREATE ROLE {TlsServer.Quote(request.MigratorRole)} LOGIN",
            $"CREATE ROLE {TlsServer.Quote(evil)} NOLOGIN",
            $"GRANT {TlsServer.Quote(request.MigratorRole)} TO {TlsServer.Quote(evil)}");
        var before = await ServerStateAsync(request, evil);

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain($"'{evil}' est membre de '{request.MigratorRole}'");
        (await ServerStateAsync(request, evil)).Should().Be(before);
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}")).Should().Be(0);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_role_that_pg_hba_does_not_admit_before_any_write()
    {
        // pg_hba du serveur de test : « hostssl all mmv_tls_rejected all reject ». L'admission est prouvée au préflight
        // par un secret délibérément faux (28P01 = admis), jamais découverte après les écritures (M3).
        var request = Track(_server.Request(TlsServer.Suffix(), n => n.Backup = "mmv_tls_rejected"));

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("pg_hba n'admet pas le rôle 'mmv_tls_rejected'").And.Contain("aucune écriture");
        await NothingWrittenAsync(request);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_an_expired_role_or_a_closed_database_before_any_write()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);

        await _server.ExecuteAsync("postgres", $"ALTER ROLE {TlsServer.Quote(request.AppRole)} VALID UNTIL '2000-01-01'");
        var expired = await ServerStateAsync(request);
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        (await ServerStateAsync(request)).Should().Be(expired);

        await _server.ExecuteAsync("postgres",
            $"ALTER ROLE {TlsServer.Quote(request.AppRole)} VALID UNTIL 'infinity'",
            $"ALTER DATABASE {TlsServer.Quote(request.Database)} WITH ALLOW_CONNECTIONS false");
        var closed = await ServerStateAsync(request);
        var result = await ProvisionAsync(request);
        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("refuse les connexions");
        (await ServerStateAsync(request)).Should().Be(closed);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_refuses_a_non_superuser_provisioning_identity()
    {
        var suffix = TlsServer.Suffix();
        var weakAdmin = "mmv_it_weak_admin_" + suffix;
        await _server.ExecuteAsync("postgres",
            $"CREATE ROLE {TlsServer.Quote(weakAdmin)} LOGIN CREATEDB CREATEROLE PASSWORD {TlsServer.Literal(TlsServer.AppSecret)}");
        var request = _server.Request(suffix);
        request = Track(new ProvisioningRequest
        {
            Host = request.Host, Port = request.Port, RootCertificatePath = request.RootCertificatePath,
            AdminUser = weakAdmin, AdminPassword = TlsServer.AppSecret, AdminDatabase = "postgres",
            Database = request.Database, MigratorRole = request.MigratorRole, MigratorPassword = request.MigratorPassword,
            AppRole = request.AppRole, AppPassword = request.AppPassword, BackupRole = request.BackupRole,
            BackupPassword = request.BackupPassword, Operator = "OP-IT"
        }, weakAdmin);

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        result.Message.Should().Contain("superutilisateur");
        await NothingWrittenAsync(request);
    }

    [PostgreSqlTlsFact]
    public async Task Hostile_but_valid_names_are_quoted_by_the_server_and_created_verbatim()
    {
        var suffix = TlsServer.Suffix();
        var request = Track(_server.Request(suffix, n =>
        {
            n.App = $"mmv it \"app\"; DROP ROLE x; -- {suffix}";
            n.Database = $"mmv it db'; DROP DATABASE postgres; -- {suffix}";
        }));

        var result = await ProvisionAsync(request);

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_roles WHERE rolname = {TlsServer.Literal(request.AppRole)}")).Should().Be(1);
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}")).Should().Be(1);
        (await _server.ScalarAsync<long>("postgres", "SELECT count(*) FROM pg_database WHERE datname = 'postgres'")).Should().Be(1);
    }

    [PostgreSqlTlsFact]
    public async Task Provision_through_the_real_entry_point_reads_secrets_from_stdin_and_never_echoes_them()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-it-provision-{Guid.NewGuid():N}.log");
        var stdin = new StringReader(string.Join('\n',
            request.AdminPassword, request.MigratorPassword, request.AppPassword, request.BackupPassword));

        int code;
        string traced;
        try
        {
            code = await Program.RunAsync(
                ["provision", "--host", request.Host, "--port", request.Port.ToString(), "--root-certificate", request.RootCertificatePath!,
                 "--admin-user", request.AdminUser, "--database", request.Database, "--migrator-role", request.MigratorRole,
                 "--app-role", request.AppRole, "--backup-role", request.BackupRole, "--operator", "OP-IT"],
                new Dictionary<string, string?>(), output, error, trace, stdin);
            traced = await File.ReadAllTextAsync(trace);
        }
        finally
        {
            File.Delete(trace);
        }

        code.Should().Be(0, error.ToString());
        foreach (var secret in new[] { request.AdminPassword, request.MigratorPassword, request.AppPassword, request.BackupPassword })
        {
            (output + error.ToString() + traced).Should().NotContain(secret);
        }

        traced.Should().Contain("provision : succès");
    }

    [PostgreSqlTlsFact]
    public async Task Rotation_replaces_the_secret_old_one_is_refused_new_one_works()
    {
        var request = Track(_server.Request(TlsServer.Suffix()));
        (await ProvisionAsync(request)).ExitCode.Should().Be(MigrationExitCode.Success);
        const string rotated = "it-rotated-application-Secret-42";

        var result = await new RolePasswordRotation(new MigrationJournal()).RunAsync(new RoleRotationRequest
        {
            Host = _server.Host, Port = _server.Port, RootCertificatePath = _server.RootCertificate,
            AdminUser = request.AdminUser, AdminPassword = request.AdminPassword, AdminDatabase = "postgres",
            Database = request.Database, Role = request.AppRole, NewPassword = rotated, Operator = "OP-IT"
        });

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        // Une session déjà ouverte n'est jamais réauthentifiée par PostgreSQL : le pool la resservirait avec l'ancien
        // secret. La preuve porte donc sur une NOUVELLE authentification (procédure : redémarrer les postes).
        var previous = _server.ConnectionString(request.Database, request.AppRole, request.AppPassword);
        Npgsql.NpgsqlConnection.ClearPool(new Npgsql.NpgsqlConnection(previous));
        var old = await Record.ExceptionAsync(() => LifecycleDatabase.ScalarAsync<int>(previous, "SELECT 1"));
        PostgreSqlConnectivityProbeTests.ReasonOf(old).Should().Be(MMV.Infrastructure.Configuration.DatabaseUnavailableReason.AuthenticationFailed);
        (await LifecycleDatabase.ScalarAsync<int>(_server.ConnectionString(request.Database, request.AppRole, rotated), "SELECT 1"))
            .Should().Be(1);
        (await _server.ScalarAsync<string>("postgres",
            $"SELECT rolpassword FROM pg_authid WHERE rolname = {TlsServer.Literal(request.AppRole)}")).Should().StartWith("SCRAM-SHA-256$");
    }

    [PostgreSqlTlsFact]
    public async Task Rotation_refuses_privileged_unknown_and_administrator_roles()
    {
        RoleRotationRequest For(string role) => new()
        {
            Host = _server.Host, Port = _server.Port, RootCertificatePath = _server.RootCertificate,
            AdminUser = _server.Admin.Username!, AdminPassword = _server.Admin.Password!, AdminDatabase = "postgres",
            Database = "postgres", Role = role, NewPassword = "it-rotated-application-Secret-42", Operator = "OP-IT"
        };
        var rotation = new RolePasswordRotation(new MigrationJournal());
        var privileged = "mmv_it_priv_" + TlsServer.Suffix();
        await _server.ExecuteAsync("postgres", $"CREATE ROLE {TlsServer.Quote(privileged)} LOGIN CREATEROLE");
        try
        {
            (await rotation.RunAsync(For(_server.Admin.Username!))).ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
            (await rotation.RunAsync(For("mmv_it_ghost_" + TlsServer.Suffix()))).ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
            (await rotation.RunAsync(For(privileged))).ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
            (await rotation.RunAsync(For("pg_monitor"))).ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        }
        finally
        {
            await _server.ExecuteAsync("postgres", $"DROP ROLE IF EXISTS {TlsServer.Quote(privileged)}");
        }
    }

    private async Task NothingWrittenAsync(ProvisioningRequest request, bool exceptApp = false)
    {
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}")).Should().Be(0);
        var roles = new[] { request.MigratorRole, request.BackupRole }.Concat(exceptApp ? [] : new[] { request.AppRole })
            .Select(TlsServer.Literal);
        (await _server.ScalarAsync<long>("postgres",
            $"SELECT count(*) FROM pg_roles WHERE rolname IN ({string.Join(", ", roles)})")).Should().Be(0);
    }

    /// <summary>
    /// État serveur exact d'une demande (M3) : attributs, vérificateurs SCRAM, appartenances et réglages des rôles
    /// (MMV, administrateur, <paramref name="extraRoles"/>) ; base (propriétaire, ACL, ouverture) ; dans la base,
    /// ACL, privilèges par défaut, propriétaires et historique EF. Un secret reposé change le vérificateur (sel neuf).
    /// </summary>
    private async Task<string> ServerStateAsync(ProvisioningRequest request, params string[] extraRoles)
    {
        var names = string.Join(", ", new[] { request.MigratorRole, request.AppRole, request.BackupRole, request.AdminUser }
            .Concat(extraRoles).Select(TlsServer.Literal));
        var db = TlsServer.Literal(request.Database);
        var server = await _server.ScalarAsync<string>("postgres",
            "SELECT concat_ws(E'\\n', " +
            "(SELECT string_agg(concat_ws(' ', a.rolname, a.rolsuper, a.rolinherit, a.rolcreaterole, a.rolcreatedb, a.rolcanlogin, " +
            "  a.rolreplication, a.rolbypassrls, a.rolconnlimit, a.rolvaliduntil, a.rolpassword), E'\\n' ORDER BY a.rolname) " +
            $" FROM pg_authid a WHERE a.rolname IN ({names})), " +
            "(SELECT string_agg(concat_ws(' ', g.rolname, u.rolname, m.inherit_option, m.set_option, m.admin_option), E'\\n' " +
            "  ORDER BY g.rolname, u.rolname) FROM pg_auth_members m JOIN pg_roles g ON g.oid = m.roleid " +
            $" JOIN pg_roles u ON u.oid = m.member WHERE g.rolname IN ({names}) OR u.rolname IN ({names})), " +
            "(SELECT concat_ws(' ', datname, pg_get_userbyid(datdba), datallowconn, datconnlimit, datacl) FROM pg_database " +
            $" WHERE datname = {db}), " +
            "(SELECT string_agg(concat_ws(' ', r.rolname, d.datname, array_to_string(s.setconfig, ',')), E'\\n' ORDER BY r.rolname, d.datname) " +
            "  FROM pg_db_role_setting s JOIN pg_roles r ON r.oid = s.setrole LEFT JOIN pg_database d ON d.oid = s.setdatabase " +
            $" WHERE r.rolname IN ({names})))");

        if (!await _server.ScalarAsync<bool>("postgres", $"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = {db} AND datallowconn)"))
        {
            return server!;
        }

        var owners = await _server.ScalarAsync<string>(request.Database,
            "SELECT string_agg(n.nspname || '.' || c.relname || '=' || pg_get_userbyid(c.relowner), ',' ORDER BY n.nspname, c.relname) " +
            "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')");
        var history = await _server.ScalarAsync<long>(request.Database,
            $"SELECT count(*) FROM pg_class WHERE relname = {TlsServer.Literal(HistoryRepository.DefaultTableName)}");
        return string.Join("\n", server, await AclSnapshotAsync(request), owners, history);
    }

    private Task<string?> AclSnapshotAsync(ProvisioningRequest request) => _server.ScalarAsync<string>(request.Database,
        "SELECT concat_ws(' | ', " +
        $"(SELECT datacl::text FROM pg_database WHERE datname = {TlsServer.Literal(request.Database)}), " +
        "(SELECT nspacl::text FROM pg_namespace WHERE nspname = 'public'), " +
        "(SELECT nspacl::text FROM pg_namespace WHERE nspname = 'mmv_meta'), " +
        "(SELECT string_agg(defaclacl::text, ',' ORDER BY defaclobjtype, defaclnamespace) FROM pg_default_acl), " +
        "(SELECT string_agg(c.relname || '=' || coalesce(c.relacl::text, ''), ',' ORDER BY c.relname) FROM pg_class c " +
        " JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('public', 'mmv_meta') AND c.relkind IN ('r', 'S')))");
}
