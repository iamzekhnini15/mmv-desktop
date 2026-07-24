using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E11 — Primitives P3 RESTANTES, exécutées via les IMPLÉMENTATIONS DE PRODUCTION réelles
/// (<c>EfStockMutationService</c>, <c>OrderRepository</c>, <c>SaleRepository</c>,
/// <c>SupplierRepository</c>, <c>NotificationRepository</c>, <c>EfTransactionRunner</c>).
///
/// Aucune logique de production n'est recopiée ici : chaque primitive est APPELÉE. Lorsqu'une
/// primitive ne peut PAS être appelée (SQL explicitement SQLite), l'échec est MESURÉ et consigné
/// tel quel — jamais contourné par une réécriture qui produirait un faux succès.
/// </summary>
public class E11_RemainingPrimitivesTests
{
    private const string Experiment = "E11-remaining-primitives";

    private static readonly DateTime Fixed = new(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc);

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Remaining_P3_primitives_run_on_server(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e11");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E11 — Primitives P3 restantes (implementations de production)");

        await IncrementStock(database, name);
        await AdjustStockCas(database, name);
        await FullSaleTransaction(database, name);
        await OrderTransition(database, name);
        await OrderTransitionWithSheet(database, name);
        await NextWorkshopSheetVersion(database, name);
        await QcDecision(database, name);
        await SettleBalanceProduction(database, name);
        await SupplierDeleteProduction(database, name);
        await LowStockCreateProduction(database, name);
        await LowStockResolveProduction(database, name);

        Assert.True(true);
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : increment relatif de stock (EfStockMutationService.IncrementStockAsync)
    // Deux increments concurrents doivent S'ADDITIONNER (update relatif, jamais read-modify-write).
    // ---------------------------------------------------------------------------------------------
    private static async Task IncrementStock(SpikeDatabase database, string name)
    {
        var productId = await SeedProduct(database, "Increment", stock: 10);

        using var barrier = new Barrier(2);

        async Task<string> Increment()
        {
            barrier.SignalAndWait();
            try
            {
                await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                var service = new EfStockMutationService(context);
                var after = await service.IncrementStockAsync(productId, 5);
                return $"OK(retour={after})";
            }
            catch (Exception ex)
            {
                return $"ERREUR({ErrorFacts.CandidateCategory(ex)})";
            }
        }

        var results = await Task.WhenAll(Task.Run(Increment), Task.Run(Increment));
        var stock = await ReadStock(database, productId);

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE increment-stock | 2 connexions, 10 +5 +5 | [{string.Join(" | ", results)}] | " +
            $"stock final={stock} (attendu 20) | {(stock == 20 ? "GARANTIE TENUE (aucun increment perdu)" : "INCREMENT PERDU")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : ajustement de stock par CAS (EfStockMutationService.AdjustStockToAsync)
    // Deux ajustements concurrents : exactement UN doit reussir, l'autre lever un conflit explicite
    // (jamais de last-write-wins).
    // ---------------------------------------------------------------------------------------------
    private static async Task AdjustStockCas(SpikeDatabase database, string name)
    {
        var productId = await SeedProduct(database, "Ajustement", stock: 7);

        using var barrier = new Barrier(2);

        async Task<string> Adjust(int target)
        {
            barrier.SignalAndWait();
            try
            {
                await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                var service = new EfStockMutationService(context);
                var result = await service.AdjustStockToAsync(productId, target);
                return $"OK({result.PreviousQuantity}->{result.NewQuantity})";
            }
            catch (StockConcurrencyConflictException)
            {
                return "CONFLIT(StockConcurrencyConflictException)";
            }
            catch (Exception ex)
            {
                return $"ERREUR({ErrorFacts.CandidateCategory(ex)})";
            }
        }

        var results = await Task.WhenAll(Task.Run(() => Adjust(100)), Task.Run(() => Adjust(200)));
        var stock = await ReadStock(database, productId);
        var wins = results.Count(r => r.StartsWith("OK", StringComparison.Ordinal));
        var conflicts = results.Count(r => r.StartsWith("CONFLIT", StringComparison.Ordinal));

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE ajustement-CAS | 2 connexions, cible 100 vs 200 | [{string.Join(" | ", results)}] | " +
            $"succes={wins} conflits={conflicts} stock final={stock} | " +
            $"{(wins == 1 && conflicts == 1 && (stock == 100 || stock == 200) ? "GARANTIE TENUE (CAS arbitre)" : "LAST-WRITE-WINS / ANOMALIE")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : transaction complete d'enregistrement de vente (EfTransactionRunner reel).
    // Verifie l'ATOMICITE composee : numero + vente + decrement de stock. Un echec de stock au
    // milieu doit tout annuler, y compris le numero deja consomme dans la meme transaction.
    // ---------------------------------------------------------------------------------------------
    private static async Task FullSaleTransaction(SpikeDatabase database, string name)
    {
        var productId = await SeedProduct(database, "VenteTx", stock: 3);

        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            seed.DocumentSequences.Add(new DocumentSequence
            {
                SequenceName = "E11VENTE",
                Prefix = "VT",
                CurrentValue = 0,
                UpdatedAt = Fixed
            });
            await seed.SaveChangesAsync();
        }

        // --- Cas nominal : la transaction complete doit COMMITER integralement ---
        string? number;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var runner = new EfTransactionRunner(context);
            var sequence = new EfNumberSequenceService(context);
            var stock = new EfStockMutationService(context);

            number = await runner.RunAsync(async ct =>
            {
                var assigned = await sequence.NextNumberAsync("E11VENTE", ct);
                var sale = new Sale
                {
                    SaleNumber = assigned,
                    SaleDate = Fixed,
                    TotalAmount = 50m,
                    FinalAmount = 50m,
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Paid,
                    Status = SaleStatus.Delivered
                };
                context.Sales.Add(sale);
                await context.SaveChangesAsync(ct);
                await stock.DecrementStockAsync(productId, 2, ct);
                return assigned;
            });
        }

        var afterOk = await ReadStock(database, productId);
        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE transaction-vente-complete | numero+vente+stock | numero={number}, stock 3-2={afterOk} | " +
            $"{(number == "VT-000001" && afterOk == 1 ? "COMMIT INTEGRAL" : "ANOMALIE")}");

        // --- Cas d'echec : stock insuffisant en fin de transaction -> TOUT doit etre annule ---
        var rolledBack = "non atteint";
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var runner = new EfTransactionRunner(context);
            var sequence = new EfNumberSequenceService(context);
            var stock = new EfStockMutationService(context);

            try
            {
                await runner.RunAsync(async ct =>
                {
                    var assigned = await sequence.NextNumberAsync("E11VENTE", ct);
                    context.Sales.Add(new Sale
                    {
                        SaleNumber = assigned,
                        SaleDate = Fixed,
                        TotalAmount = 999m,
                        FinalAmount = 999m,
                        PaymentMethod = PaymentMethod.Cash,
                        PaymentStatus = PaymentStatus.Paid,
                        Status = SaleStatus.Delivered
                    });
                    await context.SaveChangesAsync(ct);
                    // Stock restant = 1 : decrementer 99 DOIT echouer et annuler toute la transaction.
                    await stock.DecrementStockAsync(productId, 99, ct);
                    return assigned;
                });
                rolledBack = "AUCUNE ERREUR (inattendu)";
            }
            catch (InsufficientStockException)
            {
                rolledBack = "InsufficientStockException";
            }
            catch (Exception ex)
            {
                rolledBack = ErrorFacts.CandidateCategory(ex);
            }
        }

        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sales = await check.Sales.CountAsync();
            var counter = await check.DocumentSequences
                .Where(s => s.SequenceName == "E11VENTE").Select(s => s.CurrentValue).SingleAsync();
            var stockNow = await ReadStock(database, productId);

            SpikeLog.Write(Experiment, name,
                $"PRIMITIVE transaction-vente-ANNULEE | erreur={rolledBack}, ventes={sales} (attendu 1), " +
                $"compteur={counter} (attendu 1 : le numero consomme est annule), stock={stockNow} (attendu 1) | " +
                $"{(sales == 1 && counter == 1 && stockNow == 1 ? "ROLLBACK COMPLET" : "FUITE TRANSACTIONNELLE")}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : transition simple de commande (OrderRepository.TryTransitionStatusAsync)
    // ---------------------------------------------------------------------------------------------
    private static async Task OrderTransition(SpikeDatabase database, string name)
    {
        var orderId = await SeedOrder(database, "E11-TR", OrderStatus.New);

        using var barrier = new Barrier(2);

        async Task<bool> Transition()
        {
            barrier.SignalAndWait();
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var repository = new OrderRepository(context);
            return await repository.TryTransitionStatusAsync(orderId, OrderStatus.New, OrderStatus.ToFabricate);
        }

        var results = await Task.WhenAll(Task.Run(Transition), Task.Run(Transition));
        var taken = results.Count(r => r);

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var status = await check.Orders.Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE transition-commande | 2 connexions, New->ToFabricate | prises={taken} (attendu 1), " +
            $"statut final={status} | {(taken == 1 && status == OrderStatus.ToFabricate ? "GARANTIE TENUE" : "DOUBLE AVANCEMENT")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : transition liee a la fiche atelier autoritaire
    // (OrderRepository.TryTransitionWithWorkshopSheetAsync) — EXISTS/NOT EXISTS correle.
    // ---------------------------------------------------------------------------------------------
    private static async Task OrderTransitionWithSheet(SpikeDatabase database, string name)
    {
        var orderId = await SeedOrder(database, "E11-TRWS", OrderStatus.QualityCheck);

        long sheetId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sheet = new WorkshopSheet
            {
                OrderId = orderId,
                Version = 1,
                IsCurrent = true,
                QcStatus = WorkshopSheetQcStatus.Passed,
                CreatedAt = Fixed,
                OrderNumberSnapshot = "E11-TRWS",
                OrderDateSnapshot = Fixed,
                TechnicalFingerprint = "EMPREINTE-A"
            };
            seed.WorkshopSheets.Add(sheet);
            await seed.SaveChangesAsync();
            sheetId = sheet.WorkshopSheetId;
        }

        // 1) Empreinte PERIMEE : la condition correlee doit refuser la transition.
        string stale;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new OrderRepository(context);
            var outcome = await repository.TryTransitionWithWorkshopSheetAsync(
                orderId, OrderStatus.QualityCheck, OrderStatus.Ready, sheetId, "EMPREINTE-PERIMEE");
            stale = outcome.ToString();
        }

        // 2) Empreinte correcte : la transition doit etre prise, une seule fois sur deux connexions.
        using var barrier = new Barrier(2);

        async Task<string> Take()
        {
            barrier.SignalAndWait();
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var repository = new OrderRepository(context);
            var outcome = await repository.TryTransitionWithWorkshopSheetAsync(
                orderId, OrderStatus.QualityCheck, OrderStatus.Ready, sheetId, "EMPREINTE-A");
            return outcome.ToString();
        }

        var results = await Task.WhenAll(Task.Run(Take), Task.Run(Take));
        var taken = results.Count(r => r == "Taken");

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var status = await check.Orders.Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE transition+fiche (EXISTS correle) | empreinte perimee -> {stale} " +
            $"(attendu WorkshopSheetRequirementNotMet) | 2 connexions empreinte correcte -> " +
            $"[{string.Join(" | ", results)}] prises={taken} (attendu 1), statut={status} | " +
            $"{(stale == "WorkshopSheetRequirementNotMet" && taken == 1 && status == OrderStatus.Ready ? "GARANTIE TENUE" : "ANOMALIE")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : creation de la version suivante de fiche atelier
    // (OrderRepository.CreateNextWorkshopSheetVersionAsync) — CAS IsCurrent + arbitrage par index.
    // ---------------------------------------------------------------------------------------------
    private static async Task NextWorkshopSheetVersion(SpikeDatabase database, string name)
    {
        var orderId = await SeedOrder(database, "E11-WSV", OrderStatus.InProgress);

        // v1 : aucune version anterieure — c'est l'unicite (OrderId, Version) qui arbitre.
        using var barrier1 = new Barrier(2);

        async Task<string> CreateFirst()
        {
            barrier1.SignalAndWait();
            try
            {
                await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                var repository = new OrderRepository(context);
                var order = await context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
                var sheet = await repository.CreateNextWorkshopSheetVersionAsync(
                    order, WorkshopSheetVersionPrecondition.None, Fixed);
                return $"CREEE(v{sheet.Version})";
            }
            catch (WorkshopSheetVersionConflictException)
            {
                return "CONFLIT(WorkshopSheetVersionConflictException)";
            }
            catch (Exception ex)
            {
                return $"ERREUR({ErrorFacts.CandidateCategory(ex)})";
            }
        }

        var first = await Task.WhenAll(Task.Run(CreateFirst), Task.Run(CreateFirst));
        var created1 = first.Count(r => r.StartsWith("CREEE", StringComparison.Ordinal));

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE fiche-v1-concurrente | 2 connexions, arbitrage par index | [{string.Join(" | ", first)}] | " +
            $"creees={created1} (attendu 1) | {(created1 == 1 ? "GARANTIE TENUE" : "DOUBLE VERSION 1")}");

        // v2 : compare-and-swap sur la version courante reellement lue.
        await using (var read = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var current = await read.WorkshopSheets.AsNoTracking()
                .SingleAsync(w => w.OrderId == orderId && w.IsCurrent);
            var precondition = WorkshopSheetVersionPrecondition.From(current);

            using var barrier2 = new Barrier(2);

            async Task<string> CreateNext()
            {
                barrier2.SignalAndWait();
                try
                {
                    await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                    var repository = new OrderRepository(context);
                    var order = await context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
                    var sheet = await repository.CreateNextWorkshopSheetVersionAsync(order, precondition, Fixed);
                    return $"CREEE(v{sheet.Version})";
                }
                catch (WorkshopSheetVersionConflictException)
                {
                    return "CONFLIT(WorkshopSheetVersionConflictException)";
                }
                catch (Exception ex)
                {
                    return $"ERREUR({ErrorFacts.CandidateCategory(ex)})";
                }
            }

            var next = await Task.WhenAll(Task.Run(CreateNext), Task.Run(CreateNext));
            var created2 = next.Count(r => r.StartsWith("CREEE", StringComparison.Ordinal));

            await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var versions = await check.WorkshopSheets.CountAsync(w => w.OrderId == orderId);
            var currents = await check.WorkshopSheets.CountAsync(w => w.OrderId == orderId && w.IsCurrent);

            SpikeLog.Write(Experiment, name,
                $"PRIMITIVE fiche-v2-CAS | CAS sur la version lue | [{string.Join(" | ", next)}] | " +
                $"creees={created2} (attendu 1), versions totales={versions} (attendu 2), " +
                $"courantes={currents} (attendu 1) | " +
                $"{(created2 == 1 && versions == 2 && currents == 1 ? "GARANTIE TENUE" : "ANOMALIE")}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : decision QC definitive (OrderRepository.TryTakeWorkshopSheetQcDecisionAsync)
    // ---------------------------------------------------------------------------------------------
    private static async Task QcDecision(SpikeDatabase database, string name)
    {
        var orderId = await SeedOrder(database, "E11-QC", OrderStatus.QualityCheck);

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
                OrderNumberSnapshot = "E11-QC",
                OrderDateSnapshot = Fixed
            };
            seed.WorkshopSheets.Add(sheet);
            await seed.SaveChangesAsync();
            sheetId = sheet.WorkshopSheetId;
        }

        using var barrier = new Barrier(2);

        async Task<bool> Decide(WorkshopSheetQcStatus decision, string comment)
        {
            barrier.SignalAndWait();
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var repository = new OrderRepository(context);
            return await repository.TryTakeWorkshopSheetQcDecisionAsync(sheetId, decision, comment, Fixed);
        }

        var results = await Task.WhenAll(
            Task.Run(() => Decide(WorkshopSheetQcStatus.Passed, "validee")),
            Task.Run(() => Decide(WorkshopSheetQcStatus.Failed, "refusee")));
        var taken = results.Count(r => r);

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var final = await check.WorkshopSheets.Where(w => w.WorkshopSheetId == sheetId)
            .Select(w => w.QcStatus).SingleAsync();

        // Rejouer la decision deja prise ne doit affecter aucune ligne.
        bool replay;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new OrderRepository(context);
            replay = await repository.TryTakeWorkshopSheetQcDecisionAsync(
                sheetId, WorkshopSheetQcStatus.Passed, "rejeu", Fixed);
        }

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE decision-QC | 2 connexions, Passed vs Failed | prises={taken} (attendu 1), decision finale={final}, " +
            $"rejeu apres decision={replay} (attendu False) | " +
            $"{(taken == 1 && !replay ? "GARANTIE TENUE (decision definitive)" : "DOUBLE DECISION")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : reglement du solde via l'IMPLEMENTATION DE PRODUCTION
    // (SaleRepository.TrySettleRemainingBalanceAsync) — E8 n'avait exerce qu'un ExecuteUpdateAsync
    // reecrit dans le harness, PAS la methode de production.
    // ---------------------------------------------------------------------------------------------
    private static async Task SettleBalanceProduction(SpikeDatabase database, string name)
    {
        long saleId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sale = new Sale
            {
                SaleNumber = "E11-SOLDE",
                SaleDate = Fixed,
                TotalAmount = 300m,
                FinalAmount = 300m,
                DepositAmount = 120m,
                RemainingAmount = 180m,
                PaymentMethod = PaymentMethod.Card,
                PaymentStatus = PaymentStatus.Partial,
                Status = SaleStatus.Delivered
            };
            seed.Sales.Add(sale);
            await seed.SaveChangesAsync();
            saleId = sale.SaleId;
        }

        using var barrier = new Barrier(2);

        async Task<bool> Settle()
        {
            barrier.SignalAndWait();
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            var repository = new SaleRepository(context);
            return await repository.TrySettleRemainingBalanceAsync(saleId);
        }

        var results = await Task.WhenAll(Task.Run(Settle), Task.Run(Settle));
        var settled = results.Count(r => r);

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var sale2 = await check.Sales.Where(s => s.SaleId == saleId)
            .Select(s => new { s.RemainingAmount, s.DepositAmount, s.PaymentStatus }).SingleAsync();

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE reglement-solde (METHODE DE PRODUCTION SaleRepository) | 2 connexions | succes={settled} (attendu 1), " +
            $"reste={sale2.RemainingAmount} (attendu 0), acompte={sale2.DepositAmount} (attendu 300 = FinalAmount en base), " +
            $"statut={sale2.PaymentStatus} | " +
            $"{(settled == 1 && sale2.RemainingAmount == 0m && sale2.DepositAmount == 300m ? "GARANTIE TENUE" : "DOUBLE REGLEMENT / ANOMALIE")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : suppression fournisseur si inutilise via l'IMPLEMENTATION DE PRODUCTION
    // (SupplierRepository.TryDeleteIfUnusedAsync).
    // ---------------------------------------------------------------------------------------------
    private static async Task SupplierDeleteProduction(SpikeDatabase database, string name)
    {
        long usedId, freeId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var used = new Supplier { Name = "Fournisseur REFERENCE E11" };
            var free = new Supplier { Name = "Fournisseur LIBRE E11" };
            seed.Suppliers.AddRange(used, free);
            await seed.SaveChangesAsync();

            seed.Products.Add(NewProduct("Produit lie E11", used.SupplierId, 4));
            await seed.SaveChangesAsync();

            usedId = used.SupplierId;
            freeId = free.SupplierId;
        }

        bool refused, deleted;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new SupplierRepository(context);
            refused = await repository.TryDeleteIfUnusedAsync(usedId);
            deleted = await repository.TryDeleteIfUnusedAsync(freeId);
        }

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var stillThere = await check.Suppliers.AnyAsync(s => s.SupplierId == usedId);
        var gone = !await check.Suppliers.AnyAsync(s => s.SupplierId == freeId);

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE suppression-fournisseur (METHODE DE PRODUCTION SupplierRepository) | reference -> {refused} " +
            $"(attendu False, encore present={stillThere}), libre -> {deleted} (attendu True, supprime={gone}) | " +
            $"{(!refused && deleted && stillThere && gone ? "GARANTIE TENUE (historique preserve)" : "ANOMALIE")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : creation d'alerte LowStock via l'IMPLEMENTATION DE PRODUCTION
    // (NotificationRepository.TryCreateActiveLowStockAsync).
    //
    // Cette primitive porte du SQL et des PARAMETRES explicitement SQLite (SqliteParameter,
    // ON CONFLICT DO NOTHING). L'appel est tente TEL QUEL sur le serveur : l'echec eventuel est une
    // MESURE de la dette de portage, pas un defaut du harness.
    // ---------------------------------------------------------------------------------------------
    private static async Task LowStockCreateProduction(SpikeDatabase database, string name)
    {
        var productId = await SeedProduct(database, "AlerteProd", stock: 1);

        string outcome;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new NotificationRepository(context);
            try
            {
                var created = await repository.TryCreateActiveLowStockAsync(new Notification
                {
                    Type = NotificationTypes.LowStock,
                    EntityType = NotificationEntityTypes.Product,
                    EntityId = productId,
                    Title = "Stock bas",
                    Message = "Seuil atteint",
                    IsRead = false,
                    CreatedAt = Fixed
                });
                outcome = $"APPEL REUSSI (creee={created})";
            }
            catch (Exception ex)
            {
                outcome = $"ECHEC — {ex.GetType().Name} : {ErrorFacts.Describe(ex)}";
            }
        }

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE creation-LowStock (METHODE DE PRODUCTION NotificationRepository, SQL + SqliteParameter) | {outcome}");
    }

    // ---------------------------------------------------------------------------------------------
    // Primitive : reconciliation / resolution des alertes LowStock
    // (NotificationRepository.ResolveActiveLowStockAsync) — mise a jour de LOT + idempotence.
    // ---------------------------------------------------------------------------------------------
    private static async Task LowStockResolveProduction(SpikeDatabase database, string name)
    {
        var p1 = await SeedProduct(database, "Reco1", stock: 1);
        var p2 = await SeedProduct(database, "Reco2", stock: 1);

        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            foreach (var id in new[] { p1, p2 })
            {
                seed.Notifications.Add(new Notification
                {
                    Type = NotificationTypes.LowStock,
                    EntityType = NotificationEntityTypes.Product,
                    EntityId = id,
                    Title = "Stock bas",
                    Message = "Seuil atteint",
                    IsRead = false,
                    CreatedAt = Fixed,
                    ResolvedAt = null
                });
            }
            await seed.SaveChangesAsync();
        }

        var resolvedAt = Fixed.AddHours(1);
        int firstBatch, secondBatch, emptyBatch;

        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var repository = new NotificationRepository(context);
            emptyBatch = await repository.ResolveActiveLowStockAsync(Array.Empty<long>(), resolvedAt);
            firstBatch = await repository.ResolveActiveLowStockAsync(new[] { p1, p2 }, resolvedAt);
            // REJEU : la condition « ResolvedAt IS NULL » doit rendre l'appel idempotent.
            secondBatch = await repository.ResolveActiveLowStockAsync(new[] { p1, p2 }, resolvedAt.AddHours(5));
        }

        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var stillActive = await check.Notifications.CountAsync(n => n.ResolvedAt == null);
        var dates = await check.Notifications.Where(n => n.ResolvedAt != null)
            .Select(n => n.ResolvedAt!.Value).Distinct().ToListAsync();

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE reconciliation-LowStock (METHODE DE PRODUCTION NotificationRepository) | lot vide={emptyBatch} (attendu 0), " +
            $"1er lot={firstBatch} (attendu 2), REJEU={secondBatch} (attendu 0 = idempotent), " +
            $"actives restantes={stillActive} (attendu 0), dates de resolution distinctes={dates.Count} (attendu 1 : " +
            $"la date initiale n'est pas reecrite) | " +
            $"{(emptyBatch == 0 && firstBatch == 2 && secondBatch == 0 && stillActive == 0 && dates.Count == 1 ? "GARANTIE TENUE (idempotent)" : "ANOMALIE")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Aides de semis
    // ---------------------------------------------------------------------------------------------
    private static int _reference;

    private static Product NewProduct(string label, long supplierId, int stock)
    {
        // La reference normalisee porte un index unique (idx_products_normalized_reference_unique) :
        // chaque produit semé doit donc en porter une distincte.
        var reference = $"E11-REF-{Interlocked.Increment(ref _reference):D4}";
        return new Product
        {
            Reference = reference,
            Name = label,
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = stock,
            StockAlertThreshold = 5,
            IsActive = true,
            EntryDate = Fixed
        };
    }

    private static async Task<long> SeedProduct(SpikeDatabase database, string label, int stock)
    {
        await using var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var supplier = new Supplier { Name = $"Fournisseur {label}" };
        seed.Suppliers.Add(supplier);
        await seed.SaveChangesAsync();

        var product = NewProduct($"Produit {label}", supplier.SupplierId, stock);
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

        var order = new Order
        {
            OrderNumber = number,
            SaleId = sale.SaleId,
            OrderDate = Fixed,
            Status = status
        };
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
}
