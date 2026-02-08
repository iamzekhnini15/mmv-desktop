using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'affichage des informations personnelles d'un client avec son historique d'achats.
/// </summary>
public class CustomerInfoViewModel : BaseViewModel
{
    private readonly IOrderRepository? _orderRepository;
    private Customer? _customer;

    /// <summary>
    /// Client dont on affiche les informations.
    /// </summary>
    public Customer? Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    /// <summary>
    /// Liste des commandes du client.
    /// </summary>
    public ObservableCollection<Order> Orders { get; } = new();

    /// <summary>
    /// Indique s'il existe des commandes.
    /// </summary>
    public bool HasOrders => Orders.Count > 0;

    public CustomerInfoViewModel(Customer customer, IOrderRepository? orderRepository = null)
    {
        Customer = customer;
        _orderRepository = orderRepository;
        Title = "Informations Personnelles";
    }

    /// <summary>
    /// Charge l'historique d'achats du client.
    /// </summary>
    public async Task LoadOrdersAsync()
    {
        if (_orderRepository == null || Customer == null)
            return;

        IsLoading = true;
        Orders.Clear();

        try
        {
            var orders = await _orderRepository.GetByCustomerIdAsync(Customer.CustomerId);
            foreach (var order in orders)
            {
                Orders.Add(order);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement de l'historique : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasOrders));
        }
    }
}
