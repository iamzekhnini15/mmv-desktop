using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P3-7 (revue avant commit) — <see cref="RegisterSaleUseCase"/> ne doit jamais fonder une décision sur une
/// entité <b>déjà suivie et périmée</b> du <c>DbContext</c> de la portée.
/// </summary>
/// <remarks>
/// <para>
/// <c>ExecuteUpdateAsync</c> (les prises atomiques <c>TryAcquireActiveAsync</c>) contourne délibérément le change
/// tracker : il n'actualise aucune entité déjà suivie. Si les lectures effectuées <b>après</b> la prise
/// utilisaient <c>GetByIdAsync</c> (donc <c>FindAsync</c>, qui renvoie une entité déjà suivie sans requêter la
/// base), un client ou un produit chargé <b>avec tracking</b> plus tôt dans le même <c>DbContext</c> — un écran
/// catalogue/client resté ouvert dans le même processus — pourrait faire lire une valeur périmée.
/// </para>
/// <para>
/// Ces tests forcent délibérément une entité tracked-et-périmée dans le <c>DbContext</c> utilisé par le use case
/// AVANT de muter la même ligne depuis un <b>second</b> contexte (donc au niveau base), puis vérifient que la
/// décision reflète la base réelle et non la copie suivie.
/// </para>
/// </remarks>
public sealed class RegisterSaleTrackedStaleEntityTests : IDisposable
{
    private readonly string _workDirectory;

    public RegisterSaleTrackedStaleEntityTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-7-stale-" + Guid.NewGuid().ToString("N"));
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
            new EfNumberSequenceService(context));

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Use", LastName = "Case" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
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
            IsActive = true,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static RegisterSaleLineCommand Frame(long productId, int quantity, decimal unitPrice)
        => new() { ProductId = productId, ItemType = OrderItemType.Frame, Quantity = quantity, UnitPrice = unitPrice };

    /// <remarks>
    /// <b>Intention d'origine conservée, résultat attendu corrigé par P3-11.</b> Ce test prouve depuis P3-7 que la
    /// décision suit la catégorie <b>réelle en base</b> et non la copie suivie et périmée. Il l'affirmait alors en
    /// observant l'<i>absence</i> de décrément — c'est-à-dire, on le sait depuis la recette P3-11, en gravant le
    /// cas B du défaut de classification (une ligne <c>Frame</c> sur un produit <c>VERRE</c> encaissée sans jamais
    /// sortir du stock). L'invariant observé devient donc le <b>refus métier</b>, qui distingue les deux lectures
    /// de façon strictement plus nette : si la copie suivie <c>MONTURE</c> décidait, la ligne <c>Frame</c> serait
    /// <b>compatible</b> et la vente <b>acceptée</b> ; c'est parce que la catégorie fraîche <c>VERRE</c> est lue
    /// que la vente est refusée. Aucune tolérance n'est introduite, et l'écart d'inventaire silencieux disparaît.
    /// </remarks>
    [Fact]
    public async Task ProduitSuiviCommeMonture_ChangeDeCategorieEnBase_LaDecisionDeStockSuitLaCategorieReelle()
    {
        // Produit initialement MONTURE (décrémenté à la vente), suivi dans le DbContext du use case.
        var dbPath = PathFor("product-category-stale.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-STALE");

        using var context = CreateContext(dbPath);

        // Charge le produit AVEC tracking dans CE contexte, comme le ferait un écran catalogue resté ouvert dans
        // la même portée applicative : le change tracker retient désormais Category = MONTURE.
        var tracked = context.Products.Single(p => p.ProductId == productId);
        tracked.Category.Should().Be(ProductCategoryEnum.MONTURE);

        // Un AUTRE contexte (autre poste / autre écran) reclasse le produit en VERRE avant l'enregistrement.
        using (var other = CreateContext(dbPath))
        {
            other.Products.Single(p => p.ProductId == productId).Category = ProductCategoryEnum.VERRE;
            other.SaveChanges();
        }

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(productId, 1, 30m) }
        });

        var thrown = await act.Should().ThrowAsync<MMV.Domain.Exceptions.BusinessRuleException>();
        thrown.Which.Message.Should().Be(
            SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage,
            "la catégorie RÉELLE est VERRE : une ligne Frame la contredit et doit être refusée ; si la copie suivie MONTURE décidait, la vente serait acceptée");

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "aucune vente ne subsiste après un refus");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(5, "un refus ne touche jamais le stock");
        verify.StockMovements.Count().Should().Be(0, "un refus ne produit aucun mouvement");
    }

    [Fact]
    public async Task ClientSuiviCommeActif_ArchiveEnBaseAvantLaPrise_LaVenteEstRefuseeCommeArchivee()
    {
        var dbPath = PathFor("customer-archived-stale.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-CUST-STALE");

        using var context = CreateContext(dbPath);

        // Charge le client AVEC tracking dans CE contexte (écran client resté ouvert) : le change tracker retient
        // IsArchived = false.
        var tracked = context.Customers.Single(c => c.CustomerId == customerId);
        tracked.IsArchived.Should().BeFalse();

        // Un AUTRE contexte archive le client avant la prise conditionnelle.
        using (var other = CreateContext(dbPath))
        {
            other.Customers.Single(c => c.CustomerId == customerId).Archive();
            other.SaveChanges();
        }

        var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(productId, 1, 30m) }
        });

        var thrown = await act.Should().ThrowAsync<MMV.Domain.Exceptions.BusinessRuleException>();
        thrown.Which.Message.Should().Be(CreatePrescriptionUseCase.CustomerArchivedMessage,
            "la décision provient de l'état réel de la base, pas de la copie suivie encore active");

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "aucune vente ne subsiste après un refus");
    }
}
