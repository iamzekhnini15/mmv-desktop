using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.App.Services;

/// <summary>
/// Service de gestion de session utilisateur (singleton).
/// Maintient l'état d'authentification durant toute la durée de vie de l'application.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Utilisateur actuellement connecté.
    /// </summary>
    User? CurrentUser { get; }

    /// <summary>
    /// Indique si un utilisateur est authentifié.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Indique si l'utilisateur courant est administrateur.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>
    /// Vérifie si l'utilisateur courant a un rôle spécifique.
    /// </summary>
    bool HasRole(UserRole role);

    /// <summary>
    /// Connecte un utilisateur (stocke sa session).
    /// </summary>
    void Login(User user);

    /// <summary>
    /// Déconnecte l'utilisateur et réinitialise la session.
    /// </summary>
    void Logout();

    /// <summary>
    /// Réinitialise le timer d'inactivité (à appeler sur chaque action utilisateur).
    /// </summary>
    void ResetInactivityTimer();

    /// <summary>
    /// Événement déclenché lorsque la session expire par inactivité.
    /// </summary>
    event EventHandler? SessionExpired;

    /// <summary>
    /// Événement déclenché lorsque l'état de la session change (login/logout).
    /// </summary>
    event EventHandler? SessionChanged;
}
