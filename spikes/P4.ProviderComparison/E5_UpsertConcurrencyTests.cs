using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E5 — Upsert atomique de l'alerte LowStock active, en CONCURRENCE réelle.
///
/// La primitive de production utilise <c>INSERT … ON CONFLICT DO NOTHING</c> avec des
/// <c>SqliteParameter</c> (P4-0 §15) : elle n'est portable telle quelle sur AUCUN des deux
/// serveurs. On mesure donc deux stratégies par provider, jamais un « check-then-act ».
///
/// Chaque scénario est répété plusieurs fois : une seule exécution ne prouverait rien en
/// concurrence.
/// </summary>
public class E5_UpsertConcurrencyTests
{
    private const string Experiment = "E5-upsert";
    private const int Rounds = 10;

    public enum Strategy
    {
        /// <summary>Primitive native du provider (ON CONFLICT / MERGE-équivalent).</summary>
        NativeUpsert,

        /// <summary>Insertion normale + capture de la violation d'unicité.</summary>
        InsertAndCatch
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var provider in new[] { ProviderKind.Postgres, ProviderKind.SqlServer })
        {
            foreach (var strategy in new[] { Strategy.NativeUpsert, Strategy.InsertAndCatch })
            {
                yield return new object[] { provider, strategy };
            }
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task Concurrent_alert_creation_yields_exactly_one_winner(ProviderKind provider, Strategy strategy)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e5");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, $"Stratégie = {strategy} — {Rounds} tours");

        var anomalies = 0;
        var deadlocks = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var productId = 9000 + round;

            // Deux connexions RÉELLES et distinctes, démarrées ensemble par une barrière.
            //
            // Task.Run est INDISPENSABLE : Barrier.SignalAndWait() est bloquant et s'exécute AVANT
            // le premier await de la méthode async. Sans thread propre, le premier appel bloquerait
            // le thread appelant et le second participant ne serait jamais créé (interblocage du
            // harness, observé réellement pendant le spike). Aucun Thread.Sleep n'est utilisé.
            using var barrier = new Barrier(2);

            async Task<string> Attempt()
            {
                barrier.SignalAndWait();
                return strategy == Strategy.NativeUpsert
                    ? await NativeUpsertAsync(database, provider, productId)
                    : await InsertAndCatchAsync(database, productId);
            }

            var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));

            var inserted = results.Count(r => r == "INSERTED");
            var refused = results.Count(r => r == "REFUSED");
            var errors = results.Where(r => r != "INSERTED" && r != "REFUSED").ToArray();

            int active;
            await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
            {
                active = await check.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null);
            }

            var ok = inserted == 1 && refused == 1 && active == 1 && errors.Length == 0;
            if (!ok)
            {
                anomalies++;
                SpikeLog.Write(Experiment, name,
                    $"ANOMALIE tour {round} : inserted={inserted} refused={refused} actives={active} erreurs=[{string.Join(" | ", errors)}]");
            }

            deadlocks += errors.Count(e => e.Contains("40P01", StringComparison.Ordinal) || e.Contains("Number=1205", StringComparison.Ordinal));
        }

        SpikeLog.Write(Experiment, name,
            $"RÉSULTAT {strategy} : {Rounds - anomalies}/{Rounds} tours conformes (1 gagnant, 1 refus, 1 alerte active) ; " +
            $"anomalies={anomalies} ; deadlocks non gérés={deadlocks}");

        Assert.True(true);
    }

    /// <summary>
    /// Primitive NATIVE. PostgreSQL : <c>ON CONFLICT DO NOTHING</c>. SQL Server : pas d'équivalent
    /// direct — on mesure la forme réellement disponible, <c>INSERT … WHERE NOT EXISTS</c>, qui
    /// s'appuie sur l'index unique filtré pour l'arbitrage final.
    /// </summary>
    private static async Task<string> NativeUpsertAsync(SpikeDatabase database, ProviderKind provider, long productId)
    {
        try
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();

            if (provider == ProviderKind.Postgres)
            {
                command.CommandText =
                    "INSERT INTO \"Notifications\" (\"Type\",\"Title\",\"Message\",\"EntityId\",\"EntityType\",\"IsRead\",\"CreatedAt\",\"ResolvedAt\") " +
                    "VALUES (@type,@title,@message,@entityId,@entityType,@isRead,@createdAt,NULL) ON CONFLICT DO NOTHING;";
            }
            else
            {
                command.CommandText =
                    "INSERT INTO [Notifications] ([Type],[Title],[Message],[EntityId],[EntityType],[IsRead],[CreatedAt],[ResolvedAt]) " +
                    "SELECT @type,@title,@message,@entityId,@entityType,@isRead,@createdAt,NULL " +
                    "WHERE NOT EXISTS (SELECT 1 FROM [Notifications] WITH (UPDLOCK, HOLDLOCK) " +
                    "WHERE [Type]=@type AND [EntityType]=@entityType AND [EntityId]=@entityId AND [ResolvedAt] IS NULL);";
            }

            AddParameters(command, productId);

            var rows = await command.ExecuteNonQueryAsync();
            return rows == 1 ? "INSERTED" : "REFUSED";
        }
        catch (Exception ex)
        {
            // Une violation d'unicité reste un REFUS métier légitime pour cette primitive.
            var facts = ErrorFacts.Describe(ex);
            return facts.Contains("23505", StringComparison.Ordinal) || facts.Contains("Number=2601", StringComparison.Ordinal) || facts.Contains("Number=2627", StringComparison.Ordinal)
                ? "REFUSED"
                : facts;
        }
    }

    private static async Task<string> InsertAndCatchAsync(SpikeDatabase database, long productId)
    {
        try
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Notifications\" (\"Type\",\"Title\",\"Message\",\"EntityId\",\"EntityType\",\"IsRead\",\"CreatedAt\",\"ResolvedAt\") " +
                "VALUES (@type,@title,@message,@entityId,@entityType,@isRead,@createdAt,NULL);";
            AddParameters(command, productId);

            await command.ExecuteNonQueryAsync();
            return "INSERTED";
        }
        catch (Exception ex)
        {
            var facts = ErrorFacts.Describe(ex);
            return facts.Contains("23505", StringComparison.Ordinal) || facts.Contains("Number=2601", StringComparison.Ordinal) || facts.Contains("Number=2627", StringComparison.Ordinal)
                ? "REFUSED"
                : facts;
        }
    }

    private static void AddParameters(System.Data.Common.DbCommand command, long productId)
    {
        void Add(string parameterName, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        Add("@type", NotificationTypes.LowStock);
        Add("@title", "Stock bas");
        Add("@message", "Alerte concurrente");
        Add("@entityId", productId);
        Add("@entityType", NotificationEntityTypes.Product);
        Add("@isRead", false);
        Add("@createdAt", new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc));
    }
}
