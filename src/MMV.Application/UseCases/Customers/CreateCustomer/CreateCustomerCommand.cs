using System;

namespace MMV.Application.UseCases.Customers.CreateCustomer;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateCustomerUseCase"/> — « Créer un client » (P2C-2). Porte uniquement
/// les données nécessaires à l'enregistrement, telles que saisies dans <c>CustomerFormViewModel</c> en mode
/// création ; la ViewModel transforme son état (champs liés à l'UI) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de la branche création de
/// <c>CustomerFormViewModel.ExecuteSave</c> : le use case ne recalcule rien et n'introduit aucun nouveau concept
/// métier. Les champs optionnels sont des <c>string?</c> ; la normalisation « blanc → <c>null</c> » est faite par
/// le use case (source unique), exactement comme le flux d'origine.
/// </remarks>
public sealed class CreateCustomerCommand
{
    /// <summary>Prénom du client (requis ; transmis tel quel, comme le flux d'origine).</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille du client (requis ; transmis tel quel, comme le flux d'origine).</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Adresse email (vidée vers <c>null</c> si blanche).</summary>
    public string? Email { get; init; }

    /// <summary>Numéro de téléphone (vidé vers <c>null</c> si blanc).</summary>
    public string? Phone { get; init; }

    /// <summary>Date de naissance (telle que saisie/initialisée par la ViewModel).</summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>Adresse postale (vidée vers <c>null</c> si blanche).</summary>
    public string? Address { get; init; }

    /// <summary>Ville (vidée vers <c>null</c> si blanche).</summary>
    public string? City { get; init; }

    /// <summary>Code postal (vidé vers <c>null</c> si blanc).</summary>
    public string? PostalCode { get; init; }

    /// <summary>Numéro de sécurité sociale (vidé vers <c>null</c> si blanc).</summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>Nom de la mutuelle/assurance (vidé vers <c>null</c> si blanc).</summary>
    public string? InsuranceName { get; init; }

    /// <summary>Notes libres (vidées vers <c>null</c> si blanches).</summary>
    public string? Notes { get; init; }
}
