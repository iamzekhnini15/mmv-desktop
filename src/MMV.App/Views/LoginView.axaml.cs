using Avalonia;
using Avalonia.Controls;

namespace MMV.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Place le focus initial sur le champ Identifiant (MMV-Login-states.md §1).
    /// </summary>
    /// <remarks>
    /// Seule responsabilité du code-behind : la saisie est liée au ViewModel par binding
    /// et la touche Entrée est portée par <c>IsDefault</c> sur le bouton de connexion,
    /// qui respecte déjà le <c>CanExecute</c> de LoginCommand.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UsernameBox.Focus();
    }
}
