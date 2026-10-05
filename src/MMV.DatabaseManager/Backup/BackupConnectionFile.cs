using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Connexion enregistrée de la tâche de sauvegarde planifiée (P4-9, ADR-PROD-DB-011, sur le modèle de D-13) : même
/// enveloppe que la configuration d'un poste — <b>tout</b> le contenu dans un blob DPAPI CurrentUser — mais un
/// fichier distinct, dans le dossier de données du compte Windows dédié qui exécute la tâche. Écrite par
/// <c>configure-backup</c> (rôle de sauvegarde vérifié non privilégié), lue par <c>backup</c> sans option de
/// connexion. Le secret administrateur et celui du migrateur ne sont <b>jamais</b> enregistrés.
/// </summary>
public static class BackupConnectionFile
{
    public const string FileName = "backup-connection.json";

    /// <summary><c>%LOCALAPPDATA%\ManageMyVision\DatabaseManager\backup-connection.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        SqliteDatabasePathResolver.ApplicationFolderName,
        "DatabaseManager",
        FileName);

    public static WorkstationDatabaseSettingsFile CreateDefault() => new(DefaultPath, new DpapiCurrentUserSecretProtector());
}
