using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.GetInventoryOverview;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.Stock.ListStockMovements;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Domain.Enums;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2D-4 — Vérifie que les ViewModels de lecture du module Produits / Stock migrés délèguent aux <i>query use cases</i>
/// Application (au lieu d'un repository), remplissent leur état d'écran depuis les DTO, conservent leurs filtres, et
/// rejettent les dépendances nulles. Ne teste pas le rendu Avalonia.
/// </summary>
public sealed class P2D4ProductStockViewModelTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount;
        while (!condition() && Environment.TickCount - start < timeoutMs)
            await Task.Delay(10);
    }

    // ---------------- StockMovementsListViewModel ----------------

    [Fact]
    public async Task StockMovementsList_LoadsMovementsAndProductPicker_FromQueryUseCases()
    {
        var listMovements = new Mock<IListStockMovementsUseCase>();
        listMovements.Setup(u => u.ExecuteAsync(It.IsAny<ListStockMovementsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMovementListItemDto>
            {
                new() { ProductId = 1, Product = new StockMovementProductRefDto { Name = "Monture A", Reference = "R1" }, MovementType = StockMovementType.In, Quantity = 4, Reason = "Réception", CreatedAt = new DateTime(2026, 6, 1) },
                new() { ProductId = 2, Product = new StockMovementProductRefDto { Name = "Verre B", Reference = "R2" }, MovementType = StockMovementType.Out, Quantity = 1, Reason = "Vente", CreatedAt = new DateTime(2026, 6, 2) },
            });
        var listPicker = new Mock<IListProductsForPickerUseCase>();
        listPicker.Setup(u => u.ExecuteAsync(It.IsAny<ListProductsForPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPickerItemDto>
            {
                new() { ProductId = 1, Name = "Monture A", Reference = "R1" },
            });

        var vm = new StockMovementsListViewModel(listMovements.Object, listPicker.Object);

        await WaitUntilAsync(() => vm.Movements.Count == 2 && vm.Products.Count == 2);
        Assert.Equal(2, vm.Movements.Count);
        // Le sélecteur produit contient l'entrée fictive « Tous les produits » (Id 0) + les produits du use case.
        Assert.Equal(2, vm.Products.Count);
        Assert.Contains(vm.Products, p => p.ProductId == 0);

        // Filtre recherche conservé (iso-fonctionnel) : recherche sur le nom du produit.
        vm.SearchText = "Verre";
        Assert.Single(vm.FilteredMovements);
        Assert.Equal("Verre B", vm.FilteredMovements[0].Product.Name);
    }

    [Fact]
    public void StockMovementsList_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new StockMovementsListViewModel(null!, Mock.Of<IListProductsForPickerUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new StockMovementsListViewModel(Mock.Of<IListStockMovementsUseCase>(), null!));
    }

    // ---------------- StockMovementsViewModel ----------------

    [Fact]
    public void StockMovements_Constructor_RejectsNullQueryUseCases()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new StockMovementsViewModel(
            null!, Mock.Of<IListProductsForPickerUseCase>(), Mock.Of<IDialogService>(), Mock.Of<ICreateStockMovementUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new StockMovementsViewModel(
            Mock.Of<IListStockMovementsUseCase>(), null!, Mock.Of<IDialogService>(), Mock.Of<ICreateStockMovementUseCase>()));
    }

    // ---------------- InventoryViewModel ----------------

    [Fact]
    public async Task Inventory_LoadsItems_FromQueryUseCase()
    {
        var overview = new Mock<IGetInventoryOverviewUseCase>();
        overview.Setup(u => u.ExecuteAsync(It.IsAny<GetInventoryOverviewQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryProductItemDto>
            {
                new() { ProductId = 1, Reference = "R1", Name = "Monture A", Category = ProductCategoryEnum.MONTURE, StockQuantity = 7 },
                new() { ProductId = 2, Reference = "R2", Name = "Verre B", Category = ProductCategoryEnum.VERRE, StockQuantity = 3 },
            });

        var vm = new InventoryViewModel(overview.Object, Mock.Of<ICreateStockMovementUseCase>(), Mock.Of<IDialogService>());

        await WaitUntilAsync(() => vm.Items.Count == 2);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, vm.FilteredItems.Count);
        // Stock théorique alimenté depuis le DTO.
        Assert.Contains(vm.Items, i => i.Product.Name == "Monture A" && i.TheoreticalStock == 7);

        // Filtre recherche conservé (iso-fonctionnel).
        vm.SearchText = "Verre";
        Assert.Single(vm.FilteredItems);
        Assert.Equal("Verre B", vm.FilteredItems[0].Product.Name);
    }

    [Fact]
    public void Inventory_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new InventoryViewModel(
            null!, Mock.Of<ICreateStockMovementUseCase>(), Mock.Of<IDialogService>()));
        Assert.Throws<ArgumentNullException>(() => _ = new InventoryViewModel(
            Mock.Of<IGetInventoryOverviewUseCase>(), null!, Mock.Of<IDialogService>()));
    }

    // ---------------- ProductFormViewModel ----------------

    [Fact]
    public async Task ProductForm_LoadsSuppliers_FromListSuppliersUseCase()
    {
        var listSuppliers = new Mock<IListSuppliersUseCase>();
        listSuppliers.Setup(u => u.ExecuteAsync(It.IsAny<ListSuppliersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierListItemDto>
            {
                new() { SupplierId = 1, Name = "Essilor" },
                new() { SupplierId = 2, Name = "Zeiss" },
            });

        var vm = new ProductFormViewModel(listSuppliers.Object, Mock.Of<ICreateProductUseCase>(), Mock.Of<IUpdateProductUseCase>());
        await vm.InitializeAsync();

        Assert.Equal(2, vm.Suppliers.Count);
        Assert.Contains(vm.Suppliers, s => s.Name == "Essilor");
    }

    [Fact]
    public void ProductForm_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new ProductFormViewModel(
            null!, Mock.Of<ICreateProductUseCase>(), Mock.Of<IUpdateProductUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new ProductFormViewModel(
            Mock.Of<IListSuppliersUseCase>(), null!, Mock.Of<IUpdateProductUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new ProductFormViewModel(
            Mock.Of<IListSuppliersUseCase>(), Mock.Of<ICreateProductUseCase>(), null!));
    }
}
