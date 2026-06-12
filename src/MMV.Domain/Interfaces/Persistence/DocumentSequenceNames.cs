namespace MMV.Domain.Interfaces.Persistence;

/// <summary>
/// Noms logiques des séquences de numérotation de documents (P2A-1E, R-03 / ADR-006).
///
/// <para>
/// Centralise les clés passées à <see cref="INumberSequenceService.NextNumberAsync"/> pour éviter les
/// chaînes magiques et garder un point unique d'extension (devis, facture, avoir, multi-magasin…).
/// </para>
/// </summary>
public static class DocumentSequenceNames
{
    /// <summary>Séquence des numéros de vente (préfixe <c>VTE</c>).</summary>
    public const string Sale = "SALE";

    /// <summary>Séquence des numéros de commande fournisseur (préfixe <c>CMD</c>).</summary>
    public const string Order = "ORDER";
}
