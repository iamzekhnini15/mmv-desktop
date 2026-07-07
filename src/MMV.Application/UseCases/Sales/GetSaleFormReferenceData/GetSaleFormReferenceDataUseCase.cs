using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// Implémentation du query use case « Charger les données de référence du formulaire de vente » (P2D-7A). Déplace,
/// sans changement de comportement observable, les deux lectures portées jusque-là par
/// <c>SaleFormViewModel.InitializeForCustomerAsync</c> (<c>IPrescriptionRepository.GetLatestByCustomerIdAsync</c> et
/// <c>IProductRepository.GetAllAsync</c>), en projetant chaque entité vers un DTO plat.
/// </summary>
public sealed class GetSaleFormReferenceDataUseCase : IGetSaleFormReferenceDataUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;

    public GetSaleFormReferenceDataUseCase(
        IProductRepository productRepository,
        IPrescriptionRepository prescriptionRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
    }

    /// <inheritdoc />
    public async Task<SaleFormReferenceDataDto> ExecuteAsync(GetSaleFormReferenceDataQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var prescription = await _prescriptionRepository.GetLatestByCustomerIdAsync(query.CustomerId, cancellationToken);
        var products = await _productRepository.GetAllAsync(cancellationToken);

        return new SaleFormReferenceDataDto
        {
            ActivePrescription = prescription is null
                ? null
                : new PrescriptionListItemDto
                {
                    PrescriptionId = prescription.PrescriptionId,
                    CustomerId = prescription.CustomerId,
                    IssueDate = prescription.IssueDate,
                    DoctorName = prescription.DoctorName,
                    OdSphere = prescription.OdSphere,
                    OdCylinder = prescription.OdCylinder,
                    OdAxis = prescription.OdAxis,
                    OdAddition = prescription.OdAddition,
                    OdPrismValue = prescription.OdPrismValue,
                    OdPrismBase = prescription.OdPrismBase,
                    OdVisualAcuity = prescription.OdVisualAcuity,
                    OgSphere = prescription.OgSphere,
                    OgCylinder = prescription.OgCylinder,
                    OgAxis = prescription.OgAxis,
                    OgAddition = prescription.OgAddition,
                    OgPrismValue = prescription.OgPrismValue,
                    OgPrismBase = prescription.OgPrismBase,
                    OgVisualAcuity = prescription.OgVisualAcuity,
                    Notes = prescription.Notes,
                },
            Products = products.Select(p => new SaleProductPickerItemDto
            {
                ProductId = p.ProductId,
                Reference = p.Reference,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category,
                SalePrice = p.SalePrice,
                StockQuantity = p.StockQuantity,
                IsActive = p.IsActive,
                GlassDetail = p.GlassDetail is null
                    ? null
                    : new SaleGlassDetailDto
                    {
                        GlassType = p.GlassDetail.GlassType,
                        PowerLimitMin = p.GlassDetail.PowerLimitMin,
                        PowerLimitMax = p.GlassDetail.PowerLimitMax,
                    },
            }).ToList(),
        };
    }
}
