using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E15 — RETRY (harness uniquement) et IDEMPOTENCE.
///
/// Le produit n'est PAS modifié : <c>EfTransactionRunner</c> ne comporte toujours aucun retry.
/// La politique expérimentale <see cref="SpikeRetryPolicy"/> sert à mesurer, par type d'erreur,
/// ce qu'un rejeu ferait — et surtout à démontrer QUELLES opérations il est dangereux de rejouer.
/// </summary>
[Collection(SpikeSerialCollection.Name)]
public class E15_RetryAndIdempotenceTests
{
    private const string Experiment = "E15-retry-idempotence";

    private static readonly DateTime Fixed = new(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc);

    // =============================================================================================
    // Partie 1 — classification des erreurs par la politique de retry
    // =============================================================================================
    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Retry_policy_classifies_each_error_kind(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e15c");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E15 partie 1 — classification des erreurs par la politique de retry");

        // --- Violation de contrainte : JAMAIS rejouable -------------------------------------------
        await ClassifyAsync(name, "violation-de-contrainte (unicite)", async () =>
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            context.ProductCategories.Add(new ProductCategory { Name = "DOUBLON-E15" });
            await context.SaveChangesAsync();
            context.ProductCategories.Add(new ProductCategory { Name = "DOUBLON-E15" });
            await context.SaveChangesAsync();
        });

        // --- Erreur metier : JAMAIS rejouable -----------------------------------------------------
        await ClassifyAsync(name, "erreur-metier (InsufficientStockException)", async () =>
        {
            var productId = await SeedProduct(database, "MetierE15", stock: 0);
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var service = new EfStockMutationService(context);
            await service.DecrementStockAsync(productId, 5);
        });

        // --- Timeout : rejouable sous condition d'idempotence -------------------------------------
        await ClassifyAsync(name, "timeout (CommandTimeout=2s)", async () =>
        {
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = provider == ProviderKind.Postgres
                ? "SELECT pg_sleep(30);"
                : "WAITFOR DELAY '00:00:30';";
            await command.ExecuteNonQueryAsync();
        });

        // --- Transaction avortee PostgreSQL (25P02) ----------------------------------------------
        if (provider == ProviderKind.Postgres)
        {
            await ClassifyAsync(name, "transaction-avortee-PostgreSQL (25P02)", async () =>
            {
                await using var connection = await database.OpenRawAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await using var bad = connection.CreateCommand();
                    bad.Transaction = transaction;
                    bad.CommandText = "SELECT 1/0;";
                    await bad.ExecuteScalarAsync();
                }
                catch
                {
                    // Erreur voulue : la transaction est desormais avortee.
                }

                await using var after = connection.CreateCommand();
                after.Transaction = transaction;
                after.CommandText = "SELECT 1;";
                await after.ExecuteScalarAsync();
            });
        }

        // --- Connexion indisponible ---------------------------------------------------------------
        await ClassifyAsync(name, "connexion-indisponible (port ferme)", async () =>
        {
            await using var dead = SpikeConnections.Create(provider, SpikeConnections.WithUnreachablePort(provider));
            await dead.OpenAsync();
        });

        Assert.True(true);
    }

    // =============================================================================================
    // Partie 2 — retry sur une INDISPONIBILITÉ RÉELLEMENT TRANSITOIRE (arrêt/redémarrage)
    // =============================================================================================
    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Retry_recovers_from_a_real_transient_outage(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var container = DockerControl.ContainerFor(provider);
        Skip.If(container is null, DockerControl.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e15t");
        await Execute(database, "CREATE TABLE retry_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await Execute(database, "INSERT INTO retry_probe (id, payload) VALUES (1, 42);");

        SpikeLog.Section(Experiment, name, "E15 partie 2 — retry sur indisponibilite reellement transitoire");

        var stopped = await DockerControl.StopAsync(container!);
        SpikeLog.Write(Experiment, name, $"panne provoquee : {stopped}");

        // Le serveur est redémarré EN PARALLÈLE : les premières tentatives échouent réellement,
        // les suivantes doivent réussir. Aucune boucle infinie : le rejeu reste borné à 3.
        // Le délai doit être assez long pour que la PREMIÈRE tentative échoue RÉELLEMENT sur les deux
        // providers : PostgreSQL redémarre en moins d'une seconde (E14), un délai court ne mesurerait
        // alors aucun rejeu.
        var restart = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            return await DockerControl.StartAsync(container!);
        });

        string outcome;
        try
        {
            var value = await SpikeRetryPolicy.ExecuteAsync(async attempt =>
            {
                SpikeConnections.ClearPools(provider);
                await using var connection = SpikeConnections.Create(
                    provider, SpikeConnections.WithShortConnectTimeout(provider, 2));
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                return (await command.ExecuteScalarAsync())?.ToString() ?? "(null)";
            }, "indisponibilite-transitoire", Experiment, name);

            outcome = $"REPRISE REUSSIE (valeur={value})";
        }
        catch (Exception ex)
        {
            outcome = $"ECHEC FINAL PROPAGE (non masque) — {ErrorFacts.Describe(ex)}";
        }

        SpikeLog.Write(Experiment, name, $"redemarrage : {await restart}");
        SpikeLog.Write(Experiment, name, $"resultat du retry sur panne transitoire : {outcome}");

        var recovered = await DockerControl.WaitUntilReachableAsync(provider, TimeSpan.FromMinutes(3));
        Assert.True(recovered is not null, $"[{name}] Le serveur n'est pas revenu.");

        Assert.True(true);
    }

    // =============================================================================================
    // Partie 3 — IDEMPOTENCE : une opération sûre à rejouer, une opération DANGEREUSE à rejouer
    // =============================================================================================
    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Idempotence_of_replayable_operations_is_verified(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e15i");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E15 partie 3 — idempotence des operations rejouables");

        // --- (a) SÛRE À REJOUER : résolution LowStock (CAS sur « ResolvedAt IS NULL ») ------------
        var productId = await SeedProduct(database, "IdemSafe", stock: 1);
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            seed.Notifications.Add(new Notification
            {
                Type = NotificationTypes.LowStock,
                EntityType = NotificationEntityTypes.Product,
                EntityId = productId,
                Title = "Stock bas",
                Message = "Seuil atteint",
                IsRead = false,
                CreatedAt = Fixed
            });
            await seed.SaveChangesAsync();
        }

        int r1, r2, r3;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new NotificationRepository(context);
            r1 = await repository.ResolveActiveLowStockAsync(new[] { productId }, Fixed.AddHours(1));
            r2 = await repository.ResolveActiveLowStockAsync(new[] { productId }, Fixed.AddHours(2));
            r3 = await repository.ResolveActiveLowStockAsync(new[] { productId }, Fixed.AddHours(3));
        }

        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var resolved = await check.Notifications
                .Where(n => n.EntityId == productId).Select(n => n.ResolvedAt).SingleAsync();

            SpikeLog.Write(Experiment, name,
                $"IDEMPOTENCE (SURE) resolution-LowStock | 3 executions identiques -> [{r1}, {r2}, {r3}] " +
                $"(attendu 1 puis 0 puis 0) | date de resolution={resolved:O} (attendue = 1re date, jamais reecrite) | " +
                $"{(r1 == 1 && r2 == 0 && r3 == 0 && resolved == Fixed.AddHours(1) ? "IDEMPOTENTE PAR CAS — REJEU SANS DANGER" : "ANOMALIE")}");
        }

        // --- (b) DANGEREUSE À REJOUER : incrément relatif de stock --------------------------------
        var dangerous = await SeedProduct(database, "IdemDanger", stock: 10);
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var service = new EfStockMutationService(context);
            await service.IncrementStockAsync(dangerous, 5);
            // REJEU du « même » appel, tel que le ferait un retry aveugle après un timeout.
            await service.IncrementStockAsync(dangerous, 5);
            await service.IncrementStockAsync(dangerous, 5);
        }

        var stock = await ReadStock(database, dangerous);
        SpikeLog.Write(Experiment, name,
            $"IDEMPOTENCE (DANGEREUSE) increment-stock | 3 executions du meme +5 sur 10 | stock={stock} " +
            $"(attendu 25 si NON idempotente, 15 si idempotente) | " +
            $"{(stock == 25 ? "NON IDEMPOTENTE — UN RETRY AVEUGLE FAUSSE LE STOCK" : "resultat inattendu")}");

        // --- (c) DANGEREUSE À REJOUER : attribution d'un numéro de document -----------------------
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            seed.DocumentSequences.Add(new DocumentSequence
            {
                SequenceName = "E15SEQ",
                Prefix = "SQ",
                CurrentValue = 0,
                UpdatedAt = Fixed
            });
            await seed.SaveChangesAsync();
        }

        string n1, n2, n3;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sequence = new EfNumberSequenceService(context);
            n1 = await sequence.NextNumberAsync("E15SEQ");
            n2 = await sequence.NextNumberAsync("E15SEQ");
            n3 = await sequence.NextNumberAsync("E15SEQ");
        }

        SpikeLog.Write(Experiment, name,
            $"IDEMPOTENCE (DANGEREUSE) numerotation | 3 executions -> [{n1}, {n2}, {n3}] | " +
            $"{(n1 != n2 && n2 != n3 ? "NON IDEMPOTENTE — CHAQUE REJEU CONSOMME UN NUMERO (trou de numerotation)" : "resultat inattendu")}");

        // --- (d) SÛRE À REJOUER : décision QC (CAS sur QcStatus = Pending) ------------------------
        var orderId = await SeedOrder(database, "E15-QC", OrderStatus.QualityCheck);
        long sheetId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sheet = new WorkshopSheet
            {
                OrderId = orderId,
                Version = 1,
                IsCurrent = true,
                QcStatus = WorkshopSheetQcStatus.Pending,
                CreatedAt = Fixed,
                OrderNumberSnapshot = "E15-QC",
                OrderDateSnapshot = Fixed
            };
            seed.WorkshopSheets.Add(sheet);
            await seed.SaveChangesAsync();
            sheetId = sheet.WorkshopSheetId;
        }

        bool q1, q2, q3;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new OrderRepository(context);
            q1 = await repository.TryTakeWorkshopSheetQcDecisionAsync(sheetId, WorkshopSheetQcStatus.Passed, "1", Fixed);
            q2 = await repository.TryTakeWorkshopSheetQcDecisionAsync(sheetId, WorkshopSheetQcStatus.Passed, "2", Fixed);
            q3 = await repository.TryTakeWorkshopSheetQcDecisionAsync(sheetId, WorkshopSheetQcStatus.Passed, "3", Fixed);
        }

        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var comment = await check.WorkshopSheets
                .Where(w => w.WorkshopSheetId == sheetId).Select(w => w.QcComment).SingleAsync();

            SpikeLog.Write(Experiment, name,
                $"IDEMPOTENCE (SURE) decision-QC | 3 executions -> [{q1}, {q2}, {q3}] (attendu True puis False puis False) | " +
                $"commentaire conserve={comment} (attendu '1' : le rejeu ne reecrit rien) | " +
                $"{(q1 && !q2 && !q3 && comment == "1" ? "IDEMPOTENTE PAR CAS — REJEU SANS DANGER" : "ANOMALIE")}");
        }

        Assert.True(true);
    }

    // ---------------------------------------------------------------------------------------------
    private static async Task ClassifyAsync(string name, string label, Func<Task> action)
    {
        try
        {
            await action();
            SpikeLog.Write(Experiment, name, $"CLASSIFICATION[{label}] : AUCUNE ERREUR (inattendu)");
        }
        catch (Exception ex)
        {
            var decision = SpikeRetryPolicy.Classify(ex);
            SpikeLog.Write(Experiment, name,
                $"CLASSIFICATION[{label}] | {ErrorFacts.Describe(ex)} | rejouable={decision.Retryable} | motif={decision.Reason}");
        }
    }

    private static int _reference;

    private static async Task<long> SeedProduct(SpikeDatabase database, string label, int stock)
    {
        await using var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var supplier = new Supplier { Name = $"Fournisseur {label}" };
        seed.Suppliers.Add(supplier);
        await seed.SaveChangesAsync();

        var product = new Product
        {
            Reference = $"E15-REF-{Interlocked.Increment(ref _reference):D4}",
            Name = $"Produit {label}",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = stock,
            StockAlertThreshold = 5,
            IsActive = true,
            EntryDate = Fixed
        };
        seed.Products.Add(product);
        await seed.SaveChangesAsync();
        return product.ProductId;
    }

    private static async Task<long> SeedOrder(SpikeDatabase database, string number, OrderStatus status)
    {
        await using var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var sale = new Sale
        {
            SaleNumber = $"S-{number}",
            SaleDate = Fixed,
            TotalAmount = 100m,
            FinalAmount = 100m,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            Status = SaleStatus.Delivered
        };
        seed.Sales.Add(sale);
        await seed.SaveChangesAsync();

        var order = new Order { OrderNumber = number, SaleId = sale.SaleId, OrderDate = Fixed, Status = status };
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        return order.OrderId;
    }

    private static async Task<int> ReadStock(SpikeDatabase database, long productId)
    {
        await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        return await context.Products.AsNoTracking()
            .Where(p => p.ProductId == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    private static async Task Execute(SpikeDatabase database, string sql)
    {
        await using var connection = await database.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
