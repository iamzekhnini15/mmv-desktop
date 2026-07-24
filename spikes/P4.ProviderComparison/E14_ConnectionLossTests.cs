using System.Diagnostics;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E14 — PERTE DE CONNEXION EN TRANSACTION, ARRÊT/REDÉMARRAGE SERVEUR et RECONNEXION.
/// Le rapport initial classait ces cas `NOT_TESTED` (« non scénarisé »).
///
/// Toutes les manipulations portent sur le conteneur JETABLE du spike (préfixe obligatoire
/// <c>mmv-p4-</c>, cf. <see cref="DockerControl"/>). Aucune donnée utilisateur, aucun service
/// Windows, aucune instance native ne sont touchés. Chaque attente est bornée.
/// </summary>
[Collection(SpikeSerialCollection.Name)]
public class E14_ConnectionLossTests
{
    private const string Experiment = "E14-connection-loss";

    private static readonly TimeSpan RecoveryLimit = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Connection_loss_restart_and_reconnect_are_measured(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var container = DockerControl.ContainerFor(provider);
        Skip.If(container is null, DockerControl.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e14");

        await Execute(database, "CREATE TABLE loss_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await Execute(database, "INSERT INTO loss_probe (id, payload) VALUES (1, 100);");

        SpikeLog.Section(Experiment, name, "E14 — Perte de connexion, arret/redemarrage, reconnexion");

        // =========================================================================================
        // Scenario A — perte de connexion EN PLEINE TRANSACTION
        // 1) transaction ouverte  2) 1re ecriture  3) arret brutal  4) 2e ecriture  5) observation
        // =========================================================================================
        string firstWrite = "non atteint", secondWrite = "non atteint";

        await using (var connection = await database.OpenRawAsync())
        {
            var transaction = await connection.BeginTransactionAsync();

            await using (var write = connection.CreateCommand())
            {
                write.Transaction = transaction;
                write.CommandText = "UPDATE loss_probe SET payload = 777 WHERE id = 1;";
                var rows = await write.ExecuteNonQueryAsync();
                firstWrite = $"{rows} ligne(s) ecrite(s), NON COMMITEE";
            }

            // Arret BRUTAL du serveur pendant que la transaction est ouverte et non commitee.
            var stopped = await DockerControl.StopAsync(container!);
            SpikeLog.Write(Experiment, name, $"arret du conteneur pendant la transaction : {stopped}");

            try
            {
                await using var write2 = connection.CreateCommand();
                write2.Transaction = transaction;
                write2.CommandTimeout = 15;
                write2.CommandText = "UPDATE loss_probe SET payload = 888 WHERE id = 1;";
                await write2.ExecuteNonQueryAsync();
                secondWrite = "AUCUNE ERREUR (inattendu)";
            }
            catch (Exception ex)
            {
                secondWrite = $"{ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)}";
            }

            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // Le serveur est arrete : le rollback client ne peut pas aboutir. Sans incidence :
                // c'est le SERVEUR qui annulera la transaction non commitee au redemarrage.
            }
        }

        SpikeLog.Write(Experiment, name, $"1re ecriture (avant coupure) : {firstWrite}");
        SpikeLog.Write(Experiment, name, $"2e ecriture (apres coupure)  : {secondWrite}");

        // Redemarrage et verification de l'etat REEL apres reprise.
        var restarted = await DockerControl.StartAsync(container!);
        var recovery = await DockerControl.WaitUntilReachableAsync(provider, RecoveryLimit);
        SpikeLog.Write(Experiment, name,
            $"redemarrage : {restarted} | serveur de nouveau joignable apres {(recovery is null ? "JAMAIS (limite atteinte)" : $"{recovery.Value.TotalSeconds:F1} s")}");

        Assert.True(recovery is not null, $"[{name}] Le serveur n'est pas revenu dans la limite de {RecoveryLimit.TotalMinutes} min.");

        var afterRestart = await database.ScalarAsync("SELECT payload FROM loss_probe WHERE id = 1;");
        SpikeLog.Write(Experiment, name,
            $"ETAT FINAL apres redemarrage : payload={afterRestart} (100 = transaction ANNULEE ; 777 = 1re ecriture VALIDEE) | " +
            $"{(afterRestart == "100" ? "TRANSACTION NON COMMITEE CORRECTEMENT ANNULEE" : "ECRITURE PARTIELLE CONSERVEE — ANOMALIE")}");

        // =========================================================================================
        // Scenario B — arret/redemarrage et RECONNEXION (pool non purge, puis purge)
        // =========================================================================================
        var before = await database.ScalarAsync("SELECT payload FROM loss_probe WHERE id = 1;");

        var stopped2 = await DockerControl.StopAsync(container!);
        SpikeLog.Write(Experiment, name, $"scenario B — arret : {stopped2}");

        // Echec attendu pendant l'arret.
        var shortString = SpikeConnections.WithShortConnectTimeout(provider, 5);
        var sw = Stopwatch.StartNew();
        string duringOutage;
        try
        {
            await using var dead = SpikeConnections.Create(provider, shortString);
            await dead.OpenAsync();
            duringOutage = "AUCUNE ERREUR (inattendu — le serveur est arrete)";
        }
        catch (Exception ex)
        {
            duringOutage = $"{ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)} | duree={sw.ElapsedMilliseconds} ms";
        }
        SpikeLog.Write(Experiment, name, $"connexion PENDANT l'arret : {duringOutage}");

        var restarted2 = await DockerControl.StartAsync(container!);
        SpikeLog.Write(Experiment, name, $"scenario B — redemarrage : {restarted2}");

        // (a) Tentative SANS purge du pool : une connexion morte peut etre rendue par le pool.
        //     Mesuree AVANT toute purge, immediatement apres le redemarrage du conteneur.
        string withoutPurge;
        var swNoPurge = Stopwatch.StartNew();
        try
        {
            await using var pooled = SpikeConnections.Create(provider, shortString);
            await pooled.OpenAsync();
            await using var command = pooled.CreateCommand();
            command.CommandText = "SELECT payload FROM loss_probe WHERE id = 1;";
            var value = await command.ExecuteScalarAsync();
            withoutPurge = $"REUSSITE immediate (valeur={value}) en {swNoPurge.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            withoutPurge = $"ECHEC — {ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)} | duree={swNoPurge.ElapsedMilliseconds} ms";
        }
        SpikeLog.Write(Experiment, name, $"reconnexion SANS purge du pool : {withoutPurge}");

        // (b) Purge du pool puis reprise bornee.
        var recovery2 = await DockerControl.WaitUntilReachableAsync(provider, RecoveryLimit);
        Assert.True(recovery2 is not null, $"[{name}] Le serveur n'est pas revenu dans la limite de {RecoveryLimit.TotalMinutes} min.");

        var after = await database.ScalarAsync("SELECT payload FROM loss_probe WHERE id = 1;");
        SpikeLog.Write(Experiment, name,
            $"reconnexion APRES purge du pool : REUSSITE en {recovery2!.Value.TotalSeconds:F1} s | " +
            $"valeur avant={before} valeur apres={after} | " +
            $"{(before == after ? "COHERENCE PRESERVEE" : "INCOHERENCE APRES REDEMARRAGE")}");

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
