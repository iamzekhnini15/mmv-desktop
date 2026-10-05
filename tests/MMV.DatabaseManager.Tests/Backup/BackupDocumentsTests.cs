using System.Text.Json.Nodes;
using FluentAssertions;
using MMV.DatabaseManager.Backup;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>P4-9 — format strict du manifeste : tout manifeste incorrect, incomplet ou dangereux est refusé.</summary>
public sealed class BackupDocumentsTests
{
    private static async Task<string> Mutate(TemporaryBackup backup, Action<JsonObject> change)
    {
        var json = JsonNode.Parse(await File.ReadAllTextAsync(backup.ManifestPath))!.AsObject();
        change(json);
        await File.WriteAllTextAsync(backup.ManifestPath, json.ToJsonString());
        return backup.ManifestPath;
    }

    private static async Task ShouldBeRefused(string path, string because)
    {
        var act = () => BackupFiles.ReadManifestAsync(path, CancellationToken.None);
        (await act.Should().ThrowAsync<BackupDocumentException>()).Which.Message.Should().Contain(because);
    }

    [Fact]
    public async Task Manifest_round_trips_exactly()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        var (manifest, sha256) = await BackupFiles.ReadManifestAsync(backup.ManifestPath, CancellationToken.None);

        manifest.Should().BeEquivalentTo(backup.Manifest);
        sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Malformed_json_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await File.WriteAllTextAsync(backup.ManifestPath, "{ \"format\": ");

        await ShouldBeRefused(backup.ManifestPath, "illisible");
    }

    [Fact]
    public async Task Missing_member_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j.Remove("appliedMigrations")), "illisible ou incomplet");
    }

    [Fact]
    public async Task Null_member_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["operator"] = null), "illisible ou incomplet");
    }

    [Fact]
    public async Task Unknown_member_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["verified"] = true), "illisible ou incomplet");
    }

    [Fact]
    public async Task Unknown_format_version_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["formatVersion"] = 2), "format de manifeste inattendu");
    }

    [Theory]
    [InlineData("../outside.dump")]
    [InlineData("sub/inside.dump")]
    [InlineData("sub\\inside.dump")]
    [InlineData("C:\\absolute.dump")]
    [InlineData("/absolute.dump")]
    [InlineData("backup.sql")]
    public async Task Dump_file_must_be_a_simple_name_next_to_the_manifest(string file)
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["dump"]!["file"] = file), "nom de fichier de sauvegarde invalide");
    }

    [Theory]
    [InlineData("ABCDEF")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000ff")]
    [InlineData("G000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Malformed_checksum_is_refused(string sha256)
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["dump"]!["sha256"] = sha256), "SHA-256");
    }

    [Fact]
    public async Task Unsorted_or_duplicated_migration_history_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["appliedMigrations"] = new JsonArray("B", "A")), "historique de migration invalide");
        await ShouldBeRefused(await Mutate(backup, j => j["appliedMigrations"] = new JsonArray("A", "A")), "historique de migration invalide");
    }

    [Fact]
    public async Task Non_utc_server_timestamp_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["createdAtServerUtc"] = "2026-10-05T10:00:00+02:00"), "UTC");
    }

    [Fact]
    public async Task Incomplete_database_identity_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await ShouldBeRefused(await Mutate(backup, j => j["server"]!["databaseOid"] = 0), "identité de la base incomplète");
    }

    [Fact]
    public async Task Atomic_write_replaces_and_leaves_no_temporary_file()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        await backup.RewriteManifestAsync(m => m with { Operator = "OP-2" });

        Directory.GetFiles(backup.Directory, "*" + BackupFiles.PartialSuffix).Should().BeEmpty();
        (await BackupFiles.ReadManifestAsync(backup.ManifestPath, CancellationToken.None)).Manifest.Operator.Should().Be("OP-2");
    }
}
