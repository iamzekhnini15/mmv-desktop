using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Domain.Services;

/// <summary>
/// Service métier pour la gestion des produits.
/// </summary>
public interface IProductService
{
    Task<Product?> GetProductAsync(long productId, CancellationToken cancellationToken = default);
    Task<IList<Product>> GetProductsByCategoryAsync(long categoryId, CancellationToken cancellationToken = default);
    Task<IList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default);
    Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateProductAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteProductAsync(long productId, CancellationToken cancellationToken = default);
    decimal CalculateMarginPercentage(Product product);
}

public class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Product?> GetProductAsync(long productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) throw new ArgumentException("ID produit invalide.", nameof(productId));
        return await _unitOfWork.Products.GetByIdAsync(productId, cancellationToken);
    }

    public async Task<IList<Product>> GetProductsByCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0) throw new ArgumentException("ID catégorie invalide.", nameof(categoryId));
        return await _unitOfWork.Products.GetByCategoryAsync(categoryId, cancellationToken);
    }

    public async Task<IList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Products.GetLowStockProductsAsync(cancellationToken);
    }

    public async Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        
        if (product.SalePrice <= 0) 
            throw new Exceptions.BusinessRuleException("Le prix de vente doit être positif.");
        
        await _unitOfWork.Products.CreateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return product;
    }

    public async Task UpdateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        
        var existing = await GetProductAsync(product.ProductId, cancellationToken);
        if (existing == null) throw new Exceptions.EntityNotFoundException(nameof(Product), product.ProductId);
        
        await _unitOfWork.Products.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProductAsync(long productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) throw new ArgumentException("ID produit invalide.", nameof(productId));
        
        await _unitOfWork.Products.DeleteAsync(productId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public decimal CalculateMarginPercentage(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        
        if (product.PurchasePrice == 0) return 0;
        
        return ((product.SalePrice - product.PurchasePrice) / product.PurchasePrice) * 100;
    }
}
