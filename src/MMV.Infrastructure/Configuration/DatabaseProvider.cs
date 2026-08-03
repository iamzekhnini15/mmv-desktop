namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Fournisseur de base de données sélectionnable par configuration (P4-3, ADR-PROD-DB-002).
///
/// La liste est volontairement <b>fermée</b> aux deux providers exposés par P4-3 : SQLite pour les
/// usages locaux autorisés et PostgreSQL comme provider serveur officiellement retenu pour la V1.
/// SQL Server Express a été <i>rejeté pour la V1</i> et n'est donc pas exposé ici.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>
    /// SQLite — fournisseur par défaut : développement, tests, démonstration et mono-poste local.
    /// Le fichier de base reste résolu par <see cref="Data.SqliteDatabasePathResolver"/>.
    /// </summary>
    Sqlite = 0,

    /// <summary>
    /// PostgreSQL — fournisseur serveur retenu pour la V1 multi-poste (ADR-PROD-DB-002).
    /// Sélectionnable dans la composition EF dès P4-3 ; la préparation et les migrations serveur
    /// n'existent pas encore (P4-5/P4-6).
    /// </summary>
    PostgreSql = 1
}
