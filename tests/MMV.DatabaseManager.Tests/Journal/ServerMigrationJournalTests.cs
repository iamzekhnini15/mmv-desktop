using System.Data;
using System.Text.Json;
using FluentAssertions;
using MMV.DatabaseManager.Journal;
using MMV.Domain.Tests.TestDoubles;

namespace MMV.DatabaseManager.Tests.Journal;

/// <summary>
/// P4-6B — format du journal serveur (DP-8, H8 ; §4.2 du plan). U-C5 : les champs minimaux de DP-8 sont tous
/// présents et non nuls à la clôture ; horodatages et rôle viennent du <b>serveur</b> (<c>now()</c>,
/// <c>current_user</c>), jamais du poste. Sans serveur, sur connexion scriptée.
/// </summary>
public sealed class ServerMigrationJournalTests
{
    private static readonly Guid RunId = Guid.Parse("6d1f8a7e-2b1c-4e35-9a0e-1f2a3b4c5d6e");

    private static (ServerMigrationJournal Journal, ScriptedDbConnection Connection) Journal()
    {
        var connection = new ScriptedDbConnection(_ => 1);
        connection.Open();
        return (new ServerMigrationJournal(connection), connection);
    }

    private static ServerMigrationRun Run() =>
        new(RunId, "1.1.0", ["B"], "OP-42", MigrationRunKind.Migrate, "dump-2026-10-04");

    [Fact]
    public async Task Open_and_close_write_every_DP8_minimal_field_with_server_clock_and_role()
    {
        var (journal, connection) = Journal();

        await journal.OpenAsync(Run());
        await journal.CloseAsync(RunId, MigrationOutcome.Success, null, ["B", "M1"]);

        connection.Executed.Should().HaveCount(2);
        var open = connection.Executed[0];
        var close = connection.Executed[1];

        open.Text.Should().StartWith("INSERT INTO mmv_meta.migration_run");
        open.Text.Should().Contain("now()").And.Contain("current_user").And.Contain("'open'");
        open.Parameters.Should().Contain(new KeyValuePair<string, object?>("run_id", RunId));
        open.Parameters["app_version"].Should().Be("1.1.0");                       // version applicative
        JsonSerializer.Deserialize<string[]>((string)open.Parameters["applied_before"]!)
            .Should().Equal("B");                                                   // migrations avant
        open.Parameters["operator_reference"].Should().Be("OP-42");                // opérateur
        open.Parameters["backup_reference"].Should().Be("dump-2026-10-04");        // sauvegarde
        open.Parameters["run_kind"].Should().Be("migrate");

        close.Text.Should().StartWith("UPDATE mmv_meta.migration_run");
        close.Text.Should().Contain("finished_at = now()");                         // date et heure
        close.Parameters["run_id"].Should().Be(RunId);
        close.Parameters["outcome"].Should().Be("success");                        // résultat
        JsonSerializer.Deserialize<string[]>((string)close.Parameters["applied_after"]!)
            .Should().Equal("B", "M1");                                             // migrations après
        close.Parameters["failure_cause"].Should().Be(DBNull.Value);

        open.Parameters.Values.Concat(close.Parameters.Values.Where(v => v is not DBNull))
            .Should().NotContainNulls();
    }

    [Fact]
    public async Task Failure_close_records_its_cause()
    {
        var (journal, connection) = Journal();

        await journal.CloseAsync(RunId, MigrationOutcome.Failure, "42P07: relation exists", ["B"]);

        connection.Executed.Single().Parameters["outcome"].Should().Be("failure");
        connection.Executed.Single().Parameters["failure_cause"].Should().Be("42P07: relation exists");
    }

    [Fact]
    public async Task Close_refuses_open_outcome_and_failure_without_cause()
    {
        var (journal, connection) = Journal();

        await journal.Invoking(j => j.CloseAsync(RunId, MigrationOutcome.Open, null, ["B"]))
            .Should().ThrowAsync<ArgumentException>();
        await journal.Invoking(j => j.CloseAsync(RunId, MigrationOutcome.Failure, " ", ["B"]))
            .Should().ThrowAsync<ArgumentException>();
        connection.Executed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "dump", "1.0.0")]
    [InlineData("OP", "", "1.0.0")]
    [InlineData("OP", "dump", "")]
    public async Task Open_refuses_missing_fields(string @operator, string backup, string version)
    {
        var (journal, connection) = Journal();
        var run = new ServerMigrationRun(RunId, version, [], @operator, MigrationRunKind.Migrate, backup);

        await journal.Invoking(j => j.OpenAsync(run)).Should().ThrowAsync<ArgumentException>();
        connection.Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task Runs_of_every_outcome_except_the_current_one_are_read_in_journal_order()
    {
        var table = new DataTable();
        table.Columns.Add("app_version", typeof(string));
        table.Columns.Add("run_kind", typeof(string));
        table.Columns.Add("outcome", typeof(string));
        table.Columns.Add("applied_before", typeof(string));
        table.Columns.Add("applied_after", typeof(string));
        table.Rows.Add("1.0.0", "migrate", "success", "[]", "[\"B\"]");
        table.Rows.Add("1.1.0", "adopt", "success", "[\"B\"]", "[\"B\"]");
        table.Rows.Add("1.2.0", "migrate", "failure", "[\"B\"]", "[\"B\",\"M1\"]");
        table.Rows.Add("1.3.0", "migrate", "open", "[\"B\",\"M1\"]", null);
        var connection = new ScriptedDbConnection(_ => table);
        connection.Open();

        var runs = await new ServerMigrationJournal(connection).ReadRunsAsync(RunId);

        var command = connection.Executed.Single();
        command.Text.Should().NotContain("outcome =", "échecs et plantages désignent aussi la release productrice")
            .And.Contain("run_id <> @current_run").And.Contain("ORDER BY started_at");
        command.Parameters["current_run"].Should().Be(RunId);
        runs.Should().BeEquivalentTo(new[]
        {
            new MigrationRunRecord("1.0.0", MigrationRunKind.Migrate, MigrationOutcome.Success, [], ["B"]),
            new MigrationRunRecord("1.1.0", MigrationRunKind.Adopt, MigrationOutcome.Success, ["B"], ["B"]),
            new MigrationRunRecord("1.2.0", MigrationRunKind.Migrate, MigrationOutcome.Failure, ["B"], ["B", "M1"]),
            new MigrationRunRecord("1.3.0", MigrationRunKind.Migrate, MigrationOutcome.Open, ["B", "M1"], null)
        }, o => o.WithStrictOrdering());
    }
}
