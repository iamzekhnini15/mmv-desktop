using System;
using MMV.Domain.Entities;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour l'affichage des informations personnelles d'un client.
/// </summary>
public class CustomerInfoViewModel : BaseViewModel
{
    private Customer? _customer;

    /// <summary>
    /// Client dont on affiche les informations.
    /// </summary>
    public Customer? Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    public CustomerInfoViewModel(Customer customer)
    {
        Customer = customer;
        Title = "Informations Personnelles";
    }
}
