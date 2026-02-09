using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Orders;

public partial class OrderKanbanView : UserControl
{
    public OrderKanbanView()
    {
        InitializeComponent();
    }

    private void KanbanCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Order order
            && DataContext is OrderKanbanViewModel vm)
        {
            vm.ViewDetailCommand.Execute(order);
        }
    }
}
