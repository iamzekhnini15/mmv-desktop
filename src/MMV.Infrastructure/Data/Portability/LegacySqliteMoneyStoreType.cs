namespace MMV.Infrastructure.Data.Portability;

/// <summary>
/// Type physique qu'une colonne monétaire porte <b>aujourd'hui sur SQLite</b>, avant P4-5C
/// (ADR-PROD-DB-003 §2.1). Ce n'est pas un choix : c'est un <b>constat du dépôt</b>, déclaré au site de
/// configuration pour que <see cref="ModelPortability.MoneyStoreType"/> puisse le reconduire à l'identique
/// sur SQLite — et l'ignorer sur PostgreSQL.
///
/// <para>
/// Les 14 colonnes monétaires se répartissent en deux mappings historiques incohérents : <b>11</b>
/// déclaraient <c>HasColumnType("REAL")</c>, <b>3</b> ne déclaraient rien et recevaient donc le mapping
/// <c>decimal</c> par défaut de SQLite, soit <c>TEXT</c>.
/// </para>
/// </summary>
public enum LegacySqliteMoneyStoreType
{
    /// <summary>
    /// La colonne ne déclarait aucun type : mapping <c>decimal</c> par défaut du provider
    /// (<c>TEXT</c> sur SQLite). Concerne <c>Supplements.SupplementPrice</c>,
    /// <c>GlassPricingTiers.PurchasePriceGrid</c> et <c>GlassPricingTiers.SalePriceGrid</c>.
    /// </summary>
    ProviderDefault = 0,

    /// <summary>
    /// La colonne déclarait <c>HasColumnType("REAL")</c> — flottant IEEE 754 8 octets sur SQLite.
    /// Concerne les 11 autres colonnes monétaires.
    ///
    /// <para>
    /// Ce littéral <b>ne doit jamais atteindre PostgreSQL</b> : <c>REAL</c> y désigne <c>float4</c>,
    /// un flottant <b>4 octets</b> (~6 chiffres significatifs). Le porter tel quel ne reconduirait pas le
    /// statu quo, il <b>diviserait par deux une précision déjà insuffisante</b> — <c>12 345,67</c> n'y est
    /// pas représentable fidèlement. C'est une régression, pas un report de dette
    /// (ADR-PROD-DB-003 §2.2, §5.3).
    /// </para>
    /// </summary>
    Real = 1
}
