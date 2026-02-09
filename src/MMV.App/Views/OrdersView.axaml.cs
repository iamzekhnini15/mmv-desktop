using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views;

public partial class OrdersView : UserControl
{
    public OrdersView()
    {
        InitializeComponent();
    }

    private void OrderRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Order order
            && DataContext is OrdersViewModel vm)
        {
            vm.ListViewModel.ViewDetailCommand.Execute(order);
        }
    }
}
