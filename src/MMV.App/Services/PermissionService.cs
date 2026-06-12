using MMV.Domain.Enums;

namespace MMV.App.Services;

/// <summary>
/// Implémentation du service de permissions basé sur la matrice de rôles.
/// 
/// Matrice de permissions :
///   Module              | ADMIN           | OPTICIAN        | TECHNICIAN
///   Dashboard           | Accès complet   | Accès complet   | Lecture seule
///   Clients             | CRUD + Export   | CRUD + Export   | Lecture seule
///   Produits            | CRUD + Import   | CRUD + Import   | Lecture + Alerte stock
///   Ordonnances         | CRUD            | CRUD            | Lecture seule
///   Commandes           | CRUD + Workflow | CRUD + Workflow | Changement statut uniquement
///   Ventes (POS)        | CRUD + Rembours | CRUD            | Aucun accès
///   Gestion Utilisateurs| CRUD            | Aucun           | Aucun
///   Rapports            | Tous + Export   | Lecture basique  | Aucun
///   Paramètres          | Config complète | Lecture          | Aucun
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly ISessionService _sessionService;

    public PermissionService(ISessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    }

    private UserRole? CurrentRole => _sessionService.CurrentUser?.Role;

    public bool CanAccessModule(string moduleName)
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return moduleName.ToLowerInvariant() switch
        {
            "dashboard" => true, // Tous les rôles
            "customers" or "clients" => true, // Tous les rôles (lecture pour technicien)
            "products" or "produits" => true, // Tous les rôles
            "prescriptions" or "ordonnances" => true, // Tous les rôles
            "orders" or "commandes" => true, // Tous les rôles
            "sales" or "ventes" or "pos" => CurrentRole != UserRole.Technician,
            "users" or "utilisateurs" => CurrentRole == UserRole.Admin,
            "reports" or "rapports" => CurrentRole != UserRole.Technician,
            "settings" or "paramètres" or "parametres" => CurrentRole != UserRole.Technician,
            "inventory" or "inventaire" => true,
            "notifications" => true,
            _ => CurrentRole == UserRole.Admin
        };
    }

    public bool CanCreate(string entityType)
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return entityType.ToLowerInvariant() switch
        {
            "customer" or "client" => CurrentRole != UserRole.Technician,
            "product" or "produit" => CurrentRole != UserRole.Technician,
            "prescription" or "ordonnance" => CurrentRole != UserRole.Technician,
            "order" or "commande" => CurrentRole != UserRole.Technician,
            "sale" or "vente" => CurrentRole != UserRole.Technician,
            "user" or "utilisateur" => CurrentRole == UserRole.Admin,
            _ => CurrentRole == UserRole.Admin
        };
    }

    public bool CanEdit(string entityType)
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return entityType.ToLowerInvariant() switch
        {
            "customer" or "client" => CurrentRole != UserRole.Technician,
            "product" or "produit" => CurrentRole != UserRole.Technician,
            "prescription" or "ordonnance" => CurrentRole != UserRole.Technician,
            "order" or "commande" => CurrentRole != UserRole.Technician,
            "sale" or "vente" => CurrentRole != UserRole.Technician,
            "user" or "utilisateur" => CurrentRole == UserRole.Admin,
            "orderstatus" or "statutcommande" => true, // Tous les rôles peuvent changer le statut
            _ => CurrentRole == UserRole.Admin
        };
    }

    public bool CanDelete(string entityType)
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return entityType.ToLowerInvariant() switch
        {
            "customer" or "client" => CurrentRole == UserRole.Admin,
            "product" or "produit" => CurrentRole == UserRole.Admin,
            "prescription" or "ordonnance" => CurrentRole != UserRole.Technician,
            "order" or "commande" => CurrentRole == UserRole.Admin,
            "sale" or "vente" => CurrentRole == UserRole.Admin,
            "user" or "utilisateur" => CurrentRole == UserRole.Admin,
            _ => CurrentRole == UserRole.Admin
        };
    }

    public bool CanViewReports()
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return CurrentRole != UserRole.Technician;
    }

    public bool CanChangeOrderStatus()
    {
        // Tous les rôles authentifiés peuvent changer le statut des commandes
        return _sessionService.IsAuthenticated;
    }

    public bool CanRefund()
    {
        if (!_sessionService.IsAuthenticated || CurrentRole == null)
            return false;

        return CurrentRole == UserRole.Admin;
    }
}
