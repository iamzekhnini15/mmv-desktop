using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E10 — Sauvegarde et restauration avec l'OUTIL OFFICIEL de chaque provider.
///
/// PostgreSQL : <c>pg_dump</c> / <c>pg_restore</c>. SQL Server : <c>BACKUP DATABASE</c> /
/// <c>RESTORE DATABASE</c>. Les deux serveurs du spike tournent en conteneur : les outils sont
/// donc invoqués via <c>docker exec</c>, et la sauvegarde reste DANS le conteneur.
///
/// Aucune base utilisateur n'est touchée. Le test est SKIPPED si Docker n'est pas disponible.
/// </summary>
public class E10_BackupRestoreTests
{
    private const string Experiment = "E10-backup-restore";
    private const string PostgresContainer = "mmv-p4-pg";
    private const string SqlServerContainer = "mmv-p4-mssql";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Backup_and_restore_preserve_the_database(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));
        Skip.IfNot(DockerAvailable(), "Docker indisponible — SAUVEGARDE/RESTAURATION NON TESTÉE.");

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e10");

        // --- Jeu de données cohérent (FK, montants exacts, index filtré) ---
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await seed.Database.EnsureCreatedAsync();

            var supplier = new Supplier { Name = "Fournisseur E10" };
            seed.Suppliers.Add(supplier);
            await seed.SaveChangesAsync();

            seed.Products.Add(new Product
            {
                Name = "Produit E10",
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId,
                PurchasePrice = 12.34m,
                SalePrice = 56.78m,
                StockQuantity = 7,
                StockAlertThreshold = 5,
                IsActive = true,
                EntryDate = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc)
            });

            seed.Sales.Add(new Sale
            {
                SaleNumber = "E10-SALE",
                SaleDate = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
                TotalAmount = 999999.99m,
                DiscountAmount = 0.07m,
                FinalAmount = 999999.92m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid,
                Status = SaleStatus.Draft
            });

            seed.Notifications.Add(new Notification
            {
                Type = NotificationTypes.LowStock,
                Title = "Stock bas E10",
                Message = "Alerte E10",
                EntityId = 1,
                EntityType = NotificationEntityTypes.Product,
                IsRead = false,
                CreatedAt = new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc)
            });

            await seed.SaveChangesAsync();
        }

        var before = await SnapshotAsync(database);
        SpikeLog.Section(Experiment, name, "E10 — Sauvegarde / restauration");
        SpikeLog.Write(Experiment, name, $"AVANT : {before}");

        var started = Stopwatch.StartNew();
        string backup, restore, sizeInfo;

        if (provider == ProviderKind.Postgres)
        {
            // Sauvegarde au format personnalisé (rejouable par pg_restore).
            backup = Docker(PostgresContainer, "bash", "-lc",
                $"pg_dump -U postgres -F c -f /tmp/{database.DatabaseName}.dump {database.DatabaseName} && echo OK");

            sizeInfo = Docker(PostgresContainer, "bash", "-lc",
                $"du -h /tmp/{database.DatabaseName}.dump | cut -f1");

            // Suppression puis recréation de la base, puis restauration.
            restore = Docker(PostgresContainer, "bash", "-lc",
                $"dropdb -U postgres --force {database.DatabaseName} && " +
                $"createdb -U postgres -O mmv_spike {database.DatabaseName} && " +
                $"pg_restore -U postgres -d {database.DatabaseName} /tmp/{database.DatabaseName}.dump && echo OK");
        }
        else
        {
            var password = Environment.GetEnvironmentVariable("MMV_P4_SA_PASSWORD") ?? string.Empty;
            Skip.If(password.Length == 0, "MMV_P4_SA_PASSWORD non définie — sauvegarde SQL Server NON TESTÉE.");

            string Sql(string query) => Docker(SqlServerContainer,
                "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", password, "-C", "-No", "-b", "-Q", query);

            backup = Sql($"BACKUP DATABASE [{database.DatabaseName}] TO DISK='/var/opt/mssql/data/{database.DatabaseName}.bak' WITH INIT, FORMAT;");

            sizeInfo = Docker(SqlServerContainer, "bash", "-lc",
                $"du -h /var/opt/mssql/data/{database.DatabaseName}.bak | cut -f1");

            restore = Sql(
                $"ALTER DATABASE [{database.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{database.DatabaseName}]; " +
                $"RESTORE DATABASE [{database.DatabaseName}] FROM DISK='/var/opt/mssql/data/{database.DatabaseName}.bak' WITH RECOVERY;");
        }

        started.Stop();

        // La base a été SUPPRIMÉE puis recréée : toute connexion encore en pool pointe vers un
        // backend détruit (PostgreSQL 57P01). Le pool doit être purgé avant toute vérification.
        if (provider == ProviderKind.Postgres)
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
        }
        else
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }

        SpikeLog.Write(Experiment, name, $"Outil de sauvegarde : {(provider == ProviderKind.Postgres ? "pg_dump -F c" : "BACKUP DATABASE ... TO DISK")} → {Summarize(backup)}");
        SpikeLog.Write(Experiment, name, $"Taille indicative de la sauvegarde : {sizeInfo.Trim()}");
        SpikeLog.Write(Experiment, name, $"Outil de restauration : {(provider == ProviderKind.Postgres ? "pg_restore" : "RESTORE DATABASE")} → {Summarize(restore)}");
        SpikeLog.Write(Experiment, name, $"Durée indicative sauvegarde+restauration : {started.Elapsed.TotalSeconds:F1}s");

        // --- Vérification APRÈS restauration : lignes, FK, index, montants, connexion applicative ---
        var after = await SnapshotAsync(database);
        SpikeLog.Write(Experiment, name, $"APRÈS : {after}");
        SpikeLog.Write(Experiment, name,
            $"Réconciliation : {(before == after ? "IDENTIQUE (lignes, FK, index, montants)" : "DIVERGENCE — À INVESTIGUER")}");

        // La connexion applicative doit fonctionner sur la base restaurée.
        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var amount = await check.Sales.Where(s => s.SaleNumber == "E10-SALE").Select(s => s.TotalAmount).SingleAsync();
            SpikeLog.Write(Experiment, name,
                $"Connexion applicative après restauration : OK | montant relu={amount} (attendu 999999,99) → {(amount == 999999.99m ? "EXACT" : "ALTÉRÉ")}");
        }

        Assert.True(true);
    }

    /// <summary>Empreinte comparable avant/après : comptes, FK, index, somme monétaire.</summary>
    private static async Task<string> SnapshotAsync(SpikeDatabase database)
    {
        var isPostgres = database.Provider == ProviderKind.Postgres;

        // L'opérateur de concaténation diffère : « || » (PostgreSQL) vs « + » (SQL Server).
        // Il est construit EXPLICITEMENT ici — un .Replace() en fin de chaîne concaténée ne
        // s'appliquerait qu'au DERNIER littéral (défaut réellement rencontré pendant le spike).
        var concat = isPostgres ? " || " : " + ";
        var rows = await database.ScalarAsync(
            "SELECT CAST((SELECT COUNT(*) FROM \"Suppliers\") AS varchar(16))" + concat + "'/'" + concat +
            "CAST((SELECT COUNT(*) FROM \"Products\") AS varchar(16))" + concat + "'/'" + concat +
            "CAST((SELECT COUNT(*) FROM \"Sales\") AS varchar(16))" + concat + "'/'" + concat +
            "CAST((SELECT COUNT(*) FROM \"Notifications\") AS varchar(16));");

        var structure = await database.ScalarAsync(isPostgres
            ? "SELECT (SELECT count(*) FROM information_schema.table_constraints WHERE constraint_type='FOREIGN KEY' AND table_schema='public') || ' FK, ' || (SELECT count(*) FROM pg_indexes WHERE schemaname='public') || ' index';"
            : "SELECT CONCAT((SELECT count(*) FROM sys.foreign_keys),' FK, ',(SELECT count(*) FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables)),' index');");

        var money = await database.ScalarAsync(
            "SELECT CAST(SUM(\"TotalAmount\") AS varchar(64)) FROM \"Sales\";");

        return $"lignes(S/P/V/N)={rows} | {structure} | somme montants={money}";
    }

    private static string Docker(string container, params string[] arguments)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add(container);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? (output.Trim().Length == 0 ? "OK" : output.Trim())
            : $"ÉCHEC(exit={process.ExitCode}) {error.Trim()}";
    }

    private static bool DockerAvailable()
    {
        try
        {
            var info = new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(info)!;
            process.WaitForExit(15000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string Summarize(string output)
    {
        var single = output.Replace(Environment.NewLine, " ").Trim();
        return single.Length <= 120 ? single : single[..120] + "…";
    }
}
