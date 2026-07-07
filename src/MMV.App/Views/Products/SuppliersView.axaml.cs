using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Suppliers.ListSuppliers;

namespace MMV.App.Views.Products;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // P2D-2 : les lignes de la liste sont désormais des DTO applicatifs (plus l'entité EF Supplier).
        if (sender is Border border && border.DataContext is SupplierListItemDto supplier)
        {
            if (DataContext is SuppliersViewModel vm)
            {
                vm.SuppliersListViewModel.SelectedSupplier = supplier;
                vm.SuppliersListViewModel.ViewDetailsCommand.Execute(null);
            }
        }
    }
}
