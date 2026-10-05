using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Backup;

/// <summary>
/// P4-9 — sauvegarde et restauration centrales contre un <b>vrai</b> PostgreSQL 17 (serveur TLS de test,
/// <c>pg_dump</c> / <c>pg_restore</c> réels), sur une base provisionnée par l'outil réel (quatre identités :
/// administrateur, migrateur, applicatif, sauvegarde). Chaque exécution de l'outil passe par
/// <see cref="Program.RunAsync"/> et vérifie qu'aucun secret n'apparaît dans sa sortie ni dans sa trace.
/// Toutes les méthodes de cette classe s'exécutent en série (verrou de vérification partagé par le serveur).
/// </summary>
public sealed class BackupRestoreTests : IAsyncLifetime
{
    private readonly TlsServer _server = new();
    private readonly ProvisioningRequest _request;
    private readonly List<ProvisioningRequest> _extraDatabases = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-it-backup-").FullName;

    public BackupRestoreTests() => _request = _server.Request(TlsServer.Suffix());

    private static string PgBin => PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.PgBinVariableName);

    private string[] Secrets => [TlsServer.MigratorSecret, TlsServer.AppSecret, TlsServer.BackupSecret, _server.Admin.Password!];

    public async Task InitializeAsync() =>
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);

    public async Task DisposeAsync()
    {
        foreach (var request in _extraDatabases.Append(_request))
        {
            await _server.DropAsync(request);
        }

        Directory.Delete(_directory, recursive: true);
    }

    // ---- outil réel ------------------------------------------------------------------------------------------

    private async Task<(int Code, string Text)> ToolAsync(string stdin, IReadOnlyDictionary<string, string?>? environment, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(_directory, $"trace-{Guid.NewGuid():N}.log");
        var code = await Program.RunAsync(args, environment ?? new Dictionary<string, string?>(), output, error, trace, new StringReader(stdin));
        var text = output + "\n" + error + "\n" + (File.Exists(trace) ? await File.ReadAllTextAsync(trace) : string.Empty);
        File.Delete(trace);
        foreach (var secret in Secrets)
        {
            text.Should().NotContain(secret, "aucun secret dans la sortie ni dans la trace de l'outil");
        }

        return (code, text);
    }

    private Task<(int Code, string Text)> BackupAsync(ProvisioningRequest? request = null, string? role = null, string? secret = null)
    {
        var target = request ?? _request;
        return ToolAsync(secret ?? TlsServer.BackupSecret, null,
            "backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--database", target.Database, "--username", role ?? target.BackupRole, "--output-directory", _directory,
            "--operator", "OP-IT", "--pg-bin", PgBin);
    }

    private Task<(int Code, string Text)> VerifyAsync(string manifest, string? user = null, string? secret = null, string? scratch = null)
    {
        string[] args =
        [
            "verify-backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--admin-user", user ?? _server.Admin.Username!, "--admin-database", "postgres", "--manifest", manifest,
            "--operator", "OP-IT", "--pg-bin", PgBin
        ];
        return ToolAsync(secret ?? _server.Admin.Password!, null,
            scratch is null ? args : [.. args, "--scratch-database", scratch]);
    }

    private Task<(int Code, string Text)> MigrateAsync(string manifest, ProvisioningRequest? request = null)
    {
        var target = request ?? _request;
        return ToolAsync(string.Empty,
            new Dictionary<string, string?>
            {
                [MMV.DatabaseManager.CommandLine.MigrationToolOptions.ConnectionStringVariableName] =
                    _server.ConnectionString(target.Database, target.MigratorRole, target.MigratorPassword)
            },
            "migrate", "--operator", "OP-IT", "--backup-ref", manifest, "--app-role", target.AppRole);
    }

    private static string ManifestOf(string text)
    {
        var match = Regex.Match(text, @"Manifeste : (.+?\.manifest\.json)");
        match.Success.Should().BeTrue(text);
        return match.Groups[1].Value;
    }

    private async Task<string> BackedUpAsync()
    {
        var (code, text) = await BackupAsync();
        code.Should().Be(0, text);
        return ManifestOf(text);
    }

    private async Task<string> VerifiedBackupAsync()
    {
        var manifest = await BackedUpAsync();
        var (code, text) = await VerifyAsync(manifest);
        code.Should().Be(0, text);
        return manifest;
    }

    // ---- données et serveur ----------------------------------------------------------------------------------

    private async Task MigratedAsync(ProvisioningRequest? request = null) =>
        (await _server.MigrateAsync(request ?? _request)).ExitCode.Should().Be(MigrationExitCode.Success);

    private async Task SeedSuppliersAsync(int count)
    {
        await using var context = _server.Context(_server.ConnectionString(_request.Database, _request.AppRole, TlsServer.AppSecret));
        for (var i = 0; i < count; i++)
        {
            await Arrange.SupplierAsync(context, $"Fournisseur {Guid.NewGuid():N}");
        }
    }

    private Task<long> SuppliersAsync() => _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM \"Suppliers\"");

    private Task<long> JournalRowsAsync() => _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM mmv_meta.migration_run");

    private Task<bool> DatabaseExistsAsync(string name) =>
        _server.ScalarAsync<bool>("postgres", $"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = {TlsServer.Literal(name)})");

    private static async Task<BackupManifest> ManifestAsync(string path) =>
        (await BackupFiles.ReadManifestAsync(path, CancellationToken.None)).Manifest;

    private static string ScratchOf(BackupManifest manifest) => $"mmv_restore_check_{manifest.BackupId:N}";

    /// <summary>Remplace le fichier puis réécrit un manifeste COHÉRENT avec lui : seule la restauration le juge.</summary>
    private static async Task ReplaceDumpConsistentlyAsync(string manifestPath, Func<byte[], byte[]> change)
    {
        var manifest = await ManifestAsync(manifestPath);
        var dumpPath = BackupFiles.DumpPathFor(manifestPath, manifest);
        await File.WriteAllBytesAsync(dumpPath, change(await File.ReadAllBytesAsync(dumpPath)));
        var (sha256, size) = await BackupFiles.HashFileAsync(dumpPath, CancellationToken.None);
        await BackupFiles.WriteManifestAsync(manifestPath, manifest with { DumpSha256 = sha256, DumpSizeBytes = size }, CancellationToken.None);
    }

    private PostgreSqlConnectionSettings AdminSettings() =>
        new(_server.Host, _server.Port, "postgres", _server.Admin.Username!, _server.Admin.Password!, _server.RootCertificate);

    // ---- bout en bout ----------------------------------------------------------------------------------------

    [PostgreSqlTlsFact]
    public async Task First_installation_needs_a_backup_verified_by_real_restore_before_migrate()
    {
        var manifest = await BackedUpAsync();
        Directory.GetFiles(_directory, "*" + BackupFiles.PartialSuffix).Should().BeEmpty();

        var refused = await MigrateAsync(manifest);
        refused.Code.Should().Be(11, refused.Text);
        refused.Text.Should().Contain("aucune preuve de restauration");
        (await _server.ScalarAsync<bool>(_request.Database, "SELECT to_regnamespace('mmv_meta') IS NULL"))
            .Should().BeTrue("un refus de sauvegarde n'écrit rien");

        var verified = await VerifyAsync(manifest);
        verified.Code.Should().Be(0, verified.Text);
        File.Exists(BackupFiles.ProofPathFor(manifest)).Should().BeTrue();
        (await DatabaseExistsAsync(ScratchOf(await ManifestAsync(manifest)))).Should().BeFalse("la base de vérification est toujours supprimée");

        var migrated = await MigrateAsync(manifest);
        migrated.Code.Should().Be(0, migrated.Text);
        (await _server.ScalarAsync<string>(_request.Database, "SELECT backup_reference FROM mmv_meta.migration_run"))
            .Should().StartWith((await ManifestAsync(manifest)).BackupId.ToString("D"), "le journal porte l'identité de la sauvegarde vérifiée (DP-8)");
    }

    [PostgreSqlTlsFact]
    public async Task Manifest_records_the_snapshot_identity_history_and_exact_row_counts()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(3);

        var manifest = await ManifestAsync(await BackedUpAsync());

        manifest.State.Database.Should().Be(_request.Database);
        manifest.State.SystemIdentifier.Should().Be(
            (await _server.ScalarAsync<long>("postgres", "SELECT system_identifier FROM pg_control_system()")).ToString());
        manifest.State.DatabaseOid.Should().Be(
            await _server.ScalarAsync<long>("postgres", $"SELECT oid::bigint FROM pg_database WHERE datname = {TlsServer.Literal(_request.Database)}"));
        manifest.Role.Should().Be(_request.BackupRole, "la sauvegarde planifiée tourne sous le rôle de sauvegarde, pas l'administrateur");
        manifest.State.AppliedMigrations.Should().NotBeEmpty();
        manifest.State.Tables.Should().ContainSingle(t => t.Schema == "public" && t.Table == "Suppliers").Which.Rows.Should().Be(3);
        manifest.State.Tables.Should().Contain(t => t.Schema == "mmv_meta" && t.Table == "migration_run");
        (DateTimeOffset.UtcNow - manifest.State.ServerTimeUtc).Duration().Should().BeLessThan(TimeSpan.FromMinutes(5));

        var dump = Path.Combine(_directory, manifest.DumpFile);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(dump).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, "sauvegarde lisible du seul propriétaire");
        }

        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            var text = await File.ReadAllTextAsync(file);
            Secrets.Should().NotContain(s => text.Contains(s));
        }
    }

    [PostgreSqlTlsFact]
    public async Task Restore_is_real_isolated_from_workstations_compared_and_always_dropped()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(3);
        var manifestPath = await BackedUpAsync();
        string? scratch = null;

        var verifier = new RestoreVerifier(new MigrationJournal())
        {
            AfterRestore = async connectionString =>
            {
                scratch = await LifecycleDatabase.ScalarAsync<string>(connectionString, "SELECT current_database()::text");
                (await LifecycleDatabase.ScalarAsync<long>(connectionString, "SELECT count(*) FROM \"Suppliers\""))
                    .Should().Be(3, "les données sont réellement restaurées");
                (await LifecycleDatabase.ScalarAsync<string>(connectionString,
                        "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = current_database()"))
                    .Should().StartWith(RestoreVerifier.ScratchMarker);
                (await TlsServer.SqlStateAsync(_server.ConnectionString(scratch!, _request.AppRole, TlsServer.AppSecret), "SELECT 1"))
                    .Should().Be("42501", "aucun poste ne peut se connecter à la base de vérification");
                (await TlsServer.SqlStateAsync(_server.ConnectionString(scratch!, _request.BackupRole, TlsServer.BackupSecret), "SELECT 1"))
                    .Should().Be("42501");
            }
        };

        var result = await verifier.RunAsync(new RestoreVerificationRequest(AdminSettings(), manifestPath, "OP-IT", PgBin, null));

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        scratch.Should().Be(ScratchOf(await ManifestAsync(manifestPath)));
        (await DatabaseExistsAsync(scratch!)).Should().BeFalse();
        var proof = await BackupFiles.ReadProofAsync(BackupFiles.ProofPathFor(manifestPath), CancellationToken.None);
        proof.VerifierRole.Should().Be(_server.Admin.Username, "la restauration appartient à l'administrateur");
        proof.Rows.Should().BeGreaterThanOrEqualTo(3);
    }

    // ---- sauvegardes refusées ----------------------------------------------------------------------------------

    [PostgreSqlTlsFact]
    public async Task Corrupted_backup_fails_the_structural_check_and_writes_no_proof()
    {
        var manifestPath = await BackedUpAsync();
        await ReplaceDumpConsistentlyAsync(manifestPath, bytes => { bytes[0] ^= 0xFF; return bytes; });

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(22, text);
        text.Should().Contain("contrôle structurel");
        File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Incomplete_backup_fails_and_leaves_neither_proof_nor_scratch_database()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(5);
        var manifestPath = await BackedUpAsync();
        await ReplaceDumpConsistentlyAsync(manifestPath, bytes => bytes[..(bytes.Length / 2)]);

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(22, text);
        text.Should().Contain("NON vérifiée");
        File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeFalse();
        (await DatabaseExistsAsync(ScratchOf(await ManifestAsync(manifestPath)))).Should().BeFalse();
        (await MigrateAsync(manifestPath)).Code.Should().Be(11);
    }

    [PostgreSqlTlsFact]
    public async Task Restored_history_different_from_the_manifest_is_refused()
    {
        await MigratedAsync();
        var manifestPath = await BackedUpAsync();
        var manifest = await ManifestAsync(manifestPath);
        await BackupFiles.WriteManifestAsync(manifestPath, manifest with
        {
            State = manifest.State with { AppliedMigrations = [.. manifest.State.AppliedMigrations, "99999999999999_NotInTheBackup"] }
        }, CancellationToken.None);

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(22, text);
        text.Should().Contain("historique de migration différent");
        (await DatabaseExistsAsync(ScratchOf(manifest))).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Restored_row_counts_different_from_the_manifest_are_refused()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(2);
        var manifestPath = await BackedUpAsync();
        var manifest = await ManifestAsync(manifestPath);
        await BackupFiles.WriteManifestAsync(manifestPath, manifest with
        {
            State = manifest.State with
            {
                Tables = manifest.State.Tables.Select(t => t.Table == "Suppliers" ? t with { Rows = t.Rows + 1 } : t).ToArray()
            }
        }, CancellationToken.None);

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(22, text);
        text.Should().Contain("nombre de lignes différent").And.Contain("public.Suppliers (3 → 2)");
    }

    [PostgreSqlTlsFact]
    public async Task Existing_scratch_database_is_never_overwritten()
    {
        var manifestPath = await BackedUpAsync();
        var scratch = "mmv_it_scr_" + TlsServer.Suffix();
        await _server.ExecuteAsync("postgres", $"CREATE DATABASE {scratch}");
        try
        {
            var (code, text) = await VerifyAsync(manifestPath, scratch: scratch);

            code.Should().Be(10, text);
            text.Should().Contain("existe déjà");
            (await DatabaseExistsAsync(scratch)).Should().BeTrue("une base que l'outil n'a pas créée n'est jamais supprimée");
            File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeFalse();
        }
        finally
        {
            await _server.ExecuteAsync("postgres", $"DROP DATABASE IF EXISTS {scratch} WITH (FORCE)");
        }
    }

    [PostgreSqlTlsFact]
    public async Task Scratch_database_equal_to_the_backed_up_database_is_refused()
    {
        var manifestPath = await BackedUpAsync();

        var (code, text) = await VerifyAsync(manifestPath, scratch: _request.Database);

        code.Should().Be(10, text);
        text.Should().Contain("jamais la base sauvegardée");
        (await _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM \"__EFMigrationsHistory\"")).Should().Be(0);
    }

    // ---- migrate : rapprochement sous verrou ---------------------------------------------------------------

    [PostgreSqlTlsFact]
    public async Task Rows_written_after_the_verified_backup_refuse_the_migration_without_any_write()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(2);
        var manifestPath = await VerifiedBackupAsync();
        await SeedSuppliersAsync(1);
        var journal = await JournalRowsAsync();

        var (code, text) = await MigrateAsync(manifestPath);

        code.Should().Be(11, text);
        text.Should().Contain("la base a changé").And.Contain("public.Suppliers (2 → 3)");
        (await JournalRowsAsync()).Should().Be(journal, "un refus de sauvegarde n'écrit rien, même sous le verrou");
    }

    [PostgreSqlTlsFact]
    public async Task Verified_backup_of_another_database_is_refused()
    {
        var other = _server.Request(TlsServer.Suffix());
        _extraDatabases.Add(other);
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(other)).ExitCode.Should().Be(MigrationExitCode.Success);
        var manifestPath = await VerifiedBackupAsync();

        var (code, text) = await MigrateAsync(manifestPath, other);

        code.Should().Be(11, text);
        text.Should().Contain("autre base");
        (await _server.ScalarAsync<bool>(other.Database, "SELECT to_regnamespace('mmv_meta') IS NULL")).Should().BeTrue();
    }

    [PostgreSqlTlsFact]
    public async Task Verified_backup_older_than_the_maximum_age_is_refused_on_the_server_clock()
    {
        var manifestPath = await VerifiedBackupAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        await using var session = PostgreSqlMigrationSession.Create(
            builder => DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = _server.ConnectionString(_request.Database, _request.MigratorRole, TlsServer.MigratorSecret)
            }),
            MigrationRunner.DefaultLockTimeout);
        var result = await new MigrationRunner(session.Ports, new ProofBackupVerification(TimeSpan.FromSeconds(1)), new MigrationJournal())
            .RunAsync(new MigrationRunRequest(MigrationRunKind.Migrate, "OP-IT", manifestPath, _request.AppRole, TimeSpan.Zero,
                ApplicationVersion.Current));

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified, result.Message);
        result.Message.Should().Contain("trop ancienne");
    }

    [PostgreSqlTlsFact]
    public async Task Proof_removed_after_verification_means_not_verified()
    {
        var manifestPath = await VerifiedBackupAsync();
        File.Delete(BackupFiles.ProofPathFor(manifestPath));

        (await MigrateAsync(manifestPath)).Code.Should().Be(11);
    }

    // ---- rôles et escalade ----------------------------------------------------------------------------------

    [PostgreSqlTlsFact]
    public async Task Application_role_cannot_back_up_the_database_and_leaves_no_file()
    {
        await MigratedAsync();

        var (code, text) = await BackupAsync(role: _request.AppRole, secret: TlsServer.AppSecret);

        code.Should().Be(21, text);
        text.Should().Contain("42501", "le journal du migrateur est illisible pour l'applicatif : la sauvegarde est refusée");
        Directory.GetFiles(_directory, "mmv-backup-*").Should().BeEmpty("un échec ne laisse aucun fichier");
    }

    [PostgreSqlTlsFact]
    public async Task Missing_pg_dump_fails_the_backup_and_leaves_no_file()
    {
        var empty = Directory.CreateTempSubdirectory("mmv-it-nobin-").FullName;
        try
        {
            var (code, text) = await ToolAsync(TlsServer.BackupSecret, null,
                "backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
                "--database", _request.Database, "--username", _request.BackupRole, "--output-directory", _directory,
                "--operator", "OP-IT", "--pg-bin", empty);

            code.Should().Be(21, text);
            text.Should().Contain("pg_dump introuvable");
            Directory.GetFiles(_directory, "mmv-backup-*").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [PostgreSqlTlsFact]
    public async Task Migrator_cannot_verify_by_restoring_and_is_never_granted_createdb()
    {
        var manifestPath = await BackedUpAsync();

        var (code, text) = await VerifyAsync(manifestPath, user: _request.MigratorRole, secret: TlsServer.MigratorSecret);

        code.Should().Be(22, text);
        text.Should().Contain("identifiant administrateur requis");
        File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeFalse();
        (await _server.ScalarAsync<bool>("postgres",
                $"SELECT rolcreatedb OR rolsuper OR rolcreaterole FROM pg_roles WHERE rolname = {TlsServer.Literal(_request.MigratorRole)}"))
            .Should().BeFalse("aucune élévation du migrateur pour vérifier une sauvegarde");
        (await MigrateAsync(manifestPath)).Code.Should().Be(11);
    }

    [PostgreSqlTlsFact]
    public async Task Application_and_backup_roles_can_neither_restore_nor_write_nor_create_databases()
    {
        await MigratedAsync();
        var app = _server.ConnectionString(_request.Database, _request.AppRole, TlsServer.AppSecret);
        var backup = _server.ConnectionString(_request.Database, _request.BackupRole, TlsServer.BackupSecret);

        (await TlsServer.SqlStateAsync(app, "CREATE DATABASE mmv_it_never")).Should().Be("42501");
        (await TlsServer.SqlStateAsync(app, "DROP TABLE \"Suppliers\"")).Should().Be("42501");
        (await TlsServer.SqlStateAsync(app, "CREATE TABLE public.mmv_it_never (id int)")).Should().Be("42501");
        (await TlsServer.SqlStateAsync(backup, "CREATE DATABASE mmv_it_never")).Should().Be("42501");
        (await TlsServer.SqlStateAsync(backup, "INSERT INTO \"Suppliers\" (\"Name\") VALUES ('x')")).Should().Be("42501");
        (await TlsServer.SqlStateAsync(backup, "DELETE FROM mmv_meta.migration_run")).Should().Be("42501");
    }

    // ---- concurrence, reprise, idempotence ----------------------------------------------------------------

    [PostgreSqlTlsFact]
    public async Task Verification_already_running_is_refused_with_code_12_and_touches_nothing()
    {
        var manifestPath = await VerifiedBackupAsync();
        // Sans pool : un verrou de session survit au retour d'une connexion dans le pool (mesure M-3, P4-6B) et
        // bloquerait les tests suivants.
        await using var holder = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(_server.AdminOn("postgres")) { Pooling = false }.ConnectionString);
        await holder.OpenAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_lock({RestoreVerifier.LockKey})", holder))
        {
            await command.ExecuteNonQueryAsync();
        }

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(12, text);
        File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeTrue("une exécution refusée ne retire pas la preuve existante");
        (await DatabaseExistsAsync(ScratchOf(await ManifestAsync(manifestPath)))).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Concurrent_verifications_serialize_and_leave_a_valid_proof()
    {
        var manifestPath = await BackedUpAsync();

        var results = await Task.WhenAll(VerifyAsync(manifestPath), VerifyAsync(manifestPath));

        results.Select(r => r.Code).Should().OnlyContain(c => c == 0 || c == 12).And.Contain(0);
        (await DatabaseExistsAsync(ScratchOf(await ManifestAsync(manifestPath)))).Should().BeFalse();
        (await MigrateAsync(manifestPath)).Code.Should().Be(0, "la preuve laissée par la vérification réussie est valide");
    }

    [PostgreSqlTlsFact]
    public async Task Concurrent_backups_produce_distinct_complete_backups()
    {
        await MigratedAsync();
        await SeedSuppliersAsync(2);

        var results = await Task.WhenAll(BackupAsync(), BackupAsync());

        results.Should().OnlyContain(r => r.Code == 0);
        var manifests = results.Select(r => ManifestOf(r.Text)).ToArray();
        manifests.Distinct().Should().HaveCount(2);
        foreach (var manifest in manifests)
        {
            (await ManifestAsync(manifest)).State.Tables.Single(t => t.Table == "Suppliers").Rows.Should().Be(2);
        }
    }

    [PostgreSqlTlsFact]
    public async Task Scratch_database_left_by_an_interrupted_run_is_cleaned_by_the_next_verification()
    {
        var leftover = "mmv_it_leftover_" + TlsServer.Suffix();
        await _server.ExecuteAsync("postgres",
            $"CREATE DATABASE {leftover}",
            $"COMMENT ON DATABASE {leftover} IS '{RestoreVerifier.ScratchMarker}interrupted'");
        var manifestPath = await BackedUpAsync();

        var (code, text) = await VerifyAsync(manifestPath);

        code.Should().Be(0, text);
        text.Should().Contain("résiduelle").And.Contain(leftover);
        (await DatabaseExistsAsync(leftover)).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Verification_is_idempotent_and_a_failed_reverification_withdraws_the_proof()
    {
        var manifestPath = await BackedUpAsync();

        (await VerifyAsync(manifestPath)).Code.Should().Be(0);
        (await VerifyAsync(manifestPath)).Code.Should().Be(0);
        var manifest = await ManifestAsync(manifestPath);
        await BackupFiles.WriteManifestAsync(manifestPath, manifest with
        {
            State = manifest.State with { AppliedMigrations = ["99999999999999_NotInTheBackup"] }
        }, CancellationToken.None);

        (await VerifyAsync(manifestPath)).Code.Should().Be(22);
        File.Exists(BackupFiles.ProofPathFor(manifestPath)).Should().BeFalse("une vérification en échec sous verrou retire la preuve");
    }

    [PostgreSqlTlsFact]
    public async Task Migration_consumes_the_backup_a_second_migration_needs_a_new_one()
    {
        var manifestPath = await VerifiedBackupAsync();
        (await MigrateAsync(manifestPath)).Code.Should().Be(0);

        var again = await MigrateAsync(manifestPath);

        again.Code.Should().Be(11, "la base a changé depuis la sauvegarde (journal, schéma) : elle n'est plus le point de retour");
        (await MigrateAsync(await VerifiedBackupAsync())).Code.Should().Be(0);
    }
}
