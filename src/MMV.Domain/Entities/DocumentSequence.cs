namespace MMV.Domain.Entities;

/// <summary>
/// Compteur persistant d'une <b>séquence de numérotation de document</b> (P2A-1E, R-03 / ADR-006).
///
/// <para>
/// Une ligne par type de document logique (ex. <c>SALE</c>, <c>ORDER</c>). Le numéro affiché est construit
/// à partir du <see cref="Prefix"/> et de la <see cref="CurrentValue"/> incrémentée de façon
/// <b>transactionnelle et atomique</b> par <c>INumberSequenceService</c> — il remplace la génération
/// aléatoire (<c>new Random().Next(10000)</c>) qui pouvait entrer en collision avec les index UNIQUE
/// <c>SaleNumber</c>/<c>OrderNumber</c>.
/// </para>
///
/// <para>
/// Structure volontairement minimale pour P2A-1E (aucune notion d'organisation, magasin, pays ou exercice).
/// Le <see cref="SequenceName"/> est la clé : il pourra plus tard encoder un découpage par
/// magasin/pays/exercice (ex. <c>SALE-FR-2027</c>) sans changer le contrat du service ni l'entité.
/// </para>
/// </summary>
public class DocumentSequence
{
    /// <summary>
    /// Identité logique de la séquence (clé primaire). Ex. <c>SALE</c>, <c>ORDER</c>.
    /// </summary>
    public string SequenceName { get; set; } = string.Empty;

    /// <summary>
    /// Préfixe affiché dans le numéro généré. Ex. <c>VTE</c> ⇒ <c>VTE-000001</c>, <c>CMD</c> ⇒ <c>CMD-000001</c>.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Dernière valeur <b>attribuée</b> (le prochain numéro est <c>CurrentValue + 1</c>). Démarre à 0 :
    /// le premier numéro émis est donc <c>1</c>.
    /// </summary>
    public long CurrentValue { get; set; }

    /// <summary>
    /// Horodatage de la dernière attribution (audit). Géré par l'application (futur <c>IClock</c>).
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
