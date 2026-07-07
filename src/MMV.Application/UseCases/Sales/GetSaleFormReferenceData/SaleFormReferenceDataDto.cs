using System.Collections.Generic;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// Sortie (DTO composite) du use case <see cref="GetSaleFormReferenceDataUseCase"/> — « Charger les données de
/// référence du formulaire de vente » (P2D-7A). Remplace les deux lectures directes que portait
/// <c>SaleFormViewModel.InitializeForCustomerAsync</c> (<c>IPrescriptionRepository.GetLatestByCustomerIdAsync</c> et
/// <c>IProductRepository.GetAllAsync</c>).
/// </summary>
public sealed class SaleFormReferenceDataDto
{
    /// <summary>
    /// Ordonnance la plus récente du client, réutilisant le DTO applicatif de P2D-5 (mêmes champs OD/OG que
    /// <c>CustomerPrescriptionsViewModel</c>), ou <c>null</c> si le client n'a aucune ordonnance.
    /// </summary>
    public PrescriptionListItemDto? ActivePrescription { get; init; }

    /// <summary>Catalogue produit complet (filtre « actif » appliqué côté présentation, comme le flux d'origine).</summary>
    public IReadOnlyList<SaleProductPickerItemDto> Products { get; init; } = System.Array.Empty<SaleProductPickerItemDto>();
}
