using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.CreateCustomer;

/// <summary>
/// Cas d'utilisation « Créer un client » (P2C-2). Orchestre la construction de l'entité client à partir de la
/// commande, sa persistance et renvoie le résultat, en remplacement de l'orchestration de persistance auparavant
/// portée par la branche création de <c>CustomerFormViewModel.ExecuteSave</c> (et le code-behind client).
/// </summary>
public interface ICreateCustomerUseCase
{
    /// <summary>
    /// Exécute la création du client décrit par <paramref name="command"/> et renvoie le résultat
    /// (identifiant attribué, nom d'affichage). Les erreurs techniques sont propagées telles quelles, exactement
    /// comme le flux d'origine (qui les affichait via <c>ErrorMessage</c>).
    /// </summary>
    Task<CreateCustomerResult> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken = default);
}
