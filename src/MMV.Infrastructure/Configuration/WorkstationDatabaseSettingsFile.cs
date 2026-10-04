using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Configuration;

/// <summary>Source de la configuration PostgreSQL d'un poste (seam de <see cref="DatabaseProviderResolver"/>).</summary>
public interface IWorkstationDatabaseSettingsSource
{
    bool Exists { get; }

    /// <exception cref="DatabaseConfigurationException">Fichier illisible, corrompu ou incomplet.</exception>
    PostgreSqlConnectionSettings Load();
}

/// <summary>
/// Configuration de connexion PostgreSQL <b>par poste</b> (P4-8, ADR-PROD-DB-010, D-13 ; ADR-APP-DISTRIBUTION-001
/// ND-10, DI-9) : un fichier dans le dossier de données de l'utilisateur, <b>hors du dossier d'installation</b>.
///
/// <para>
/// <b>Aucun champ en clair.</b> Le fichier n'est qu'une enveloppe (format, schéma de protection) autour d'un blob
/// DPAPI CurrentUser qui contient <i>tous</i> les paramètres — le secret, mais aussi l'hôte et l'autorité
/// racine : protégés ensemble, ils ne peuvent pas être redirigés vers un autre serveur sans que le
/// déchiffrement échoue. Un fichier corrompu, tronqué, ou écrit par un autre utilisateur Windows est
/// <b>refusé explicitement</b>, jamais ignoré.
/// </para>
/// </summary>
public sealed class WorkstationDatabaseSettingsFile : IWorkstationDatabaseSettingsSource
{
    public const string FileName = "database-connection.json";
    public const string FormatName = "mmv-workstation-database/1";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IWorkstationSecretProtector _protector;

    public WorkstationDatabaseSettingsFile(string path, IWorkstationSecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Emplacement par défaut : <c>%LOCALAPPDATA%\ManageMyVision\database-connection.json</c>.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        SqliteDatabasePathResolver.ApplicationFolderName,
        FileName);

    /// <summary>Fichier par défaut, protégé par DPAPI CurrentUser.</summary>
    public static WorkstationDatabaseSettingsFile CreateDefault() =>
        new(DefaultPath, new DpapiCurrentUserSecretProtector());

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public PostgreSqlConnectionSettings Load()
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(Path));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw Unreadable("enveloppe illisible", exception);
        }

        if (envelope is null || envelope.Format != FormatName)
        {
            throw Unreadable("format inconnu ou absent", null);
        }

        if (envelope.Protection != _protector.Scheme || string.IsNullOrEmpty(envelope.Payload))
        {
            throw Unreadable("protection inconnue ou contenu absent", null);
        }

        byte[] plaintext;
        try
        {
            plaintext = _protector.Unprotect(Convert.FromBase64String(envelope.Payload));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw Unreadable("contenu corrompu, ou protégé pour un autre utilisateur Windows ou un autre poste", exception);
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(plaintext);
        }
        catch (JsonException)
        {
            // Aucune exception interne : son message pourrait citer un fragment du contenu déchiffré (secret).
            throw Unreadable("contenu déchiffré invalide", null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        if (payload?.Host is null || payload.Database is null || payload.Username is null || payload.Password is null)
        {
            throw Unreadable("paramètre obligatoire manquant", null);
        }

        var settings = new PostgreSqlConnectionSettings(
            payload.Host, payload.Port, payload.Database, payload.Username, payload.Password, payload.RootCertificatePath);
        settings.Validate();
        return settings;
    }

    /// <summary>
    /// Écrit (ou remplace : rotation du secret) la configuration. Écriture dans un fichier temporaire du même
    /// dossier puis remplacement : un poste n'est jamais laissé avec un fichier à moitié écrit.
    /// </summary>
    public void Save(PostgreSqlConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Payload
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.Username,
            Password = settings.Password,
            RootCertificatePath = settings.RootCertificatePath
        });

        byte[] protectedPayload;
        try
        {
            protectedPayload = _protector.Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var envelope = JsonSerializer.SerializeToUtf8Bytes(new Envelope
        {
            Format = FormatName,
            Protection = _protector.Scheme,
            Payload = Convert.ToBase64String(protectedPayload)
        }, Json);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, envelope);
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private DatabaseConfigurationException Unreadable(string cause, Exception? inner)
    {
        var message = $"Configuration PostgreSQL du poste inutilisable ({cause}) : {Path}. Reconfigurez le poste " +
                      "avec MMV.DatabaseManager configure-workstation. Le démarrage est bloqué (aucun repli sur SQLite).";
        return inner is null ? new DatabaseConfigurationException(message) : new DatabaseConfigurationException(message, inner);
    }

    private sealed class Envelope
    {
        public string? Format { get; set; }
        public string? Protection { get; set; }
        public string? Payload { get; set; }
    }

    private sealed class Payload
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Database { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? RootCertificatePath { get; set; }
    }
}
