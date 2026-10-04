using System.Data.Common;
using System.Text.Json;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Journal;

/// <summary>Nature d'une exécution : <c>migrate</c> ou <c>adopt</c> (une adoption sert d'ancre sans modifier le schéma).</summary>
public enum MigrationRunKind
{
    Migrate,
    Adopt
}

/// <summary>Issue d'une exécution : <c>open</c> tant qu'elle n'est pas close — une ligne restée <c>open</c> prouve l'échec.</summary>
public enum MigrationOutcome
{
    Open,
    Success,
    Failure
}

/// <summary>Ouverture d'une exécution dans le journal serveur.</summary>
public sealed record ServerMigrationRun(
    Guid RunId,
    string ApplicationVersion,
    IReadOnlyList<string> AppliedBefore,
    string OperatorReference,
    MigrationRunKind Kind,
    string BackupReference);

/// <summary>Ligne du journal telle que lue pour le calcul du minimum supporté (§4.3).</summary>
public sealed record MigrationRunRecord(
    string ApplicationVersion,
    MigrationRunKind Kind,
    MigrationOutcome Outcome,
    IReadOnlyList<string> AppliedBefore,
    IReadOnlyList<string>? AppliedAfter);

/// <summary>Port du journal serveur autoritatif (DP-8).</summary>
public interface IServerMigrationJournal
{
    Task OpenAsync(ServerMigrationRun run, CancellationToken cancellationToken = default);

    Task CloseAsync(Guid runId, MigrationOutcome outcome, string? failureCause,
        IReadOnlyList<string> appliedAfter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exécutions antérieures, <b>toutes issues</b> (succès, échec, plantage), dans l'ordre du journal
    /// (<c>started_at</c>), sans <paramref name="currentRunId"/> : elles désignent la release qui a physiquement
    /// produit chaque migration (§4.3 amendé).
    /// </summary>
    Task<IReadOnlyList<MigrationRunRecord>> ReadRunsAsync(Guid currentRunId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Journal serveur <c>mmv_meta.migration_run</c> (P4-6B, DP-8, H8 ; §4.2 du plan). Deux écritures par
/// exécution, <b>hors</b> transaction de migration (DP-8.3) — sur la connexion de contrôle, distincte de celle
/// qui migre : <c>INSERT</c> à l'ouverture (<c>outcome = 'open'</c>), <c>UPDATE</c> de résultat à la clôture.
/// Horodatages par <c>now()</c> <b>serveur</b> et rôle par <c>current_user</c>, jamais l'horloge ni l'identité
/// du poste (ADR-004, T9). Le rôle applicatif n'a aucun droit sur cette table.
/// </summary>
public sealed class ServerMigrationJournal : IServerMigrationJournal
{
    private const string Table = ServerCompatibilityMetadataNames.QualifiedJournalTable;

    private readonly DbConnection _connection;

    /// <param name="connection">Connexion de contrôle, ouverte, tenant le verrou de session.</param>
    public ServerMigrationJournal(DbConnection connection) =>
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public async Task OpenAsync(ServerMigrationRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.ApplicationVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.OperatorReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.BackupReference);
        ArgumentNullException.ThrowIfNull(run.AppliedBefore);

        await ServerCommand.NonQueryAsync(_connection,
            "INSERT INTO " + Table + " (run_id, app_version, applied_before, applied_after, started_at, " +
            "finished_at, operator_role, operator_reference, run_kind, outcome, failure_cause, backup_reference) " +
            "VALUES (@run_id, @app_version, CAST(@applied_before AS jsonb), NULL, now(), NULL, current_user, " +
            "@operator_reference, @run_kind, 'open', NULL, @backup_reference)",
            cancellationToken,
            ("run_id", run.RunId),
            ("app_version", run.ApplicationVersion),
            ("applied_before", JsonSerializer.Serialize(run.AppliedBefore)),
            ("operator_reference", run.OperatorReference),
            ("run_kind", ToText(run.Kind)),
            ("backup_reference", run.BackupReference));
    }

    public async Task CloseAsync(Guid runId, MigrationOutcome outcome, string? failureCause,
        IReadOnlyList<string> appliedAfter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appliedAfter);
        if (outcome == MigrationOutcome.Open)
        {
            throw new ArgumentException("Une exécution se clôt en succès ou en échec.", nameof(outcome));
        }

        if (outcome == MigrationOutcome.Failure && string.IsNullOrWhiteSpace(failureCause))
        {
            throw new ArgumentException("Un échec est journalisé avec sa cause.", nameof(failureCause));
        }

        var rows = await ServerCommand.NonQueryAsync(_connection,
            "UPDATE " + Table + " SET applied_after = CAST(@applied_after AS jsonb), finished_at = now(), " +
            "outcome = @outcome, failure_cause = @failure_cause WHERE run_id = @run_id AND outcome = 'open'",
            cancellationToken,
            ("applied_after", JsonSerializer.Serialize(appliedAfter)),
            ("outcome", ToText(outcome)),
            ("failure_cause", outcome == MigrationOutcome.Failure ? failureCause : null),
            ("run_id", runId));

        if (rows != 1)
        {
            throw new InvalidOperationException($"Exécution {runId} introuvable ou déjà close dans le journal serveur.");
        }
    }

    public async Task<IReadOnlyList<MigrationRunRecord>> ReadRunsAsync(Guid currentRunId, CancellationToken cancellationToken = default)
    {
        await using var command = ServerCommand.Create(_connection,
            "SELECT app_version, run_kind, outcome, applied_before::text, applied_after::text FROM " + Table +
            " WHERE run_id <> @current_run ORDER BY started_at, run_id",
            ("current_run", currentRunId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var runs = new List<MigrationRunRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new MigrationRunRecord(
                reader.GetString(0),
                ParseKind(reader.GetString(1)),
                ParseOutcome(reader.GetString(2)),
                ParseList(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseList(reader.GetString(4))));
        }

        return runs;
    }

    internal static string ToText(MigrationRunKind kind) => kind switch
    {
        MigrationRunKind.Migrate => "migrate",
        MigrationRunKind.Adopt => "adopt",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static string ToText(MigrationOutcome outcome) => outcome switch
    {
        MigrationOutcome.Open => "open",
        MigrationOutcome.Success => "success",
        MigrationOutcome.Failure => "failure",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static MigrationRunKind ParseKind(string value) => value switch
    {
        "migrate" => MigrationRunKind.Migrate,
        "adopt" => MigrationRunKind.Adopt,
        _ => throw new FormatException($"run_kind inconnu : '{value}'.")
    };

    private static MigrationOutcome ParseOutcome(string value) => value switch
    {
        "open" => MigrationOutcome.Open,
        "success" => MigrationOutcome.Success,
        "failure" => MigrationOutcome.Failure,
        _ => throw new FormatException($"outcome inconnu : '{value}'.")
    };

    private static IReadOnlyList<string> ParseList(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? throw new FormatException("Liste de migrations illisible.");
}
