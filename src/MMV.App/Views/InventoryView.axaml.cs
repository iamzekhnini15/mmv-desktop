using Avalonia.Controls;
using Avalonia.Interactivity;
using MMV.App.ViewModels;

namespace MMV.App.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is InventoryItem item)
        {
            if (DataContext is InventoryViewModel viewModel)
            {
                viewModel.ConfirmItemAdjustmentCommand.Execute(item);
            }
        }
    }
}
