using Avalonia.Controls;
using Avalonia.Input;
using MMV.App.ViewModels;

namespace MMV.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LoginViewModel vm)
        {
            // Récupérer les références aux TextBox
            var usernameBox = this.FindControl<TextBox>("UsernameBox");
            var passwordBox = this.FindControl<TextBox>("PasswordBox");
            var loginButton = this.FindControl<Button>("LoginButton");

            if (passwordBox != null)
            {
                // Synchroniser le mot de passe avec le ViewModel
                passwordBox.TextChanged += (s, e) =>
                {
                    vm.Password = passwordBox.Text ?? string.Empty;
                };
                
                // Exécuter le login quand l'utilisateur appuie sur Entrée dans le password
                passwordBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Return && vm.LoginCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        vm.LoginCommand.Execute(null);
                    }
                };
            }

            if (usernameBox != null)
            {
                // Exécuter le login quand l'utilisateur appuie sur Entrée dans le username
                usernameBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Return)
                    {
                        e.Handled = true;
                        passwordBox?.Focus();
                    }
                };
            }


        }
    }
}
