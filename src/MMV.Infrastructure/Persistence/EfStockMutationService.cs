using Microsoft.EntityFrameworkCore;
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
            .Where(p => p.ProductId == productId)
            .Select(p => (int?)p.StockQuantity)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        throw new InsufficientStockException(productId, quantity, available ?? 0);
    }
}
