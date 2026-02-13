using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'historique d'achats d'un client.
/// Affiche toutes les ventes du client.
/// </summary>
public class CustomerPurchaseHistoryViewModel : BaseViewModel
{
    private readonly ISaleRepository _saleRepository;
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
    public ObservableCollection<Sale> Sales { get; } = new();

    /// <summary>
    /// Indique s'il existe des ventes.
    /// </summary>
    public bool HasSales => Sales.Count > 0;

    /// <summary>
    /// Indique si un message d'erreur est présent.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public CustomerPurchaseHistoryViewModel(ISaleRepository saleRepository)
    {
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
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
            // Charger toutes les ventes du client
            var sales = await _saleRepository.GetByCustomerIdAsync(customerId);
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
