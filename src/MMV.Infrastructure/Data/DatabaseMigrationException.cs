namespace MMV.Infrastructure.Data;

/// <summary>
/// Erreur explicite et compréhensible levée par le cycle de vie SQLite (P2A-1A) lorsque la base
/// ne peut pas être préparée/migrée en toute sécurité (fichier illisible/corrompu, schéma
/// incohérent, migration en échec). Aucune donnée n'est supprimée : la base d'origine et sa
/// sauvegarde sont conservées pour reprise manuelle.
/// </summary>
public sealed class DatabaseMigrationException : Exception
{
    public DatabaseMigrationException(string message) : base(message)
    {
    }

    public DatabaseMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
