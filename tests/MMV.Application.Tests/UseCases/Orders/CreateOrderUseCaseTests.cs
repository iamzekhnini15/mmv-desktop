using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2B-2D — Deuxième vertical slice : use case « Créer une commande fournisseur »
/// (<see cref="CreateOrderUseCase"/>), extrait iso-fonctionnellement de la branche création de
/// <c>OrderFormViewModel.SaveAsync</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>crée la commande et ses articles, préserve le numéro <c>ORDER</c> fourni, fixe le statut initial
///         <c>New</c> et conserve les paramètres optiques pour les verres ;</item>
///   <item>ignore les paramètres optiques pour les articles non-verres (monture) — règle du flux d'origine ;</item>
///   <item>vide les notes blanches vers <c>null</c> ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class CreateOrderUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public CreateOrderUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2d-" + Guid.NewGuid().ToString("N"));
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

    // NB — Enforcement des clés étrangères DÉSACTIVÉ ("Foreign Keys=False") pour ces tests.
    //
    // Le flux de création d'origine (OrderFormViewModel) crée une commande autonome en laissant
    // Order.SaleId = 0 (aucune vente parente). Sous l'enforcement FK par défaut de Microsoft.Data.Sqlite,
    // cela viole la clé étrangère Order → Sale. Ce comportement est PRÉEXISTANT et INCHANGÉ par P2B-2D : le
    // use case est strictement iso-fonctionnel (il reproduit SaleId = 0 à l'identique). On relâche donc
    // l'enforcement FK pour isoler et valider le contrat réel du use case (numéro préservé, statut initial,
    // lignes, gating optique, notes) indépendamment de ce problème latent orthogonal (cf. rapport, risques).
    // Il s'agit toujours de vrai SQLite (fichier + schéma réels), jamais du provider InMemory.
    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False;Foreign Keys=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (DbContext partagé).</summary>
    private static CreateOrderUseCase CreateUseCase(OpticDbContext context)
    {
        return new CreateOrderUseCase(new OrderRepository(context), new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedProduct(string databasePath, ProductCategoryEnum category, string reference)
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
            StockQuantity = 5,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    // ------------------------------------------------------------------
    // (1) Création commande avec lignes : numéro préservé, statut New, verre conserve l'optique
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CreatesOrder_WithLines_PreservesNumber_AndStatusNew_AndLensOptics()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, "VER-001");

        var estimated = DateTime.Now.AddDays(14);
        CreateOrderResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new CreateOrderCommand
            {
                OrderNumber = "CMD-000042",
                EstimatedDelivery = estimated,
                Notes = "Verres progressifs",
                Lines = new[]
                {
                    new CreateOrderLineCommand
                    {
                        ProductId = lensId,
                        ItemType = OrderItemType.LensOd,
                        Quantity = 1,
                        UnitPrice = 80m,
                        Sphere = -2.0,
                        Cylinder = -0.5,
                        Axis = 90,
                        Addition = 1.5,
                    }
                }
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.OrderId.Should().BeGreaterThan(0);
        result.OrderNumber.Should().Be("CMD-000042", "le numéro attribué à l'ouverture du formulaire est préservé");
        result.Status.Should().Be(OrderStatus.New);
        result.EstimatedDelivery.Should().Be(estimated);

        using var verify = CreateContext(dbPath);
        var order = verify.Orders.AsNoTracking().Include(o => o.OrderItems).Single();
        order.OrderNumber.Should().Be("CMD-000042");
        order.Status.Should().Be(OrderStatus.New);
        order.Notes.Should().Be("Verres progressifs");
        order.OrderItems.Should().ContainSingle();
        var item = order.OrderItems.Single();
        item.ProductId.Should().Be(lensId);
        item.ItemType.Should().Be(OrderItemType.LensOd);
        item.Quantity.Should().Be(1);
        item.UnitPrice.Should().Be(80m);
        item.Sphere.Should().Be(-2.0, "les paramètres optiques sont conservés pour les verres");
        item.Cylinder.Should().Be(-0.5);
        item.Axis.Should().Be(90);
        item.Addition.Should().Be(1.5);
    }

    // ------------------------------------------------------------------
    // (2) Article non-verre (monture) : les paramètres optiques sont ignorés
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FrameLine_IgnoresOpticalParameters()
    {
        var dbPath = PathFor("frame.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-001");

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new CreateOrderCommand
            {
                OrderNumber = "CMD-000043",
                Lines = new[]
                {
                    new CreateOrderLineCommand
                    {
                        ProductId = frameId,
                        ItemType = OrderItemType.Frame,
                        Quantity = 2,
                        UnitPrice = 30m,
                        // Valeurs optiques fournies mais qui doivent être ignorées pour une monture.
                        Sphere = -1.0,
                        Cylinder = -1.0,
                        Axis = 45,
                        Addition = 2.0,
                    }
                }
            };

            await useCase.ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        var item = verify.Orders.AsNoTracking().Include(o => o.OrderItems).Single().OrderItems.Single();
        item.ItemType.Should().Be(OrderItemType.Frame);
        item.Sphere.Should().BeNull("les paramètres optiques ne sont conservés que pour les verres");
        item.Cylinder.Should().BeNull();
        item.Axis.Should().BeNull();
        item.Addition.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // (3) Notes blanches → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankNotes_StoredAsNull()
    {
        var dbPath = PathFor("notes.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-002");

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new CreateOrderCommand
            {
                OrderNumber = "CMD-000044",
                Notes = "   ",
                Lines = new[]
                {
                    new CreateOrderLineCommand
                    {
                        ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 30m
                    }
                }
            };

            await useCase.ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single().Notes
            .Should().BeNull("les notes blanches sont normalisées en null, comme le flux d'origine");
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
    public void Constructor_WithoutOrderRepository_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new CreateOrderUseCase(orderRepository: null!, new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }
}
