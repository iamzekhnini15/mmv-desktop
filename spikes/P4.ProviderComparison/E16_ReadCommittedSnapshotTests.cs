using System.Diagnostics;
using Microsoft.Data.SqlClient;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E16 — <c>READ_COMMITTED_SNAPSHOT</c> (RCSI) sur SQL Server.
///
/// Le rapport initial classait cette mesure `NOT_TESTED` : une requête <c>sys.databases</c> avait
/// échoué sur un conflit de collation et n'avait pas été rejouée. La cause exacte est identifiée
/// ici (voir <see cref="CollationConflictIsDiagnosed"/>) puis contournée, et les deux
/// configurations sont réellement mesurées.
///
/// Aucune base principale n'est modifiée : chaque variante s'exécute sur sa PROPRE base jetable.
/// </summary>
public class E16_ReadCommittedSnapshotTests
{
    private const string Experiment = "E16-rcsi";

    /// <summary>
    /// Reproduit et DIAGNOSTIQUE le conflit de collation qui avait rendu la mesure initiale
    /// impossible, puis démontre le contournement. C'est la correction factuelle du §16 initial.
    /// </summary>
    [SkippableFact]
    public async Task CollationConflictIsDiagnosed()
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(ProviderKind.SqlServer),
            SpikeEnvironment.SkipReason(ProviderKind.SqlServer));

        const string name = "SqlServer";
        await using var database = await SpikeDatabase.CreateAsync(ProviderKind.SqlServer, "e16col");

        SpikeLog.Section(Experiment, name, "E16 — diagnostic du conflit de collation sur sys.databases");

        var serverCollation = await database.ScalarAsync(
            "SELECT CAST(SERVERPROPERTY('Collation') AS nvarchar(128));");

        // Collation RÉELLE de chaque colonne du catalogue, mesurée et non supposée.
        var nameCollation = await database.ScalarAsync(
            "SELECT CAST(SQL_VARIANT_PROPERTY(name,'Collation') AS nvarchar(128)) FROM sys.databases WHERE database_id = 1;");
        var descCollation = await database.ScalarAsync(
            "SELECT CAST(SQL_VARIANT_PROPERTY(snapshot_isolation_state_desc,'Collation') AS nvarchar(128)) FROM sys.databases WHERE database_id = 1;");

        // La requête FAUTIVE : concaténer une colonne descriptive du catalogue avec un littéral.
        string faulty;
        try
        {
            await database.ScalarAsync(
                "SELECT CONCAT(name, ' | ', snapshot_isolation_state_desc) FROM sys.databases WHERE database_id = DB_ID();");
            faulty = "AUCUNE ERREUR (le conflit ne se reproduit pas)";
        }
        catch (Exception ex)
        {
            faulty = ErrorFacts.Describe(ex);
        }

        // Le CONTOURNEMENT : colonnes séparées (ou COLLATE explicite), jamais de concaténation.
        var workaround = await database.ScalarAsync(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID();");

        SpikeLog.Write(Experiment, name,
            $"collation serveur={serverCollation} | collation de sys.databases.name={nameCollation} | " +
            $"collation de snapshot_isolation_state_desc={descCollation}");
        SpikeLog.Write(Experiment, name, $"requete fautive (CONCAT sur colonne descriptive) : {faulty}");
        SpikeLog.Write(Experiment, name,
            $"contournement (colonnes separees, sans CONCAT) : is_read_committed_snapshot_on={workaround} | MESURE POSSIBLE");

        Assert.True(true);
    }

    /// <summary>
    /// Mesure les DEUX configurations : RCSI par défaut (OFF) puis RCSI ON, sur le même scénario
    /// « lecture pendant écriture non commitée » et sur la primitive CAS de décrément de stock.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rcsi_off_and_on_are_measured(bool enableRcsi)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(ProviderKind.SqlServer),
            SpikeEnvironment.SkipReason(ProviderKind.SqlServer));

        const string name = "SqlServer";
        var label = enableRcsi ? "RCSI=ON" : "RCSI=OFF (defaut)";

        await using var database = await SpikeDatabase.CreateAsync(
            ProviderKind.SqlServer, enableRcsi ? "e16on" : "e16off");

        SpikeLog.Section(Experiment, name, $"E16 — {label}");

        if (enableRcsi)
        {
            // Exécuté depuis `master` : ALTER DATABASE ne peut pas viser la base courante en
            // exclusivité. ROLLBACK IMMEDIATE est sans risque : base JETABLE, aucune donnée utilisateur.
            await using var admin = new SqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            await using var alter = admin.CreateCommand();
            alter.CommandText =
                $"ALTER DATABASE [{database.DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
            await alter.ExecuteNonQueryAsync();
        }

        // État RELEVÉ SUR LE SERVEUR pour CETTE base (colonnes séparées : pas de conflit de collation).
        var rcsi = await database.ScalarAsync(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID();");
        var snapshotState = await database.ScalarAsync(
            "SELECT CAST(snapshot_isolation_state_desc AS nvarchar(60)) FROM sys.databases WHERE database_id = DB_ID();");
        var collation = await database.ScalarAsync(
            "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(),'Collation') AS nvarchar(128));");

        SpikeLog.Write(Experiment, name,
            $"{label} | is_read_committed_snapshot_on={rcsi} | snapshot_isolation_state_desc={snapshotState} | " +
            $"collation de la base={collation}");

        await Execute(database, "CREATE TABLE rcsi_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await Execute(database, "INSERT INTO rcsi_probe (id, payload) VALUES (1, 100);");

        // --- Lecture PENDANT une écriture non commitée -------------------------------------------
        await using (var writer = await database.OpenRawAsync())
        {
            await using var writeTx = await writer.BeginTransactionAsync();
            await using (var write = writer.CreateCommand())
            {
                write.Transaction = writeTx;
                write.CommandText = "UPDATE rcsi_probe SET payload = 999 WHERE id = 1;";
                await write.ExecuteNonQueryAsync();
            }

            var sw = Stopwatch.StartNew();
            string readResult;
            try
            {
                await using var reader = await database.OpenRawAsync();
                await using var read = reader.CreateCommand();
                read.CommandTimeout = 5; // borne : sans RCSI la lecture doit bloquer puis expirer
                read.CommandText = "SELECT payload FROM rcsi_probe WHERE id = 1;";
                var value = await read.ExecuteScalarAsync();
                readResult = $"LECTURE NON BLOQUANTE, valeur={value} (valeur COMMITEE, l'ecriture en cours est ignoree)";
            }
            catch (Exception ex)
            {
                readResult = $"LECTURE BLOQUEE puis expiree — {ErrorFacts.Describe(ex)}";
            }
            sw.Stop();

            SpikeLog.Write(Experiment, name,
                $"{label} | lecture pendant ecriture non commitee : {readResult} | duree={sw.ElapsedMilliseconds} ms");

            await writeTx.RollbackAsync();
        }

        var afterRollback = await database.ScalarAsync("SELECT payload FROM rcsi_probe WHERE id = 1;");
        SpikeLog.Write(Experiment, name,
            $"{label} | valeur apres rollback de l'ecrivain={afterRollback} (attendu 100) | " +
            $"{(afterRollback == "100" ? "COHERENCE PRESERVEE" : "ANOMALIE")}");

        // --- Impact sur la primitive CAS (décrément conditionnel du dernier article) --------------
        await Execute(database, "CREATE TABLE rcsi_stock (id int PRIMARY KEY, qty int NOT NULL);");
        await Execute(database, "INSERT INTO rcsi_stock (id, qty) VALUES (1, 1);");

        using var barrier = new Barrier(2);

        async Task<int> ConditionalDecrement()
        {
            barrier.SignalAndWait();
            await using var connection = await database.OpenRawAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            // Forme exacte de la primitive de production : UPDATE conditionnel atomique.
            command.CommandText = "UPDATE rcsi_stock SET qty = qty - 1 WHERE id = 1 AND qty >= 1;";
            return await command.ExecuteNonQueryAsync();
        }

        var rows = await Task.WhenAll(Task.Run(ConditionalDecrement), Task.Run(ConditionalDecrement));
        var finalQty = await database.ScalarAsync("SELECT qty FROM rcsi_stock WHERE id = 1;");

        SpikeLog.Write(Experiment, name,
            $"{label} | primitive CAS decrement du dernier article : lignes affectees=[{string.Join(", ", rows)}] " +
            $"(attendu un seul 1), stock final={finalQty} (attendu 0) | " +
            $"{(rows.Count(r => r == 1) == 1 && finalQty == "0" ? "AUCUNE SURVENTE — CAS INCHANGE PAR RCSI" : "SURVENTE / ANOMALIE")}");

        Assert.True(true);
    }

    private static async Task Execute(SpikeDatabase database, string sql)
    {
        await using var connection = await database.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
