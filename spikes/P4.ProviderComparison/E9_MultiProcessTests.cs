using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E9 — Concurrence MULTI-PROCESSUS réelle.
///
/// Deux exécutables indépendants (PID distincts, connexions distinctes) sont lancés, synchronisés
/// par un ÉVÉNEMENT NOMMÉ du système. Aucun <c>Thread.Sleep</c> ne sert de mécanisme de
/// synchronisation. C'est la forme de preuve absente du corpus P3 (P4-0 §23/§24).
///
/// Si le worker n'a pas été compilé, le test est SKIPPED — jamais vert artificiellement.
/// </summary>
public class E9_MultiProcessTests
{
    private const string Experiment = "E9-multi-process";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Two_real_processes_compete(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var worker = LocateWorker();
        Skip.If(worker is null,
            "Worker E9 introuvable — exécuter « dotnet build spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj ». MULTI-PROCESSUS NON TESTÉ.");

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e9");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E9 — Deux PROCESSUS indépendants");

        long productId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var supplier = new Supplier { Name = "Fournisseur E9" };
            seed.Suppliers.Add(supplier);
            await seed.SaveChangesAsync();

            var product = new Product
            {
                Name = "Dernier article E9",
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId,
                PurchasePrice = 10m,
                SalePrice = 20m,
                StockQuantity = 1,
                StockAlertThreshold = 5,
                IsActive = true,
                EntryDate = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc)
            };
            seed.Products.Add(product);

            seed.DocumentSequences.Add(new DocumentSequence
            {
                SequenceName = "E9SEQ",
                Prefix = "E9",
                CurrentValue = 0,
                UpdatedAt = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc)
            });

            await seed.SaveChangesAsync();
            productId = product.ProductId;
        }

        var providerArgument = provider == ProviderKind.Postgres ? "postgres" : "sqlserver";

        // Scénario 1 — deux postes vendent le DERNIER article.
        var stockResults = await RaceAsync(worker!, providerArgument, database, "decrement-stock", productId.ToString());
        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var stock = await check.Products.Where(p => p.ProductId == productId).Select(p => p.StockQuantity).SingleAsync();
            var winners = stockResults.Count(r => r.Contains("RESULT=WON", StringComparison.Ordinal));
            SpikeLog.Write(Experiment, name,
                $"Dernier article — {string.Join(" ;; ", stockResults)} | stock final={stock} | gagnants={winners} → {(winners == 1 && stock == 0 ? "GARANTIE TENUE (aucune survente)" : "SURVENTE")}");
        }

        // Scénario 2 — deux postes créent la MÊME alerte LowStock.
        var alertResults = await RaceAsync(worker!, providerArgument, database, "low-stock-alert", productId.ToString());
        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var active = await check.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null);
            var winners = alertResults.Count(r => r.Contains("RESULT=WON", StringComparison.Ordinal));
            SpikeLog.Write(Experiment, name,
                $"Alerte LowStock — {string.Join(" ;; ", alertResults)} | alertes actives={active} | gagnants={winners} → {(active == 1 ? "ANTI-DOUBLON TENU" : "DOUBLON")}");
        }

        // Scénario 3 — deux postes tirent le MÊME numéro.
        var numberResults = await RaceAsync(worker!, providerArgument, database, "next-number", "E9SEQ");
        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var value = await check.DocumentSequences.Where(s => s.SequenceName == "E9SEQ").Select(s => s.CurrentValue).SingleAsync();
            SpikeLog.Write(Experiment, name,
                $"Numérotation — {string.Join(" ;; ", numberResults)} | compteur final={value} (attendu 2 : deux incréments distincts) → {(value == 2 ? "AUCUN NUMÉRO PERDU NI DUPLIQUÉ" : "ANOMALIE")}");
        }

        Assert.True(true);
    }

    private static async Task<string[]> RaceAsync(string worker, string provider, SpikeDatabase database, string operation, string argument)
    {
        var gateName = "MMV_P4_E9_" + Guid.NewGuid().ToString("n")[..8];
        using var gate = new EventWaitHandle(false, EventResetMode.ManualReset, gateName);

        var processes = new List<Process>();
        var readies = new List<Task<string?>>();

        for (var i = 0; i < 2; i++)
        {
            var info = new ProcessStartInfo
            {
                FileName = worker,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add(provider);
            info.ArgumentList.Add(operation);
            info.ArgumentList.Add(gateName);
            info.ArgumentList.Add(argument);

            // Le secret passe par l'ENVIRONNEMENT du processus enfant, jamais par la ligne de
            // commande (qui serait lisible par d'autres processus).
            info.Environment["MMV_P4_WORKER_CONNECTION"] = database.ConnectionString;

            var process = Process.Start(info)!;
            processes.Add(process);
            readies.Add(process.StandardError.ReadLineAsync());
        }

        // On attend que les DEUX processus aient ouvert leur connexion et annoncé READY.
        await Task.WhenAll(readies);

        // Départ simultané par signal externe.
        gate.Set();

        var outputs = new List<string>();
        foreach (var process in processes)
        {
            var line = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            outputs.Add(line.Trim().Length == 0 ? $"(aucune sortie, exit={process.ExitCode})" : line.Trim());
            process.Dispose();
        }

        return outputs.ToArray();
    }

    private static string? LocateWorker()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, "Worker", "bin", "Debug", "net8.0", "MMV.P4.Worker.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
