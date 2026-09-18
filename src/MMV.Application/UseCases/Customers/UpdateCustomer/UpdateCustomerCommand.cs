using System;

namespace MMV.Application.UseCases.Customers.UpdateCustomer;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdateCustomerUseCase"/> — « Modifier un client » (P2C-2). Porte
/// l'identifiant du client à modifier et les champs éditables, tels que saisis dans <c>CustomerFormViewModel</c>
/// en mode édition ; la ViewModel transforme son état en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de la branche édition de
/// <c>CustomerFormViewModel.ExecuteSave</c>. La normalisation « blanc → <c>null</c> » est faite par le use case
/// (source unique), exactement comme le flux d'origine.
/// </remarks>
public sealed class UpdateCustomerCommand
{
    /// <summary>Identifiant du client à modifier (chargé par le use case).</summary>
    public long CustomerId { get; init; }

    /// <summary>Prénom du client (requis ; transmis tel quel, comme le flux d'origine).</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille du client (requis ; transmis tel quel, comme le flux d'origine).</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Adresse email (vidée vers <c>null</c> si blanche).</summary>
    public string? Email { get; init; }

    /// <summary>Numéro de téléphone (vidé vers <c>null</c> si blanc).</summary>
    public string? Phone { get; init; }

    /// <summary>Date de naissance — date civile, telle que saisie par la ViewModel.</summary>
    /// <remarks>
    /// P4-5D : <see cref="DateOnly"/> et non <c>DateTime</c> — une date civile n'est pas un instant, et
    /// la convertir en UTC pouvait la décaler d'un jour (ADR-PROD-DB-004 §2.4, §5 décision 7).
    /// </remarks>
    public DateOnly? BirthDate { get; init; }

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
