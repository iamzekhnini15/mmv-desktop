# ManageMyVision — Architecture SaaS & Système de Licences

> **Objectif** : Transformer ManageMyVision en produit SaaS vendu par abonnement.  
> Les opticiens souscrivent un plan sur le site web, téléchargent l'app desktop, et leurs fonctionnalités sont limitées selon leur plan.

---

## Table des matières

1. [Vue d'ensemble](#1-vue-densemble)
2. [Les 3 plans d'abonnement](#2-les-3-plans-dabonnement)
3. [Architecture technique globale](#3-architecture-technique-globale)
4. [Le site web (front-end)](#4-le-site-web-front-end)
5. [L'API serveur (back-end)](#5-lapi-serveur-back-end)
6. [Intégration dans l'app desktop](#6-intégration-dans-lapp-desktop)
7. [Flux complet : de l'inscription à l'utilisation](#7-flux-complet--de-linscription-à-lutilisation)
8. [Système de clé de licence](#8-système-de-clé-de-licence)
9. [Validation de licence dans l'app](#9-validation-de-licence-dans-lapp)
10. [Enforcement des limites par plan](#10-enforcement-des-limites-par-plan)
11. [Gestion du paiement (Stripe)](#11-gestion-du-paiement-stripe)
12. [Base de données serveur](#12-base-de-données-serveur)
13. [Sécurité](#13-sécurité)
14. [Scénarios détaillés](#14-scénarios-détaillés)
15. [Planning d'implémentation](#15-planning-dimplémentation)
16. [FAQ technique](#16-faq-technique)

---

## 1. Vue d'ensemble

```
┌─────────────────────────────────────────────────────────────────────┐
│                        INFRASTRUCTURE SAAS                         │
│                                                                    │
│  ┌──────────────┐    ┌──────────────────┐    ┌──────────────────┐  │
│  │  Site Web     │───▶│  API Serveur     │◀───│  App Desktop     │  │
│  │  (Next.js)    │    │  (ASP.NET Core)  │    │  (Avalonia)      │  │
│  │              │    │                  │    │                  │  │
│  │ • Inscription │    │ • Auth           │    │ • Vérif licence  │  │
│  │ • Plans       │    │ • Licences       │    │ • Limites plan   │  │
│  │ • Paiement    │    │ • Abonnements    │    │ • Fonctionnalités│  │
│  │ • Télécharg.  │    │ • Webhooks Stripe│    │ • Sync périodique│  │
│  └──────────────┘    └──────────────────┘    └──────────────────┘  │
│                             │                                      │
│                      ┌──────▼──────┐                               │
│                      │  PostgreSQL  │                               │
│                      │  (Cloud DB)  │                               │
│                      └─────────────┘                               │
│                                                                    │
│              ┌──────────────────────────┐                          │
│              │       Stripe            │                          │
│              │  (Paiement récurrent)    │                          │
│              └──────────────────────────┘                          │
└─────────────────────────────────────────────────────────────────────┘
```

**Principe clé** : L'opticien crée un compte sur le site web, paie un abonnement, reçoit une **clé de licence**, télécharge l'app, et entre sa clé de licence au premier lancement. L'app vérifie la validité de la licence auprès de l'API serveur.

---

## 2. Les 3 plans d'abonnement

### Tableau comparatif

| Fonctionnalité                  | 🟢 Starter (29€/mois) | 🔵 Pro (59€/mois) | 🟣 Enterprise (99€/mois) |
|---------------------------------|------------------------|--------------------|--------------------------| 
| **Nombre de clients**           | 50 max                 | 200 max            | Illimité                 |
| **Nombre d'utilisateurs (staff)**| 1                     | 3                  | Illimité                 |
| **Produits**                    | 100 max                | 500 max            | Illimité                 |
| **Ordonnances**                 | ✅                     | ✅                 | ✅                       |
| **Commandes fournisseurs**      | ❌                     | ✅                 | ✅                       |
| **Point de vente (POS)**        | ❌                     | ✅                 | ✅                       |
| **Rapports & statistiques**     | Basique (CA)           | Complets           | Complets + Export PDF     |
| **Gestion du stock**            | ❌                     | ✅                 | ✅                       |
| **Multi-utilisateurs (rôles)**  | ❌ (1 admin)           | ✅                 | ✅                       |
| **Notifications automatiques**  | ❌                     | ❌                 | ✅                       |
| **Support**                     | Email                  | Email + Chat       | Prioritaire + Téléphone  |
| **Mises à jour**                | ✅                     | ✅                 | ✅ + accès anticipé      |
| **Sauvegarde cloud**            | ❌                     | ❌                 | ✅                       |

### Détail des limites techniques par plan

```json
{
  "starter": {
    "planId": "starter",
    "maxClients": 50,
    "maxUsers": 1,
    "maxProducts": 100,
    "features": ["clients", "products", "prescriptions"],
    "hasOrders": false,
    "hasSales": false,
    "hasReports": "basic",
    "hasStock": false,
    "hasMultiUser": false,
    "hasNotifications": false,
    "hasCloudBackup": false
  },
  "pro": {
    "planId": "pro",
    "maxClients": 200,
    "maxUsers": 3,
    "maxProducts": 500,
    "features": ["clients", "products", "prescriptions", "orders", "sales", "stock", "reports"],
    "hasOrders": true,
    "hasSales": true,
    "hasReports": "full",
    "hasStock": true,
    "hasMultiUser": true,
    "hasNotifications": false,
    "hasCloudBackup": false
  },
  "enterprise": {
    "planId": "enterprise",
    "maxClients": -1,
    "maxUsers": -1,
    "maxProducts": -1,
    "features": ["clients", "products", "prescriptions", "orders", "sales", "stock", "reports", "notifications", "cloud_backup"],
    "hasOrders": true,
    "hasSales": true,
    "hasReports": "full_export",
    "hasStock": true,
    "hasMultiUser": true,
    "hasNotifications": true,
    "hasCloudBackup": true
  }
}
```

> `-1` signifie illimité.

---

## 3. Architecture technique globale

### Les 3 composants

| Composant          | Technologie recommandée        | Rôle                                            |
|--------------------|--------------------------------|--------------------------------------------------|
| **Site web**       | Next.js (React) + Tailwind CSS | Vitrine, inscription, paiement, portail client   |
| **API serveur**    | ASP.NET Core 8 Web API         | Auth, gestion licences, webhooks Stripe          |
| **App desktop**    | Avalonia (existant)            | L'application vendue, vérifie la licence         |

### Pourquoi ces choix ?

- **ASP.NET Core** pour l'API : même écosystème .NET que l'app desktop, partage possible d'entités/DTOs
- **Next.js** pour le site : SEO, performance, écosystème React riche pour les landing pages
- **Stripe** pour le paiement : standard du marché SaaS, gère les abonnements récurrents, webhooks fiables

---

## 4. Le site web (front-end)

### Pages du site

```
managemyvision.fr/
├── /                        → Landing page (présentation, plans, CTA)
├── /pricing                 → Page de tarification détaillée
├── /signup                  → Inscription (email + mot de passe)
├── /login                   → Connexion au portail
├── /checkout/:planId        → Paiement Stripe Checkout
├── /dashboard               → Portail client (après connexion)
│   ├── /dashboard/license   → Voir sa clé de licence
│   ├── /dashboard/plan      → Gérer son plan (upgrade/downgrade)
│   ├── /dashboard/billing   → Historique facturation
│   └── /dashboard/download  → Télécharger l'app
├── /download                → Page de téléchargement (après paiement)
└── /docs                    → Documentation utilisateur
```

### Flux d'inscription sur le site

```
┌─────────────┐     ┌─────────────┐     ┌─────────────────┐     ┌──────────────┐
│  Landing     │────▶│  Sign Up     │────▶│  Choisir plan    │────▶│  Paiement    │
│  Page        │     │  (email/pwd) │     │  (Starter/Pro/   │     │  (Stripe     │
│              │     │              │     │   Enterprise)    │     │   Checkout)  │
└─────────────┘     └─────────────┘     └─────────────────┘     └──────┬───────┘
                                                                       │
                    ┌─────────────────────────────────────────────────┘
                    ▼
     ┌──────────────────────┐     ┌──────────────────┐
     │  Confirmation        │────▶│  Portail client   │
     │  • Clé de licence    │     │  • Télécharger app│
     │  • Lien télécharg.   │     │  • Voir licence   │
     │  • Email de bienvenue│     │  • Gérer abo      │
     └──────────────────────┘     └──────────────────┘
```

---

## 5. L'API serveur (back-end)

### Endpoints principaux

```
API: https://api.managemyvision.fr/v1

POST   /auth/register          → Inscription (email, password, nom, prénom)
POST   /auth/login             → Connexion → retourne un JWT
POST   /auth/refresh           → Rafraîchir le JWT

GET    /license/validate       → Valider une clé de licence (appelé par l'app desktop)
GET    /license/info           → Infos sur la licence (plan, limites, expiration)
POST   /license/activate       → Activer la licence sur une machine (hardware ID)

GET    /subscription           → Infos sur l'abonnement en cours
POST   /subscription/create    → Créer un abonnement (redirige vers Stripe)
POST   /subscription/cancel    → Annuler un abonnement
POST   /subscription/change    → Changer de plan

POST   /webhooks/stripe        → Webhook Stripe (paiements, échecs, annulations)

GET    /download/:platform     → URL de téléchargement de l'app (.exe, .msi)
```

### Modèle de réponse `/license/validate`

C'est l'endpoint le plus important — appelé par l'app desktop au démarrage.

```json
// POST https://api.managemyvision.fr/v1/license/validate
// Body: { "licenseKey": "MMV-XXXX-XXXX-XXXX-XXXX", "hardwareId": "abc123..." }

// Réponse succès :
{
  "valid": true,
  "plan": "pro",
  "organizationName": "Optique Dupont",
  "expiresAt": "2026-03-14T00:00:00Z",
  "limits": {
    "maxClients": 200,
    "maxUsers": 3,
    "maxProducts": 500,
    "features": ["clients", "products", "prescriptions", "orders", "sales", "stock", "reports"]
  },
  "gracePeriodDays": 7
}

// Réponse échec :
{
  "valid": false,
  "reason": "subscription_expired",
  "message": "Votre abonnement a expiré. Veuillez renouveler sur managemyvision.fr"
}
```

---

## 6. Intégration dans l'app desktop

### Nouveau flux de démarrage de l'app

```
┌──────────────────┐
│  Lancement app   │
└────────┬─────────┘
         │
         ▼
┌──────────────────────┐     Non      ┌────────────────────────┐
│ Clé de licence        │────────────▶│  Écran "Entrer licence" │
│ enregistrée localement?│             │  (input + bouton)       │
└────────┬─────────────┘              └───────────┬────────────┘
         │ Oui                                     │
         ▼                                         ▼
┌────────────────────────────────────────────────────────┐
│  Appel API : POST /license/validate                    │
│  { licenseKey: "MMV-...", hardwareId: "..." }          │
└────────────────────────┬──────────────────────────────┘
                         │
              ┌──────────┴──────────┐
              │                     │
         valid: true           valid: false
              │                     │
              ▼                     ▼
┌──────────────────┐    ┌───────────────────────┐
│ Stocker limites   │    │ Afficher message       │
│ du plan localement│    │ "Licence invalide /    │
│                  │    │  expirée / ..."        │
│ → Écran Login     │    │ → Bouton "Renouveler"  │
│ → App normale     │    │ → Bouton "Réessayer"   │
└──────────────────┘    └───────────────────────┘
```

### Fichiers concernés dans l'app desktop

```
src/MMV.App/
├── Services/
│   ├── ILicenseService.cs        → Interface du service de licence
│   ├── LicenseService.cs         → Implémentation (appels HTTP vers l'API)
│   ├── ILicenseStore.cs          → Stockage local chiffré de la clé
│   └── LicenseStore.cs           → Implémentation (fichier chiffré local)
├── ViewModels/
│   ├── LicenseActivationViewModel.cs  → VM de l'écran d'activation
│   └── LicenseExpiredViewModel.cs     → VM de l'écran "licence expirée"
├── Views/
│   ├── LicenseActivationView.axaml    → UI saisie de clé
│   └── LicenseExpiredView.axaml       → UI licence expirée
└── Models/
    └── LicensePlan.cs            → Modèle local des limites du plan

src/MMV.Domain/
├── Entities/
│   └── LicenseInfo.cs            → Entité représentant les infos de licence
└── Enums/
    └── PlanType.cs               → Enum: Starter, Pro, Enterprise
```

### Nouvel enum `PlanType`

```csharp
namespace MMV.Domain.Enums;

public enum PlanType
{
    Starter,
    Pro,
    Enterprise
}
```

### Modèle `LicenseInfo` (stocké localement)

```csharp
namespace MMV.Domain.Entities;

public class LicenseInfo
{
    public string LicenseKey { get; set; } = string.Empty;
    public PlanType Plan { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int MaxClients { get; set; }
    public int MaxUsers { get; set; }
    public int MaxProducts { get; set; }
    public List<string> Features { get; set; } = new();
    public DateTime LastValidated { get; set; }
    public int GracePeriodDays { get; set; }
}
```

### Interface `ILicenseService`

```csharp
namespace MMV.App.Services;

public interface ILicenseService
{
    /// <summary>
    /// Valide la clé de licence auprès du serveur.
    /// </summary>
    Task<LicenseValidationResult> ValidateLicenseAsync(string licenseKey);

    /// <summary>
    /// Retourne les infos de licence en cache.
    /// </summary>
    LicenseInfo? GetCachedLicense();

    /// <summary>
    /// Vérifie si une fonctionnalité est disponible dans le plan actuel.
    /// </summary>
    bool IsFeatureAvailable(string featureName);

    /// <summary>
    /// Vérifie si la limite d'entités (clients, produits...) est atteinte.
    /// </summary>
    bool HasReachedLimit(string entityType, int currentCount);

    /// <summary>
    /// Le plan actuel.
    /// </summary>
    PlanType CurrentPlan { get; }

    /// <summary>
    /// Indique si la licence est valide.
    /// </summary>
    bool IsLicenseValid { get; }
}
```

---

## 7. Flux complet : de l'inscription à l'utilisation

### Scénario : Un opticien souscrit au plan Pro

```
ÉTAPE 1 : INSCRIPTION SUR LE SITE WEB
═══════════════════════════════════════
L'opticien visite managemyvision.fr
  → Clique sur "Essai gratuit" ou "Voir les plans"
  → Page /signup : remplit email, mot de passe, nom de la boutique
  → Un compte est créé dans la BD serveur
  → Il est redirigé vers la page des plans (/pricing)

ÉTAPE 2 : CHOIX DU PLAN ET PAIEMENT
════════════════════════════════════
  → Il choisit le plan "Pro" à 59€/mois
  → Clic sur "Souscrire" → redirigé vers Stripe Checkout
  → Il entre sa carte bancaire
  → Stripe confirme le paiement → webhook vers notre API
  → Notre API :
      1. Crée un enregistrement "Subscription" (plan=pro, status=active)
      2. Génère une clé de licence unique : "MMV-A7K2-F9X1-D3M8-W5P6"
      3. Associe la licence au compte de l'opticien
      4. Envoie un email de bienvenue avec :
         - La clé de licence
         - Le lien de téléchargement
         - Un guide de démarrage rapide

ÉTAPE 3 : TÉLÉCHARGEMENT DE L'APP
═══════════════════════════════════
  → L'opticien va sur /dashboard/download (ou le lien dans l'email)
  → Il télécharge le fichier ManageMyVision-Setup.exe
  → Il installe l'application sur son PC

ÉTAPE 4 : PREMIER LANCEMENT — ACTIVATION
═════════════════════════════════════════
  → Il lance ManageMyVision pour la première fois
  → L'app affiche l'écran "Activation de licence" :
      ┌──────────────────────────────────────┐
      │    🔑 Activer ManageMyVision         │
      │                                      │
      │    Clé de licence :                  │
      │    ┌────────────────────────────┐    │
      │    │ MMV-A7K2-F9X1-D3M8-W5P6   │    │
      │    └────────────────────────────┘    │
      │                                      │
      │    [     Activer ma licence      ]   │
      │                                      │
      │    Pas encore de licence ?            │
      │    → Souscrire sur managemyvision.fr │
      └──────────────────────────────────────┘
  → Il colle sa clé de licence
  → L'app envoie la clé + un identifiant hardware au serveur API
  → Le serveur vérifie :
      ✓ La clé existe
      ✓ L'abonnement est actif (payé)
      ✓ Le nombre de machines activées < limite
  → Le serveur répond avec les limites du plan Pro :
      { maxClients: 200, maxUsers: 3, maxProducts: 500, features: [...] }
  → L'app stocke ces infos localement (fichier chiffré)
  → L'app affiche l'écran de login

ÉTAPE 5 : CONNEXION ET UTILISATION
═══════════════════════════════════
  → L'écran de login apparaît
  → Il se connecte avec le compte admin créé par défaut
      (username: admin, password: à changer au premier login)
  → Il accède à l'application avec les fonctionnalités du plan Pro :
      ✓ Jusqu'à 200 clients
      ✓ 3 utilisateurs (staff)
      ✓ 500 produits
      ✓ Commandes fournisseurs ✓
      ✓ Point de vente ✓
      ✓ Rapports complets ✓
      ✓ Gestion du stock ✓

ÉTAPE 6 : UTILISATION QUOTIDIENNE
══════════════════════════════════
  → Chaque jour, au démarrage de l'app :
      1. Vérifie la licence auprès du serveur (si internet disponible)
      2. Si OK → met à jour le cache local → continue
      3. Si pas d'internet → utilise le cache local (valide pendant 7 jours)
      4. Si licence expirée → affiche l'écran "Licence expirée"

  → Quand l'opticien essaie de créer un 201ème client :
      ┌──────────────────────────────────────────┐
      │  ⚠️ Limite de plan atteinte              │
      │                                          │
      │  Votre plan Pro est limité à 200 clients.│
      │  Vous avez actuellement 200 clients.     │
      │                                          │
      │  Pour ajouter plus de clients, passez    │
      │  au plan Enterprise.                     │
      │                                          │
      │  [  Mettre à niveau  ]  [  Fermer  ]    │
      └──────────────────────────────────────────┘
```

---

## 8. Système de clé de licence

### Format de la clé

```
MMV-XXXX-XXXX-XXXX-XXXX

Où X = caractère alphanumérique (A-Z, 0-9)
Préfixe "MMV-" pour identifier la marque
Exemple : MMV-A7K2-F9X1-D3M8-W5P6
```

### Génération côté serveur

```csharp
public static string GenerateLicenseKey()
{
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Pas de 0/O/1/I/L
    var random = RandomNumberGenerator.Create();
    var bytes = new byte[16];
    random.GetBytes(bytes);

    var segments = new string[4];
    for (int i = 0; i < 4; i++)
    {
        var segment = new char[4];
        for (int j = 0; j < 4; j++)
        {
            segment[j] = chars[bytes[i * 4 + j] % chars.Length];
        }
        segments[i] = new string(segment);
    }

    return $"MMV-{string.Join("-", segments)}";
}
```

### Stockage local dans l'app desktop

La clé de licence et les infos du plan sont stockées dans un **fichier chiffré** sur le disque :

```
%LOCALAPPDATA%/ManageMyVision/license.dat
```

Ce fichier est chiffré avec **DPAPI** (Data Protection API de Windows) pour que seul l'utilisateur Windows courant puisse le lire :

```csharp
public class LicenseStore : ILicenseStore
{
    private static readonly string LicensePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ManageMyVision", "license.dat");

    public void Save(LicenseInfo license)
    {
        var json = JsonSerializer.Serialize(license);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(LicensePath, encrypted);
    }

    public LicenseInfo? Load()
    {
        if (!File.Exists(LicensePath)) return null;
        var encrypted = File.ReadAllBytes(LicensePath);
        var decrypted = ProtectedData.Unprotect(
            encrypted, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<LicenseInfo>(
            Encoding.UTF8.GetString(decrypted));
    }
}
```

---

## 9. Validation de licence dans l'app

### Quand l'app valide-t-elle la licence ?

| Moment                          | Comportement                                                  |
|---------------------------------|---------------------------------------------------------------|
| **Premier lancement**           | Écran d'activation → appel API obligatoire                    |
| **Chaque démarrage**            | Appel API en arrière-plan, utilise le cache si hors ligne     |
| **Toutes les 24h (app ouverte)**| Vérification périodique en arrière-plan                       |
| **Création d'entité (client…)** | Vérification locale des limites du plan                       |
| **Ajout d'utilisateur**         | Vérification locale du nombre max d'utilisateurs              |

### Mode hors-ligne (grace period)

L'app peut fonctionner **sans internet pendant 7 jours** grâce au cache local :

```
Dernière validation réussie : 10 février 2026
Grace period : 7 jours
Limite hors-ligne : 17 février 2026

Si on est le 15 février → OK, l'app fonctionne
Si on est le 18 février → L'app demande une connexion internet
```

### Logique de validation au démarrage

```csharp
public async Task<LicenseStatus> CheckLicenseOnStartup()
{
    var cached = _licenseStore.Load();

    // Pas de licence → écran d'activation
    if (cached == null)
        return LicenseStatus.NotActivated;

    // Essayer de valider en ligne
    try
    {
        var result = await _httpClient.PostAsync("/license/validate", ...);
        if (result.Valid)
        {
            _licenseStore.Save(result.LicenseInfo); // Rafraîchir le cache
            return LicenseStatus.Valid;
        }
        else
        {
            return LicenseStatus.Invalid(result.Reason);
        }
    }
    catch (HttpRequestException)
    {
        // Pas d'internet → vérifier le cache
        var daysSinceValidation = (DateTime.UtcNow - cached.LastValidated).TotalDays;
        if (daysSinceValidation <= cached.GracePeriodDays)
        {
            return LicenseStatus.ValidOffline(cached);
        }
        else
        {
            return LicenseStatus.GracePeriodExpired;
        }
    }
}
```

---

## 10. Enforcement des limites par plan

### Comment l'app empêche de dépasser les limites

L'enforcement se fait à **deux niveaux** :

#### Niveau 1 : UI (masquer les fonctionnalités non disponibles)

Le `IPermissionService` existant est étendu pour prendre en compte le plan de licence en plus du rôle utilisateur :

```csharp
// Avant (Sprint 10 actuel) — basé uniquement sur le rôle
public bool CanAccessModule(string moduleName) 
{
    return moduleName switch {
        "Sales" => CurrentRole != UserRole.Technician,
        // ...
    };
}

// Après (avec licence) — basé sur rôle ET plan
public bool CanAccessModule(string moduleName) 
{
    // Vérifier d'abord le plan
    if (!_licenseService.IsFeatureAvailable(moduleName))
        return false;

    // Puis vérifier le rôle
    return moduleName switch {
        "Sales" => CurrentRole != UserRole.Technician,
        // ...
    };
}
```

**Résultat concret** : si l'opticien a un plan Starter, les boutons "Ventes", "Commandes", "Stock" n'apparaissent pas du tout dans la sidebar.

#### Niveau 2 : Logique métier (bloquer la création au-delà des limites)

```csharp
// Dans CustomerService par exemple :
public async Task<Customer> CreateCustomerAsync(Customer customer)
{
    // Vérifier la limite du plan
    var currentCount = await _customerRepository.CountAsync();
    if (_licenseService.HasReachedLimit("clients", currentCount))
    {
        throw new PlanLimitExceededException(
            "clients", 
            _licenseService.GetLimit("clients"), 
            currentCount);
    }

    // ... logique normale de création
}
```

### Mapping fonctionnalités → plan

```csharp
public bool IsFeatureAvailable(string featureName)
{
    var plan = _licenseInfo?.Plan ?? PlanType.Starter;

    return featureName.ToLowerInvariant() switch
    {
        "clients" or "customers"     => true,  // Tous les plans
        "products" or "produits"     => true,  // Tous les plans
        "prescriptions"              => true,  // Tous les plans
        "orders" or "commandes"      => plan >= PlanType.Pro,
        "sales" or "ventes" or "pos" => plan >= PlanType.Pro,
        "stock" or "inventory"       => plan >= PlanType.Pro,
        "reports" or "rapports"      => true,  // Tous, mais "basic" pour Starter
        "reports_export"             => plan >= PlanType.Enterprise,
        "notifications"              => plan >= PlanType.Enterprise,
        "cloud_backup"               => plan >= PlanType.Enterprise,
        "multi_user"                 => plan >= PlanType.Pro,
        _ => false
    };
}
```

---

## 11. Gestion du paiement (Stripe)

### Configuration Stripe

Tu auras besoin de :
1. Un compte Stripe (https://dashboard.stripe.com)
2. Les 3 produits/prix créés dans Stripe Dashboard
3. Stripe Checkout pour la page de paiement
4. Stripe Webhooks pour les notifications

### Créer les produits dans Stripe

```
Produit 1 : ManageMyVision Starter
  → Prix : 29€/mois (récurrent)
  → Price ID : price_starter_monthly

Produit 2 : ManageMyVision Pro
  → Prix : 59€/mois (récurrent)
  → Price ID : price_pro_monthly

Produit 3 : ManageMyVision Enterprise
  → Prix : 99€/mois (récurrent)
  → Price ID : price_enterprise_monthly
```

### Flux de paiement Stripe

```
Site web                    Stripe                      API Serveur
   │                          │                             │
   │ 1. Clic "Souscrire Pro"  │                             │
   │ ────────────────────────▶ │                             │
   │  (Stripe Checkout)       │                             │
   │                          │                             │
   │ 2. Client entre sa CB    │                             │
   │ ────────────────────────▶ │                             │
   │                          │                             │
   │                          │ 3. Paiement réussi          │
   │                          │ ───────────────────────────▶ │
   │                          │ (Webhook: checkout.session.  │
   │                          │  completed)                  │
   │                          │                             │
   │                          │           4. API crée :      │
   │                          │           - Subscription     │
   │                          │           - License key      │
   │                          │           - Envoie email     │
   │                          │                             │
   │ 5. Redirect vers /success│                             │
   │ ◀──────────────────────── │                             │
```

### Webhooks Stripe à gérer

| Événement Stripe                      | Action API                                            |
|---------------------------------------|-------------------------------------------------------|
| `checkout.session.completed`          | Créer subscription + licence + email bienvenue        |
| `invoice.paid`                        | Renouveler la date d'expiration de la licence         |
| `invoice.payment_failed`              | Marquer la licence en « grace period »                |
| `customer.subscription.deleted`       | Désactiver la licence                                 |
| `customer.subscription.updated`       | Mettre à jour le plan (upgrade/downgrade)             |

---

## 12. Base de données serveur

### Schéma PostgreSQL (côté API serveur)

```sql
-- Table des organisations (boutiques d'optique)
CREATE TABLE organizations (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(200) NOT NULL,
    email           VARCHAR(255) NOT NULL UNIQUE,
    password_hash   VARCHAR(255) NOT NULL,
    phone           VARCHAR(20),
    address         TEXT,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

-- Table des abonnements
CREATE TABLE subscriptions (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id     UUID NOT NULL REFERENCES organizations(id),
    stripe_customer_id  VARCHAR(100),
    stripe_subscription_id VARCHAR(100),
    plan                VARCHAR(20) NOT NULL CHECK (plan IN ('starter', 'pro', 'enterprise')),
    status              VARCHAR(20) NOT NULL DEFAULT 'active'
                        CHECK (status IN ('active', 'past_due', 'canceled', 'trialing')),
    current_period_start TIMESTAMPTZ,
    current_period_end   TIMESTAMPTZ,
    cancel_at_period_end BOOLEAN DEFAULT FALSE,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    updated_at          TIMESTAMPTZ DEFAULT NOW()
);

-- Table des clés de licence
CREATE TABLE licenses (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id     UUID NOT NULL REFERENCES organizations(id),
    subscription_id     UUID NOT NULL REFERENCES subscriptions(id),
    license_key         VARCHAR(25) NOT NULL UNIQUE,  -- MMV-XXXX-XXXX-XXXX-XXXX
    plan                VARCHAR(20) NOT NULL,
    is_active           BOOLEAN DEFAULT TRUE,
    max_activations     INT DEFAULT 1,                -- Nombre de machines autorisées
    activated_count     INT DEFAULT 0,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    expires_at          TIMESTAMPTZ
);

-- Table des activations (machines)
CREATE TABLE license_activations (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    license_id      UUID NOT NULL REFERENCES licenses(id),
    hardware_id     VARCHAR(255) NOT NULL,            -- Identifiant unique de la machine
    machine_name    VARCHAR(200),
    activated_at    TIMESTAMPTZ DEFAULT NOW(),
    last_validated  TIMESTAMPTZ DEFAULT NOW(),
    is_active       BOOLEAN DEFAULT TRUE,
    UNIQUE(license_id, hardware_id)
);

-- Table des événements de facturation (audit)
CREATE TABLE billing_events (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id     UUID NOT NULL REFERENCES organizations(id),
    stripe_event_id     VARCHAR(100) NOT NULL UNIQUE,
    event_type          VARCHAR(100) NOT NULL,
    data                JSONB,
    created_at          TIMESTAMPTZ DEFAULT NOW()
);
```

### Relations

```
Organization (1) ──── (N) Subscriptions
Organization (1) ──── (N) Licenses
Subscription (1) ──── (1) License
License      (1) ──── (N) LicenseActivations
```

---

## 13. Sécurité

### Points de sécurité critiques

| Risque                               | Protection                                                   |
|--------------------------------------|--------------------------------------------------------------|
| Clé de licence partagée              | Limite d'activations par licence (1 machine par défaut)      |
| Modification du cache local          | Fichier chiffré DPAPI + vérification serveur au démarrage    |
| Interception réseau                  | HTTPS uniquement + certificate pinning                       |
| Décompilation de l'app               | Obfuscation IL (dotnet-obfuscator) + vérif côté serveur      |
| Rejeu de réponses API                | Timestamp + nonce dans les réponses + signature HMAC         |
| Contournement des limites            | Double vérification : locale (UX) + serveur (sync quotidien) |
| Accès non autorisé à l'API           | JWT avec expiration courte (15 min) + refresh token          |

### Hardware ID : identifier la machine

L'app génère un identifiant unique de la machine basé sur :
- ID de la carte mère
- Numéro de série du disque dur
- Nom de la machine

```csharp
public static string GenerateHardwareId()
{
    var data = new StringBuilder();
    
    // Carte mère
    using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
    foreach (var obj in searcher.Get())
        data.Append(obj["SerialNumber"]);

    // Disque dur
    using var diskSearcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
    foreach (var obj in diskSearcher.Get())
        data.Append(obj["SerialNumber"]);

    data.Append(Environment.MachineName);

    // Hash le tout
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString()));
    return Convert.ToBase64String(hash);
}
```

---

## 14. Scénarios détaillés

### Scénario A : Abonnement expiré

```
1. L'abonnement de l'opticien expire (carte refusée)
2. Stripe envoie webhook "invoice.payment_failed" → API marque la licence "past_due"
3. Au prochain démarrage de l'app :
   - API répond : { valid: false, reason: "payment_failed", gracePeriodDays: 7 }
   - L'app affiche un bandeau jaune : "⚠️ Votre paiement a échoué. 
     Vous avez 7 jours pour régulariser."
   - L'app reste UTILISABLE pendant 7 jours (grace period)
4. Après 7 jours sans paiement :
   - Stripe envoie webhook "customer.subscription.deleted"
   - API désactive la licence
   - L'app affiche l'écran "Licence expirée" (bloquant)
   - Les données ne sont PAS supprimées (elles restent dans la BD locale)
   - L'opticien peut consulter en lecture seule ? (à décider)
```

### Scénario B : Upgrade de plan (Starter → Pro)

```
1. L'opticien se connecte sur managemyvision.fr/dashboard
2. Il clique "Changer de plan" → choisit "Pro"
3. Stripe calcule le prorata et met à jour l'abonnement
4. Webhook "customer.subscription.updated" → API met à jour :
   - subscription.plan = "pro"
   - license.plan = "pro"
5. Au prochain démarrage (ou vérification périodique) de l'app :
   - L'API renvoie les nouvelles limites (200 clients, 3 users, etc.)
   - L'app débloque automatiquement les nouvelles fonctionnalités
   - Les boutons "Ventes", "Commandes", "Stock" apparaissent dans la sidebar
```

### Scénario C : Nouvelle machine

```
1. L'opticien achète un nouveau PC
2. Il installe ManageMyVision sur le nouveau PC
3. L'app demande la clé de licence
4. Il entre sa clé → l'app envoie la clé + le nouveau hardware ID
5. Le serveur vérifie :
   - La clé est valide
   - Nombre d'activations actuelles (1) < max_activations (1)
   - → REFUSÉ : "Cette licence est déjà activée sur une autre machine"
6. Options :
   a. L'opticien se connecte sur le site et désactive l'ancienne machine
   b. Ou il contacte le support
7. Après désactivation de l'ancienne → la nouvelle activation est acceptée
```

### Scénario D : Pas d'internet au démarrage

```
1. L'opticien lance l'app sans connexion internet
2. L'app essaie de contacter l'API → échec (timeout)
3. L'app vérifie le cache local :
   - Dernière validation : il y a 3 jours
   - Grace period : 7 jours
   → La licence est considérée valide (mode hors-ligne)
4. L'app démarre normalement avec un petit indicateur :
   "🔸 Mode hors-ligne — dernière vérification il y a 3 jours"
5. Si ça fait plus de 7 jours sans validation :
   → L'app affiche : "Connexion internet requise pour vérifier votre licence"
```

---

## 15. Planning d'implémentation

### Phase 1 : API serveur (Sprint 11)

> Durée estimée : 2-3 semaines

- [ ] Créer le projet ASP.NET Core Web API
- [ ] Base de données PostgreSQL (schema ci-dessus)
- [ ] Auth : inscription, login, JWT, refresh tokens
- [ ] Endpoints licence : validate, activate, info
- [ ] Intégration Stripe : produits, checkout, webhooks
- [ ] Génération de clés de licence
- [ ] Email transactionnel (bienvenue, licence, expiration)
- [ ] Tests unitaires + intégration

### Phase 2 : Site web (Sprint 12)

> Durée estimée : 2-3 semaines

- [ ] Projet Next.js + Tailwind CSS
- [ ] Landing page responsive
- [ ] Page pricing avec les 3 plans
- [ ] Inscription + connexion
- [ ] Intégration Stripe Checkout
- [ ] Portail client (dashboard)
- [ ] Page de téléchargement
- [ ] Emails transactionnels (templates)

### Phase 3 : Intégration app desktop (Sprint 13)

> Durée estimée : 1-2 semaines

- [ ] `ILicenseService` + `LicenseService`
- [ ] `LicenseStore` (stockage chiffré local)
- [ ] Écran d'activation de licence
- [ ] Écran de licence expirée
- [ ] Vérification au démarrage
- [ ] Vérification périodique (24h)
- [ ] Enforcement des limites dans les services métier
- [ ] Intégration avec `IPermissionService` existant
- [ ] Messages d'upgrade quand limite atteinte
- [ ] Mode hors-ligne (grace period 7 jours)
- [ ] Hardware ID Windows
- [ ] Tests unitaires

### Phase 4 : Distribution (Sprint 14)

> Durée estimée : 1 semaine

- [ ] Build automatisé (.exe + installateur MSI/MSIX)
- [ ] Code signing (certificat)
- [ ] Auto-updater intégré
- [ ] Pipeline CI/CD pour le déploiement
- [ ] CDN pour le téléchargement

---

## 16. FAQ technique

### Q : Pourquoi une clé de licence plutôt que les mêmes identifiants (email/password) ?

**R** : La clé de licence offre plusieurs avantages :
1. **Séparation des concerns** : Le compte sur le site (propriétaire de la boutique) est séparé des comptes utilisateurs dans l'app (staff : opticiens, techniciens). Le propriétaire peut avoir 3 employés qui utilisent l'app sans partager ses identifiants du site.
2. **Simplicité** : Une clé se copie-colle. Pas besoin de saisir email+password à chaque installation.
3. **Sécurité** : La clé n'est pas le mot de passe. Si la clé fuit, on peut la révoquer et en générer une nouvelle.
4. **Offline** : La validation de clé peut être cachée localement. Pas besoin de stocker un mot de passe.

### Q : L'opticien doit-il se connecter SUR LE SITE et DANS L'APP ?

**R** : Ce sont deux systèmes différents :
- **Sur le site** : Il se connecte avec son **email + mot de passe** pour gérer son abonnement, sa facturation, télécharger l'app.
- **Dans l'app** : Il se connecte avec son **username + mot de passe** créé localement dans l'app (le système actuel avec rôles Admin/Opticien/Technicien).

La **clé de licence** fait le lien entre les deux : elle prouve que l'installation de l'app est liée à un abonnement payé.

### Q : Les données des clients sont-elles sur le cloud ?

**R** : **Non.** Les données métier (clients, ordonnances, ventes…) restent dans la base de données SQLite **locale** de l'opticien. Seules les données d'abonnement/licence sont sur le serveur cloud. C'est un argument de vente : "Vos données restent sur votre machine."

Exception : le plan Enterprise pourrait offrir une **sauvegarde cloud optionnelle** (backup chiffré).

### Q : Que se passe-t-il si l'opticien arrête de payer ?

**R** : 
1. Grace period de 7 jours (l'app fonctionne encore)
2. Après 7 jours : l'app se bloque en mode "Licence expirée"
3. Les données locales ne sont **jamais supprimées**
4. Si l'opticien repaie, il retrouve tout tel quel
5. Optionnel : mode "lecture seule" pour qu'il puisse consulter ses données

### Q : Comment empêcher le piratage ?

**R** : Aucune protection n'est 100% infaillible pour une app desktop, mais on rend le piratage suffisamment coûteux :
1. **Vérification serveur obligatoire** : pas de crack local possible
2. **Hardware ID** : la licence est liée à une machine physique
3. **Vérification périodique** : même si le cache est modifié, il expire après 7 jours
4. **Obfuscation** : rend le reverse engineering difficile
5. **Grace period limitée** : pas d'usage permanent hors-ligne
6. **Prix abordable** : à 29€/mois, la plupart des professionnels préfèrent payer plutôt que pirater

### Q : Comment gérer les mises à jour de l'app ?

**R** : 
- L'app vérifie les mises à jour au démarrage (endpoint API `/updates/check`)
- Si une mise à jour est disponible, notification à l'utilisateur
- Téléchargement + installation automatique ou manuelle
- Les utilisateurs Enterprise ont accès aux mises à jour en avant-première
- Bibliothèque recommandée : **Squirrel.Windows** ou **Velopack** pour l'auto-update
