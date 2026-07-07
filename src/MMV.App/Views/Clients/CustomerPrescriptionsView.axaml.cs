using Avalonia.Controls;
using Avalonia.Interactivity;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

namespace MMV.App.Views.Clients;

public partial class CustomerPrescriptionsView : UserControl
{
    public CustomerPrescriptionsView()
    {
        InitializeComponent();
    }

    private void OnPrescriptionTapped(object? sender, RoutedEventArgs e)
    {
        // P2D-5 : la liste porte désormais des DTO applicatifs (PrescriptionListItemDto) au lieu de l'entité EF.
        if (sender is Border border && border.DataContext is PrescriptionListItemDto prescription)
        {
            if (DataContext is CustomerPrescriptionsViewModel viewModel)
            {
                viewModel.ViewDetailsCommand.Execute(prescription);
            }
        }
    }
}
