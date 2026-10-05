using System;
using System.Windows.Input;
using MMV.App.Commands;

namespace MMV.App.ViewModels;

/// <summary>
/// Écran de blocage d'un poste PostgreSQL (P4-6C, ADR-PROD-DB-009 DP-4 ; ADR-PROD-DB-010 D-15) : il affiche ce qui
/// ne va pas et ce qu'il faut faire, puis permet seulement de quitter. Aucune nouvelle tentative (D-15), aucune
/// écriture : le poste n'a ouvert aucune session métier.
/// </summary>
public sealed class DatabaseBlockedViewModel : BaseViewModel
{
    public DatabaseBlockedViewModel(string title, string message, string action, Action quit)
    {
        ArgumentNullException.ThrowIfNull(quit);

        Title = title;
        Message = message;
        Action = action;
        QuitCommand = new RelayCommand(quit);
    }

    public string Message { get; }

    public string Action { get; }

    public ICommand QuitCommand { get; }
}
