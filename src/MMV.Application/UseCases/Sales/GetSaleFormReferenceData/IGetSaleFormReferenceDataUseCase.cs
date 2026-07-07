using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// Query use case « Charger les données de référence du formulaire de vente » (P2D-7A). Remplace
/// <c>IPrescriptionRepository.GetLatestByCustomerIdAsync</c> et <c>IProductRepository.GetAllAsync</c> côté UI
/// (<c>SaleFormViewModel</c>).
/// </summary>
public interface IGetSaleFormReferenceDataUseCase
{
    /// <summary>
    /// Renvoie l'ordonnance active du client et le catalogue produit, projetés en DTO applicatifs plats (jamais
    /// les entités EF <c>Prescription</c> / <c>Product</c>).
    /// </summary>
    Task<SaleFormReferenceDataDto> ExecuteAsync(GetSaleFormReferenceDataQuery query, CancellationToken cancellationToken = default);
}
