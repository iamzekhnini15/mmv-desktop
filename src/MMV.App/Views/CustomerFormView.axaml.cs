using Avalonia.Controls;
using MMV.App.ViewModels;

namespace MMV.App.Views;

public partial class CustomerFormView : Window
{
    public CustomerFormView()
    {
        InitializeComponent();
    }
    
    public CustomerFormView(CustomerFormViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Fermer la fenêtre après un enregistrement réussi
        viewModel.CustomerSaved += (s, e) =>
        {
            Close(e); // Retourner le client sauvegardé
        };
        
        // Fermer la fenêtre lors de l'annulation
        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}
