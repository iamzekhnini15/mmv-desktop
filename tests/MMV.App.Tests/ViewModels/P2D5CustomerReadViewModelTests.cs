using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;
using MMV.Domain.Enums;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2D-5 — Vérifie que les ViewModels de lecture Clients / Ordonnances migrés délèguent aux <i>query use cases</i>
/// Application (au lieu d'un repository), remplissent leur état d'écran depuis les DTO plats, propagent les erreurs
/// et rejettent les dépendances nulles. Ne teste pas le rendu Avalonia.
/// </summary>
public sealed class P2D5CustomerReadViewModelTests
{
    // ---------------- CustomerPurchaseHistoryViewModel ----------------

    private static Mock<IGetCustomerPurchaseHistoryUseCase> PurchaseHistoryReturning(params CustomerSaleItemDto[] sales)
    {
        var useCase = new Mock<IGetCustomerPurchaseHistoryUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<GetCustomerPurchaseHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sales);
        return useCase;
    }

    [Fact]
    public async Task PurchaseHistory_LoadsSales_FromQueryUseCase()
    {
        var useCase = PurchaseHistoryReturning(
            new CustomerSaleItemDto { SaleId = 1, SaleNumber = "VTE-1", SaleDate = new DateTime(2026, 6, 1), FinalAmount = 250m, Status = SaleStatus.Delivered },
            new CustomerSaleItemDto { SaleId = 2, SaleNumber = "VTE-2", SaleDate = new DateTime(2026, 1, 1), FinalAmount = 100m, Status = SaleStatus.Draft });

        var vm = new CustomerPurchaseHistoryViewModel(useCase.Object);
        await vm.LoadAsync(7);

        Assert.Equal(2, vm.Sales.Count);
        Assert.True(vm.HasSales);
        Assert.Equal(7, vm.CustomerId);
        Assert.Equal("VTE-1", vm.Sales[0].SaleNumber);
        useCase.Verify(u => u.ExecuteAsync(It.Is<GetCustomerPurchaseHistoryQuery>(q => q.CustomerId == 7), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurchaseHistory_WhenUseCaseThrows_SetsErrorMessage()
    {
        var useCase = new Mock<IGetCustomerPurchaseHistoryUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<GetCustomerPurchaseHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var vm = new CustomerPurchaseHistoryViewModel(useCase.Object);
        await vm.LoadAsync(7);

        Assert.False(vm.HasSales);
        Assert.StartsWith("Erreur lors du chargement : ", vm.ErrorMessage);
        Assert.True(vm.HasError);
    }

    [Fact]
    public void PurchaseHistory_Constructor_RejectsNullUseCase()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new CustomerPurchaseHistoryViewModel(null!));
    }

    // ---------------- CustomerInfoViewModel ----------------

    [Fact]
    public async Task CustomerInfo_LoadsSales_FromQueryUseCase_AndKeepsCustomer()
    {
        var customer = new CustomerListItemDto { CustomerId = 7, FirstName = "Alice", LastName = "Martin" };
        var useCase = PurchaseHistoryReturning(
            new CustomerSaleItemDto { SaleId = 1, SaleNumber = "VTE-1", SaleDate = new DateTime(2026, 6, 1), FinalAmount = 250m, Status = SaleStatus.Delivered });

        var vm = new CustomerInfoViewModel(customer, useCase.Object);
        await vm.LoadSalesAsync();

        Assert.Same(customer, vm.Customer);
        Assert.Single(vm.Sales);
        Assert.True(vm.HasSales);
        useCase.Verify(u => u.ExecuteAsync(It.Is<GetCustomerPurchaseHistoryQuery>(q => q.CustomerId == 7), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CustomerInfo_WithoutUseCase_DoesNotLoad()
    {
        var customer = new CustomerListItemDto { CustomerId = 7, FirstName = "Alice", LastName = "Martin" };
        var vm = new CustomerInfoViewModel(customer);

        await vm.LoadSalesAsync();

        Assert.False(vm.HasSales);
        Assert.Empty(vm.Sales);
    }

    // ---------------- CustomerPrescriptionsViewModel ----------------

    [Fact]
    public async Task Prescriptions_LoadsList_FromQueryUseCase()
    {
        var listUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();
        listUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListPrescriptionsByCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PrescriptionListItemDto>
            {
                new() { PrescriptionId = 1, CustomerId = 7, IssueDate = new DateTime(2026, 1, 1), DoctorName = "Dr A" },
                new() { PrescriptionId = 2, CustomerId = 7, IssueDate = new DateTime(2026, 6, 1), DoctorName = "Dr B" },
            });

        var vm = new CustomerPrescriptionsViewModel(
            listUseCase.Object,
            Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(),
            Mock.Of<IDeletePrescriptionUseCase>(),
            Mock.Of<IDialogService>());

        await vm.InitializeAsync(7);

        Assert.Equal(2, vm.Prescriptions.Count);
        Assert.True(vm.HasPrescriptions);
        // Tri décroissant par date d'émission conservé (iso-fonctionnel).
        Assert.Equal("Dr B", vm.Prescriptions[0].DoctorName);
        listUseCase.Verify(u => u.ExecuteAsync(It.Is<ListPrescriptionsByCustomerQuery>(q => q.CustomerId == 7), It.IsAny<CancellationToken>()), Times.Once);
    }
}
