using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views;

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
            // Créer directement le formulaire
            vm.CustomerFormViewModel = new CustomerFormViewModel(
                vm.CustomersListViewModel.GetType().GetField("_customerRepository", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(vm.CustomersListViewModel) as MMV.Domain.Interfaces.Repositories.ICustomerRepository,
                vm.CustomersListViewModel.GetType().GetField("_unitOfWork", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(vm.CustomersListViewModel) as MMV.Domain.Interfaces.Repositories.IUnitOfWork
            );
            vm.IsInEditMode = true;
        }
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Customer customer)
        {
            if (DataContext is CustomersViewModel vm)
            {
                vm.CustomersListViewModel.SelectedCustomer = customer;
            }
        }
    }
}
