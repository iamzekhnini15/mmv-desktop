using MMV.Domain.Entities;

namespace MMV.Application.UseCases.Suppliers.Common;

/// <summary>
/// Normalisation d'entrée <b>unique et partagée</b> des champs fournisseur (P3-9). Propriétaire commun de
/// <c>CreateSupplierUseCase</c> et <c>UpdateSupplierUseCase</c> : les deux chemins produisent exactement les mêmes
/// valeurs, et la validation s'exécute toujours sur une entrée déjà normalisée.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi normaliser — et ce que ce n'est PAS.</b> L'audit P3-9 §9.5 justifiait le <c>Trim</c> en affirmant
/// que <c>" contact@essilor.fr "</c> échouerait <c>EmailAddress()</c>. Cette affirmation est <b>fausse</b> pour ce
/// dépôt et a été corrigée : le mode <c>AspNetCoreCompatible</c> de FluentValidation tolère les espaces
/// environnants (prouvé par <c>SupplierValidatorTests</c>). Les raisons réelles subsistent, et suffisent : ne pas
/// persister d'espaces parasites, unifier <c>""</c> et <c>null</c> en une seule représentation de « non
/// renseigné », et mesurer les bornes de longueur sur le contenu réel plutôt que sur le remplissage.
/// </para>
/// <para>
/// <b>Chaîne vide ⇒ <c>null</c> pour les quatre champs optionnels.</b> Deux représentations de « pas d'e-mail »
/// coexistaient en base (<c>NULL</c> et <c>""</c>), rendant ambiguë toute comparaison future (audit §8.9, 🟡-2).
/// Une seule subsiste désormais côté écriture. <c>Name</c> reste non nullable : des espaces seuls deviennent
/// <c>""</c>, que <see cref="MMV.Domain.Validators.SupplierValidator"/> refuse ensuite explicitement.
/// </para>
/// <para>
/// <b>Ce que cette normalisation ne fait PAS</b>, délibérément : aucun changement de casse (ni e-mail, ni
/// <c>ReferenceCode</c>), aucun reformatage de numéro de téléphone, aucune comparaison de doublon. Chacun de ces
/// gestes supposerait une règle métier que le dépôt ne prouve nulle part (audit §10) et modifierait des valeurs
/// que l'utilisateur a saisies intentionnellement.
/// </para>
/// <para>
/// Composant <b>pur</b> : aucune persistance, aucune dépendance EF/SQLite, aucun état.
/// </para>
/// </remarks>
public static class SupplierInputNormalizer
{
    /// <summary>
    /// Normalise les cinq champs saisis et renvoie le résultat immuable.
    /// </summary>
    public static NormalizedSupplierInput Normalize(
        string? name,
        string? contactEmail,
        string? phone,
        string? address,
        string? referenceCode)
        => new(
            Name: name?.Trim() ?? string.Empty,
            ContactEmail: NullIfBlank(contactEmail),
            Phone: NullIfBlank(phone),
            Address: NullIfBlank(address),
            ReferenceCode: NullIfBlank(referenceCode));

    /// <summary>
    /// <c>Trim</c> puis <c>null</c> si le résultat est vide : « renseigné avec des espaces » et « non renseigné »
    /// deviennent le même état, une seule fois, au même endroit.
    /// </summary>
    private static string? NullIfBlank(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}

/// <summary>
/// Résultat immuable de <see cref="SupplierInputNormalizer.Normalize"/> — les cinq champs fournisseur tels qu'ils
/// seront validés puis persistés.
/// </summary>
public sealed record NormalizedSupplierInput(
    string Name,
    string? ContactEmail,
    string? Phone,
    string? Address,
    string? ReferenceCode)
{
    /// <summary>
    /// Construit un <see cref="Supplier"/> <b>détaché</b> portant ces valeurs, destiné à être validé <b>avant</b>
    /// toute écriture et avant toute mutation d'une entité suivie.
    /// </summary>
    public Supplier ToCandidate(long supplierId = 0) => new()
    {
        SupplierId = supplierId,
        Name = Name,
        ContactEmail = ContactEmail,
        Phone = Phone,
        Address = Address,
        ReferenceCode = ReferenceCode,
    };

    /// <summary>
    /// Reporte ces valeurs sur une entité existante. Les cinq champs sont remplacés : la commande est complète,
    /// il n'existe pas de sémantique « champ non fourni = ne pas toucher » (décision P3-9 — aucun patch partiel).
    /// </summary>
    public void ApplyTo(Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        supplier.Name = Name;
        supplier.ContactEmail = ContactEmail;
        supplier.Phone = Phone;
        supplier.Address = Address;
        supplier.ReferenceCode = ReferenceCode;
    }
}
