using System.Security.Cryptography;
using System.Text;

namespace MMV.DatabaseManager.Provisioning;

/// <summary>
/// Vérificateur SCRAM-SHA-256 au format stocké par PostgreSQL (<c>pg_authid.rolpassword</c>), calculé
/// <b>côté outil</b> (P4-8, D-14) — comme le fait <c>psql \password</c> (libpq <c>PQencryptPasswordConn</c>) :
/// le secret en clair n'est <b>jamais</b> envoyé au serveur, donc jamais exposé à <c>log_statement</c> ni à
/// <c>pg_stat_activity</c>. Le serveur stocke le vérificateur tel quel, quel que soit
/// <c>password_encryption</c> : aucun hachage MD5 ne peut être produit par ce chemin.
///
/// <para>
/// RFC 5802 / RFC 7677 : <c>SaltedPassword = PBKDF2-HMAC-SHA-256(secret, sel, i)</c>,
/// <c>StoredKey = SHA-256(HMAC(SaltedPassword, "Client Key"))</c>, <c>ServerKey = HMAC(SaltedPassword, "Server
/// Key")</c>. Le secret est restreint à l'ASCII imprimable (<see cref="RoleSecretPolicy"/>) : SASLprep y est
/// l'identité, le vérificateur est donc exactement celui que le serveur recalculerait.
/// </para>
/// </summary>
public static class ScramSha256Verifier
{
    /// <summary>Valeur par défaut de <c>scram_iterations</c> sur PostgreSQL 16 et 17.</summary>
    public const int DefaultIterations = 4096;

    public const int SaltBytes = 16;

    public static string Create(string secret, byte[]? salt = null, int iterations = DefaultIterations)
    {
        RoleSecretPolicy.ThrowIfInvalid(secret);
        if (iterations < DefaultIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Nombre d'itérations SCRAM inférieur au défaut du serveur.");
        }

        salt ??= RandomNumberGenerator.GetBytes(SaltBytes);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var salted = Rfc2898DeriveBytes.Pbkdf2(secretBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            var storedKey = SHA256.HashData(HMACSHA256.HashData(salted, "Client Key"u8));
            var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
            return $"SCRAM-SHA-256${iterations}:{Convert.ToBase64String(salt)}$" +
                   $"{Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            CryptographicOperations.ZeroMemory(salted);
        }
    }
}

/// <summary>
/// Secrets des rôles PostgreSQL de MMV (P4-8) : 24 à 128 caractères ASCII imprimables, sans espace. Ce sont des
/// secrets de service, saisis une fois et stockés protégés : la longueur minimale vaut entropie.
/// </summary>
public static class RoleSecretPolicy
{
    public const int MinimumLength = 24;
    public const int MaximumLength = 128;

    public static bool IsValid(string? secret) =>
        secret is { Length: >= MinimumLength and <= MaximumLength } && secret.All(c => c is >= '!' and <= '~');

    public static void ThrowIfInvalid(string? secret)
    {
        if (!IsValid(secret))
        {
            throw new ArgumentException(Requirement);
        }
    }

    public static string Requirement =>
        $"Secret de rôle refusé : {MinimumLength} à {MaximumLength} caractères ASCII imprimables, sans espace.";
}
