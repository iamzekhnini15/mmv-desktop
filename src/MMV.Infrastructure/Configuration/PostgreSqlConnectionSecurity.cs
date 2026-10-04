using Npgsql;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Politique de connexion PostgreSQL de production (P4-8, ADR-PROD-DB-010, D-14) — point <b>unique</b>, partagé
/// par les postes (<see cref="DatabaseProviderResolver"/>) et par <c>MMV.DatabaseManager</c> :
///
/// <list type="bullet">
///   <item><b>TLS obligatoire, <c>VerifyFull</c></b> : chaîne de certificats <b>et</b> nom d'hôte vérifiés ;</item>
///   <item><b>SCRAM-SHA-256 seul</b> (<c>Require Auth</c>) : un serveur qui demanderait MD5 ou un mot de passe
///   en clair est refusé <b>par le client</b> ;</item>
///   <item><b>aucun repli</b> : une valeur explicitement plus faible n'est jamais « corrigée » en silence, elle
///   est refusée ; seules les valeurs <b>par défaut</b> de Npgsql (<c>Prefer</c>, aucune exigence
///   d'authentification) sont <b>durcies</b> ;</item>
///   <item>aucun message ne restitue la chaîne de connexion (elle porte un secret).</item>
/// </list>
///
/// <para>Aucune nouvelle tentative de connexion : la résilience appartient à P4-10 (D-15).</para>
/// </summary>
public static class PostgreSqlConnectionSecurity
{
    /// <summary>Seule méthode d'authentification acceptée (valeur Npgsql de <c>Require Auth</c>).</summary>
    public const string RequiredAuthentication = "ScramSHA256";

    /// <summary>Délai d'établissement de connexion borné (secondes) des chaînes construites ici.</summary>
    public const int ConnectTimeoutSeconds = 15;

    /// <summary>Chaîne de connexion conforme, construite depuis des paramètres de poste validés.</summary>
    /// <exception cref="DatabaseConfigurationException">Paramètre inutilisable.</exception>
    public static string BuildConnectionString(PostgreSqlConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.Username,
            Password = settings.Password,
            Timeout = ConnectTimeoutSeconds
        };
        if (settings.RootCertificatePath is not null)
        {
            builder.RootCertificate = settings.RootCertificatePath;
        }

        return Enforce(builder);
    }

    /// <summary>
    /// Durcit une chaîne fournie par l'exploitant (variable d'environnement d'un poste, chaîne du migrateur) :
    /// défauts relevés, valeurs plus faibles refusées.
    /// </summary>
    /// <exception cref="ArgumentException">Chaîne mal formée (jamais restituée).</exception>
    /// <exception cref="DatabaseConfigurationException">Réglage de sécurité explicitement affaibli.</exception>
    public static string Harden(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException)
        {
            // Le message de Npgsql peut citer un fragment de la chaîne : il n'est pas propagé.
            throw new ArgumentException("Chaîne de connexion PostgreSQL mal formée.");
        }

        return Enforce(builder);
    }

    /// <summary>
    /// Connexion non ouverte, hors EF — pour une chaîne issue de <see cref="BuildConnectionString"/> ou de
    /// <see cref="Harden"/>. Point unique : <c>MMV.DatabaseManager</c> ne construit aucun type de provider.
    /// </summary>
    public static System.Data.Common.DbConnection CreateConnection(string connectionString) =>
        new NpgsqlConnection(connectionString);

    private static string Enforce(NpgsqlConnectionStringBuilder builder)
    {
        switch (builder.SslMode)
        {
            case SslMode.VerifyFull:
                break;
            case SslMode.Prefer:
                // Défaut de Npgsql (absent de la chaîne) : relevé, jamais conservé.
                builder.SslMode = SslMode.VerifyFull;
                break;
            default:
                throw new DatabaseConfigurationException(
                    $"SSL Mode={builder.SslMode} refusé : TLS avec vérification complète (VerifyFull) est " +
                    "obligatoire pour toute connexion PostgreSQL de production (D-14).");
        }

        // « Trust Server Certificate » est inopérant depuis Npgsql 8 (obsolète, « does nothing ») : seul SSL Mode
        // gouverne la vérification, et il vaut ici VerifyFull. Le seul contournement restant — un rappel de
        // validation de certificat — n'existe qu'en code (NpgsqlDataSourceBuilder), jamais dans une chaîne.
        if (string.IsNullOrWhiteSpace(builder.RequireAuth))
        {
            builder.RequireAuth = RequiredAuthentication;
        }
        else if (!string.Equals(builder.RequireAuth.Trim(), RequiredAuthentication, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseConfigurationException(
                $"Require Auth={builder.RequireAuth} refusé : seule l'authentification {RequiredAuthentication} " +
                "est admise (aucune authentification MD5, D-14).");
        }

        return builder.ConnectionString;
    }
}
