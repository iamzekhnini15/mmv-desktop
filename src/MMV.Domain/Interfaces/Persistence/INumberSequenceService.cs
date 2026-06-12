namespace MMV.Domain.Interfaces.Persistence;

/// <summary>
/// Génération <b>fiable</b> des numéros de document (P2A-1E, R-03 / ADR-006).
///
/// <para>
/// Objectif : remplacer la génération <i>aléatoire</i> et non bornée des numéros critiques
/// (<c>SaleNumber</c> = <c>VTE-{yyyy}-{Random.Next(10000)}</c>, <c>OrderNumber</c> idem ; comptage
/// <c>count + 1</c> côté commande) par une attribution <b>déterministe, séquentielle et unique</b> issue
/// d'un compteur persistant (<see cref="MMV.Domain.Entities.DocumentSequence"/>). La génération aléatoire
/// pouvait entrer en collision avec les index UNIQUE <c>SaleNumber</c>/<c>OrderNumber</c> (vente refusée)
/// et rendait tout audit impossible.
/// </para>
///
/// <para>
/// Garanties du contrat :
/// <list type="bullet">
///   <item>chaque appel renvoie un numéro <b>unique</b> pour la séquence demandée ;</item>
///   <item>deux séquences (<c>SALE</c>, <c>ORDER</c>) sont <b>indépendantes</b> ;</item>
///   <item>sous appels <b>concurrents</b>, aucun numéro n'est attribué deux fois (incrément atomique
///         conditionnel) ;</item>
///   <item>l'incrément participe à la <b>transaction courante</b> ouverte par
///         <see cref="ITransactionRunner"/> lorsque le service partage le même contexte de portée : si la
///         vente/commande est annulée, le numéro n'est <b>pas</b> consommé (rollback du compteur).</item>
/// </list>
/// </para>
///
/// <para>
/// Abstraction volontairement neutre (aucune dépendance EF/SQLite) afin de rester compatible avec la future
/// couche Application / use cases et une extension ultérieure par magasin/pays/exercice (le découpage sera
/// encodé dans le nom de séquence, sans changer ce contrat).
/// </para>
/// </summary>
public interface INumberSequenceService
{
    /// <summary>
    /// Attribue et renvoie le <b>prochain numéro</b> de la séquence <paramref name="sequenceName"/>
    /// (ex. <c>VTE-000001</c>). L'attribution est atomique : un même numéro ne peut pas être renvoyé deux
    /// fois, même sous concurrence.
    /// </summary>
    /// <param name="sequenceName">
    /// Nom logique de la séquence (cf. <see cref="DocumentSequenceNames"/>), ex. <c>SALE</c> ou <c>ORDER</c>.
    /// </param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le numéro formaté (préfixe + valeur séquentielle zéro-paddée).</returns>
    /// <exception cref="System.ArgumentException">
    /// si <paramref name="sequenceName"/> est nul ou vide (erreur d'appel).
    /// </exception>
    /// <exception cref="MMV.Domain.Exceptions.NumberSequenceException">
    /// si la séquence est introuvable (non initialisée) ou si aucun numéro n'a pu être attribué malgré les
    /// tentatives (contention persistante) : <b>aucun</b> numéro n'est consommé.
    /// </exception>
    Task<string> NextNumberAsync(string sequenceName, CancellationToken cancellationToken = default);
}
