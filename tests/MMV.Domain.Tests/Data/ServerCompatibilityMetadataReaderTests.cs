using FluentAssertions;
using MMV.Domain.Tests.TestDoubles;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P4-6B tranche B, B6 — lecture <b>seule</b> de <c>mmv_meta.schema_compatibility</c> (DP-3.5), hors modèle EF.
/// Absence de la table, table vide, valeurs illisibles : sans serveur, sur connexion scriptée.
/// </summary>
public sealed class ServerCompatibilityMetadataReaderTests
{
    private static async Task<(ServerCompatibilityMetadata Metadata, ScriptedDbConnection Connection)> ReadAsync(
        bool tableExists, (string? Schema, string? Minimum, DateTime? Maintenance)? row)
    {
        var connection = new ScriptedDbConnection(new ServerScript
        {
            MetadataTableExists = tableExists,
            MetadataRow = row
        }.Respond);
        connection.Open();

        return (await ServerCompatibilityMetadataReader.ReadAsync(connection), connection);
    }

    [Fact]
    public void Names_are_declared_once_and_frozen()
    {
        ServerCompatibilityMetadataNames.Schema.Should().Be("mmv_meta");
        ServerCompatibilityMetadataNames.CompatibilityTable.Should().Be("schema_compatibility");
        ServerCompatibilityMetadataNames.JournalTable.Should().Be("migration_run");
        ServerCompatibilityMetadataNames.QualifiedCompatibilityTable.Should().Be("mmv_meta.schema_compatibility");
        ServerCompatibilityMetadataNames.QualifiedJournalTable.Should().Be("mmv_meta.migration_run");
    }

    [Fact]
    public async Task Missing_table_is_Absent_and_the_row_is_never_queried()
    {
        var (metadata, connection) = await ReadAsync(tableExists: false, row: null);

        metadata.Status.Should().Be(ServerMetadataStatus.Absent);
        connection.Executed.Should().ContainSingle("seule l'existence de la table est interrogée");
        connection.Executed[0].Parameters.Values.Should().ContainSingle()
            .Which.Should().Be(ServerCompatibilityMetadataNames.QualifiedCompatibilityTable,
                "le nom de relation est un paramètre lié, jamais concaténé");
    }

    [Fact]
    public async Task Empty_table_is_NoRow()
    {
        var (metadata, _) = await ReadAsync(tableExists: true, row: null);

        metadata.Status.Should().Be(ServerMetadataStatus.NoRow);
    }

    [Fact]
    public async Task Present_row_is_read_with_its_three_values()
    {
        var marker = new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc);

        var (metadata, _) = await ReadAsync(tableExists: true, row: ("1.3.0", "1.1.0", marker));

        metadata.Status.Should().Be(ServerMetadataStatus.Present);
        metadata.SchemaVersion.Should().Be("1.3.0");
        metadata.MinimumSupportedVersion.Should().Be("1.1.0");
        metadata.MaintenanceStartedAt.Should().Be(new DateTimeOffset(marker));
        metadata.IsInitializationIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task Initialization_row_is_Present_and_flagged_incomplete()
    {
        var (metadata, _) = await ReadAsync(tableExists: true, row: (null, null, DateTime.UtcNow));

        metadata.Status.Should().Be(ServerMetadataStatus.Present);
        metadata.IsInitializationIncomplete.Should().BeTrue();
        metadata.MaintenanceStartedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("1.3", "1.1.0")]
    [InlineData("1.3.0", "x")]
    [InlineData("1.3.0-rc.1", "1.1.0")]
    [InlineData("1.3.0", null)]   // contrainte (NULL ensemble) violée
    [InlineData(null, "1.1.0")]
    public async Task Unreadable_values_are_reported_never_guessed(string? schema, string? minimum)
    {
        var (metadata, _) = await ReadAsync(tableExists: true, row: (schema, minimum, null));

        metadata.Status.Should().Be(ServerMetadataStatus.Unreadable);
    }

    [Fact]
    public async Task Unreadable_row_still_carries_its_maintenance_marker()
    {
        var (metadata, _) = await ReadAsync(tableExists: true, row: ("garbage", "1.0.0", DateTime.UtcNow));

        metadata.Status.Should().Be(ServerMetadataStatus.Unreadable);
        metadata.MaintenanceStartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reader_emits_only_reads()
    {
        var (_, connection) = await ReadAsync(tableExists: true, row: ("1.0.0", "1.0.0", null));

        connection.Executed.Should().HaveCount(2).And.OnlyContain(c => ServerScript.IsReadOnly(c.Text));
    }
}
