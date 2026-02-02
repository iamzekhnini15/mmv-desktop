using Avalonia.Controls;
using System.Threading.Tasks;

namespace MMV.App.Services;

/// <summary>
/// Implémentation simple du service de dialogue pour Avalonia.
/// </summary>
public class DialogService : IDialogService
{
    private Window? _mainWindow;

    public void SetMainWindow(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        if (_mainWindow == null)
            return false;

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var result = false;

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20
        };

        // Message
        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Avalonia.Thickness(0, 10, 0, 20)
        };
        stackPanel.Children.Add(messageBlock);

        // Boutons
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        var yesButton = new Button
        {
            Content = "Oui",
            Width = 100,
            Height = 35,
            Background = Avalonia.Media.Brushes.Red,
            Foreground = Avalonia.Media.Brushes.White
        };
        yesButton.Click += (s, e) =>
        {
            result = true;
            dialog.Close();
        };

        var noButton = new Button
        {
            Content = "Non",
            Width = 100,
            Height = 35
        };
        noButton.Click += (s, e) =>
        {
            result = false;
            dialog.Close();
        };

        buttonPanel.Children.Add(noButton);
        buttonPanel.Children.Add(yesButton);
        stackPanel.Children.Add(buttonPanel);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(_mainWindow);

        return result;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (_mainWindow == null)
            return;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Red
        };
        stackPanel.Children.Add(messageBlock);

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        okButton.Click += (s, e) => dialog.Close();
        stackPanel.Children.Add(okButton);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(_mainWindow);
    }

    public async Task ShowInformationAsync(string title, string message)
    {
        if (_mainWindow == null)
            return;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        stackPanel.Children.Add(messageBlock);

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        okButton.Click += (s, e) => dialog.Close();
        stackPanel.Children.Add(okButton);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(_mainWindow);
    }
}
