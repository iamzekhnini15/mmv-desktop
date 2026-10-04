using System.Data;
using FluentAssertions;
using MMV.Domain.Tests.TestDoubles;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P4-6B tranche B (DP-3, DP-4 ; H1, H3, H4, H14) — garde de compatibilité d'un poste, <b>en lecture seule</b>,
/// éprouvée sans serveur sur ensembles injectés (U-B1 … U-B5, U-B7) et sur connexion scriptée (U-B6).
/// La garde n'est appelée par aucun poste en P4-6B : son branchement est P4-6C.
/// </summary>
public sealed class ServerSchemaCompatibilityGuardTests
{
    private const string M1 = "20260101000000_One";
    private const string M2 = "20260201000000_Two";
    private const string M3 = "20260301000000_Three";
    private const string Foreign = "20260215000000_Foreign";

    private static readonly DateTimeOffset AncientMarker = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ServerCompatibilityMetadata Row(string? schema, string? minimum, DateTimeOffset? maintenance = null) =>
        ServerCompatibilityMetadata.FromRow(schema, minimum, maintenance);

    private static ServerSchemaVerdict Evaluate(
        string[] applied,
        string[] known,
        ServerCompatibilityMetadata? metadata = null,
        string version = "1.1.0",
        bool historyExists = true,
        bool applicationTablesExist = true) =>
        ServerSchemaCompatibilityGuard.Evaluate(new ServerSchemaObservation(
            applied, known, historyExists, applicationTablesExist,
            metadata ?? Row("1.1.0", "1.0.0"), version));

    // ---- U-B1 : les cinq cas, et l'ordre d'évaluation -------------------------------------------------

    [Fact]
    public void C1_applied_equals_known_is_E1_and_starts()
    {
        var verdict = Evaluate([M1, M2], [M1, M2]);

        verdict.State.Should().Be(ServerSchemaState.E1);
        verdict.Case.Should().Be(CompatibilityCase.C1);
        verdict.CanStart.Should().BeTrue();
    }

    [Fact]
    public void C2_known_not_applied_is_E2_and_blocks_without_any_window()
    {
        var verdict = Evaluate([M1], [M1, M2], version: "9.9.9");

        verdict.State.Should().Be(ServerSchemaState.E2);
        verdict.Case.Should().Be(CompatibilityCase.C2);
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void C3_applied_beyond_known_with_version_at_least_minimum_is_E3a_and_starts()
    {
        var verdict = Evaluate([M1, M2, M3], [M1, M2], Row("1.3.0", "1.1.0"), version: "1.2.0");

        verdict.State.Should().Be(ServerSchemaState.E3a);
        verdict.Case.Should().Be(CompatibilityCase.C3);
        verdict.CanStart.Should().BeTrue();
    }

    [Fact]
    public void C4_applied_beyond_known_with_version_below_minimum_is_E3b_and_blocks()
    {
        var verdict = Evaluate([M1, M2, M3], [M1], Row("1.3.0", "1.1.0"), version: "1.0.0");

        verdict.State.Should().Be(ServerSchemaState.E3b);
        verdict.Case.Should().Be(CompatibilityCase.C4);
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void C5_divergent_chains_are_E3c_and_block_unconditionally()
    {
        var verdict = Evaluate([M1, Foreign], [M1, M2], Row("1.3.0", "1.0.0"), version: "9.9.9");

        verdict.State.Should().Be(ServerSchemaState.E3c);
        verdict.Case.Should().Be(CompatibilityCase.C5);
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void Divergence_is_evaluated_before_lag_and_never_read_as_a_simple_delay()
    {
        // Lu dans le mauvais ordre, K \ A ≠ ∅ donnerait C-2 (« base en retard ») au lieu de C-5.
        var verdict = Evaluate([Foreign], [M1, M2]);

        verdict.Case.Should().Be(CompatibilityCase.C5);
    }

    [Fact]
    public void Applied_but_unknown_migration_is_detected_where_pending_only_check_saw_nothing()
    {
        // U-1 : GetPendingMigrations() (K \ A) est vide ici ; la garde voit pourtant A \ K ≠ ∅.
        var verdict = Evaluate([M1, M2], [M1], Row("1.1.0", "1.1.0"), version: "1.0.0");

        verdict.State.Should().Be(ServerSchemaState.E3b);
        verdict.CanStart.Should().BeFalse();
    }

    // ---- U-B2 : la fenêtre N-1 -------------------------------------------------------------------------

    [Theory]
    [InlineData("1.1.0", true)]   // N-1 exactement au minimum
    [InlineData("1.2.0", true)]   // release sans migration au-dessus du minimum
    [InlineData("1.0.9", false)]  // N-2
    [InlineData("0.9.0", false)]
    public void E3a_is_granted_only_when_version_is_at_least_minimum(string version, bool canStart)
    {
        var verdict = Evaluate([M1, M2, M3], [M1, M2], Row("1.3.0", "1.1.0"), version);

        verdict.CanStart.Should().Be(canStart);
        verdict.State.Should().Be(canStart ? ServerSchemaState.E3a : ServerSchemaState.E3b);
    }

    [Fact]
    public void Minimum_comparison_is_numeric_not_lexical()
    {
        var verdict = Evaluate([M1, M2, M3], [M1, M2], Row("1.10.0", "1.9.0"), version: "1.10.0");

        verdict.State.Should().Be(ServerSchemaState.E3a);
    }

    [Fact]
    public void Newer_client_than_schema_is_blocked_without_window()
    {
        var verdict = Evaluate([M1, M2], [M1, M2, M3], Row("1.1.0", "1.0.0"), version: "1.3.0");

        verdict.State.Should().Be(ServerSchemaState.E2);
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void Prerelease_client_version_never_gets_a_window()
    {
        var verdict = Evaluate([M1, M2, M3], [M1, M2], Row("1.3.0", "1.1.0"), version: "1.2.0-rc.1");

        verdict.State.Should().Be(ServerSchemaState.E3b);
        verdict.CanStart.Should().BeFalse();
    }

    // ---- U-B3 : métadonnée absente ⇒ égalité stricte ---------------------------------------------------

    [Theory]
    [MemberData(nameof(MissingMetadata))]
    public void Missing_metadata_allows_only_strict_equality(ServerCompatibilityMetadata metadata)
    {
        Evaluate([M1, M2], [M1, M2], metadata).State.Should().Be(ServerSchemaState.E1);

        var ahead = Evaluate([M1, M2, M3], [M1, M2], metadata, version: "9.9.9");
        ahead.State.Should().Be(ServerSchemaState.E3b);
        ahead.CanStart.Should().BeFalse();
    }

    public static TheoryData<ServerCompatibilityMetadata> MissingMetadata() => new()
    {
        ServerCompatibilityMetadata.Absent,
        ServerCompatibilityMetadata.NoRow,
        Row("not-semver", "1.0.0")
    };

    // ---- U-B4 : maintenance_started_at — signal d'admission à sens unique ------------------------------

    [Fact]
    public void Maintenance_marker_blocks_whatever_its_age()
    {
        foreach (var marker in new[] { AncientMarker, DateTimeOffset.UtcNow })
        {
            var verdict = Evaluate([M1, M2], [M1, M2], Row("1.1.0", "1.0.0", marker));

            verdict.State.Should().Be(ServerSchemaState.E5);
            verdict.CanStart.Should().BeFalse();
            verdict.InitializationIncomplete.Should().BeFalse();
        }
    }

    [Fact]
    public void Maintenance_marker_never_authorizes_a_start()
    {
        // Sans marqueur, ces observations bloquent ; avec marqueur, elles bloquent toujours.
        foreach (var (applied, known) in new[] { (new[] { M1 }, new[] { M1, M2 }), (new[] { Foreign }, new[] { M1 }) })
        {
            Evaluate(applied, known, Row("1.1.0", "1.0.0", AncientMarker)).CanStart.Should().BeFalse();
        }
    }

    // ---- U-B5 : E7 (tables sans historique) et E4 (base vide) ------------------------------------------

    [Fact]
    public void Tables_without_EF_history_are_E7_and_block()
    {
        var verdict = Evaluate([], [M1], ServerCompatibilityMetadata.Absent,
            historyExists: false, applicationTablesExist: true);

        verdict.State.Should().Be(ServerSchemaState.E7);
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void Empty_database_is_E4_and_blocks()
    {
        var verdict = Evaluate([], [M1], ServerCompatibilityMetadata.Absent,
            historyExists: false, applicationTablesExist: false);

        verdict.State.Should().Be(ServerSchemaState.E4);
        verdict.CanStart.Should().BeFalse();
    }

    // ---- U-B7 : ligne en état d'initialisation ⇒ bloqué, avant C-1 … C-5 -------------------------------

    [Fact]
    public void Initialization_row_blocks_even_when_applied_equals_known()
    {
        var verdict = Evaluate([M1], [M1], Row(null, null, AncientMarker));

        verdict.State.Should().Be(ServerSchemaState.E5);
        verdict.InitializationIncomplete.Should().BeTrue();
        verdict.CanStart.Should().BeFalse();
    }

    [Fact]
    public void Initialization_row_blocks_even_without_maintenance_marker()
    {
        var verdict = Evaluate([M1], [M1], Row(null, null));

        verdict.InitializationIncomplete.Should().BeTrue();
        verdict.CanStart.Should().BeFalse();
    }

    // ---- U-B6 : aucune écriture — commandes interceptées -----------------------------------------------

    [Fact]
    public async Task Guard_reads_through_the_connection_and_emits_no_write_sql()
    {
        var connection = new ScriptedDbConnection(new ServerScript
        {
            HistoryExists = true,
            ApplicationTablesExist = true,
            Applied = [M1, M2, M3],
            MetadataTableExists = true,
            MetadataRow = ("1.3.0", "1.1.0", null)
        }.Respond);

        var verdict = await ServerSchemaCompatibilityGuard.EvaluateAsync(connection, [M1, M2], "1.2.0");

        verdict.State.Should().Be(ServerSchemaState.E3a);
        connection.Executed.Should().NotBeEmpty();
        connection.Executed.Should().OnlyContain(c => ServerScript.IsReadOnly(c.Text),
            "la garde est une fonction de lectures : zéro écriture, zéro DDL, zéro seed (DP-4.1, SB-3)");
    }

    [Fact]
    public async Task Guard_does_not_swallow_an_unreachable_server()
    {
        var connection = new ScriptedDbConnection(_ => new InvalidOperationException("serveur injoignable"));

        var act = () => ServerSchemaCompatibilityGuard.EvaluateAsync(connection, [M1], "1.0.0");

        await act.Should().ThrowAsync<InvalidOperationException>(); // E6 : jamais avalé (K-13)
    }

    [Fact]
    public async Task Guard_reports_E7_from_the_server_observation()
    {
        var connection = new ScriptedDbConnection(new ServerScript
        {
            HistoryExists = false,
            ApplicationTablesExist = true,
            MetadataTableExists = false
        }.Respond);

        var verdict = await ServerSchemaCompatibilityGuard.EvaluateAsync(connection, [M1], "1.0.0");

        verdict.State.Should().Be(ServerSchemaState.E7);
        connection.Executed.Should().NotContain(c => c.Text.Contains("\"MigrationId\""),
            "l'historique absent n'est pas interrogé");
    }
}

/// <summary>État serveur scripté pour <see cref="ScriptedDbConnection"/>, aligné sur le SQL de la garde.</summary>
internal sealed class ServerScript
{
    public bool HistoryExists { get; init; }
    public bool ApplicationTablesExist { get; init; }
    public string[] Applied { get; init; } = [];
    public bool MetadataTableExists { get; init; }
    public (string? Schema, string? Minimum, DateTime? Maintenance)? MetadataRow { get; init; }

    public object? Respond(ExecutedCommand command)
    {
        var text = command.Text;
        if (text.Contains("to_regclass"))
        {
            var relation = (string)command.Parameters.Values.Single()!;
            return relation.Contains("__EFMigrationsHistory") ? HistoryExists
                : relation.Contains(ServerCompatibilityMetadataNames.CompatibilityTable) ? MetadataTableExists
                : throw new InvalidOperationException($"Relation inattendue : {relation}");
        }

        if (text.Contains("pg_tables"))
        {
            return ApplicationTablesExist;
        }

        if (text.Contains("\"MigrationId\""))
        {
            var table = new DataTable();
            table.Columns.Add("MigrationId", typeof(string));
            foreach (var id in Applied)
            {
                table.Rows.Add(id);
            }

            return table;
        }

        if (text.Contains(ServerCompatibilityMetadataNames.CompatibilityTable))
        {
            return MetadataTable(MetadataRow);
        }

        throw new InvalidOperationException($"SQL inattendu : {text}");
    }

    public static DataTable MetadataTable((string? Schema, string? Minimum, DateTime? Maintenance)? row)
    {
        var table = new DataTable();
        table.Columns.Add("schema_version", typeof(string));
        table.Columns.Add("minimum_supported_version", typeof(string));
        table.Columns.Add("maintenance_started_at", typeof(DateTime));
        if (row is { } r)
        {
            table.Rows.Add((object?)r.Schema ?? DBNull.Value, (object?)r.Minimum ?? DBNull.Value,
                (object?)r.Maintenance ?? DBNull.Value);
        }

        return table;
    }

    /// <summary>Une commande est en lecture seule si elle est un SELECT sans verbe d'écriture ni de DDL.</summary>
    public static bool IsReadOnly(string sql)
    {
        var upper = sql.ToUpperInvariant();
        string[] writes = [@"\bINSERT\b", @"\bUPDATE\b", @"\bDELETE\b", @"\bMERGE\b", @"\bCREATE\b", @"\bALTER\b",
            @"\bDROP\b", @"\bTRUNCATE\b", @"\bGRANT\b", @"\bREVOKE\b", @"\bLOCK\b", @"\bPG_ADVISORY\w*",
            @"\bSET\b", @"\bSETVAL\b", @"\bNEXTVAL\b", @"\bCOPY\b", @"\bCALL\b", @"\bDO\b", @"\bINTO\b"];
        return upper.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
               && !writes.Any(w => System.Text.RegularExpressions.Regex.IsMatch(upper, w));
    }
}
