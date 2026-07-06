using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Customers.ListCustomersForPicker;

/// <summary>
/// Implémentation du query use case « Lister les clients pour un sélecteur » (P2D-6). Déplace, sans changement de
/// comportement observable, la lecture <c>ICustomerRepository.GetAllAsync</c> qu'effectuait
/// <c>OrderFormViewModel.InitializeAsync</c> pour peupler son sélecteur de client, en projetant chaque entité
/// <c>Customer</c> vers un <see cref="CustomerPickerItemDto"/> plat et en conservant le tri par nom appliqué par le
/// formulaire.
/// </summary>
public sealed class ListCustomersForPickerUseCase : IListCustomersForPickerUseCase
{
    private readonly ICustomerRepository _customerRepository;

    public ListCustomersForPickerUseCase(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerPickerItemDto>> ExecuteAsync(ListCustomersForPickerQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var customers = await _customerRepository.GetAllAsync(cancellationToken);

        return customers
            .OrderBy(c => c.LastName)
            .Select(c => new CustomerPickerItemDto
            {
                CustomerId = c.CustomerId,
                FirstName = c.FirstName,
                LastName = c.LastName,
                Phone = c.Phone,
                Email = c.Email,
            })
            .ToList();
    }
}
