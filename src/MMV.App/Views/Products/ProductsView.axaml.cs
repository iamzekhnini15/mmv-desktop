using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Products;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Product product)
        {
            if (DataContext is ProductsViewModel vm)
            {
                vm.ProductsListViewModel.SelectedProduct = product;
                if (vm.ViewDetailCommand.CanExecute(product))
                {
                    vm.ViewDetailCommand.Execute(product);
                }
            }
        }
    }
}
