using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel de base implémentant INotifyPropertyChanged pour MVVM.
/// </summary>
public class BaseViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isLoading = false;
    private string? _errorMessage;

    /// <summary>
    /// Titre de la vue/page.
    /// </summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// Indique si un chargement est en cours.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Message d'erreur à afficher.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Événement déclenché quand une propriété change.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Modifie une propriété et déclenche l'événement PropertyChanged si nécessaire.
    /// </summary>
    protected void SetProperty<T>(ref T field, T newValue, [CallerMemberName] string propertyName = "")
    {
        if (!EqualityComparer<T>.Default.Equals(field, newValue))
        {
            field = newValue;
            OnPropertyChanged(propertyName);
        }
    }

    /// <summary>
    /// Déclenche l'événement PropertyChanged pour une propriété donnée.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
