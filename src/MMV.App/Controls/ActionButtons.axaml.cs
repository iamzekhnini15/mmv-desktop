using System;
using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace MMV.App.Controls;

/// <summary>
/// Composant de boutons d'action CRUD réutilisable.
/// </summary>
public partial class ActionButtons : UserControl
{
    /// <summary>
    /// Commande pour créer un élément.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CreateCommandProperty =
        AvaloniaProperty.Register<ActionButtons, ICommand?>(nameof(CreateCommand));

    /// <summary>
    /// Commande pour modifier un élément.
    /// </summary>
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<ActionButtons, ICommand?>(nameof(EditCommand));

    /// <summary>
    /// Commande pour supprimer un élément.
    /// </summary>
    public static readonly StyledProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.Register<ActionButtons, ICommand?>(nameof(DeleteCommand));

    /// <summary>
    /// Commande pour actualiser la liste.
    /// </summary>
    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<ActionButtons, ICommand?>(nameof(RefreshCommand));

    public ICommand? CreateCommand
    {
        get => GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public ICommand? DeleteCommand
    {
        get => GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public ActionButtons()
    {
        InitializeComponent();
        
        var createButton = this.FindControl<Button>("CreateButton");
        var editButton = this.FindControl<Button>("EditButton");
        var deleteButton = this.FindControl<Button>("DeleteButton");
        var refreshButton = this.FindControl<Button>("RefreshButton");
        
        if (createButton != null)
        {
            createButton.Click += (s, e) => CreateCommand?.Execute(null);
        }
        
        if (editButton != null)
        {
            editButton.Click += (s, e) => EditCommand?.Execute(null);
        }
        
        if (deleteButton != null)
        {
            deleteButton.Click += (s, e) => DeleteCommand?.Execute(null);
        }
        
        if (refreshButton != null)
        {
            refreshButton.Click += (s, e) => RefreshCommand?.Execute(null);
        }
    }
}
