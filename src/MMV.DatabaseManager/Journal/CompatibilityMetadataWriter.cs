using System.Data.Common;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Journal;

/// <summary>
/// Versions écrites dans la métadonnée : <b>constat</b> calculé depuis le journal, jamais une saisie (H17). Le
/// constructeur n'est pas public : seul <see cref="CompatibilityMetadataWriter.Compute"/> en produit.
/// </summary>
public sealed record CompatibilityVersions
{
    internal CompatibilityVersions(string schemaVersion, string minimumSupportedVersion)
    {
        SchemaVersion = schemaVersion;
        MinimumSupportedVersion = minimumSupportedVersion;
    }

    /// <summary>Release qui a produit le schéma présent (N).</summary>
    public string SchemaVersion { get; }

    /// <summary>Release qui a produit le schéma immédiatement précédent (N-1), ou N pour le premier schéma.</summary>
    public string MinimumSupportedVersion { get; }
}

/// <summary>Port d'accès à <c>mmv_meta.schema_compatibility</c> côté outil.</summary>
public interface ICompatibilityMetadataWriter
{
    /// <summary>Lecture seule de la métadonnée (étape 3).</summary>
    Task<ServerCompatibilityMetadata> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Verdict de la garde de compatibilité pour une version de poste (diagnostic <c>status</c>).</summary>
    Task<ServerSchemaVerdict> EvaluateAsync(
        IEnumerable<string> known, string applicationVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Âge du marqueur de maintenance selon l'horloge du <b>serveur</b> (<c>now()</c>) — information de
    /// diagnostic, jamais une durée d'expiration ; <c>null</c> sans marqueur ou sans métadonnée.
    /// </summary>
    Task<TimeSpan?> MaintenanceAgeAsync(CancellationToken cancellationToken = default);

    /// <summary>DDL idempotent du schéma et des deux tables hors modèle EF (étape 4) — aucune ligne insérée.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Nettoyage, <b>sous verrou</b>, d'un marqueur laissé par une exécution morte (étape 5) ; rend l'ancienne valeur.</summary>
    Task<DateTimeOffset?> ClearStaleMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Pose <c>maintenance_started_at = now()</c> ; base sans ligne : ligne en état d'initialisation (étape 6).</summary>
    Task BeginMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Calcule et écrit les versions depuis le journal (étape 10a) ; <c>null</c> si rien à écrire.</summary>
    Task<CompatibilityVersions?> AdvanceAsync(
        Guid runId, IReadOnlyList<MigrationRunRecord> runs, CancellationToken cancellationToken = default);

    /// <summary>Remet la maintenance à <c>NULL</c>, sauf ligne en état d'initialisation (étape 11).</summary>
    Task EndMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Écriture de la métadonnée de compatibilité (P4-6B, DP-3.5, H17 ; §4.3 du plan), en SQL brut sur la
/// connexion de contrôle — <b>hors modèle EF</b> : aucune entité, aucune migration (H16). Le DDL est la voie
/// sanctionnée par DP-3.5.1 / DP-8.1 (précédent : <c>__EFMigrationsHistory</c>), exécutée sous verrou.
/// </summary>
public sealed class CompatibilityMetadataWriter : ICompatibilityMetadataWriter
{
    private const string Compatibility = ServerCompatibilityMetadataNames.QualifiedCompatibilityTable;

    private static readonly string[] SchemaDdl =
    [
        "CREATE SCHEMA IF NOT EXISTS " + ServerCompatibilityMetadataNames.Schema,
        "CREATE TABLE IF NOT EXISTS " + ServerCompatibilityMetadataNames.QualifiedJournalTable + " (" +
        "run_id uuid PRIMARY KEY, " +
        "app_version text NOT NULL, " +
        "applied_before jsonb NOT NULL, " +
        "applied_after jsonb NULL, " +
        "started_at timestamptz NOT NULL, " +
        "finished_at timestamptz NULL, " +
        "operator_role text NOT NULL, " +
        "operator_reference text NOT NULL, " +
        "run_kind text NOT NULL CHECK (run_kind IN ('migrate', 'adopt')), " +
        "outcome text NOT NULL CHECK (outcome IN ('open', 'success', 'failure')), " +
        "failure_cause text NULL, " +
        "backup_reference text NOT NULL)",
        "CREATE TABLE IF NOT EXISTS " + Compatibility + " (" +
        "id integer PRIMARY KEY CHECK (id = 1), " +
        "schema_version text NULL, " +
        "minimum_supported_version text NULL, " +
        "maintenance_started_at timestamptz NULL, " +
        "updated_at timestamptz NOT NULL, " +
        "updated_by_run uuid NOT NULL, " +
        "CHECK ((schema_version IS NULL) = (minimum_supported_version IS NULL)))"
    ];

    private readonly DbConnection _connection;

    /// <param name="connection">Connexion de contrôle, ouverte, tenant le verrou de session.</param>
    public CompatibilityMetadataWriter(DbConnection connection) =>
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Règle du §4.3, <b>amendée par décision d'architecte du 04/10/2026</b>, dérivée <b>uniquement</b> du journal.
    /// <list type="number">
    ///   <item>Seul un état <b>vérifié</b> est une ancre : la dernière exécution de <paramref name="runs"/> (l'exécution
    ///     courante) doit être un succès ; sinon <c>null</c>.</item>
    ///   <item>Une exécution <c>adopt</c> réussie est une ancre explicite et un point de départ : rien d'antérieur ne
    ///     compte, et elle est la productrice de tout l'état qu'elle adopte.</item>
    ///   <item>L'état courant (Appliquées après) est comparé à celui de la <b>dernière ancre</b> — les Appliquées
    ///     après du dernier succès antérieur. Identiques ⇒ <c>null</c> : aucune évolution de compatibilité, une
    ///     release sans migration ne consomme aucun cran.</item>
    ///   <item>Différents ⇒ nouvelle ancre. Chaque migration de l'état est attribuée à la release qui l'a
    ///     <b>physiquement</b> appliquée : la dernière exécution, quelle que soit son issue (échec et plantage
    ///     compris), dont les Appliquées avant ne la contenaient pas — le verrou sérialise le journal. Les versions
    ///     productrices, dans l'ordre de la chaîne, consécutives identiques fusionnées : N = la dernière,
    ///     N-1 = l'avant-dernière (ou N si elle est seule).</item>
    /// </list>
    /// Jamais la version courante par défaut : une relance sans migration par une release plus récente ne s'attribue
    /// pas le schéma. Une migration sans exécution productrice dans le journal est refusée, jamais devinée.
    /// </summary>
    /// <param name="runs">Exécutions dans l'ordre du journal, toutes issues, exécution courante en dernier.</param>
    /// <exception cref="InvalidOperationException">Si une migration de l'état n'a aucune exécution productrice.</exception>
    public static CompatibilityVersions? Compute(IEnumerable<MigrationRunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var journal = runs.ToList();
        if (journal.Count == 0 || journal[^1] is not { Outcome: MigrationOutcome.Success, AppliedAfter: { } state })
        {
            return null;
        }

        var lastAdopt = journal.FindLastIndex(r => r is { Kind: MigrationRunKind.Adopt, Outcome: MigrationOutcome.Success });
        if (lastAdopt > 0)
        {
            journal.RemoveRange(0, lastAdopt);
        }

        var adopted = journal[0] is { Kind: MigrationRunKind.Adopt, Outcome: MigrationOutcome.Success, AppliedAfter: { } adoptedState }
            ? (Version: journal[0].ApplicationVersion, State: adoptedState)
            : default;

        var lastAnchorState = journal.Take(journal.Count - 1)
            .LastOrDefault(r => r is { Outcome: MigrationOutcome.Success, AppliedAfter: not null })?.AppliedAfter;
        if (journal[^1].Kind == MigrationRunKind.Migrate && lastAnchorState is not null
            && lastAnchorState.SequenceEqual(state, StringComparer.Ordinal))
        {
            return null;
        }

        var versions = new List<string>();
        foreach (var migration in state)
        {
            var producer = adopted.State is not null && adopted.State.Contains(migration, StringComparer.Ordinal)
                ? adopted.Version
                : journal.LastOrDefault(r => !r.AppliedBefore.Contains(migration, StringComparer.Ordinal))?.ApplicationVersion
                  ?? throw new InvalidOperationException(
                      $"Migration {migration} appliquée hors de toute exécution journalisée : provenance inconnue.");

            if (versions.Count == 0 || versions[^1] != producer)
            {
                versions.Add(producer);
            }
        }

        return versions.Count switch
        {
            0 => null,
            1 => new CompatibilityVersions(versions[0], versions[0]),
            _ => new CompatibilityVersions(versions[^1], versions[^2])
        };
    }

    public Task<ServerCompatibilityMetadata> ReadAsync(CancellationToken cancellationToken = default) =>
        ServerCompatibilityMetadataReader.ReadAsync(ServerCommand.RequireOpen(_connection), cancellationToken);

    public Task<ServerSchemaVerdict> EvaluateAsync(
        IEnumerable<string> known, string applicationVersion, CancellationToken cancellationToken = default) =>
        ServerSchemaCompatibilityGuard.EvaluateAsync(
            ServerCommand.RequireOpen(_connection), known, applicationVersion, cancellationToken);

    public async Task<TimeSpan?> MaintenanceAgeAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await ReadAsync(cancellationToken);
        if (metadata.MaintenanceStartedAt is null)
        {
            return null;
        }

        var seconds = await ServerCommand.ScalarAsync(_connection,
            "SELECT extract(epoch FROM now() - maintenance_started_at)::float8 FROM " + Compatibility + " WHERE id = 1",
            cancellationToken);
        return seconds is double value ? TimeSpan.FromSeconds(Math.Round(value)) : null;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        foreach (var statement in SchemaDdl)
        {
            await ServerCommand.NonQueryAsync(_connection, statement, cancellationToken);
        }
    }

    public async Task<DateTimeOffset?> ClearStaleMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var previous = await ServerCommand.ScalarAsync(_connection,
            "SELECT maintenance_started_at FROM " + Compatibility + " WHERE id = 1", cancellationToken);
        if (previous is null or DBNull)
        {
            return null;
        }

        await ServerCommand.NonQueryAsync(_connection,
            "UPDATE " + Compatibility + " SET maintenance_started_at = NULL, updated_at = now(), " +
            "updated_by_run = @run_id WHERE id = 1",
            cancellationToken, ("run_id", runId));

        return previous switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"maintenance_started_at : type inattendu {previous.GetType().Name}.")
        };
    }

    public Task BeginMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default) =>
        ServerCommand.NonQueryAsync(_connection,
            "INSERT INTO " + Compatibility + " (id, schema_version, minimum_supported_version, " +
            "maintenance_started_at, updated_at, updated_by_run) VALUES (1, NULL, NULL, now(), now(), @run_id) " +
            "ON CONFLICT (id) DO UPDATE SET maintenance_started_at = now(), updated_at = now(), " +
            "updated_by_run = @run_id",
            cancellationToken, ("run_id", runId));

    public async Task<CompatibilityVersions?> AdvanceAsync(
        Guid runId, IReadOnlyList<MigrationRunRecord> runs, CancellationToken cancellationToken = default)
    {
        var versions = Compute(runs);
        if (versions is null)
        {
            return null;
        }

        var rows = await ServerCommand.NonQueryAsync(_connection,
            "UPDATE " + Compatibility + " SET schema_version = @schema_version, " +
            "minimum_supported_version = @minimum_supported_version, updated_at = now(), " +
            "updated_by_run = @run_id WHERE id = 1",
            cancellationToken,
            ("schema_version", versions.SchemaVersion),
            ("minimum_supported_version", versions.MinimumSupportedVersion),
            ("run_id", runId));

        if (rows != 1)
        {
            throw new InvalidOperationException("Ligne de compatibilité absente : la métadonnée n'a pas pu avancer.");
        }

        return versions;
    }

    public Task EndMaintenanceAsync(Guid runId, CancellationToken cancellationToken = default) =>
        ServerCommand.NonQueryAsync(_connection,
            "UPDATE " + Compatibility + " SET maintenance_started_at = NULL, updated_at = now(), " +
            "updated_by_run = @run_id WHERE id = 1 AND schema_version IS NOT NULL",
            cancellationToken, ("run_id", runId));
}
