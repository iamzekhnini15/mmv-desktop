using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Base PostgreSQL jetable (ADR-PROD-DB-008 §5.5, §5.6) : créée sous un nom unique, construite par la chaîne
/// de migrations de production, puis supprimée. Jamais <c>EnsureCreated</c> (ADR-PROD-DB-007 S5) : les
/// migrations sont l'objet même du test.
/// </summary>
public sealed class PostgreSqlDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    private PostgreSqlDatabase(string adminConnectionString, string name)
    {
        _adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = name }.ConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>Crée une base vide au nom unique, sans la migrer.</summary>
    public static async Task<PostgreSqlDatabase> CreateEmptyAsync()
    {
        var database = new PostgreSqlDatabase(
            PostgreSqlTestEnvironment.GetRequiredConnectionString(),
            "mmv_it_" + Guid.NewGuid().ToString("N"));
        await database.ExecuteAdminAsync($"CREATE DATABASE \"{database.Name}\"");
        return database;
    }

    /// <summary>Crée une base au nom unique et lui applique la chaîne de migrations PostgreSQL.</summary>
    public static async Task<PostgreSqlDatabase> CreateAsync()
    {
        var database = await CreateEmptyAsync();
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Contexte configuré par le chemin de PRODUCTION : <see cref="DatabaseProviderResolver.Configure"/>
    /// (provider Npgsql + assembly de migrations PostgreSQL).
    /// </summary>
    public OpticDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = ConnectionString
        });
        return new OpticDbContext(builder.Options);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
