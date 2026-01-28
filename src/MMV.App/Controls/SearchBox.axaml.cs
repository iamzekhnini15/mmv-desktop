using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MMV.App.Controls;

/// <summary>
/// Composant de recherche réutilisable avec bouton clear.
/// </summary>
public partial class SearchBox : UserControl
{
    public SearchBox()
    {
        InitializeComponent();
        
        var searchTextBox = this.FindControl<TextBox>("SearchTextBox");
        var clearButton = this.FindControl<Button>("ClearButton");
        
        if (searchTextBox != null && clearButton != null)
        {
            // Afficher le bouton clear quand il y a du texte
            searchTextBox.TextChanged += (s, e) =>
            {
                clearButton.IsVisible = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            };
            
            // Vider le texte quand on clique sur clear
            clearButton.Click += (s, e) =>
            {
                searchTextBox.Text = string.Empty;
                searchTextBox.Focus();
            };
        }
    }
    
    /// <summary>
    /// Obtient ou définit le texte de recherche.
    /// </summary>
    public string SearchText
    {
        get
        {
            var textBox = this.FindControl<TextBox>("SearchTextBox");
            return textBox?.Text ?? string.Empty;
        }
        set
        {
            var textBox = this.FindControl<TextBox>("SearchTextBox");
            if (textBox != null)
            {
                textBox.Text = value;
            }
        }
    }
    
    /// <summary>
    /// Définit le focus sur le champ de recherche.
    /// </summary>
    public void FocusSearchBox()
    {
        var textBox = this.FindControl<TextBox>("SearchTextBox");
        textBox?.Focus();
    }
}
