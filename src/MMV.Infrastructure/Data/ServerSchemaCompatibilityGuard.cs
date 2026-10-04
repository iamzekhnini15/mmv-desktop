using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Configuration;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Garde de compatibilité d'un poste face à une base serveur (P4-6B, ADR-PROD-DB-009 DP-3, DP-4 ; §5 du plan).
///
/// <para>
/// <b>Lecture seule, sans exception</b> : zéro écriture, zéro DDL, zéro seed (DP-4.1, SB-3). Le verdict est une
/// fonction pure de quatre lectures (<see cref="Evaluate"/>) ; <see cref="EvaluateAsync(DbConnection, IEnumerable{string}, string, CancellationToken)"/>
/// ne fait que les collecter en SQL de lecture.
/// </para>
///
/// <para>
/// <b>Non branchée en P4-6B</b> : aucun poste ne l'appelle ; le garde-fou de démarrage reste en place. Son
/// branchement, l'écran de blocage et la levée du garde-fou appartiennent à P4-6C.
/// </para>
/// </summary>
public static class ServerSchemaCompatibilityGuard
{
    private const string HistoryRelation = "\"" + HistoryRepository.DefaultTableName + "\"";

    private const string ApplicationTablesSql =
        "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables " +
        "WHERE schemaname = current_schema() AND tablename <> @history)";

    private const string AppliedSql =
        "SELECT \"MigrationId\" FROM \"" + HistoryRepository.DefaultTableName + "\"";

    /// <summary>
    /// Verdict. Ordre d'évaluation normatif (§5.2) : blocages lisibles dans la métadonnée (initialisation
    /// inachevée, maintenance), historique absent (E4, E7), puis C-5, C-2, C-1, C-3 / C-4 — une divergence
    /// n'est jamais lue comme un simple retard.
    /// </summary>
    public static ServerSchemaVerdict Evaluate(ServerSchemaObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var metadata = observation.Metadata;

        if (metadata.IsInitializationIncomplete)
        {
            return new(ServerSchemaState.E5, null, false, true,
                "Installation de la base inachevée — relancer l'outil MMV.DatabaseManager.");
        }

        // Le marqueur bloque quel que soit son âge : aucune durée n'est appliquée (§5.3).
        if (metadata.MaintenanceStartedAt is not null)
        {
            return new(ServerSchemaState.E5, null, false, false, "Maintenance de la base en cours.");
        }

        if (!observation.HistoryTableExists)
        {
            return observation.ApplicationTablesExist
                ? new(ServerSchemaState.E7, null, false, false,
                    "Base sans historique de migrations : l'adoption relève de l'outil MMV.DatabaseManager.")
                : new(ServerSchemaState.E4, null, false, false,
                    "Base vide : l'installation relève de l'outil MMV.DatabaseManager.");
        }

        var applied = observation.Applied.ToHashSet(StringComparer.Ordinal);
        var known = observation.Known.ToHashSet(StringComparer.Ordinal);
        var appliedOnly = applied.Except(known).Any();
        var knownOnly = known.Except(applied).Any();

        if (appliedOnly && knownOnly)
        {
            return new(ServerSchemaState.E3c, CompatibilityCase.C5, false, false,
                "Chaînes de migrations divergentes entre ce poste et la base.");
        }

        if (knownOnly)
        {
            return new(ServerSchemaState.E2, CompatibilityCase.C2, false, false,
                "La base doit être mise à jour par l'opérateur.");
        }

        if (!appliedOnly)
        {
            return new(ServerSchemaState.E1, CompatibilityCase.C1, true, false, "Base compatible.");
        }

        // A ⊋ K : seule une métadonnée lisible accorde la fenêtre ; absente ou illisible ⇒ égalité stricte.
        var withinWindow = metadata.Status == ServerMetadataStatus.Present
                           && ApplicationVersion.TryParse(observation.ApplicationVersion, out var version)
                           && ApplicationVersion.TryParse(metadata.MinimumSupportedVersion, out var minimum)
                           && version >= minimum;

        return withinWindow
            ? new(ServerSchemaState.E3a, CompatibilityCase.C3, true, false, "Base compatible (fenêtre N-1).")
            : new(ServerSchemaState.E3b, CompatibilityCase.C4, false, false, "Ce poste doit être mis à jour.");
    }

    /// <summary>
    /// Collecte les lectures sur la base du contexte et rend le verdict pour la version de cette build.
    /// </summary>
    public static Task<ServerSchemaVerdict> EvaluateAsync(
        OpticDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return EvaluateAsync(
            context.Database.GetDbConnection(),
            context.Database.GetMigrations(),
            ApplicationVersion.Current,
            cancellationToken);
    }

    /// <summary>
    /// Collecte les lectures sur <paramref name="connection"/> (ouverte si nécessaire, puis refermée dans ce
    /// cas) et rend le verdict. Aucune erreur serveur n'est avalée (E6, K-13).
    /// </summary>
    /// <param name="connection">Connexion à la base serveur.</param>
    /// <param name="known">Migrations connues du binaire (<c>GetMigrations()</c>).</param>
    /// <param name="applicationVersion">Version du poste.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public static async Task<ServerSchemaVerdict> EvaluateAsync(
        DbConnection connection,
        IEnumerable<string> known,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(known);

        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var historyExists = await ServerCompatibilityMetadataReader.RelationExistsAsync(
                connection, HistoryRelation, cancellationToken);
            var applicationTablesExist = await ApplicationTablesExistAsync(connection, cancellationToken);
            var applied = historyExists
                ? await ReadAppliedAsync(connection, cancellationToken)
                : Array.Empty<string>();
            var metadata = await ServerCompatibilityMetadataReader.ReadAsync(connection, cancellationToken);

            return Evaluate(new ServerSchemaObservation(
                applied, known.ToArray(), historyExists, applicationTablesExist, metadata, applicationVersion));
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> ApplicationTablesExistAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ApplicationTablesSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "history";
        parameter.Value = HistoryRepository.DefaultTableName;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IReadOnlyCollection<string>> ReadAppliedAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = AppliedSql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var applied = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }
}
