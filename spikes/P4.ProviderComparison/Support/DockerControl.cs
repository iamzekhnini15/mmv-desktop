using System.Diagnostics;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Pilotage MINIMAL du conteneur JETABLE du spike (E14 : perte de connexion, redémarrage,
/// reconnexion).
///
/// GARDE-FOU DE SÛRETÉ : seul un conteneur dont le nom commence par <c>mmv-p4-</c> peut être
/// arrêté ou redémarré. Toute autre valeur est refusée. Aucune commande destructive n'est
/// exposée : ni <c>rm</c>, ni <c>volume</c>, ni <c>prune</c>. Le nom vient de l'environnement ;
/// sans lui, l'expérimentation est SKIPPÉE plutôt que devinée.
/// </summary>
public static class DockerControl
{
    private const string RequiredPrefix = "mmv-p4-";

    public const string PostgresVariable = "MMV_P4_PG_CONTAINER";
    public const string SqlServerVariable = "MMV_P4_MSSQL_CONTAINER";

    public static string? ContainerFor(ProviderKind provider)
    {
        var raw = Environment.GetEnvironmentVariable(
            provider == ProviderKind.Postgres ? PostgresVariable : SqlServerVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var name = raw.Trim();
        return name.StartsWith(RequiredPrefix, StringComparison.Ordinal) ? name : null;
    }

    public static string SkipReason(ProviderKind provider)
    {
        var variable = provider == ProviderKind.Postgres ? PostgresVariable : SqlServerVariable;
        return $"{variable} non definie ou nom de conteneur hors prefixe '{RequiredPrefix}' — " +
               "experimentation de panne NON EXECUTEE (aucun conteneur devine).";
    }

    public static Task<string> StopAsync(string container) => RunAsync("stop", container);

    public static Task<string> StartAsync(string container) => RunAsync("start", container);

    private static async Task<string> RunAsync(string verb, string container)
    {
        if (!container.StartsWith(RequiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refus : '{container}' ne porte pas le prefixe obligatoire '{RequiredPrefix}'.");
        }

        if (verb is not ("stop" or "start"))
        {
            throw new InvalidOperationException($"Verbe docker non autorise : '{verb}'.");
        }

        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(verb);
        info.ArgumentList.Add(container);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Impossible de demarrer le client docker.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? $"docker {verb} OK ({stdout.Trim()})"
            : $"docker {verb} ECHEC (code={process.ExitCode}) {stderr.Trim()}";
    }

    /// <summary>
    /// Attend que le serveur réponde de nouveau, en bornant strictement l'attente. Retourne la
    /// durée observée, ou <c>null</c> si le serveur n'est pas revenu dans le délai.
    /// </summary>
    public static async Task<TimeSpan?> WaitUntilReachableAsync(ProviderKind provider, TimeSpan limit)
    {
        var connectionString = SpikeConnections.WithShortConnectTimeout(provider, 3);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < limit)
        {
            SpikeConnections.ClearPools(provider);
            try
            {
                await using var connection = SpikeConnections.Create(provider, connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                stopwatch.Stop();
                return stopwatch.Elapsed;
            }
            catch
            {
                // Serveur pas encore revenu : nouvelle tentative bornée par `limit`.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }

        return null;
    }
}
