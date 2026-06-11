using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Persistence;

/// <summary>
/// Implémentation EF Core / SQLite de <see cref="ITransactionRunner"/>.
///
/// <para>
/// Ouvre une transaction explicite sur le <see cref="OpticDbContext"/> partagé (même instance que
/// les repositories et l'<c>UnitOfWork</c> dans une portée DI), exécute l'opération, puis valide
/// (commit) en cas de succès. En cas d'exception : <b>rollback</b> garanti (annulation explicite
/// + annulation à la libération de la transaction si non validée), puis transformation des erreurs
/// de persistance via <see cref="PersistenceErrorMapper"/>.
/// </para>
///
/// <para>
/// Remplace l'usage de <c>UnitOfWork.RollbackAsync</c> (qui se contentait de disposer le contexte,
/// R-23) sur le flux protégé : la frontière transactionnelle est désormais explicite et testée.
/// </para>
///
/// <para>
/// Limite SQLite assumée : le provider SQLite par défaut n'a pas de stratégie d'exécution avec
/// re-tentative, donc une transaction manuelle est correcte ; l'idempotence/re-tentative relèvera
/// d'un futur provider serveur et de la couche Application (hors P2A-1C).
/// </para>
/// </summary>
public sealed class EfTransactionRunner : ITransactionRunner
{
    private readonly OpticDbContext _context;

    public EfTransactionRunner(OpticDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RunAsync<object?>(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Frontière imbriquée : une transaction est déjà active (cas futur use case appelant un autre).
        // On s'y rattache sans en ouvrir une seconde (SQLite ne supporte pas les transactions imbriquées).
        if (_context.Database.CurrentTransaction != null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);

            // Transforme les erreurs de persistance en erreur contrôlée ;
            // toute autre exception est propagée inchangée (stack trace préservée).
            var mapped = PersistenceErrorMapper.Map(ex);
            if (ReferenceEquals(mapped, ex))
            {
                throw;
            }

            throw mapped;
        }
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            // Rollback explicite avec CancellationToken.None : l'annulation doit aboutir même si le
            // jeton d'origine est annulé. À défaut, la libération (await using) annule aussi la
            // transaction non validée.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best effort : la transaction sera de toute façon annulée à la libération si non validée.
        }
    }
}
