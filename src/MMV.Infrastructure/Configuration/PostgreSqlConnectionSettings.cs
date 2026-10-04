using System.Text;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Paramètres de connexion PostgreSQL d'un poste (P4-8, ADR-PROD-DB-010, D-13). Le seul secret est
/// <see cref="Password"/> : il n'est jamais restitué par <see cref="ToString"/> ni par un message d'erreur.
/// </summary>
/// <param name="Host">Nom DNS du serveur — celui que porte son certificat (VerifyFull, D-14).</param>
/// <param name="Port">Port TCP.</param>
/// <param name="Database">Base applicative.</param>
/// <param name="Username">Rôle applicatif du poste (jamais le migrateur ni l'administrateur, DP-5).</param>
/// <param name="Password">Secret du rôle.</param>
/// <param name="RootCertificatePath">
/// Autorité racine qui valide le certificat serveur ; <c>null</c> ⇒ magasin de certificats du système.
/// </param>
public sealed record PostgreSqlConnectionSettings(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    string? RootCertificatePath)
{
    /// <summary>Longueur maximale d'un identifiant PostgreSQL (NAMEDATALEN − 1).</summary>
    public const int MaximumIdentifierBytes = 63;

    /// <summary>Lève une <see cref="DatabaseConfigurationException"/> au premier champ inutilisable.</summary>
    public void Validate()
    {
        if (!IsUsableText(Host))
        {
            throw new DatabaseConfigurationException("Hôte PostgreSQL absent ou invalide.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new DatabaseConfigurationException("Port PostgreSQL hors de l'intervalle 1–65535.");
        }

        if (!IsUsableIdentifier(Database))
        {
            throw new DatabaseConfigurationException(
                $"Nom de base absent, invalide ou au-delà de {MaximumIdentifierBytes} octets.");
        }

        if (!IsUsableIdentifier(Username))
        {
            throw new DatabaseConfigurationException(
                $"Rôle PostgreSQL absent, invalide ou au-delà de {MaximumIdentifierBytes} octets.");
        }

        if (string.IsNullOrEmpty(Password) || Password.Any(char.IsControl))
        {
            throw new DatabaseConfigurationException("Secret du rôle PostgreSQL absent ou invalide.");
        }

        if (RootCertificatePath is not null
            && (!IsUsableText(RootCertificatePath) || !Path.IsPathFullyQualified(RootCertificatePath)
                || !File.Exists(RootCertificatePath)))
        {
            throw new DatabaseConfigurationException(
                "Certificat racine introuvable : un chemin absolu vers un fichier existant est exigé.");
        }
    }

    /// <summary>Jamais le secret.</summary>
    public override string ToString() =>
        $"{Username}@{Host}:{Port}/{Database} (racine : {RootCertificatePath ?? "magasin système"})";

    public static bool IsUsableText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    public static bool IsUsableIdentifier(string? value) =>
        IsUsableText(value) && Encoding.UTF8.GetByteCount(value!) <= MaximumIdentifierBytes;
}
