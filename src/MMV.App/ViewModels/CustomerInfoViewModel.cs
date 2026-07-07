using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'affichage des informations personnelles d'un client avec son historique d'achats.
/// </summary>
public class CustomerInfoViewModel : BaseViewModel
{
    // P2D-5 : la lecture directe ISaleRepository.GetByCustomerIdAsync est remplacée par le query use case Application,
    // qui renvoie des DTO plats (CustomerSaleItemDto). Reste optionnel (null) pour préserver le comportement d'origine
    // (aucun chargement de ventes si non fourni).
    private readonly IGetCustomerPurchaseHistoryUseCase? _getPurchaseHistoryUseCase;
    private CustomerListItemDto? _customer;

    /// <summary>
    /// Client dont on affiche les informations.
    /// </summary>
    /// <remarks>P2D-7C : entité EF <c>Customer</c> remplacée par le DTO applicatif <see cref="CustomerListItemDto"/>.</remarks>
    public CustomerListItemDto? Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    /// <summary>
    /// Liste des ventes du client.
    /// </summary>
    public ObservableCollection<CustomerSaleItemDto> Sales { get; } = new();

    /// <summary>
    /// Indique s'il existe des ventes.
    /// </summary>
    public bool HasSales => Sales.Count > 0;

    public CustomerInfoViewModel(CustomerListItemDto customer, IGetCustomerPurchaseHistoryUseCase? getPurchaseHistoryUseCase = null)
    {
        Customer = customer;
        _getPurchaseHistoryUseCase = getPurchaseHistoryUseCase;
        Title = "Informations Personnelles";
    }

    /// <summary>
    /// Charge l'historique d'achats du client.
    /// </summary>
    public async Task LoadSalesAsync()
    {
        if (_getPurchaseHistoryUseCase == null || Customer == null)
            return;

        IsLoading = true;
        Sales.Clear();

        try
        {
            var sales = await _getPurchaseHistoryUseCase.ExecuteAsync(
                new GetCustomerPurchaseHistoryQuery { CustomerId = Customer.CustomerId });
            foreach (var sale in sales)
            {
                Sales.Add(sale);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement de l'historique : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasSales));
        }
    }
}
