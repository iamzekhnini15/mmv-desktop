using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace MMV.Application.Tests.UseCases.Customers;

/// <summary>
/// P3-2B — Use case « Archiver / réactiver un client » (<see cref="SetCustomerArchivedUseCase"/>), alternative
/// non destructive à la suppression.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>archive un client et <b>persiste</b> réellement l'état ;</item>
///   <item>réactive un client archivé ;</item>
///   <item>client introuvable : <c>CustomerFound = false</c>, aucune écriture ;</item>
///   <item>est <b>strictement idempotent</b> : réappliquer l'état courant renvoie un succès <b>sans aucune
///         écriture</b> (ni <c>UpdateAsync</c>, ni <c>SaveChangesAsync</c>) et <b>sans toucher</b>
///         <c>UpdatedAt</c> ;</item>
///   <item>ne supprime <b>jamais</b> l'historique du client (ordonnance et vente conservées).</item>
/// </list>
/// </summary>
public sealed class SetCustomerArchivedUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public SetCustomerArchivedUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p32b-archive-" + Guid.NewGuid().ToString("N"));
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

    private static SetCustomerArchivedUseCase CreateUseCase(OpticDbContext context)
    {
        return new SetCustomerArchivedUseCase(new CustomerRepository(context), new UnitOfWork(context));
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
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static async Task<SetCustomerArchivedResult> ExecuteAsync(string databasePath, long customerId, bool isArchived)
    {
        using var context = CreateContext(databasePath);
        var useCase = CreateUseCase(context);
        return await useCase.ExecuteAsync(new SetCustomerArchivedCommand
        {
            CustomerId = customerId,
            IsArchived = isArchived
        });
    }

    private static bool ReadIsArchived(string databasePath, long customerId)
    {
        using var context = CreateContext(databasePath);
        return context.Customers.AsNoTracking().Single(c => c.CustomerId == customerId).IsArchived;
    }

    private static DateTime ReadUpdatedAt(string databasePath, long customerId)
    {
        using var context = CreateContext(databasePath);
        return context.Customers.AsNoTracking().Single(c => c.CustomerId == customerId).UpdatedAt;
    }

    // ------------------------------------------------------------------
    // (1) Archivage : l'état est appliqué ET persisté
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ArchivesCustomer_AndPersists()
    {
        var dbPath = PathFor("archive.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var result = await ExecuteAsync(dbPath, customerId, isArchived: true);

        result.CustomerFound.Should().BeTrue();
        result.CustomerId.Should().Be(customerId);
        result.IsArchived.Should().BeTrue();

        ReadIsArchived(dbPath, customerId).Should().BeTrue("l'archivage est réellement persisté");
    }

    // ------------------------------------------------------------------
    // (2) Réactivation d'un client archivé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ReactivatesArchivedCustomer_AndPersists()
    {
        var dbPath = PathFor("reactivate.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        await ExecuteAsync(dbPath, customerId, isArchived: true);
        var result = await ExecuteAsync(dbPath, customerId, isArchived: false);

        result.CustomerFound.Should().BeTrue();
        result.IsArchived.Should().BeFalse();

        ReadIsArchived(dbPath, customerId).Should().BeFalse("la réactivation est réellement persistée");
    }

    // ------------------------------------------------------------------
    // (3) Client introuvable → CustomerFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var survivorId = SeedCustomer(dbPath);

        var result = await ExecuteAsync(dbPath, customerId: 4242, isArchived: true);

        result.CustomerFound.Should().BeFalse();
        result.CustomerId.Should().Be(4242);

        ReadIsArchived(dbPath, survivorId).Should().BeFalse("aucun autre client n'est modifié");
    }

    // ------------------------------------------------------------------
    // (4) Idempotence STRICTE : réappliquer le même état n'écrit RIEN
    //     (ni UpdateAsync, ni SaveChangesAsync) et ne touche PAS UpdatedAt.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ArchiveAlreadyArchivedCustomer_WritesNothing_AndKeepsUpdatedAt()
    {
        var dbPath = PathFor("archive-twice.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        // Premier archivage : écriture réelle (UpdatedAt est légitimement rafraîchi).
        await ExecuteAsync(dbPath, customerId, isArchived: true);
        var updatedAtAfterRealChange = ReadUpdatedAt(dbPath, customerId);

        // Second archivage : le client est DÉJÀ archivé → no-op.
        var second = await ExecuteAsync(dbPath, customerId, isArchived: true);

        second.CustomerFound.Should().BeTrue();
        second.IsArchived.Should().BeTrue();
        ReadIsArchived(dbPath, customerId).Should().BeTrue();
        ReadUpdatedAt(dbPath, customerId).Should().Be(updatedAtAfterRealChange,
            "UpdatedAt reflète la dernière modification RÉELLE, pas une commande sans effet");
    }

    [Fact]
    public async Task ExecuteAsync_ReactivateAlreadyActiveCustomer_WritesNothing_AndKeepsUpdatedAt()
    {
        var dbPath = PathFor("reactivate-twice.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var updatedAtAtSeed = ReadUpdatedAt(dbPath, customerId);

        // Le client est déjà actif : réactiver est un no-op, dès le premier appel.
        var first = await ExecuteAsync(dbPath, customerId, isArchived: false);
        var second = await ExecuteAsync(dbPath, customerId, isArchived: false);

        first.CustomerFound.Should().BeTrue();
        first.IsArchived.Should().BeFalse();
        second.CustomerFound.Should().BeTrue();
        second.IsArchived.Should().BeFalse();

        ReadIsArchived(dbPath, customerId).Should().BeFalse();
        ReadUpdatedAt(dbPath, customerId).Should().Be(updatedAtAtSeed,
            "aucune écriture n'a eu lieu : UpdatedAt est resté celui du seed");
    }

    /// <summary>
    /// Preuve directe (ports mockés, <c>MockBehavior.Strict</c>) : réappliquer l'état courant n'appelle
    /// <b>ni</b> <c>UpdateAsync</c> <b>ni</b> <c>SaveChangesAsync</c>, et ne modifie pas <c>UpdatedAt</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]   // déjà archivé → archiver
    [InlineData(false)]  // déjà actif   → réactiver
    public async Task ExecuteAsync_SameState_CallsNeitherUpdateNorSaveChanges(bool currentAndTargetState)
    {
        var updatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var customer = new Customer { CustomerId = 7, FirstName = "Jean", LastName = "Dupont", UpdatedAt = updatedAt };
        if (currentAndTargetState)
        {
            customer.Archive();
        }

        var customerRepository = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customerRepository
            .Setup(r => r.GetByIdAsync(customer.CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var useCase = new SetCustomerArchivedUseCase(customerRepository.Object, unitOfWork.Object);

        var result = await useCase.ExecuteAsync(new SetCustomerArchivedCommand
        {
            CustomerId = customer.CustomerId,
            IsArchived = currentAndTargetState
        });

        result.CustomerFound.Should().BeTrue();
        result.IsArchived.Should().Be(currentAndTargetState);

        customerRepository.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never,
            "réappliquer l'état courant ne doit produire aucune écriture");
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "réappliquer l'état courant ne doit valider aucune transaction");
        customer.UpdatedAt.Should().Be(updatedAt, "UpdatedAt ne doit pas être touché par une commande sans effet");
    }

    // ------------------------------------------------------------------
    // (5) L'archivage ne détruit AUCUN historique
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Archiving_KeepsPrescriptionsAndSales()
    {
        var dbPath = PathFor("archive-keeps-history.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var seed = CreateContext(dbPath))
        {
            seed.Prescriptions.Add(new Prescription
            {
                CustomerId = customerId,
                IssueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5)
            });
            seed.Sales.Add(new Sale
            {
                SaleNumber = "VTE-TEST-0002",
                CustomerId = customerId,
                SaleDate = DateTime.UtcNow.AddDays(-3),
                TotalAmount = 100m,
                FinalAmount = 100m,
                PaymentMethod = PaymentMethod.Cash,
                Status = SaleStatus.Delivered
            });
            seed.SaveChanges();
        }

        await ExecuteAsync(dbPath, customerId, isArchived: true);

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.CustomerId == customerId,
            "l'archivage ne supprime aucune ordonnance");
        verify.Sales.AsNoTracking().Should().ContainSingle(s => s.CustomerId == customerId,
            "l'archivage ne supprime ni ne détache aucune vente");
    }

    // ------------------------------------------------------------------
    // (6) Gardes : commande nulle et dépendances manquantes
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

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new SetCustomerArchivedUseCase(customerRepository: null!, new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new SetCustomerArchivedUseCase(new CustomerRepository(context), unitOfWork: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
