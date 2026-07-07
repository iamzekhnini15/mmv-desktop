using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Customers.ListCustomers;

/// <summary>
/// Implémentation du query use case « Lister les clients » (P2D-7C). Déplace, sans changement de comportement
/// observable, la lecture portée jusque-là par <c>CustomersListViewModel.LoadCustomersAsync</c>
/// (<c>ICustomerRepository.GetAllAsync</c>), en projetant chaque entité vers un <see cref="CustomerListItemDto"/>
/// plat.
/// </summary>
public sealed class ListCustomersUseCase : IListCustomersUseCase
{
    private readonly ICustomerRepository _customerRepository;

    public ListCustomersUseCase(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerListItemDto>> ExecuteAsync(ListCustomersQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var customers = await _customerRepository.GetAllAsync(cancellationToken);

        return customers.Select(c => new CustomerListItemDto
        {
            CustomerId = c.CustomerId,
            FirstName = c.FirstName,
            LastName = c.LastName,
            BirthDate = c.BirthDate,
            Phone = c.Phone,
            Email = c.Email,
            Address = c.Address,
            City = c.City,
            PostalCode = c.PostalCode,
            SocialSecurityNumber = c.SocialSecurityNumber,
            InsuranceName = c.InsuranceName,
            Notes = c.Notes,
            CreatedAt = c.CreatedAt,
        }).ToList();
    }
}
