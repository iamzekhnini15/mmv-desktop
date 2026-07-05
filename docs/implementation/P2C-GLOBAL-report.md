# Rapport P2C-GLOBAL — VERDICT : **GO (local)**

> Reprise après **P2C-SEC-1** (pin SQLite / CVE). Nettoyage final de la persistance directe
> restante dans `src/MMV.App` : extraction de **toutes** les écritures UI vers des use cases
> de la couche Application, réduction maximale des garde-fous, préparation de P2D.
> Aucun commit, aucun push (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-GLOBAL |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche : `p2c-ui-cleanup` ✓
- Working tree : propre ✓
- Dernier commit : `aa488fa fix(P2C-SEC-1): pin sqlite native bundle to remediate CVE` ✓ (présent)

## 3. Baseline après P2C-SEC-1 (avant modification)

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | vert (0 erreur) |
| `dotnet test MMV.sln` | **463** tests verts (App 161 · Application 79 · Domain 223) |
| `dotnet list … --vulnerable --include-transitive` | 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | `false` |
| `MMV.Application` references | uniquement `MMV.Domain` |
| `MMV.Application` packages | uniquement `Microsoft.Extensions.DependencyInjection.Abstractions` |

> **Réserve CI :** l'outil `gh` n'était pas authentifié dans l'environnement d'exécution ; la
> validation distante **CI #56** n'a pas pu être vérifiée directement. La baseline **locale**
> est intégralement verte, ce qui a autorisé la reprise. À confirmer côté CI avant tout merge.

## 4. Inventaire global des violations UI restantes (avant modification)

| ViewModel / code-behind | Dépendance | Type | Usage exact | Action P2C-GLOBAL |
|---|---|---|---|---|
| `SupplierFormViewModel` | `ISupplierRepository`, `IUnitOfWork` | écriture | `CreateAsync`/`UpdateAsync` + `SaveChangesAsync` | → `ICreate/IUpdateSupplierUseCase` |
| `SuppliersViewModel` | `ISupplierRepository`, `IUnitOfWork` | écriture + lecture + composition | `DeleteAsync`+`SaveChangesAsync` ; `GetWithProductsAsync` (lecture) | delete → `IDeleteSupplierUseCase` ; repo conservé (lecture) ; UoW retiré |
| `SuppliersListViewModel` | `ISupplierRepository`, `IUnitOfWork` | lecture + **dépendance morte** | `GetAllAsync` ; UoW jamais utilisé | UoW **supprimé** (mort) ; repo conservé (lecture) |
| `UserFormViewModel` | `IUserRepository`, `IUnitOfWork`, `IAuthenticationService` | écriture | unicité + hachage + `Create/UpdateAsync` + `SaveChangesAsync` | → `ICreate/IUpdateUserUseCase` |
| `UsersListViewModel` | `IUserRepository`, `IUnitOfWork` | écriture + lecture | toggle actif : `UpdateAsync`+`SaveChangesAsync` ; `GetAllAsync` (lecture) | toggle → `ISetUserActiveUseCase` ; repo conservé (lecture) ; UoW retiré |
| `UsersViewModel` | `IUserRepository`, `IUnitOfWork`, `IAuthenticationService` | composition | construit list/form | injecte use cases ; UoW + auth retirés |
| `ProductFormViewModel` | `IProductRepository`, `ISupplierRepository`, `IUnitOfWork`, `INotificationRepository` | écriture + lecture | `Create/UpdateAsync` produit + détails ; `GetAllAsync` fournisseurs (lecture) | → `ICreate/IUpdateProductUseCase` ; supplier repo conservé (lecture) |
| `ProductsListViewModel` | `IProductRepository`, `IUnitOfWork` | écriture + lecture | `DeleteAsync`+`SaveChangesAsync` ; `GetAllAsync` (lecture) | delete → `IDeleteProductUseCase` ; repo conservé ; UoW retiré |
| `ProductsViewModel` | `IProductRepository`, `ISupplierRepository`, `IUnitOfWork` | composition | `UnitOfWork.StockMovements` pour construire l'enfant | UoW → injection directe `IStockMovementRepository` ; injecte les use cases produit/fournisseur |
| `InventoryViewModel` | `IProductRepository`, `IStockMovementRepository`, `IUnitOfWork` | écriture + lecture | ajustement : `CreateAsync`+`UpdateAsync`+`SaveChangesAsync` ; `GetAllAsync` (lecture) | ajustement → `ICreateStockMovementUseCase` (réutilisé) ; product repo conservé (lecture) |
| `NotificationsListViewModel` | `INotificationRepository`, `IProductRepository`, `IUnitOfWork` | écriture + lecture | `MarkAllAsRead`/génération stock bas + `SaveChangesAsync` ; `GetAllAsync`/`CountUnread` (lecture) | → `IMarkAllNotificationsRead`/`IGenerateLowStockNotifications` ; notif repo conservé (lecture) |
| `NotificationsViewModel` | `INotificationRepository`, `IProductRepository`, `IUnitOfWork` | composition | construit l'enfant | injecte use cases ; product repo + UoW retirés |
| `MainWindowViewModel` | `INotificationRepository`, `IProductRepository`, `IUnitOfWork` | écriture + lecture | génération stock bas + `CountUnread` | → `IGenerateLowStockNotificationsUseCase` |
| `Views/MainWindow.axaml.cs` | 3 repos (code-behind) | composition | transmet au VM | remplacés par `IGenerateLowStockNotificationsUseCase` |
| `OrderKanbanViewModel` | `IOrderRepository`, `IUnitOfWork` | écriture + lecture | avancement : `UpdateAsync`+`SaveChangesAsync` ; `GetAllWithItemsAsync` (lecture) | avancement → `IAdvanceOrderStatusUseCase` (réutilisé) ; order repo conservé (lecture) |
| `OrdersViewModel` | `IUnitOfWork` | **dépendance morte** (après Kanban) | plus aucun usage | UoW **supprimé** (mort) |
| `CustomersViewModel` | `IUnitOfWork` | pass-through | transmis à `CustomerDetailViewModel` | UoW **supprimé** (mort en aval) |
| `CustomerDetailViewModel` | `IUnitOfWork` | **dépendance morte** | seulement null-checké, jamais stocké | UoW **supprimé** (mort) |

## 5. Stratégie appliquée

Quatre sous-phases internes, **module par module**, chaque module suivi d'une compilation :

- **A — Dépendances mortes.** `SuppliersListViewModel`, `OrdersViewModel`, `CustomersViewModel`,
  `CustomerDetailViewModel` : suppression de paramètres `IUnitOfWork` injectés mais inutilisés.
- **B — Écritures → Application.** Création de use cases *command* pour Fournisseurs, Utilisateurs,
  Produits, Notifications ; réutilisation de use cases existants pour Inventaire
  (`CreateStockMovement`, branche *Adjustment*) et Kanban (`AdvanceOrderStatus`).
- **C — Lectures.** Les lectures d'affichage restantes (chargement de listes / détail) sont
  **conservées et justifiées** en tant que dette explicite reportée vers **P2D** (query use cases +
  DTO applicatifs) — voir `docs/architecture/P2D-read-application-roadmap.md`.
- **D — Garde-fous.** Synchronisation des allowlists : `IUnitOfWork` → **0**, code-behind → `App.axaml.cs`
  uniquement, propriétés publiques de persistance → **0** (inchangé), repositories → uniquement des lectures.

Principe directeur : **déplacement iso-fonctionnel** (aucune règle métier nouvelle, aucun `Money`,
aucune règle Belgique/Maroc/SaaS). Chaque écriture mono-`SaveChanges` reste atomique sans
`ITransactionRunner` (cohérent avec les use cases P2B/P2C existants).

## 6. Use cases *command* créés

| Module | Use cases | Dépendances |
|---|---|---|
| Fournisseurs | `CreateSupplier`, `UpdateSupplier`, `DeleteSupplier` | `ISupplierRepository`, `IUnitOfWork` |
| Utilisateurs | `CreateUser`, `UpdateUser`, `SetUserActive` | `IUserRepository`, `IUnitOfWork`, `IAuthenticationService` (hachage) |
| Produits | `CreateProduct`, `UpdateProduct`, `DeleteProduct` | `IProductRepository`, `IUnitOfWork` |
| Notifications | `MarkAllNotificationsRead`, `GenerateLowStockNotifications` | `INotificationRepository` (+ `IProductRepository` pour la génération), `IUnitOfWork` |

**11 use cases** (42 fichiers source : commande/résultat/interface/implémentation), enregistrés en
`Scoped` dans `MMV.Application/DependencyInjection.cs`.

## 7. Query use cases créés

**Aucun** en P2C-GLOBAL. Les lectures d'affichage restantes sont volontairement conservées et
justifiées ; leur extraction vers des *query use cases* (retournant des **DTO applicatifs**, jamais
des entités EF suivies) constitue le cœur de **P2D** (roadmap dédiée créée).

## 8. ViewModels modifiés

`SupplierFormViewModel`, `SuppliersViewModel`, `SuppliersListViewModel`, `UserFormViewModel`,
`UsersListViewModel`, `UsersViewModel`, `ProductFormViewModel`, `ProductsListViewModel`,
`ProductsViewModel`, `InventoryViewModel`, `NotificationsListViewModel`, `NotificationsViewModel`,
`MainWindowViewModel`, `OrderKanbanViewModel`, `OrdersViewModel`, `CustomersViewModel`,
`CustomerDetailViewModel` (**17 ViewModels**).

## 9. Code-behind modifiés / confirmés propres

- `src/MMV.App/Views/MainWindow.axaml.cs` : ne reçoit plus de repositories ; reçoit
  `IGenerateLowStockNotificationsUseCase`. **Retiré de l'allowlist code-behind.**
- `src/MMV.App/App.axaml.cs` : **composition root** (seul code-behind autorisé) — résout le use case
  au lieu des trois ports de persistance pour la fenêtre principale.
- Tous les autres `*.axaml.cs` : aucun jeton de persistance (garde-fou vert).

## 10. Tests créés / modifiés

- **Créés (Application, vrai SQLite)** : `Suppliers/SupplierUseCasesTests`,
  `Users/UserUseCasesTests`, `Products/ProductUseCasesTests`,
  `Notifications/NotificationUseCasesTests` — succès, introuvable, anti-doublon,
  commande nulle, constructeur null. **+25 tests** (79 → 104).
- **Créé (App)** : `SupplierFormViewModelDelegationTests` — délégation create/update, constructeur
  null. **+3 tests** (161 → 164).
- **Adaptés (App)** : `OrdersViewModelDeleteDelegationTests`, `SaleFormViewModelTransactionTests`
  (suppression des arguments `IUnitOfWork` retirés).

Total : **491** tests (App 164 · Application 104 · Domain 223) — tous verts.

## 11. Réduction exacte d'allowlist (`AppUiPersistenceGuardrailTests`)

| Allowlist | Avant | Après | Δ |
|---|---|---|---|
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 17 | **0** | −17 (vidée) |
| `AllowedViewModelRepositoryConstructorDependencies` | 48 | **40** | 9 entrées d'écriture retirées, 1 entrée de lecture ajoutée (`ProductsViewModel -> IStockMovementRepository`), soit **−8 net** |
| `AllowedPublicPersistenceProperties` | 0 | 0 | inchangé |
| `AllowedCodeBehindPersistenceFiles` | 2 | **1** (`App.axaml.cs`) | −1 (`MainWindow.axaml.cs`) |

Entrées repository retirées : `InventoryViewModel→IStockMovementRepository`,
`MainWindowViewModel→INotification/IProduct`, `Notifications*→IProduct`,
`ProductFormViewModel→IProduct/INotification`, `SupplierFormViewModel→ISupplier`,
`UserFormViewModel→IUser`. Les 40 entrées restantes sont **exclusivement des lectures** (dette P2D).

## 12. Fichiers modifiés / créés

- **Créés (source)** : `UseCases/Suppliers/**`, `UseCases/Users/**`, `UseCases/Products/**`,
  `UseCases/Notifications/**` (42 fichiers).
- **Modifiés (source)** : `MMV.Application/DependencyInjection.cs`, 17 ViewModels,
  `Views/MainWindow.axaml.cs`, `App.axaml.cs`.
- **Créés (tests)** : 4 classes de tests use cases + 1 classe de délégation VM.
- **Modifiés (tests)** : `AppUiPersistenceGuardrailTests.cs`, `OrdersViewModelDeleteDelegationTests.cs`,
  `SaleFormViewModelTransactionTests.cs`.
- **Docs** : ce rapport + `docs/architecture/P2D-read-application-roadmap.md`.

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check` (propre), `dotnet restore`,
`dotnet build --no-restore -c Debug`, `dotnet test --no-build -c Debug`,
`dotnet list … --vulnerable --include-transitive`, `dotnet tool restore`,
`ef migrations has-pending-model-changes`, `dotnet list …Application… reference`,
`dotnet list …Application… package`.

## 14. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| Build | vert | ✅ 0 erreur |
| Tests | > 463 | ✅ **491** |
| Vulnérabilités | 0 | ✅ 0 |
| `has-pending-model-changes` | false | ✅ false |
| Migration créée | aucune | ✅ aucune |
| Modèle EF modifié | aucun | ✅ aucun |
| `MMV.Application` référence | `MMV.Domain` seul | ✅ |
| `MMV.Application` package | `DI.Abstractions` seul | ✅ |
| Allowlist UoW | 0 si possible | ✅ **0** |
| Propriétés publiques persistance | 0 | ✅ 0 |
| VM avec `SaveChangesAsync`/`Create/Update/DeleteAsync` direct | 0 | ✅ 0 (seulement des commentaires documentaires) |

## 15. Migrations créées ou non

**Aucune migration créée.** Aucune entité Domain / `DbContext` / migration touchée.
`has-pending-model-changes = false`. Le pin `SQLitePCLRaw.bundle_e_sqlite3` (P2C-SEC-1) est intact.

## 16. Risques résiduels

1. **Alignement du Kanban sur `AdvanceOrderStatusUseCase`** — *changement de comportement assumé*.
   L'ancien `OrderKanbanViewModel` ne faisait que `UpdateAsync(statut)` + `SaveChanges`. En
   réutilisant le use case canonique (comme `OrderDetailViewModel`), l'avancement Kanban crée
   désormais, lors de la transition *À fabriquer → En fabrication*, les mouvements de stock de
   fabrication et une notification. C'est un **alignement voulu** des deux surfaces UI sur la même
   orchestration, conforme à la consigne « réutiliser `AdvanceOrderStatusUseCase` ». À valider en
   recette : ne pas avancer une même commande depuis les deux vues (double décrément possible,
   déjà vrai avant sur deux avancements successifs).
2. **Notification « stock bas » à la sauvegarde produit** — *non reportée volontairement*. Dans
   l'application réelle, `ProductFormViewModel` était toujours construit **sans**
   `INotificationRepository` (constructeur à 3 arguments) : le bloc de notification à la sauvegarde
   ne s'exécutait **jamais** (code mort). Il n'a donc pas été porté ; les alertes de stock bas
   restent générées par le flux Notifications/Tableau de bord (`GenerateLowStockNotifications`).
3. **CI distante non vérifiée** (`gh` non authentifié) — la baseline locale est verte ; confirmer
   CI avant merge.

## 17. Violations restantes éventuelles et justification

- **40 dépendances repository en constructeur de VM** subsistent : ce sont **exclusivement des
  lectures d'affichage** (chargement de listes / détail), tolérées par la roadmap P2C (§2/§6) et
  verrouillées par l'allowlist. Elles constituent le périmètre de **P2D** (query use cases + DTO).
- Aucune écriture directe, aucun `IUnitOfWork`, aucune propriété publique de persistance ne subsiste.

## 18. Verdict

**GO (local).** Tous les critères d'acceptation locaux sont satisfaits : écritures UI intégralement
extraites, lectures restantes justifiées et cadrées pour P2D, aucun VM n'expose ni n'utilise de port
de persistance en écriture, allowlists fortement réduites (UoW = 0), 491 tests verts, 0 vulnérabilité,
aucune migration, `MMV.Application` pure. **Réserve unique : confirmation de la CI distante.**

## 19. Préparation P2D

`docs/architecture/P2D-read-application-roadmap.md` créé : état final P2C, inventaire des lectures
restantes, règles DTO applicatifs, interdiction du retour d'entités EF vers l'UI, plan par étapes,
critères d'entrée/sortie et risques.
