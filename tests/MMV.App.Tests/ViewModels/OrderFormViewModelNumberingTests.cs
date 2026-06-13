using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
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
/// </summary>
public class OrderFormViewModelNumberingTests
{
    private static Mock<ICustomerRepository> CustomerRepo()
    {
        var repo = new Mock<ICustomerRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Customer>());
        return repo;
    }

    private static Mock<IProductRepository> ProductRepo()
    {
        var repo = new Mock<IProductRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Product>());
        return repo;
    }

    [Fact]
    public async Task InitializeAsync_AssignsOrderNumberFromSequenceService_NotCounting()
    {
        var customerRepo = CustomerRepo();
        var productRepo = ProductRepo();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();

        var numberSequence = new Mock<INumberSequenceService>();
        numberSequence.Setup(s => s.NextNumberAsync(DocumentSequenceNames.Order, It.IsAny<CancellationToken>()))
            .ReturnsAsync("CMD-000007");

        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        var viewModel = new OrderFormViewModel(
            customerRepo.Object, productRepo.Object, prescriptionRepo.Object,
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
        var customerRepo = new Mock<ICustomerRepository>();
        var productRepo = new Mock<IProductRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();

        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderFormViewModel(
            customerRepo.Object, productRepo.Object, prescriptionRepo.Object,
            null!, createOrderUseCase.Object, updateOrderUseCase.Object));
    }
}
