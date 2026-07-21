using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Suppliers;

/// <summary>
/// P2C-GLOBAL — Use cases du module Fournisseurs (Create / Update / Delete), extraits iso-fonctionnellement de
/// <c>SupplierFormViewModel.SaveAsync</c> et <c>SuppliersViewModel.OnDetailDeleteRequested</c>. Prouve sur un
/// <b>vrai SQLite temporaire</b> (jamais EF InMemory) la création, la mise à jour, la suppression, le contrat
/// « introuvable » et les gardes (commande nulle, dépendance de constructeur nulle).
/// </summary>
public sealed class SupplierUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public SupplierUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2cg-supplier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_PersistsSupplier()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);

        CreateSupplierResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateSupplierUseCase(new SupplierRepository(context), new UnitOfWork(context));
            result = await useCase.ExecuteAsync(new CreateSupplierCommand
            {
                Name = "Essilor",
                ContactEmail = "contact@essilor.test",
                Phone = "0102030405",
                Address = "Paris",
                ReferenceCode = "ESS-01"
            });
        }

        result.SupplierId.Should().BeGreaterThan(0);
        result.Name.Should().Be("Essilor");

        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.Name.Should().Be("Essilor");
        supplier.ContactEmail.Should().Be("contact@essilor.test");
        supplier.ReferenceCode.Should().Be("ESS-01");
    }

    [Fact]
    public async Task Update_ExistingSupplier_AppliesChanges()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Ancien" });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.SupplierId;
        }

        UpdateSupplierResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new UpdateSupplierUseCase(new SupplierRepository(context), new UnitOfWork(context));
            result = await useCase.ExecuteAsync(new UpdateSupplierCommand { SupplierId = id, Name = "Nouveau", Phone = "0600000000" });
        }

        result.SupplierFound.Should().BeTrue();
        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.Name.Should().Be("Nouveau");
        supplier.Phone.Should().Be("0600000000");
    }

    [Fact]
    public async Task Update_MissingSupplier_ReturnsNotFound_NoWrite()
    {
        var dbPath = PathFor("update-missing.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = new UpdateSupplierUseCase(new SupplierRepository(context), new UnitOfWork(context));
        var result = await useCase.ExecuteAsync(new UpdateSupplierCommand { SupplierId = 999, Name = "X" });

        result.SupplierFound.Should().BeFalse();
        context.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ExistingSupplier_RemovesIt()
    {
        var dbPath = PathFor("delete.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "ASupprimer" });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.SupplierId;
        }

        DeleteSupplierResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new DeleteSupplierUseCase(new SupplierRepository(context), new EfTransactionRunner(context));
            result = await useCase.ExecuteAsync(new DeleteSupplierCommand { SupplierId = id });
        }

        result.SupplierFound.Should().BeTrue();
        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_MissingSupplier_ReturnsNotFound()
    {
        var dbPath = PathFor("delete-missing.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = new DeleteSupplierUseCase(new SupplierRepository(context), new EfTransactionRunner(context));
        var result = await useCase.ExecuteAsync(new DeleteSupplierCommand { SupplierId = 12345 });

        result.SupplierFound.Should().BeFalse();
    }

    [Fact]
    public async Task NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        var create = new CreateSupplierUseCase(new SupplierRepository(context), new UnitOfWork(context));
        var update = new UpdateSupplierUseCase(new SupplierRepository(context), new UnitOfWork(context));
        var delete = new DeleteSupplierUseCase(new SupplierRepository(context), new EfTransactionRunner(context));

        await ((Func<Task>)(() => create.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => update.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => delete.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullDependencies()
    {
        ((Action)(() => _ = new CreateSupplierUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new UpdateSupplierUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new DeleteSupplierUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
    }
}
