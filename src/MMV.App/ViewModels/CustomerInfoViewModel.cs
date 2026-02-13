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
    private readonly ISaleRepository? _saleRepository;
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
    /// Liste des ventes du client.
    /// </summary>
    public ObservableCollection<Sale> Sales { get; } = new();

    /// <summary>
    /// Indique s'il existe des ventes.
    /// </summary>
    public bool HasSales => Sales.Count > 0;

    public CustomerInfoViewModel(Customer customer, ISaleRepository? saleRepository = null)
    {
        Customer = customer;
        _saleRepository = saleRepository;
        Title = "Informations Personnelles";
    }

    /// <summary>
    /// Charge l'historique d'achats du client.
    /// </summary>
    public async Task LoadSalesAsync()
    {
        if (_saleRepository == null || Customer == null)
            return;

        IsLoading = true;
        Sales.Clear();

        try
        {
            var sales = await _saleRepository.GetByCustomerIdAsync(Customer.CustomerId);
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
