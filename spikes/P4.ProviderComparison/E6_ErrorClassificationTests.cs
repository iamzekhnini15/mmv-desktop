using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.P4.ProviderComparison.Support;
using Npgsql;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E6 — Classification des erreurs de persistance.
///
/// <c>PersistenceErrorMapper</c> n'est PAS modifié : il repose aujourd'hui sur
/// <c>SqliteException</c> et ses codes (P4-0 §16), donc aucune de ces erreurs serveur ne serait
/// reconnue. Cette expérimentation RELÈVE les faits nécessaires à une future classification.
/// </summary>
public class E6_ErrorClassificationTests
{
    private const string Experiment = "E6-error-classification";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Error_categories_are_identifiable(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e6");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        await ProbeAsync(database, name, "Unicité (index filtré LowStock)", async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                await using var connection = await database.OpenRawAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO \"Notifications\" (\"Type\",\"Title\",\"Message\",\"EntityId\",\"EntityType\",\"IsRead\",\"CreatedAt\",\"ResolvedAt\") " +
                    $"VALUES ('{NotificationTypes.LowStock}','t','m',777,'{NotificationEntityTypes.Product}',{FalseLiteral(database)},{TimestampLiteral(database)},NULL);";
                await command.ExecuteNonQueryAsync();
            }
        });

        await ProbeAsync(database, name, "Clé primaire dupliquée", async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                await using var connection = await database.OpenRawAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = database.Provider == ProviderKind.Postgres
                    ? "INSERT INTO \"ProductCategories\" (\"CategoryId\",\"Name\") VALUES (5150, 'PK-' || 5150);"
                    : "SET IDENTITY_INSERT [ProductCategories] ON; INSERT INTO [ProductCategories] ([CategoryId],[Name]) VALUES (5150, 'PK-5150'); SET IDENTITY_INSERT [ProductCategories] OFF;";
                await command.ExecuteNonQueryAsync();
            }
        });

        await ProbeAsync(database, name, "Clé étrangère inexistante", async () =>
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Orders\" (\"OrderNumber\",\"SaleId\",\"OrderDate\",\"Status\") " +
                $"VALUES ('FK-TEST', 999999, {TimestampLiteral(database)}, 'New');";
            await command.ExecuteNonQueryAsync();
        });

        await ProbeAsync(database, name, "Colonne NOT NULL violée", async () =>
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Notifications\" (\"Type\",\"Title\",\"Message\",\"IsRead\",\"CreatedAt\") " +
                $"VALUES (NULL,'t','m',{FalseLiteral(database)},{TimestampLiteral(database)});";
            await command.ExecuteNonQueryAsync();
        });

        await ProbeAsync(database, name, "Timeout de commande", async () =>
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 1;
            command.CommandText = database.Provider == ProviderKind.Postgres
                ? "SELECT pg_sleep(5);"
                : "WAITFOR DELAY '00:00:05';";
            await command.ExecuteNonQueryAsync();
        });

        await ProbeAsync(database, name, "Annulation par CancellationToken", async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = database.Provider == ProviderKind.Postgres
                ? "SELECT pg_sleep(5);"
                : "WAITFOR DELAY '00:00:05';";
            await command.ExecuteNonQueryAsync(cts.Token);
        });

        await ProbeAsync(database, name, "Connexion refusée (port fermé)", async () =>
        {
            if (database.Provider == ProviderKind.Postgres)
            {
                var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Port = 5999, Timeout = 3 };
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
            }
            else
            {
                var builder = new SqlConnectionStringBuilder(database.ConnectionString)
                { DataSource = "127.0.0.1,14999", ConnectTimeout = 3 };
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
            }
        });

        await ProbeAsync(database, name, "Base inexistante", async () =>
        {
            if (database.Provider == ProviderKind.Postgres)
            {
                var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = "mmv_p4_absente", Timeout = 5 };
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
            }
            else
            {
                var builder = new SqlConnectionStringBuilder(database.ConnectionString)
                { InitialCatalog = "mmv_p4_absente", ConnectTimeout = 10 };
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
            }
        });

        Assert.True(true);
    }

    private static async Task ProbeAsync(SpikeDatabase database, string provider, string label, Func<Task> action)
    {
        try
        {
            await action();
            SpikeLog.Write(Experiment, provider, $"{label} | AUCUNE ERREUR LEVÉE (résultat inattendu à consigner)");
        }
        catch (Exception ex)
        {
            SpikeLog.Write(Experiment, provider,
                $"{label} | {ex.GetType().Name} | {ErrorFacts.Describe(ex)} | catégorie candidate = {ErrorFacts.CandidateCategory(ex)}");
        }
    }

    private static string FalseLiteral(SpikeDatabase database)
        => database.Provider == ProviderKind.Postgres ? "false" : "0";

    private static string TimestampLiteral(SpikeDatabase database)
        => database.Provider == ProviderKind.Postgres
            ? "TIMESTAMP '2026-07-24 12:00:00'"
            : "'2026-07-24T12:00:00'";
}
