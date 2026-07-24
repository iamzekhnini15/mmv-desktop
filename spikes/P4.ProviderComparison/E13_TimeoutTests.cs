using System.Diagnostics;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E13 — TIMEOUTS mesurés et DISTINGUÉS. Le rapport initial classait deux cas PostgreSQL en
/// « Unknown » (aucun <c>SqlState</c>) ; il faut savoir ce qui est distinguable, et dans quel état
/// la connexion et la transaction se retrouvent APRÈS l'erreur.
///
/// Quatre situations volontairement distinctes sont mesurées :
///   1. timeout du CLIENT (<c>CommandTimeout</c> court sur une commande volontairement longue) ;
///   2. annulation par <c>CancellationToken</c> ;
///   3. timeout de VERROU (une autre transaction détient la ligne) ;
///   4. connexion refusée (port fermé).
/// </summary>
public class E13_TimeoutTests
{
    private const string Experiment = "E13-timeouts";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Timeout_situations_are_measured_and_distinguished(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e13");

        await Execute(database, "CREATE TABLE timeout_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await Execute(database, "INSERT INTO timeout_probe (id, payload) VALUES (1, 0);");

        SpikeLog.Section(Experiment, name, "E13 — Timeouts et etat post-erreur");

        var sleep = provider == ProviderKind.Postgres
            ? "SELECT pg_sleep(30);"
            : "WAITFOR DELAY '00:00:30';";

        // --- 1) Timeout CLIENT : CommandTimeout court sur une commande longue ---------------------
        await MeasureAsync(database, name, "timeout-client (CommandTimeout=2s sur commande 30s)", async (connection, transaction) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 2;
            command.CommandText = sleep;
            await command.ExecuteNonQueryAsync();
        });

        // --- 2) Annulation par CancellationToken --------------------------------------------------
        await MeasureAsync(database, name, "annulation-CancellationToken (2s)", async (connection, transaction) =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 60; // le timeout client ne doit PAS être la cause
            command.CommandText = sleep;
            await command.ExecuteNonQueryAsync(cts.Token);
        });

        // --- 3) Timeout de VERROU : une autre transaction détient la ligne -------------------------
        await using (var holder = await database.OpenRawAsync())
        {
            await using var holdTx = await holder.BeginTransactionAsync();
            await using (var hold = holder.CreateCommand())
            {
                hold.Transaction = holdTx;
                hold.CommandText = "UPDATE timeout_probe SET payload = payload + 1 WHERE id = 1;";
                await hold.ExecuteNonQueryAsync();
            }

            // La ligne est verrouillée : un second UPDATE doit attendre, puis expirer côté client.
            await MeasureAsync(database, name, "timeout-de-verrou (ligne detenue par une autre transaction)", async (connection, transaction) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 3;
                command.CommandText = "UPDATE timeout_probe SET payload = payload + 100 WHERE id = 1;";
                await command.ExecuteNonQueryAsync();
            });

            await holdTx.RollbackAsync();
        }

        // --- 4) Connexion refusée : port fermé -----------------------------------------------------
        var closedPort = SpikeConnections.WithUnreachablePort(provider);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var dead = SpikeConnections.Create(provider, closedPort);
            await dead.OpenAsync();
            SpikeLog.Write(Experiment, name, "connexion-refusee | AUCUNE ERREUR (inattendu)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            SpikeLog.Write(Experiment, name,
                $"connexion-refusee (port ferme) | {ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)} | " +
                $"duree={sw.ElapsedMilliseconds} ms");
        }

        // État final : aucune écriture ne doit subsister des tentatives expirées.
        var payload = await database.ScalarAsync("SELECT payload FROM timeout_probe WHERE id = 1;");
        SpikeLog.Write(Experiment, name, $"etat final timeout_probe.payload={payload} (attendu 0 : aucune ecriture expiree conservee)");

        Assert.True(true);
    }

    /// <summary>
    /// Exécute une action attendue en échec, puis mesure l'état de la CONNEXION et de la TRANSACTION
    /// après l'erreur — c'est ce qui décide si un retry est seulement envisageable.
    /// </summary>
    private static async Task MeasureAsync(
        SpikeDatabase database,
        string name,
        string label,
        Func<System.Data.Common.DbConnection, System.Data.Common.DbTransaction, Task> action)
    {
        await using var connection = await database.OpenRawAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var sw = Stopwatch.StartNew();
        string outcome;
        try
        {
            await action(connection, transaction);
            outcome = "AUCUNE ERREUR (inattendu)";
        }
        catch (Exception ex)
        {
            outcome = $"{ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)}";
        }
        sw.Stop();

        // La CONNEXION est-elle encore ouverte ?
        var connectionState = connection.State.ToString();

        // La TRANSACTION est-elle encore utilisable ? (PostgreSQL avorte tout après une erreur : 25P02)
        string transactionState;
        try
        {
            await using var probe = connection.CreateCommand();
            probe.Transaction = transaction;
            probe.CommandText = "SELECT 1;";
            await probe.ExecuteScalarAsync();
            transactionState = "UTILISABLE";
        }
        catch (Exception ex)
        {
            transactionState = $"INUTILISABLE — {ErrorFacts.Describe(ex)}";
        }

        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // Sans incidence sur la mesure.
        }

        SpikeLog.Write(Experiment, name,
            $"{label} | {outcome} | duree={sw.ElapsedMilliseconds} ms | connexion={connectionState} | transaction={transactionState}");
    }

    private static async Task Execute(SpikeDatabase database, string sql)
    {
        await using var connection = await database.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
