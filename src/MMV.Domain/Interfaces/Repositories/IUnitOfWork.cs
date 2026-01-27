namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du pattern Unit of Work pour la gestion des transactions.
/// Tous les repositories partagent le même DbContext et la même transaction.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Repository des utilisateurs.
    /// </summary>
    IUserRepository Users { get; }

    /// <summary>
    /// Repository des clients.
    /// </summary>
    ICustomerRepository Customers { get; }

    /// <summary>
    /// Repository des catégories de produits.
    /// </summary>
    IProductCategoryRepository ProductCategories { get; }

    /// <summary>
    /// Repository des fournisseurs.
    /// </summary>
    ISupplierRepository Suppliers { get; }

    /// <summary>
    /// Repository des produits.
    /// </summary>
    IProductRepository Products { get; }

    /// <summary>
    /// Repository des ordonnances.
    /// </summary>
    IPrescriptionRepository Prescriptions { get; }

    /// <summary>
    /// Repository des commandes.
    /// </summary>
    IOrderRepository Orders { get; }

    /// <summary>
    /// Repository des ventes.
    /// </summary>
    ISaleRepository Sales { get; }

    /// <summary>
    /// Repository des mouvements de stock.
    /// </summary>
    IStockMovementRepository StockMovements { get; }

    /// <summary>
    /// Sauvegarde tous les changements dans une seule transaction.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Annule tous les changements non sauvegardés.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commence une nouvelle transaction.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide et sauvegarde la transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
