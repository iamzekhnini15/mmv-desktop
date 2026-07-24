using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Base JETABLE dédiée au spike. Chaque expérimentation crée sa propre base, l'exerce, puis la
/// supprime. Aucune base utilisateur n'est jamais touchée : le nom est toujours préfixé
/// <c>mmv_p4_</c> et généré aléatoirement.
/// </summary>
public sealed class SpikeDatabase : IAsyncDisposable
{
    private const string NamePrefix = "mmv_p4_";

    private SpikeDatabase(ProviderKind provider, string databaseName, string connectionString, string adminConnectionString)
    {
        Provider = provider;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        AdminConnectionString = adminConnectionString;
    }

    public ProviderKind Provider { get; }

    public string DatabaseName { get; }

    /// <summary>Chaîne vers la base jetable. Ne jamais journaliser.</summary>
    public string ConnectionString { get; }

    /// <summary>Chaîne vers la base d'administration (postgres/master). Ne jamais journaliser.</summary>
    public string AdminConnectionString { get; }

    public static async Task<SpikeDatabase> CreateAsync(ProviderKind provider, string label, CancellationToken ct = default)
    {
        var baseConnection = SpikeEnvironment.For(provider)
            ?? throw new InvalidOperationException(SpikeEnvironment.SkipReason(provider));

        var name = NamePrefix + Sanitize(label) + "_" + Guid.NewGuid().ToString("n")[..8];

        if (provider == ProviderKind.Postgres)
        {
            var admin = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" }.ConnectionString;
            var target = new NpgsqlConnectionStringBuilder(baseConnection) { Database = name }.ConnectionString;

            await using (var connection = new NpgsqlConnection(admin))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{name}\";";
                await command.ExecuteNonQueryAsync(ct);
            }

            return new SpikeDatabase(provider, name, target, admin);
        }
        else
        {
            var admin = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString;
            var target = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = name }.ConnectionString;

            await using (var connection = new SqlConnection(admin))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{name}];";
                await command.ExecuteNonQueryAsync(ct);
            }

            return new SpikeDatabase(provider, name, target, admin);
        }
    }

    /// <summary>
    /// Construit le VRAI <see cref="OpticDbContext"/> (modèle de production, non modifié) sur le
    /// provider du spike. <c>OnConfiguring</c> ne retombe sur SQLite que si les options ne sont pas
    /// déjà configurées : en les fournissant ici, le modèle réel est exercé tel quel.
    /// </summary>
    public OpticDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();

        if (Provider == ProviderKind.Postgres)
        {
            builder.UseNpgsql(ConnectionString);
        }
        else
        {
            builder.UseSqlServer(ConnectionString);
        }

        return new OpticDbContext(builder.Options);
    }

    public async Task<System.Data.Common.DbConnection> OpenRawAsync(CancellationToken ct = default)
    {
        System.Data.Common.DbConnection connection = Provider == ProviderKind.Postgres
            ? new NpgsqlConnection(ConnectionString)
            : new SqlConnection(ConnectionString);

        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<string> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = await OpenRawAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(ct);
        return value?.ToString() ?? "(null)";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Provider == ProviderKind.Postgres)
            {
                NpgsqlConnection.ClearAllPools();
                await using var connection = new NpgsqlConnection(AdminConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);";
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                SqlConnection.ClearAllPools();
                await using var connection = new SqlConnection(AdminConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN " +
                    $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                    $"DROP DATABASE [{DatabaseName}]; END";
                await command.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            // Le nettoyage ne doit jamais masquer le résultat de l'expérimentation, mais il doit
            // rester visible dans la sortie de test.
            Console.WriteLine($"[SPIKE CLEANUP] Échec de suppression de {DatabaseName} : {ex.GetType().Name}");
        }
    }

    private static string Sanitize(string label)
    {
        var cleaned = new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return cleaned.Length == 0 ? "spike" : cleaned[..Math.Min(cleaned.Length, 12)];
    }
}
