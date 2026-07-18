using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Persistence;

/// <summary>
/// Implémentation EF Core 8 / SQLite de <see cref="IStockMutationService"/> (P2A-1D, R-09).
///
/// <para>
/// Le décrément est réalisé par un <b>UPDATE conditionnel atomique</b> :
/// <code>
/// UPDATE Products
/// SET StockQuantity = StockQuantity - @quantity
/// WHERE ProductId = @productId
///   AND StockQuantity &gt;= @quantity
/// </code>
/// généré par <see cref="RelationalQueryableExtensions"/> /
/// <c>ExecuteUpdateAsync</c>. La base décide en une seule instruction :
/// <list type="bullet">
///   <item><b>1 ligne affectée</b> ⇒ stock suffisant, décrément appliqué ;</item>
///   <item><b>0 ligne affectée</b> ⇒ stock insuffisant, produit introuvable, ou stock modifié
///         concurremment ⇒ <see cref="InsufficientStockException"/> (aucune écriture).</item>
/// </list>
/// </para>
///
/// <para>
/// Partage le <see cref="OpticDbContext"/> de la portée DI : l'<c>UPDATE</c> s'exécute sur la même
/// connexion et donc dans la <b>transaction courante</b> ouverte par <c>EfTransactionRunner</c>. Un échec
/// (<see cref="InsufficientStockException"/>) annule l'écriture composée de la vente (atomicité P2A-1C).
/// </para>
///
/// <para>
/// Limite SQLite assumée : pas de <c>rowversion</c> natif (d'où l'update conditionnel plutôt qu'un token
/// de ligne) ; écrivain unique (les écritures sont sérialisées) — l'update conditionnel reste correct que
/// les transactions soient sérialisées ou entrelacées. <c>ExecuteUpdateAsync</c> contourne le change
/// tracker : l'écriture est directe en base, sans double décrément d'une entité suivie.
/// </para>
/// </summary>
public sealed class EfStockMutationService : IStockMutationService
{
    private readonly OpticDbContext _context;

    public EfStockMutationService(OpticDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task DecrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "La quantité à décrémenter doit être strictement positive.");
        }

        // Décrément atomique conditionnel : n'affecte la ligne que si le stock disponible le permet.
        var rowsAffected = await _context.Products
            .Where(p => p.ProductId == productId && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected == 1)
        {
            return; // Stock décrémenté avec succès.
        }

        // 0 ligne : stock insuffisant, produit introuvable, ou stock modifié entre-temps.
        // On relit le stock courant (0 si le produit n'existe pas) pour un message utilisateur précis.
        var available = await _context.Products
            .AsNoTracking()
            .Where(p => p.ProductId == productId)
            .Select(p => (int?)p.StockQuantity)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        throw new InsufficientStockException(productId, quantity, available ?? 0);
    }

    /// <inheritdoc />
    public async Task<int> IncrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "La quantité à incrémenter doit être strictement positive.");
        }

        // Incrément atomique : SET StockQuantity = StockQuantity + q (jamais un read-modify-write en mémoire).
        // Deux entrées concurrentes s'additionnent sans perte (chacune est un UPDATE relatif).
        var rowsAffected = await _context.Products
            .Where(p => p.ProductId == productId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected == 0)
        {
            throw new EntityNotFoundException(nameof(Product), productId);
        }

        // Relecture SANS tracking : le produit peut être suivi par la portée avec une valeur obsolète ; on veut
        // la valeur réellement écrite en base après l'incrément atomique.
        return await _context.Products
            .AsNoTracking()
            .Where(p => p.ProductId == productId)
            .Select(p => p.StockQuantity)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StockAdjustmentResult> AdjustStockToAsync(long productId, int targetQuantity, CancellationToken cancellationToken = default)
    {
        if (targetQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetQuantity), targetQuantity, "La quantité comptée d'un ajustement ne peut pas être négative (zéro autorisé).");
        }

        // 1) Lire la valeur courante SANS tracking (valeur réelle en base, jamais une entité suivie obsolète).
        var current = await _context.Products
            .AsNoTracking()
            .Where(p => p.ProductId == productId)
            .Select(p => (int?)p.StockQuantity)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            throw new EntityNotFoundException(nameof(Product), productId);
        }

        var previous = current.Value;

        // 2) Mise à jour conditionnelle basée sur la valeur lue : n'écrit QUE si le stock n'a pas bougé entre-temps.
        var rowsAffected = await _context.Products
            .Where(p => p.ProductId == productId && p.StockQuantity == previous)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.StockQuantity, targetQuantity),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected == 0)
        {
            // Un autre poste a modifié le stock entre la lecture et l'écriture : refus explicite (pas de last-write-wins).
            throw new StockConcurrencyConflictException(productId, previous);
        }

        return new StockAdjustmentResult(productId, previous, targetQuantity);
    }
}
