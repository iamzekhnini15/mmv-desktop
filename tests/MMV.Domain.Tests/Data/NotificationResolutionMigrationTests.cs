using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-8 — Migration <c>AddNotificationResolution</c> : additive, dédoublonnante et physiquement conforme au modèle.
///
/// <para>
/// Exécutée sur de <b>vraies bases SQLite jetables</b> via le pipeline de migrations réel, jamais le provider
/// InMemory : c'est le schéma effectivement produit — et le comportement effectif du dédoublonnage SQL — qui est
/// prouvé, pas seulement le modèle EF.
/// </para>
/// </summary>
public sealed class NotificationResolutionMigrationTests : IDisposable
{
    private readonly string _workDirectory;

    public NotificationResolutionMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p38-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private const string NotificationsTable = "Notifications";
    private const string IndexName = "idx_notifications_active_low_stock_unique";
    private const string MigrationSuffix = "_AddNotificationResolution";

    private OpticDbContext CreateContext(string fileName = "migrate.db")
    {
        var path = Path.Combine(_workDirectory, fileName);
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static void Execute(OpticDbContext context, string sql)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open) command.Connection.Open();
        command.ExecuteNonQuery();
    }

    private static List<string> Query(OpticDbContext context, string sql)
    {
        var values = new List<string>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open) command.Connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    private static List<string> Columns(OpticDbContext context, string table)
        => Query(context, $"SELECT name FROM pragma_table_info('{table}');");

    private static string PreviousMigration(OpticDbContext context)
    {
        var migrations = context.Database.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var index = migrations.FindIndex(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
        index.Should().BeGreaterThan(0, "la migration P3-8 doit exister et ne pas être la première");
        return migrations[index - 1];
    }

    // =========================================================================================================
    // Base neuve
    // =========================================================================================================

    [Fact]
    public void Migrate_DepuisZero_AjouteLaColonneResolvedAt()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Columns(context, NotificationsTable).Should().Contain("ResolvedAt");
    }

    [Fact]
    public void Migrate_DepuisZero_CreeLIndexUniqueFiltre()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var definition = Query(context,
            $"SELECT sql FROM sqlite_master WHERE type='index' AND name='{IndexName}';");

        definition.Should().ContainSingle();
        definition[0].Should().Contain("UNIQUE");
        definition[0].Should().Contain("WHERE");

        // Les quatre termes du filtre sont indispensables : chacun protège un cas précis.
        definition[0].Should().Contain("LowStock", "le filtre doit restreindre le TYPE, sinon il bloquerait les transitions de commande");
        definition[0].Should().Contain("Product");
        definition[0].Should().Contain("\"EntityId\" IS NOT NULL");
        definition[0].Should().Contain("\"ResolvedAt\" IS NULL");
    }

    [Fact]
    public void Migrate_DepuisZero_LaMigrationEstInscriteDansLHistorique()
    {
        using var context = CreateContext("history.db");
        context.Database.Migrate();

        context.Database.GetAppliedMigrations()
            .Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void Migrate_DepuisZero_LeModeleEstAligne()
    {
        using var context = CreateContext("aligned.db");
        context.Database.Migrate();

        new SqliteSchemaVerifier().Verify(context).Differences.Should().BeEmpty();
    }

    [Fact]
    public void MigrationP38_NeToucheQueLaTableDesNotifications()
    {
        using var context = CreateContext("sqlscript.db");

        var previous = PreviousMigration(context);
        var migrations = context.Database.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var target = migrations[migrations.FindIndex(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal))];

        var script = context.Database.GetService<IMigrator>().GenerateScript(previous, target);

        script.Should().Contain(NotificationsTable);

        foreach (var protectedTable in new[]
                 { "Customers", "Products", "Sales", "SaleItems", "Orders", "OrderItems", "Prescriptions", "StockMovements", "WorkshopSheets" })
        {
            script.Should().NotContain($"ALTER TABLE \"{protectedTable}\"");
            script.Should().NotContain($"DROP TABLE \"{protectedTable}\"");
            script.Should().NotContain($"UPDATE \"{protectedTable}\"");
            script.Should().NotContain($"INSERT INTO \"{protectedTable}\"");
        }
    }

    // =========================================================================================================
    // Adoption depuis l'état précédent — dédoublonnage déterministe
    // =========================================================================================================

    /// <summary>
    /// Amène une base au niveau <b>précédant</b> P3-8, puis y insère un jeu de notifications héritées représentatif
    /// des doublons que le défaut corrigé produisait réellement.
    /// </summary>
    /// <remarks>
    /// Jeu inséré (identifiants explicites pour rendre le départage observable) :
    /// <list type="bullet">
    ///   <item><b>Produit 10</b> — deux alertes LUES (id 1, 2) et une alerte NON LUE (id 3, donc la <i>plus grande</i>).
    ///   Le gardien doit être l'id 3 : la priorité au non-lu l'emporte sur l'ancienneté.</item>
    ///   <item><b>Produit 20</b> — deux alertes LUES (id 4, 5), aucune non lue. Départage par le plus petit
    ///   identifiant : le gardien doit être l'id 4.</item>
    ///   <item>Trois <c>OrderStatusChanged</c> sur la même commande, et deux <c>Info</c> sans <c>EntityId</c> —
    ///   aucun ne doit être touché.</item>
    /// </list>
    /// </remarks>
    private OpticDbContext ArrangeLegacyDatabase(string fileName)
    {
        var context = CreateContext(fileName);
        context.Database.GetService<IMigrator>().Migrate(PreviousMigration(context));

        // À ce niveau, la colonne ResolvedAt n'existe pas encore.
        Columns(context, NotificationsTable).Should().NotContain("ResolvedAt");

        Execute(context,
            "INSERT INTO \"Notifications\" " +
            "(\"NotificationId\", \"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\") VALUES " +
            "(1, 'LowStock', 'Stock bas', 'm', 10, 'Product', 1, '2026-01-01 10:00:00'), " +
            "(2, 'LowStock', 'Stock bas', 'm', 10, 'Product', 1, '2026-01-02 10:00:00'), " +
            "(3, 'LowStock', 'Stock bas', 'm', 10, 'Product', 0, '2026-01-03 10:00:00'), " +
            "(4, 'LowStock', 'Stock bas', 'm', 20, 'Product', 1, '2026-01-04 10:00:00'), " +
            "(5, 'LowStock', 'Stock bas', 'm', 20, 'Product', 1, '2026-01-05 10:00:00'), " +
            "(6, 'OrderStatusChanged', 'Transition', 'm', 30, 'Order', 0, '2026-01-06 10:00:00'), " +
            "(7, 'OrderStatusChanged', 'Transition', 'm', 30, 'Order', 0, '2026-01-07 10:00:00'), " +
            "(8, 'OrderStatusChanged', 'Transition', 'm', 30, 'Order', 1, '2026-01-08 10:00:00'), " +
            "(9, 'Info', 'Information', 'm', NULL, NULL, 0, '2026-01-09 10:00:00'), " +
            "(10, 'Info', 'Information', 'm', NULL, NULL, 0, '2026-01-10 10:00:00');");

        return context;
    }

    private static List<(long Id, long EntityId, bool IsRead, string CreatedAt, string? ResolvedAt)> ReadLowStock(OpticDbContext context)
    {
        var rows = new List<(long, long, bool, string, string?)>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT \"NotificationId\", \"EntityId\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\" " +
            "FROM \"Notifications\" WHERE \"Type\" = 'LowStock' ORDER BY \"NotificationId\";";
        if (command.Connection!.State != System.Data.ConnectionState.Open) command.Connection.Open();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2) != 0,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    [Fact]
    public void Adoption_UneSeuleAlerteResteActiveParProduit()
    {
        using var context = ArrangeLegacyDatabase("adopt-single.db");
        context.Database.Migrate();

        var lowStock = ReadLowStock(context);

        lowStock.Where(r => r.EntityId == 10).Count(r => r.ResolvedAt == null).Should().Be(1);
        lowStock.Where(r => r.EntityId == 20).Count(r => r.ResolvedAt == null).Should().Be(1);
    }

    [Fact]
    public void Adoption_LAlerteConservee_EstCelleQuiNaPasEteLue()
    {
        using var context = ArrangeLegacyDatabase("adopt-unread.db");
        context.Database.Migrate();

        var keeper = ReadLowStock(context).Single(r => r.EntityId == 10 && r.ResolvedAt == null);

        keeper.Id.Should().Be(3);
        keeper.IsRead.Should().BeFalse(
            "fermer la seule alerte jamais vue au profit d'une alerte déjà consultée effacerait l'information du badge");
    }

    [Fact]
    public void Adoption_ADefautDeNonLue_LaPlusPetiteNotificationIdEstConservee()
    {
        using var context = ArrangeLegacyDatabase("adopt-tie.db");
        context.Database.Migrate();

        ReadLowStock(context).Single(r => r.EntityId == 20 && r.ResolvedAt == null).Id.Should().Be(4);
    }

    [Fact]
    public void Adoption_LesDoublonsFermes_PortentLeurPropreCreatedAt()
    {
        using var context = ArrangeLegacyDatabase("adopt-dates.db");
        context.Database.Migrate();

        var closed = ReadLowStock(context).Where(r => r.ResolvedAt != null).ToList();

        closed.Should().HaveCount(3, "ids 1, 2 (produit 10) et 5 (produit 20)");
        closed.Select(r => r.Id).Should().BeEquivalentTo(new long[] { 1, 2, 5 });

        foreach (var row in closed)
        {
            row.ResolvedAt.Should().Be(row.CreatedAt,
                "fermeture technique d'un doublon : jamais l'heure d'exécution de la migration, jamais une date inventée");
        }
    }

    [Fact]
    public void Adoption_AucuneLigneNEstSupprimee()
    {
        using var context = ArrangeLegacyDatabase("adopt-nodelete.db");
        context.Database.Migrate();

        Query(context, "SELECT COUNT(*) FROM \"Notifications\";")[0].Should().Be("10");
    }

    [Fact]
    public void Adoption_LesFaitsHistoriquesSurvivent_EtRestentActifs()
    {
        using var context = ArrangeLegacyDatabase("adopt-events.db");
        context.Database.Migrate();

        Query(context, "SELECT COUNT(*) FROM \"Notifications\" WHERE \"Type\" = 'OrderStatusChanged';")[0].Should().Be("3");
        Query(context, "SELECT COUNT(*) FROM \"Notifications\" WHERE \"Type\" = 'Info';")[0].Should().Be("2");

        // Aucun fait historique ne doit avoir été résolu : la résolution ne concerne que LowStock/Product.
        Query(context, "SELECT COUNT(*) FROM \"Notifications\" WHERE \"Type\" <> 'LowStock' AND \"ResolvedAt\" IS NOT NULL;")[0]
            .Should().Be("0");
    }

    [Fact]
    public void Adoption_LHistoriqueEfEnregistreLaMigration()
    {
        using var context = ArrangeLegacyDatabase("adopt-history.db");
        context.Database.Migrate();

        context.Database.GetAppliedMigrations()
            .Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    // =========================================================================================================
    // Contrainte effective après migration
    // =========================================================================================================

    [Fact]
    public void ApresMigration_UneSecondeAlerteActivePourLaMemeCle_EstRefusee()
    {
        using var context = ArrangeLegacyDatabase("constraint.db");
        context.Database.Migrate();

        // Le produit 10 porte déjà une alerte active (id 3).
        var act = () => Execute(context,
            "INSERT INTO \"Notifications\" " +
            "(\"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\") " +
            "VALUES ('LowStock', 'Doublon', 'm', 10, 'Product', 0, '2026-02-01 10:00:00', NULL);");

        act.Should().Throw<SqliteException>("la base elle-même refuse un second épisode actif");
    }

    [Fact]
    public void ApresMigration_UneAlerteResolueSupplementaire_EstAutorisee()
    {
        using var context = ArrangeLegacyDatabase("constraint-resolved.db");
        context.Database.Migrate();

        var act = () => Execute(context,
            "INSERT INTO \"Notifications\" " +
            "(\"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\") " +
            "VALUES ('LowStock', 'Historique', 'm', 10, 'Product', 0, '2026-02-01 10:00:00', '2026-02-02 10:00:00');");

        act.Should().NotThrow("l'index ne contraint que les alertes ACTIVES : l'historique peut s'empiler");
    }

    [Fact]
    public void ApresMigration_LesFaitsHistoriquesMultiplesRestentAutorises()
    {
        using var context = ArrangeLegacyDatabase("constraint-events.db");
        context.Database.Migrate();

        var act = () => Execute(context,
            "INSERT INTO \"Notifications\" " +
            "(\"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\") VALUES " +
            "('OrderStatusChanged', 'Transition', 'm', 30, 'Order', 0, '2026-02-01 10:00:00', NULL), " +
            "('PaymentReceived', 'Encaissement', 'm', 30, 'Order', 0, '2026-02-02 10:00:00', NULL), " +
            "('PaymentReceived', 'Encaissement', 'm', 30, 'Order', 0, '2026-02-03 10:00:00', NULL), " +
            "('Info', 'Information', 'm', NULL, NULL, 0, '2026-02-04 10:00:00', NULL), " +
            "('Info', 'Information', 'm', NULL, NULL, 0, '2026-02-05 10:00:00', NULL);");

        act.Should().NotThrow("une commande traverse plusieurs transitions, et les Info n'ont aucune entité liée");
    }

    // =========================================================================================================
    // Rollback
    // =========================================================================================================

    [Fact]
    public void Down_SupprimeLIndexEtLaColonne_SansPerdreAucuneLigne()
    {
        using var context = ArrangeLegacyDatabase("rollback.db");
        context.Database.Migrate();

        context.Database.GetService<IMigrator>().Migrate(PreviousMigration(context));

        Columns(context, NotificationsTable).Should().NotContain("ResolvedAt");
        Query(context, $"SELECT name FROM sqlite_master WHERE type='index' AND name='{IndexName}';").Should().BeEmpty();

        // Aucune ligne perdue : le rollback ne détruit que l'INFORMATION de résolution — les doublons historiques
        // redeviennent donc implicitement actifs, faute de pouvoir être représentés autrement.
        Query(context, "SELECT COUNT(*) FROM \"Notifications\";")[0].Should().Be("10");
        Query(context, "SELECT COUNT(*) FROM \"Notifications\" WHERE \"Type\" = 'LowStock' AND \"EntityId\" = 10;")[0]
            .Should().Be("3");
    }
}
