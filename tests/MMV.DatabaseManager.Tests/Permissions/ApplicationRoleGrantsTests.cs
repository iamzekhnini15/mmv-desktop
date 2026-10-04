using FluentAssertions;
using MMV.DatabaseManager.Permissions;
using MMV.Domain.Tests.TestDoubles;

namespace MMV.DatabaseManager.Tests.Permissions;

/// <summary>
/// P4-6B (Q-23 ; §4.5 du plan) — GRANT idempotents au rôle <c>--app-role</c>. U-C7, volet adverse : un nom
/// hostile n'est <b>jamais concaténé</b> côté client ; il est lié en paramètre et cité par le serveur
/// (<c>format('%I')</c>), et le texte exécuté est exactement celui que le serveur a produit.
/// </summary>
public sealed class ApplicationRoleGrantsTests
{
    [Theory]
    [InlineData("mmv_app")]
    [InlineData("x; DROP TABLE \"Customers\"; --")]
    [InlineData("rôle \"cité\"")]
    public async Task Grant_text_is_produced_by_the_server_with_the_role_as_bound_parameter(string role)
    {
        var serverProduced = new Queue<string>(["GRANT USAGE ON SCHEMA mmv_meta TO <cité>", "GRANT SELECT ON TABLE mmv_meta.schema_compatibility TO <cité>"]);
        var connection = new ScriptedDbConnection(c => c.Text.StartsWith("SELECT format(") ? serverProduced.Dequeue() : 0);
        connection.Open();

        await new ApplicationRoleGrants(connection).GrantAsync(role);

        connection.Executed.Should().HaveCount(4);
        connection.Executed.Where(c => c.Text.StartsWith("SELECT format(")).Should().HaveCount(2)
            .And.OnlyContain(c => Equals(c.Parameters["role"], role) && !c.Text.Contains(role));
        connection.Executed.Select(c => c.Text).Should().ContainInOrder(
            "GRANT USAGE ON SCHEMA mmv_meta TO <cité>",
            "GRANT SELECT ON TABLE mmv_meta.schema_compatibility TO <cité>");
    }

    [Fact]
    public async Task Grants_cover_only_the_metadata_never_the_journal()
    {
        var formats = new List<string>();
        var connection = new ScriptedDbConnection(c =>
        {
            if (c.Text.StartsWith("SELECT format("))
            {
                formats.Add(c.Text + " | " + string.Join(",", c.Parameters.Values));
                return "GRANT …";
            }

            return 0;
        });
        connection.Open();

        await new ApplicationRoleGrants(connection).GrantAsync("mmv_app");

        formats.Should().HaveCount(2);
        formats.Should().NotContain(f => f.Contains("migration_run"), "le rôle applicatif ne lit pas le journal (DP-5, DP-8)");
        formats.Should().Contain(f => f.Contains("GRANT USAGE ON SCHEMA"));
        formats.Should().Contain(f => f.Contains("GRANT SELECT ON TABLE") && f.Contains("schema_compatibility"));
        formats.Should().NotContain(f => f.Contains("ALL") || f.Contains("INSERT") || f.Contains("UPDATE"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Role_existence_is_a_parameterized_pg_roles_lookup(bool exists)
    {
        var connection = new ScriptedDbConnection(_ => exists);
        connection.Open();
        const string hostile = "x'; DROP ROLE postgres; --";

        var result = await new ApplicationRoleGrants(connection).RoleExistsAsync(hostile);

        result.Should().Be(exists);
        var command = connection.Executed.Single();
        command.Text.Should().Contain("pg_catalog.pg_roles").And.Contain("@role").And.NotContain(hostile);
        command.Parameters["role"].Should().Be(hostile);
    }

    [Fact]
    public async Task Blank_role_is_refused_before_any_command()
    {
        var connection = new ScriptedDbConnection(_ => 0);
        connection.Open();

        await new ApplicationRoleGrants(connection).Invoking(g => g.GrantAsync(" ")).Should().ThrowAsync<ArgumentException>();
        connection.Executed.Should().BeEmpty();
    }
}
