using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Cas d'utilisation « Enregistrer une vente en magasin » (premier vertical slice, P2B-2C). Orchestre,
/// <b>dans une frontière transactionnelle unique</b>, la numérotation, la création de la vente et de ses
/// articles, l'éventuelle commande fournisseur (verres), le décrément de stock (vente comptoir) et les
/// mouvements de stock. Remplace l'orchestration métier auparavant portée par
/// <c>SaleFormViewModel.PersistSaleAsync</c>.
/// </summary>
public interface IRegisterSaleUseCase
{
    /// <summary>
    /// Exécute l'enregistrement de la vente décrite par <paramref name="command"/> et renvoie le résultat
    /// (identifiants, numéros attribués, statut). Les erreurs métier/techniques sont propagées via les
    /// exceptions typées P2A (<see cref="MMV.Domain.Exceptions.InsufficientStockException"/>,
    /// <see cref="MMV.Domain.Exceptions.NumberSequenceException"/>,
    /// <see cref="MMV.Domain.Exceptions.PersistenceException"/>) ; toute exception annule l'écriture composée.
    /// </summary>
    Task<RegisterSaleResult> ExecuteAsync(RegisterSaleCommand command, CancellationToken cancellationToken = default);
}
