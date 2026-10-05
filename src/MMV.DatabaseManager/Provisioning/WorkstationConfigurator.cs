using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>
/// Configuration d'un poste (P4-8, D-13) : <b>vérifie d'abord</b>, n'écrit qu'ensuite. La connexion doit
/// s'établir en TLS VerifyFull et SCRAM-SHA-256, et l'identité doit être <b>non privilégiée</b>
/// (<see cref="PostgreSqlConnectivityProbe"/>) — le migrateur ou l'administrateur ne sont jamais enregistrés sur un
/// poste (DP-5). Le fichier est protégé par DPAPI CurrentUser : l'outil doit donc être lancé <b>par l'utilisateur
/// Windows du poste</b>. Relancer la commande remplace le fichier (rotation du secret).
/// </summary>
public sealed class WorkstationConfigurator
{
    private readonly WorkstationDatabaseSettingsFile _file;
    private readonly IMigrationJournal _trace;
    private readonly string _verb;

    /// <param name="file">Fichier protégé à écrire.</param>
    /// <param name="trace">Trace locale.</param>
    /// <param name="verb">Verbe tracé : <c>configure-workstation</c>, ou <c>configure-backup</c> (P4-9, même contrôle).</param>
    public WorkstationConfigurator(WorkstationDatabaseSettingsFile file, IMigrationJournal trace, string verb = "configure-workstation")
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _verb = verb;
    }

    public async Task<AdministrationResult> RunAsync(PostgreSqlConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string connectionString;
        try
        {
            connectionString = PostgreSqlConnectionSecurity.BuildConnectionString(settings);
        }
        catch (DatabaseConfigurationException exception)
        {
            return Finish(MigrationExitCode.InvalidArguments, exception.Message);
        }

        try
        {
            await PostgreSqlConnectivityProbe.EnsureAvailableAsync(connectionString, cancellationToken);
        }
        catch (DatabaseUnavailableException exception)
        {
            return Finish(exception.Reason == DatabaseUnavailableReason.PrivilegedIdentity
                ? MigrationExitCode.SecurityRefused
                : MigrationExitCode.ServerUnreachable, exception.Message + " Aucune configuration n'a été écrite.");
        }

        _file.Save(settings);
        return Finish(MigrationExitCode.Success, $"Connexion enregistrée : {settings} — fichier protégé {_file.Path}.");
    }

    private AdministrationResult Finish(MigrationExitCode code, string message)
    {
        _trace.Write($"{_verb} : {(code == MigrationExitCode.Success ? "succès" : $"échec code {(int)code}")} — {message}");
        return new AdministrationResult(code, message);
    }
}
