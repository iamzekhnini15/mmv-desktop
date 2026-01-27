using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Domain.Services;

/// <summary>
/// Service métier pour la gestion des clients.
/// </summary>
public interface ICustomerService
{
    Task<Customer?> GetCustomerAsync(long customerId, CancellationToken cancellationToken = default);
    Task<IList<Customer>> SearchCustomersAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}

public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;

    public CustomerService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Customer?> GetCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        return await _unitOfWork.Customers.GetWithHistoryAsync(customerId, cancellationToken);
    }

    public async Task<IList<Customer>> SearchCustomersAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) throw new ArgumentException("Terme de recherche requis.", nameof(searchTerm));
        return await _unitOfWork.Customers.SearchByNameAsync(searchTerm, cancellationToken);
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        
        customer.CreatedAt = DateTime.UtcNow;
        customer.UpdatedAt = DateTime.UtcNow;
        
        await _unitOfWork.Customers.CreateAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return customer;
    }

    public async Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        
        var existing = await GetCustomerAsync(customer.CustomerId, cancellationToken);
        if (existing == null) throw new Exceptions.EntityNotFoundException(nameof(Customer), customer.CustomerId);
        
        customer.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Customers.UpdateAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCustomerAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        
        await _unitOfWork.Customers.DeleteAsync(customerId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
