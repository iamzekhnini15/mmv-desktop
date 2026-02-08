using Avalonia.Controls;
using Avalonia.Interactivity;
using MMV.App.ViewModels;
using MMV.Domain.Entities;

namespace MMV.App.Views.Clients;

public partial class CustomerPrescriptionsView : UserControl
{
    public CustomerPrescriptionsView()
    {
        InitializeComponent();
    }

    private void OnPrescriptionTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Prescription prescription)
        {
            if (DataContext is CustomerPrescriptionsViewModel viewModel)
            {
                viewModel.ViewDetailsCommand.Execute(prescription);
            }
        }
    }
}
