using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Data.Time;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D — le <b>convertisseur validant</b> d'instants (ADR-PROD-DB-004 §5.4, option D3, obligation T3).
///
/// <para>
/// Ces tests portent sur trois affirmations distinctes, qu'il ne faut pas confondre :
/// </para>
/// <list type="number">
///   <item><description>la <b>règle</b> elle-même — ce qui est accepté, ce qui est refusé ;</description></item>
///   <item><description>sa <b>couverture</b> — elle s'applique à TOUTES les propriétés d'instants du
///   modèle, et à AUCUNE date civile ;</description></item>
///   <item><description>son <b>indépendance au provider</b> — la même règle s'applique au modèle SQLite et
///   au modèle PostgreSQL, ce qui est précisément ce qui permet de détecter la faute sans serveur.</description></item>
/// </list>
/// </summary>
public sealed class UtcDateTimeConverterTests
{
    private static OpticDbContext SqliteContext() => new(
        new DbContextOptionsBuilder<OpticDbContext>().UseSqlite("Data Source=:memory:").Options);

    private static OpticDbContext PostgreSqlContext() => new(
        new DbContextOptionsBuilder<OpticDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=mmv_model_only;Username=none;Password=none").Options);

    // ---------------------------------------------------------------------------- la règle

    [Fact]
    public void EnsureUtc_AccepteUnInstantUtc_EtLeRenvoieInchange()
    {
        var value = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Utc);

        UtcDateTimeConverter.EnsureUtc(value).Should().Be(value);
        UtcDateTimeConverter.EnsureUtc(value).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void EnsureUtc_RefuseToutAutreKind(DateTimeKind kind)
    {
        var value = new DateTime(2026, 9, 18, 14, 30, 0, kind);

        var act = () => UtcDateTimeConverter.EnsureUtc(value);

        act.Should().Throw<NonUtcDateTimeException>()
            .Which.OffendingKind.Should().Be(kind);
    }

    [Fact]
    public void EnsureUtc_NeCorrigeJamaisSilencieusement()
    {
        // Le cœur du choix D3 contre D1 : un Local N'EST PAS converti. Si quelqu'un remplaçait un jour le
        // throw par ToUniversalTime(), ce test tomberait — et c'est exactement l'effet recherché : la
        // conversion masquerait un DateTime.Now oublié au lieu de le révéler (ADR-PROD-DB-004 §4.2).
        var local = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Local);

        var act = () => UtcDateTimeConverter.EnsureUtc(local);

        act.Should().Throw<NonUtcDateTimeException>()
            .Which.OffendingValue.Should().Be(local);
    }

    [Fact]
    public void LaLecture_EtiquetteLaValeurEnUtc_SansLaDecaler()
    {
        // Branche « base → CLR ». Elle ne convertit rien : la valeur stockée EST UTC par l'invariant
        // d'écriture. Elle supprime la divergence d'étiquetage entre SQLite (Unspecified) et PostgreSQL
        // (Utc), qui ferait autrement changer de comportement tout code comparant ou formatant une date.
        var fromDatabase = new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Unspecified);

        var materialized = (DateTime)UtcDateTimeConverter.Instance.ConvertFromProvider(fromDatabase)!;

        materialized.Kind.Should().Be(DateTimeKind.Utc);
        materialized.Should().Be(new DateTime(2026, 9, 18, 14, 30, 0, DateTimeKind.Utc));
    }

    // ---------------------------------------------------------------------------- la couverture

    [Fact]
    public void ToutesLesProprietesDInstantDuModeleSqlite_PortentLeConvertisseur()
    {
        using var context = SqliteContext();

        UnprotectedInstantProperties(context).Should().BeEmpty(
            "le balayage d'OnModelCreating protège toute propriété d'instant, présente ou future");
    }

    [Fact]
    public void ToutesLesProprietesDInstantDuModelePostgreSql_PortentLeConvertisseur()
    {
        // Même assertion sur l'autre provider : c'est ce qui rend la détection SQLite représentative de
        // PostgreSQL. Aucun serveur n'est contacté — la construction du modèle EF est statique.
        using var context = PostgreSqlContext();

        UnprotectedInstantProperties(context).Should().BeEmpty();
    }

    [Fact]
    public void LeModeleCompte_DixNeufProprietesDInstant()
    {
        // Chiffre MESURÉ, pas cité : les 21 colonnes DateTime recensées par P4-5A, moins les DEUX dates
        // civiles passées à DateOnly en P4-5D (Customer.BirthDate, Prescription.IssueDate).
        using var context = SqliteContext();

        InstantProperties(context).Should().HaveCount(19);
    }

    [Theory]
    [InlineData(typeof(Customer), nameof(Customer.BirthDate))]
    [InlineData(typeof(Prescription), nameof(Prescription.IssueDate))]
    public void LesDatesCiviles_NeSontPasDesInstants_DoncNeSubissentPasLeConvertisseur(
        Type entity, string propertyName)
    {
        using var context = SqliteContext();

        var property = context.Model.FindEntityType(entity)!.FindProperty(propertyName)!;

        property.ClrType.Should().Match(t => t == typeof(DateOnly) || t == typeof(DateOnly?));
        (property.GetValueConverter() is UtcDateTimeConverter).Should().BeFalse(
            "une date civile ne porte aucun Kind : la soumettre au convertisseur d'instants n'aurait pas de sens");
    }

    // ---------------------------------------------------------------------------- pas d'effet de schéma

    [Theory]
    [InlineData(typeof(Sale), nameof(Sale.SaleDate))]
    [InlineData(typeof(Notification), nameof(Notification.CreatedAt))]
    [InlineData(typeof(Order), nameof(Order.EstimatedDelivery))]
    public void LeConvertisseur_NeChangePasLeTypeFourniAuProvider(Type entity, string propertyName)
    {
        // Conséquence attendue : aucune colonne ne change de type physique, donc aucune migration n'est
        // requise, donc le contrôle de dérive de modèle EF reste vert (ADR-PROD-DB-004 §7.3).
        using var context = SqliteContext();

        var property = context.Model.FindEntityType(entity)!.FindProperty(propertyName)!;

        property.GetValueConverter()!.ProviderClrType.Should().Be(typeof(DateTime));
    }

    [Fact]
    public void LeConvertisseur_EstIndependantDuProvider()
    {
        // Contrairement à ModelPortability (P4-5C), qui CHOISIT selon le moteur, le convertisseur impose la
        // même chose des deux côtés : c'est une contrainte, pas une adaptation.
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        var sqliteConverter = sqlite.Model.FindEntityType(typeof(Sale))!
            .FindProperty(nameof(Sale.SaleDate))!.GetValueConverter();
        var postgresConverter = postgres.Model.FindEntityType(typeof(Sale))!
            .FindProperty(nameof(Sale.SaleDate))!.GetValueConverter();

        sqliteConverter.Should().BeSameAs(postgresConverter);
    }

    private static string[] InstantProperties(OpticDbContext context)
        => context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToArray();

    private static string[] UnprotectedInstantProperties(OpticDbContext context)
        => context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .Where(p => p.GetValueConverter() is not UtcDateTimeConverter)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToArray();
}
