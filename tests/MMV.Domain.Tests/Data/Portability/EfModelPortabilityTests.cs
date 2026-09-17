using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Data.Portability;
using Xunit;

namespace MMV.Domain.Tests.Data.Portability;

/// <summary>
/// P4-5C — vérifie le <b>modèle EF réellement construit</b> par <see cref="OpticDbContext"/> pour chacun
/// des deux providers : mapping monétaire (ADR-PROD-DB-003 M1/M2), filtre d'index partiel et retrait des
/// littéraux de type optiques (ADR-PROD-DB-006 X1/X3).
///
/// <para>
/// <b>Aucun serveur n'est contacté.</b> La construction du modèle EF est purement statique : elle
/// n'ouvre aucune connexion. Le modèle PostgreSQL est donc vérifiable sur cette machine comme en CI, sans
/// instance PostgreSQL.
/// </para>
///
/// <para>
/// <b>Ce que ces tests NE prouvent PAS.</b> Ils prouvent ce que le modèle <i>déclare</i>, jamais ce que la
/// base <i>fait</i>. La vérification physique de <c>numeric(12,2)</c> dans
/// <c>information_schema.columns</c> (M4), la fidélité monétaire aller-retour (M5), la sémantique de
/// <c>"RemainingAmount" &gt; 0</c> et de <c>SUM("FinalAmount")</c> (M6) et la présence réelle des index
/// partiels dans <c>pg_index</c> (X5) appartiennent à la suite d'intégration PostgreSQL de <b>P4-5F</b>
/// (ADR-PROD-DB-008). Un test vert ici ne vaut pas preuve d'exactitude monétaire.
/// </para>
/// </summary>
public sealed class EfModelPortabilityTests
{
    /// <summary>Les 14 colonnes monétaires du modèle (ADR-PROD-DB-003 §2.1).</summary>
    public static TheoryData<Type, string> MoneyColumns => new()
    {
        { typeof(Product), nameof(Product.PurchasePrice) },
        { typeof(Product), nameof(Product.SalePrice) },
        { typeof(Product), nameof(Product.RecommendedPrice) },
        { typeof(Sale), nameof(Sale.TotalAmount) },
        { typeof(Sale), nameof(Sale.DiscountAmount) },
        { typeof(Sale), nameof(Sale.FinalAmount) },
        { typeof(Sale), nameof(Sale.DepositAmount) },
        { typeof(Sale), nameof(Sale.RemainingAmount) },
        { typeof(SaleItem), nameof(SaleItem.UnitPrice) },
        { typeof(SaleItem), nameof(SaleItem.TotalPrice) },
        { typeof(OrderItem), nameof(OrderItem.UnitPrice) },
        { typeof(Supplement), nameof(Supplement.SupplementPrice) },
        { typeof(GlassPricingTier), nameof(GlassPricingTier.PurchasePriceGrid) },
        { typeof(GlassPricingTier), nameof(GlassPricingTier.SalePriceGrid) }
    };

    /// <summary>Les 11 colonnes monétaires historiquement déclarées <c>REAL</c> sur SQLite.</summary>
    public static TheoryData<Type, string> LegacyRealMoneyColumns => new()
    {
        { typeof(Product), nameof(Product.PurchasePrice) },
        { typeof(Product), nameof(Product.SalePrice) },
        { typeof(Product), nameof(Product.RecommendedPrice) },
        { typeof(Sale), nameof(Sale.TotalAmount) },
        { typeof(Sale), nameof(Sale.DiscountAmount) },
        { typeof(Sale), nameof(Sale.FinalAmount) },
        { typeof(Sale), nameof(Sale.DepositAmount) },
        { typeof(Sale), nameof(Sale.RemainingAmount) },
        { typeof(SaleItem), nameof(SaleItem.UnitPrice) },
        { typeof(SaleItem), nameof(SaleItem.TotalPrice) },
        { typeof(OrderItem), nameof(OrderItem.UnitPrice) }
    };

    /// <summary>Les 3 colonnes monétaires qui n'ont jamais déclaré de type (défaut SQLite : <c>TEXT</c>).</summary>
    public static TheoryData<Type, string> LegacyDefaultMoneyColumns => new()
    {
        { typeof(Supplement), nameof(Supplement.SupplementPrice) },
        { typeof(GlassPricingTier), nameof(GlassPricingTier.PurchasePriceGrid) },
        { typeof(GlassPricingTier), nameof(GlassPricingTier.SalePriceGrid) }
    };

    /// <summary>Les 18 colonnes optiques <c>double</c> dont le littéral « REAL » est retiré (X3).</summary>
    public static TheoryData<Type, string> OpticalColumns => new()
    {
        { typeof(Prescription), nameof(Prescription.OdSphere) },
        { typeof(Prescription), nameof(Prescription.OdCylinder) },
        { typeof(Prescription), nameof(Prescription.OdAddition) },
        { typeof(Prescription), nameof(Prescription.OdPrismValue) },
        { typeof(Prescription), nameof(Prescription.OgSphere) },
        { typeof(Prescription), nameof(Prescription.OgCylinder) },
        { typeof(Prescription), nameof(Prescription.OgAddition) },
        { typeof(Prescription), nameof(Prescription.OgPrismValue) },
        { typeof(OrderItem), nameof(OrderItem.Sphere) },
        { typeof(OrderItem), nameof(OrderItem.Cylinder) },
        { typeof(OrderItem), nameof(OrderItem.Addition) },
        { typeof(OrderItem), nameof(OrderItem.PrismValue) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.SourceSphere) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.SourceCylinder) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.Addition) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.PrismValue) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.TransposedSphere) },
        { typeof(WorkshopSheetItem), nameof(WorkshopSheetItem.TransposedCylinder) }
    };

    private const string CurrentWorkshopSheetIndexName = "idx_workshop_sheets_current_unique";

    // La chaîne PostgreSQL n'est JAMAIS ouverte : seul le modèle est construit.
    private static OpticDbContext SqliteContext() => new(
        new DbContextOptionsBuilder<OpticDbContext>().UseSqlite("Data Source=:memory:").Options);

    private static OpticDbContext PostgreSqlContext() => new(
        new DbContextOptionsBuilder<OpticDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=mmv_model_only;Username=none;Password=none").Options);

    private static IProperty Property(OpticDbContext context, Type entity, string propertyName)
        => context.Model.FindEntityType(entity)!.FindProperty(propertyName)!;

    private static IIndex CurrentWorkshopSheetIndex(OpticDbContext context)
        => context.Model.FindEntityType(typeof(WorkshopSheet))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == CurrentWorkshopSheetIndexName);

    // ---------------------------------------------------------------- mapping monétaire (M1)

    [Theory]
    [MemberData(nameof(MoneyColumns))]
    public void Money_DeclaresPrecision12_2_OnSqlite(Type entity, string propertyName)
    {
        using var context = SqliteContext();
        var property = Property(context, entity, propertyName);

        property.GetPrecision().Should().Be(12);
        property.GetScale().Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(MoneyColumns))]
    public void Money_DeclaresPrecision12_2_OnPostgreSql(Type entity, string propertyName)
    {
        using var context = PostgreSqlContext();
        var property = Property(context, entity, propertyName);

        property.GetPrecision().Should().Be(12);
        property.GetScale().Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(MoneyColumns))]
    public void Money_IsExactNumeric_OnPostgreSql(Type entity, string propertyName)
    {
        using var context = PostgreSqlContext();

        Property(context, entity, propertyName).GetColumnType().Should().Be("numeric(12,2)",
            "la production exige une arithmétique décimale exacte, contrainte à deux décimales par la base");
    }

    // ---------------------------------------------------------------- non-régression SQLite (M2, M3)

    [Theory]
    [MemberData(nameof(LegacyRealMoneyColumns))]
    public void Money_KeepsLegacyRealStoreType_OnSqlite(Type entity, string propertyName)
    {
        using var context = SqliteContext();

        Property(context, entity, propertyName).GetColumnType().Should().Be("REAL",
            "basculer ces colonnes sur TEXT ferait de « RemainingAmount > 0 » une comparaison " +
            "lexicographique, où '0.00' > '0' — soit un second règlement d'un solde déjà réglé");
    }

    [Theory]
    [MemberData(nameof(LegacyDefaultMoneyColumns))]
    public void Money_KeepsLegacyDefaultStoreType_OnSqlite(Type entity, string propertyName)
    {
        using var context = SqliteContext();

        Property(context, entity, propertyName).GetColumnType().Should().Be("TEXT",
            "ces trois colonnes n'ont jamais déclaré de type : le défaut decimal de SQLite est reconduit");
    }

    [Fact]
    public void PostgreSqlModel_CarriesNoSqliteRealLiteral()
    {
        using var context = PostgreSqlContext();

        var offenders = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => string.Equals(p.GetColumnType(), "REAL", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToArray();

        offenders.Should().BeEmpty(
            "« REAL » désigne float4 (4 octets) sur PostgreSQL : le transmettre serait une régression " +
            "de précision, pas un report de dette");
    }

    // ---------------------------------------------------------------- littéraux optiques (X3)

    [Theory]
    [MemberData(nameof(OpticalColumns))]
    public void Optical_KeepsRealStoreType_OnSqlite(Type entity, string propertyName)
    {
        using var context = SqliteContext();

        // Le littéral a été retiré de la configuration ; le mapping par défaut d'un double sur SQLite
        // redonne exactement REAL — la chaîne SQLite est donc inchangée.
        Property(context, entity, propertyName).GetColumnType().Should().Be("REAL");
    }

    [Theory]
    [MemberData(nameof(OpticalColumns))]
    public void Optical_BecomesDoublePrecision_OnPostgreSql(Type entity, string propertyName)
    {
        using var context = PostgreSqlContext();

        Property(context, entity, propertyName).GetColumnType().Should().Be("double precision",
            "la dioptrie se mesure par pas de 0,25 : float8 est adéquat, float4 serait une perte gratuite");
    }

    [Fact]
    public void TechnicalSpecs_KeepsItsTextLiteral_OnBothProviders()
    {
        // Littéral CONSERVÉ délibérément : « TEXT » est un type valide des deux côtés, donc portable tel
        // quel. jsonb est rejeté pour la V1 (ADR-PROD-DB-006 §5.6).
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        Property(sqlite, typeof(Product), nameof(Product.TechnicalSpecs)).GetColumnType().Should().Be("TEXT");
        Property(postgres, typeof(Product), nameof(Product.TechnicalSpecs)).GetColumnType().Should().Be("TEXT");
    }

    // ---------------------------------------------------------------- index partiel (X1)

    [Fact]
    public void CurrentWorkshopSheetIndex_UsesIntegerComparison_OnSqlite()
    {
        using var context = SqliteContext();
        var index = CurrentWorkshopSheetIndex(context);

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("\"IsCurrent\" = 1",
            "forme historique, inchangée au caractère près : les 14 migrations SQLite restent valides");
    }

    [Fact]
    public void CurrentWorkshopSheetIndex_UsesBareBoolean_OnPostgreSql()
    {
        using var context = PostgreSqlContext();
        var index = CurrentWorkshopSheetIndex(context);

        index.IsUnique.Should().BeTrue("la garantie « au plus une version courante par commande » reste " +
            "arbitrée par la base, jamais par un contrôle applicatif");
        index.GetFilter().Should().Be("\"IsCurrent\"");
    }

    [Fact]
    public void ActiveLowStockIndexFilter_IsUntouched_AndPortableAsIs()
    {
        // Ce filtre ne contient que des comparaisons de texte et des tests IS NULL : valides et immuables
        // sur les deux moteurs. Le modifier serait un risque gratuit (ADR-PROD-DB-006 §5.4).
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        static string? Filter(OpticDbContext context) => context.Model
            .FindEntityType(typeof(Notification))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "idx_notifications_active_low_stock_unique")
            .GetFilter();

        Filter(postgres).Should().Be(Filter(sqlite));
    }

    [Fact]
    public void BothProviderModels_CoexistInTheSameProcess_WithoutContaminatingEachOther()
    {
        // RISQUE RÉEL, ici verrouillé par mesure : EF met le modèle en cache. Si ce cache était partagé
        // entre providers, le PREMIER contexte construit dans le processus imposerait son filtre d'index et
        // son type monétaire au second — un schéma PostgreSQL pourrait alors naître avec la forme SQLite.
        // Rien ne le signalerait : aucun test fonctionnel ne casse, l'anomalie ne se voit qu'en concurrence.
        // Les deux modèles sont donc construits ICI, dans le même processus et dans cet ordre.
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        CurrentWorkshopSheetIndex(sqlite).GetFilter().Should().Be("\"IsCurrent\" = 1");
        CurrentWorkshopSheetIndex(postgres).GetFilter().Should().Be("\"IsCurrent\"");

        Property(sqlite, typeof(Sale), nameof(Sale.RemainingAmount)).GetColumnType().Should().Be("REAL");
        Property(postgres, typeof(Sale), nameof(Sale.RemainingAmount)).GetColumnType().Should().Be("numeric(12,2)");
    }

    // ---------------------------------------------------------------- point de lecture unique (X2)

    [Fact]
    public void ProviderIsReadOnlyByTheDbContext_NotByEntityConfigurations()
    {
        // Garde structurelle d'X2 : le nom du provider n'est lu qu'à UN endroit du modèle. Si une
        // configuration d'entité se met à interroger Database.ProviderName pour son propre compte, ce test
        // le signale — la dispersion du couplage est précisément ce que l'ADR interdit.
        var configurations = Directory.GetFiles(
            ConfigurationsDirectory(), "*Configuration.cs", SearchOption.TopDirectoryOnly);

        configurations.Should().NotBeEmpty("le répertoire des configurations EF doit être trouvé");

        var offenders = configurations
            .Where(f => File.ReadAllText(f).Contains("ProviderName", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "le provider est lu une seule fois, dans OpticDbContext.OnModelCreating, puis passé aux " +
            "configurations sous forme de ModelPortability");
    }

    private static string ConfigurationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MMV.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("les tests s'exécutent sous la racine du dépôt");
        return Path.Combine(
            directory!.FullName, "src", "MMV.Infrastructure", "Data", "Configurations");
    }
}
