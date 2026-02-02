using Avalonia.Controls;
using System.Threading.Tasks;

namespace MMV.App.Services;

/// <summary>
/// Implémentation du service de dialogue pour Avalonia avec gestion de la taille dynamique.
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
            // FIX : Utilisation de SizeToContent pour adapter la fenêtre au contenu
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 400,
            MaxWidth = 600,
            MaxHeight = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1C1C1E")), // Fond Apple Dark
            FontFamily = "Segoe UI"
        };

        var result = false;

        var mainPanel = new DockPanel
        {
            LastChildFill = true
        };

        // Header
        var headerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")), // Fond légèrement plus clair
            Padding = new Avalonia.Thickness(24, 16),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1)
        };
        DockPanel.SetDock(headerBorder, Avalonia.Controls.Dock.Top);

        var headerText = new TextBlock
        {
            Text = title,
            FontSize = 17, // Taille standard pour titres de fenêtres
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            Height = 22 // Hauteur fixe pour alignement
        };
        headerBorder.Child = headerText;
        mainPanel.Children.Add(headerBorder);

        // Footer avec DockPanel pour l'alignement correct
        var footerPanel = new DockPanel
        {
            LastChildFill = true
        };

        var footerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")),
            Padding = new Avalonia.Thickness(24, 12),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = footerPanel
        };
        DockPanel.SetDock(footerBorder, Avalonia.Controls.Dock.Bottom);

        // Panneau vide pour pousser les boutons à droite
        var spacer = new Border { Width = 0 };
        DockPanel.SetDock(spacer, Dock.Left);
        footerPanel.Children.Add(spacer);

        var buttonContainer = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        DockPanel.SetDock(buttonContainer, Dock.Right);
        footerPanel.Children.Add(buttonContainer);

        var noButton = new Button
        {
            Content = "Annuler",
            Padding = new Avalonia.Thickness(20, 8),
            Height = 36,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        noButton.Click += (s, e) =>
        {
            result = false;
            dialog.Close();
        };

        var yesButton = new Button
        {
            Content = "Confirmer",
            Padding = new Avalonia.Thickness(20, 8),
            Height = 36,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A84FF")), // Apple Blue
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        yesButton.Click += (s, e) =>
        {
            result = true;
            dialog.Close();
        };

        buttonContainer.Children.Add(noButton);
        buttonContainer.Children.Add(yesButton);

        mainPanel.Children.Add(footerBorder);

        // Content
        var contentBorder = new Border
        {
            // 1. Padding fixe et uniforme autour du texte (Haut/Bas/Gauche/Droite)
            Padding = new Avalonia.Thickness(24),

            // 2. MinHeight pour éviter que la fenêtre ne soit trop "écrasée" avec un petit message
            MinHeight = 80,

            Child = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E0E0E0")),

                // 3. Correction du LineHeight : 13px (Police) + ~5px (Respiration) = 18px
                // Cela gère l'espacement vertical entre les lignes de manière automatique et fluide.
                LineHeight = 18,

                TextAlignment = Avalonia.Media.TextAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        mainPanel.Children.Add(contentBorder);

        dialog.Content = mainPanel;

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
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 400,
            MaxWidth = 600,
            MaxHeight = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1C1C1E")),
            FontFamily = "Segoe UI"
        };

        var mainPanel = new DockPanel { LastChildFill = true };

        // Header
        var headerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")),
            Padding = new Avalonia.Thickness(24, 16),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1)
        };
        DockPanel.SetDock(headerBorder, Avalonia.Controls.Dock.Top);

        var headerText = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF453A")) // Apple Red
        };
        headerBorder.Child = headerText;
        mainPanel.Children.Add(headerBorder);

        // Footer
        var footerPanel = new DockPanel { LastChildFill = true };
        var footerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")),
            Padding = new Avalonia.Thickness(24, 12),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = footerPanel
        };
        DockPanel.SetDock(footerBorder, Avalonia.Controls.Dock.Bottom);

        // Spacer
        var spacer = new Border { Width = 0 };
        DockPanel.SetDock(spacer, Dock.Left);
        footerPanel.Children.Add(spacer);

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Avalonia.Thickness(20, 8),
            Height = 36,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")), // Style Secondaire pour OK d'erreur
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        // Optionnel : Bordure rouge pour accentuer l'erreur
        // okButton.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF453A"));
        // okButton.BorderThickness = new Avalonia.Thickness(1);

        DockPanel.SetDock(okButton, Dock.Right);
        okButton.Click += (s, e) => dialog.Close();
        footerPanel.Children.Add(okButton);
        mainPanel.Children.Add(footerBorder);

        // Content
        var contentBorder = new Border
        {
            // 1. Padding fixe et uniforme autour du texte (Haut/Bas/Gauche/Droite)
            Padding = new Avalonia.Thickness(24),

            // 2. MinHeight pour éviter que la fenêtre ne soit trop "écrasée" avec un petit message
            MinHeight = 80,

            Child = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E0E0E0")),

                // 3. Correction du LineHeight : 13px (Police) + ~5px (Respiration) = 18px
                // Cela gère l'espacement vertical entre les lignes de manière automatique et fluide.
                LineHeight = 18,

                TextAlignment = Avalonia.Media.TextAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        mainPanel.Children.Add(contentBorder);

        dialog.Content = mainPanel;

        await dialog.ShowDialog(_mainWindow);
    }

    public async Task ShowInformationAsync(string title, string message)
    {
        if (_mainWindow == null)
            return;

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 400,
            MaxWidth = 600,
            MaxHeight = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1C1C1E")),
            FontFamily = "Segoe UI"
        };

        var mainPanel = new DockPanel { LastChildFill = true };

        // Header
        var headerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")),
            Padding = new Avalonia.Thickness(24, 16),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1)
        };
        DockPanel.SetDock(headerBorder, Avalonia.Controls.Dock.Top);

        var headerText = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White)
        };
        headerBorder.Child = headerText;
        mainPanel.Children.Add(headerBorder);

        // Footer
        var footerPanel = new DockPanel { LastChildFill = true };
        var footerBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2C2C2E")),
            Padding = new Avalonia.Thickness(24, 12),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C")),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = footerPanel
        };
        DockPanel.SetDock(footerBorder, Avalonia.Controls.Dock.Bottom);

        var spacer = new Border { Width = 0 };
        DockPanel.SetDock(spacer, Dock.Left);
        footerPanel.Children.Add(spacer);

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Avalonia.Thickness(20, 8),
            Height = 36,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A84FF")), // Apple Blue
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        DockPanel.SetDock(okButton, Dock.Right);
        okButton.Click += (s, e) => dialog.Close();
        footerPanel.Children.Add(okButton);
        mainPanel.Children.Add(footerBorder);

        // Content
        var contentBorder = new Border
        {
            // 1. Padding fixe et uniforme autour du texte (Haut/Bas/Gauche/Droite)
            Padding = new Avalonia.Thickness(24),

            // 2. MinHeight pour éviter que la fenêtre ne soit trop "écrasée" avec un petit message
            MinHeight = 80,

            Child = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E0E0E0")),

                // 3. Correction du LineHeight : 13px (Police) + ~5px (Respiration) = 18px
                // Cela gère l'espacement vertical entre les lignes de manière automatique et fluide.
                LineHeight = 18,

                TextAlignment = Avalonia.Media.TextAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        mainPanel.Children.Add(contentBorder);

        dialog.Content = mainPanel;

        await dialog.ShowDialog(_mainWindow);
    }
}