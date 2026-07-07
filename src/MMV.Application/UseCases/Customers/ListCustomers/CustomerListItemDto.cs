using System;

namespace MMV.Application.UseCases.Customers.ListCustomers;

/// <summary>
/// DTO applicatif (lecture seule) d'un client (P2D-7C). Remplace l'entité EF <c>Customer</c> côté UI dans
/// <c>CustomersListViewModel</c> (liste), <c>CustomerDetailViewModel</c> / <c>CustomerInfoViewModel</c> (fiche) et
/// <c>CustomerFormViewModel.InitializeForEdit</c> (pré-remplissage du formulaire d'édition). Porte l'ensemble des
/// champs scalaires de l'entité (aucune navigation EF : <c>Prescriptions</c> / <c>Sales</c> non exposées).
/// </summary>
public sealed class CustomerListItemDto
{
    /// <summary>Identifiant unique du client.</summary>
    public long CustomerId { get; init; }

    /// <summary>Prénom du client.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille du client.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Date de naissance.</summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>Numéro de téléphone.</summary>
    public string? Phone { get; init; }

    /// <summary>Adresse email.</summary>
    public string? Email { get; init; }

    /// <summary>Adresse postale.</summary>
    public string? Address { get; init; }

    /// <summary>Ville de résidence.</summary>
    public string? City { get; init; }

    /// <summary>Code postal.</summary>
    public string? PostalCode { get; init; }

    /// <summary>Numéro de sécurité sociale.</summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>Nom de la mutuelle/assurance.</summary>
    public string? InsuranceName { get; init; }

    /// <summary>Notes diverses concernant le client.</summary>
    public string? Notes { get; init; }

    /// <summary>Date de création de la fiche client.</summary>
    public DateTime CreatedAt { get; init; }
}
