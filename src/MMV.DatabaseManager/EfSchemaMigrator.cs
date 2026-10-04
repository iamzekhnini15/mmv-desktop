using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>Port de la chaîne de migrations EF.</summary>
public interface ISchemaMigrator
{
    /// <summary>Migrations connues du binaire (<c>GetMigrations()</c>, assembly de migrations PostgreSQL).</summary>
    IReadOnlyList<string> KnownMigrations { get; }

    /// <summary>Migrations appliquées (historique EF) ; vide si l'historique n'existe pas.</summary>
    Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken = default);

    /// <summary>Applique la chaîne (étape 8). Jamais dans une transaction de l'outil.</summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>Migrate()</c> d'EF Core sur la session <b>qui tient le verrou</b> (P4-6B, §3.1.1 ; §3.3 amendé le
/// 04/10/2026 : session unique). <c>lock_timeout</c> est posé en SQL de session (CX-1) : un <c>ALTER TABLE</c> bloqué
/// derrière une session de poste échoue proprement au lieu d'attendre. La configuration EF de
/// <see cref="OpticDbContext"/> n'est pas modifiée ; le verrou natif d'EF reste actif.
/// </summary>
public sealed class EfSchemaMigrator : ISchemaMigrator
{
    private readonly OpticDbContext _context;
    private readonly TimeSpan _lockTimeout;
    private bool _sessionPrepared;

    /// <param name="context">Contexte dédié à la migration.</param>
    /// <param name="lockTimeout">Délai d'attente d'un verrou de table pour la session qui migre (CX-1).</param>
    public EfSchemaMigrator(OpticDbContext context, TimeSpan lockTimeout)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (lockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockTimeout), "lock_timeout doit être borné et positif.");
        }

        _lockTimeout = lockTimeout;
        KnownMigrations = context.Database.GetMigrations().ToArray();
    }

    public IReadOnlyList<string> KnownMigrations { get; }

    public async Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken = default)
    {
        await PrepareSessionAsync(cancellationToken);
        return (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await PrepareSessionAsync(cancellationToken);
        await _context.Database.MigrateAsync(cancellationToken);
    }

    private async Task PrepareSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionPrepared)
        {
            return;
        }

        // La session est ouverte par l'acquisition du verrou, jamais ici (session unique, SingleSessionGuard).
        ServerCommand.RequireOpen(_context.Database.GetDbConnection());
        var milliseconds = ((long)_lockTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        await ServerCommand.NonQueryAsync(_context.Database.GetDbConnection(),
            "SET lock_timeout = " + milliseconds, cancellationToken);
        _sessionPrepared = true;
    }
}
