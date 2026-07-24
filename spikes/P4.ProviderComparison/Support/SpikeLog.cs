namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Journal de preuves du spike. Écrit un fichier texte hors du dépôt (répertoire fourni par
/// <c>MMV_P4_SPIKE_LOG_DIR</c>, sinon TEMP) afin que les observations soient exploitables même
/// quand le test réussit. Aucun secret n'y transite : seuls des faits de schéma et de valeurs.
/// </summary>
public static class SpikeLog
{
    private static readonly object Gate = new();

    private static readonly string Directory =
        Environment.GetEnvironmentVariable("MMV_P4_SPIKE_LOG_DIR")
        ?? Path.Combine(Path.GetTempPath(), "mmv-p4-spike");

    public static void Write(string experiment, string provider, string message)
    {
        lock (Gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, $"{experiment}.log");
            File.AppendAllText(path, $"[{provider}] {message}{Environment.NewLine}");
        }
    }

    public static void Section(string experiment, string provider, string title)
        => Write(experiment, provider, $"--- {title} ---");
}
