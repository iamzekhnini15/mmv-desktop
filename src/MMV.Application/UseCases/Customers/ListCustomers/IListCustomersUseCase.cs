using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.ListCustomers;

/// <summary>
/// Query use case « Lister les clients » (P2D-7C). Remplace <c>ICustomerRepository.GetAllAsync</c> côté UI
/// (<c>CustomersListViewModel</c>).
/// </summary>
public interface IListCustomersUseCase
{
    /// <summary>
    /// Renvoie tous les clients, projetés en <see cref="CustomerListItemDto"/> plats (jamais l'entité EF
    /// <c>Customer</c>). Recherche/filtre restent en présentation (iso-fonctionnel).
    /// </summary>
    Task<IReadOnlyList<CustomerListItemDto>> ExecuteAsync(ListCustomersQuery query, CancellationToken cancellationToken = default);
}
