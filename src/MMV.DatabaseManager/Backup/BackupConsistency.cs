using System.Globalization;

namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Règles de correspondance entre un manifeste et un état de base (P4-9), sans effet de bord. Chaque méthode rend
/// la <b>première</b> cause de refus, ou <c>null</c>.
/// </summary>
public static class BackupConsistency
{
    /// <summary>
    /// Contenu restauré ⇔ manifeste : même historique EF, mêmes tables, mêmes nombres de lignes. L'identité n'est
    /// pas comparée : la base de vérification est, par construction, une autre base.
    /// </summary>
    public static string? CompareContent(BackupManifest manifest, DatabaseFingerprint restored)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(restored);

        if (!manifest.State.AppliedMigrations.SequenceEqual(restored.AppliedMigrations, StringComparer.Ordinal))
        {
            return $"historique de migration différent : sauvegarde [{string.Join(", ", manifest.State.AppliedMigrations)}], " +
                   $"base [{string.Join(", ", restored.AppliedMigrations)}]";
        }

        var expected = manifest.State.Tables.ToDictionary(t => (t.Schema, t.Table), t => t.Rows);
        var actual = restored.Tables.ToDictionary(t => (t.Schema, t.Table), t => t.Rows);

        var missing = expected.Keys.Except(actual.Keys).Select(Name).ToArray();
        if (missing.Length > 0)
        {
            return $"table(s) absente(s) de la base : {string.Join(", ", missing)}";
        }

        var extra = actual.Keys.Except(expected.Keys).Select(Name).ToArray();
        if (extra.Length > 0)
        {
            return $"table(s) absente(s) de la sauvegarde : {string.Join(", ", extra)}";
        }

        var different = expected.Where(e => actual[e.Key] != e.Value)
            .Select(e => string.Create(CultureInfo.InvariantCulture, $"{Name(e.Key)} ({e.Value} → {actual[e.Key]})"))
            .ToArray();
        return different.Length > 0
            ? $"nombre de lignes différent : {string.Join(", ", different)}"
            : null;
    }

    /// <summary>
    /// Sauvegarde ⇔ base réelle, juste avant une migration et sous le verrou : <b>même installation</b>, <b>même
    /// base</b>, contenu identique (aucune écriture depuis la sauvegarde), et âge serveur borné.
    /// </summary>
    public static string? CheckCurrent(BackupManifest manifest, DatabaseFingerprint live, TimeSpan maximumAge)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(live);

        var source = manifest.State;
        if (!string.Equals(source.SystemIdentifier, live.SystemIdentifier, StringComparison.Ordinal))
        {
            return "sauvegarde d'une autre installation PostgreSQL (identifiant de cluster différent)";
        }

        if (!string.Equals(source.Database, live.Database, StringComparison.Ordinal) || source.DatabaseOid != live.DatabaseOid)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"sauvegarde d'une autre base ('{source.Database}' oid {source.DatabaseOid}, base courante '{live.Database}' oid {live.DatabaseOid})");
        }

        var content = CompareContent(manifest, live);
        if (content is not null)
        {
            return $"la base a changé depuis la sauvegarde — {content}";
        }

        var age = live.ServerTimeUtc - source.ServerTimeUtc;
        if (age < TimeSpan.Zero)
        {
            return "horodatage de sauvegarde postérieur à l'horloge du serveur";
        }

        return age > maximumAge
            ? string.Create(CultureInfo.InvariantCulture,
                $"sauvegarde trop ancienne : {age:c} (maximum {maximumAge:c}, horloge du serveur)")
            : null;
    }

    private static string Name((string Schema, string Table) key) => $"{key.Schema}.{key.Table}";
}
