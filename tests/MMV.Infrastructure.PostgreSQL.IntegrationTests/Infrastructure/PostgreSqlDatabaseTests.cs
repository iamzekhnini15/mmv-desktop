using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Socle F0 : base jetable créée par MIGRATIONS (ADR-PROD-DB-008 §5.5, §5.6 ; Q5).
/// </summary>
public class PostgreSqlDatabaseTests
{
    [PostgreSqlFact]
    public async Task Fresh_database_is_built_by_the_PostgreSQL_migration_chain_only()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var context = database.CreateContext();

        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        (await context.Database.GetAppliedMigrationsAsync())
            .Should().Equal("20260922001219_InitialPostgreSqlBaseline");
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [PostgreSqlFact]
    public async Task Database_is_dropped_on_dispose()
    {
        string name;
        await using (var database = await PostgreSqlDatabase.CreateAsync())
        {
            name = database.Name;
        }

        await using var admin = new NpgsqlConnection(PostgreSqlTestEnvironment.GetRequiredConnectionString());
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @n", admin);
        command.Parameters.AddWithValue("n", name);
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }
}
