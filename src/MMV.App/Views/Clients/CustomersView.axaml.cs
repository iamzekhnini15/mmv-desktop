using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Clients;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    private void ButtonNouveau_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm)
        {
            // Créer un nouveau client vide avec les propriétés de base
            vm.CustomersListViewModel.SelectedCustomer = new Customer
            {
                FirstName = string.Empty,
                LastName = string.Empty,
                Email = string.Empty,
                Phone = string.Empty,
                Address = string.Empty,
                City = string.Empty,
                PostalCode = string.Empty,
                SocialSecurityNumber = string.Empty,
                InsuranceName = string.Empty,
                Notes = string.Empty,
                CreatedAt = DateTime.Now
            };
            vm.IsCreatingNew = true;
            vm.IsInEditMode = true;
        }
    }

    private void ButtonRetour_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm)
        {
            // Retour à la liste
            vm.IsInEditMode = false;
            vm.IsCreatingNew = false;
            vm.CustomersListViewModel.SelectedCustomer = null;
        }
    }

    private void ButtonSupprimer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm && vm.CustomersListViewModel.SelectedCustomer != null)
        {
            // Exécuter la suppression
            vm.CustomersListViewModel.DeleteCommand.Execute(null);
            // Retour à la liste
            vm.IsInEditMode = false;
            vm.IsCreatingNew = false;
        }
    }

    private async void ButtonEnregistrer_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm)
        {
            var customer = vm.CustomersListViewModel.SelectedCustomer;
            
            if (customer == null)
            {
                // Nouveau client - créer une nouvelle instance
                customer = new Customer();
            }
            
            // Sauvegarder le client
            try
            {
                if (customer.CustomerId == 0)
                {
                    // Nouveau client - ajouter via le repository
                    await vm.CustomersListViewModel.Repository.CreateAsync(customer);
                }
                else
                {
                    // Client existant - mettre à jour
                    await vm.CustomersListViewModel.Repository.UpdateAsync(customer);
                }
                
                await vm.CustomersListViewModel.UnitOfWork.SaveChangesAsync();
                
                // Recharger la liste
                await vm.CustomersListViewModel.LoadCustomersAsync();
                
                // Retour à la liste
                vm.IsInEditMode = false;
                vm.IsCreatingNew = false;
                vm.CustomersListViewModel.SelectedCustomer = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors de l'enregistrement: {ex.Message}");
            }
        }
    }

    private void ButtonNouvelleOrdonnance_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Ouvrir le formulaire de nouvelle ordonnance
        if (DataContext is CustomersViewModel vm && vm.CustomersListViewModel.SelectedCustomer != null)
        {
            // À implémenter: créer une nouvelle ordonnance pour ce client
        }
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Customer customer)
        {
            if (DataContext is CustomersViewModel vm)
            {
                // Afficher la fiche détaillée du client sélectionné
                vm.ViewDetailCommand.Execute(customer);
            }
        }
    }
}
