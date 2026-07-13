using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.ListCustomersForPicker;

/// <summary>
/// Query use case « Lister les clients pour un sélecteur » (P2D-6). Remplace <c>ICustomerRepository.GetAllAsync</c>
/// côté UI pour le sélecteur de client du formulaire de commande (<c>OrderFormViewModel</c>).
/// </summary>
public interface IListCustomersForPickerUseCase
{
    /// <summary>
    /// Renvoie les clients <b>actifs</b> (tri par nom, comme le formulaire d'origine), projetés en
    /// <see cref="CustomerPickerItemDto"/> plats (jamais l'entité EF <c>Customer</c>). P3-2B : les clients
    /// archivés sont <b>toujours</b> exclus — un sélecteur ne doit jamais permettre de rattacher un nouveau
    /// document à un client archivé.
    /// </summary>
    Task<IReadOnlyList<CustomerPickerItemDto>> ExecuteAsync(ListCustomersForPickerQuery query, CancellationToken cancellationToken = default);
}
