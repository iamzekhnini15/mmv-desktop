# Rapport P2D-GLOBAL — VERDICT : **GO PARTIEL (local)**

> Extraction des **lectures** UI restantes vers des *query use cases* Application (première famille de
> use cases *query* de la solution), renvoyant des **DTO applicatifs plats** (jamais d'entité EF suivie).
> Aucun commit, aucun push (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2D-GLOBAL |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : **`p2d-query-cleanup`** (et non `p2c-ui-cleanup`). **Écart assumé, non bloquant** : les
  deux déclencheurs d'arrêt (STOP) de la roadmap sont (a) working tree non propre, (b) P2C-GLOBAL absent.
  Aucun ne s'applique — le working tree était **propre** et P2C-GLOBAL est **présent** (`4c10204
  feat(P2C-GLOBAL): complete UI persistence cleanup` + doc `2780228`). `p2d-query-cleanup` est une branche
  propre issue du merge P2C (PR #12) : nom **plus** adapté au travail P2D que la branche P2C déjà mergée.
- Dernier commit : `d468a11 Merge pull request #12 from iamzekhnini15/p2c-ui-cleanup`.

## 3. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | ✅ vert (0 erreur ; 1 warning préexistant CS1998 `OrderFormViewModel`) |
| `dotnet test MMV.sln` | ✅ **491** (App 164 · Application 104 · Domain 223) |
| `dotnet list … --vulnerable --include-transitive` | ✅ 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | ✅ `false` |
| `MMV.Application` references | ✅ `MMV.Domain` seul |
| `MMV.Application` packages | ✅ `Microsoft.Extensions.DependencyInjection.Abstractions` seul |

Baseline = **GO**.

## 4. Inventaire des lectures UI avant extraction

40 dépendances `I…Repository` en constructeur de ViewModel (allowlist P2C-GLOBAL), **exclusivement des
lectures d'affichage**. Regroupées par module :

| Module | ViewModels (repo lu) | Nature |
|---|---|---|
| **Utilisateurs** | `UsersListViewModel`→`IUserRepository`, `UsersViewModel`→`IUserRepository` | liste (GetAll) |
| **Fournisseurs** | `SuppliersListViewModel`→`ISupplierRepository`, `SuppliersViewModel`→`ISupplierRepository` | liste (GetAll) + fiche (GetWithProducts) |
| **Notifications** | `NotificationsListViewModel`→`INotificationRepository`, `NotificationsViewModel`→`INotificationRepository` | liste (GetAll) + compteur (CountUnread) |
| **Produits / Stock** | `ProductsListViewModel`, `ProductsViewModel` (×IProduct/ISupplier/IStockMovement), `InventoryViewModel`, `StockMovementsListViewModel`, `StockMovementsViewModel`, `StockMovementFormViewModel` | listes + fiche produit + inventaire + mouvements + pickers |
| **Clients / Ordonnances** | `CustomersListViewModel`, `CustomerDetailViewModel` (×4), `CustomerInfoViewModel`, `CustomerPrescriptionsViewModel`, `CustomerPurchaseHistoryViewModel`, `CustomersViewModel` (×4) | fiches composites + historique + ordonnances |
| **Commandes / Ventes** | `OrdersListViewModel`, `OrdersViewModel` (×4), `OrderKanbanViewModel`, `OrderFormViewModel` (×3), `SaleFormViewModel` (×2) | listes + Kanban + données de référence de formulaires |

## 5. Stratégie appliquée

**Découpage strict une étape = un module**, du plus simple au plus composite (ordre de la roadmap
`P2D-read-application-roadmap.md §6`), **compilation après chaque module**, tests verts à la fin.

Principe directeur : **déplacement iso-fonctionnel**. Pour chaque écran migré :

1. La lecture directe `I…Repository` du ViewModel est remplacée par un *query use case* Application dédié.
2. Le use case **projette** l'entité vers un **DTO applicatif plat** (`init`-only, sans navigation EF).
   Aucune entité EF suivie ne franchit la frontière UI.
3. Le ViewModel expose désormais des **DTO** (collections / propriétés), avec **parité de noms** pour ne
   **toucher aucun `.axaml`** (liaisons par réflexion résolues par nom). Les commandes / événements /
   `InitializeForEdit` typés « entité » sont retypés « DTO » (changements **code-only**).
4. Recherche / filtre / tri / pagination **restent en présentation** (iso-fonctionnel P2C — risk 3 roadmap).

**Décision de périmètre (GO PARTIEL) :** 3 modules migrés intégralement (**Utilisateurs, Fournisseurs,
Notifications**). Les 3 modules restants (**Produits/Stock, Clients, Commandes/Ventes**) sont **des écrans
composites à graphes d'entités** (fiches détaillées, historiques, données de référence de formulaires,
Kanban) dont l'entité circule à travers de nombreux événements inter-VM, pickers de formulaire et
code-behind. Les migrer **en un seul passage non interactif, sans possibilité d'exécuter l'UI Avalonia pour
valider l'iso-fonctionnalité**, présente un risque de régression d'affichage inacceptable. Conformément aux
clauses d'échappement explicites de la consigne (§13 « documenter comme reliquat P2D-final au lieu de forcer
une grosse refonte risquée » ; §19 « ne pas forcer ; documenter précisément ; verdict = GO partiel »), ils
sont **documentés en reliquat P2D-4/5/6** (voir §17) avec allowlist minimale justifiée.

## 6. Query use cases créés (5)

| Étape | Module | Query use case | Sortie |
|---|---|---|---|
| P2D-1 | Utilisateurs | `IListUsersUseCase` | `IReadOnlyList<UserListItemDto>` |
| P2D-2 | Fournisseurs | `IListSuppliersUseCase` | `IReadOnlyList<SupplierListItemDto>` |
| P2D-2 | Fournisseurs | `IGetSupplierWithProductsUseCase` | `SupplierDetailsDto?` |
| P2D-3 | Notifications | `IListNotificationsUseCase` | `IReadOnlyList<NotificationListItemDto>` |
| P2D-3 | Notifications | `ICountUnreadNotificationsUseCase` | `int` |

Chacun : `Query.cs` (objet d'entrée, garde « query nulle »), `Dto.cs`, `I…UseCase.cs`, `…UseCase.cs`.
Enregistrés en **`Scoped`** dans `MMV.Application/DependencyInjection.cs` (même portée qu'`OpticDbContext`
et les repositories). `MMV.Application` reste **pure** (référence `MMV.Domain` seul).

## 7. DTO applicatifs créés (5)

`UserListItemDto`, `SupplierListItemDto`, `SupplierDetailsDto`, `SupplierProductItemDto`,
`NotificationListItemDto`. Tous : `sealed`, propriétés `init`-only, aucun type Avalonia / CommunityToolkit,
aucune navigation EF, uniquement les champs consommés par l'écran (+ enums Domain, qui ne sont pas des
entités). `SupplierListItemDto.Products` reste **vide** en contexte liste : iso-fonctionnel (l'ancien
`GetAllAsync` `AsNoTracking` sans `Include` ne chargeait jamais la navigation ; le compteur « {0} ref. »
valait déjà 0), la collection n'existe que pour préserver la liaison `Products.Count`.

## 8. ViewModels modifiés (8)

- **Utilisateurs** : `UsersListViewModel`, `UsersViewModel`, `UserFormViewModel` (`InitializeForEdit` →
  `UserListItemDto`).
- **Fournisseurs** : `SuppliersListViewModel`, `SuppliersViewModel`, `SupplierDetailViewModel`,
  `SupplierFormViewModel` (`InitializeForEdit` → `SupplierDetailsDto` ; événement `SupplierSaved` sans
  entité).
- **Notifications** : `NotificationsListViewModel`, `NotificationsViewModel`.
- **Coordination** : `ProductsViewModel` (module Produits, non migré) reçoit et **transmet** les query use
  cases fournisseur à `SuppliersViewModel` ; il **conserve** `ISupplierRepository` pour son propre picker
  (reliquat P2D-4).

## 9. Code-behind modifiés / confirmés propres

- `src/MMV.App/Views/Products/SuppliersView.axaml.cs` : le cast de ligne `is Supplier` devient
  `is SupplierListItemDto` (les lignes sont désormais des DTO). **Aucun jeton de persistance** ajouté ;
  reste hors allowlist code-behind (garde-fou vert).
- `App.axaml.cs` : composition root inchangé (les query use cases sont enregistrés via `AddApplication`).
- Aucun `.axaml` modifié (interdiction §16 respectée).

## 10. Tests créés / modifiés

- **Créés — Application (vrai SQLite temporaire)** : `Users/ListUsersUseCaseTests`,
  `Suppliers/SupplierQueryUseCasesTests`, `Notifications/NotificationQueryUseCasesTests` — projection DTO
  (jamais d'entité), tri, cas introuvable/vide, query nulle, constructeur null. **+15** (104 → 119).
- **Créé — App** : `ViewModels/P2DQueryViewModelTests` — délégation au query use case, remplissage d'état,
  conservation du filtre « actifs uniquement », constructeurs null. **+6** (164 → 170).
- **Adapté — App** : `SupplierFormViewModelDelegationTests` (édition via `SupplierDetailsDto` ; événement
  `SupplierSaved` sans entité).

Total : **512** tests (App 170 · Application 119 · Domain 223) — tous verts.

## 11. Réduction exacte d'allowlist (`AppUiPersistenceGuardrailTests`)

| Allowlist | Avant (P2C-GLOBAL) | Après (P2D-GLOBAL) | Δ |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 40 | **34** | **−6** |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 0 | 0 | inchangé |
| `AllowedPublicPersistenceProperties` | 0 | 0 | inchangé |
| `AllowedCodeBehindPersistenceFiles` | 1 (`App.axaml.cs`) | 1 (`App.axaml.cs`) | inchangé |

Entrées retirées : `UsersListViewModel→IUser`, `UsersViewModel→IUser`, `SuppliersListViewModel→ISupplier`,
`SuppliersViewModel→ISupplier`, `NotificationsListViewModel→INotification`,
`NotificationsViewModel→INotification`. La règle « l'allowlist ne fait que diminuer » est respectée (aucune
entrée ajoutée ; l'assertion anti-allowlist-obsolète passe).

## 12. Garde-fou anti-entités Domain

**Reporté (justifié), conforme à §13.** Les 3 ViewModels migrés n'exposent plus d'entité en propriété
publique (ils exposent des DTO). Mais les modules **non migrés** (Produits/Stock, Clients, Commandes/Ventes)
exposent encore massivement des entités EF (`ObservableCollection<Product>`, `Customer`, `Order`, …). Un
test `ViewModels_DoNotExposeDomainEntitiesAsPublicProperties` global **échouerait** aujourd'hui ; l'ajouter
imposerait la refonte immédiate des 3 modules composites — précisément la « refonte démesurée risquée » que
§13 demande d'éviter. Il sera introduit à la clôture P2D (P2D-7), une fois le reliquat soldé.

## 13. Fichiers modifiés / créés

- **Créés (source, 20 fichiers)** : `UseCases/Users/ListUsers/**`, `UseCases/Suppliers/ListSuppliers/**`,
  `UseCases/Suppliers/GetSupplierWithProducts/**`, `UseCases/Notifications/ListNotifications/**`,
  `UseCases/Notifications/CountUnreadNotifications/**`.
- **Modifiés (source)** : `MMV.Application/DependencyInjection.cs`, 8 ViewModels + `ProductsViewModel`
  (coordination), `Views/Products/SuppliersView.axaml.cs`.
- **Créés (tests, 4 classes)** : 3 classes Application + 1 classe App (`P2DQueryViewModelTests`).
- **Modifiés (tests)** : `AppUiPersistenceGuardrailTests.cs`, `SupplierFormViewModelDelegationTests.cs`.
- **Docs** : ce rapport.

## 14. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check` (propre), `dotnet restore`,
`dotnet build --no-restore -c Debug`, `dotnet test --no-build -c Debug`,
`dotnet list … --vulnerable --include-transitive`, `dotnet tool restore`,
`ef migrations has-pending-model-changes`, `dotnet list …Application… reference`,
`dotnet list …Application… package`.

## 15. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| Build | vert | ✅ 0 erreur |
| Tests | > 491 | ✅ **512** |
| Vulnérabilités | 0 | ✅ 0 |
| `has-pending-model-changes` | false | ✅ false |
| Migration créée | aucune | ✅ aucune |
| Modèle EF modifié | aucun | ✅ aucun |
| `.axaml` modifié | aucun | ✅ aucun |
| `MMV.Application` référence | `MMV.Domain` seul | ✅ |
| `MMV.Application` package | `DI.Abstractions` seul | ✅ |
| Allowlist repository | ↓ (0 si possible) | ⚠️ **34** (−6 ; 0 non atteint — reliquat) |
| Allowlist UoW / propriétés publiques persistance | 0 | ✅ 0 |
| Code-behind persistant | `App.axaml.cs` seul | ✅ |

## 16. Migrations créées ou non

**Aucune migration créée.** Aucune entité Domain / `DbContext` / migration / configuration EF touchée.
`has-pending-model-changes = false`. Le pin `SQLitePCLRaw.bundle_e_sqlite3` (P2C-SEC-1) est intact.

## 17. Risques résiduels / reliquat P2D (justifié)

**34 dépendances repository** subsistent en constructeur de ViewModel — **exclusivement des lectures** — sur
3 modules composites, verrouillées par l'allowlist (qui ne peut que diminuer) :

- **P2D-4 — Produits / Stock** : `ProductsListViewModel`, `ProductsViewModel` (×3), `InventoryViewModel`,
  `StockMovements*` (×5), `StockMovementFormViewModel`, `ProductFormViewModel→ISupplier` (picker). Fiche
  produit (`ProductDetailViewModel`) et pickers font circuler l'entité `Product` via de nombreux événements.
  Cibles : `ListProductsQuery`, `GetProductDetailsQuery`, `ListProductsForPickerQuery`,
  `ListSuppliersForPickerQuery`, `GetInventoryOverviewQuery`, `ListStockMovementsQuery`. Attention au
  service de stock atomique (P2A/P2B) — inchangé.
- **P2D-5 — Clients / Ordonnances** : `CustomersListViewModel`, `CustomerDetailViewModel` (×4),
  `CustomerInfoViewModel`, `CustomerPrescriptionsViewModel`, `CustomerPurchaseHistoryViewModel`,
  `CustomersViewModel` (×4). Fiche client composite (client + ventes + ordonnances + produits).
- **P2D-6 — Commandes / Ventes** : `OrdersListViewModel`, `OrdersViewModel` (×4), `OrderKanbanViewModel`,
  `OrderFormViewModel` (×3), `SaleFormViewModel` (×2). Kanban et données de référence de formulaires.

Motif du report : **risque de régression d'affichage** non vérifiable en passage non interactif (UI Avalonia
non exécutable ici) + **couplage XAML fort** (bindings compilés `x:DataType`, casts `local:Entity`,
`CommandParameter` typés entité, code-behind). Aucune écriture directe, aucun `IUnitOfWork`, aucune propriété
publique de persistance ne subsiste (invariants P2C préservés).

## 18. Verdict

**GO PARTIEL (local).** Trois modules (**Utilisateurs, Fournisseurs, Notifications**) sont intégralement
migrés vers des *query use cases* renvoyant des DTO applicatifs ; l'allowlist repository passe de **40 à 34**
(−6), sans jamais grossir. Build vert, **512** tests verts (> 491), 0 vulnérabilité,
`has-pending-model-changes = false`, aucune migration, aucun `.axaml`/Domain/Infrastructure touché,
`MMV.Application` toujours pure. Les 3 modules composites restants sont **documentés en reliquat P2D-4/5/6**
avec allowlist minimale justifiée, conformément aux clauses §13/§19 (« ne pas forcer une refonte risquée »).
L'objectif final `AllowedViewModelRepositoryConstructorDependencies = 0` **n'est pas encore atteint** ; il
reste la cible de clôture P2D-7.

## 19. Prochaine étape candidate

**P2D-4 — Produits / Stock** : introduire `ListProductsQuery` / `GetProductDetailsQuery` /
`ListSuppliersForPickerQuery` / `GetInventoryOverviewQuery` / `ListStockMovementsQuery` + DTO dédiés, en
migrant `ProductsListViewModel`, `ProductDetailViewModel`, `InventoryViewModel` et les VM de mouvements,
**une étape = un module = un commit** (protocole P2B/P2C, avec exécution UI de recette pour valider
l'iso-fonctionnalité des écrans composites). Ne pas ouvrir P3 / SaaS / Organization / Store / Subscription.
