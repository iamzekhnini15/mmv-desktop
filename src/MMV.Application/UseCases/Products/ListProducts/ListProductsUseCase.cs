using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Implémentation du query use case « Lister les produits » (P2D-7D). Déplace, sans changement de comportement
/// observable, la lecture portée jusque-là par <c>ProductsListViewModel.LoadProductsAsync</c>
/// (<c>IProductRepository.GetAllAsync</c>, qui inclut déjà fournisseur, détails de catégorie et historique de
/// commandes — cf. <c>ProductRepository.GetAllAsync</c>), en projetant chaque entité vers un
/// <see cref="ProductListItemDto"/> plat.
/// </summary>
public sealed class ListProductsUseCase : IListProductsUseCase
{
    private readonly IProductRepository _productRepository;

    public ListProductsUseCase(IProductRepository productRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductListItemDto>> ExecuteAsync(ListProductsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var products = await _productRepository.GetAllAsync(cancellationToken);

        return products.Select(MapToDto).ToList();
    }

    private static ProductListItemDto MapToDto(Product p)
    {
        return new ProductListItemDto
        {
            ProductId = p.ProductId,
            Reference = p.Reference,
            Name = p.Name,
            Description = p.Description,
            Category = p.Category,
            SupplierId = p.SupplierId,
            PurchasePrice = p.PurchasePrice,
            SalePrice = p.SalePrice,
            RecommendedPrice = p.RecommendedPrice,
            StockQuantity = p.StockQuantity,
            StockAlertThreshold = p.StockAlertThreshold,
            IsActive = p.IsActive,
            EntryDate = p.EntryDate,
            Supplier = p.Supplier is null
                ? null
                : new ProductSupplierRefDto { SupplierId = p.Supplier.SupplierId, Name = p.Supplier.Name },
            GlassDetail = p.GlassDetail is null
                ? null
                : new ProductGlassDetailsDto
                {
                    Material = p.GlassDetail.Material,
                    GlassType = p.GlassDetail.GlassType,
                    Diameter = p.GlassDetail.Diameter,
                    Index = p.GlassDetail.Index,
                    PowerLimitMin = p.GlassDetail.PowerLimitMin,
                    PowerLimitMax = p.GlassDetail.PowerLimitMax,
                },
            LensDetail = p.LensDetail is null
                ? null
                : new ProductLensDetailsDto
                {
                    Brand = p.LensDetail.Brand,
                    Model = p.LensDetail.Model,
                    Material = p.LensDetail.Material,
                    LensType = p.LensDetail.LensType,
                    Diameter = p.LensDetail.Diameter,
                    BaseCurve = p.LensDetail.BaseCurve,
                    IsColored = p.LensDetail.IsColored,
                    Duration = p.LensDetail.Duration,
                },
            AccessoryDetail = p.AccessoryDetail is null
                ? null
                : new ProductAccessoryDetailsDto
                {
                    Color = p.AccessoryDetail.Color,
                    Size = p.AccessoryDetail.Size,
                    Material = p.AccessoryDetail.Material,
                },
            OrderHistory = (p.OrderItems ?? Array.Empty<OrderItem>())
                .OrderByDescending(oi => oi.Order?.OrderDate ?? DateTime.MinValue)
                .Select(oi => new ProductOrderHistoryItemDto
                {
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    Order = oi.Order is null
                        ? null
                        : new ProductOrderHistoryOrderDto
                        {
                            OrderNumber = oi.Order.OrderNumber,
                            OrderDate = oi.Order.OrderDate,
                            Status = oi.Order.Status,
                            Sale = oi.Order.Sale is null
                                ? null
                                : new ProductOrderHistorySaleDto
                                {
                                    Customer = oi.Order.Sale.Customer is null
                                        ? null
                                        : new ProductOrderHistoryCustomerDto
                                        {
                                            FirstName = oi.Order.Sale.Customer.FirstName,
                                            LastName = oi.Order.Sale.Customer.LastName,
                                        },
                                },
                        },
                })
                .ToList(),
        };
    }
}
