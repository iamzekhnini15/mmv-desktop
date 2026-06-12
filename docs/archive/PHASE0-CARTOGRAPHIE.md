> **Document historique** — remplacé par les référentiels consolidés de `docs/domain` et `docs/architecture`.
> Ne pas utiliser comme source de vérité actuelle.
>
> *(Archivé en P2A-0B. La cartographie/audit de référence vivent dans
> [`docs/architecture/`](../architecture/) — voir `dependency-map.md` et `phase-1-technical-audit.md`.)*

---

# PHASE 0 — CARTOGRAPHIE COMPLÈTE DU PROJET MMV

> Document de compréhension uniquement — **aucune modification de code, aucune proposition d'implémentation.**
> Objectif : comprendre parfaitement MMV avant de lancer l'audit complet de la Phase 1.
>
> **Date :** 2026-06-09
> **Branche analysée :** `refactor/redesignLightAndDarkMode`

---

## 1. Vue d'ensemble du projet

**ManageMyVision (MMV)** est une application **desktop de gestion pour opticiens** (magasin d'optique unique), développée en **.NET 8 / C# 12** avec **Avalonia UI 11.2** (MVVM via CommunityToolkit) et **SQLite + EF Core 8**. Elle est **mono-poste, mono-magasin, sans réseau**, avec une base SQLite locale.

Le périmètre métier couvert : authentification du personnel, gestion clients, ordonnances optiques, catalogue produits (montures/verres/lentilles/accessoires), fournisseurs, ventes (POS), commandes fournisseurs de verres (workflow atelier), mouvements de stock, et notifications.

Le projet est organisé en **sprints** (jusqu'au Sprint 8 « Order Management ») et dispose d'une documentation interne riche (`ARCHITECTURE.md`, `SPRINTS.md`, `TESTS.md`) — signe d'une intention pédagogique/structurée, mais cette doc est partiellement désynchronisée du code réel (voir §10).

---

## 2. Cartographie de la solution

### 2.1 Projets (3 src + 2 tests)

| Projet | Rôle réel | Dépend de |
|--------|-----------|-----------|
| **MMV.Domain** | Cœur métier : entités, enums, interfaces repos, services métier, value objects, validators FluentValidation, exceptions | *(aucune référence projet)* — dépend juste de FluentValidation |
| **MMV.Infrastructure** | Accès données : `OpticDbContext`, configurations Fluent API, repositories, UnitOfWork, migrations, seeders, `AuthenticationService` (BCrypt) | MMV.Domain |
| **MMV.App** | Présentation : Avalonia, Views (.axaml), ViewModels, services UI (Navigation, Session, Permission, Dialog, Theme), converters, DI | MMV.Domain + MMV.Infrastructure |
| MMV.Domain.Tests | Tests unitaires (services, validators, value objects, repos) | Domain (+ Infra) |
| MMV.App.Tests | Tests ViewModels, navigation, session, permissions | App |

### 2.2 Diagramme logique

```
        ┌──────────────────────────────┐
        │   MMV.App (Avalonia + MVVM)   │
        │  Views / ViewModels / Services UI │
        └───────────┬──────────────┬────┘
                    │              │
                    ▼              ▼
   ┌─────────────────────┐   ┌──────────────────┐
   │ MMV.Infrastructure  │──▶│   MMV.Domain     │
   │ EF Core / Repos / UoW│   │ Entities/Services│
   └─────────────────────┘   │ (aucune dépendance)│
                             └──────────────────┘
```

La **règle de dépendance Clean Architecture est respectée au niveau des références projet** (Domain ne référence rien, Infra→Domain, App→les deux). Mais l'usage réel s'en écarte (voir §3).

### 2.3 Packages NuGet

| Package | Projet | Rôle | Remarque / risque |
|---------|--------|------|-------------------|
| FluentValidation 11.9 | Domain | Validation entités | Validators présents mais **non câblés dans les flux UI** |
| Microsoft.EntityFrameworkCore.Sqlite 8.0 | Infra | ORM + provider | OK, version LTS |
| EFCore.Design 8.0 | Infra + App | Migrations | Référencé aussi dans App (inutile en prod) |
| BCrypt.Net-Next 4.0.3 | Infra | Hash mots de passe (work factor 11) | Bon choix |
| Configuration.Abstractions 8.0 | Infra | Connection string | Pas d'`appsettings.json` réel (voir §2.4) |
| Avalonia 11.2.8 (+ Desktop, DataGrid, Fluent, Inter, Diagnostics) | App | UI | OK |
| FluentAvaloniaUI 2.1.0 | App | Contrôles Fluent | OK |
| CommunityToolkit.Mvvm 8.2.2 | App | MVVM | **Sous-utilisé** : la plupart des VM utilisent un `RelayCommand` maison et `BaseViewModel` custom plutôt que les source generators |
| Microsoft.Extensions.DependencyInjection / Hosting 8.0 | App | IoC | Hosting importé mais non exploité |
| Projektanker.Icons.Avalonia 9.6.2 | App | Icônes | OK |

Aucun package obsolète/vulnérable majeur. Les risques sont surtout d'**incohérence d'usage** (packages présents non utilisés), pas de sécurité.

### 2.4 Configuration / secrets / build

- **Aucun `appsettings.json`** dans le dépôt. La chaîne de connexion est gérée à 3 endroits **divergents** :
  - `OpticDbContext.OnConfiguring` → `%LOCALAPPDATA%\ManageMyVision\mmv.db`
  - `DependencyInjection.AddInfrastructure` (fallback) → même chemin LOCALAPPDATA
  - **`App.axaml.cs` → `Data Source=mmv-optic.db`** (chemin **relatif** au répertoire de travail) ← **c'est celui réellement utilisé au runtime**
- **Pas de gestion de secrets** : hash admin BCrypt en dur dans `DbInitializer` (mot de passe « admin »). Acceptable pour démo, bloquant pour prod.
- **Pas de variables d'environnement**.
- Build : `net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, `WinExe`, bindings compilés Avalonia. `regenerate-database.ps1` à la racine pour reconstruire la base.

---

## 3. Cartographie technique (architecture réellement implémentée)

**Verdict : architecture hybride à intention Clean Architecture + MVVM, mais partiellement court-circuitée.**

Ce qui est **conforme** :
- 3 couches avec règle de dépendance respectée au niveau références.
- MVVM réel côté App (Views ↔ ViewModels, `BaseViewModel`, navigation par `ViewLocator`).
- Pattern **Repository + Unit of Work** complet, services métier avec interfaces, value objects, validators, exceptions métier.

Ce qui **diverge** (incohérences architecturales majeures) :

1. **La couche Services métier (Domain) est contournée par l'UI.** Aucun ViewModel n'injecte `ICustomerService`/`ISaleService`/`IOrderService`/etc. (grep : 0 résultat dans MMV.App). Les ViewModels injectent **directement les repositories** et orchestrent eux-mêmes la logique métier.
   → Exemple emblématique : `SaleFormViewModel.ExecuteSave()` (≈170 lignes) crée la `Sale`, génère le `SaleNumber`, crée l'`Order` fournisseur, décrémente le stock et écrit les `StockMovement` — **toute la logique métier est dans le ViewModel**, pas dans `SaleService`. `SaleService.CreateSaleAsync` existe mais n'est jamais appelé par l'app.

2. **Double configuration DI divergente.** `Infrastructure.DependencyInjection.AddInfrastructure(...)` (propre, avec services métier) **n'est jamais appelé** (grep : 0). `App.ConfigureServices()` réimplémente sa propre DI, **n'enregistre aucun service métier**, et pointe sur une autre base (`mmv-optic.db`).

3. **`IUnitOfWork` n'expose pas tous les repos** (`Users`, `Notifications` absents de l'interface), alors que `App` les résout séparément via DI. Incohérence du contrat UoW.

4. **Persistance « anémique »** : les entités sont de purs sacs de propriétés (aucune invariant, aucun comportement). Les Value Objects (`Money`, `Email`, `Address`, `PhoneNumber`) existent et sont testés mais **ne sont utilisés nulle part** dans les entités (qui stockent des `string`/`decimal` bruts) — code mort métier.

5. **Validation non câblée** : les `Validators` FluentValidation existent et sont testés, mais la validation des formulaires est refaite à la main dans les ViewModels.

**Flux d'exécution réel :**
```
View (.axaml) → ViewModel (RelayCommand) → Repository → UnitOfWork.SaveChangesAsync → OpticDbContext → SQLite
                         └─(logique métier inline)
```
au lieu du flux Clean attendu `ViewModel → Service métier → UoW/Repository`.

---

## 4. Cartographie métier

### 4.1 Entités (19)

| Entité | Rôle | Propriétés clés | Relations |
|--------|------|-----------------|-----------|
| **User** | Personnel | Username, PasswordHash, Role (Admin/Optician/Technician), IsActive | 1–N Sales, 1–N StockMovements |
| **Customer** | Client | Identité, contact, `SocialSecurityNumber`, `InsuranceName` (string), Notes | 1–N Prescriptions, 1–N Sales |
| **Prescription** | Ordonnance optique | OD/OG : Sphère, Cylindre, Axe, Addition, Prisme, Acuité ; DoctorName, IssueDate | N–1 Customer |
| **Product** | Catalogue (table mère) | Reference, Name, Category (enum), PurchasePrice/SalePrice (decimal), Stock, Threshold, IsActive | N–1 Supplier, N–1 ProductCategory, 1–1 Glass/Lens/AccessoryDetail |
| **GlassDetail** | Détail verre | Material, GlassType, Index, PowerLimitMin/Max | 1–1 Product, N suppléments, N tiers |
| **LensDetail** | Détail lentille | Brand, Material, LensType, Diameter, BaseCurve, Duration | 1–1 Product |
| **AccessoryDetail** | Détail monture/accessoire | Color, Size, Material | 1–1 Product |
| **Supplement** | Option verre (anti-reflet…) | Name, SupplementPrice | N–N GlassDetail via GlassSupplement |
| **GlassSupplement** | Table d'association | GlassId, SupplementId | jointure |
| **GlassPricingTier** | Grille tarifaire par puissance | PowerMin/Max, prix achat/vente | N–1 GlassDetail |
| **ProductCategory** | Catégorie (ancienne structure) | Name, Description | 1–N Product — **redondant avec l'enum Category** |
| **Supplier** | Fournisseur | Name, Contact, ReferenceCode | 1–N Products |
| **Sale** | Vente (transaction client) | SaleNumber, montants, Deposit/Remaining, PaymentMethod/Status, **SaleStatus** | N–1 Customer, N–1 Staff, 1–N SaleItem, 1–N Order |
| **SaleItem** | Ligne de vente | ProductId, ItemType, Quantity, prix, **+ paramètres de correction OD/OG dupliqués** | N–1 Sale, N–1 Product |
| **Order** | Commande **fournisseur de verres** (≠ commande client) | OrderNumber, **SaleId obligatoire**, SupplierId?, statuts atelier | N–1 Sale, 1–N OrderItem |
| **OrderItem** | Ligne de commande fournisseur | ItemType, Quantity, prix, **+ paramètres de fabrication OD/OG** | N–1 Order, N–1 Product |
| **StockMovement** | Mouvement stock | Type (In/Out/Adjustment), Quantity, Reason, PerformedByUserId | N–1 Product, N–1 User |
| **Notification** | Notification système | Type (string), Title, Message, EntityId/Type, IsRead | *(aucune FK)* |

### 4.2 Agrégats potentiels (frontières observées)

- **Sale** (racine) → SaleItems + Orders → OrderItems. Invariant central : *une vente contenant des verres crée automatiquement une commande fournisseur (Order) liée*. C'est l'agrégat le plus consistant.
- **Product** (racine) → GlassDetail/LensDetail/AccessoryDetail (héritage par composition / table-per-type) + PricingTiers + GlassSupplements.
- **Customer** (racine) → Prescriptions.

### 4.3 Value Objects (présents mais inactifs)

`Money` (montant + devise, arrondi), `Email`, `PhoneNumber`, `Address` — immuables, bien testés, **non intégrés aux entités**. Candidats naturels à promotion si refactoring DDD.

### 4.4 Services / logique métier

| Localisation | Logique | Statut |
|---|---|---|
| `OrderService` | Validation des **transitions de statut** atelier (New→ToFabricate→InProgress→QualityCheck→Ready→Delivered) | défini, **non utilisé par l'UI** |
| `SaleService` | Calcul total/remise/final, garde-fous remise | défini, **non utilisé** |
| `ProductService` | Calcul marge %, low stock | défini, **non utilisé** |
| `PrescriptionService` | Interdiction date future | défini, **non utilisé** |
| `AuthenticationService` (Infra) | BCrypt verify/hash, last login, change password | **réellement utilisé** (login) |
| `SaleFormViewModel` (App) | **Le vrai moteur métier vente** : suggestion verres selon ordonnance, split OD/OG, création Order auto, décrément stock | utilisé |
| `PermissionService` (App) | Matrice de rôles (CanAccess/Create/Edit/Delete/Refund) | utilisé |

---

## 5. Cartographie base de données

### 5.1 Modèle logique (18 tables)

```
Users ──< Sales >── Customers ──< Prescriptions
  │         │  │
  │         │  └──< Orders ──< OrderItems >── Products
  │         └──< SaleItems >── Products
  └──< StockMovements >── Products

Suppliers ──< Products >── ProductCategories
Products ─1:1─ GlassDetail / LensDetail / AccessoryDetail
GlassDetail ──< GlassPricingTiers
GlassDetail >──< Supplements  (via GlassSupplement)
Notifications  (table isolée, pas de FK)
```

Cardinalités principales : Customer 1–N Prescription ; Customer 1–N Sale ; Sale 1–N SaleItem ; **Sale 1–N Order** (Order.SaleId obligatoire) ; Order 1–N OrderItem ; Product 1–N Sale/OrderItem/StockMovement ; Supplier 1–N Product.

Migrations EF : 7 migrations (`InitialCreate` → `AddProductEntryDate` → `ProductSchemaRefactoring` → `AddNotifications` → `AddCounterSaleFields` → `AddDeposit/RemainingAmount` → `RestoreSaleOrderSeparation`). L'historique montre une **séparation Sales/Orders refaite plusieurs fois** (sujet instable).

### 5.2 Analyse de cohérence / redondances / risques

- **Double catégorisation produit** : `Product.Category` (enum) **et** `Product.CategoryId` + table `ProductCategory`. La table est qualifiée « conservée pour compatibilité » → redondance, source d'incohérence.
- **Duplication des données optiques** sur 3 niveaux : `Prescription` (OD/OG), `SaleItem` (Sphere/Cylinder/…), `OrderItem` (idem). Les paramètres sont recopiés à chaque étape → pas de source unique de vérité.
- **`Notification` orphelin** : `EntityId`/`EntityType` en string sans FK → intégrité référentielle non garantie.
- **`SaleItem.TotalPrice` stocké** alors que `Sale.FinalAmount` est recalculé : risque de désynchronisation (valeurs persistées vs recalculées dans le VM).
- **Argent en `decimal`** (bon), **optique en `double`** (cohérent avec l'intention documentée).
- **Aucune colonne d'audit homogène** (CreatedAt présent sur certaines entités, `UpdatedAt` que sur Customer ; `Notification.CreatedAt` en `DateTime.Now` alors que le reste en `UtcNow` → incohérence fuseaux).

### 5.3 Projection SaaS / multi-tenant

- **Compatible SaaS** : usage d'EF Core (provider remplaçable PostgreSQL/SQL Server), repos + UoW, services interfacés, auth BCrypt.
- **Bloquant SaaS** : SQLite fichier local ; chaîne de connexion en dur ; DI dupliquée ; pas de couche API ; logique métier dans les ViewModels desktop (non réutilisable côté serveur).
- **Bloquant multi-tenant** : **aucune notion de tenant/magasin/organisation** (grep `TenantId/StoreId/...` = 0). Aucune entité `Store`/`Company`. Toutes les requêtes sont globales, sans filtrage par tenant. C'est le chantier le plus lourd pour un futur SaaS.

---

## 6. Cartographie UI (parcours utilisateur)

Démarrage : `App` → fenêtre **Login** → sur succès → `MainWindow` (shell avec menu latéral filtré par permissions) → navigation par `NavigationService` (registre nom→type de VM, instanciation via `ActivatorUtilities`).

| Écran | Rôle | Données | Actions | Accès |
|---|---|---|---|---|
| Login | Authentification | username/password | Connexion (BCrypt) | tous |
| Dashboard | Vue d'ensemble | KPIs | — | tous |
| Customers (+ Detail/Form/Info/Prescriptions/PurchaseHistory) | CRM | fiche client, historique, ordonnances | CRUD client, voir achats, créer ordonnance, **lancer vente** | CRUD sauf Technicien (lecture) |
| Prescriptions (+ Detail/Form) | Ordonnances | OD/OG | CRUD ordonnance | CRUD sauf Technicien |
| Products (+ Detail/Form/OrderHistory/StockMovement) | Catalogue | produits, détails, stock | CRUD produit, mouvements stock | CRUD sauf Technicien (lecture+alerte) |
| Suppliers (+ Detail/Form) | Fournisseurs | fiches | CRUD | — |
| Sales / SaleForm | POS | panier, ordonnance active, suggestion verres | créer vente (comptoir ou fabrication), acompte | pas Technicien |
| Orders (+ Detail/Form/Kanban/FabricationSheet) | Atelier | commandes verres | changer statut (Kanban), fiche fabrication | tous (statut) |
| Inventory | Stock | niveaux, mouvements | ajustements | tous |
| Notifications | Alertes | stock bas/rupture | marquer lu | tous |
| Reports | Rapports | CA, stats | export | pas Technicien |
| Settings | Paramètres | thème (clair/sombre récent) | config | pas Technicien |
| Users (+ Form/Profile) | Gestion personnel | comptes | CRUD users | **Admin uniquement** |

**Carte de flux générique :**
`Utilisateur → Écran (View) → Action (RelayCommand) → ViewModel → Repository → UnitOfWork → OpticDbContext → SQLite`
*(la couche Service métier n'est pas traversée).*

> Note structurelle UI : présence de **doublons de vues** (`Views/UsersView.axaml` ET `Views/Users/UsersView.axaml` ; `Views/UserProfileView.axaml` ET `Views/Users/UserProfileView.axaml`) et de **code mort** (`Views/ProductDetailView.old.axaml.cs`).

---

## 7. Cartographie des flux majeurs

1. **Authentification** : LoginVM → `AuthenticationService.AuthenticateAsync` (BCrypt) → `SessionService.Login` (timer inactivité 30 min) → événement `LoginSuccessful` → `App` ouvre `MainWindow`. *(seul flux qui passe par un vrai service)*

2. **Création client** : CustomerFormVM → `ICustomerRepository.CreateAsync` → UoW.Save. (validation manuelle inline ; `CustomerService`/`CustomerValidator` non utilisés).

3. **Vente** (flux central) : SaleFormVM `InitializeForCustomerAsync` charge ordonnance + produits, filtre verres compatibles (type SF/MF + limites de puissance) → ajout panier (verre = split auto OD+OG depuis l'ordonnance) → `ExecuteSave` : crée `Sale`, persiste, **si verres → crée `Order` fournisseur auto**, si vente comptoir → décrémente stock + `StockMovement`. Tout dans le VM.

4. **Commande / atelier** : OrderKanbanVM affiche par statut ; transitions de statut (la validation des transitions de `OrderService` existe mais le VM peut écrire directement).

5. **Stock** : StockMovementFormVM → `IStockMovementRepository` + update `Product.StockQuantity` (déclenché par ventes comptoir et ajustements manuels).

6. **Notifications** : générées paresseusement par `MainWindowViewModel.LoadUnreadNotificationsCountAsync` (scan stock bas à chaque ouverture) + au seeding. Logique de génération **dans le VM du shell**.

7. **Gestion utilisateurs** : UsersVM/UserFormVM → `IUserRepository` + `AuthenticationService.HashPassword`. Admin only.

---

## 8. Niveau de maturité

| Axe | Niveau | Commentaire |
|---|---|---|
| Couverture fonctionnelle métier optique | **Bon** (cœur) | vente, ordonnance, atelier verres, stock fonctionnels |
| Rigueur architecturale (réelle) | **Moyen** | structure Clean en façade, contournée en pratique |
| Tests | **Moyen+** | bons tests Domain (services/validators/VO) et quelques tests App, mais testent du code partiellement non utilisé en prod |
| Qualité données | **Moyen** | redondances, duplications, pas de tenant |
| Prod-readiness | **Faible** | secrets en dur, DI dupliquée, chemin DB incohérent |
| SaaS-readiness | **Faible** | mono-tenant, mono-fichier, logique non réutilisable |

**Stade global : prototype/MVP avancé et fonctionnel**, structuré pour évoluer, mais avec une dette architecturale d'incohérence (intention ≠ implémentation).

---

## 9. Forces du projet

1. Découpage en 3 projets avec règle de dépendance respectée (bonne fondation).
2. Domaine optique réellement modélisé (ordonnance OD/OG, prismes, types de verres, grille tarifaire par puissance, suppléments).
3. Logique métier intéressante : suggestion automatique de verres selon ordonnance, split OD/OG, création Order fournisseur automatique.
4. Sécurité auth correcte (BCrypt, work factor 11, timeout d'inactivité, matrice de permissions par rôle).
5. Pattern Repository + UoW + services interfacés + Value Objects + validators **présents** (matière première pour un vrai refactoring DDD).
6. Bonne documentation interne et tests existants ; commentaires XML en français systématiques.
7. Seeding riche et reproductible (seed fixe) facilitant démos et tests.

---

## 10. Faiblesses du projet

1. **Couche Services métier court-circuitée** : logique métier dans les ViewModels → non réutilisable, non testée au bon endroit, duplication.
2. **Double DI divergente** ; `AddInfrastructure` jamais appelé ; **3 chemins de base de données différents** (le runtime utilise `mmv-optic.db` relatif).
3. **Value Objects et Validators inutilisés** (code mort métier) ; entités anémiques sans invariants.
4. **Redondances de schéma** : `Category` enum vs `ProductCategory` table ; paramètres optiques dupliqués Prescription/SaleItem/OrderItem.
5. **`IUnitOfWork` incomplet** (Users/Notifications hors contrat) ; `RollbackAsync` qui *dispose* le contexte (comportement douteux).
6. **Doublons de vues et fichiers `.old`** ; ambiguïté Order = « commande client » vs « commande fournisseur de verres » (nommage piégeux).
7. **Incohérences temporelles** (`DateTime.Now` vs `UtcNow`), **`SaleNumber`/`OrderNumber` générés via `new Random()`** dans le VM → **risque de collision** (pas d'unicité garantie).
8. **Aucune préparation multi-tenant / multi-magasin** ; secrets et mot de passe admin en dur.
9. Notifications générées dans le VM du shell (responsabilité mal placée).

---

## 11. Questions ouvertes (à clarifier avant Phase 1)

1. **Cible produit** : MMV reste-t-il un logiciel desktop mono-poste, ou l'objectif est-il une bascule **SaaS multi-tenant / multi-magasin** ? (détermine l'ampleur du refactoring DB + couche API).
2. **Intention sur les Services métier** : doit-on **réhabiliter** la couche Service (et y déplacer la logique des ViewModels), ou assumer une architecture « VM→Repository » et supprimer les services inutilisés ?
3. **Sale vs Order** : la séparation a été refaite plusieurs fois (migrations). Le modèle « Order = commande fournisseur de verres liée à une Sale » est-il définitif ?
4. **Double catégorisation produit** (enum vs table) : laquelle garder ?
5. **Données optiques dupliquées** : faut-il une source unique (Prescription) référencée, plutôt que recopiée dans SaleItem/OrderItem ?
6. **Numérotation** des ventes/commandes : faut-il un générateur séquentiel transactionnel (remplacer `Random`) ?
7. **Périmètre fonctionnel manquant** (cf. annexe) : lesquels sont au backlog ?

---

## Annexe — Domaine optique (Étape 0.6) : état des fonctionnalités

| Domaine | État | Détail |
|---|---|---|
| Clients | **Présent** | CRUD complet + historique |
| Patients | **Présent** (= Clients) | pas de distinction client/patient |
| Prescriptions | **Présent** | OD/OG complet, historique, médecin |
| Ordonnances | **Présent** (= Prescription) | synonyme dans le modèle |
| Montures | **Présent** | Product + AccessoryDetail |
| Verres | **Présent** | GlassDetail + grille tarifaire + suppléments + suggestion |
| Traitements/Suppléments | **Partiel** | Supplements/GlassSupplement modélisés mais **non appliqués au calcul de prix** de vente |
| Fournisseurs | **Présent** | CRUD |
| Commandes | **Partiel** | commandes **fournisseur de verres** OK (workflow Kanban) ; pas de commande d'approvisionnement générale ; seed Orders vide (TODO) |
| Stock | **Présent** | mouvements In/Out/Adjustment, seuils, alertes |
| Vente | **Présent** | POS comptoir + fabrication, acompte/reste |
| Facturation | **Absent** | pas d'entité Invoice ni génération de facture/PDF |
| Mutuelles | **Partiel** | `Customer.InsuranceName` (string) seulement ; pas de tiers-payant, ni part mutuelle/SS calculée |
| SAV | **Absent** | — |
| Réparations | **Absent** | — |
| Rendez-vous | **Absent** | — |
| Gestion utilisateurs | **Présent** | CRUD + rôles + permissions (Admin only) |
| Multi-magasins | **Absent** | aucune entité magasin |
| Multi-tenant SaaS | **Absent** | aucune notion de tenant |

---

*Fin de la Phase 0 — Cartographie. Prochaine étape : Phase 1 (audit complet), à lancer après clarification des questions ouvertes ci-dessus (notamment cible SaaS et sort de la couche Services).*
