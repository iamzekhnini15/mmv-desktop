using Microsoft.EntityFrameworkCore;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E1b — Même modèle réel, mais avec le MINIMUM d'adaptations nécessaires pour que le provider
/// accepte le schéma. Le but est de MESURER la dette de portage : chaque adaptation appliquée est
/// un travail que P4-2 devra réaliser dans le modèle de production.
/// </summary>
public class E1b_AdaptedModelTests
{
    private const string Experiment = "E1b-adapted-model";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Adapted_model_creation_is_measured(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e1b");

        string outcome;
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.AsIs);
            await context.Database.EnsureCreatedAsync();
            outcome = "SUCCÈS";
        }
        catch (Exception ex)
        {
            outcome = "ÉCHEC";
            SpikeLog.Write(Experiment, name, $"Erreur : {E1_ModelCreationTests.Flatten(ex)}");
        }

        SpikeLog.Write(Experiment, name, $"EnsureCreated (modèle réel + adaptations minimales) : {outcome}");

        foreach (var adaptation in AdaptedOpticDbContext.AdaptationsFor(provider))
        {
            SpikeLog.Write(Experiment, name, $"ADAPTATION REQUISE : {adaptation}");
        }

        if (outcome == "SUCCÈS")
        {
            await DumpAsync(database, provider);
        }

        Assert.True(true);
    }

    private static async Task DumpAsync(SpikeDatabase database, ProviderKind provider)
    {
        var name = provider.ToString();

        var counts = provider == ProviderKind.Postgres
            ? "SELECT (SELECT count(*) FROM information_schema.tables WHERE table_schema='public') || ' tables, ' || " +
              "(SELECT count(*) FROM pg_indexes WHERE schemaname='public') || ' index, ' || " +
              "(SELECT count(*) FROM information_schema.table_constraints WHERE constraint_type='FOREIGN KEY' AND table_schema='public') || ' FK';"
            : "SELECT CONCAT((SELECT count(*) FROM sys.tables),' tables, ',(SELECT count(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)),' index, ',(SELECT count(*) FROM sys.foreign_keys),' FK');";
        SpikeLog.Write(Experiment, name, $"Schéma : {await database.ScalarAsync(counts)}");

        var money = provider == ProviderKind.Postgres
            ? "SELECT string_agg(column_name || '=' || data_type, ' ; ' ORDER BY column_name) FROM information_schema.columns " +
              "WHERE table_name='Sales' AND column_name IN ('TotalAmount','FinalAmount','RemainingAmount');"
            : "SELECT STRING_AGG(CONCAT(c.name,'=',t.name), ' ; ') FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id " +
              "WHERE c.object_id=OBJECT_ID('Sales') AND c.name IN ('TotalAmount','FinalAmount','RemainingAmount');";
        SpikeLog.Write(Experiment, name, $"Types physiques montants : {await database.ScalarAsync(money)}");

        var filters = provider == ProviderKind.Postgres
            ? "SELECT string_agg(indexdef, ' ;; ') FROM pg_indexes WHERE schemaname='public' AND indexname IN ('idx_notifications_active_low_stock_unique','idx_workshop_sheets_current_unique');"
            : "SELECT STRING_AGG(CONCAT(name,' filter=',ISNULL(filter_definition,'(aucun)')), ' ;; ') FROM sys.indexes WHERE name IN ('idx_notifications_active_low_stock_unique','idx_workshop_sheets_current_unique');";
        SpikeLog.Write(Experiment, name, $"Index filtrés réels : {await database.ScalarAsync(filters)}");

        var booleans = provider == ProviderKind.Postgres
            ? "SELECT string_agg(table_name || '.' || column_name || '=' || data_type, ' ; ' ORDER BY table_name) FROM information_schema.columns WHERE column_name IN ('IsCurrent','IsRead');"
            : "SELECT STRING_AGG(CONCAT(OBJECT_NAME(c.object_id),'.',c.name,'=',t.name), ' ; ') FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.name IN ('IsCurrent','IsRead');";
        SpikeLog.Write(Experiment, name, $"Booléens physiques : {await database.ScalarAsync(booleans)}");
    }
}
