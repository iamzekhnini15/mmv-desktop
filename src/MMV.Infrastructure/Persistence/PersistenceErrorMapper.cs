using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Exceptions;
using Npgsql;

namespace MMV.Infrastructure.Persistence;

/// <summary>
/// Transforme les erreurs techniques de persistance (SQLite / PostgreSQL / EF Core) en
/// <see cref="PersistenceException"/> contrôlée (catégorie + message utilisateur assaini).
///
/// <para>
/// Principe : <b>seules</b> les erreurs réellement liées à la persistance
/// (<see cref="DbUpdateException"/>, <see cref="SqliteException"/>, <see cref="NpgsqlException"/>
/// dans la chaîne) sont transformées. Toute autre exception (règle métier, argument invalide, panne
/// applicative) est renvoyée <b>inchangée</b> afin de ne pas altérer le comportement existant.
/// </para>
///
/// <para>
/// P4-4A1 — Prise en charge PostgreSQL. Les mesures P4-4A0 (spikes E19/E20) ont établi que le
/// <b>type externe ne porte aucune information de classification</b> : une même panne apparaît, selon
/// le chemin, sous <c>DbUpdateException</c>, sous <c>InvalidOperationException</c> ou nue. La
/// classification parcourt donc TOUTE la chaîne <see cref="Exception.InnerException"/> et ne
/// s'appuie que sur des signaux <b>structurés</b> (type d'exception, <c>SqlState</c>,
/// <c>CancellationToken.IsCancellationRequested</c>).
/// </para>
///
/// <para>
/// RÈGLE ABSOLUE — <b>aucune décision par le TEXTE</b>. Ni <see cref="Exception.Message"/>, ni
/// <c>Detail</c>, ni <c>Hint</c>, ni <c>ConstraintName</c> ne sont lus pour choisir une catégorie :
/// un message est localisé et non contractuel. Le détail technique complet (dont
/// <c>PostgresException.ConstraintName</c>) reste accessible via l'exception d'origine, conservée en
/// <see cref="Exception.InnerException"/> de la <see cref="PersistenceException"/> produite.
/// </para>
///
/// <para>
/// Neutralité de couche : ce mapper vit dans l'Infrastructure. Aucun type Npgsql/SQLite ne franchit
/// la frontière — seules la <see cref="PersistenceException"/> et la
/// <see cref="PersistenceErrorCategory"/> du Domain sont exposées (garde O14).
/// </para>
/// </summary>
public static class PersistenceErrorMapper
{
    // ------------------------------------------------------------------
    // Messages utilisateur par catégorie (assainis : jamais de SqlState, de nom de contrainte, de
    // table, de colonne, d'hôte ni de chaîne de connexion). Extraits en constantes pour être
    // partagés entre providers — les libellés SQLite historiques sont conservés à l'identique.
    // ------------------------------------------------------------------

    private const string UniqueConstraintMessage =
        "Un enregistrement avec ces informations existe déjà (doublon). " +
        "Aucune modification n'a été conservée.";

    private const string ConstraintViolationMessage =
        "L'enregistrement viole une contrainte d'intégrité des données. " +
        "Aucune modification n'a été conservée.";

    private const string DatabaseBusyMessage =
        "La base de données est momentanément occupée. Veuillez réessayer. " +
        "Aucune modification n'a été conservée.";

    private const string ConnectionFailureMessage =
        "La base de données est inaccessible. Aucune modification n'a été conservée.";

    private const string UnknownMessage =
        "Une erreur de sauvegarde est survenue. Aucune modification n'a été conservée.";

    /// <summary>
    /// P4-4A1 — Seule catégorie sans libellé réutilisable préexistant (aucun provider ne la
    /// produisait avant PostgreSQL). Volontairement non technique.
    /// </summary>
    private const string ConcurrencyMessage =
        "Une opération concurrente a empêché l'enregistrement. Veuillez réessayer. " +
        "Aucune modification n'a été conservée.";

    /// <summary>Libellé historique de la branche <see cref="DbUpdateException"/> générique.</summary>
    private const string DbUpdateFallbackMessage =
        "L'enregistrement a échoué : les données n'ont pas pu être sauvegardées. " +
        "Aucune modification n'a été conservée.";

    // ------------------------------------------------------------------
    // Codes de résultat SQLite (primaires).
    // ------------------------------------------------------------------

    private const int SqliteConstraint = 19;  // SQLITE_CONSTRAINT (FK, NOT NULL, CHECK, UNIQUE…)
    private const int SqliteBusy = 5;         // SQLITE_BUSY
    private const int SqliteLocked = 6;       // SQLITE_LOCKED
    private const int SqliteIoErr = 10;       // SQLITE_IOERR
    private const int SqliteCantOpen = 14;    // SQLITE_CANTOPEN

    // Codes étendus.
    private const int SqliteConstraintPrimaryKey = 1555; // SQLITE_CONSTRAINT_PRIMARYKEY
    private const int SqliteConstraintUnique = 2067;     // SQLITE_CONSTRAINT_UNIQUE

    // ------------------------------------------------------------------
    // SqlState PostgreSQL (5 caractères, valeur structurée et contractuelle).
    // SqlState prouvés par les mesures P4-1 et/ou P4-4A0 (E19/E20). Aucun code non prouvé ou non
    // décidé n'est classé par hypothèse : tout autre SqlState reste Unknown.
    // ------------------------------------------------------------------

    private const string PgUniqueViolation = "23505";        // unique_violation
    private const string PgForeignKeyViolation = "23503";    // foreign_key_violation
    private const string PgNotNullViolation = "23502";       // not_null_violation
    private const string PgCheckViolation = "23514";         // check_violation
    private const string PgInvalidCatalogName = "3D000";     // invalid_catalog_name (base absente)
    private const string PgDeadlockDetected = "40P01";       // deadlock_detected
    private const string PgLockNotAvailable = "55P03";       // lock_not_available
    private const string PgQueryCanceled = "57014";          // query_canceled
    private const string PgInFailedSqlTransaction = "25P02"; // in_failed_sql_transaction

    /// <summary>
    /// Renvoie une <see cref="PersistenceException"/> si <paramref name="exception"/> est (ou contient)
    /// une erreur de persistance ; sinon renvoie l'exception d'origine inchangée.
    ///
    /// <para>
    /// L'ORDRE des branches ci-dessous est un <b>contrat</b> (P4-4A1) :
    /// <list type="number">
    ///   <item><see cref="PersistenceException"/> déjà mappée : passthrough ;</item>
    ///   <item>annulation cliente réellement demandée : exception d'origine inchangée ;</item>
    ///   <item><see cref="PostgresException"/> : classification par <c>SqlState</c> ;</item>
    ///   <item><see cref="SqliteException"/> : classification historique inchangée ;</item>
    ///   <item><see cref="NpgsqlException"/> sans <c>SqlState</c> exploitable : forme structurelle ;</item>
    ///   <item><see cref="DbUpdateException"/> générique : <c>Unknown</c> (comportement historique) ;</item>
    ///   <item>toute autre exception : inchangée.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static Exception Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // (A) Déjà contrôlée.
        if (exception is PersistenceException)
        {
            return exception;
        }

        // (B) Annulation cliente RÉELLEMENT demandée. Doit précéder toute recherche de
        // PostgresException : la forme mesurée d'une annulation utilisateur est
        // OperationCanceledException -> PostgresException 57014. Un 57014 SANS jeton annulé est un
        // cas différent (annulation subie côté serveur), classé plus bas en DatabaseBusy.
        if (IsRequestedClientCancellation(exception))
        {
            return exception;
        }

        // (C) PostgreSQL : SqlState structuré, jamais le texte.
        var postgres = FindInChain<PostgresException>(exception);
        if (postgres != null)
        {
            var (category, message) = ClassifyPostgres(postgres.SqlState);
            return new PersistenceException(message, category, exception);
        }

        // (D) SQLite : comportement historique strictement inchangé.
        var sqlite = FindInChain<SqliteException>(exception);
        if (sqlite != null)
        {
            var (category, message) = Classify(sqlite);
            return new PersistenceException(message, category, exception);
        }

        // (E) PostgreSQL sans SqlState exploitable (timeout client, connexion refusée/perdue).
        // Strictement conditionnée à la présence d'une NpgsqlException : une IOException, une
        // SocketException ou une TimeoutException ARBITRAIRE ne doit jamais devenir une erreur de
        // persistance.
        if (FindInChain<NpgsqlException>(exception) != null)
        {
            var (category, message) = ClassifyNpgsqlWithoutSqlState(exception);
            return new PersistenceException(message, category, exception);
        }

        // (F) DbUpdateException générique : aucun signal provider exploitable.
        // Jamais assimilée à une violation de contrainte — E20 a montré qu'un timeout d'écriture est
        // lui aussi enveloppé par une DbUpdateException.
        if (FindInChain<DbUpdateException>(exception) != null)
        {
            return new PersistenceException(
                DbUpdateFallbackMessage,
                PersistenceErrorCategory.Unknown,
                exception);
        }

        // (G) Pas une erreur de persistance : inchangée.
        return exception;
    }

    /// <summary>
    /// Vrai si la chaîne contient une <see cref="OperationCanceledException"/> (y compris
    /// <see cref="TaskCanceledException"/>, qui en dérive) dont le <see cref="CancellationToken"/> a
    /// réellement été déclenché. Dans ce cas l'annulation est relayée INCHANGÉE : ce n'est pas une
    /// panne de persistance, et l'appelant doit pouvoir l'attraper comme une annulation.
    /// </summary>
    private static bool IsRequestedClientCancellation(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is OperationCanceledException canceled
                && canceled.CancellationToken.IsCancellationRequested)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Classification PostgreSQL par <c>SqlState</c> uniquement. Seuls les codes prouvés par P4-1
    /// et/ou P4-4A0 et explicitement décidés sont classés ; tout autre reste délibérément
    /// <see cref="PersistenceErrorCategory.Unknown"/> : aucune catégorie n'est attribuée sur hypothèse.
    /// </summary>
    private static (PersistenceErrorCategory category, string message) ClassifyPostgres(string? sqlState)
        => sqlState switch
        {
            PgUniqueViolation => (PersistenceErrorCategory.UniqueConstraint, UniqueConstraintMessage),

            PgForeignKeyViolation or PgNotNullViolation or PgCheckViolation
                => (PersistenceErrorCategory.ConstraintViolation, ConstraintViolationMessage),

            PgInvalidCatalogName => (PersistenceErrorCategory.ConnectionFailure, ConnectionFailureMessage),

            PgDeadlockDetected => (PersistenceErrorCategory.Concurrency, ConcurrencyMessage),

            // 55P03 : verrou indisponible. 57014 : requête annulée côté serveur SANS annulation
            // cliente demandée (le cas « annulation demandée » a déjà été relayé en branche B).
            PgLockNotAvailable or PgQueryCanceled
                => (PersistenceErrorCategory.DatabaseBusy, DatabaseBusyMessage),

            // 25P02 : transaction déjà avortée. C'est une CONSÉQUENCE, pas la cause : la classer en
            // Concurrency serait faux. Politique transactionnelle reportée (P4-4B).
            PgInFailedSqlTransaction => (PersistenceErrorCategory.Unknown, UnknownMessage),

            _ => (PersistenceErrorCategory.Unknown, UnknownMessage),
        };

    /// <summary>
    /// PostgreSQL sans <c>SqlState</c> : la panne est survenue au niveau du transport ou du délai
    /// client, avant toute réponse structurée du serveur. Classification par TYPE présent dans la
    /// chaîne. <c>ErrorCode</c>, <c>IsTransient</c> et <c>Connection.State</c> sont délibérément
    /// ignorés : P4-4A0/E20 a montré qu'ils ne sont pas des discriminants fiables et stables entre
    /// les différents chemins ADO.NET et EF Core.
    /// </summary>
    private static (PersistenceErrorCategory category, string message) ClassifyNpgsqlWithoutSqlState(
        Exception exception)
    {
        // Connexion refusée / réinitialisée.
        if (FindInChain<SocketException>(exception) != null)
        {
            return (PersistenceErrorCategory.ConnectionFailure, ConnectionFailureMessage);
        }

        // Perte de connexion en cours d'échange. EndOfStreamException DÉRIVE d'IOException : les deux
        // formes mesurées sont donc couvertes par ce seul test.
        if (FindInChain<IOException>(exception) != null)
        {
            return (PersistenceErrorCategory.ConnectionFailure, ConnectionFailureMessage);
        }

        // Délai d'attente client (command timeout / lock timeout côté client).
        if (FindInChain<TimeoutException>(exception) != null)
        {
            return (PersistenceErrorCategory.DatabaseBusy, DatabaseBusyMessage);
        }

        return (PersistenceErrorCategory.Unknown, UnknownMessage);
    }

    private static (PersistenceErrorCategory category, string message) Classify(SqliteException sqlite)
    {
        // Les codes étendus sont plus précis que le code primaire lorsqu'ils sont renseignés.
        switch (sqlite.SqliteExtendedErrorCode)
        {
            case SqliteConstraintUnique:
            case SqliteConstraintPrimaryKey:
                return (PersistenceErrorCategory.UniqueConstraint, UniqueConstraintMessage);
        }

        switch (sqlite.SqliteErrorCode)
        {
            case SqliteConstraint:
                return (PersistenceErrorCategory.ConstraintViolation, ConstraintViolationMessage);
            case SqliteBusy:
            case SqliteLocked:
                return (PersistenceErrorCategory.DatabaseBusy, DatabaseBusyMessage);
            case SqliteIoErr:
            case SqliteCantOpen:
                return (PersistenceErrorCategory.ConnectionFailure, ConnectionFailureMessage);
            default:
                return (PersistenceErrorCategory.Unknown, UnknownMessage);
        }
    }

    private static T? FindInChain<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
