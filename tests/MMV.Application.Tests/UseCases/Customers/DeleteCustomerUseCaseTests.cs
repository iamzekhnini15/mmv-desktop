using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P2C-3 puis P3-2B — Use case « Supprimer un client » (<see cref="DeleteCustomerUseCase"/>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>supprime un client <b>sans historique</b> et persiste (CustomerFound = true) ;</item>
///   <item>client introuvable : renvoie <c>CustomerFound = false</c> sans écrire ;</item>
///   <item>P3-2B — <b>refuse</b> la suppression d'un client porteur d'une ordonnance
///         (<see cref="BusinessRuleException"/>, message stable, aucune écriture, ordonnance conservée) ;</item>
///   <item>P3-2B — <b>refuse</b> la suppression d'un client porteur d'une vente (idem, vente conservée) ;</item>
///   <item>P3-2B — le refus n'appelle <b>ni</b> <c>DeleteAsync</c> <b>ni</b> <c>SaveChangesAsync</c> (espions) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendances critiques manquantes : le constructeur rejette <c>null</c>.</item>
/// </list>
/// </summary>
public sealed class DeleteCustomerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public DeleteCustomerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2c3-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    private static DeleteCustomerUseCase CreateUseCase(OpticDbContext context)
    {
        return new DeleteCustomerUseCase(
            new CustomerRepository(context),
            new PrescriptionRepository(context),
            new SaleRepository(context),
            new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean@example.com",
            Phone = "0600000000",
            City = "Paris",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static void SeedPrescription(string databasePath, long customerId)
    {
        using var context = CreateContext(databasePath);
        context.Prescriptions.Add(new Prescription
        {
            CustomerId = customerId,
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
            DoctorName = "Dr Martin"
        });
        context.SaveChanges();
    }

    private static void SeedSale(string databasePath, long customerId)
    {
        using var context = CreateContext(databasePath);
        context.Sales.Add(new Sale
        {
            SaleNumber = "VTE-TEST-0001",
            CustomerId = customerId,
            SaleDate = DateTime.UtcNow.AddDays(-3),
            TotalAmount = 100m,
            FinalAmount = 100m,
            PaymentMethod = PaymentMethod.Cash,
            Status = SaleStatus.Delivered
        });
        context.SaveChanges();
    }

    // ------------------------------------------------------------------
    // (1) Suppression nominale : un client SANS historique est supprimé et persisté
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DeletesExistingCustomer_AndPersists()
    {
        var dbPath = PathFor("delete.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        DeleteCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = customerId });
        }

        result.CustomerFound.Should().BeTrue();
        result.CustomerId.Should().Be(customerId);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().BeEmpty("le client supprimé n'est plus persisté");
    }

    // ------------------------------------------------------------------
    // (2) Client introuvable → CustomerFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var survivorId = SeedCustomer(dbPath);

        DeleteCustomerResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = 4242 });
        }

        result.CustomerFound.Should().BeFalse();
        result.CustomerId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == survivorId,
            "aucun client n'est supprimé quand l'id est introuvable");
    }

    // ------------------------------------------------------------------
    // (3) P3-2B — Client avec ORDONNANCE : suppression refusée, rien n'est détruit
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerWithPrescription_IsRefused_AndKeepsEverything()
    {
        var dbPath = PathFor("has-prescription.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        SeedPrescription(dbPath, customerId);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);

            Func<Task> act = () => useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = customerId });

            (await act.Should().ThrowAsync<BusinessRuleException>())
                .WithMessage(DeleteCustomerUseCase.CustomerHasHistoryMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == customerId,
            "le client porteur d'un historique n'est jamais supprimé");
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.CustomerId == customerId,
            "l'ordonnance est conservée (plus aucune cascade destructrice)");
    }

    // ------------------------------------------------------------------
    // (4) P3-2B — Client avec VENTE : suppression refusée, rien n'est détruit ni anonymisé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerWithSale_IsRefused_AndKeepsEverything()
    {
        var dbPath = PathFor("has-sale.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        SeedSale(dbPath, customerId);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);

            Func<Task> act = () => useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = customerId });

            (await act.Should().ThrowAsync<BusinessRuleException>())
                .WithMessage(DeleteCustomerUseCase.CustomerHasHistoryMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == customerId,
            "le client porteur d'un historique n'est jamais supprimé");
        verify.Sales.AsNoTracking().Should().ContainSingle(s => s.CustomerId == customerId,
            "la vente est conservée ET reste rattachée à son client (plus de SetNull)");
    }

    // ------------------------------------------------------------------
    // (5) P3-2B — Le refus n'écrit rien : ni DeleteAsync, ni SaveChangesAsync
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(true, false)]  // historique = ordonnance seule
    [InlineData(false, true)]  // historique = vente seule
    [InlineData(true, true)]   // historique = les deux
    public async Task ExecuteAsync_Refusal_CallsNeitherDeleteNorSaveChanges(bool hasPrescription, bool hasSale)
    {
        var customer = new Customer { CustomerId = 7, FirstName = "Jean", LastName = "Dupont" };

        var customerRepository = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customerRepository
            .Setup(r => r.GetByIdAsync(customer.CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var prescriptionRepository = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        prescriptionRepository
            .Setup(r => r.ExistsByCustomerIdAsync(customer.CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPrescription);

        var saleRepository = new Mock<ISaleRepository>(MockBehavior.Strict);
        saleRepository
            .Setup(r => r.ExistsByCustomerIdAsync(customer.CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasSale);

        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var useCase = new DeleteCustomerUseCase(
            customerRepository.Object,
            prescriptionRepository.Object,
            saleRepository.Object,
            unitOfWork.Object);

        Func<Task> act = () => useCase.ExecuteAsync(new DeleteCustomerCommand { CustomerId = customer.CustomerId });
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage(DeleteCustomerUseCase.CustomerHasHistoryMessage);

        customerRepository.Verify(r => r.DeleteAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never,
            "un refus métier ne doit jamais marquer l'entité comme supprimée");
        customerRepository.Verify(r => r.DeleteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "un refus métier ne doit jamais valider de transaction");
    }

    // ------------------------------------------------------------------
    // (6) Commande nulle → ArgumentNullException
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = CreateUseCase(context);

        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (7) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(
            customerRepository: null!,
            new PrescriptionRepository(context),
            new SaleRepository(context),
            new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutPrescriptionRepository_Throws()
    {
        var dbPath = PathFor("ctor-prescriptions.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(
            new CustomerRepository(context),
            prescriptionRepository: null!,
            new SaleRepository(context),
            new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutSaleRepository_Throws()
    {
        var dbPath = PathFor("ctor-sales.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(
            new CustomerRepository(context),
            new PrescriptionRepository(context),
            saleRepository: null!,
            new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteCustomerUseCase(
            new CustomerRepository(context),
            new PrescriptionRepository(context),
            new SaleRepository(context),
            unitOfWork: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
