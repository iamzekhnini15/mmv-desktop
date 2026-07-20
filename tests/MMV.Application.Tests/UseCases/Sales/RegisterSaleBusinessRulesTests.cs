using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Services;
using MMV.Domain.Validators;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P3-7 — Règles métier de l'enregistrement d'une vente (<see cref="RegisterSaleUseCase"/>), prouvées sur un
/// <b>vrai SQLite temporaire</b> (jamais le provider InMemory), conformément à la convention de
/// <c>RegisterSaleUseCaseTests</c>.
/// </summary>
/// <remarks>
/// Couvre les défauts critiques de l'audit : montants falsifiables (C2/C3/C4), <c>SaleItem.TotalPrice</c> jamais
/// affecté (C1), client archivé accepté (C5), produit introuvable silencieux (C8), vente sans client impossible
/// (C9) et <c>PaymentStatus</c> faux (C10). Aucun montant n'est fixé à l'avance : chaque attendu est exprimé à
/// partir des entrées du test.
/// </remarks>
public sealed class RegisterSaleBusinessRulesTests : IDisposable
{
    private readonly string _workDirectory;

    public RegisterSaleBusinessRulesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-7-" + Guid.NewGuid().ToString("N"));
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

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath, bool archived = false)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Use", LastName = "Case" };
        if (archived) customer.Archive();
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static long SeedProduct(
        string databasePath,
        ProductCategoryEnum category,
        int stock,
        string reference,
        bool isActive = true,
        decimal salePrice = 20m)
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
            SalePrice = salePrice,
            StockQuantity = stock,
            IsActive = isActive,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static RegisterSaleLineCommand Frame(long productId, int quantity, decimal unitPrice)
        => new() { ProductId = productId, ItemType = OrderItemType.Frame, Quantity = quantity, UnitPrice = unitPrice };

    /// <summary>Vérifie qu'un refus n'a laissé AUCUNE trace : ni vente, ni numéro consommé, ni mouvement.</summary>
    private static void AssertNothingPersisted(string dbPath)
    {
        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "aucune vente ne doit subsister après un refus");
        verify.SaleItems.Count().Should().Be(0);
        verify.Orders.Count().Should().Be(0);
        verify.StockMovements.Count().Should().Be(0);
        verify.DocumentSequences.AsNoTracking().Single(s => s.SequenceName == DocumentSequenceNames.Sale).CurrentValue
            .Should().Be(0, "un refus ne doit consommer aucun numéro de vente");
    }

    private static async Task<BusinessRuleException> ExecuteAndCaptureRefusalAsync(string dbPath, RegisterSaleCommand command)
    {
        using var context = CreateContext(dbPath);
        var act = () => CreateUseCase(context).ExecuteAsync(command);
        var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
        return thrown.Which;
    }

    // ==================================================================
    // 1. Recalcul monétaire : les montants fournis sont IGNORÉS
    // ==================================================================

    [Fact]
    public async Task MontantsFournisFalsifies_SontIgnores_EtRecalcules()
    {
        var dbPath = PathFor("recalc-falsified.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-100");

        const int quantity = 2;
        const decimal unitPrice = 80m;
        const decimal discount = 10m;
        const decimal deposit = 50m;

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                // Montants totaux DÉLIBÉRÉMENT faux : ils ne doivent être ni lus, ni comparés, ni persistés.
                TotalAmount = 1m,
                FinalAmount = 1m,
                RemainingAmount = -500m,
                DiscountAmount = discount,
                DepositAmount = deposit,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, quantity, unitPrice) }
            });
        }

        var expectedTotal = quantity * unitPrice;
        var expectedFinal = expectedTotal - discount;
        var expectedRemaining = expectedFinal - deposit;

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.TotalAmount.Should().Be(expectedTotal, "le total est recalculé depuis les lignes");
        sale.DiscountAmount.Should().Be(discount);
        sale.FinalAmount.Should().Be(expectedFinal, "le final est recalculé (total − remise)");
        sale.DepositAmount.Should().Be(deposit);
        sale.RemainingAmount.Should().Be(expectedRemaining, "le reste est recalculé (final − acompte)");
    }

    [Fact]
    public async Task TotalPriceDeLigne_EstPersisteCorrectement()
    {
        // Défaut C1 : TotalPrice n'était JAMAIS affecté ⇒ toutes les lignes valaient 0 en base.
        var dbPath = PathFor("line-total.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-101");

        const int quantity = 3;
        const decimal unitPrice = 45.50m;

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, quantity, unitPrice) }
            });
        }

        using var verify = CreateContext(dbPath);
        var item = verify.SaleItems.AsNoTracking().Single();
        item.TotalPrice.Should().Be(quantity * unitPrice);
        item.TotalPrice.Should().NotBe(0m, "le défaut historique était précisément un TotalPrice à zéro");
    }

    [Fact]
    public async Task TotalMultiLignes_EstRecalculeCommeLaSommeDesLignes()
    {
        var dbPath = PathFor("multi-total.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-102");
        var accessoryId = SeedProduct(dbPath, ProductCategoryEnum.CLIPS, stock: 10, reference: "ACC-102");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                TotalAmount = 999m, // ignoré
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    Frame(frameId, 2, 30m),
                    new RegisterSaleLineCommand { ProductId = accessoryId, ItemType = OrderItemType.Accessory, Quantity = 1, UnitPrice = 12.75m }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var items = verify.SaleItems.AsNoTracking().ToList();
        var sale = verify.Sales.AsNoTracking().Single();

        items.Sum(i => i.TotalPrice).Should().Be((2 * 30m) + (1 * 12.75m));
        sale.TotalAmount.Should().Be(items.Sum(i => i.TotalPrice), "le total de la vente est exactement la somme des lignes");
    }

    [Fact]
    public async Task PrixNegocieDifferentDuCatalogue_EstConserve()
    {
        var dbPath = PathFor("negotiated-price.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        const decimal catalogPrice = 200m;
        const decimal negotiatedPrice = 150m;
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-103", salePrice: catalogPrice);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, negotiatedPrice) }
            });
        }

        using var verify = CreateContext(dbPath);
        var item = verify.SaleItems.AsNoTracking().Single();
        item.UnitPrice.Should().Be(negotiatedPrice, "le prix négocié fourni est conservé (remises et promotions légitimes)");
        item.UnitPrice.Should().NotBe(catalogPrice, "Product.SalePrice ne remplace jamais le prix de la commande");
        item.TotalPrice.Should().Be(1 * negotiatedPrice);
    }

    [Fact]
    public async Task PrixZero_EstAccepte()
    {
        var dbPath = PathFor("free.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-104");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 0m) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.AsNoTracking().Single().FinalAmount.Should().Be(0m, "la gratuité reste un geste commercial légitime");
    }

    // ==================================================================
    // 2. PaymentStatus
    // ==================================================================

    [Fact]
    public async Task SansAcompte_LeStatutDePaiement_EstPending()
    {
        var dbPath = PathFor("pay-pending.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-110");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                DepositAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 100m) }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.RemainingAmount.Should().BeGreaterThan(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Pending, "une vente à crédit ne doit pas hériter du défaut EF « Paid »");
    }

    [Fact]
    public async Task AcomptePartiel_LeStatutDePaiement_EstPartial()
    {
        var dbPath = PathFor("pay-partial.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-111");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                DepositAmount = 40m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 100m) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.AsNoTracking().Single().PaymentStatus.Should().Be(PaymentStatus.Partial);
    }

    [Fact]
    public async Task SoldeIntegralementRegle_LeStatutDePaiement_EstPaid()
    {
        var dbPath = PathFor("pay-paid.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-112");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                DepositAmount = 1 * 100m,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 100m) }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.RemainingAmount.Should().Be(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    // ==================================================================
    // 3. Validation des lignes
    // ==================================================================

    [Fact]
    public async Task VenteSansLigne_EstRefusee_AvantConsommationDUnNumero()
    {
        var dbPath = PathFor("no-lines.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = Array.Empty<RegisterSaleLineCommand>()
        });

        refusal.Message.Should().Be(SalePricingPolicy.EmptyLinesMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task LigneSansProduit_EstRefusee()
    {
        var dbPath = PathFor("no-product.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { new RegisterSaleLineCommand { ProductId = null, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 10m } }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.ProductRequiredMessage);
        AssertNothingPersisted(dbPath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task QuantiteNonPositive_EstRefusee_ParUneErreurMetier(int quantity)
    {
        var dbPath = PathFor($"qty-{quantity}.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-120");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, quantity, 10m) }
        });

        // Erreur MÉTIER, jamais l'ArgumentException technique que produisait le décrément de stock.
        refusal.Message.Should().Be(SalePricingPolicy.QuantityMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task PrixNegatif_EstRefuse()
    {
        var dbPath = PathFor("neg-price.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-121");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, -1m) }
        });

        refusal.Message.Should().Be(SalePricingPolicy.UnitPriceMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task AcompteSuperieurAuFinalRecalcule_EstRefuse()
    {
        var dbPath = PathFor("deposit-above.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-122");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            DepositAmount = (1 * 50m) + 1m,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 50m) }
        });

        refusal.Message.Should().Be(SalePricingPolicy.DepositAboveFinalMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task RemiseSuperieureAuTotalRecalcule_EstRefusee()
    {
        var dbPath = PathFor("discount-above.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-123");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            DiscountAmount = (1 * 50m) + 1m,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 50m) }
        });

        refusal.Message.Should().Be(SalePricingPolicy.DiscountAboveTotalMessage);
        AssertNothingPersisted(dbPath);
    }

    // ==================================================================
    // 4. Produits
    // ==================================================================

    [Fact]
    public async Task ProduitIntrouvable_EstRefuse_SansEchecSilencieux()
    {
        // Défaut C8 : le `continue` silencieux persistait la ligne SANS décrémenter le stock.
        var dbPath = PathFor("product-missing.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(productId: 999_999L, quantity: 1, unitPrice: 10m) }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.ProductNotFoundMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task ProduitInactif_EstRefuse()
    {
        var dbPath = PathFor("product-inactive.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-130", isActive: false);

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 10m) }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.ProductInactiveMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task ProduitDesactiveConcurremmentAvantLaPrise_EstRefuse()
    {
        // Le produit est actif au moment où le panier est constitué, puis désactivé par un autre poste AVANT la
        // prise conditionnelle : la vente doit être refusée, car la condition est évaluée à l'écriture.
        var dbPath = PathFor("product-deactivated-race.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-131");

        using (var other = CreateContext(dbPath))
        {
            other.Products.Single(p => p.ProductId == frameId).IsActive = false;
            other.SaveChanges();
        }

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 10m) }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.ProductInactiveMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task PlusieursLignesDuMemeProduit_RestentValides_EtLeStockEstCorrect()
    {
        var dbPath = PathFor("same-product.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-132");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 2, 30m), Frame(frameId, 3, 30m) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.SaleItems.Count().Should().Be(2, "la déduplication des PRISES ne fusionne jamais les lignes");
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(10 - 2 - 3, "chaque ligne décrémente sa propre quantité");
        verify.StockMovements.Count(m => m.ProductId == frameId).Should().Be(2, "un mouvement par ligne");
        verify.Sales.AsNoTracking().Single().TotalAmount.Should().Be((2 * 30m) + (3 * 30m));
    }

    // ==================================================================
    // 5. Client
    // ==================================================================

    [Fact]
    public async Task VenteSansClient_EstAcceptee_EtCustomerIdEstNull()
    {
        // Défaut C9 : le contrat non nullable rendait la vente au comptoir anonyme IMPOSSIBLE.
        var dbPath = PathFor("no-customer.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-140");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = null,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 25m) }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.CustomerId.Should().BeNull("la vente sans client est réellement persistée sans client");

        var movement = verify.StockMovements.AsNoTracking().Single();
        movement.Reason.Should().Be($"Vente {sale.SaleNumber} - Vente sans client");
        movement.Reason.Should().NotContain("Client #", "aucun libellé « Client # » sans identifiant");
    }

    [Fact]
    public async Task ClientExistantActif_EstAccepte()
    {
        var dbPath = PathFor("customer-active.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-141");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 25m) }
            });
        }

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.CustomerId.Should().Be(customerId);
        verify.StockMovements.AsNoTracking().Single().Reason
            .Should().Be($"Vente {sale.SaleNumber} - Client #{customerId}");
        verify.Customers.AsNoTracking().Single(c => c.CustomerId == customerId).IsArchived
            .Should().BeFalse("la prise de ligne ne modifie aucune donnée métier du client");
    }

    [Fact]
    public async Task ClientIntrouvable_EstRefuse()
    {
        var dbPath = PathFor("customer-missing.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-142");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = 999_999L,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 25m) }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.CustomerNotFoundMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task ClientArchive_EstRefuse_AvecLeMessageStableDeP3_3B()
    {
        var dbPath = PathFor("customer-archived.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath, archived: true);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-143");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 25m) }
        });

        refusal.Message.Should().Be(CreatePrescriptionUseCase.CustomerArchivedMessage,
            "un même refus métier produit le même message, quel que soit le flux (P3-3B)");
        refusal.Message.Should().Be(RegisterSaleUseCase.CustomerArchivedMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task ClientArchiveConcurremmentAvantLaPrise_EstRefuse()
    {
        var dbPath = PathFor("customer-archived-race.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-144");

        // Le formulaire a été ouvert avec un client actif ; un autre poste l'archive avant la prise conditionnelle.
        using (var other = CreateContext(dbPath))
        {
            other.Customers.Single(c => c.CustomerId == customerId).Archive();
            other.SaveChanges();
        }

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 25m) }
        });

        refusal.Message.Should().Be(RegisterSaleUseCase.CustomerArchivedMessage);
        AssertNothingPersisted(dbPath);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(5, "aucun stock n'est prélevé lors d'un refus");
    }

    // ==================================================================
    // 6. Données optiques
    // ==================================================================

    [Fact]
    public async Task AxeZeroSurUnVerre_EstNormaliseA180_SurLaVenteEtLaCommande()
    {
        var dbPath = PathFor("optics-axis.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-150");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m,
                        Sphere = -2.0, Cylinder = -1.25, Axis = 0
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.SaleItems.AsNoTracking().Single().Axis.Should().Be(180);
        verify.OrderItems.AsNoTracking().Single().Axis.Should().Be(180, "la commande fournisseur reçoit la même valeur canonique");
    }

    [Fact]
    public async Task DonneesOptiquesPartiellesValides_SontAcceptees()
    {
        var dbPath = PathFor("optics-partial.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-151");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = lensId, ItemType = OrderItemType.LensOg, Quantity = 1, UnitPrice = 60m, Sphere = -1.5
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var item = verify.SaleItems.AsNoTracking().Single();
        item.Sphere.Should().Be(-1.5);
        item.Cylinder.Should().BeNull("une ordonnance complète n'est pas exigée");
    }

    [Fact]
    public async Task CylindreNonNulSansAxe_EstRefuse()
    {
        var dbPath = PathFor("optics-no-axis.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-152");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = false,
            PaymentMethod = PaymentMethod.Card,
            Lines = new[]
            {
                new RegisterSaleLineCommand
                {
                    ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m, Cylinder = -1.5
                }
            }
        });

        refusal.Message.Should().Be(PrescriptionValidator.AxisRequiredMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task AxeSansCylindreOrientable_EstRefuse()
    {
        var dbPath = PathFor("optics-axis-alone.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-153");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = false,
            PaymentMethod = PaymentMethod.Card,
            Lines = new[]
            {
                new RegisterSaleLineCommand
                {
                    ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m, Axis = 90
                }
            }
        });

        refusal.Message.Should().Be(PrescriptionValidator.AxisWithoutCylinderMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task PrismeSansBase_EstRefuse()
    {
        var dbPath = PathFor("optics-prism.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-154");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = false,
            PaymentMethod = PaymentMethod.Card,
            Lines = new[]
            {
                new RegisterSaleLineCommand
                {
                    ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m, PrismValue = 2.0
                }
            }
        });

        refusal.Message.Should().Be(PrescriptionValidator.PrismBaseRequiredMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task BaseSansPrisme_EstRefusee()
    {
        var dbPath = PathFor("optics-base.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-155");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = false,
            PaymentMethod = PaymentMethod.Card,
            Lines = new[]
            {
                new RegisterSaleLineCommand
                {
                    ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m, PrismBase = PrismBase.Up
                }
            }
        });

        refusal.Message.Should().Be(PrescriptionValidator.PrismBaseWithoutValueMessage);
        AssertNothingPersisted(dbPath);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task ValeurOptiqueNonFinie_EstRefusee(double value)
    {
        var dbPath = PathFor($"optics-nonfinite-{value}.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-156");

        var refusal = await ExecuteAndCaptureRefusalAsync(dbPath, new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = false,
            PaymentMethod = PaymentMethod.Card,
            Lines = new[]
            {
                new RegisterSaleLineCommand
                {
                    ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m, Sphere = value
                }
            }
        });

        refusal.Message.Should().Be(PrescriptionValidator.FiniteValueMessage);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task LesDonneesOptiques_SontCopiees_SansMuterAucuneOrdonnance()
    {
        var dbPath = PathFor("optics-snapshot.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-157");

        // Une ordonnance existante, dont la vente ne dépend pas : les valeurs de la vente sont un instantané.
        long prescriptionId;
        using (var seed = CreateContext(dbPath))
        {
            var prescription = new Prescription { CustomerId = customerId, OdSphere = -4.0, OdCylinder = -2.0, OdAxis = 45 };
            seed.Prescriptions.Add(prescription);
            seed.SaveChanges();
            prescriptionId = prescription.PrescriptionId;
        }

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand
                    {
                        ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m,
                        Sphere = -1.0, Cylinder = -0.5, Axis = 10
                    }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        var prescriptionAfter = verify.Prescriptions.AsNoTracking().Single(p => p.PrescriptionId == prescriptionId);
        prescriptionAfter.OdSphere.Should().Be(-4.0, "aucune ordonnance existante n'est modifiée par une vente");
        prescriptionAfter.OdCylinder.Should().Be(-2.0);
        prescriptionAfter.OdAxis.Should().Be(45);

        verify.SaleItems.AsNoTracking().Single().Sphere.Should().Be(-1.0, "la vente porte ses propres valeurs copiées");
    }

    // ==================================================================
    // 7. SaleStatus — état initial cohérent, aucune synchronisation
    // ==================================================================

    [Fact]
    public async Task VenteSansVerre_NaitDelivered()
    {
        var dbPath = PathFor("status-delivered.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 5, reference: "MON-160");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(frameId, 1, 25m) }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.AsNoTracking().Single().Status.Should().Be(SaleStatus.Delivered);
        verify.Orders.Count().Should().Be(0);
    }

    [Fact]
    public async Task VenteComptoirContenantUnVerre_NaitAwaitingLenses()
    {
        // Défaut historique : IsCounterSale = true donnait « livrée » alors qu'une commande verres était créée.
        var dbPath = PathFor("status-counter-lens.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-161");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true, // déclarée « comptoir »…
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m }
                }
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.Count().Should().Be(1, "…mais un verre crée bien une commande fournisseur");
        verify.Sales.AsNoTracking().Single().Status
            .Should().Be(SaleStatus.AwaitingLenses, "une vente qui attend des verres ne peut pas naître « livrée »");
    }

    [Fact]
    public async Task LeStatutDeVente_NEstJamaisSynchroniseApresCreation()
    {
        // P3-7 n'introduit AUCUNE machine à états SaleStatus : la vérité du workflow reste OrderStatus.
        var dbPath = PathFor("status-no-sync.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-162");

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m }
                }
            });
        }

        // La commande avance ; la vente ne bouge pas (dette de modèle documentée, sans nouvelle règle P3-7).
        using (var advance = CreateContext(dbPath))
        {
            advance.Orders.Single().Status = OrderStatus.ToFabricate;
            advance.SaveChanges();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.AsNoTracking().Single().Status.Should().Be(SaleStatus.AwaitingLenses);
        verify.Orders.AsNoTracking().Single().Status.Should().Be(OrderStatus.ToFabricate);
    }

    // ==================================================================
    // 8. Stock et atomicité
    // ==================================================================

    [Fact]
    public async Task StockInsuffisantSurUneLigneUlterieure_AnnuleLesDecrementsPrecedents()
    {
        var dbPath = PathFor("rollback-later-line.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var firstId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 10, reference: "MON-170");
        var secondId = SeedProduct(dbPath, ProductCategoryEnum.CLIPS, stock: 1, reference: "ACC-170");

        using (var context = CreateContext(dbPath))
        {
            var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[]
                {
                    Frame(firstId, 2, 30m), // décrément RÉUSSI…
                    new RegisterSaleLineCommand { ProductId = secondId, ItemType = OrderItemType.Accessory, Quantity = 5, UnitPrice = 10m } // …puis échec
                }
            });

            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == firstId).StockQuantity
            .Should().Be(10, "le décrément déjà effectué sur la première ligne est annulé");
        verify.Products.AsNoTracking().Single(p => p.ProductId == secondId).StockQuantity.Should().Be(1);
        AssertNothingPersisted(dbPath);
    }

    [Fact]
    public async Task DeuxVentesSurLeDernierArticle_AuPlusUneReussit_EtLeStockNEstJamaisNegatif()
    {
        // Concurrence sur le chemin VENTE (P3-5 le prouvait hors vente). SQLite sérialise les écritures : les deux
        // tentatives sont donc exécutées l'une après l'autre sur le MÊME état de départ, ce qui reproduit
        // exactement la décision que le décrément conditionnel doit prendre — sans aucun délai fragile.
        var dbPath = PathFor("concurrent-last-item.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, stock: 1, reference: "MON-171");

        RegisterSaleCommand Command() => new()
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(frameId, 1, 30m) }
        };

        var successes = 0;
        var refusals = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var context = CreateContext(dbPath);
            try
            {
                await CreateUseCase(context).ExecuteAsync(Command());
                successes++;
            }
            catch (InsufficientStockException)
            {
                refusals++;
            }
        }

        successes.Should().Be(1, "un seul poste peut emporter le dernier article");
        refusals.Should().Be(1, "le perdant reçoit une erreur métier contrôlée");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == frameId).StockQuantity
            .Should().Be(0).And.BeGreaterThanOrEqualTo(0, "le stock n'est jamais négatif");
        verify.Sales.Count().Should().Be(1, "la vente perdante est totalement annulée");
        verify.StockMovements.Count().Should().Be(1, "aucun mouvement résiduel");
    }

    [Fact]
    public async Task UnRefusApresCreationDeLOrder_AnnuleAussiLOrder()
    {
        // Verre (Order créé) + accessoire au stock insuffisant : la commande fournisseur ne doit pas survivre.
        var dbPath = PathFor("rollback-order.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, stock: 5, reference: "VER-172");
        var accessoryId = SeedProduct(dbPath, ProductCategoryEnum.CLIPS, stock: 0, reference: "ACC-172");

        using (var context = CreateContext(dbPath))
        {
            var act = () => CreateUseCase(context).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = false,
                PaymentMethod = PaymentMethod.Card,
                Lines = new[]
                {
                    new RegisterSaleLineCommand { ProductId = lensId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m },
                    new RegisterSaleLineCommand { ProductId = accessoryId, ItemType = OrderItemType.Accessory, Quantity = 1, UnitPrice = 10m }
                }
            });

            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.Count().Should().Be(0, "l'Order créé dans la transaction est annulé avec elle");
        verify.OrderItems.Count().Should().Be(0);
        verify.DocumentSequences.AsNoTracking().Single(s => s.SequenceName == DocumentSequenceNames.Order).CurrentValue
            .Should().Be(0, "le numéro de commande n'est pas consommé");
        AssertNothingPersisted(dbPath);
    }
}
