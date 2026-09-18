using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Moq;
using Xunit;

namespace MMV.Application.Tests.UseCases.Prescriptions;

/// <summary>
/// P2C-4 puis <b>P3-12</b> — Use case « Supprimer une ordonnance » (<see cref="DeletePrescriptionUseCase"/>).
///
/// <para>
/// L'audit final P3-12 a établi que ce use case supprimait <b>physiquement et inconditionnellement</b> une
/// ordonnance, sur un chemin public, enregistré en DI et atteignable depuis l'UI, la seule protection étant une
/// boîte de dialogue de confirmation. La remédiation transforme ce chemin en point de refus. Ces tests opposent
/// désormais la conservation.
/// </para>
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory) sauf pour les doubles :
/// <list type="number">
///   <item>ordonnance introuvable : <c>PrescriptionFound = false</c>, aucune exception, aucune écriture ;</item>
///   <item>ordonnance existante : <see cref="BusinessRuleException"/>, aucun <c>Delete</c>, aucun <c>Save</c> ;</item>
///   <item>sur base réelle : l'ordonnance et son client survivent intacts ;</item>
///   <item>tentatives répétées : deux refus, aucun effet cumulé ;</item>
///   <item>le client porteur d'une ordonnance reste protégé (P3-2B, non régressé) ;</item>
///   <item>commande nulle et dépendances manquantes : contrats de garde inchangés.</item>
/// </list>
/// </summary>
public sealed class DeletePrescriptionUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public DeletePrescriptionUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-12-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
                Directory.Delete(_workDirectory, recursive: true);
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

    private static DeletePrescriptionUseCase CreateUseCase(OpticDbContext context)
        => new(new PrescriptionRepository(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>Crée un client et son ordonnance, et renvoie les deux identifiants.</summary>
    private (long CustomerId, long PrescriptionId) SeedPrescription(string databasePath)
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

        var prescription = new Prescription
        {
            CustomerId = customer.CustomerId,
            IssueDate = new DateOnly(2025, 6, 1),
            DoctorName = "Dr House",
            OdSphere = -1.25,
            OdCylinder = -0.75,
            OdAxis = 90
        };
        context.Prescriptions.Add(prescription);
        context.SaveChanges();
        return (customer.CustomerId, prescription.PrescriptionId);
    }

    // ------------------------------------------------------------------
    // T1 — Ordonnance introuvable : contrat P2C-4 préservé, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PrescriptionNotFound_ReturnsNotFound_WithoutThrowing_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        var seeded = SeedPrescription(dbPath);

        DeletePrescriptionResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeletePrescriptionCommand { PrescriptionId = 4242 });
        }

        // Un identifiant absent n'est pas un refus métier : c'est un état multi-poste banal.
        result.PrescriptionFound.Should().BeFalse();
        result.PrescriptionId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.PrescriptionId == seeded.PrescriptionId,
            "aucune ordonnance n'est supprimée quand l'id est introuvable");
    }

    [Fact]
    public async Task ExecuteAsync_PrescriptionNotFound_NeverDeletesNorSaves()
    {
        var repository = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        repository
            .Setup(r => r.GetByIdAsync(4242L, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);

        var useCase = new DeletePrescriptionUseCase(repository.Object);
        var result = await useCase.ExecuteAsync(new DeletePrescriptionCommand { PrescriptionId = 4242 });

        result.PrescriptionFound.Should().BeFalse();
        // Le double est strict : toute primitive non configurée — DeleteAsync en tête — ferait échouer le test.
        // Depuis P3-12, aucune IUnitOfWork n'est même injectée : la sauvegarde est structurellement inatteignable.
        repository.Verify(r => r.DeleteAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.DeleteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // T2 — Ordonnance existante : refus métier, aucune primitive de persistance sollicitée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ExistingPrescription_ThrowsBusinessRule_WithStableMessage()
    {
        var repository = new Mock<IPrescriptionRepository>(MockBehavior.Strict);
        repository
            .Setup(r => r.GetByIdAsync(7L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Prescription { PrescriptionId = 7, CustomerId = 1 });

        var useCase = new DeletePrescriptionUseCase(repository.Object);

        Func<Task> act = () => useCase.ExecuteAsync(new DeletePrescriptionCommand { PrescriptionId = 7 });

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage(PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage);

        // La preuve décisive : aucune primitive d'écriture n'a été sollicitée, même en échec. Le double strict
        // n'autorise que GetByIdAsync ; toute autre sollicitation ferait échouer le test.
        repository.Verify(r => r.DeleteAsync(It.IsAny<Prescription>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.DeleteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.GetByIdAsync(7L, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // T3 — Vraie base SQLite : l'ordonnance et son client survivent intacts
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OnRealSqlite_KeepsPrescriptionAndCustomerIntact()
    {
        var dbPath = PathFor("retention.db");
        EnsureSchema(dbPath);
        var seeded = SeedPrescription(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            Func<Task> act = () => useCase.ExecuteAsync(
                new DeletePrescriptionCommand { PrescriptionId = seeded.PrescriptionId });

            await act.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage(PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage);
        }

        // Relecture par un contexte neuf : ce que la base contient réellement, pas ce qu'un tracker croit.
        using var verify = CreateContext(dbPath);

        verify.Prescriptions.AsNoTracking().Should().HaveCount(1, "le refus ne supprime rien");

        var survivor = verify.Prescriptions.AsNoTracking().Single();
        survivor.PrescriptionId.Should().Be(seeded.PrescriptionId);
        survivor.CustomerId.Should().Be(seeded.CustomerId);
        survivor.DoctorName.Should().Be("Dr House");
        survivor.OdSphere.Should().Be(-1.25, "les valeurs optiques sources sont conservées telles quelles");
        survivor.OdCylinder.Should().Be(-0.75);
        survivor.OdAxis.Should().Be(90);

        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == seeded.CustomerId,
            "aucune autre table n'est touchée par le refus");
    }

    // ------------------------------------------------------------------
    // T4 — Tentatives répétées : deux refus, aucun effet cumulé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RepeatedAttempts_AreRefusedEveryTime_WithoutSideEffect()
    {
        var dbPath = PathFor("repeat.db");
        EnsureSchema(dbPath);
        var seeded = SeedPrescription(dbPath);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // Portée neuve à chaque tentative : c'est ce que fait l'UI, et cela interdit qu'un état de tracker
            // rémanent explique le résultat.
            using var context = CreateContext(dbPath);
            var useCase = CreateUseCase(context);

            Func<Task> act = () => useCase.ExecuteAsync(
                new DeletePrescriptionCommand { PrescriptionId = seeded.PrescriptionId });

            await act.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage(PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.PrescriptionId == seeded.PrescriptionId,
            "un refus répété reste un refus : ni suppression, ni duplication");
    }

    // ------------------------------------------------------------------
    // T5 — Non-régression P3-2B : le client porteur d'une ordonnance reste protégé
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeletingCustomer_WithSurvivingPrescription_IsStillRefused()
    {
        var dbPath = PathFor("customer-guard.db");
        EnsureSchema(dbPath);
        var seeded = SeedPrescription(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var deleteCustomer = new DeleteCustomerUseCase(
                new CustomerRepository(context),
                new PrescriptionRepository(context),
                new SaleRepository(context),
                new UnitOfWork(context));

            Func<Task> act = () => deleteCustomer.ExecuteAsync(
                new DeleteCustomerCommand { CustomerId = seeded.CustomerId });

            await act.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage(DeleteCustomerUseCase.CustomerHasHistoryMessage);
        }

        // La conservation de l'ordonnance ne doit pas ouvrir une brèche latérale : le client reste non supprimable.
        using var verify = CreateContext(dbPath);
        verify.Customers.AsNoTracking().Should().ContainSingle(c => c.CustomerId == seeded.CustomerId);
        verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.PrescriptionId == seeded.PrescriptionId);
    }

    // ------------------------------------------------------------------
    // Contrats de garde inchangés
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
        Action act = () => _ = new DeletePrescriptionUseCase(prescriptionRepository: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// P3-12 — Le use case ne déclare <b>que</b> le repository. La garde <c>Constructor_WithoutUnitOfWork_Throws</c>
    /// a été retirée avec le paramètre qu'elle protégeait : elle n'avait plus d'objet. Ce test oppose la propriété
    /// qui la remplace, et qui est plus forte — la sauvegarde n'est pas « non appelée », elle est
    /// <b>inatteignable</b>, faute de toute dépendance capable d'écrire.
    /// </summary>
    [Fact]
    public void Constructor_DeclaresOnlyThePrescriptionRepository()
    {
        var parameters = typeof(DeletePrescriptionUseCase)
            .GetConstructors()
            .Should().ContainSingle().Subject
            .GetParameters();

        parameters.Should().ContainSingle("aucune dépendance d'écriture n'est injectable dans un point de refus");
        parameters[0].ParameterType.Should().Be<IPrescriptionRepository>();
    }
}
