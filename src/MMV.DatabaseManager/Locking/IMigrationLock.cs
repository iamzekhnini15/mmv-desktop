namespace MMV.DatabaseManager.Locking;

/// <summary>
/// Port du verrou de migration (P4-6B, DP-2). <b>Seule autorité de sérialisation</b> des exécutions de l'outil :
/// <c>maintenance_started_at</c> n'est jamais un verrou.
/// </summary>
public interface IMigrationLock : IAsyncDisposable
{
    /// <summary>Tente d'acquérir le verrou pendant au plus <paramref name="wait"/> ; jamais d'attente infinie.</summary>
    Task<bool> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken = default);

    /// <summary>Décrit l'exécution qui détient le verrou, si le serveur la laisse voir ; sinon <c>null</c>.</summary>
    Task<string?> DescribeHolderAsync(CancellationToken cancellationToken = default);

    /// <summary>Libère explicitement le verrou s'il est détenu, puis ferme sa session.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
