using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Infrastructure.Services;

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
            vm.CustomersListViewModel.SelectedCustomer = new CustomerListItemDto
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
                // P4-5D : était DateTime.Now. Ce DTO amorce le formulaire « nouveau client » ; sa date de
                // création est réécrite par CreateCustomerUseCase à l'enregistrement réel. Elle n'en reste pas
                // moins un instant, affiché et comparé avec ceux de la liste — donc UTC comme tous les autres
                // (ADR-PROD-DB-004 §5, invariant 1). SystemClock.Instance : ce code-behind est construit par
                // Avalonia, sans conteneur, et lit donc la MÊME instance d'horloge que le reste du processus.
                CreatedAt = SystemClock.Instance.UtcNow
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
        // P3-2C : un clic sur un bouton d'action de la ligne (Archiver / Réactiver / Supprimer) ne doit pas ouvrir
        // la fiche. Le Button marque déjà l'événement comme traité ; cette garde couvre le cas où l'événement
        // remonterait tout de même jusqu'à la ligne.
        if (e.Source is Control source && source.FindAncestorOfType<Button>() != null)
        {
            return;
        }

        if (sender is Border border && border.DataContext is CustomerListItemDto customer)
        {
            if (DataContext is CustomersViewModel vm)
            {
                // Afficher la fiche détaillée du client sélectionné
                vm.ViewDetailCommand.Execute(customer);
            }
        }
    }

    /// <summary>
    /// Archive le client de la ligne (état absolu : le client affiché est actif).
    /// </summary>
    private void ArchiveButton_Click(object? sender, RoutedEventArgs e)
        => ExecuteRowCommand(sender, vm => vm.CustomersListViewModel.ArchiveCommand);

    /// <summary>
    /// Réactive le client de la ligne (état absolu : le client affiché est archivé).
    /// </summary>
    private void ReactivateButton_Click(object? sender, RoutedEventArgs e)
        => ExecuteRowCommand(sender, vm => vm.CustomersListViewModel.ReactivateCommand);

    /// <summary>
    /// Demande la suppression physique du client de la ligne. La confirmation explicite et le refus métier
    /// (client porteur d'historique) sont portés par le ViewModel.
    /// </summary>
    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
        => ExecuteRowCommand(sender, vm => vm.CustomersListViewModel.DeleteCommand);

    /// <summary>
    /// Exécute une commande de liste sur le client porté par la ligne cliquée, en le passant en paramètre :
    /// l'intention provient de l'état réellement affiché, pas d'une sélection supposée fraîche.
    /// </summary>
    private void ExecuteRowCommand(object? sender, Func<CustomersViewModel, ICommand> commandSelector)
    {
        if (sender is Control control &&
            control.DataContext is CustomerListItemDto customer &&
            DataContext is CustomersViewModel vm)
        {
            vm.CustomersListViewModel.SelectedCustomer = customer;
            commandSelector(vm).Execute(customer);
        }
    }
}
