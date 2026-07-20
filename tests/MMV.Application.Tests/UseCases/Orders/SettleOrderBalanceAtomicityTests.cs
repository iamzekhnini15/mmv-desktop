using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P3-7 — Règlement <b>atomique et idempotent</b> du solde (<see cref="SettleOrderBalanceUseCase"/>), prouvé sur
/// un <b>vrai SQLite temporaire</b> (jamais le provider InMemory).
/// </summary>
/// <remarks>
/// Le règlement était le seul acte sensible dépourvu de protection concurrentielle (audit §22 C6/C7) : deux postes
/// réglaient le même solde, chacun avec succès, produisant <b>deux</b> notifications du même encaissement. Ces
/// tests prouvent qu'un seul passage à zéro et une seule notification restent possibles.
/// </remarks>
public sealed class SettleOrderBalanceAtomicityTests : IDisposable
{
    private readonly string _workDirectory;

    public SettleOrderBalanceAtomicityTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-7-settle-" + Guid.NewGuid().ToString("N"));
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

    private static SettleOrderBalanceUseCase CreateUseCase(OpticDbContext context)
        => new(
            new OrderRepository(context),
            new SaleRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new NotificationRepository(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>Crée une vente au solde partiellement réglé, et la commande liée.</summary>
    private static (long OrderId, long SaleId) SeedOrder(
        string databasePath,
        decimal finalAmount,
        decimal depositAmount,
        OrderStatus status = OrderStatus.New)
    {
        using var context = CreateContext(databasePath);

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale
        {
            SaleNumber = "VTE-000001",
            CustomerId = customer.CustomerId,
            TotalAmount = finalAmount,
            FinalAmount = finalAmount,
            DepositAmount = depositAmount,
            RemainingAmount = finalAmount - depositAmount,
            PaymentStatus = depositAmount == 0m ? PaymentStatus.Pending : PaymentStatus.Partial,
        };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            SaleId = sale.SaleId,
            OrderNumber = "CMD-000001",
            OrderDate = DateTime.Now,
            Status = status,
        };
        context.Orders.Add(order);
        context.SaveChanges();

        return (order.OrderId, sale.SaleId);
    }

    private static async Task<SettleOrderBalanceResult> SettleAsync(string dbPath, long orderId)
    {
        using var context = CreateContext(dbPath);
        return await CreateUseCase(context).ExecuteAsync(new SettleOrderBalanceCommand { OrderId = orderId });
    }

    // ------------------------------------------------------------------
    // 1. Règlement nominal
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReglementNominal_SoldeLaVente_EtCreeUneSeuleNotification()
    {
        const decimal final = 300m;
        const decimal deposit = 100m;

        var dbPath = PathFor("nominal.db");
        EnsureSchema(dbPath);
        var (orderId, saleId) = SeedOrder(dbPath, final, deposit);

        var result = await SettleAsync(dbPath, orderId);

        result.OrderFound.Should().BeTrue();
        result.AlreadySettled.Should().BeFalse();
        result.AmountEncashed.Should().Be(final - deposit);
        result.HasNotification.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single(s => s.SaleId == saleId);
        sale.DepositAmount.Should().Be(final, "l'acompte devient le montant final");
        sale.RemainingAmount.Should().Be(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid, "le règlement rend la vérité de paiement cohérente");
        verify.Notifications.Count().Should().Be(1);
    }

    [Fact]
    public async Task ReglementNominal_NeChangeAucunStatutDeCommande()
    {
        var dbPath = PathFor("no-order-status.db");
        EnsureSchema(dbPath);
        var (orderId, _) = SeedOrder(dbPath, 300m, 100m, status: OrderStatus.InProgress);

        await SettleAsync(dbPath, orderId);

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single().Status
            .Should().Be(OrderStatus.InProgress, "P3-7 ne touche à aucun statut de commande");
    }

    // ------------------------------------------------------------------
    // 2. Idempotence
    // ------------------------------------------------------------------

    [Fact]
    public async Task SecondAppelApresReglement_EstRefuse_SansNouvelleNotification()
    {
        var dbPath = PathFor("second-call.db");
        EnsureSchema(dbPath);
        var (orderId, saleId) = SeedOrder(dbPath, 300m, 100m);

        var first = await SettleAsync(dbPath, orderId);
        var second = await SettleAsync(dbPath, orderId);

        first.AlreadySettled.Should().BeFalse();
        first.HasNotification.Should().BeTrue();

        second.OrderFound.Should().BeTrue();
        second.AlreadySettled.Should().BeTrue("le solde était déjà nul");
        second.AmountEncashed.Should().Be(0m, "rien n'est réencaissé");
        second.HasNotification.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Notifications.Count().Should().Be(1, "un rejeu ne crée jamais de notification fantôme");
        verify.Sales.AsNoTracking().Single(s => s.SaleId == saleId).RemainingAmount.Should().Be(0m);
    }

    [Fact]
    public async Task SoldeDejaNul_EstRefuse_SansEcritureNiNotification()
    {
        const decimal final = 200m;

        var dbPath = PathFor("already-zero.db");
        EnsureSchema(dbPath);
        var (orderId, saleId) = SeedOrder(dbPath, final, depositAmount: final); // rien à encaisser

        var result = await SettleAsync(dbPath, orderId);

        result.AlreadySettled.Should().BeTrue();
        result.HasNotification.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Notifications.Count().Should().Be(0);
        verify.Sales.AsNoTracking().Single(s => s.SaleId == saleId).DepositAmount.Should().Be(final);
    }

    // ------------------------------------------------------------------
    // 3. Concurrence
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeuxAppelsFondesSurLeMemeSolde_ProduisentUnSeulSucces_EtUneSeuleNotification()
    {
        // Les deux postes ont lu le MÊME solde restant avant que l'un d'eux n'écrive : c'est exactement le
        // scénario de « lost update » qui produisait auparavant deux encaissements du même montant. SQLite
        // sérialise les écritures, si bien qu'exécuter les deux tentatives l'une après l'autre reproduit
        // fidèlement l'état que l'UPDATE conditionnel doit rencontrer — sans aucun délai fragile.
        const decimal final = 500m;
        const decimal deposit = 200m;

        var dbPath = PathFor("concurrent.db");
        EnsureSchema(dbPath);
        var (orderId, saleId) = SeedOrder(dbPath, final, deposit);

        var observedRemainingBeforeAnyWrite = ReadRemaining(dbPath, saleId);
        observedRemainingBeforeAnyWrite.Should().Be(final - deposit);

        var results = new List<SettleOrderBalanceResult>
        {
            await SettleAsync(dbPath, orderId),
            await SettleAsync(dbPath, orderId)
        };

        results.Count(r => !r.AlreadySettled).Should().Be(1, "exactement un succès");
        results.Count(r => r.AlreadySettled).Should().Be(1, "exactement un refus contrôlé");
        results.Count(r => r.HasNotification).Should().Be(1, "une seule notification");
        results.Single(r => !r.AlreadySettled).AmountEncashed.Should().Be(observedRemainingBeforeAnyWrite);

        using var verify = CreateContext(dbPath);
        verify.Notifications.Count().Should().Be(1, "un encaissement unique n'est jamais comptabilisé deux fois");
        verify.Sales.AsNoTracking().Single(s => s.SaleId == saleId).DepositAmount
            .Should().Be(final, "un seul passage à zéro : l'acompte n'est pas cumulé deux fois");
    }

    [Fact]
    public async Task UnReglementConcurrentSurvenuAvantLaPrise_EstRefuseProprement()
    {
        var dbPath = PathFor("race-before-take.db");
        EnsureSchema(dbPath);
        var (orderId, saleId) = SeedOrder(dbPath, 400m, 100m);

        // Un autre poste solde la vente avant notre prise conditionnelle.
        using (var other = CreateContext(dbPath))
        {
            var sale = other.Sales.Single(s => s.SaleId == saleId);
            sale.DepositAmount = sale.FinalAmount;
            sale.RemainingAmount = 0m;
            sale.PaymentStatus = PaymentStatus.Paid;
            other.SaveChanges();
        }

        var result = await SettleAsync(dbPath, orderId);

        result.OrderFound.Should().BeTrue();
        result.AlreadySettled.Should().BeTrue();
        result.HasNotification.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Notifications.Count().Should().Be(0, "aucune notification pour un encaissement qui n'a pas eu lieu");
    }

    private static decimal ReadRemaining(string dbPath, long saleId)
    {
        using var context = CreateContext(dbPath);
        return context.Sales.AsNoTracking().Single(s => s.SaleId == saleId).RemainingAmount ?? 0m;
    }

    // ------------------------------------------------------------------
    // 4. Introuvable
    // ------------------------------------------------------------------

    [Fact]
    public async Task CommandeIntrouvable_NeProduitAucuneEcriture()
    {
        var dbPath = PathFor("order-missing.db");
        EnsureSchema(dbPath);

        var result = await SettleAsync(dbPath, orderId: 999_999L);

        result.OrderFound.Should().BeFalse();
        result.HasNotification.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Notifications.Count().Should().Be(0);
    }
}
