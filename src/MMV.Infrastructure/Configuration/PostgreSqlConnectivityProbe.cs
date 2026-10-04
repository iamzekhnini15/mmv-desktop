using System.Net.Sockets;
using System.Security.Authentication;
using Npgsql;

namespace MMV.Infrastructure.Configuration;

/// <summary>Cause d'indisponibilité de la base centrale (P4-8, D-15). Aucune ne déclenche de nouvelle tentative.</summary>
public enum DatabaseUnavailableReason
{
    /// <summary>Hôte inconnu, port fermé, délai dépassé : le serveur n'a pas répondu.</summary>
    ServerUnreachable,

    /// <summary>Certificat serveur refusé : autorité non reconnue, nom d'hôte différent, certificat expiré.</summary>
    TlsValidationFailed,

    /// <summary>Le serveur ne propose pas TLS : aucune connexion non chiffrée n'est tentée à la place.</summary>
    TlsUnavailable,

    /// <summary>Secret refusé, ou rôle inexistant (PostgreSQL ne les distingue pas, par conception : 28P01).</summary>
    AuthenticationFailed,

    /// <summary>Le serveur exige une méthode d'authentification autre que SCRAM-SHA-256 (MD5, clair).</summary>
    AuthenticationMethodRefused,

    /// <summary>La base demandée n'existe pas (3D000).</summary>
    DatabaseNotFound,

    /// <summary>Le poste est configuré avec une identité privilégiée (superutilisateur, création, DDL).</summary>
    PrivilegedIdentity,

    /// <summary>Échec de connexion non classé : traité comme une indisponibilité, jamais avalé.</summary>
    Unknown
}

/// <summary>
/// Base centrale indisponible au démarrage (D-15) : <b>arrêt explicite</b>, aucune opération métier, aucune
/// nouvelle tentative (P4-10). Le message ne contient ni la chaîne de connexion ni le secret.
/// </summary>
public sealed class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(DatabaseUnavailableReason reason, string message, Exception? innerException = null)
        : base(message, innerException) => Reason = reason;

    public DatabaseUnavailableReason Reason { get; }
}

/// <summary>
/// Vérification de connexion <b>unique et bornée</b> à la base centrale (P4-8, D-15) : une ouverture, une requête,
/// aucune nouvelle tentative. Contrôle aussi que l'identité du poste <b>n'est pas privilégiée</b> (DP-5 : le rôle
/// applicatif n'a ni DDL ni droit de création) — un poste configuré avec le migrateur ou l'administrateur est
/// refusé, jamais servi.
///
/// <para>
/// La classification repose sur les formes d'exception <b>mesurées</b> sur PostgreSQL 17.10 / Npgsql 10.0.3
/// (rapport P4-8 §3) ; un test d'intégration épingle chacune, pour qu'une montée de version qui les changerait
/// échoue visiblement plutôt que de reclasser en silence.
/// </para>
/// </summary>
public static class PostgreSqlConnectivityProbe
{
    private const string PrivilegeQuery =
        "SELECT r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls " +
        "OR has_database_privilege(current_database(), 'CREATE') " +
        "FROM pg_catalog.pg_roles r WHERE r.rolname = current_user";

    /// <summary>Ouvre une connexion et la vérifie ; lève <see cref="DatabaseUnavailableException"/> sinon.</summary>
    public static void EnsureAvailable(string connectionString) =>
        EnsureAvailableAsync(connectionString).GetAwaiter().GetResult();

    /// <inheritdoc cref="EnsureAvailable"/>
    public static async Task EnsureAvailableAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var target = Describe(connectionString);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = PrivilegeQuery;
            if (await command.ExecuteScalarAsync(cancellationToken) is not false)
            {
                throw new DatabaseUnavailableException(DatabaseUnavailableReason.PrivilegedIdentity,
                    $"Base centrale {target} : le poste est configuré avec une identité PostgreSQL privilégiée " +
                    "(superutilisateur, création, ou droit DDL sur la base). Seul le rôle applicatif est admis " +
                    "sur un poste. Démarrage arrêté.");
            }
        }
        catch (DatabaseUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tout échec d'ouverture est un arrêt explicite (K-13 : jamais avalé) ; l'exception d'origine reste
            // attachée pour le diagnostic — Npgsql n'y écrit jamais le secret.
            var reason = Classify(exception);
            throw new DatabaseUnavailableException(reason, Message(reason, target), exception);
        }
    }

    /// <summary>Classe un échec de connexion. Ne lève jamais.</summary>
    public static DatabaseUnavailableReason Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case AuthenticationException:
                    return DatabaseUnavailableReason.TlsValidationFailed;
                case PostgresException { SqlState: "28P01" or "28000" }:
                    return DatabaseUnavailableReason.AuthenticationFailed;
                case PostgresException { SqlState: "3D000" }:
                    return DatabaseUnavailableReason.DatabaseNotFound;
                case SocketException or TimeoutException:
                    return DatabaseUnavailableReason.ServerUnreachable;
            }
        }

        if (exception is NpgsqlException { InnerException: null } npgsql and not PostgresException)
        {
            // Formes sans exception interne, mesurées (Npgsql 10.0.3) : refus côté client.
            if (npgsql.Message.Contains("No SSL enabled connection", StringComparison.Ordinal))
            {
                return DatabaseUnavailableReason.TlsUnavailable;
            }

            if (npgsql.Message.Contains("authentication method is not allowed", StringComparison.Ordinal))
            {
                return DatabaseUnavailableReason.AuthenticationMethodRefused;
            }
        }

        return DatabaseUnavailableReason.Unknown;
    }

    /// <summary>Message explicite, sans secret : cause et action attendue.</summary>
    public static string Message(DatabaseUnavailableReason reason, string target) => reason switch
    {
        DatabaseUnavailableReason.ServerUnreachable =>
            $"Base centrale {target} injoignable (serveur arrêté, réseau, pare-feu ou nom d'hôte). " +
            "Démarrage arrêté : aucune opération n'est possible sans le serveur. Contactez l'administrateur.",
        DatabaseUnavailableReason.TlsValidationFailed =>
            $"Base centrale {target} : certificat du serveur refusé (autorité non reconnue, nom d'hôte différent " +
            "ou certificat expiré). Aucune connexion non vérifiée n'est tentée. Démarrage arrêté.",
        DatabaseUnavailableReason.TlsUnavailable =>
            $"Base centrale {target} : le serveur ne propose pas de connexion chiffrée (TLS). Aucune connexion " +
            "non chiffrée n'est tentée. Démarrage arrêté.",
        DatabaseUnavailableReason.AuthenticationFailed =>
            $"Base centrale {target} : authentification refusée (secret invalide ou rôle inconnu). " +
            "Reconfigurez le poste. Démarrage arrêté.",
        DatabaseUnavailableReason.AuthenticationMethodRefused =>
            $"Base centrale {target} : le serveur demande une authentification autre que SCRAM-SHA-256 " +
            "(MD5 ou mot de passe en clair), refusée par le poste. Démarrage arrêté.",
        DatabaseUnavailableReason.DatabaseNotFound =>
            $"Base centrale {target} introuvable sur le serveur. Démarrage arrêté.",
        DatabaseUnavailableReason.PrivilegedIdentity =>
            $"Base centrale {target} : identité PostgreSQL privilégiée refusée sur un poste. Démarrage arrêté.",
        _ =>
            $"Base centrale {target} : connexion impossible (cause non classée). Démarrage arrêté."
    };

    /// <summary>Cible lisible <c>hôte:port/base</c> — jamais le rôle ni le secret.</summary>
    public static string Describe(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"'{builder.Host}:{builder.Port}/{builder.Database}'";
        }
        catch (ArgumentException)
        {
            return "'(configuration illisible)'";
        }
    }
}
