using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P3-11 — garde de classification du flux de stock opposée par <see cref="RegisterSaleUseCase"/>, sur de
/// <b>vraies</b> bases SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Le défaut corrigé n'était observable qu'en traversant la vente <b>puis</b> la fabrication : une ligne dont le
/// type contredit la catégorie du produit était acceptée et produisait soit un <b>double</b> décrément, soit
/// <b>aucun</b>. La garde referme les deux sens <b>avant toute écriture</b>.
/// </para>
/// <para>
/// Chaque refus est vérifié sur l'ensemble des effets de bord possibles — vente, ligne, commande, article de
/// commande, stock, mouvement, notification et <b>séquences de numérotation</b> — depuis un contexte neuf.
/// </para>
/// </remarks>
public sealed class RegisterSaleLineStockFlowGuardTests : IDisposable
{
    private readonly string _workDirectory;

    public RegisterSaleLineStockFlowGuardTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-11-guard-" + Guid.NewGuid().ToString("N"));
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

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static RegisterSaleUseCase CreateUseCase(OpticDbContext context)
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
            SystemClock.Instance);

    private const int StockInitial = 6;

    private static long SeedProduct(string databasePath, ProductCategoryEnum category, string reference)
    {
        using var context = CreateContext(databasePath);

        var supplier = context.Suppliers.FirstOrDefault();
        if (supplier is null)
        {
            supplier = new Supplier { Name = "Fournisseur Test" };
            context.Suppliers.Add(supplier);
            context.SaveChanges();
        }

        var product = new Product
        {
            Reference = reference,
            Name = "Produit " + reference,
            Category = category,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = StockInitial,
            StockAlertThreshold = 1,
            IsActive = true,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    /// <summary>Ligne verre valide (optiques cohérentes : cylindre nul ⇒ aucun axe).</summary>
    private static RegisterSaleLineCommand Lens(long productId, OrderItemType itemType)
        => new()
        {
            ProductId = productId,
            ItemType = itemType,
            Quantity = 1,
            UnitPrice = 100m,
            Sphere = -1.5,
            UsageType = LensUsageType.Distance,
        };

    private static RegisterSaleLineCommand Simple(long productId, OrderItemType itemType)
        => new() { ProductId = productId, ItemType = itemType, Quantity = 1, UnitPrice = 100m };

    /// <summary>
    /// Vérifie qu'un refus n'a laissé <b>strictement aucune</b> trace : ni document, ni stock, ni numérotation.
    /// Lecture depuis un contexte neuf, indispensable puisque le décrément passe par <c>ExecuteUpdateAsync</c>.
    /// </summary>
    private static void AssertAucuneEcriture(string databasePath, long productId)
    {
        using var verify = CreateContext(databasePath);

        verify.Sales.Count().Should().Be(0, "aucune vente ne doit subsister");
        verify.SaleItems.Count().Should().Be(0, "aucune ligne de vente ne doit subsister");
        verify.Orders.Count().Should().Be(0, "aucune commande de fabrication ne doit être créée");
        verify.OrderItems.Count().Should().Be(0, "aucun article de commande ne doit être créé");
        verify.StockMovements.Count().Should().Be(0, "aucun mouvement de stock ne doit être créé");
        verify.Notifications.Count().Should().Be(0, "aucune notification ne doit être créée");

        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial, "le stock doit rester intact");

        // Les séquences sont pré-déclarées par le modèle (CurrentValue = 0, aucun numéro encore attribué) : le
        // critère n'est donc pas leur absence, mais le fait qu'AUCUNE n'ait été CONSOMMÉE.
        verify.DocumentSequences.AsNoTracking().Select(s => s.CurrentValue).Should().OnlyContain(v => v == 0,
            "le refus précède toute numérotation : aucun numéro de vente ni de commande ne doit être consommé");
    }

    // =========================================================================================================
    // Cas A — produit MONTURE vendu sur une ligne verre : c'était le DOUBLE décrément.
    // =========================================================================================================

    [Fact]
    public async Task LigneVerre_SurProduitMonture_EstRefuseeAvantTouteEcriture()
    {
        var dbPath = PathFor("cas-a-monture-sur-ligne-verre.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-9001");

        using var context = CreateContext(dbPath);

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Lens(productId, OrderItemType.LensOd) }
        });

        var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
        thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);

        AssertAucuneEcriture(dbPath, productId);
    }

    // =========================================================================================================
    // Cas B — produit VERRE vendu sur une ligne monture : c'était l'ABSENCE totale de décrément.
    // =========================================================================================================

    [Fact]
    public async Task LigneMonture_SurProduitVerre_EstRefuseeAvantTouteEcriture()
    {
        var dbPath = PathFor("cas-b-verre-sur-ligne-monture.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, "VER-9002");

        using var context = CreateContext(dbPath);

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Simple(productId, OrderItemType.Frame) }
        });

        var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
        thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);

        AssertAucuneEcriture(dbPath, productId);
    }

    [Fact]
    public async Task LigneAccessoire_SurProduitLentille_EstRefuseeAvantTouteEcriture()
    {
        var dbPath = PathFor("cas-b-bis-lentille-sur-accessoire.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.LENTILLE, "LEN-9003");

        using var context = CreateContext(dbPath);

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Simple(productId, OrderItemType.Accessory) }
        });

        var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
        thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);

        AssertAucuneEcriture(dbPath, productId);
    }

    /// <summary>
    /// Une seule ligne incohérente suffit à refuser la vente <b>entière</b> : les lignes cohérentes qui
    /// l'accompagnent ne sont ni persistées ni décrémentées.
    /// </summary>
    [Fact]
    public async Task UneSeuleLigneIncoherente_RefuseLaVenteEntiere()
    {
        var dbPath = PathFor("melange-coherent-et-incoherent.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var montureId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-9004");
        var verreId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, "VER-9004");

        using var context = CreateContext(dbPath);

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[]
            {
                Simple(montureId, OrderItemType.Frame),          // cohérente
                Simple(verreId, OrderItemType.Accessory),        // incohérente
            }
        });

        var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
        thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0);
        verify.StockMovements.Count().Should().Be(0);
        verify.Products.AsNoTracking().Single(p => p.ProductId == montureId).StockQuantity
            .Should().Be(StockInitial, "la ligne cohérente ne doit pas être décrémentée par une vente refusée");
        verify.DocumentSequences.AsNoTracking().Select(s => s.CurrentValue).Should().OnlyContain(v => v == 0);
    }

    // =========================================================================================================
    // Cas nominaux — la garde ne durcit RIEN au-delà de l'incohérence de flux de stock.
    // =========================================================================================================

    [Fact]
    public async Task LigneMonture_SurProduitMonture_EstAccepteeEtDecrementeeUneFois()
    {
        var dbPath = PathFor("nominal-monture.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-9100");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Simple(productId, OrderItemType.Frame) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        verify.Orders.Count().Should().Be(0, "aucun article à consommation différée ⇒ aucune commande");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial - 1, "une monture sort du stock à la vente, exactement une fois");
        verify.StockMovements.Count().Should().Be(1);
    }

    [Fact]
    public async Task LigneAccessoire_SurCategorieImmediate_EstAcceptee()
    {
        var dbPath = PathFor("nominal-accessoire.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.CLIPS, "CLI-9101");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Simple(productId, OrderItemType.Accessory) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial - 1);
        verify.StockMovements.Count().Should().Be(1);
    }

    /// <summary>
    /// <b>Limite explicitement assumée.</b> La règle porte sur le <b>moment de consommation du stock</b>, pas sur
    /// une taxonomie commerciale : une monture vendue sur une ligne <c>Accessory</c> reste acceptée, car les deux
    /// se consomment immédiatement et aucun écart d'inventaire n'en résulte. P3-11 ne prétend pas que
    /// <c>Frame</c> désigne exactement <c>MONTURE</c>.
    /// </summary>
    [Fact]
    public async Task MontureVendueSurUneLigneAccessoire_ResteAcceptee_LaRegleNEstPasUneTaxonomie()
    {
        var dbPath = PathFor("limite-taxonomie.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-9102");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Simple(productId, OrderItemType.Accessory) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial - 1, "un seul chemin de consommation, donc exactement un décrément");
        verify.StockMovements.Count().Should().Be(1);
    }

    [Theory]
    [InlineData(ProductCategoryEnum.VERRE, OrderItemType.LensOd, "VER-9200")]
    [InlineData(ProductCategoryEnum.VERRE, OrderItemType.LensOg, "VER-9201")]
    [InlineData(ProductCategoryEnum.LENTILLE, OrderItemType.LensOd, "LEN-9202")]
    [InlineData(ProductCategoryEnum.LENTILLE, OrderItemType.LensOg, "LEN-9203")]
    public async Task LigneVerre_SurCategorieDifferee_EstAcceptee_EtDecrementeeSeulementALaFabrication(
        ProductCategoryEnum category, OrderItemType itemType, string reference)
    {
        var dbPath = PathFor($"nominal-differe-{reference}.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, category, reference);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Lens(productId, itemType) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1);
        verify.Orders.Count().Should().Be(1, "un article à consommation différée engendre une commande de fabrication");
        verify.OrderItems.Count().Should().Be(1);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial, "un article différé ne sort pas du stock à la vente : il sortira à la fabrication");
        verify.StockMovements.Count().Should().Be(0);
    }
}
