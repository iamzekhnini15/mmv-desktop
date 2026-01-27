using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du pattern Unit of Work pour gérer les transactions
/// et l'accès aux repositories de manière atomique.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly OpticDbContext _context;
    private IDbContextTransaction? _transaction;

    // Repositories
    private IUserRepository? _users;
    private ICustomerRepository? _customers;
    private IProductCategoryRepository? _productCategories;
    private ISupplierRepository? _suppliers;
    private IProductRepository? _products;
    private IPrescriptionRepository? _prescriptions;
    private IOrderRepository? _orders;
    private ISaleRepository? _sales;
    private IStockMovementRepository? _stockMovements;

    public UnitOfWork(OpticDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // Lazy-loaded repositories properties
    public IUserRepository Users => 
        _users ??= new UserRepository(_context);

    public ICustomerRepository Customers => 
        _customers ??= new CustomerRepository(_context);

    public IProductCategoryRepository ProductCategories => 
        _productCategories ??= new ProductCategoryRepository(_context);

    public ISupplierRepository Suppliers => 
        _suppliers ??= new SupplierRepository(_context);

    public IProductRepository Products => 
        _products ??= new ProductRepository(_context);

    public IPrescriptionRepository Prescriptions => 
        _prescriptions ??= new PrescriptionRepository(_context);

    public IOrderRepository Orders => 
        _orders ??= new OrderRepository(_context);

    public ISaleRepository Sales => 
        _sales ??= new SaleRepository(_context);

    public IStockMovementRepository StockMovements => 
        _stockMovements ??= new StockMovementRepository(_context);

    /// <summary>
    /// Sauvegarde tous les changements en attente dans la base de données.
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Annule tous les changements en attente.
    /// </summary>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _context.DisposeAsync();
    }

    /// <summary>
    /// Commence une nouvelle transaction.
    /// </summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Valide et persiste la transaction actuelle.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            
            if (_transaction != null)
            {
                await _transaction.CommitAsync(cancellationToken);
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Annule la transaction actuelle.
    /// </summary>
    private async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
        }
        
        await _context.DisposeAsync();
    }
}
