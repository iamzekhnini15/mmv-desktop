using Avalonia;
using Avalonia.Controls;

namespace MMV.App.Controls;

/// <summary>
/// Composant de chargement réutilisable.
/// </summary>
public partial class LoadingSpinner : UserControl
{
    /// <summary>
    /// Propriété pour le message de chargement.
    /// </summary>
    public static readonly StyledProperty<string> LoadingMessageProperty =
        AvaloniaProperty.Register<LoadingSpinner, string>(nameof(LoadingMessage), defaultValue: "Chargement...");

    public string LoadingMessage
    {
        get => GetValue(LoadingMessageProperty);
        set => SetValue(LoadingMessageProperty, value);
    }

    public LoadingSpinner()
    {
        InitializeComponent();
        
        // Initialiser le texte
        var textBlock = this.FindControl<TextBlock>("MessageTextBlock");
        if (textBlock != null)
        {
            textBlock.Text = LoadingMessage;
        }
        
        // Observer les changements de propriété
        LoadingMessageProperty.Changed.AddClassHandler<LoadingSpinner>((spinner, e) =>
        {
            var tb = spinner.FindControl<TextBlock>("MessageTextBlock");
            if (tb != null && e.NewValue is string message)
            {
                tb.Text = message;
            }
        });
    }
}
