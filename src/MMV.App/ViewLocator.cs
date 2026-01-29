using System;
using System.Linq;
using System.Reflection;
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

        // Conventions:
        // - ViewModel: MMV.App.ViewModels.SomeThingViewModel
        // - View:      MMV.App.Views[.Clients].SomeThingView
        // We attempt a few resolutions: direct name, replace namespace part, then search loaded assemblies.

        var vmType = data.GetType();
        var shortViewName = vmType.Name.Replace("ViewModel", "View");

        // Try common namespace replacement first (ViewModels -> Views)
        var candidateFullName = vmType.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
        Type? viewType = Type.GetType(candidateFullName);

        // If not found, search loaded assemblies for a matching type by short name or full name ending with the short name
        if (viewType == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }

                var match = types.FirstOrDefault(t => string.Equals(t.FullName, candidateFullName, StringComparison.Ordinal)
                                                    || string.Equals(t.Name, shortViewName, StringComparison.Ordinal)
                                                    || (t.FullName != null && t.FullName.EndsWith("." + shortViewName, StringComparison.Ordinal)));
                if (match != null)
                {
                    viewType = match;
                    break;
                }
            }
        }

        if (viewType == null)
        {
            return new TextBlock { Text = $"Vue non trouvée: {shortViewName} (candidates: {candidateFullName})" };
        }

        var control = (Control)Activator.CreateInstance(viewType)!;
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
