using System.Diagnostics;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// P4-10 — plusieurs « postes » = plusieurs <b>processus OS</b> (<c>MMV.MultiProcess.Worker</c>), chacun avec sa
/// propre connexion et son propre pool. Départ simultané par barrière serveur : le test tient un verrou consultatif
/// exclusif ; chaque poste attend le même verrou en mode partagé ; le test ne le relâche qu'une fois <b>tous</b> les
/// postes en attente (visible dans <c>pg_locks</c>). Une seconde barrière permet un rendez-vous au milieu d'une
/// transaction (interblocage).
/// </summary>
public static class Workstations
{
    public const long Barrier = 0x4D4D5650_31305354; // « MMVP10ST »
    public const long MiddleBarrier = 0x4D4D5650_31304D44; // « MMVP10MD »

    private static readonly string WorkerPath = LocateWorker();

    /// <summary>Démarre un poste (sans attendre sa fin).</summary>
    public static Process Start(string connectionString, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(WorkerPath);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["MMV_WORKER_CONNECTION_STRING"] = connectionString;
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Poste non démarré.");
    }

    /// <summary>Résultat d'un poste : la ligne <c>RESULT</c>, ou l'erreur complète s'il n'en a pas produit.</summary>
    public static async Task<string> ResultAsync(Process worker, TimeSpan? timeout = null)
    {
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        var output = worker.StandardOutput.ReadToEndAsync(cancel.Token);
        var error = worker.StandardError.ReadToEndAsync(cancel.Token);
        await worker.WaitForExitAsync(cancel.Token);
        var line = (await output).Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal));
        return line?["RESULT ".Length..] ?? $"crash(code {worker.ExitCode}): {await error}";
    }

    /// <summary>
    /// Lance <paramref name="runs"/> postes derrière la barrière, les libère ensemble, puis rend leurs résultats.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RaceAsync(
        string connectionString, IReadOnlyList<string[]> runs, bool middleBarrier = false)
    {
        await using var gate = await HoldAsync(connectionString, Barrier);
        await using var middle = middleBarrier ? await HoldAsync(connectionString, MiddleBarrier) : null;
        var environment = new Dictionary<string, string> { ["MMV_WORKER_BARRIER"] = Barrier.ToString() };
        if (middleBarrier)
        {
            environment["MMV_WORKER_BARRIER2"] = MiddleBarrier.ToString();
        }

        var workers = runs.Select(r => Start(connectionString, environment, r)).ToList();
        try
        {
            await WaitForWaitersAsync(connectionString, Barrier, runs.Count);
            await gate.ReleaseAsync();
            if (middle is not null)
            {
                await WaitForWaitersAsync(connectionString, MiddleBarrier, runs.Count);
                await middle.ReleaseAsync();
            }

            return await Task.WhenAll(workers.Select(w => ResultAsync(w)));
        }
        finally
        {
            foreach (var worker in workers.Where(w => !w.HasExited))
            {
                worker.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>Verrou consultatif exclusif tenu par le test (barrière fermée).</summary>
    public static async Task<HeldLock> HoldAsync(string connectionString, long key)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", connection);
        command.Parameters.AddWithValue("k", key);
        await command.ExecuteNonQueryAsync();
        return new HeldLock(connection, key);
    }

    public static async Task WaitForWaitersAsync(string connectionString, long key, int count)
    {
        var clock = Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        while (true)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND NOT granted " +
                "AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) " +
                "AND classid::bigint = (@k >> 32) AND objid::bigint = (@k & 4294967295)", connection);
            command.Parameters.AddWithValue("k", key);
            if ((long)(await command.ExecuteScalarAsync())! >= count)
            {
                return;
            }

            if (clock.Elapsed > TimeSpan.FromSeconds(90))
            {
                throw new TimeoutException($"{count} poste(s) attendus à la barrière {key:X}.");
            }

            await Task.Delay(50);
        }
    }

    private static string LocateWorker()
    {
        // …/tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/bin/<config>/<tfm>/ → …/tests/MMV.MultiProcess.Worker/bin/<config>/<tfm>/
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = output.Name;
        var configuration = output.Parent!.Name;
        var tests = output.Parent!.Parent!.Parent!.Parent!.FullName;
        var path = Path.Combine(tests, "MMV.MultiProcess.Worker", "bin", configuration, tfm, "MMV.MultiProcess.Worker.dll");
        return File.Exists(path) ? path : throw new FileNotFoundException("Processus poste non construit.", path);
    }

    public sealed class HeldLock(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        private bool _released;

        public async Task ReleaseAsync()
        {
            if (_released)
            {
                return;
            }

            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", connection);
            command.Parameters.AddWithValue("k", key);
            await command.ExecuteNonQueryAsync();
            _released = true;
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
        }
    }
}
