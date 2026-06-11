using System.Globalization;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Journal de migration (P2A-1A). Trace, de façon horodatée, les décisions et actions du cycle de
/// vie SQLite (état détecté, sauvegarde créée, migrations appliquées/adoptées, échec). Les entrées
/// sont conservées en mémoire (inspection/tests) et, si un chemin de fichier est fourni, ajoutées en
/// fin de fichier (append) pour le support.
/// </summary>
public interface IMigrationJournal
{
    /// <summary>Ajoute une entrée horodatée au journal.</summary>
    void Write(string message);

    /// <summary>Entrées accumulées (lecture seule), dans l'ordre d'écriture.</summary>
    IReadOnlyList<string> Entries { get; }
}

/// <summary>
/// Implémentation par défaut : mémoire + fichier optionnel (append, sûr en cas d'échec d'écriture).
/// </summary>
public sealed class MigrationJournal : IMigrationJournal
{
    private readonly List<string> _entries = new();
    private readonly string? _filePath;
    private readonly object _gate = new();

    /// <param name="filePath">
    /// Chemin du fichier journal. Si null, le journal reste en mémoire uniquement.
    /// Le dossier parent est créé si nécessaire.
    /// </param>
    public MigrationJournal(string? filePath = null)
    {
        _filePath = filePath;
        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Write(string message)
    {
        var line = $"{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)} {message}";
        lock (_gate)
        {
            _entries.Add(line);
            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                try
                {
                    File.AppendAllText(_filePath, line + Environment.NewLine);
                }
                catch
                {
                    // Le journal ne doit jamais faire échouer la préparation de la base :
                    // l'échec d'écriture du fichier est ignoré (l'entrée reste en mémoire).
                }
            }
        }
    }
}
