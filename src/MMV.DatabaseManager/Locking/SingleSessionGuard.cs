using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MMV.DatabaseManager.Locking;

/// <summary>
/// Garde de session unique (P4-6B, décision d'architecte du 04/10/2026) : le verrou consultatif et
/// <c>Migrate()</c> partagent <b>une seule</b> session PostgreSQL, ouverte par l'acquisition du verrou.
/// EF ne doit <b>jamais</b> l'ouvrir lui-même : si la session meurt, EF la remplacerait sinon par une session
/// neuve, sans verrou, et la migration continuerait. Toute ouverture initiée par EF est donc refusée.
/// </summary>
public sealed class SingleSessionGuard : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        throw Refusal();

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        throw Refusal();

    private static InvalidOperationException Refusal() => new(
        "Ouverture de connexion par EF refusée : seule l'acquisition du verrou ouvre la session de migration ; " +
        "une session perdue a perdu le verrou et n'est jamais remplacée.");
}
