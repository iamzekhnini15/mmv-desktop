namespace MMV.Application.UseCases.Customers.ListCustomersForPicker;

/// <summary>
/// DTO applicatif (lecture seule) d'un client dans le sélecteur de client du formulaire de commande
/// (<c>OrderFormViewModel</c>) — P2D-6. Porte les champs consommés par l'écran : affichage (prénom, nom, téléphone),
/// recherche (prénom, nom, téléphone, email) et identification pour le rattachement (<see cref="CustomerId"/>).
/// Remplace l'entité EF suivie <c>Customer</c> côté sélecteur ; aucune navigation EF n'est exposée.
/// </summary>
public sealed class CustomerPickerItemDto
{
    /// <summary>Identifiant du client (rattachement / mode édition).</summary>
    public long CustomerId { get; init; }

    /// <summary>Prénom (affichage + recherche).</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom (affichage + recherche).</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Téléphone (affichage + recherche).</summary>
    public string? Phone { get; init; }

    /// <summary>Email (critère de recherche).</summary>
    public string? Email { get; init; }
}
