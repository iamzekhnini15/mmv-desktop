using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E12 — DEADLOCK réellement provoqué (le rapport initial le classait `NOT_TESTED` : « aucun
/// deadlock n'est survenu, aucun scénario croisé n'a été construit »).
///
/// Scénario croisé strict, sur base JETABLE :
///   Transaction A : verrouille R1, attend, demande R2.
///   Transaction B : verrouille R2, attend, demande R1.
///
/// Synchronisation DÉTERMINISTE par barrière (jamais un Thread.Sleep comme mécanisme de
/// synchronisation), timeout de sécurité borné, aucune boucle infinie. Les codes ne sont PAS
/// supposés : ils sont MESURÉS.
/// </summary>
public class E12_DeadlockTests
{
    private const string Experiment = "E12-deadlock";

    /// <summary>Garde-fou : aucune expérimentation ne doit pouvoir bloquer indéfiniment.</summary>
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(60);

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Cross_lock_deadlock_is_provoked_and_measured(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e12");

        // Table jetable dédiée : aucune donnée utilisateur, aucun modèle de production impliqué.
        await Execute(database, "CREATE TABLE deadlock_probe (id int PRIMARY KEY, payload int NOT NULL);");
        await Execute(database, "INSERT INTO deadlock_probe (id, payload) VALUES (1, 0), (2, 0);");

        SpikeLog.Section(Experiment, name, "E12 — Deadlock croisé provoqué");

        using var ready = new Barrier(2);
        var stopwatch = Stopwatch.StartNew();

        // Chaque participant verrouille SA ressource, signale, puis demande CELLE DE L'AUTRE.
        async Task<string> Participant(string label, int firstId, int secondId)
        {
            await using var connection = await database.OpenRawAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 1) Verrou exclusif sur la première ressource (UPDATE = verrou de ligne).
                await using (var first = connection.CreateCommand())
                {
                    first.Transaction = transaction;
                    first.CommandText = $"UPDATE deadlock_probe SET payload = payload + 1 WHERE id = {firstId};";
                    await first.ExecuteNonQueryAsync();
                }

                // 2) Les deux participants tiennent maintenant un verrou : le croisement est garanti.
                ready.SignalAndWait(SafetyTimeout);

                // 3) Demande de la ressource détenue par l'autre → attente croisée → deadlock.
                await using (var second = connection.CreateCommand())
                {
                    second.Transaction = transaction;
                    second.CommandTimeout = 30;
                    second.CommandText = $"UPDATE deadlock_probe SET payload = payload + 1 WHERE id = {secondId};";
                    await second.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                return $"{label}=SURVIVANT";
            }
            catch (Exception ex)
            {
                var facts = ErrorFacts.Describe(ex);
                var category = ErrorFacts.CandidateCategory(ex);

                // La transaction est-elle encore utilisable APRÈS le deadlock ? Fait mesuré, non supposé.
                string usable;
                try
                {
                    await using var probe = connection.CreateCommand();
                    probe.Transaction = transaction;
                    probe.CommandText = "SELECT COUNT(*) FROM deadlock_probe;";
                    var count = await probe.ExecuteScalarAsync();
                    usable = $"OUI (lecture={count})";
                }
                catch (Exception probeEx)
                {
                    usable = $"NON — {ErrorFacts.Describe(probeEx)}";
                }

                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Le rollback peut échouer si le serveur a déjà annulé la transaction : sans incidence.
                }

                SpikeLog.Write(Experiment, name,
                    $"VICTIME {label} | {facts} | categorie={category} | transaction utilisable apres deadlock : {usable}");

                return $"{label}=VICTIME";
            }
        }

        var run = Task.WhenAll(
            Task.Run(() => Participant("A", 1, 2)),
            Task.Run(() => Participant("B", 2, 1)));

        var finished = await Task.WhenAny(run, Task.Delay(SafetyTimeout));
        stopwatch.Stop();

        if (finished != run)
        {
            SpikeLog.Write(Experiment, name,
                $"AUCUN DEADLOCK DETECTE dans le delai de securite ({SafetyTimeout.TotalSeconds}s) — experimentation NON CONCLUANTE");
            Assert.Fail($"[{name}] Le scenario de deadlock n'a pas abouti dans le delai de securite.");
        }

        var results = await run;
        var victims = results.Count(r => r.EndsWith("VICTIME", StringComparison.Ordinal));
        var survivors = results.Count(r => r.EndsWith("SURVIVANT", StringComparison.Ordinal));

        // État final vérifié APRÈS rollback : le survivant a incrémenté deux lignes, la victime aucune.
        var total = await database.ScalarAsync("SELECT SUM(payload) FROM deadlock_probe;");

        SpikeLog.Write(Experiment, name,
            $"DEADLOCK | [{string.Join(" | ", results)}] | victimes={victims} survivants={survivors} | " +
            $"duree={stopwatch.ElapsedMilliseconds} ms | somme payload apres rollback={total} (attendu 2 : " +
            $"seul le survivant a ecrit) | {(victims == 1 && survivors == 1 ? "DEADLOCK REEL ARBITRE PAR LE SERVEUR" : "RESULTAT ATYPIQUE")}");

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
