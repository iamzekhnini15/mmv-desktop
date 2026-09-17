using FluentAssertions;
using MMV.Infrastructure.Data.Portability;
using Xunit;

namespace MMV.Domain.Tests.Data.Portability;

/// <summary>
/// P4-5C — tests du <b>point de sélection unique</b> des constructions de modèle dépendantes du moteur
/// (<see cref="ModelPortability"/>), appliquant ADR-PROD-DB-003 (M1, M2) et ADR-PROD-DB-006 (X1, X2).
///
/// <para>
/// Ces tests portent sur la <b>décision</b> prise par le point de sélection, pas sur le modèle EF construit
/// — celui-ci est vérifié par <see cref="EfModelPortabilityTests"/>.
/// </para>
/// </summary>
public sealed class ModelPortabilityTests
{
    [Fact]
    public void Sqlite_KeepsIntegerComparisonFilter()
        => ModelPortability.For(ModelPortability.SqliteProviderName)
            .CurrentWorkshopSheetIndexFilter
            .Should().Be("\"IsCurrent\" = 1",
                "SQLite n'a pas de type booléen : IsCurrent y est un INTEGER 0/1, et la forme historique " +
                "doit rester inchangée au caractère près");

    [Fact]
    public void PostgreSql_UsesBareBooleanFilter()
        => ModelPortability.For(ModelPortability.PostgreSqlProviderName)
            .CurrentWorkshopSheetIndexFilter
            .Should().Be("\"IsCurrent\"",
                "PostgreSQL crée une colonne boolean et refuse toute comparaison booléen ↔ entier : " +
                "« \"IsCurrent\" = 1 » y ferait échouer la création de l'index, donc du schéma");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    [InlineData("microsoft.entityframeworkcore.sqlite")]
    public void UnknownProvider_Throws_RatherThanGuessing(string? providerName)
    {
        // Un filtre d'index partiel faux ne casse AUCUN test fonctionnel : il ne se voit qu'en concurrence,
        // et trop tard. L'échec doit donc survenir à la construction du modèle (ADR-PROD-DB-006 §5.1, §7.2).
        var act = () => ModelPortability.For(providerName);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*portabilité*");
    }

    [Fact]
    public void MoneyPrecisionAndScale_AreThoseDecidedByAdr003()
    {
        ModelPortability.MoneyPrecision.Should().Be(12,
            "douze chiffres couvrent 9 999 999 999,99 € (ADR-PROD-DB-003 §5.2)");
        ModelPortability.MoneyScale.Should().Be(2,
            "deux décimales, cohérent avec le value object Money");
    }

    [Fact]
    public void Sqlite_ReconductsLegacyStoreTypes_ForBothHistoricalMappings()
    {
        var sqlite = ModelPortability.For(ModelPortability.SqliteProviderName);

        sqlite.MoneyStoreType(LegacySqliteMoneyStoreType.Real).Should().Be("REAL",
            "les 11 colonnes historiquement REAL le restent : les basculer sur TEXT transformerait le CAS " +
            "« RemainingAmount > 0 » en comparaison de chaînes et imposerait un rebuild aux bases déployées");

        sqlite.MoneyStoreType(LegacySqliteMoneyStoreType.ProviderDefault).Should().BeNull(
            "les 3 colonnes sans type déclaré gardent le mapping decimal par défaut de SQLite (TEXT)");
    }

    [Fact]
    public void PostgreSql_NeverReceivesTheSqliteRealLiteral()
    {
        var postgres = ModelPortability.For(ModelPortability.PostgreSqlProviderName);

        // « REAL » désigne float4 (4 octets, ~6 chiffres significatifs) sur PostgreSQL : le transmettre
        // diviserait par deux une précision déjà insuffisante (ADR-PROD-DB-003 §2.2).
        postgres.MoneyStoreType(LegacySqliteMoneyStoreType.Real).Should().BeNull();
        postgres.MoneyStoreType(LegacySqliteMoneyStoreType.ProviderDefault).Should().BeNull();
    }

    [Theory]
    [InlineData(ModelPortability.SqliteProviderName)]
    [InlineData(ModelPortability.PostgreSqlProviderName)]
    public void ProviderName_IsReportedBack(string providerName)
        => ModelPortability.For(providerName).ProviderName.Should().Be(providerName);
}
