using System.ComponentModel;
using System.Diagnostics;
using MMV.Infrastructure.Configuration;

namespace MMV.DatabaseManager.Backup;

/// <summary>Échec de lancement ou d'exécution d'un outil client PostgreSQL ; message sans secret.</summary>
public sealed class PostgreSqlClientToolException(string message) : Exception(message);

/// <summary>Issue d'un outil client : code de sortie et fin de sa sortie d'erreur, expurgée du secret.</summary>
public sealed record ClientToolResult(int ExitCode, string Diagnostics)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Lancement de <c>pg_dump</c> et <c>pg_restore</c> (P4-9) avec la politique de connexion de D-14 :
/// <list type="bullet">
///   <item><b>aucun shell</b> : exécutable résolu, arguments passés un par un (<see cref="ProcessStartInfo.ArgumentList"/>) —
///   aucune interprétation, aucune injection ;</item>
///   <item><b>aucun secret en argument</b> (liste des processus) : la connexion passe par l'environnement du seul
///   processus enfant ; toutes les variables <c>PG*</c> héritées sont d'abord retirées (aucun <c>PGSERVICE</c>,
///   <c>PGPASSFILE</c> ni <c>PGSSLMODE</c> de l'exploitant ne peut affaiblir la connexion) ;</item>
///   <item><b>TLS <c>verify-full</c></b> (autorité fournie, sinon magasin du système) et <b>SCRAM-SHA-256 seul</b>
///   (<c>PGREQUIREAUTH</c>) ; aucune invite de mot de passe (<c>--no-password</c>) ;</item>
///   <item>la sortie d'erreur rapportée est bornée et le secret, s'il y figurait, est masqué.</item>
/// </list>
/// </summary>
public sealed class PostgreSqlClientTools
{
    private const int MaximumDiagnosticsLength = 2000;

    private readonly string? _binDirectory;

    /// <param name="binDirectory">Dossier des binaires PostgreSQL ; <c>null</c> ⇒ résolution par le <c>PATH</c>.</param>
    public PostgreSqlClientTools(string? binDirectory)
    {
        if (binDirectory is not null && !Path.IsPathFullyQualified(binDirectory))
        {
            throw new ArgumentException("Le dossier des binaires PostgreSQL doit être un chemin absolu.", nameof(binDirectory));
        }

        _binDirectory = binDirectory;
    }

    public string Executable(string tool) => _binDirectory is null
        ? tool
        : Path.Combine(_binDirectory, OperatingSystem.IsWindows() ? tool + ".exe" : tool);

    /// <summary>Environnement de connexion d'un processus enfant ; exposé pour les tests de non-fuite.</summary>
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments, PostgreSqlConnectionSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Validate();

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("PG", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            info.Environment.Remove(key);
        }

        info.Environment["PGHOST"] = target.Host;
        info.Environment["PGPORT"] = target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["PGDATABASE"] = target.Database;
        info.Environment["PGUSER"] = target.Username;
        info.Environment["PGPASSWORD"] = target.Password;
        info.Environment["PGSSLMODE"] = "verify-full";
        info.Environment["PGSSLROOTCERT"] = target.RootCertificatePath ?? "system";
        info.Environment["PGREQUIREAUTH"] = "scram-sha-256";
        info.Environment["PGCONNECT_TIMEOUT"] = PostgreSqlConnectionSecurity.ConnectTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["PGAPPNAME"] = "MMV.DatabaseManager";
        info.Environment["PGCLIENTENCODING"] = "UTF8";
        return info;
    }

    public async Task<ClientToolResult> RunAsync(
        string tool, IEnumerable<string> arguments, PostgreSqlConnectionSettings target, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(Executable(tool), arguments, target) };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new PostgreSqlClientToolException(
                $"{tool} introuvable ou non exécutable ({(_binDirectory is null ? "PATH" : "--pg-bin")}).");
        }

        // Les deux flux sont vidés en parallèle : un flux plein bloquerait l'enfant.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await output;
        return new ClientToolResult(process.ExitCode, Sanitize(await error, target.Password));
    }

    private static string Sanitize(string diagnostics, string secret)
    {
        var text = diagnostics.Replace(secret, "***", StringComparison.Ordinal).Trim();
        return text.Length <= MaximumDiagnosticsLength ? text : "…" + text[^MaximumDiagnosticsLength..];
    }
}
