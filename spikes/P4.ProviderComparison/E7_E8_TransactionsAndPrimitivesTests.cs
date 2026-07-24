using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Persistence;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E7 — Transactions et isolation RÉELLEMENT observées (pas seulement documentées).
/// E8 — Primitives P3 prioritaires, exécutées via les IMPLÉMENTATIONS DE PRODUCTION
///      (<c>EfStockMutationService</c>, <c>EfNumberSequenceService</c>, <c>EfTransactionRunner</c>),
///      sur deux connexions réelles.
/// </summary>
public class E7_E8_TransactionsAndPrimitivesTests
{
    private const string Experiment = "E7-E8-transactions-primitives";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Transactions_and_isolation_are_observed(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e7");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E7 — Transactions et isolation");

        // Isolation par défaut RELEVÉE SUR LE SERVEUR, pas supposée.
        var observed = await database.ScalarAsync(provider == ProviderKind.Postgres
            ? "SHOW transaction_isolation;"
            : "SELECT CASE transaction_isolation_level WHEN 0 THEN 'Unspecified' WHEN 1 THEN 'ReadUncommitted' WHEN 2 THEN 'ReadCommitted' WHEN 3 THEN 'Repeatable Read' WHEN 4 THEN 'Serializable' WHEN 5 THEN 'Snapshot' END FROM sys.dm_exec_sessions WHERE session_id = @@SPID;");
        SpikeLog.Write(Experiment, name, $"Isolation par défaut OBSERVÉE : {observed}");

        // Rollback : l'écriture ne doit laisser aucune trace.
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.ProductCategories.Add(new ProductCategory { Name = "ROLLBACK-TEST" });
            await context.SaveChangesAsync();

            // SQL BRUT enrôlé dans la MÊME transaction (partage du DbContext de la portée).
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"ProductCategories\" (\"Name\") VALUES ('ROLLBACK-RAW')");

            await transaction.RollbackAsync();
        }

        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var remaining = await check.ProductCategories.CountAsync(c => c.Name.StartsWith("ROLLBACK"));
            SpikeLog.Write(Experiment, name,
                $"Rollback (EF + SQL brut enrôlé) : {remaining} ligne(s) subsistante(s) — attendu 0 → {(remaining == 0 ? "CONFORME" : "NON CONFORME")}");
        }

        // Savepoint / transaction imbriquée.
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.ProductCategories.Add(new ProductCategory { Name = "SP-OUTER" });
            await context.SaveChangesAsync();

            await transaction.CreateSavepointAsync("sp1");
            context.ProductCategories.Add(new ProductCategory { Name = "SP-INNER" });
            await context.SaveChangesAsync();
            await transaction.RollbackToSavepointAsync("sp1");

            await transaction.CommitAsync();

            await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var outer = await check.ProductCategories.CountAsync(c => c.Name == "SP-OUTER");
            var inner = await check.ProductCategories.CountAsync(c => c.Name == "SP-INNER");
            SpikeLog.Write(Experiment, name, $"Savepoint : SP-OUTER={outer} (attendu 1), SP-INNER={inner} (attendu 0) → {(outer == 1 && inner == 0 ? "SUPPORTÉ" : "COMPORTEMENT À REVOIR")}");
        }
        catch (Exception ex)
        {
            SpikeLog.Write(Experiment, name, $"Savepoint : NON SUPPORTÉ — {ErrorFacts.Describe(ex)}");
        }

        // Comportement APRÈS exception dans une transaction.
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO \"Notifications\" (\"Type\") VALUES (NULL)");
            }
            catch (Exception inner)
            {
                SpikeLog.Write(Experiment, name, $"Erreur attendue dans la transaction : {ErrorFacts.CandidateCategory(inner)}");
            }

            var stillUsable = await context.ProductCategories.CountAsync();
            SpikeLog.Write(Experiment, name, $"Transaction utilisable APRÈS erreur : OUI (lecture={stillUsable})");
            await transaction.RollbackAsync();
        }
        catch (Exception ex)
        {
            SpikeLog.Write(Experiment, name, $"Transaction INUTILISABLE après erreur : {ErrorFacts.Describe(ex)}");
        }

        Assert.True(true);
    }

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task P3_primitives_run_on_server(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e8");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E8 — Primitives P3 (implémentations de production)");

        long productId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var supplier = new Supplier { Name = "Fournisseur Spike" };
            seed.Suppliers.Add(supplier);
            await seed.SaveChangesAsync();

            var product = new Product
            {
                Name = "Dernier article",
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId,
                PurchasePrice = 10m,
                SalePrice = 20m,
                StockQuantity = 1,           // UN SEUL en stock : deux postes vont se le disputer.
                StockAlertThreshold = 5,
                IsActive = true,
                EntryDate = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc)
            };
            seed.Products.Add(product);

            seed.DocumentSequences.Add(new DocumentSequence
            {
                SequenceName = "SPIKE",
                Prefix = "SP",
                CurrentValue = 0,
                UpdatedAt = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc)
            });

            await seed.SaveChangesAsync();
            productId = product.ProductId;
        }

        // --- Primitive 1 : décrément du DERNIER article, deux connexions concurrentes ---
        using (var barrier = new Barrier(2))
        {
            async Task<string> Decrement()
            {
                barrier.SignalAndWait();
                try
                {
                    await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                    var service = new EfStockMutationService(context);
                    await service.DecrementStockAsync(productId, 1);
                    return "OK";
                }
                catch (Exception ex)
                {
                    return $"REFUS ({ex.GetType().Name})";
                }
            }

            var results = await Task.WhenAll(Task.Run(Decrement), Task.Run(Decrement));

            await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var stock = await check.Products.Where(p => p.ProductId == productId).Select(p => p.StockQuantity).SingleAsync();
            var wins = results.Count(r => r == "OK");
            SpikeLog.Write(Experiment, name,
                $"Décrément du dernier article (2 connexions) : succès={wins} (attendu 1), stock final={stock} (attendu 0) → {(wins == 1 && stock == 0 ? "GARANTIE TENUE" : "SURVENTE")}");
        }

        // --- Primitive 2 : numérotation documentaire concurrente ---
        using (var barrier = new Barrier(2))
        {
            async Task<string> Next()
            {
                barrier.SignalAndWait();
                try
                {
                    await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                    var service = new EfNumberSequenceService(context);
                    return await service.NextNumberAsync("SPIKE");
                }
                catch (Exception ex)
                {
                    return $"ERREUR ({ex.GetType().Name})";
                }
            }

            var numbers = await Task.WhenAll(Task.Run(Next), Task.Run(Next));
            var distinct = numbers.Distinct().Count();
            SpikeLog.Write(Experiment, name,
                $"Numérotation concurrente : [{string.Join(", ", numbers)}] — distincts={distinct}/2 → {(distinct == 2 ? "UNICITÉ TENUE" : "COLLISION")}");
        }

        // --- Primitive 3 : règlement du solde (montant + lignes affectées) ---
        long saleId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sale = new Sale
            {
                SaleNumber = "E8-SALE",
                SaleDate = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
                TotalAmount = 100m,
                FinalAmount = 100m,
                DepositAmount = 40m,
                RemainingAmount = 60m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Partial,
                Status = SaleStatus.Draft
            };
            seed.Sales.Add(sale);
            await seed.SaveChangesAsync();
            saleId = sale.SaleId;
        }

        using (var barrier = new Barrier(2))
        {
            async Task<int> Settle()
            {
                barrier.SignalAndWait();
                await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                return await context.Sales
                    .Where(s => s.SaleId == saleId && s.RemainingAmount > 0)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.RemainingAmount, 0m)
                        .SetProperty(s => s.PaymentStatus, PaymentStatus.Paid));
            }

            var rows = await Task.WhenAll(Task.Run(Settle), Task.Run(Settle));
            SpikeLog.Write(Experiment, name,
                $"Règlement du solde (2 connexions) : lignes affectées=[{string.Join(", ", rows)}] — un seul gagnant attendu → {(rows.Count(r => r == 1) == 1 ? "GARANTIE TENUE" : "DOUBLE RÈGLEMENT")}");
        }

        // --- Primitive 4 : suppression fournisseur vs création de produit ---
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var deleted = await context.Suppliers
                .Where(s => !context.Products.Any(p => p.SupplierId == s.SupplierId))
                .ExecuteDeleteAsync();
            SpikeLog.Write(Experiment, name,
                $"Suppression fournisseur SI inutilisé (NOT EXISTS traduit par le provider) : {deleted} supprimé(s) — attendu 0 (un produit référence le fournisseur) → {(deleted == 0 ? "GARANTIE TENUE" : "HISTORIQUE PERDU")}");
        }

        // --- Primitive 5 : unicité du login normalisé, deux connexions ---
        using (var barrier = new Barrier(2))
        {
            async Task<string> CreateUser()
            {
                barrier.SignalAndWait();
                try
                {
                    await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                    context.Users.Add(new User
                    {
                        Username = "Doublon",
                        NormalizedUsername = "doublon",
                        PasswordHash = new string('x', 60),
                        FirstName = "Utilisateur",
                        LastName = "Spike",
                        Role = UserRole.Optician,
                        IsActive = true
                    });
                    await context.SaveChangesAsync();
                    return "CRÉÉ";
                }
                catch (DbUpdateException ex)
                {
                    return $"REFUSÉ ({ErrorFacts.CandidateCategory(ex)})";
                }
            }

            var results = await Task.WhenAll(Task.Run(CreateUser), Task.Run(CreateUser));
            var created = results.Count(r => r == "CRÉÉ");
            SpikeLog.Write(Experiment, name,
                $"Unicité login normalisé (2 connexions) : créés={created} (attendu 1) — [{string.Join(" | ", results)}] → {(created == 1 ? "GARANTIE TENUE" : "DOUBLON")}");
        }

        Assert.True(true);
    }
}
