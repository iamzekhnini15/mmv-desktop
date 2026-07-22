namespace MMV.Domain.Policies;

/// <summary>
/// Propriétaire <b>unique</b> de la normalisation de l'identifiant de connexion (P3-10). Domain pur : aucune
/// dépendance EF, SQLite, Application ni UI.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi une clé normalisée.</b> Avant P3-10, l'unicité du login reposait sur <c>User.Username</c> comparé
/// en binaire, côté Application comme côté base (index unique SQLite, collation BINARY). <c>admin</c>,
/// <c>Admin</c> et <c>ADMIN</c> désignaient donc <b>trois comptes distincts</b> qu'un exploitant tient pour un
/// seul : deux postes pouvaient créer des logins « équivalents », et l'authentification devenait ambiguë (audit
/// P3-10 §18, R2). La normalisation transforme cette ambiguïté en égalité décidable.
/// </para>
/// <para>
/// <b>Un seul propriétaire.</b> Recopier <c>Trim().ToLowerInvariant()</c> dans le use case, le repository, le
/// service d'authentification et le seed créerait quatre définitions qui peuvent diverger — exactement le défaut
/// que P3-10 corrige. Tous ces chemins appellent donc cette méthode, et elle seule (garde d'architecture, §33).
/// </para>
/// <para>
/// <b>ToLowerInvariant, pas ToLower.</b> La culture de la machine ne doit jamais influer sur l'identité d'un
/// compte : sous une culture turque, <c>ToLower()</c> replierait <c>I</c> sur <c>ı</c> (i sans point), et le même
/// login normaliserait différemment selon le poste. <c>ToLowerInvariant</c> est stable partout, et — point
/// décisif pour le backfill — coïncide exactement avec le <c>lower()</c> natif de SQLite sur le jeu ASCII, seul
/// jeu que <see cref="MMV.Domain.Validators.UserValidator"/> autorise (§19).
/// </para>
/// </remarks>
public static class UserIdentityPolicy
{
    /// <summary>
    /// Longueur maximale de l'identifiant de connexion, partagée par <c>Username</c> et sa forme normalisée.
    /// Alignée sur <see cref="MMV.Domain.Validators.UserValidator"/> et sur la configuration EF.
    /// </summary>
    public const int UsernameMaxLength = 50;

    /// <summary>
    /// Calcule la forme normalisée d'un identifiant de connexion : espaces périphériques retirés, casse repliée
    /// en invariant. C'est la <b>clé métier</b> de recherche et d'unicité ; <c>User.Username</c> ne conserve que
    /// la forme affichable saisie par l'exploitant.
    /// </summary>
    /// <param name="username">
    /// Identifiant saisi. <c>null</c> produit une chaîne vide plutôt qu'une exception : une entrée manquante
    /// reste une entrée <b>invalide</b>, refusée en aval par <see cref="MMV.Domain.Validators.UserValidator"/>
    /// (<c>NotEmpty</c>) avec un message de saisie lisible. Lever ici transformerait une erreur de saisie en
    /// panne technique, contre la convention P3-1.
    /// </param>
    /// <returns>La forme normalisée (chaîne vide si l'entrée est nulle ou entièrement blanche).</returns>
    public static string NormalizeUsername(string? username)
        => username is null ? string.Empty : username.Trim().ToLowerInvariant();
}
