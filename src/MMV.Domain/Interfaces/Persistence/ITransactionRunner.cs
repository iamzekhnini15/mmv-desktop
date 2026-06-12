namespace MMV.Domain.Interfaces.Persistence;

/// <summary>
/// Frontière transactionnelle réutilisable pour les opérations d'écriture composées de
/// plusieurs étapes (plusieurs <c>SaveChanges</c>, plusieurs agrégats).
///
/// <para>
/// Contrat (P2A-1C) :
/// <list type="bullet">
///   <item>l'<paramref name="operation"/> s'exécute dans une transaction unique ;</item>
///   <item>si l'opération réussit, la transaction est validée (commit) ;</item>
///   <item>si une exception survient à n'importe quel moment, la transaction est annulée
///         (rollback) : <b>aucune écriture partielle</b> n'est conservée ;</item>
///   <item>les erreurs de persistance techniques sont transformées en
///         <see cref="MMV.Domain.Exceptions.PersistenceException"/> contrôlée ; les autres
///         exceptions (règles métier, etc.) sont propagées inchangées.</item>
/// </list>
/// </para>
///
/// <para>
/// Abstraction volontairement neutre (aucune dépendance EF/SQLite) afin de rester compatible
/// avec la future couche Application / use cases (cf. ADR-001, R-05) : la logique d'orchestration
/// peut migrer des ViewModels vers des use cases sans changer cette frontière.
/// </para>
/// </summary>
public interface ITransactionRunner
{
    /// <summary>
    /// Exécute <paramref name="operation"/> dans une transaction (commit si succès, rollback si exception).
    /// </summary>
    Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Variante renvoyant un résultat (par ex. l'entité créée), même garanties transactionnelles.
    /// </summary>
    Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
}
