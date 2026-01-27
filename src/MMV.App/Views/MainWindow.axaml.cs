using Avalonia.Controls;
using MMV.App.ViewModels;

namespace MMV.App.Views;

/// <summary>
/// Code-behind de la fenêtre principale.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
