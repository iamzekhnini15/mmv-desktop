using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.Tests.TestDoubles;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Time;

/// <summary>
/// P4-5D — <b>le temps est devenu une donnée d'entrée du use case</b>
/// (ADR-PROD-DB-004 §5, décision 2, obligation T1 ; objectif « tests déterministes »).
///
/// <para>
/// Avant ce lot, un horodatage métier ne pouvait être asserté qu'à une tolérance près
/// (« <c>SaleDate</c> est proche de maintenant »), et le test dépendait de sa propre durée d'exécution.
/// Ici, chaque assertion est une <b>égalité stricte</b> avec l'instant que le test a choisi — ce qui n'est
/// possible que parce que le use case a cessé d'aller chercher l'heure lui-même.
/// </para>
///
/// <para>
/// Ces tests vérifient aussi une propriété que le simple remplacement de <c>DateTime.Now</c> n'aurait pas
/// donnée : <b>un acte de gestion = un instant</b>. Les cinq horodatages d'une vente proviennent d'une
/// unique lecture d'horloge, donc la vente, sa commande fournisseur et ses mouvements de stock sont
/// horodatés <i>ensemble</i> et non à quelques microsecondes d'écart.
/// </para>
/// </summary>
public sealed class InjectedClockDeterminismTests : IDisposable
{
    /// <summary>Instant de référence de tous les scénarios : fixe, UTC, sans ambiguïté.</summary>
    private static readonly DateTime Instant = new(2026, 9, 18, 10, 20, 30, DateTimeKind.Utc);

    private readonly string _workDirectory;

    public InjectedClockDeterminismTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p45d-clock-" + Guid.NewGuid().ToString("N"));
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
            // Nettoyage best-effort, aligné sur les suites existantes.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath) => new(
        new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options);

    private void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static RegisterSaleUseCase CreateRegisterSale(OpticDbContext context, FixedClock clock)
        => new(
            new SaleRepository(context),
            new OrderRepository(context),
            new ProductRepository(context),
            new CustomerRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            new EfNumberSequenceService(context),
            clock);

    private long SeedLensProduct(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = "VER-001",
            Name = "Verre",
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 50m,
            StockQuantity = 0,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private long SeedFrameProduct(string databasePath, int stock)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = "MON-001",
            Name = "Monture",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = stock,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    // ------------------------------------------------------------------ RegisterSale

    [Fact]
    public async Task UneVente_EstHorodateeExactementALInstantDeLHorloge()
    {
        var dbPath = PathFor("sale-date.db");
        EnsureSchema(dbPath);
        var productId = SeedFrameProduct(dbPath, stock: 5);

        using (var context = CreateContext(dbPath))
        {
            await CreateRegisterSale(context, new FixedClock(Instant)).ExecuteAsync(new RegisterSaleCommand
            {
                IsCounterSale = true,
                TotalAmount = 20m,
                FinalAmount = 20m,
                RemainingAmount = 20m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = productId, ItemType = OrderItemType.Frame, Quantity = 2, UnitPrice = 10m
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = await verify.Sales.SingleAsync();

        // Égalité STRICTE — impossible à écrire avant P4-5D.
        sale.SaleDate.Should().Be(Instant);
        sale.SaleDate.Kind.Should().Be(DateTimeKind.Utc);
        sale.EstimatedDelivery.Should().Be(Instant, "une vente comptoir est livrée immédiatement");
    }

    [Fact]
    public async Task UneVenteAvecVerres_PartageUnSeulInstantEntreVente_CommandeEtMouvements()
    {
        // La propriété « un acte = un instant ». Cinq horodatages, une seule lecture d'horloge : la vente,
        // l'échéance de livraison, la commande fournisseur et son échéance décrivent le MÊME événement.
        var dbPath = PathFor("sale-coherence.db");
        EnsureSchema(dbPath);
        var lensId = SeedLensProduct(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await CreateRegisterSale(context, new FixedClock(Instant)).ExecuteAsync(new RegisterSaleCommand
            {
                IsCounterSale = false,
                TotalAmount = 100m,
                FinalAmount = 100m,
                RemainingAmount = 100m,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 50m, Sphere = -2.5
                    },
                    new RegisterSaleLineCommand
                    {
                        ProductId = lensId, ItemType = OrderItemType.LensOg, Quantity = 1, UnitPrice = 50m, Sphere = -3.0
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = await verify.Sales.SingleAsync();
        var order = await verify.Orders.SingleAsync();

        sale.SaleDate.Should().Be(Instant);
        sale.EstimatedDelivery.Should().Be(Instant.AddDays(14));
        order.OrderDate.Should().Be(Instant, "la commande fournisseur naît de la même vente, au même instant");
        order.EstimatedDelivery.Should().Be(Instant.AddDays(14));
    }

    [Fact]
    public async Task LeMouvementDeStockDUneVente_PorteLInstantDeLaVente()
    {
        var dbPath = PathFor("sale-movement.db");
        EnsureSchema(dbPath);
        var productId = SeedFrameProduct(dbPath, stock: 5);

        using (var context = CreateContext(dbPath))
        {
            await CreateRegisterSale(context, new FixedClock(Instant)).ExecuteAsync(new RegisterSaleCommand
            {
                IsCounterSale = true,
                TotalAmount = 20m,
                FinalAmount = 20m,
                RemainingAmount = 20m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = productId, ItemType = OrderItemType.Frame, Quantity = 2, UnitPrice = 10m
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var movement = await verify.StockMovements.SingleAsync();

        movement.CreatedAt.Should().Be(Instant);
        movement.Quantity.Should().Be(-2, "non-régression : le signe de sortie est inchangé (P3-5)");
    }

    [Fact]
    public async Task DeuxHorlogesDifferentes_ProduisentDeuxHorodatagesDifferents()
    {
        // Contrepoint du déterminisme : prouve que la valeur vient RÉELLEMENT de l'horloge injectée, et
        // non d'un hasard qui rendrait les assertions précédentes vraies pour de mauvaises raisons.
        var earlier = new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 11, 30, 19, 45, 0, DateTimeKind.Utc);

        (await SaleDateWithClock("clock-a.db", earlier)).Should().Be(earlier);
        (await SaleDateWithClock("clock-b.db", later)).Should().Be(later);
    }

    private async Task<DateTime> SaleDateWithClock(string fileName, DateTime instant)
    {
        var dbPath = PathFor(fileName);
        EnsureSchema(dbPath);
        var productId = SeedFrameProduct(dbPath, stock: 5);

        using (var context = CreateContext(dbPath))
        {
            await CreateRegisterSale(context, new FixedClock(instant)).ExecuteAsync(new RegisterSaleCommand
            {
                IsCounterSale = true,
                TotalAmount = 20m,
                FinalAmount = 20m,
                RemainingAmount = 20m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = productId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 20m
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        return (await verify.Sales.SingleAsync()).SaleDate;
    }

    // ------------------------------------------------------------------ GenerateLowStockNotifications

    [Fact]
    public async Task UneAlerteDeStockBas_EstHorodateeParLHorlogeInjectee()
    {
        var dbPath = PathFor("low-stock.db");
        EnsureSchema(dbPath);
        SeedFrameProduct(dbPath, stock: 0);

        using (var context = CreateContext(dbPath))
        {
            var useCase = new GenerateLowStockNotificationsUseCase(
                new ProductRepository(context),
                new NotificationRepository(context),
                new UnitOfWork(context),
                new EfTransactionRunner(context),
                new FixedClock(Instant));

            var result = await useCase.ExecuteAsync();
            result.CreatedCount.Should().Be(1);
        }

        using var verify = CreateContext(dbPath);
        (await verify.Notifications.SingleAsync()).CreatedAt.Should().Be(Instant);
    }

    [Fact]
    public async Task LaResolutionEtLOuverture_DUnMemePassage_PartagentLeurInstant()
    {
        // Une réconciliation est atomique : ses fermetures et ses ouvertures appartiennent au même passage.
        // Relire l'horloge à chaque écriture aurait produit des microdécalages faisant passer une opération
        // unique pour une suite d'événements distincts.
        var dbPath = PathFor("low-stock-reconciliation.db");
        EnsureSchema(dbPath);
        var productId = SeedFrameProduct(dbPath, stock: 50);

        using (var context = CreateContext(dbPath))
        {
            // Alerte active pour un produit qui n'est PLUS sous seuil : elle doit être résolue.
            context.Notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Stock bas",
                Message = "m",
                EntityId = productId,
                EntityType = "Product",
                CreatedAt = Instant.AddDays(-1)
            });
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(dbPath))
        {
            await new GenerateLowStockNotificationsUseCase(
                new ProductRepository(context),
                new NotificationRepository(context),
                new UnitOfWork(context),
                new EfTransactionRunner(context),
                new FixedClock(Instant)).ExecuteAsync();
        }

        using var verify = CreateContext(dbPath);
        var notification = await verify.Notifications.SingleAsync();

        notification.ResolvedAt.Should().Be(Instant);
        notification.ResolvedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ------------------------------------------------------------------ SettleOrderBalance

    [Fact]
    public async Task UneNotificationDEncaissement_EstHorodateeParLHorlogeInjectee()
    {
        var dbPath = PathFor("settle.db");
        EnsureSchema(dbPath);

        long orderId;
        using (var context = CreateContext(dbPath))
        {
            var sale = new Sale
            {
                SaleNumber = "VTE-000001",
                SaleDate = Instant.AddDays(-3),
                TotalAmount = 100m,
                FinalAmount = 100m,
                DepositAmount = 30m,
                RemainingAmount = 70m,
                PaymentMethod = PaymentMethod.Card,
                PaymentStatus = PaymentStatus.Partial,
                Status = SaleStatus.AwaitingLenses
            };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();

            var order = new Order
            {
                SaleId = sale.SaleId,
                OrderNumber = "CMD-000001",
                OrderDate = Instant.AddDays(-3),
                Status = OrderStatus.New
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.OrderId;
        }

        using (var context = CreateContext(dbPath))
        {
            await new SettleOrderBalanceUseCase(
                new OrderRepository(context),
                new SaleRepository(context),
                new UnitOfWork(context),
                new EfTransactionRunner(context),
                new FixedClock(Instant),
                new NotificationRepository(context)).ExecuteAsync(new SettleOrderBalanceCommand
                {
                    OrderId = orderId
                });
        }

        using var verify = CreateContext(dbPath);
        (await verify.Notifications.SingleAsync()).CreatedAt.Should().Be(Instant);
    }

    // ------------------------------------------------------------------ garde de construction

    [Fact]
    public void UnUseCase_RefuseUneHorlogeNulle()
    {
        // L'horloge est une dépendance OBLIGATOIRE, au même titre que le repository : un use case sans
        // horloge ne peut plus horodater quoi que ce soit, et doit le dire à la construction.
        var dbPath = PathFor("null-clock.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var act = () => new GenerateLowStockNotificationsUseCase(
            new ProductRepository(context),
            new NotificationRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            clock: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }
}
