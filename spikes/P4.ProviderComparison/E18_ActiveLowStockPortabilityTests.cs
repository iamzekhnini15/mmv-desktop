using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Repositories;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E18 — P4-1 <b>Lot D</b> : portabilité de <c>NotificationRepository.TryCreateActiveLowStockAsync</c>,
/// la 14ᵉ et dernière primitive de l'inventaire réconcilié, seule restée en échec au lot B.
///
/// <para>
/// E5 mesurait des <b>stratégies SQL écrites par le harness</b> ; E11 a appelé la <b>méthode de production</b> et
/// l'a vue échouer en <c>InvalidCastException</c> sur les DEUX providers (paramètres <c>SqliteParameter</c>
/// construits en dur). E18 appelle de nouveau la <b>méthode de production</b>, après correction, et vérifie par
/// des ASSERTIONS — non par de simples traces — que la garantie « au plus une alerte LowStock active par
/// produit » est tenue par la BASE, en concurrence réelle, sur les deux providers.
/// </para>
///
/// <para>
/// Aucune logique de production n'est recopiée ici. Aucun provider n'est choisi ni éliminé : les deux pistes sont
/// exercées à l'identique, avec le même protocole et les mêmes assertions.
/// </para>
/// </summary>
public class E18_ActiveLowStockPortabilityTests
{
    private const string Experiment = "E18-lotD-active-lowstock-portability";

    /// <summary>Une seule exécution ne prouverait rien en concurrence.</summary>
    private const int ConcurrencyRounds = 20;

    // ---------------------------------------------------------------------------------------------
    // Fonctionnel : la méthode de production s'exécute réellement, et la base arbitre.
    // ---------------------------------------------------------------------------------------------
    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Production_primitive_opens_then_refuses_then_reopens_after_resolution(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e18fn");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E18 — creation LowStock active (METHODE DE PRODUCTION, apres Lot D)");

        var productId = (await SeedProductsAsync(database, 1))[0];

        // 1) Première alerte active : la base doit l'ouvrir.
        var sent = LowStockFor(productId);
        bool first;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            first = await new NotificationRepository(context).TryCreateActiveLowStockAsync(sent);
        }

        // 2) Doublon depuis une AUTRE connexion : la base doit le refuser, sans exception technique.
        bool duplicate;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            duplicate = await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId));
        }

        var (totalAfterDuplicate, activeAfterDuplicate) = await CountAsync(database, productId);

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE creation-LowStock (PRODUCTION) | 1re={first} (attendu True), doublon={duplicate} " +
            $"(attendu False) | lignes={totalAfterDuplicate} (attendu 1), actives={activeAfterDuplicate} (attendu 1)");

        // MESURE annexe, sans assertion : fidélité du DateTime traversé par un paramètre NON typé explicitement.
        // La production passe `DateTime.Now` (Kind=Local) ; la restitution exacte relève du MAPPING du modèle
        // (portée P4-2, commune à toutes les colonnes de date), pas de cette primitive. On la CONSIGNE, on ne la
        // revendique pas.
        await using (var read = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var stored = await read.Notifications.AsNoTracking()
                .Where(n => n.EntityId == productId)
                .Select(n => n.CreatedAt)
                .SingleAsync();

            SpikeLog.Write(Experiment, name,
                $"MESURE CreatedAt via SQL BRUT (aucune assertion) | envoye={sent.CreatedAt:O} " +
                $"(Kind={sent.CreatedAt.Kind}) | relu={stored:O} (Kind={stored.Kind}) | " +
                $"identique={(stored == sent.CreatedAt ? "OUI" : "NON")}");
        }

        // Comparaison indispensable pour interpréter la mesure ci-dessus : que fait le CHEMIN EF NORMAL avec
        // exactement la même valeur ? Si EF refuse ce que le SQL brut accepte (ou l'inverse), l'écart appartient au
        // MAPPING du modèle — donc à P4-2 — et non à la primitive du Lot D.
        await using (var efPath = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var viaEf = LowStockFor(productId);
            viaEf.EntityId = productId + 1_000_000; // clé distincte : ne pas déclencher l'index unique filtré.
            try
            {
                efPath.Notifications.Add(viaEf);
                await efPath.SaveChangesAsync();

                var stored = await efPath.Notifications.AsNoTracking()
                    .Where(n => n.EntityId == viaEf.EntityId)
                    .Select(n => n.CreatedAt)
                    .SingleAsync();

                SpikeLog.Write(Experiment, name,
                    $"MESURE CreatedAt via EF (aucune assertion) | envoye={viaEf.CreatedAt:O} " +
                    $"(Kind={viaEf.CreatedAt.Kind}) | relu={stored:O} (Kind={stored.Kind}) | " +
                    $"identique={(stored == viaEf.CreatedAt ? "OUI" : "NON")}");
            }
            catch (Exception ex)
            {
                SpikeLog.Write(Experiment, name,
                    $"MESURE CreatedAt via EF (aucune assertion) | REFUS : {ex.GetType().Name} — {ErrorFacts.Describe(ex)}");
            }
        }

        Assert.True(first, "la première alerte active doit être ouverte");
        Assert.False(duplicate, "le doublon doit être refusé par la base, sans exception");
        Assert.Equal(1, totalAfterDuplicate);
        Assert.Equal(1, activeAfterDuplicate);

        // 3) Après résolution, un nouvel épisode doit être ACCEPTÉ : l'index ne contraint que l'actif.
        await using (var resolve = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await resolve.Notifications
                .Where(n => n.EntityId == productId && n.ResolvedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ResolvedAt, DateTime.UtcNow));
        }

        bool newEpisode;
        await using (var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            newEpisode = await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId));
        }

        var (totalAfterEpisode, activeAfterEpisode) = await CountAsync(database, productId);

        SpikeLog.Write(Experiment, name,
            $"PRIMITIVE creation-LowStock (PRODUCTION) | nouvel episode apres resolution={newEpisode} (attendu True) | " +
            $"lignes={totalAfterEpisode} (attendu 2), actives={activeAfterEpisode} (attendu 1) | " +
            $"{(first && !duplicate && newEpisode && totalAfterEpisode == 2 && activeAfterEpisode == 1 ? "GARANTIE TENUE" : "ANOMALIE")}");

        Assert.True(newEpisode, "une nouvelle pénurie après résolution ouvre légitimement une alerte");
        Assert.Equal(2, totalAfterEpisode);
        Assert.Equal(1, activeAfterEpisode);
    }

    // ---------------------------------------------------------------------------------------------
    // Concurrence : deux connexions RÉELLES démarrées ensemble, N tours, méthode de production.
    // ---------------------------------------------------------------------------------------------
    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Concurrent_production_calls_yield_exactly_one_active_alert(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e18cc");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, $"E18 — concurrence sur la METHODE DE PRODUCTION, {ConcurrencyRounds} tours");

        var productIds = await SeedProductsAsync(database, ConcurrencyRounds);

        var conforming = 0;
        var anomalies = new List<string>();

        for (var round = 0; round < ConcurrencyRounds; round++)
        {
            var productId = productIds[round];

            // Task.Run est INDISPENSABLE : Barrier.SignalAndWait() est bloquant et s'exécute AVANT le premier
            // await ; sans thread propre, le second participant ne serait jamais créé (interblocage du harness,
            // réellement observé pendant le spike). Aucun Thread.Sleep n'est employé.
            using var barrier = new Barrier(2);

            async Task<string> Attempt()
            {
                barrier.SignalAndWait();
                try
                {
                    await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
                    var created = await new NotificationRepository(context)
                        .TryCreateActiveLowStockAsync(LowStockFor(productId));
                    return created ? "CREEE" : "REFUSEE";
                }
                catch (Exception ex)
                {
                    // Aucune erreur n'est absorbée : elle est mesurée et fait échouer le tour.
                    return $"ERREUR({ErrorFacts.Describe(ex)})";
                }
            }

            var results = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));

            var created = results.Count(r => r == "CREEE");
            var refused = results.Count(r => r == "REFUSEE");
            var errors = results.Where(r => r is not ("CREEE" or "REFUSEE")).ToArray();
            var (total, active) = await CountAsync(database, productId);

            if (created == 1 && refused == 1 && errors.Length == 0 && total == 1 && active == 1)
            {
                conforming++;
            }
            else
            {
                anomalies.Add(
                    $"tour {round} : creees={created} refusees={refused} lignes={total} actives={active} " +
                    $"erreurs=[{string.Join(" | ", errors)}]");
                SpikeLog.Write(Experiment, name, $"ANOMALIE {anomalies[^1]}");
            }
        }

        SpikeLog.Write(Experiment, name,
            $"RESULTAT concurrence (METHODE DE PRODUCTION) : {conforming}/{ConcurrencyRounds} tours conformes " +
            $"(1 creation, 1 refus, 1 seule alerte active) ; anomalies={anomalies.Count}");

        Assert.True(anomalies.Count == 0,
            $"{anomalies.Count} tour(s) non conforme(s) : {string.Join(" ; ", anomalies)}");
        Assert.Equal(ConcurrencyRounds, conforming);
    }

    // ---------------------------------------------------------------------------------------------
    // Semis et lectures
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reproduit FIDÈLEMENT ce que construit <c>GenerateLowStockNotificationsUseCase</c>, y compris
    /// <c>CreatedAt = DateTime.Now</c> (<c>DateTimeKind.Local</c>) : tester avec une date UTC aurait masqué le
    /// comportement réel de l'application.
    /// </summary>
    private static Notification LowStockFor(long productId) => new()
    {
        Type = NotificationTypes.LowStock,
        Title = $"Stock bas : produit {productId}",
        Message = "Seuil d'alerte atteint",
        EntityId = productId,
        EntityType = NotificationEntityTypes.Product,
        IsRead = false,
        CreatedAt = DateTime.Now,
    };

    private static int _reference;

    private static async Task<IReadOnlyList<long>> SeedProductsAsync(SpikeDatabase database, int count)
    {
        await using var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);

        var supplier = new Supplier { Name = "Fournisseur E18" };
        seed.Suppliers.Add(supplier);
        await seed.SaveChangesAsync();

        var products = new List<Product>(count);
        for (var i = 0; i < count; i++)
        {
            products.Add(new Product
            {
                // idx_products_normalized_reference_unique : chaque référence doit être distincte.
                Reference = $"E18-REF-{Interlocked.Increment(ref _reference):D4}",
                Name = $"Produit E18 {i}",
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId,
                PurchasePrice = 10m,
                SalePrice = 20m,
                StockQuantity = 1,
                StockAlertThreshold = 5,
                IsActive = true,
                EntryDate = new DateTime(2026, 7, 26, 9, 0, 0, DateTimeKind.Utc)
            });
        }

        seed.Products.AddRange(products);
        await seed.SaveChangesAsync();

        return products.Select(p => p.ProductId).ToList();
    }

    private static async Task<(int Total, int Active)> CountAsync(SpikeDatabase database, long productId)
    {
        await using var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
        var total = await check.Notifications.CountAsync(n => n.EntityId == productId);
        var active = await check.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null);
        return (total, active);
    }
}
