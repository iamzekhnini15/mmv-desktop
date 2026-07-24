using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E2 — Précision monétaire.
///
/// Le modèle de production mappe des <c>decimal</c> métier sur <c>HasColumnType("REAL")</c>
/// (P4-0 §17). Cette expérimentation écrit des montants représentatifs via EF, les relit par un
/// NOUVEAU contexte puis par SQL natif, et compare à la valeur EXACTE attendue.
///
/// Aucune précision finale n'est choisie ici : on mesure le mapping ACTUEL et, à titre de
/// comparaison, un mapping candidat exact.
/// </summary>
public class E2_MoneyPrecisionTests
{
    private const string Experiment = "E2-money-precision";

    public static IEnumerable<object[]> Cases()
    {
        foreach (var provider in new[] { ProviderKind.Postgres, ProviderKind.SqlServer })
        {
            foreach (var mapping in new[] { MoneyMapping.AsIs, MoneyMapping.ExactDecimal })
            {
                yield return new object[] { provider, mapping };
            }
        }
    }

    private static readonly decimal[] Amounts =
    {
        0.01m, 0.10m, 0.30m, 19.99m, 999999.99m
    };

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task Monetary_values_round_trip(ProviderKind provider, MoneyMapping mapping)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e2");

        await using (var context = AdaptedOpticDbContext.Create(database, mapping))
        {
            await context.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, $"Mapping = {mapping}");

        var physicalType = await database.ScalarAsync(provider == ProviderKind.Postgres
            ? "SELECT data_type || COALESCE('(' || numeric_precision || ',' || numeric_scale || ')','') FROM information_schema.columns WHERE table_name='Sales' AND column_name='TotalAmount';"
            : "SELECT CONCAT(t.name,'(',c.precision,',',c.scale,')') FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID('Sales') AND c.name='TotalAmount';");

        SpikeLog.Write(Experiment, name, $"Type physique de Sales.TotalAmount : {physicalType}");

        var exactCount = 0;

        foreach (var amount in Amounts)
        {
            var saleNumber = $"E2-{mapping}-{amount}".Replace(',', '.');

            // 1) Écriture via EF.
            await using (var write = AdaptedOpticDbContext.Create(database, mapping))
            {
                write.Sales.Add(new Sale
                {
                    SaleNumber = saleNumber,
                    SaleDate = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc),
                    TotalAmount = amount,
                    DiscountAmount = 0m,
                    FinalAmount = amount,
                    RemainingAmount = amount,
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Paid,
                    Status = SaleStatus.Draft
                });
                await write.SaveChangesAsync();
            }

            // 2) Relecture par un NOUVEAU contexte (aucun cache de suivi).
            decimal readBack;
            await using (var read = AdaptedOpticDbContext.Create(database, mapping))
            {
                readBack = await read.Sales.Where(s => s.SaleNumber == saleNumber)
                    .Select(s => s.TotalAmount)
                    .SingleAsync();
            }

            // 3) Relecture par SQL NATIF (contourne toute conversion EF). Les identifiants entre
            // guillemets doubles sont acceptés par les deux providers (QUOTED_IDENTIFIER est ON
            // par défaut avec SqlClient).
            var nativeText = await database.ScalarAsync(
                $"SELECT CAST(\"TotalAmount\" AS varchar(64)) FROM \"Sales\" WHERE \"SaleNumber\" = '{saleNumber}';");

            var exact = readBack == amount;
            if (exact)
            {
                exactCount++;
            }

            SpikeLog.Write(Experiment, name,
                $"attendu={amount} | relu_EF={readBack} | relu_SQL={nativeText} | EXACT={(exact ? "OUI" : "NON")}");
        }

        // Scénario métier composite exigé par le brief : somme de plusieurs lignes, remise,
        // acompte, reste à payer. L'erreur d'un flottant s'ACCUMULE : c'est le cas qui compte.
        await CompositeSaleAsync(database, mapping, name);

        SpikeLog.Write(Experiment, name,
            $"BILAN mapping {mapping} sur {provider} : {exactCount}/{Amounts.Length} valeurs unitaires exactes (type {physicalType})");

        Assert.True(true);
    }

    private static async Task CompositeSaleAsync(SpikeDatabase database, MoneyMapping mapping, string name)
    {
        // 3 lignes à 0,10 € ; remise 0,07 € ; acompte 0,05 €.
        const int lines = 3;
        const decimal unitPrice = 0.10m;
        const decimal discount = 0.07m;
        const decimal deposit = 0.05m;

        var total = unitPrice * lines;          // 0,30
        var final = total - discount;           // 0,23
        var remaining = final - deposit;        // 0,18

        var saleNumber = $"E2-COMPOSITE-{mapping}";

        await using (var write = AdaptedOpticDbContext.Create(database, mapping))
        {
            var sale = new Sale
            {
                SaleNumber = saleNumber,
                SaleDate = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc),
                TotalAmount = total,
                DiscountAmount = discount,
                FinalAmount = final,
                DepositAmount = deposit,
                RemainingAmount = remaining,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Partial,
                Status = SaleStatus.Draft
            };
            write.Sales.Add(sale);
            await write.SaveChangesAsync();
        }

        // Somme calculée PAR LE SERVEUR sur les colonnes monétaires.
        var serverSum = await database.ScalarAsync(
            $"SELECT CAST(SUM(\"FinalAmount\") AS varchar(64)) FROM \"Sales\" WHERE \"SaleNumber\" = '{saleNumber}';");

        decimal readRemaining;
        await using (var read = AdaptedOpticDbContext.Create(database, mapping))
        {
            readRemaining = (await read.Sales.Where(s => s.SaleNumber == saleNumber)
                .Select(s => s.RemainingAmount)
                .SingleAsync()) ?? -1m;
        }

        SpikeLog.Write(Experiment, name,
            $"COMPOSITE ({lines}x{unitPrice} - remise {discount} - acompte {deposit}) : " +
            $"final attendu={final} somme_serveur={serverSum} | reste attendu={remaining} relu={readRemaining} | " +
            $"EXACT={(readRemaining == remaining ? "OUI" : "NON")}");
    }
}
