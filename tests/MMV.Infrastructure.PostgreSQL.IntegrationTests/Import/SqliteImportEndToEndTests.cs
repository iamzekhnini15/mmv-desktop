using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Provisioning;
using MMV.Import.TestSupport;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Import;

/// <summary>
/// P4-7 de bout en bout par le point d'entrée réel (<see cref="Program.RunAsync"/>) : serveur TLS (D-14), base
/// provisionnée et migrée par l'outil, <b>vraie</b> sauvegarde vérifiée P4-9 exigée avant l'import, et retour arrière
/// après validation par <c>restore-backup</c> dans une base neuve (R-9). Aucune sortie, trace ni rapport ne contient
/// de secret. Sérialisé avec les autres tests de vérification de sauvegarde (verrou de vérification du serveur).
/// </summary>
[Collection(BackupVerificationCollection.Name)]
public sealed class SqliteImportEndToEndTests : IAsyncLifetime
{
    private readonly TlsServer _server = new();
    private readonly ProvisioningRequest _request;
    private readonly List<string> _restored = new();
    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-it-import-e2e-").FullName;

    public SqliteImportEndToEndTests() => _request = _server.Request(TlsServer.Suffix());

    private static string PgBin => PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.PgBinVariableName);

    private string[] Secrets => [TlsServer.MigratorSecret, TlsServer.AppSecret, TlsServer.BackupSecret, _server.Admin.Password!];

    public async Task InitializeAsync()
    {
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
        (await _server.MigrateAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    public async Task DisposeAsync()
    {
        foreach (var database in _restored)
        {
            await _server.ExecuteAsync("postgres", $"DROP DATABASE IF EXISTS {TlsServer.Quote(database)} WITH (FORCE)");
        }

        await _server.DropAsync(_request);
        Directory.Delete(_directory, recursive: true);
    }

    private async Task<(int Code, string Text)> ToolAsync(string stdin, IReadOnlyDictionary<string, string?>? environment, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(_directory, $"trace-{Guid.NewGuid():N}.log");
        var code = await Program.RunAsync(args, environment ?? new Dictionary<string, string?>(), output, error, trace, new StringReader(stdin));
        var text = output + "\n" + error + "\n" + (File.Exists(trace) ? await File.ReadAllTextAsync(trace) : string.Empty);
        foreach (var secret in Secrets)
        {
            text.Should().NotContain(secret, "aucun secret dans la sortie ni dans la trace de l'outil");
        }

        return (code, text);
    }

    private async Task<string> VerifiedBackupAsync()
    {
        var (code, text) = await ToolAsync(TlsServer.BackupSecret, null,
            "backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--database", _request.Database, "--username", _request.BackupRole, "--output-directory", _directory,
            "--operator", "OP-IT", "--pg-bin", PgBin);
        code.Should().Be(0, text);
        var manifest = Regex.Match(text, @"Manifeste : (.+?\.manifest\.json)").Groups[1].Value;

        var verify = await ToolAsync(_server.Admin.Password!, null,
            "verify-backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--admin-user", _server.Admin.Username!, "--admin-database", "postgres", "--manifest", manifest,
            "--operator", "OP-IT", "--pg-bin", PgBin);
        verify.Code.Should().Be(0, verify.Text);
        return manifest;
    }

    private async Task<(int Code, string Text, string Report)> ImportAsync(string source, string manifest, bool dryRun = false)
    {
        var report = Path.Combine(_directory, $"import-{Guid.NewGuid():N}.json");
        string[] args =
        [
            "import-sqlite", "--source", source, "--source-time-zone", "Africa/Casablanca", "--operator", "OP-IT",
            "--backup-ref", manifest, "--report", report
        ];
        var (code, text) = await ToolAsync(string.Empty,
            new Dictionary<string, string?>
            {
                [MigrationToolOptions.ConnectionStringVariableName] =
                    _server.ConnectionString(_request.Database, _request.MigratorRole, _request.MigratorPassword)
            },
            dryRun ? [.. args, "--dry-run"] : args);
        var reportText = await File.ReadAllTextAsync(report);
        foreach (var secret in Secrets)
        {
            reportText.Should().NotContain(secret, "aucun secret dans le rapport");
        }

        return (code, text, reportText);
    }

    private Task<long?> SuppliersAsync(string database) =>
        _server.ScalarAsync<long?>(database, "SELECT count(*) FROM \"Suppliers\"");

    [PostgreSqlTlsFact]
    public async Task Import_through_the_tool_requires_a_current_verified_backup_and_restore_backup_undoes_it()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(5);
        var manifest = await VerifiedBackupAsync();

        var dry = await ImportAsync(source.Path, manifest, dryRun: true);
        dry.Code.Should().Be(0, dry.Text);
        JsonDocument.Parse(dry.Report).RootElement.GetProperty("Outcome").GetString().Should().Be("dry-run-rolled-back");
        (await SuppliersAsync(_request.Database)).Should().Be(0);

        var (code, text, report) = await ImportAsync(source.Path, manifest);

        code.Should().Be(0, text + "\n(l'essai à blanc ne consomme pas la sauvegarde : même manifeste)");
        var json = JsonDocument.Parse(report).RootElement;
        json.GetProperty("Outcome").GetString().Should().Be("imported");
        json.GetProperty("BackupId").GetString().Should().NotBeNullOrEmpty("le rapport cite la sauvegarde vérifiée, point de retour");
        json.GetProperty("SourceTimeZone").GetString().Should().Be("Africa/Casablanca");
        (await SuppliersAsync(_request.Database)).Should().Be(5);
        (await LifecycleDatabase.ScalarAsync<long>(_server.ConnectionString(_request.Database, _request.AppRole, TlsServer.AppSecret),
            "SELECT count(*) FROM \"Suppliers\"")).Should().Be(5, "les postes lisent les données importées (droits P4-8)");

        // Retour arrière APRÈS validation : la sauvegarde d'avant import, restaurée dans une base neuve (R-9).
        var target = _request.Database + "_r";
        _restored.Add(target);
        var restore = await ToolAsync(_server.Admin.Password!, null,
            "restore-backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--admin-user", _server.Admin.Username!, "--admin-database", "postgres", "--manifest", manifest,
            "--target-database", target, "--migrator-role", _request.MigratorRole, "--operator", "OP-IT", "--pg-bin", PgBin);

        restore.Code.Should().Be(0, restore.Text);
        (await SuppliersAsync(target)).Should().Be(0, "la base restaurée est l'état exact d'avant import");
        (await _server.ScalarAsync<long?>(target, "SELECT count(*) FROM \"DocumentSequences\"")).Should().Be(2);
        (await SuppliersAsync(_request.Database)).Should().Be(5, "la base importée n'est jamais écrasée");
    }

    [PostgreSqlTlsFact]
    public async Task Import_is_refused_with_an_outdated_or_unverified_backup_and_writes_nothing()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(2);
        var manifest = await VerifiedBackupAsync();
        await _server.ExecuteAsync(_request.Database, "INSERT INTO \"Suppliers\" (\"Name\") VALUES ('écrit après la sauvegarde')");

        var outdated = await ImportAsync(source.Path, manifest);

        outdated.Code.Should().Be(11, outdated.Text);
        outdated.Text.Should().Contain("la base a changé depuis la sauvegarde");
        (await SuppliersAsync(_request.Database)).Should().Be(1);

        var missing = await ImportAsync(source.Path, Path.Combine(_directory, "absent.manifest.json"));
        missing.Code.Should().Be(11, missing.Text);
        (await SuppliersAsync(_request.Database)).Should().Be(1);
    }
}

/// <summary>Collection des tests qui vérifient des sauvegardes : <c>verify-backup</c> prend un verrou propre au serveur.</summary>
[CollectionDefinition(Name)]
public sealed class BackupVerificationCollection
{
    public const string Name = "Vérification de sauvegarde (verrou serveur)";
}
