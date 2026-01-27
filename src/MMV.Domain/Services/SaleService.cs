using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Domain.Services;

/// <summary>
/// Service métier pour la gestion des ventes.
/// </summary>
public interface ISaleService
{
    Task<Sale?> GetSaleAsync(long saleId, CancellationToken cancellationToken = default);
    Task<IList<Sale>> GetCustomerSalesAsync(long customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<Sale> CreateSaleAsync(Sale sale, CancellationToken cancellationToken = default);
    Task<Sale> CalculateSaleAsync(Sale sale);
}

public class SaleService : ISaleService
{
    private readonly IUnitOfWork _unitOfWork;

    public SaleService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Sale?> GetSaleAsync(long saleId, CancellationToken cancellationToken = default)
    {
        if (saleId <= 0) throw new ArgumentException("ID vente invalide.", nameof(saleId));
        return await _unitOfWork.Sales.GetWithItemsAsync(saleId, cancellationToken);
    }

    public async Task<IList<Sale>> GetCustomerSalesAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        return await _unitOfWork.Sales.GetByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (startDate > endDate) throw new ArgumentException("Date de début doit être avant date de fin.");
        return await _unitOfWork.Sales.GetTotalSalesAsync(startDate, endDate, cancellationToken);
    }

    public async Task<Sale> CreateSaleAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        
        if (sale.SaleItems == null || sale.SaleItems.Count == 0)
            throw new Exceptions.BusinessRuleException("Une vente doit contenir au moins un article.");
        
        sale = await CalculateSaleAsync(sale);
        sale.SaleDate = DateTime.UtcNow;
        sale.PaymentStatus = PaymentStatus.Paid; // Par défaut au paiement
        
        await _unitOfWork.Sales.CreateAsync(sale, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return sale;
    }

    public async Task<Sale> CalculateSaleAsync(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        
        if (sale.SaleItems == null || sale.SaleItems.Count == 0)
            throw new Exceptions.BusinessRuleException("Pas d'articles à calculer.");
        
        // Calculer TotalAmount
        sale.TotalAmount = sale.SaleItems.Sum(s => s.TotalPrice);
        
        // Appliquer remise si existe
        if (sale.DiscountAmount < 0)
            throw new Exceptions.BusinessRuleException("La remise ne peut pas être négative.");
        
        // Calculer montant final
        sale.FinalAmount = sale.TotalAmount - sale.DiscountAmount;
        
        if (sale.FinalAmount < 0)
            throw new Exceptions.BusinessRuleException("La remise ne peut pas dépasser le total.");
        
        return await Task.FromResult(sale);
    }
}
