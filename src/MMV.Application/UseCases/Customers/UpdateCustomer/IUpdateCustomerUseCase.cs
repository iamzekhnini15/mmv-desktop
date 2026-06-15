using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.UpdateCustomer;

/// <summary>
/// Cas d'utilisation « Modifier un client » (P2C-2). Orchestre le chargement du client, la mise à jour de ses
/// champs et sa persistance, en remplacement de l'orchestration auparavant portée par la branche édition de
/// <c>CustomerFormViewModel.ExecuteSave</c> (et le code-behind client).
/// </summary>
public interface IUpdateCustomerUseCase
{
    /// <summary>
    /// Exécute la modification du client décrit par <paramref name="command"/> et renvoie le résultat
    /// (présence, identifiant, nom d'affichage). Si le client est introuvable, aucune écriture n'est effectuée
    /// (<see cref="UpdateCustomerResult.CustomerFound"/> = <c>false</c>). Les erreurs techniques sont propagées
    /// telles quelles, exactement comme le flux d'origine.
    /// </summary>
    Task<UpdateCustomerResult> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken = default);
}
