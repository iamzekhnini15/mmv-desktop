using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Exceptions;

namespace MMV.Infrastructure.Persistence;

/// <summary>
/// Transforme les erreurs techniques de persistance (SQLite / EF Core) en
/// <see cref="PersistenceException"/> contrôlée (catégorie + message utilisateur assaini).
///
/// <para>
/// Principe : <b>seules</b> les erreurs réellement liées à la persistance
/// (<see cref="DbUpdateException"/>, <see cref="SqliteException"/> dans la chaîne) sont
/// transformées. Toute autre exception (règle métier, argument invalide, panne applicative)
/// est renvoyée <b>inchangée</b> afin de ne pas altérer le comportement existant.
/// </para>
/// </summary>
public static class PersistenceErrorMapper
{
    // Codes de résultat SQLite (primaires).
    private const int SqliteConstraint = 19;  // SQLITE_CONSTRAINT (FK, NOT NULL, CHECK, UNIQUE…)
    private const int SqliteBusy = 5;         // SQLITE_BUSY
    private const int SqliteLocked = 6;       // SQLITE_LOCKED
    private const int SqliteIoErr = 10;       // SQLITE_IOERR
    private const int SqliteCantOpen = 14;    // SQLITE_CANTOPEN

    // Codes étendus.
    private const int SqliteConstraintPrimaryKey = 1555; // SQLITE_CONSTRAINT_PRIMARYKEY
    private const int SqliteConstraintUnique = 2067;     // SQLITE_CONSTRAINT_UNIQUE

    /// <summary>
    /// Renvoie une <see cref="PersistenceException"/> si <paramref name="exception"/> est (ou contient)
    /// une erreur de persistance ; sinon renvoie l'exception d'origine inchangée.
    /// </summary>
    public static Exception Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Déjà contrôlée.
        if (exception is PersistenceException)
        {
            return exception;
        }

        var sqlite = FindInChain<SqliteException>(exception);
        if (sqlite != null)
        {
            var (category, message) = Classify(sqlite);
            return new PersistenceException(message, category, exception);
        }

        if (FindInChain<DbUpdateException>(exception) != null)
        {
            return new PersistenceException(
                "L'enregistrement a échoué : les données n'ont pas pu être sauvegardées. " +
                "Aucune modification n'a été conservée.",
                PersistenceErrorCategory.Unknown,
                exception);
        }

        // Pas une erreur de persistance → inchangée.
        return exception;
    }

    private static (PersistenceErrorCategory category, string message) Classify(SqliteException sqlite)
    {
        // Les codes étendus sont plus précis que le code primaire lorsqu'ils sont renseignés.
        switch (sqlite.SqliteExtendedErrorCode)
        {
            case SqliteConstraintUnique:
            case SqliteConstraintPrimaryKey:
                return (PersistenceErrorCategory.UniqueConstraint,
                    "Un enregistrement avec ces informations existe déjà (doublon). " +
                    "Aucune modification n'a été conservée.");
        }

        switch (sqlite.SqliteErrorCode)
        {
            case SqliteConstraint:
                return (PersistenceErrorCategory.ConstraintViolation,
                    "L'enregistrement viole une contrainte d'intégrité des données. " +
                    "Aucune modification n'a été conservée.");
            case SqliteBusy:
            case SqliteLocked:
                return (PersistenceErrorCategory.DatabaseBusy,
                    "La base de données est momentanément occupée. Veuillez réessayer. " +
                    "Aucune modification n'a été conservée.");
            case SqliteIoErr:
            case SqliteCantOpen:
                return (PersistenceErrorCategory.ConnectionFailure,
                    "La base de données est inaccessible. Aucune modification n'a été conservée.");
            default:
                return (PersistenceErrorCategory.Unknown,
                    "Une erreur de sauvegarde est survenue. Aucune modification n'a été conservée.");
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
