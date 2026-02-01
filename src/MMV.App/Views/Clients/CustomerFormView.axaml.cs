using Avalonia.Controls;
using Avalonia.Interactivity;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Clients;

public partial class CustomerFormView : UserControl
{
    public CustomerFormView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        System.Diagnostics.Debug.WriteLine($"[CustomerFormView] OnDataContextChanged - type: {DataContext?.GetType().Name ?? "null"}");
        Console.WriteLine($"[CustomerFormView] OnDataContextChanged - type: {DataContext?.GetType().Name ?? "null"}");

        if (DataContext is CustomerFormViewModel vm)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerFormView] SaveCommand present: {vm.SaveCommand != null}, CanExecute: {vm.SaveCommand?.CanExecute(null)}");
            System.Diagnostics.Debug.WriteLine($"[CustomerFormView] CancelCommand present: {vm.CancelCommand != null}, CanExecute: {vm.CancelCommand?.CanExecute(null)}");
            Console.WriteLine($"[CustomerFormView] SaveCommand present: {vm.SaveCommand != null}, CanExecute: {vm.SaveCommand?.CanExecute(null)}");
            Console.WriteLine($"[CustomerFormView] CancelCommand present: {vm.CancelCommand != null}, CanExecute: {vm.CancelCommand?.CanExecute(null)}");

            // Subscribe to CanExecuteChanged to log state changes
            if (vm.SaveCommand != null)
            {
                vm.SaveCommand.CanExecuteChanged += (s, _) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[CustomerFormView] SaveCommand.CanExecuteChanged -> {vm.SaveCommand.CanExecute(null)}");
                    Console.WriteLine($"[CustomerFormView] SaveCommand.CanExecuteChanged -> {vm.SaveCommand.CanExecute(null)}");
                };
            }

            if (vm.CancelCommand != null)
            {
                vm.CancelCommand.CanExecuteChanged += (s, _) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[CustomerFormView] CancelCommand.CanExecuteChanged -> {vm.CancelCommand.CanExecute(null)}");
                    Console.WriteLine($"[CustomerFormView] CancelCommand.CanExecuteChanged -> {vm.CancelCommand.CanExecute(null)}");
                };
            }
        }
        else if (DataContext is CustomersViewModel parent)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomerFormView] Bound to parent CustomersViewModel (legacy mode)");
            Console.WriteLine($"[CustomerFormView] Bound to parent CustomersViewModel (legacy mode)");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[CustomerFormView] DataContext is neither CustomerFormViewModel nor CustomersViewModel");
            Console.WriteLine("[CustomerFormView] DataContext is neither CustomerFormViewModel nor CustomersViewModel");
        }
    }

    private void ButtonRetour_Click(object? sender, RoutedEventArgs e)
    {
        // Support both scenarios: parent view's DataContext (CustomersViewModel)
        // or the form's own DataContext (CustomerFormViewModel).
        if (DataContext is CustomersViewModel parentVm)
        {
            parentVm.IsInEditMode = false;
            parentVm.IsCreatingNew = false;
            parentVm.CustomersListViewModel.SelectedCustomer = null;
            return;
        }

        if (DataContext is CustomerFormViewModel formVm)
        {
            if (formVm.CancelCommand.CanExecute(null))
                formVm.CancelCommand.Execute(null);
        }
    }

    private async void ButtonEnregistrer_Click(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CustomerFormView] ButtonEnregistrer_Click called");
        System.Diagnostics.Debug.WriteLine($"[CustomerFormView] DataContext type: {DataContext?.GetType().Name ?? "null"}");

        // If the view's DataContext is the parent CustomersViewModel, keep old behavior
        if (DataContext is CustomersViewModel parentVm)
        {
            var customer = parentVm.CustomersListViewModel.SelectedCustomer;

            if (customer == null)
            {
                customer = new Customer();
            }

            try
            {
                if (customer.CustomerId == 0)
                {
                    await parentVm.CustomersListViewModel.Repository.CreateAsync(customer);
                }
                else
                {
                    await parentVm.CustomersListViewModel.Repository.UpdateAsync(customer);
                }

                await parentVm.CustomersListViewModel.UnitOfWork.SaveChangesAsync();
                await parentVm.CustomersListViewModel.LoadCustomersAsync();

                parentVm.IsInEditMode = false;
                parentVm.IsCreatingNew = false;
                parentVm.CustomersListViewModel.SelectedCustomer = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur: {ex.Message}");
            }

            return;
        }

        // If the view's DataContext is the form's ViewModel, execute its SaveCommand
        if (DataContext is CustomerFormViewModel formVm)
        {
            System.Diagnostics.Debug.WriteLine("[CustomerFormView] Found CustomerFormViewModel");
            System.Diagnostics.Debug.WriteLine($"[CustomerFormView] SaveCommand.CanExecute: {formVm.SaveCommand.CanExecute(null)}");
            System.Diagnostics.Debug.WriteLine($"[CustomerFormView] FirstName: '{formVm.FirstName}', LastName: '{formVm.LastName}'");
            
            if (formVm.SaveCommand.CanExecute(null))
            {
                System.Diagnostics.Debug.WriteLine("[CustomerFormView] Executing SaveCommand");
                formVm.SaveCommand.Execute(null);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[CustomerFormView] SaveCommand.CanExecute returned false - cannot save");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[CustomerFormView] DataContext is NOT CustomerFormViewModel");
        }
    }
}
