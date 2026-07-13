namespace MMV.Application.UseCases.Customers.ListCustomers;

/// <summary>
/// Entrée (query) du use case <see cref="ListCustomersUseCase"/> — « Lister les clients » (P2D-7C, étendue en
/// P3-2B). La recherche textuelle reste en présentation (<c>CustomersListViewModel.SearchText</c>) ; seul le
/// filtre d'archivage est porté par la query, car il doit être appliqué en base.
/// </summary>
public sealed class ListCustomersQuery
{
    /// <summary>
    /// Inclure les clients archivés (P3-2B). <c>false</c> par défaut : les clients archivés sont exclus, ce qui
    /// préserve le comportement des appels existants (aucun client n'est archivé avant cette étape) tout en
    /// laissant l'UI P3-2C offrir un filtre « afficher les archivés ».
    /// </summary>
    public bool IncludeArchived { get; init; }
}
