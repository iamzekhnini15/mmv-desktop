using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Users.ListUsers;

/// <summary>
/// DTO applicatif (lecture seule) d'une ligne de la liste des utilisateurs (P2D-1).
/// <para>
/// Porte <b>exactement</b> les champs consommés par <c>UsersListViewModel</c> (recherche / filtre / tri /
/// pagination) et son écran (<c>UsersListView</c>). Aucune entité <c>MMV.Domain.Entities.User</c> suivie par EF
/// ne franchit la frontière UI : le <see cref="ListUsersUseCase"/> projette l'entité vers ce DTO plat.
/// </para>
/// </summary>
public sealed class UserListItemDto
{
    /// <summary>Identifiant de l'utilisateur (utilisé pour l'édition).</summary>
    public long UserId { get; init; }

    /// <summary>Nom d'utilisateur (identifiant de connexion).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Prénom.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Rôle applicatif.</summary>
    public UserRole Role { get; init; }

    /// <summary>Compte actif ou non.</summary>
    public bool IsActive { get; init; }

    /// <summary>Dernière connexion (jamais connecté = <c>null</c>).</summary>
    public DateTime? LastLogin { get; init; }

    /// <summary>Date de création du compte.</summary>
    public DateTime CreatedAt { get; init; }
}
