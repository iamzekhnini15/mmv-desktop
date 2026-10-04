using MMV.Infrastructure.Configuration;

namespace MMV.Infrastructure.Data;

/// <summary>
/// États d'une base serveur vue par un poste (P4-6B, ADR-PROD-DB-009 DP-3 §5.2.3.2, §5.3 du plan).
/// E6 (serveur injoignable) n'est pas un état rendu : c'est une exception, jamais avalée (K-13).
/// </summary>
public enum ServerSchemaState
{
    /// <summary>Appliquées = Connues : démarrage normal.</summary>
    E1,

    /// <summary>Le poste connaît des migrations non appliquées : la base doit être mise à jour par l'opérateur.</summary>
    E2,

    /// <summary>Base en avance d'un cran admis (version ≥ minimum supporté) : démarrage normal, en fenêtre.</summary>
    E3a,

    /// <summary>Base en avance au-delà de la fenêtre : ce poste doit être mis à jour.</summary>
    E3b,

    /// <summary>Chaînes divergentes : blocage inconditionnel.</summary>
    E3c,

    /// <summary>Base vide : la baseline appartient à l'outil (DP-1).</summary>
    E4,

    /// <summary>Maintenance en cours, ou installation de la base inachevée (§4.3.1).</summary>
    E5,

    /// <summary>Tables applicatives sans historique EF : l'adoption est un chemin d'écriture retiré aux postes.</summary>
    E7
}

/// <summary>Les cinq cas de DP-3 (§5.2 du plan).</summary>
public enum CompatibilityCase
{
    C1,
    C2,
    C3,
    C4,
    C5
}

/// <summary>Disponibilité de la métadonnée de compatibilité.</summary>
public enum ServerMetadataStatus
{
    /// <summary>Schéma ou table absents.</summary>
    Absent,

    /// <summary>Table présente, sans ligne.</summary>
    NoRow,

    /// <summary>Ligne présente et lisible (y compris en état d'initialisation).</summary>
    Present,

    /// <summary>Ligne présente mais illisible : version non SemVer, ou contrainte « nulles ensemble » violée.</summary>
    Unreadable
}

/// <summary>
/// Ligne <c>mmv_meta.schema_compatibility</c> telle que lue par un poste. <see cref="MaintenanceStartedAt"/>
/// est un <b>signal d'admission à sens unique</b> : il peut empêcher un démarrage, jamais l'autoriser ; ce
/// n'est ni un verrou, ni une autorité de compatibilité.
/// </summary>
public sealed record ServerCompatibilityMetadata(
    ServerMetadataStatus Status,
    string? SchemaVersion,
    string? MinimumSupportedVersion,
    DateTimeOffset? MaintenanceStartedAt)
{
    public static ServerCompatibilityMetadata Absent { get; } = new(ServerMetadataStatus.Absent, null, null, null);

    public static ServerCompatibilityMetadata NoRow { get; } = new(ServerMetadataStatus.NoRow, null, null, null);

    /// <summary>Ligne présente en état d'initialisation (§4.3.1) : versions nulles ensemble.</summary>
    public bool IsInitializationIncomplete =>
        Status == ServerMetadataStatus.Present && SchemaVersion is null;

    /// <summary>
    /// Classe une ligne lue : <see cref="ServerMetadataStatus.Present"/> si les deux versions sont nulles
    /// ensemble ou toutes deux <c>Major.Minor.Patch</c>, <see cref="ServerMetadataStatus.Unreadable"/> sinon.
    /// Rien n'est deviné.
    /// </summary>
    public static ServerCompatibilityMetadata FromRow(
        string? schemaVersion, string? minimumSupportedVersion, DateTimeOffset? maintenanceStartedAt)
    {
        var bothNull = schemaVersion is null && minimumSupportedVersion is null;
        var bothValid = ApplicationVersion.TryParse(schemaVersion, out _)
                        && ApplicationVersion.TryParse(minimumSupportedVersion, out _);

        var status = bothNull || bothValid ? ServerMetadataStatus.Present : ServerMetadataStatus.Unreadable;
        return new ServerCompatibilityMetadata(status, schemaVersion, minimumSupportedVersion, maintenanceStartedAt);
    }
}

/// <summary>
/// Les quatre lectures dont la garde est une fonction pure (§5.1) : Appliquées, Connues, métadonnée, version
/// du poste — plus l'existence de l'historique et des tables, qui distinguent E4 et E7.
/// </summary>
public sealed record ServerSchemaObservation(
    IReadOnlyCollection<string> Applied,
    IReadOnlyCollection<string> Known,
    bool HistoryTableExists,
    bool ApplicationTablesExist,
    ServerCompatibilityMetadata Metadata,
    string ApplicationVersion);

/// <summary>Verdict de la garde.</summary>
/// <param name="State">État observé.</param>
/// <param name="Case">Cas DP-3 lorsque la comparaison Appliquées/Connues a eu lieu ; sinon <c>null</c>.</param>
/// <param name="CanStart">Le poste peut-il démarrer ?</param>
/// <param name="InitializationIncomplete">E5 dû à une installation inachevée (§4.3.1), et non à une maintenance.</param>
/// <param name="Message">Message destiné à l'utilisateur ou au support.</param>
public sealed record ServerSchemaVerdict(
    ServerSchemaState State,
    CompatibilityCase? Case,
    bool CanStart,
    bool InitializationIncomplete,
    string Message);
