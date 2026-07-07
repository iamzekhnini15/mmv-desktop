using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-GLOBAL — Vérifie que <see cref="SupplierFormViewModel"/> ne fait plus aucune persistance directe et délègue
/// au bon use case Application selon le mode (création vs édition), et rejette les dépendances nulles.
/// </summary>
public sealed class SupplierFormViewModelDelegationTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount;
        while (!condition() && Environment.TickCount - start < timeoutMs)
            await Task.Delay(10);
    }

    [Fact]
    public async Task Save_InCreateMode_DelegatesToCreateUseCase()
    {
        var create = new Mock<ICreateSupplierUseCase>();
        create.Setup(u => u.ExecuteAsync(It.IsAny<CreateSupplierCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateSupplierResult { SupplierId = 42, Name = "Neuf" });
        var update = new Mock<IUpdateSupplierUseCase>();

        var vm = new SupplierFormViewModel(create.Object, update.Object);
        vm.InitializeForCreate();
        vm.Name = "Neuf";

        var savedRaised = false;
        vm.SupplierSaved += (_, _) => savedRaised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => savedRaised);

        create.Verify(u => u.ExecuteAsync(It.Is<CreateSupplierCommand>(c => c.Name == "Neuf"), It.IsAny<CancellationToken>()), Times.Once);
        update.Verify(u => u.ExecuteAsync(It.IsAny<UpdateSupplierCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(savedRaised);
    }

    [Fact]
    public async Task Save_InEditMode_DelegatesToUpdateUseCase()
    {
        var create = new Mock<ICreateSupplierUseCase>();
        var update = new Mock<IUpdateSupplierUseCase>();
        update.Setup(u => u.ExecuteAsync(It.IsAny<UpdateSupplierCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateSupplierResult { SupplierFound = true, SupplierId = 7 });

        var vm = new SupplierFormViewModel(create.Object, update.Object);
        vm.InitializeForEdit(new SupplierDetailsDto { SupplierId = 7, Name = "Ancien" });
        vm.Name = "Modifié";

        var savedRaised = false;
        vm.SupplierSaved += (_, _) => savedRaised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => savedRaised);

        update.Verify(u => u.ExecuteAsync(It.Is<UpdateSupplierCommand>(c => c.SupplierId == 7 && c.Name == "Modifié"), It.IsAny<CancellationToken>()), Times.Once);
        create.Verify(u => u.ExecuteAsync(It.IsAny<CreateSupplierCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new SupplierFormViewModel(null!, Mock.Of<IUpdateSupplierUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new SupplierFormViewModel(Mock.Of<ICreateSupplierUseCase>(), null!));
    }
}
