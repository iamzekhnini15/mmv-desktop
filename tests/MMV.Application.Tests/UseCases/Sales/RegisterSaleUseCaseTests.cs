using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P2B-2C — Premier vertical slice : use case « Enregistrer une vente en magasin »
/// (<see cref="RegisterSaleUseCase"/>), extrait iso-fonctionnellement de
/// <c>SaleFormViewModel.PersistSaleAsync</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>vente comptoir (monture) : crée la vente, attribue <c>VTE-000001</c>, décrémente le stock et
///         écrit un mouvement de stock négatif ; aucune commande fournisseur ;</item>
///   <item>vente avec verres (fabrication) : crée la vente <c>AwaitingLenses</c>, la commande fournisseur
///         <c>CMD-000001</c> avec ses lignes, et NE décrémente PAS le stock ;</item>
///   <item>stock insuffisant : <see cref="InsufficientStockException"/> ⇒ rollback complet (aucune vente,
///         stock inchangé, numéro <c>SALE</c> non consommé) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes primitives P2A (<see cref="EfTransactionRunner"/>,
/// <see cref="EfNumberSequenceService"/>, <see cref="EfStockMutationService"/>) et repositories que le flux
/// d'origine, partageant un unique <see cref="OpticDbContext"/> (donc la même transaction).
/// </summary>
public sealed class RegisterSaleUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public RegisterSaleUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2c-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (transaction partagée).</summary>
    private static RegisterSaleUseCase CreateUseCase(OpticDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        return new RegisterSaleUseCase(
            new SaleRepository(context),
            new OrderRepository(context),
            new ProductRepository(context),
            new CustomerRepository(context),
            new StockMovementRepository(context),
            unitOfWork,
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            new EfNumberSequenceService(context),
            SystemClock.Instance);
    }

    /// <summary>Crée la base (schéma + seed HasData des séquences SALE/ORDER).</summary>
    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Use", LastName = "Case" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static long SeedProduct(string databasePath, ProductCategoryEnum category, int stock, string reference)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = "Produit Test",
            Category = category,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = stock,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    // ------------------------------------------------------------------
    // (1) Vente comptoir (monture) : vente + décrément stock + mouvement, pas de commande
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CounterSale_Frame_DecrementsStock_WritesMovement_AssignsSaleNumber()
    {
        var dbPath = PathFor("counter.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-001");

        RegisterSaleResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new RegisterSaleCommand
            {
                CustomerId = customerId,
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
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.SaleNumber.Should().Be("VTE-000001");
        result.OrderNumber.Should().BeNull("une vente comptoir de monture ne crée pas de commande fournisseur");
        result.OrderId.Should().BeNull();
        result.Status.Should().Be(SaleStatus.Delivered);
        result.Sale.Should().NotBeNull("l'entité vente est exposée pour préserver l'événement OrderSaved");

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        verify.Sales.Single().SaleNumber.Should().Be("VTE-000001");
        verify.Orders.Count().Should().Be(0);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(3, "5 - 2 (décrément atomique conditionnel)");
        var movements = verify.StockMovements.AsNoTracking().Where(m => m.ProductId == productId).ToList();
        movements.Should().ContainSingle();
        movements[0].MovementType.Should().Be(StockMovementType.Out);
        movements[0].Quantity.Should().Be(-2, "sortie de stock négative");
    }

    // ------------------------------------------------------------------
    // (2) Vente avec verres (fabrication) : commande fournisseur + numéro ORDER, pas de décrément
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SaleWithLenses_CreatesSupplierOrder_WithOrderNumber_AndNoStockMovement()
    {
        var dbPath = PathFor("lenses.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 0, reference: "VER-001");

        RegisterSaleResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                TotalAmount = 100m,
                FinalAmount = 100m,
                DepositAmount = 30m,
                RemainingAmount = 70m,
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
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.SaleNumber.Should().Be("VTE-000001");
        result.OrderNumber.Should().Be("CMD-000001");
        result.OrderId.Should().NotBeNull();
        result.Status.Should().Be(SaleStatus.AwaitingLenses);

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        var order = verify.Orders.AsNoTracking().Include(o => o.OrderItems).Single();
        order.OrderNumber.Should().Be("CMD-000001");
        order.SaleId.Should().Be(result.SaleId);
        order.OrderItems.Should().HaveCount(2, "les deux verres OD/OG sont commandés au fournisseur");
        verify.StockMovements.Count().Should().Be(0, "vente fabrication : aucun décrément de stock");
        verify.Products.AsNoTracking().Single(p => p.ProductId == lensId).StockQuantity
            .Should().Be(0, "les verres sont commandés aux fournisseurs, pas prélevés en stock");
    }

    // ------------------------------------------------------------------
    // (3) Stock insuffisant → rollback complet, numéro SALE non consommé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CounterSale_InsufficientStock_RollsBack_NoSale_NoNumberConsumed()
    {
        var dbPath = PathFor("insufficient.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 1, reference: "MON-002");

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                TotalAmount = 30m,
                FinalAmount = 30m,
                RemainingAmount = 30m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = productId, ItemType = OrderItemType.Frame, Quantity = 3, UnitPrice = 10m
                    }
                }
            };

            Func<Task> act = () => useCase.ExecuteAsync(command);
            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "rollback complet : aucune vente persistée");
        verify.StockMovements.Count().Should().Be(0);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(1, "le stock reste inchangé après annulation");
        verify.DocumentSequences.AsNoTracking().Single(s => s.SequenceName == DocumentSequenceNames.Sale).CurrentValue
            .Should().Be(0, "le numéro SALE attribué dans une transaction annulée n'est pas consommé");
    }

    // ------------------------------------------------------------------
    // (4) Commande nulle → ArgumentNullException
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
    // (5) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutTransactionRunner_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var unitOfWork = new UnitOfWork(context);

        Action act = () => _ = new RegisterSaleUseCase(
            new SaleRepository(context),
            new OrderRepository(context),
            new ProductRepository(context),
            new CustomerRepository(context),
            new StockMovementRepository(context),
            unitOfWork,
            null!, // ITransactionRunner absent : dépendance critique (P2A-1C, R-23)
            new EfStockMutationService(context),
            new EfNumberSequenceService(context),
            SystemClock.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (6) P3-5 — Vente FABRICATION avec monture : le NON-VERRE est décrémenté À LA VENTE, le verre non
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FabricationSale_DecrementsNonLensAtSale_NotTheLens()
    {
        var dbPath = PathFor("fab-nonlens.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 4, reference: "MON-010");
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 2, reference: "VER-010");

        RegisterSaleResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false, // vente FABRICATION
                TotalAmount = 120m,
                FinalAmount = 120m,
                RemainingAmount = 120m,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 20m },
                    new RegisterSaleLineCommand { ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 50m, Sphere = -2.0 },
                }
            });
        }

        result.OrderNumber.Should().NotBeNull("le verre crée une commande fournisseur même en vente fabrication");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(3, "la monture (non-verre) est décrémentée à la vente, même en vente fabrication (4 - 1)");
        verify.Products.AsNoTracking().Single(p => p.ProductId == lensId).StockQuantity
            .Should().Be(2, "le verre n'est PAS décrémenté à la vente (il le sera à la fabrication)");

        var movements = verify.StockMovements.AsNoTracking().ToList();
        movements.Should().ContainSingle("un seul mouvement de sortie : celui de la monture");
        movements[0].ProductId.Should().Be(frameId);
        movements[0].MovementType.Should().Be(StockMovementType.Out);
        movements[0].Quantity.Should().Be(-1, "sortie ⇒ delta négatif");
    }

    // ------------------------------------------------------------------
    // (7) P3-5 — Plusieurs lignes du même produit non-verre : stock final correct, pas de double comptage
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleLinesSameNonLensProduct_FinalStockCorrect()
    {
        var dbPath = PathFor("multi-line.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-020");

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                TotalAmount = 60m,
                FinalAmount = 60m,
                RemainingAmount = 60m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 2, UnitPrice = 20m },
                    new RegisterSaleLineCommand { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 3, UnitPrice = 20m },
                }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(5, "10 - 2 - 3 = 5 : chaque ligne décrémente son propre montant");
        verify.StockMovements.AsNoTracking().Count(m => m.ProductId == frameId)
            .Should().Be(2, "un mouvement par ligne");
    }

    // ------------------------------------------------------------------
    // (8) P3-5 — Combinaison monture + verre : chacun décrémenté AU BON MOMENT (vente vs fabrication)
    // ------------------------------------------------------------------

    [Fact]
    public async Task FrameAndLens_EachDecrementedAtTheRightMoment()
    {
        var dbPath = PathFor("frame-and-lens.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 4, reference: "MON-030");
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 2, reference: "VER-030");

        // 1) Vente fabrication : monture décrémentée (4→3), verre non ; une commande fournisseur créée.
        long orderId;
        using (var context = CreateContext(dbPath))
        {
            var result = await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                TotalAmount = 100m,
                FinalAmount = 100m,
                RemainingAmount = 100m,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 20m },
                    new RegisterSaleLineCommand { ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 50m, Sphere = -1.5 },
                }
            });
            orderId = result.OrderId!.Value;
        }

        // 2) Avancement New → ToFabricate (aucun décrément), puis ToFabricate → En fabrication (verre décrémenté).
        await AdvanceAsync(dbPath, orderId, OrderStatus.New, OrderStatus.ToFabricate);
        await AdvanceAsync(dbPath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(3, "monture décrémentée à la vente (4 - 1)");
        verify.Products.AsNoTracking().Single(p => p.ProductId == lensId).StockQuantity
            .Should().Be(1, "verre décrémenté à la fabrication (2 - 1)");

        verify.StockMovements.AsNoTracking().Single(m => m.ProductId == frameId).Quantity.Should().Be(-1);
        verify.StockMovements.AsNoTracking().Single(m => m.ProductId == lensId).Quantity.Should().Be(-1);
    }

    private static async Task AdvanceAsync(string dbPath, long orderId, OrderStatus from, OrderStatus to)
    {
        using var context = CreateContext(dbPath);
        var useCase = new AdvanceOrderStatusUseCase(
            new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            SystemClock.Instance,
            new NotificationRepository(context));

        await useCase.ExecuteAsync(new AdvanceOrderStatusCommand
        {
            OrderId = orderId,
            CurrentStatus = from,
            NextStatus = to,
            CustomerDisplayName = "Client",
            CurrentStatusDisplay = from.ToString(),
            NextStatusDisplay = to.ToString(),
        });
    }
}
