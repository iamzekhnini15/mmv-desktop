namespace MMV.Domain.Exceptions;

/// <summary>
/// Catégorie d'erreur de persistance, dérivée d'une erreur technique (SQLite/EF) et mappée
/// vers un message utilisateur contrôlé. Permet à l'appelant de réagir (réessayer, signaler un
/// doublon, etc.) sans dépendre des types techniques du provider.
/// </summary>
public enum PersistenceErrorCategory
{
    /// <summary>Erreur de persistance non classifiée.</summary>
    Unknown = 0,

    /// <summary>Violation d'unicité (index/clé unique) — doublon.</summary>
    UniqueConstraint = 1,

    /// <summary>Autre violation de contrainte d'intégrité (clé étrangère, NOT NULL, CHECK).</summary>
    ConstraintViolation = 2,

    /// <summary>Base momentanément occupée/verrouillée (SQLITE_BUSY / SQLITE_LOCKED).</summary>
    DatabaseBusy = 3,

    /// <summary>Base inaccessible (ouverture/E-S impossible).</summary>
    ConnectionFailure = 4,

    /// <summary>Conflit de concurrence détecté au commit (écriture concurrente).</summary>
    Concurrency = 5,
}

/// <summary>
/// Erreur de persistance <b>contrôlée</b> : une erreur technique de sauvegarde (par ex.
/// <c>DbUpdateException</c> ou <c>SqliteException</c>) a été interceptée, la transaction a été
/// annulée (aucune écriture partielle), et un message utilisateur assaini est exposé via
/// <see cref="System.Exception.Message"/>. Le détail technique reste disponible dans
/// <see cref="System.Exception.InnerException"/> pour la journalisation.
///
/// <para>
/// Définie dans le domaine (sans dépendance EF/SQLite) afin que les ViewModels actuels — et la
/// future couche Application — puissent l'attraper de façon uniforme.
/// </para>
/// </summary>
public class PersistenceException : Exception
{
    /// <summary>Catégorie de l'erreur de persistance.</summary>
    public PersistenceErrorCategory Category { get; }

    public PersistenceException(
        string message,
        PersistenceErrorCategory category = PersistenceErrorCategory.Unknown,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
    }
}
