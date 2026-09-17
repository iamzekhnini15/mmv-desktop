using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MMV.Infrastructure.Data.Portability;

/// <summary>
/// Écriture fluide du mapping monétaire décidé par ADR-PROD-DB-003 (obligations M1 et M2), appliqué aux
/// <b>14</b> colonnes monétaires du modèle.
///
/// <para>
/// Cette classe ne prend <b>aucune</b> décision de provider : elle applique la précision commune et délègue
/// le type physique à <see cref="ModelPortability.MoneyStoreType"/>, seul point de sélection.
/// </para>
/// </summary>
public static class MoneyMappingExtensions
{
    /// <summary>
    /// Déclare une propriété comme <b>colonne monétaire</b> :
    /// <list type="bullet">
    ///   <item>précision <c>(12, 2)</c> sur les deux providers ⇒ <c>numeric(12,2)</c> sur PostgreSQL,
    ///     exact en base 10 et contraint à deux décimales par la base elle-même ;</item>
    ///   <item>type physique SQLite <b>reconduit à l'identique</b>, jamais transmis à PostgreSQL.</item>
    /// </list>
    ///
    /// <para>
    /// Mesuré en P4-5C : déclarer cette précision <b>ne produit aucune dérive du modèle SQLite</b> — le
    /// type de stockage SQLite d'un <c>decimal</c> ne dépend pas de la précision, et celui des colonnes
    /// <c>REAL</c> reste imposé explicitement. La chaîne des 14 migrations SQLite reste donc valide et
    /// <c>has-pending-model-changes</c> reste vert (ADR-PROD-DB-003 M3).
    /// </para>
    /// </summary>
    /// <param name="property">Propriété <c>decimal</c> ou <c>decimal?</c> à mapper.</param>
    /// <param name="portability">Point de sélection unique, fourni par <c>OnModelCreating</c>.</param>
    /// <param name="legacySqliteStoreType">Type physique actuel de la colonne sur SQLite (constat du dépôt).</param>
    public static PropertyBuilder HasMoneyMapping(
        this PropertyBuilder property,
        ModelPortability portability,
        LegacySqliteMoneyStoreType legacySqliteStoreType)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(portability);

        property.HasPrecision(ModelPortability.MoneyPrecision, ModelPortability.MoneyScale);

        var storeType = portability.MoneyStoreType(legacySqliteStoreType);
        if (storeType is not null)
        {
            property.HasColumnType(storeType);
        }

        return property;
    }
}
