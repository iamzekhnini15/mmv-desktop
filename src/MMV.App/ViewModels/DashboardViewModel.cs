using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la page Tableau de Bord.
/// Affiche les statistiques clés de l'application.
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly INavigationService? _navigationService;
    private int _totalCustomers = 0;
    private int _totalProducts = 0;
    private decimal _totalSales = 0m;
    private int _pendingOrders = 0;

    public int TotalCustomers
    {
        get => _totalCustomers;
        set => SetProperty(ref _totalCustomers, value);
    }

    public int TotalProducts
    {
        get => _totalProducts;
        set => SetProperty(ref _totalProducts, value);
    }

    public decimal TotalSales
    {
        get => _totalSales;
        set => SetProperty(ref _totalSales, value);
    }

    public int PendingOrders
    {
        get => _pendingOrders;
        set => SetProperty(ref _pendingOrders, value);
    }

    public ICommand NavigateToSaleCommand { get; }
    public ICommand NavigateToCustomerCommand { get; }
    public ICommand NavigateToInventoryCommand { get; }

    public DashboardViewModel(INavigationService? navigationService = null)
    {
        _navigationService = navigationService;
        Title = "Tableau de Bord";

        NavigateToSaleCommand = new RelayCommand(() => ExecuteNavigate("Sales"), CanNavigate);
        NavigateToCustomerCommand = new RelayCommand(() => ExecuteNavigate("Customers"), CanNavigate);
        NavigateToInventoryCommand = new RelayCommand(() => ExecuteNavigate("Inventory"), CanNavigate);

        LoadData();
    }

    private void ExecuteNavigate(string viewName)
    {
        _navigationService?.Navigate(viewName);
    }

    private bool CanNavigate()
    {
        return _navigationService != null;
    }

    private void LoadData()
    {
        // TODO: Charger les données depuis les services
        IsLoading = true;
        
        try
        {
            // Simulation de données
            TotalCustomers = 42;
            TotalProducts = 156;
            TotalSales = 12540.50m;
            PendingOrders = 8;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
