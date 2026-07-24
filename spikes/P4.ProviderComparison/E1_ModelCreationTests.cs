using Microsoft.EntityFrameworkCore;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E1 — Connexion, version serveur, création du VRAI modèle EF (OpticDbContext), inspection
/// physique du schéma produit, puis suppression de la base jetable.
///
/// Ce test ne « valide » rien : il RELÈVE ce que chaque provider fait du modèle de production
/// tel qu'il existe aujourd'hui. Un échec de création est un RÉSULTAT, pas un défaut du harness.
/// </summary>
public class E1_ModelCreationTests
{
    private const string Experiment = "E1-model-creation";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Real_model_creation_is_observed(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        SpikeLog.Section(Experiment, provider.ToString(), "Configuration");
        SpikeLog.Write(Experiment, provider.ToString(), SpikeEnvironment.Describe(provider));

        await using var database = await SpikeDatabase.CreateAsync(provider, "e1");

        var versionSql = provider == ProviderKind.Postgres
            ? "SELECT version();"
            : "SELECT CONCAT(CAST(SERVERPROPERTY('Edition') AS nvarchar(100)), ' | EngineEdition=', CAST(SERVERPROPERTY('EngineEdition') AS nvarchar(10)), ' | ', CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)));";

        var version = await database.ScalarAsync(versionSql);
        SpikeLog.Write(Experiment, provider.ToString(), $"Version serveur relevée : {version}");
        SpikeLog.Write(Experiment, provider.ToString(), $"Base jetable : {database.DatabaseName}");

        var started = DateTimeOffset.UtcNow;
        string outcome;
        try
        {
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            outcome = "SUCCÈS";
        }
        catch (Exception ex)
        {
            outcome = $"ÉCHEC : {ex.GetType().FullName}";
            SpikeLog.Write(Experiment, provider.ToString(), $"Message : {Flatten(ex)}");
        }

        var elapsed = DateTimeOffset.UtcNow - started;
        SpikeLog.Write(Experiment, provider.ToString(), $"EnsureCreated (modèle RÉEL OpticDbContext) : {outcome} en {elapsed.TotalSeconds:F1}s");

        if (outcome.StartsWith("SUCCÈS", StringComparison.Ordinal))
        {
            await DumpSchemaAsync(database, provider);
        }

        // Le test ne prononce aucun verdict : la preuve est le journal.
        Assert.True(true);
    }

    private static async Task DumpSchemaAsync(SpikeDatabase database, ProviderKind provider)
    {
        var name = provider.ToString();

        var tableSql = provider == ProviderKind.Postgres
            ? "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';"
            : "SELECT count(*) FROM sys.tables;";
        SpikeLog.Write(Experiment, name, $"Tables créées : {await database.ScalarAsync(tableSql)}");

        var indexSql = provider == ProviderKind.Postgres
            ? "SELECT count(*) FROM pg_indexes WHERE schemaname='public';"
            : "SELECT count(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables);";
        SpikeLog.Write(Experiment, name, $"Index créés : {await database.ScalarAsync(indexSql)}");

        var fkSql = provider == ProviderKind.Postgres
            ? "SELECT count(*) FROM information_schema.table_constraints WHERE constraint_type='FOREIGN KEY' AND table_schema='public';"
            : "SELECT count(*) FROM sys.foreign_keys;";
        SpikeLog.Write(Experiment, name, $"Clés étrangères : {await database.ScalarAsync(fkSql)}");

        // Type physique réellement retenu pour les montants (HasColumnType("REAL") côté modèle).
        var moneySql = provider == ProviderKind.Postgres
            ? "SELECT string_agg(column_name || '=' || data_type || COALESCE('(' || numeric_precision || ',' || numeric_scale || ')',''), ' ; ' ORDER BY column_name) " +
              "FROM information_schema.columns WHERE table_name='Sales' AND column_name IN ('TotalAmount','FinalAmount','DiscountAmount','DepositAmount','RemainingAmount');"
            : "SELECT STRING_AGG(CONCAT(c.name,'=',t.name,'(',c.precision,',',c.scale,')'), ' ; ') " +
              "FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id " +
              "WHERE c.object_id=OBJECT_ID('Sales') AND c.name IN ('TotalAmount','FinalAmount','DiscountAmount','DepositAmount','RemainingAmount');";
        SpikeLog.Write(Experiment, name, $"Types physiques des montants (Sales) : {await database.ScalarAsync(moneySql)}");

        // DDL réel des deux index filtrés identifiés par P4-0.
        if (provider == ProviderKind.Postgres)
        {
            var ddl = await database.ScalarAsync(
                "SELECT string_agg(indexdef, ' ;; ') FROM pg_indexes WHERE schemaname='public' " +
                "AND indexname IN ('idx_notifications_active_low_stock_unique','idx_workshop_sheets_current_unique');");
            SpikeLog.Write(Experiment, name, $"DDL index filtrés : {ddl}");
        }
        else
        {
            var ddl = await database.ScalarAsync(
                "SELECT STRING_AGG(CONCAT(i.name,' filter=',ISNULL(i.filter_definition,'(aucun)')), ' ;; ') " +
                "FROM sys.indexes i WHERE i.name IN ('idx_notifications_active_low_stock_unique','idx_workshop_sheets_current_unique');");
            SpikeLog.Write(Experiment, name, $"DDL index filtrés : {ddl}");
        }
    }

    internal static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message.Replace(Environment.NewLine, " ")}");
        }

        return string.Join(" || ", parts);
    }
}
