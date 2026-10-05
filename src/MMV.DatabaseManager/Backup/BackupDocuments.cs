using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Manifeste d'une sauvegarde (P4-9) : identité de la base sauvegardée, instant serveur de l'instantané, état
/// relevé <b>dans le même instantané</b> que <c>pg_dump</c> (historique EF, nombre de lignes par table), et
/// empreinte SHA-256 du fichier de sauvegarde. Écrit <b>en dernier</b> : un manifeste présent désigne une
/// sauvegarde complète.
/// </summary>
public sealed record BackupManifest(
    Guid BackupId,
    string ToolVersion,
    string Operator,
    string Role,
    string ServerVersion,
    DatabaseFingerprint State,
    string DumpFile,
    long DumpSizeBytes,
    string DumpSha256);

/// <summary>
/// Preuve de vérification (P4-9) : la sauvegarde désignée par <see cref="BackupId"/>, dans l'état exact
/// (<see cref="ManifestSha256"/>, <see cref="DumpSha256"/>), a été <b>réellement restaurée</b> dans une base de
/// vérification isolée, et son contenu restauré est identique au manifeste. Écrite par l'administrateur
/// (<c>verify-backup</c>) ; lue par l'outil de migration.
/// </summary>
public sealed record BackupProof(
    Guid BackupId,
    string ManifestSha256,
    string DumpSha256,
    DateTimeOffset VerifiedAtServerUtc,
    string VerifierRole,
    string Operator,
    string ToolVersion,
    string ServerSystemIdentifier,
    string ScratchDatabase,
    int Tables,
    long Rows);

/// <summary>Document de sauvegarde illisible, incomplet ou non conforme.</summary>
public sealed class BackupDocumentException(string message) : Exception(message);

/// <summary>
/// Fichiers d'une sauvegarde — <c>&lt;nom&gt;.dump</c>, <c>&lt;nom&gt;.manifest.json</c>,
/// <c>&lt;nom&gt;.verification.json</c> dans un même dossier — et leur format JSON <b>strict</b> : membre inconnu,
/// manquant ou nul, format ou version inattendus ⇒ refus. Écritures atomiques (fichier temporaire puis
/// renommage) : un lecteur ne voit jamais un document à moitié écrit.
/// </summary>
public static partial class BackupFiles
{
    public const string ManifestSuffix = ".manifest.json";
    public const string ProofSuffix = ".verification.json";
    public const string DumpSuffix = ".dump";
    public const string PartialSuffix = ".partial";

    private const string ManifestFormat = "mmv-backup-manifest";
    private const string ProofFormat = "mmv-backup-verification";
    private const int FormatVersion = 1;
    private const string VerifiedResult = "verified";

    /// <summary>Taille maximale lue pour un document JSON : un manifeste réel pèse quelques kilo-octets.</summary>
    private const long MaximumDocumentBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true
    };

    /// <summary>Nom commun des fichiers d'une sauvegarde : horodatage serveur UTC puis identifiant.</summary>
    public static string Stem(Guid backupId, DateTimeOffset createdAtServerUtc) =>
        $"mmv-backup-{createdAtServerUtc.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{backupId:N}";

    /// <summary>Preuve associée à un manifeste (même dossier, même nom commun).</summary>
    public static string ProofPathFor(string manifestPath)
    {
        if (!manifestPath.EndsWith(ManifestSuffix, StringComparison.Ordinal))
        {
            throw new BackupDocumentException($"un manifeste de sauvegarde se termine par '{ManifestSuffix}'");
        }

        return manifestPath[..^ManifestSuffix.Length] + ProofSuffix;
    }

    /// <summary>Fichier de sauvegarde désigné par un manifeste : un nom simple, dans le dossier du manifeste.</summary>
    public static string DumpPathFor(string manifestPath, BackupManifest manifest) =>
        Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.DumpFile);

    public static async Task WriteManifestAsync(string path, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var document = new ManifestDocument(ManifestFormat, FormatVersion, manifest.BackupId, manifest.ToolVersion,
            manifest.Operator, manifest.Role, manifest.ServerVersion,
            new ServerDocument(manifest.State.SystemIdentifier, manifest.State.Database, manifest.State.DatabaseOid),
            manifest.State.ServerTimeUtc, manifest.State.AppliedMigrations.ToArray(),
            manifest.State.Tables.Select(t => new TableDocument(t.Schema, t.Table, t.Rows)).ToArray(),
            new DumpDocument(manifest.DumpFile, "custom", manifest.DumpSizeBytes, manifest.DumpSha256));
        await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, Json), cancellationToken);
    }

    public static async Task WriteProofAsync(string path, BackupProof proof, CancellationToken cancellationToken)
    {
        var document = new ProofDocument(ProofFormat, FormatVersion, VerifiedResult, proof.BackupId, proof.ManifestSha256,
            proof.DumpSha256, proof.VerifiedAtServerUtc, proof.VerifierRole, proof.Operator, proof.ToolVersion,
            proof.ServerSystemIdentifier, proof.ScratchDatabase, proof.Tables, proof.Rows);
        await WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, Json), cancellationToken);
    }

    /// <summary>Manifeste lu et validé, avec l'empreinte SHA-256 de ses octets exacts.</summary>
    public static async Task<(BackupManifest Manifest, string Sha256)> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await ReadDocumentAsync(path, "manifeste", cancellationToken);
        var document = Deserialize<ManifestDocument>(bytes, "manifeste");

        Require(document.Format == ManifestFormat && document.FormatVersion == FormatVersion,
            $"format de manifeste inattendu ('{document.Format}' v{document.FormatVersion})");
        Require(document.BackupId != Guid.Empty, "identifiant de sauvegarde vide");
        Require(IsText(document.ToolVersion) && IsText(document.Operator) && IsText(document.Role) && IsText(document.ServerVersion),
            "champ texte vide");
        Require(IsText(document.Server.SystemIdentifier) && IsText(document.Server.Database) && document.Server.DatabaseOid > 0,
            "identité de la base incomplète");
        Require(document.CreatedAtServerUtc.Offset == TimeSpan.Zero, "horodatage serveur non UTC");
        Require(document.AppliedMigrations.All(IsText)
                && document.AppliedMigrations.SequenceEqual(document.AppliedMigrations.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal)),
            "historique de migration invalide (vide, dupliqué ou non trié)");
        Require(document.Tables.All(t => IsText(t.Schema) && IsText(t.Name) && t.Rows >= 0), "table invalide");
        Require(document.Tables.Select(t => (t.Schema, t.Name)).Distinct().Count() == document.Tables.Length, "table dupliquée");
        Require(document.Dump.Format == "custom", "format de sauvegarde inattendu");
        Require(IsSimpleFileName(document.Dump.File) && document.Dump.File.EndsWith(DumpSuffix, StringComparison.Ordinal),
            "nom de fichier de sauvegarde invalide (un nom simple « .dump » est exigé, jamais un chemin)");
        Require(document.Dump.SizeBytes > 0, "taille de sauvegarde invalide");
        Require(IsSha256(document.Dump.Sha256), "empreinte SHA-256 de sauvegarde invalide");

        var manifest = new BackupManifest(document.BackupId, document.ToolVersion, document.Operator, document.Role,
            document.ServerVersion,
            new DatabaseFingerprint(document.Server.SystemIdentifier, document.Server.Database, document.Server.DatabaseOid,
                document.CreatedAtServerUtc, document.AppliedMigrations,
                document.Tables.Select(t => new TableRowCount(t.Schema, t.Name, t.Rows)).ToArray()),
            document.Dump.File, document.Dump.SizeBytes, document.Dump.Sha256);
        return (manifest, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static async Task<BackupProof> ReadProofAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await ReadDocumentAsync(path, "preuve de vérification", cancellationToken);
        var document = Deserialize<ProofDocument>(bytes, "preuve de vérification");

        Require(document.Format == ProofFormat && document.FormatVersion == FormatVersion,
            $"format de preuve inattendu ('{document.Format}' v{document.FormatVersion})");
        Require(document.Result == VerifiedResult, $"résultat de vérification '{document.Result}'");
        Require(IsSha256(document.ManifestSha256) && IsSha256(document.DumpSha256), "empreinte invalide dans la preuve");
        Require(document.VerifiedAtServerUtc.Offset == TimeSpan.Zero, "horodatage de vérification non UTC");
        Require(document.Tables >= 0 && document.Rows >= 0, "comptes invalides dans la preuve");

        return new BackupProof(document.BackupId, document.ManifestSha256, document.DumpSha256, document.VerifiedAtServerUtc,
            document.VerifierRole, document.Operator, document.ToolVersion, document.ServerSystemIdentifier,
            document.ScratchDatabase, document.Tables, document.Rows);
    }

    /// <summary>Empreinte SHA-256 (hexadécimal minuscule) et taille d'un fichier, lu en flux.</summary>
    public static async Task<(string Sha256, long Size)> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (Convert.ToHexStringLower(hash), stream.Length);
    }

    /// <summary>Fichier créé par l'outil : lecture et écriture réservées au propriétaire là où le système le permet.</summary>
    public static FileStream CreateExclusive(string path) => new(path, OperatingSystem.IsWindows()
        ? new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous }
        : new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });

    public static bool IsSimpleFileName(string? name) =>
        IsText(name) && name != "." && name != ".." && Path.GetFileName(name) == name
        && name!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(['/', '\\', ':']) < 0;

    private static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + PartialSuffix;
        try
        {
            await using (var stream = CreateExclusive(temporary))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<byte[]> ReadDocumentAsync(string path, string what, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new BackupDocumentException($"{what} introuvable");
        }

        if (info.Length > MaximumDocumentBytes)
        {
            throw new BackupDocumentException($"{what} anormalement volumineux");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static T Deserialize<T>(byte[] bytes, string what)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new BackupDocumentException($"{what} vide");
        }
        catch (JsonException exception)
        {
            throw new BackupDocumentException($"{what} illisible ou incomplet ({exception.Message})");
        }
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new BackupDocumentException(failure);
        }
    }

    private static bool IsText(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static bool IsSha256(string? value) => value is not null && Sha256Pattern().IsMatch(value);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    private sealed record ManifestDocument(
        string Format, int FormatVersion, Guid BackupId, string ToolVersion, string Operator, string Role,
        string ServerVersion, ServerDocument Server, DateTimeOffset CreatedAtServerUtc, string[] AppliedMigrations,
        TableDocument[] Tables, DumpDocument Dump);

    private sealed record ServerDocument(string SystemIdentifier, string Database, long DatabaseOid);

    private sealed record TableDocument(string Schema, string Name, long Rows);

    private sealed record DumpDocument(string File, string Format, long SizeBytes, string Sha256);

    private sealed record ProofDocument(
        string Format, int FormatVersion, string Result, Guid BackupId, string ManifestSha256, string DumpSha256,
        DateTimeOffset VerifiedAtServerUtc, string VerifierRole, string Operator, string ToolVersion,
        string ServerSystemIdentifier, string ScratchDatabase, int Tables, long Rows);
}
