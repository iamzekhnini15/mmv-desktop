using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Import;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// P4-10 — exploitation pendant que des postes travaillent (processus OS distincts, rôle applicatif) : migration
/// additive appliquée par l'outil réel sur la table que les postes écrivent (roadmap : « migration pendant qu'un poste
/// travaille »), puis sauvegarde prise sous écritures concurrentes et vérifiée par restauration réelle (« sauvegarde
/// puis restauration »). Les ventes de chaque poste sont comptées par le poste lui-même et rapprochées de la base.
/// </summary>
public static class WorkLoad
{
    private static readonly Regex Ok = new(@"(?:^|,)ok=(\d+)");

    public static int Sold(IEnumerable<string> results) =>
        results.Sum(r => Ok.Match(r) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0);

    public static async Task WaitForSalesAsync(string connectionString, long atLeast)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (await LifecycleDatabase.ScalarAsync<long>(connectionString, "SELECT count(*) FROM \"Sales\"") < atLeast)
        {
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(90), "les postes doivent avoir commencé à vendre");
            await Task.Delay(50);
        }
    }
}

public sealed class MigrationUnderLoadTests : LifecycleTestBase
{
    [PostgreSqlFact]
    public async Task Additive_migration_applied_while_workstations_sell_on_the_same_table_loses_and_duplicates_nothing()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.GrantApplicationDataAccessAsync();
        long productId;
        await using (var context = Db.Context<ChainS1>(Db.MigratorConnectionString))
        {
            productId = await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context), stock: 1000);
        }

        var workers = Enumerable.Range(0, 2)
            .Select(_ => Workstations.Start(Db.AppConnectionString, null, "loop-sales", productId.ToString(), "400"))
            .ToList();
        await WorkLoad.WaitForSalesAsync(Db.AdminConnectionString, 5);

        var migration = await Db.RunAsync<ChainS1ThenAlterProducts>("1.1.0", wait: TimeSpan.FromSeconds(30));
        var soldWhenMigrated = await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"Sales\"");
        var results = await Task.WhenAll(workers.Select(w => Workstations.ResultAsync(w)));

        migration.ExitCode.Should().Be(MigrationExitCode.Success, migration.Message);
        soldWhenMigrated.Should().BeInRange(5, WorkLoad.Sold(results) - 1, "les postes vendaient avant, pendant et après la migration");
        results.Should().OnlyContain(r => Regex.IsMatch(r, @"^(ok|error:DatabaseBusy)=\d+(,(ok|error:DatabaseBusy)=\d+)*$"),
            "un poste ne voit que des ventes réussies ou un refus franc pendant le verrou du DDL : " + string.Join(" | ", results));
        var sold = WorkLoad.Sold(results);
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"Sales\"")).Should().Be(sold, "aucune vente perdue ni dupliquée");
        (await Db.AdminScalarAsync<int>($"SELECT \"StockQuantity\" FROM \"Products\" WHERE \"ProductId\" = {productId}")).Should().Be(1000 - sold);
        (await Db.AdminScalarAsync<long>("SELECT \"CurrentValue\" FROM \"DocumentSequences\" WHERE \"SequenceName\" = 'SALE'")).Should().Be(sold);
        (await Db.AdminScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Products' AND column_name = 'it_expand_probe')"))
            .Should().BeTrue("la migration additive est appliquée sous charge");
    }
}

/// <summary>Sauvegarde prise pendant que deux postes vendent, puis vérifiée par restauration réelle (B-1, B-2).</summary>
[Collection(BackupVerificationCollection.Name)]
public sealed class BackupUnderLoadTests : IAsyncLifetime
{
    private readonly TlsServer _server = new();
    private readonly ProvisioningRequest _request;
    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-it-backup-load-").FullName;

    public BackupUnderLoadTests() => _request = _server.Request(TlsServer.Suffix());

    private static string PgBin => PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.PgBinVariableName);

    public async Task InitializeAsync()
    {
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
        (await _server.MigrateAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    public async Task DisposeAsync()
    {
        await _server.DropAsync(_request);
        Directory.Delete(_directory, recursive: true);
    }

    private async Task<(int Code, string Text)> ToolAsync(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await Program.RunAsync(args, new Dictionary<string, string?>(), output, error,
            Path.Combine(_directory, "trace.log"), new StringReader(stdin));
        return (code, output + "\n" + error);
    }

    [PostgreSqlTlsFact]
    public async Task Backup_taken_while_workstations_sell_restores_exactly_its_manifest()
    {
        var app = _server.ConnectionString(_request.Database, _request.AppRole, TlsServer.AppSecret);
        long productId;
        await using (var context = _server.Context(app))
        {
            productId = await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context), stock: 1000);
        }

        var workers = Enumerable.Range(0, 2).Select(_ => Workstations.Start(app, null, "loop-sales", productId.ToString(), "300")).ToList();
        await WorkLoad.WaitForSalesAsync(app, 3);

        var backup = await ToolAsync(TlsServer.BackupSecret,
            "backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--database", _request.Database, "--username", _request.BackupRole, "--output-directory", _directory,
            "--operator", "OP-IT", "--pg-bin", PgBin);
        var results = await Task.WhenAll(workers.Select(w => Workstations.ResultAsync(w)));

        backup.Code.Should().Be(0, backup.Text);
        var manifest = Regex.Match(backup.Text, @"Manifeste : (.+?\.manifest\.json)").Groups[1].Value;
        var verify = await ToolAsync(_server.Admin.Password!,
            "verify-backup", "--host", _server.Host, "--port", _server.Port.ToString(), "--root-certificate", _server.RootCertificate,
            "--admin-user", _server.Admin.Username!, "--admin-database", "postgres", "--manifest", manifest,
            "--operator", "OP-IT", "--pg-bin", PgBin);

        verify.Code.Should().Be(0, "le fichier restauré correspond exactement au manifeste malgré les écritures concurrentes (B-2) : " + verify.Text);
        var sold = WorkLoad.Sold(results);
        sold.Should().Be(600, string.Join(" | ", results));
        (await LifecycleDatabase.ScalarAsync<long>(app, "SELECT count(*) FROM \"Sales\"")).Should().Be(sold);
        var (saved, _) = await MMV.DatabaseManager.Backup.BackupFiles.ReadManifestAsync(manifest, CancellationToken.None);
        saved.State.Tables.Single(t => t.Table == "Sales").Rows.Should().BeInRange(3, sold - 1,
            "l'instantané a été pris pendant que les postes vendaient : il précède les dernières ventes");
    }
}
