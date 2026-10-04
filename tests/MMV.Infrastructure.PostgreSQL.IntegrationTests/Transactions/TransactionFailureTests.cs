using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.UpdateUser;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Transactions;

/// <summary>
/// N7 — erreur en transaction, <c>25P02</c>, rollback, commande suivante (critère 6), et passage AU RUNTIME
/// des 12 <c>catch</c> intra-transactionnels recensés par P4-5A §6 (O6, jusqu'ici prouvée statiquement).
/// Sur PostgreSQL, toute erreur avorte la transaction entière : un <c>catch</c> qui continuerait d'écrire
/// échouerait en cascade. Chaque test vérifie donc aussi que la connexion SERT la commande suivante.
/// </summary>
public class TransactionFailureTests : PostgreSqlTestBase
{
    // ---------- comportement PostgreSQL brut ----------

    [PostgreSqlFact]
    public async Task N7_after_an_error_every_statement_fails_with_25P02_until_rollback()
    {
        await AddUserAsync("ADMIN");

        await using var context = NewContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var duplicate = () => InsertUserSqlAsync(context, "ADMIN");
            (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

            var next = () => context.Users.CountAsync();
            var aborted = (await next.Should().ThrowAsync<PostgresException>()).Which;
            aborted.SqlState.Should().Be(PostgresErrorCodes.InFailedSqlTransaction);
            PersistenceErrorMapper.Map(aborted).Should().BeOfType<PersistenceException>()
                .Which.Category.Should().Be(PersistenceErrorCategory.Unknown);

            await transaction.RollbackAsync();
        }

        (await context.Users.CountAsync()).Should().Be(1, "après rollback, la même connexion sert la commande suivante");
    }

    // ---------- EfTransactionRunner (2 catch) ----------

    [PostgreSqlFact]
    public async Task N7_runner_rolls_back_maps_the_error_and_the_context_serves_the_next_command()
    {
        await AddUserAsync("ADMIN");

        await using var context = NewContext();
        var runner = new EfTransactionRunner(context);

        // L'opération AVALE l'erreur et continue d'écrire : exactement le risque O6 sur PostgreSQL.
        var act = () => runner.RunAsync(async ct =>
        {
            await context.Database.ExecuteSqlRawAsync("""INSERT INTO "Suppliers" ("Name") VALUES ('écrit avant l''erreur')""", ct);
            try { await InsertUserSqlAsync(context, "ADMIN"); } catch (PostgresException) { /* avalée */ }
            await context.Database.ExecuteSqlRawAsync("""INSERT INTO "Suppliers" ("Name") VALUES ('écrit après')""", ct);
        });

        var thrown = (await act.Should().ThrowAsync<PersistenceException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InFailedSqlTransaction);

        // Le rollback d'une transaction avortée réussit : le second catch (rollback « best effort ») n'est pas requis.
        (await context.Suppliers.CountAsync()).Should().Be(0, "rien de la transaction avortée n'est validé");
        await runner.RunAsync(ct => context.Database.ExecuteSqlRawAsync("""INSERT INTO "Suppliers" ("Name") VALUES ('suivant')""", ct));
        (await context.Suppliers.CountAsync()).Should().Be(1);
    }

    // ---------- UnitOfWork.CommitAsync (1 catch) ----------

    [PostgreSqlFact]
    public async Task N7_unit_of_work_commit_failure_rolls_back_every_write_of_the_transaction()
    {
        await AddUserAsync("ADMIN");

        await using var context = NewContext();
        await using var unitOfWork = new UnitOfWork(context);
        await unitOfWork.BeginTransactionAsync();
        context.Suppliers.Add(new Supplier { Name = "écrit dans la transaction" });
        await unitOfWork.SaveChangesAsync();
        context.Users.Add(NewUser("ADMIN"));

        var act = () => unitOfWork.CommitAsync();

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
        await using var verify = NewContext();
        (await verify.Suppliers.CountAsync()).Should().Be(0);
        (await context.Suppliers.AsNoTracking().CountAsync()).Should().Be(0, "la connexion du UnitOfWork reste utilisable");
    }

    // ---------- OrderRepository.CreateNextWorkshopSheetVersionAsync (1 catch) ----------

    [PostgreSqlFact]
    public async Task N7_concurrent_first_sheet_is_refused_as_a_business_conflict_inside_a_transaction()
    {
        long orderId;
        await using (var context = NewContext())
        {
            orderId = await Arrange.OrderAsync(context, OrderStatus.InProgress);
            var orders = new OrderRepository(context);
            await orders.CreateNextWorkshopSheetVersionAsync((await orders.GetWithItemsAsync(orderId))!, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
        }

        await using (var context = NewContext())
        {
            // Second poste n'ayant vu AUCUNE version : pas de compare-and-swap possible, la base arbitre.
            var orders = new OrderRepository(context);
            var order = (await orders.GetWithItemsAsync(orderId))!;
            var act = () => new EfTransactionRunner(context).RunAsync(ct =>
                orders.CreateNextWorkshopSheetVersionAsync(order, WorkshopSheetVersionPrecondition.None, DateTime.UtcNow, ct));

            await act.Should().ThrowAsync<WorkshopSheetVersionConflictException>();
            (await context.WorkshopSheets.AsNoTracking().CountAsync()).Should().Be(1);
        }
    }

    // ---------- CreateProductUseCase (2 catch) ----------

    [PostgreSqlFact]
    public async Task N7_create_product_losing_a_reference_race_returns_the_duplicate_result()
    {
        var supplierId = await SupplierAsync("Fournisseur");

        await using var context = NewContext();
        var products = InterleavedCall<IProductRepository>.Before(new ProductRepository(context), nameof(IProductRepository.CreateAsync),
            () => OtherWorkstationAsync(c => Arrange.ProductAsync(c, supplierId, reference: "REF-1")));

        var result = await ComposeCreateProduct(context, products).ExecuteAsync(ProductCommand("ref-1", supplierId));

        result.ValidationErrors.Should().ContainSingle().Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);
        (await CountAsync<Product>()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task N7_create_product_whose_supplier_vanishes_rechecks_after_rollback_and_reports_it()
    {
        var supplierId = await SupplierAsync("Éphémère");

        await using var context = NewContext();
        var products = InterleavedCall<IProductRepository>.Before(new ProductRepository(context), nameof(IProductRepository.CreateAsync),
            () => OtherWorkstationAsync(c => c.Suppliers.Where(s => s.SupplierId == supplierId).ExecuteDeleteAsync()));

        // Le catch ConstraintViolation ROUVRE une requête (ExistsFreshAsync) après l'annulation : sur PostgreSQL,
        // elle n'aboutit que parce que la transaction avortée a bien été close par le runner.
        var result = await ComposeCreateProduct(context, products).ExecuteAsync(ProductCommand("REF-1", supplierId));

        result.ValidationErrors.Should().ContainSingle().Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);
        (await CountAsync<Product>()).Should().Be(0);
    }

    // ---------- UpdateProductUseCase (2 catch) ----------

    [PostgreSqlFact]
    public async Task N7_update_product_losing_a_reference_race_returns_the_duplicate_result()
    {
        var supplierId = await SupplierAsync("Fournisseur");
        var productId = await OtherWorkstationAsync(c => Arrange.ProductAsync(c, supplierId, reference: "REF-1"));

        await using var context = NewContext();
        var products = InterleavedCall<IProductRepository>.Before(new ProductRepository(context), nameof(IProductRepository.UpdateCatalogAsync),
            () => OtherWorkstationAsync(c => Arrange.ProductAsync(c, supplierId, reference: "REF-2")));

        var result = await ComposeUpdateProduct(context, products).ExecuteAsync(UpdateCommand(productId, "REF-2", supplierId));

        result.ValidationErrors.Should().ContainSingle().Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);
        await using var verify = NewContext();
        (await verify.Products.SingleAsync(p => p.ProductId == productId)).Reference.Should().Be("REF-1");
    }

    [PostgreSqlFact]
    public async Task N7_update_product_whose_new_supplier_vanishes_rechecks_after_rollback_and_reports_it()
    {
        var supplierId = await SupplierAsync("Fournisseur");
        var vanishingId = await SupplierAsync("Éphémère");
        var productId = await OtherWorkstationAsync(c => Arrange.ProductAsync(c, supplierId, reference: "REF-1"));

        await using var context = NewContext();
        var products = InterleavedCall<IProductRepository>.Before(new ProductRepository(context), nameof(IProductRepository.UpdateCatalogAsync),
            () => OtherWorkstationAsync(c => c.Suppliers.Where(s => s.SupplierId == vanishingId).ExecuteDeleteAsync()));

        var result = await ComposeUpdateProduct(context, products).ExecuteAsync(UpdateCommand(productId, "REF-1", vanishingId));

        result.ValidationErrors.Should().ContainSingle().Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);
        await using var verify = NewContext();
        (await verify.Products.SingleAsync()).SupplierId.Should().Be(supplierId);
    }

    // ---------- DeleteSupplierUseCase (1 catch) ----------

    /// <summary>
    /// Vraie course à deux connexions : un autre poste insère un produit référençant le fournisseur SANS valider ;
    /// la suppression conditionnelle ne le voit pas et attend son verrou. Une fois l'insertion validée, la FK
    /// arbitre et le use case doit renvoyer la règle métier, jamais l'erreur du provider.
    /// </summary>
    [PostgreSqlFact]
    public async Task N7_delete_supplier_racing_an_uncommitted_product_reports_the_business_rule()
    {
        var supplierId = await SupplierAsync("Convoité");

        await using var other = NewContext();
        await using var otherTransaction = await other.Database.BeginTransactionAsync();
        await Arrange.ProductAsync(other, supplierId);

        await using var context = NewContext();
        // Sonde : seul le chemin de DIAGNOSTIC (DELETE à 0 ligne) appelle ExistsFreshAsync ; le catch FK, non.
        var diagnosticPathTaken = false;
        var suppliers = InterleavedCall<ISupplierRepository>.Before(new SupplierRepository(context),
            nameof(ISupplierRepository.ExistsFreshAsync), () => { diagnosticPathTaken = true; return Task.CompletedTask; });
        var deletion = Task.Run(() => new DeleteSupplierUseCase(suppliers, new EfTransactionRunner(context))
            .ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId }));

        await WaitUntilABackendWaitsOnALockAsync();
        await otherTransaction.CommitAsync();

        var act = () => deletion;
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().NotContainAny("23503", "Npgsql", "violates");
        diagnosticPathTaken.Should().BeFalse("la FK a arbitré : c'est le catch ConstraintViolation qui a répondu");
        (await CountAsync<Supplier>()).Should().Be(1);
    }

    // ---------- CreateUserUseCase · UpdateUserUseCase (1 catch chacun) ----------

    [PostgreSqlFact]
    public async Task N7_create_user_losing_a_login_race_returns_taken_and_the_scope_stays_usable()
    {
        await using var context = NewContext();
        var unitOfWork = new UnitOfWork(context);
        var users = InterleavedCall<IUserRepository>.Before(new UserRepository(context), nameof(IUserRepository.CreateAsync),
            () => AddUserAsync("MARIE"));

        var result = await new CreateUserUseCase(users, unitOfWork, new AuthenticationService(unitOfWork), new EfTransactionRunner(context))
            .ExecuteAsync(new CreateUserCommand { Username = "marie", FirstName = "Marie", LastName = "Curie", Role = UserRole.Admin, IsActive = true, Password = "Radium-1898!" });

        result.UsernameTaken.Should().BeTrue();
        context.Suppliers.Add(new Supplier { Name = "écriture sans rapport" });
        await context.SaveChangesAsync();
        (await CountAsync<User>()).Should().Be(1);
        (await CountAsync<Supplier>()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task N7_update_user_losing_a_login_race_returns_taken()
    {
        var userId = await AddUserAsync("PIERRE");

        await using var context = NewContext();
        var unitOfWork = new UnitOfWork(context);
        var users = InterleavedCall<IUserRepository>.Before(new UserRepository(context), nameof(IUserRepository.UpdateAsync),
            () => AddUserAsync("IRENE"));

        var result = await new UpdateUserUseCase(users, unitOfWork, new AuthenticationService(unitOfWork), new EfTransactionRunner(context))
            .ExecuteAsync(new UpdateUserCommand { UserId = userId, Username = "irene", FirstName = "Pierre", LastName = "Curie", Role = UserRole.Admin, IsActive = true });

        result.UsernameTaken.Should().BeTrue();
        await using var verify = NewContext();
        (await verify.Users.SingleAsync(u => u.UserId == userId)).NormalizedUsername.Should().Be("pierre");
    }

    // ---------- composition et arrangement ----------

    private static CreateProductUseCase ComposeCreateProduct(OpticDbContext context, IProductRepository products) =>
        new(products, new SupplierRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));

    private static UpdateProductUseCase ComposeUpdateProduct(OpticDbContext context, IProductRepository products) =>
        new(products, new SupplierRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));

    private static CreateProductCommand ProductCommand(string reference, long supplierId) => new()
    {
        Reference = reference, Name = "Monture", PurchasePrice = 20m, SalePrice = 60m, StockQuantity = 1,
        StockAlertThreshold = 1, Category = ProductCategoryEnum.MONTURE, SupplierId = supplierId
    };

    private static UpdateProductCommand UpdateCommand(long productId, string reference, long supplierId) => new()
    {
        ProductId = productId, Reference = reference, Name = "Monture", PurchasePrice = 20m, SalePrice = 60m,
        StockQuantity = 10, StockAlertThreshold = 1, Category = ProductCategoryEnum.MONTURE, SupplierId = supplierId
    };

    private async Task<T> OtherWorkstationAsync<T>(Func<OpticDbContext, Task<T>> write)
    {
        await using var context = NewContext();
        return await write(context);
    }

    private Task<long> SupplierAsync(string name) => OtherWorkstationAsync(c => Arrange.SupplierAsync(c, name));

    private Task<long> AddUserAsync(string username) => OtherWorkstationAsync(async c =>
    {
        var user = NewUser(username);
        c.Users.Add(user);
        await c.SaveChangesAsync();
        return user.UserId;
    });

    /// <summary>Login normalisé par la règle de PRODUCTION (<see cref="UserIdentityPolicy"/>).</summary>
    private static User NewUser(string username) => new()
    {
        Username = username, NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username), PasswordHash = "h",
        FirstName = "Test", LastName = "Utilisateur", Role = UserRole.Admin
    };

    private static Task<int> InsertUserSqlAsync(OpticDbContext context, string username) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Username", "NormalizedUsername", "PasswordHash", "FirstName", "LastName", "Role", "IsActive", "CreatedAt")
            VALUES ({username}, {UserIdentityPolicy.NormalizeUsername(username)}, 'h', 'T', 'U', 'Admin', true, now())
            """);

    private async Task<int> CountAsync<T>() where T : class
    {
        await using var context = NewContext();
        return await context.Set<T>().CountAsync();
    }

    private async Task WaitUntilABackendWaitsOnALockAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waiting = await SchemaCatalog.QueryAsync(Database,
                "SELECT pid::text FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'");
            if (waiting.Count > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("La suppression concurrente n'a jamais attendu le verrou de l'autre poste.");
    }
}
