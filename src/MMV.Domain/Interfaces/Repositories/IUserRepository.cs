using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des utilisateurs.
/// </summary>
public interface IUserRepository : IGenericRepository<User, long>
{
    /// <summary>
    /// Récupère un utilisateur par son identifiant de connexion, quelle qu'en soit la casse ou les espaces
    /// périphériques (P3-10). L'argument est <b>normalisé par la méthode elle-même</b> via
    /// <see cref="MMV.Domain.Policies.UserIdentityPolicy.NormalizeUsername"/> : l'appelant passe la saisie brute
    /// et n'a aucune règle à connaître ni à dupliquer.
    /// </summary>
    /// <remarks>
    /// Remplace l'ancien <c>GetByUsernameAsync</c>, qui comparait <c>Username</c> en binaire : <c>admin</c> et
    /// <c>Admin</c> y désignaient deux comptes distincts (audit P3-10 §18, R2). Le nom porte désormais la clé
    /// réellement interrogée, pour qu'aucun appelant ne puisse croire à une recherche sensible à la casse.
    /// Une seule méthode de recherche est exposée : deux surcharges dont la différence serait implicite
    /// rouvriraient exactement l'ambiguïté corrigée ici.
    /// </remarks>
    Task<User?> GetByNormalizedUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indique si un <b>autre</b> utilisateur porte déjà cet identifiant de connexion (comparaison sur la forme
    /// normalisée). Sert de garde d'unicité avant écriture, avec un message métier lisible ; le filet réel reste
    /// l'index unique en base, qui arbitre les courses entre postes.
    /// </summary>
    /// <param name="username">Identifiant saisi (normalisé par la méthode).</param>
    /// <param name="excludingUserId">
    /// Utilisateur à exclure de la recherche — l'utilisateur en cours de modification, qui n'est pas son propre
    /// doublon. <c>null</c> à la création (aucune exclusion).
    /// </param>
    Task<bool> ExistsByNormalizedUsernameAsync(
        string username, long? excludingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Détache <paramref name="user"/> du suivi si le contexte le suit encore. Le rollback SQL d'une écriture
    /// rejetée par l'index unique de login est déjà garanti (<c>ITransactionRunner</c>) ; ce nettoyage couvre le
    /// suivi EF en mémoire, que le rollback transactionnel ne réinitialise pas — sans lui, une écriture
    /// ultérieure et sans rapport, dans la même portée de contexte, retenterait l'écriture rejetée.
    /// </summary>
    void DetachIfTracked(User user);

    /// <summary>
    /// Récupère tous les utilisateurs actifs.
    /// </summary>
    Task<IList<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les utilisateurs par rôle.
    /// </summary>
    Task<IList<User>> GetByRoleAsync(UserRole role, CancellationToken cancellationToken = default);
}
