namespace MMV.App.Services;

/// <summary>
/// Service de vérification des permissions basé sur le rôle de l'utilisateur connecté.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Vérifie si l'utilisateur peut accéder à un module.
    /// </summary>
    bool CanAccessModule(string moduleName);

    /// <summary>
    /// Vérifie si l'utilisateur peut créer une entité.
    /// </summary>
    bool CanCreate(string entityType);

    /// <summary>
    /// Vérifie si l'utilisateur peut modifier une entité.
    /// </summary>
    bool CanEdit(string entityType);

    /// <summary>
    /// Vérifie si l'utilisateur peut supprimer/désactiver une entité.
    /// </summary>
    bool CanDelete(string entityType);

    /// <summary>
    /// Vérifie si l'utilisateur peut voir les rapports.
    /// </summary>
    bool CanViewReports();

    /// <summary>
    /// Vérifie si l'utilisateur peut changer le statut d'une commande.
    /// </summary>
    bool CanChangeOrderStatus();

    /// <summary>
    /// Vérifie si l'utilisateur peut effectuer des remboursements.
    /// </summary>
    bool CanRefund();
}
