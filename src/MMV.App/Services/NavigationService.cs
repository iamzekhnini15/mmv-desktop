using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using MMV.App.ViewModels;

namespace MMV.App.Services;

/// <summary>
/// Service de navigation pour gérer la navigation entre les vues.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigue vers une vue donnée.
    /// </summary>
    void Navigate(string viewName, object? parameter = null);

    /// <summary>
    /// Navigue vers la page précédente (si possible).
    /// </summary>
    void GoBack();

    /// <summary>
    /// Enregistre un ViewModel pour une vue donnée.
    /// </summary>
    void RegisterViewModel(string viewName, Type viewModelType);

    /// <summary>
    /// Obtient le ViewModel actuellement actif.
    /// </summary>
    BaseViewModel? CurrentViewModel { get; }

    /// <summary>
    /// Événement déclenché quand la navigation change.
    /// </summary>
    event EventHandler<NavigationEventArgs>? NavigationChanged;
}

/// <summary>
/// Événement passé lors d'un changement de navigation.
/// </summary>
public class NavigationEventArgs : EventArgs
{
    public string ViewName { get; set; } = string.Empty;
    public object? Parameter { get; set; }
}

/// <summary>
/// Implémentation du service de navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Dictionary<string, Type> _viewModelRegistry = new();
    private readonly IServiceProvider _serviceProvider;
    private BaseViewModel? _currentViewModel;
    private readonly Stack<string> _navigationHistory = new();

    public BaseViewModel? CurrentViewModel => _currentViewModel;

    public event EventHandler<NavigationEventArgs>? NavigationChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Enregistre un ViewModel pour une vue donnée.
    /// </summary>
    public void RegisterViewModel(string viewName, Type viewModelType)
    {
        if (!_viewModelRegistry.ContainsKey(viewName))
        {
            _viewModelRegistry[viewName] = viewModelType;
        }
    }

    /// <summary>
    /// Navigue vers une vue donnée.
    /// </summary>
    public void Navigate(string viewName, object? parameter = null)
    {
        if (_viewModelRegistry.TryGetValue(viewName, out var viewModelType))
        {
            _navigationHistory.Push(viewName);
            
            try
            {
                // Utiliser ActivatorUtilities pour supporter l'injection de dépendances
                _currentViewModel = ActivatorUtilities.CreateInstance(_serviceProvider, viewModelType) as BaseViewModel;
                NavigationChanged?.Invoke(this, new NavigationEventArgs 
                { 
                    ViewName = viewName, 
                    Parameter = parameter 
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible de créer le ViewModel pour {viewName}", ex);
            }
        }
        else
        {
            throw new KeyNotFoundException($"Aucun ViewModel enregistré pour la vue '{viewName}'");
        }
    }

    /// <summary>
    /// Navigue vers la page précédente.
    /// </summary>
    public void GoBack()
    {
        if (_navigationHistory.Count > 1)
        {
            _navigationHistory.Pop(); // Enlever la vue actuelle
            var previousView = _navigationHistory.Peek();
            Navigate(previousView);
        }
    }
}
