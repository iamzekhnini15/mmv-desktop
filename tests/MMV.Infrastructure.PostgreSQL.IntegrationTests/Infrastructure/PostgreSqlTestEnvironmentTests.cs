using FluentAssertions;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Contrat de connexion des tests d'intégration (ADR-PROD-DB-008 §5.2, §5.4 ; Q2, Q4). Ces tests ne
/// requièrent aucun serveur : ils s'exécutent partout, y compris en local sans PostgreSQL.
/// </summary>
public class PostgreSqlTestEnvironmentTests
{
    [Fact]
    public void Test_variable_is_distinct_from_production_variable()
    {
        PostgreSqlTestEnvironment.ConnectionStringVariableName
            .Should().Be("MMV_TEST_POSTGRESQL_CONNECTION_STRING")
            .And.NotBe(MMV.Infrastructure.Configuration.DatabaseProviderResolver.ConnectionStringVariableName);
    }

    [Fact]
    public void Without_server_locally_tests_are_skipped_with_an_actionable_message()
    {
        var reason = PostgreSqlTestEnvironment.SkipReason(new Dictionary<string, string?>());

        reason.Should().NotBeNull();
        reason.Should().Contain("NON PROUVÉ")
            .And.Contain("MMV_TEST_POSTGRESQL_CONNECTION_STRING");
    }

    [Fact]
    public void Without_server_in_CI_tests_are_never_skipped()
    {
        var reason = PostgreSqlTestEnvironment.SkipReason(new Dictionary<string, string?>
        {
            ["MMV_INTEGRATION_REQUIRED"] = "true"
        });

        // Pas de skip : le test s'exécute et échoue faute de serveur (un skip en CI est un échec, §5.4).
        reason.Should().BeNull();
    }

    [Fact]
    public void With_server_tests_run()
    {
        var reason = PostgreSqlTestEnvironment.SkipReason(new Dictionary<string, string?>
        {
            ["MMV_TEST_POSTGRESQL_CONNECTION_STRING"] = "Host=localhost;Username=u;Password=p"
        });

        reason.Should().BeNull();
    }

    [Fact]
    public void Missing_server_when_required_fails_with_explicit_message()
    {
        var act = () => PostgreSqlTestEnvironment.GetRequiredConnectionString(new Dictionary<string, string?>
        {
            ["MMV_INTEGRATION_REQUIRED"] = "true"
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MMV_TEST_POSTGRESQL_CONNECTION_STRING*");
    }
}
