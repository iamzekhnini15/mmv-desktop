using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MMV.App.ViewModels;

namespace MMV.App;

/// <summary>
/// ViewLocator résout les Views à partir de leurs ViewModels.
/// Utilise une convention de nommage: ViewModel -> View.
/// </summary>
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// Construit la vue correspondante au ViewModel donné.
    /// </summary>
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "Aucune vue disponible" };

        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);

        if (type == null)
        {
            return new TextBlock { Text = $"Vue non trouvée: {name}" };
        }

        var control = (Control)Activator.CreateInstance(type)!;
        control.DataContext = data;
        return control;
    }

    /// <summary>
    /// Vérifie si ce locator peut traiter le type de données donné.
    /// </summary>
    public bool Match(object? data)
    {
        return data is BaseViewModel;
    }
}
