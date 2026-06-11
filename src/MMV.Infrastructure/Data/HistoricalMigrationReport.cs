using System.Globalization;
using System.Text;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Rapport avant/après d'une reprise / migration de base historique (P2A-1B). Conçu pour le support :
/// il ne contient que des <b>métadonnées techniques</b> (décision appliquée, fichiers source/cible,
/// sauvegarde, tables, divergences de schéma, comptes de lignes avant/après, erreurs, résultat final).
/// <b>Aucune donnée personnelle</b> (nom, identifiant, contenu d'enregistrement) n'y figure : seules
/// des agrégations (noms de tables et nombres de lignes) sont exposées.
/// </summary>
public sealed class HistoricalMigrationReport
{
    /// <summary>Décision appliquée pour la base traitée.</summary>
    public HistoricalDatabaseDecision Decision { get; init; }

    /// <summary>Fichier source (ancienne base ou base historique).</summary>
    public string? SourceFile { get; init; }

    /// <summary>Fichier cible (chemin courant) si une copie/migration a eu lieu.</summary>
    public string? TargetFile { get; init; }

    /// <summary>Vrai si une sauvegarde a été créée avant toute mutation.</summary>
    public bool BackupCreated { get; init; }

    /// <summary>Chemin de la sauvegarde créée (si applicable).</summary>
    public string? BackupPath { get; init; }

    /// <summary>Vrai si une copie de l'ancien fichier vers le chemin courant a été réalisée.</summary>
    public bool CopyPerformed { get; init; }

    /// <summary>Vrai si un conflit ancien/nouveau fichier a été détecté (les deux présents).</summary>
    public bool ConflictDetected { get; init; }

    /// <summary>Tables applicatives recensées.</summary>
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();

    /// <summary>Colonnes / éléments de schéma divergents (vide si compatible).</summary>
    public IReadOnlyList<string> DivergentColumns { get; init; } = Array.Empty<string>();

    /// <summary>Comptes de lignes par table principale AVANT reprise.</summary>
    public IReadOnlyDictionary<string, long> RowCountsBefore { get; init; }
        = new Dictionary<string, long>();

    /// <summary>Comptes de lignes par table principale APRÈS reprise.</summary>
    public IReadOnlyDictionary<string, long> RowCountsAfter { get; init; }
        = new Dictionary<string, long>();

    /// <summary>Erreurs éventuelles rencontrées (messages techniques).</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Résultat final lisible (succès / refus / conflit / aucune action).</summary>
    public string FinalResult { get; init; } = string.Empty;

    /// <summary>
    /// Vrai si tous les comptes de lignes AVANT sont conservés à l'identique APRÈS reprise
    /// (aucune perte ni création silencieuse). Vrai par défaut si aucune copie n'a eu lieu.
    /// </summary>
    public bool RowCountsPreserved
    {
        get
        {
            if (!CopyPerformed)
            {
                return true;
            }

            foreach (var (table, before) in RowCountsBefore)
            {
                if (!RowCountsAfter.TryGetValue(table, out var after) || after != before)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Produit un rapport texte lisible pour le support. Ne contient que des métadonnées techniques.
    /// </summary>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== Rapport de reprise de base historique (P2A-1B) ===");
        builder.AppendLine(FormattableString.Invariant($"Décision        : {Decision}"));
        builder.AppendLine(FormattableString.Invariant($"Fichier source  : {SourceFile ?? "(aucun)"}"));
        builder.AppendLine(FormattableString.Invariant($"Fichier cible   : {TargetFile ?? "(aucun)"}"));
        builder.AppendLine(FormattableString.Invariant($"Sauvegarde      : {(BackupCreated ? BackupPath ?? "(créée)" : "(aucune)")}"));
        builder.AppendLine(FormattableString.Invariant($"Copie effectuée : {(CopyPerformed ? "oui" : "non")}"));
        builder.AppendLine(FormattableString.Invariant($"Conflit détecté : {(ConflictDetected ? "oui" : "non")}"));

        builder.AppendLine();
        builder.AppendLine("-- Tables --");
        builder.AppendLine(Tables.Count == 0 ? "(aucune)" : string.Join(", ", Tables));

        builder.AppendLine();
        builder.AppendLine("-- Divergences de schéma --");
        if (DivergentColumns.Count == 0)
        {
            builder.AppendLine("(aucune)");
        }
        else
        {
            foreach (var difference in DivergentColumns)
            {
                builder.AppendLine(FormattableString.Invariant($"  - {difference}"));
            }
        }

        builder.AppendLine();
        builder.AppendLine("-- Comptes de lignes (avant / après) --");
        var allTables = RowCountsBefore.Keys.Union(RowCountsAfter.Keys, StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal);
        foreach (var table in allTables)
        {
            var before = RowCountsBefore.TryGetValue(table, out var b) ? b.ToString(CultureInfo.InvariantCulture) : "-";
            var after = RowCountsAfter.TryGetValue(table, out var a) ? a.ToString(CultureInfo.InvariantCulture) : "-";
            builder.AppendLine(FormattableString.Invariant($"  {table}: {before} -> {after}"));
        }

        builder.AppendLine(FormattableString.Invariant($"Comptes conservés : {(RowCountsPreserved ? "oui" : "NON")}"));

        builder.AppendLine();
        builder.AppendLine("-- Erreurs --");
        if (Errors.Count == 0)
        {
            builder.AppendLine("(aucune)");
        }
        else
        {
            foreach (var error in Errors)
            {
                builder.AppendLine(FormattableString.Invariant($"  - {error}"));
            }
        }

        builder.AppendLine();
        builder.AppendLine(FormattableString.Invariant($"Résultat final  : {FinalResult}"));
        return builder.ToString();
    }
}
