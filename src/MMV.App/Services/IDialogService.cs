using System.Threading.Tasks;

namespace MMV.App.Services;

/// <summary>
/// Service pour afficher des dialogues à l'utilisateur.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Affiche une boîte de dialogue de confirmation.
    /// </summary>
    /// <param name="title">Titre du dialogue</param>
    /// <param name="message">Message à afficher</param>
    /// <returns>True si l'utilisateur confirme, False sinon</returns>
    Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// Affiche une boîte de dialogue d'erreur.
    /// </summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Affiche une boîte de dialogue d'information.
    /// </summary>
    Task ShowInformationAsync(string title, string message);
}
