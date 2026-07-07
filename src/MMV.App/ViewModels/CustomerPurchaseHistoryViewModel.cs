using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'historique d'achats d'un client.
/// Affiche toutes les ventes du client.
/// </summary>
public class CustomerPurchaseHistoryViewModel : BaseViewModel
{
    // P2D-5 : la lecture directe ISaleRepository.GetByCustomerIdAsync est remplacée par le query use case
    // Application, qui renvoie des DTO plats (CustomerSaleItemDto). Aucune entité EF suivie n'atteint plus l'écran.
    private readonly IGetCustomerPurchaseHistoryUseCase _getPurchaseHistoryUseCase;
    private long _customerId;

    /// <summary>
    /// Identifiant du client concerné.
    /// </summary>
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    /// <summary>
    /// Liste des ventes du client.
    /// </summary>
    public ObservableCollection<CustomerSaleItemDto> Sales { get; } = new();

    /// <summary>
    /// Indique s'il existe des ventes.
    /// </summary>
    public bool HasSales => Sales.Count > 0;

    /// <summary>
    /// Indique si un message d'erreur est présent.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public CustomerPurchaseHistoryViewModel(IGetCustomerPurchaseHistoryUseCase getPurchaseHistoryUseCase)
    {
        _getPurchaseHistoryUseCase = getPurchaseHistoryUseCase ?? throw new ArgumentNullException(nameof(getPurchaseHistoryUseCase));
        Title = "Historique d'achats";

        Sales.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSales));
        };
    }

    /// <summary>
    /// Charge l'historique d'achats du client (toutes les ventes).
    /// </summary>
    public async Task LoadAsync(long customerId)
    {
        CustomerId = customerId;
        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasError));
        Sales.Clear();

        try
        {
            // Charger toutes les ventes du client (tri décroissant par date hérité du repository via le use case).
            var sales = await _getPurchaseHistoryUseCase.ExecuteAsync(
                new GetCustomerPurchaseHistoryQuery { CustomerId = customerId });
            foreach (var sale in sales)
            {
                Sales.Add(sale);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement : {ex.Message}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
