using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Products;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Supplier supplier)
        {
            if (DataContext is SuppliersViewModel vm)
            {
                vm.SuppliersListViewModel.SelectedSupplier = supplier;
                vm.SuppliersListViewModel.ViewDetailsCommand.Execute(null);
            }
        }
    }
}
