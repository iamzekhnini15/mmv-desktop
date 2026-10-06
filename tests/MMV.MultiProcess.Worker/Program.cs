using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Policies;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Npgsql;

// P4-10 — un « poste » = un processus. Arguments : <scénario> <paramètres…>.
// Environnement : MMV_WORKER_CONNECTION_STRING (rôle applicatif ou base de test), MMV_WORKER_BARRIER (clé de départ
// commun, optionnelle), MMV_WORKER_BARRIER2 (point de rendez-vous au milieu de la transaction, optionnel).
// Sortie : une seule ligne « RESULT <issue> » ; le code de sortie est toujours 0 si l'issue a pu être écrite.

var connectionString = Environment.GetEnvironmentVariable("MMV_WORKER_CONNECTION_STRING")
                       ?? throw new InvalidOperationException("MMV_WORKER_CONNECTION_STRING absente.");

OpticDbContext NewContext()
{
    var builder = new DbContextOptionsBuilder<OpticDbContext>();
    DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
    {
        Provider = DatabaseProvider.PostgreSql,
        ConnectionString = connectionString
    });
    return new OpticDbContext(builder.Options);
}

async Task BarrierAsync(string variable)
{
    if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } text)
    {
        return;
    }

    var key = long.Parse(text, CultureInfo.InvariantCulture);
    var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT pg_advisory_lock_shared(@k); SELECT pg_advisory_unlock_shared(@k);", connection);
    command.Parameters.AddWithValue("k", key);
    await command.ExecuteNonQueryAsync();
}

static string Error(Exception exception) =>
    PersistenceErrorMapper.Map(exception) is PersistenceException mapped
        ? "error:" + mapped.Category
        : "error:" + exception.GetType().Name;

async Task<string> ComposedSaleAsync(OpticDbContext context, params long[] products)
{
    try
    {
        var number = await new EfTransactionRunner(context).RunAsync(async ct =>
        {
            var saleNumber = await new EfNumberSequenceService(context).NextNumberAsync("SALE", ct);
            context.Sales.Add(new Sale { SaleNumber = saleNumber, FinalAmount = 60m });
            await context.SaveChangesAsync(ct);
            foreach (var product in products)
            {
                await new EfStockMutationService(context).DecrementStockAsync(product, 1, ct);
            }

            return saleNumber;
        });
        return "ok:" + number;
    }
    catch (InsufficientStockException)
    {
        return "insufficient";
    }
    catch (Exception exception)
    {
        return Error(exception);
    }
}

// Deux décréments dans une transaction, sans numérotation (qui sérialiserait), avec rendez-vous entre les deux :
// deux postes croisés (A puis B / B puis A) forment un interblocage réel.
async Task<string> CrossedStockAsync(OpticDbContext context, long first, long second)
{
    try
    {
        await new EfTransactionRunner(context).RunAsync(async ct =>
        {
            await new EfStockMutationService(context).DecrementStockAsync(first, 1, ct);
            await BarrierAsync("MMV_WORKER_BARRIER2");
            await new EfStockMutationService(context).DecrementStockAsync(second, 1, ct);
        });
        return "ok";
    }
    catch (Exception exception)
    {
        return Error(exception);
    }
}

var scenario = args[0];
var p = args.Skip(1).ToArray();
long Arg(int i) => long.Parse(p[i], CultureInfo.InvariantCulture);

string result;
await using (var context = NewContext())
{
    // Préparation propre à chaque scénario AVANT la barrière (ex. vue périmée d'une fiche).
    WorkshopSheetVersionPrecondition staleView = default;
    Order? order = null;
    if (scenario == "sheet")
    {
        var orderId = Arg(0);
        var orders = new OrderRepository(context);
        order = await orders.GetWithItemsAsync(orderId);
        var current = await context.WorkshopSheets.AsNoTracking().SingleAsync(w => w.OrderId == orderId && w.IsCurrent);
        staleView = WorkshopSheetVersionPrecondition.From(current);
    }

    await BarrierAsync("MMV_WORKER_BARRIER");

    try
    {
        result = scenario switch
        {
            "sale" => await ComposedSaleAsync(context, Arg(0)),
            "crossed-stock" => await CrossedStockAsync(context, Arg(0), Arg(1)),
            "advance" => await new OrderRepository(context).TryTransitionStatusAsync(
                Arg(0), Enum.Parse<OrderStatus>(p[1]), Enum.Parse<OrderStatus>(p[2])) ? "taken" : "lost",
            "sheet" => await SheetAsync(),
            "qc" => await new OrderRepository(context).TryTakeWorkshopSheetQcDecisionAsync(
                Arg(0), Enum.Parse<WorkshopSheetQcStatus>(p[1]), "QC", DateTime.UtcNow) ? "taken" : "lost",
            "settle" => await new SaleRepository(context).TrySettleRemainingBalanceAsync(Arg(0)) ? "taken" : "lost",
            "lowstock" => await new NotificationRepository(context).TryCreateActiveLowStockAsync(new Notification
            {
                Type = NotificationTypes.LowStock,
                EntityType = NotificationEntityTypes.Product,
                EntityId = Arg(0),
                Title = "Stock bas",
                Message = "Stock bas",
                CreatedAt = DateTime.UtcNow
            }) ? "created" : "exists",
            "user" => await UserAsync(p[0]),
            "delete-supplier" => await DeleteSupplierAsync(Arg(0)),
            "product" => await ProductAsync(Arg(0), p[1]),
            "product-held" => await HeldProductAsync(Arg(0), p[1]),
            "loop-sales" => await LoopAsync(Arg(0), (int)Arg(1)),
            "hold-sale" => await HoldAsync(Arg(0)),
            _ => throw new ArgumentException($"Scénario inconnu : {scenario}")
        };
    }
    catch (Exception exception)
    {
        result = Error(exception);
    }

    async Task<string> SheetAsync()
    {
        try
        {
            var sheet = await new OrderRepository(context).CreateNextWorkshopSheetVersionAsync(order!, staleView, DateTime.UtcNow);
            return "created:v" + sheet.Version.ToString(CultureInfo.InvariantCulture);
        }
        catch (WorkshopSheetVersionConflictException)
        {
            return "conflict";
        }
    }

    async Task<string> UserAsync(string username)
    {
        context.Users.Add(new User
        {
            Username = username,
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username),
            PasswordHash = "hash",
            FirstName = "Poste",
            LastName = "Concurrent",
            Role = UserRole.Optician
        });
        await context.SaveChangesAsync();
        return "created";
    }

    // Chemin de production complet (cas d'usage + runner + dépôt) : l'issue est celle que lit l'utilisateur.
    async Task<string> DeleteSupplierAsync(long supplierId)
    {
        try
        {
            var deletion = await new DeleteSupplierUseCase(new SupplierRepository(context), new EfTransactionRunner(context))
                .ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId });
            return deletion.SupplierFound ? "deleted" : "absent";
        }
        catch (BusinessRuleException exception) when (exception.Message == DeleteSupplierUseCase.SupplierHasProductsMessage)
        {
            return "refused";
        }
    }

    Product NewProduct(long supplierId, string reference) => new()
    {
        Reference = reference,
        Name = "Produit " + reference,
        Category = ProductCategoryEnum.MONTURE,
        SupplierId = supplierId,
        PurchasePrice = 20m,
        SalePrice = 60m,
        StockQuantity = 1
    };

    async Task<string> ProductAsync(long supplierId, string reference)
    {
        context.Products.Add(NewProduct(supplierId, reference));
        await context.SaveChangesAsync();
        return "created";
    }

    // Produit inséré puis transaction TENUE ouverte au rendez-vous MMV_WORKER_BARRIER2 : l'insertion verrouille la
    // ligne fournisseur (FK) sans être visible des autres postes tant que le test ne relâche pas la barrière.
    async Task<string> HeldProductAsync(long supplierId, string reference)
    {
        await new EfTransactionRunner(context).RunAsync(async ct =>
        {
            context.Products.Add(NewProduct(supplierId, reference));
            await context.SaveChangesAsync(ct);
            await BarrierAsync("MMV_WORKER_BARRIER2");
        });
        return "created";
    }

    async Task<string> LoopAsync(long productId, int count)
    {
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            await using var each = NewContext();
            var outcome = await ComposedSaleAsync(each, productId);
            outcome = outcome.StartsWith("ok:", StringComparison.Ordinal) ? "ok" : outcome;
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }

        return string.Join(",", outcomes.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}={o.Value}"));
    }

    async Task<string> HoldAsync(long productId)
    {
        await new EfTransactionRunner(context).RunAsync(async ct =>
        {
            var saleNumber = await new EfNumberSequenceService(context).NextNumberAsync("SALE", ct);
            context.Sales.Add(new Sale { SaleNumber = saleNumber, FinalAmount = 60m });
            await context.SaveChangesAsync(ct);
            await new EfStockMutationService(context).DecrementStockAsync(productId, 1, ct);
            Console.WriteLine("HOLDING");
            await Console.Out.FlushAsync();
            await Task.Delay(Timeout.Infinite, ct); // le test tue le processus : panne de poste en pleine transaction
        });
        return "unreachable";
    }
}

Console.WriteLine("RESULT " + result);
