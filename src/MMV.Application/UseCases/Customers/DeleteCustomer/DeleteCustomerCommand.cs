namespace MMV.Application.UseCases.Customers.DeleteCustomer;

/// <summary>
/// Entrée (DTO) du use case <see cref="DeleteCustomerUseCase"/> — « Supprimer un client » (P2C-3). Porte
/// l'identifiant du client à supprimer, tel que sélectionné dans <c>CustomersListViewModel</c> ; la ViewModel
/// transforme son état (<c>SelectedCustomer.CustomerId</c>) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de
/// <c>CustomersListViewModel.ExecuteDelete</c> (qui appelait directement
/// <c>ICustomerRepository.DeleteAsync(id)</c> + <c>IUnitOfWork.SaveChangesAsync()</c>).
/// </remarks>
public sealed class DeleteCustomerCommand
{
    /// <summary>Identifiant du client à supprimer.</summary>
    public long CustomerId { get; init; }
}
