using System.Security.Cryptography;
using System.Text;

namespace MMV.Infrastructure.Configuration;

/// <summary>Protection au repos d'un secret de poste (P4-8, D-13). Seam de test : la production n'a que DPAPI.</summary>
public interface IWorkstationSecretProtector
{
    /// <summary>Identifiant écrit dans le fichier : une protection inconnue n'est jamais « devinée ».</summary>
    string Scheme { get; }

    byte[] Protect(byte[] plaintext);

    /// <exception cref="CryptographicException">Donnée corrompue, ou protégée pour un autre utilisateur.</exception>
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>
/// DPAPI, portée <b>CurrentUser</b> (D-13) : seul l'utilisateur Windows qui a écrit la configuration, sur ce
/// poste, peut la relire. Le blob DPAPI est authentifié : une altération est détectée au déchiffrement.
/// L'entropie additionnelle n'est pas un secret ; elle cloisonne ce blob des autres usages DPAPI du compte.
/// </summary>
public sealed class DpapiCurrentUserSecretProtector : IWorkstationSecretProtector
{
    public const string SchemeName = "dpapi-current-user";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MMV.Workstation.PostgreSqlConnection.v1");

    public string Scheme => SchemeName;

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (!OperatingSystem.IsWindows())
        {
            throw NotWindows();
        }

        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        if (!OperatingSystem.IsWindows())
        {
            throw NotWindows();
        }

        return ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
    }

    private static PlatformNotSupportedException NotWindows() => new(
        "DPAPI n'existe que sous Windows : la configuration protégée d'un poste MMV n'est pas lisible ici.");
}
