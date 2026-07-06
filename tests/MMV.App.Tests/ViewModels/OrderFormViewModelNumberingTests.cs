using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.ListCustomersForPicker;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Domain.Interfaces.Persistence;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2A-1E — Numérotation fiable côté formulaire de commande (R-03 / ADR-006).
///
/// Vérifie que <see cref="OrderFormViewModel"/> : (1) obtient son <c>OrderNumber</c> du
/// <see cref="INumberSequenceService"/> (séquence <c>ORDER</c>) à l'ouverture — et non plus d'un comptage
/// <c>count + 1</c> sujet aux collisions ; (2) ne peut pas être construit sans ce service (erreur de
/// configuration). La preuve d'unicité concurrente sur vrai SQLite est apportée par
/// <c>EfNumberSequenceServiceTests</c> (couche Infrastructure) ; ici on isole le ViewModel.
///
/// P2D-6 : les lectures de référence (clients / produits / ordonnances) passent par des query use cases Application
/// renvoyant des DTO plats — le formulaire ne reçoit plus de repositories.
/// </summary>
public class OrderFormViewModelNumberingTests
{
    private static Mock<IListCustomersForPickerUseCase> CustomersUseCase()
    {
        var useCase = new Mock<IListCustomersForPickerUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<ListCustomersForPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerPickerItemDto>());
        return useCase;
    }

    private static Mock<IListProductsForOrderPickerUseCase> ProductsUseCase()
    {
        var useCase = new Mock<IListProductsForOrderPickerUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<ListProductsForOrderPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderProductPickerDto>());
        return useCase;
    }

    [Fact]
    public async Task InitializeAsync_AssignsOrderNumberFromSequenceService_NotCounting()
    {
        var customersUseCase = CustomersUseCase();
        var productsUseCase = ProductsUseCase();
        var prescriptionsUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();

        var numberSequence = new Mock<INumberSequenceService>();
        numberSequence.Setup(s => s.NextNumberAsync(DocumentSequenceNames.Order, It.IsAny<CancellationToken>()))
            .ReturnsAsync("CMD-000007");

        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        var viewModel = new OrderFormViewModel(
            customersUseCase.Object, productsUseCase.Object, prescriptionsUseCase.Object,
            numberSequence.Object, createOrderUseCase.Object, updateOrderUseCase.Object);

        await viewModel.InitializeAsync();

        // Le numéro vient de la séquence (déterministe) ; l'ancien comptage count+1 n'est plus possible — depuis
        // P2B-2I la ViewModel ne reçoit même plus IOrderRepository (persistance entièrement déléguée).
        Assert.Equal("CMD-000007", viewModel.OrderNumber);
        numberSequence.Verify(s => s.NextNumberAsync(DocumentSequenceNames.Order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Constructor_WithoutNumberSequenceService_Throws()
    {
        var customersUseCase = new Mock<IListCustomersForPickerUseCase>();
        var productsUseCase = new Mock<IListProductsForOrderPickerUseCase>();
        var prescriptionsUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();

        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderFormViewModel(
            customersUseCase.Object, productsUseCase.Object, prescriptionsUseCase.Object,
            null!, createOrderUseCase.Object, updateOrderUseCase.Object));
    }
}
