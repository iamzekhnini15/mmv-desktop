using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'historique d'achats d'un client.
/// </summary>
public class CustomerPurchaseHistoryViewModel : BaseViewModel
{
    private readonly IOrderRepository _orderRepository;
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
    /// Liste des commandes du client.
    /// </summary>
    public ObservableCollection<Order> Orders { get; } = new();

    /// <summary>
    /// Indique s'il existe des commandes.
    /// </summary>
    public bool HasOrders => Orders.Count > 0;

    /// <summary>
    /// Indique si un message d'erreur est présent.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public CustomerPurchaseHistoryViewModel(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        Title = "Historique d'achats";

        Orders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOrders));
    }

    /// <summary>
    /// Charge l'historique d'achats du client.
    /// </summary>
    public async Task LoadAsync(long customerId)
    {
        CustomerId = customerId;
        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(HasError));
        Orders.Clear();

        try
        {
            var orders = await _orderRepository.GetByCustomerIdAsync(customerId);
            foreach (var order in orders)
            {
                Orders.Add(order);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des achats : {ex.Message}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasOrders));
            OnPropertyChanged(nameof(HasError));
        }
    }
}
