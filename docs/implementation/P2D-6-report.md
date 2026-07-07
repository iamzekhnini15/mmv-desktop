# Rapport P2D-6 — Commandes / Ventes : lectures UI → Query Use Cases — VERDICT : **GO PARTIEL (local)**

> Extraction des **lectures** UI du module **Commandes** vers des *query use cases* Application renvoyant des **DTO
> applicatifs plats / composites** (jamais d'entité EF suivie). Le module **Ventes** (`SaleFormViewModel`) est laissé
> en **reliquat justifié** (écran composite à graphe `GlassDetail` + panier d'entités `OrderItem`). Aucun commit,
> aucun push (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2D-6 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2d-query-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : **`p2d-query-cleanup`** ✓
- Working tree : **propre** ✓
- Présence de `e81f99b feat(P2D)`, `37067cc docs(P2D)`, `6592d46 feat(P2D-4)`, `d11b45c docs(P2D-4)` ✓
- Présence de `9b0806d feat(P2D-5): migrate customer read flows to query use cases` ✓
- Le rapport `docs(P2D-5)` a été **intégré au commit `feat(P2D-5)`** (pas de commit docs distinct) ; le contenu de
  `docs/implementation/P2D-5-report.md` est bien versionné (working tree propre). Précondition = **GO**.

## 3. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | ✅ vert (0 erreur ; 1 warning préexistant CS1998 `OrderFormViewModel.LoadExistingOrderAsync`) |
| `dotnet test MMV.sln` | ✅ **543** (App 183 · Application 137 · Domain 223) |
| `dotnet list … --vulnerable --include-transitive` | ✅ 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | ✅ `false` |
| `MMV.Application` references / packages | ✅ `MMV.Domain` seul / `Microsoft.Extensions.DependencyInjection.Abstractions` seul |

Baseline = **GO**.

## 4. Inventaire Commandes / Ventes avant extraction

Dépendances repository en constructeur de ViewModel, **exclusivement des lectures d'affichage** (les écritures sont
déjà extraites en P2B/P2C) :

| ViewModel | Repo injecté | Méthode | Données consommées | Entité exposée | Query cible | DTO cible | Risque | Action |
|---|---|---|---|---|---|---|---|---|
| `OrdersListViewModel` | `IOrderRepository` | `GetAllWithItemsAsync` | liste (n°, client, statut, dates, montants) ; filtre statut/recherche/pagination en présentation | `ObservableCollection<Order>` | `IListOrdersUseCase` | `OrderListItemDto` (+ `Sale`/`Customer` imbriqués) | moyen (liaisons réflexion imbriquées) | **migré** |
| `OrderKanbanViewModel` | `IOrderRepository` | `GetAllWithItemsAsync` | répartition par statut (n°, client, montant) | `ObservableCollection<Order>` ×6 | `IListOrdersUseCase` (réutilisé) | `OrderListItemDto` | moyen | **migré** |
| `OrderFormViewModel` | `ICustomerRepository` | `GetAllAsync` | sélecteur client (nom, tél, email, id) | `ObservableCollection<Customer>` | `IListCustomersForPickerUseCase` | `CustomerPickerItemDto` | faible (liaisons `SelectedCustomer.*` **compilées**) | **migré** |
| `OrderFormViewModel` | `IProductRepository` | `GetAllAsync` | sélecteur article (réf, nom, prix, catégorie, id) | `ObservableCollection<Product>` | `IListProductsForOrderPickerUseCase` | `OrderProductPickerDto` | faible (DataTemplate **compilé** `x:DataType`) | **migré** |
| `OrderFormViewModel` | `IPrescriptionRepository` | `GetByCustomerIdAsync` | ordonnances du client (auto-remplissage verres) | `ObservableCollection<Prescription>` | `IListPrescriptionsByCustomerUseCase` (P2D-5 réutilisé) | `PrescriptionListItemDto` | faible | **migré** |
| `OrdersViewModel` | `ICustomerRepository` / `IProductRepository` / `IPrescriptionRepository` | — (pass-through) | transmis à `OrderFormViewModel` | non | (transmet les query use cases) | — | faible | **migré** |
| `OrdersViewModel` | `IOrderRepository` | `GetWithItemsAsync` | recharge la fiche détaillée (entité complète → détail → **formulaire d'ÉDITION**) | non (threadé) | *(reliquat)* | — | **élevé** | **conservé** |
| `SaleFormViewModel` | `IProductRepository` | `GetAllAsync` | catalogue à graphe (`Product.GlassDetail`, filtrage de compatibilité) ; panier d'entités `OrderItem` (nav. `Product`) | `ObservableCollection<Product>` | *(reliquat)* | — | **élevé** | **reporté** |
| `SaleFormViewModel` | `IPrescriptionRepository` | `GetLatestByCustomerIdAsync` | ordonnance active (suggestion de verres) | `Prescription?` | *(reliquat)* | — | **élevé** | **reporté** |
| `CustomerDetailViewModel` / `CustomersViewModel` | `IProductRepository` / `IPrescriptionRepository` | — (pass-through) | transmis à `SaleFormViewModel` | non | *(reliquat SaleForm)* | — | élevé | **conservé** |

`OrderDetailViewModel` et `FabricationSheetViewModel` **n'ont aucune dépendance repository** (ils reçoivent l'entité
`Order` rechargée) : hors périmètre du garde-fou, inchangés.

## 5. Stratégie appliquée

**Déplacement iso-fonctionnel**, compilation après chaque groupe. **Décision de périmètre (GO PARTIEL) : module
Commandes migré, module Ventes (`SaleFormViewModel`) reporté.**

- **Liste + Kanban** — le tri décroissant par date et la répartition par statut restent en présentation. Les DTO
  sont **composites** (`OrderListItemDto` → `OrderSaleSummaryDto` → `OrderCustomerSummaryDto`), **reproduisant
  exactement** les chemins de liaison XAML existants (`Sale.Customer.FirstName`, `Sale.FinalAmount`, `Status`, …) et
  leurs **types** (`decimal`/`decimal?`, enum `OrderStatus`) — **aucun `.axaml` de liste/Kanban modifié** (liaisons
  par réflexion, parité de noms/types → comportement d'affichage identique). Ce sont des DTO plats (aucun suivi EF,
  aucune navigation vers le `DbContext`) : l'invariant « aucune entité EF suivie ne franchit la frontière UI » est
  respecté. La liste/le Kanban émettent le DTO via `ViewOrderDetailRequested` ; `OrdersViewModel.OnViewOrderDetail`
  n'en consomme que l'**identifiant** pour recharger l'entité complète (comportement d'origine).
- **Formulaire de commande** — les trois sélecteurs (clients, produits, ordonnances) passent aux query use cases. Le
  sélecteur produit est une **liaison compilée** (`DataTemplate x:DataType="entities:Product"`) : le changement de
  type est **vérifié par le compilateur** (mise à jour du `x:DataType` → `pickers:OrderProductPickerDto`). Les liaisons
  `SelectedCustomer.*` sont **compilées** (contexte VM). Le panier est un `OrderItemLine` (**ViewModel**, pas une
  entité) : sa migration au DTO produit ne touche pas d'entité. L'entité `Order` d'édition (`_existingOrder`) reste
  un paramètre de constructeur (threadé depuis le détail) — autorisé (ce n'est pas un repository).
- **Réutilisation (DRY).** Le Kanban réutilise `IListOrdersUseCase` de la liste (répartition en présentation, comme
  l'original) ; le formulaire de commande réutilise `IListPrescriptionsByCustomerUseCase` (P2D-5). Cohérent avec la
  réutilisation `ListSuppliers`/`ListPrescriptions` des étapes précédentes.

**Reportés (reliquat P2D-6 justifié, clause §20).**
- **Formulaire de VENTE (`SaleFormViewModel`)** : écran **composite à graphe**. (1) Le filtrage de compatibilité des
  verres lit la navigation `Product.GlassDetail` (`GlassType`, `PowerLimitMin/Max`) ; (2) le **panier** est composé
  d'entités **`OrderItem`** portant la navigation `Product` (liaisons XAML `Product.Name` / `Product.Reference` sur
  `OrderItems`) et alimente `RegisterSaleUseCase`. Migrer imposerait de remplacer le modèle de panier (entité →
  présentation) — **non validable en iso-fonctionnel sans exécution UI de recette** (Avalonia non exécutable ici).
  Même clause d'échappement que la liste clients P2D-5 / la liste produit P2D-4. `CustomerDetailViewModel` /
  `CustomersViewModel` conservent donc `IProductRepository` + `IPrescriptionRepository` uniquement pour **construire**
  `SaleFormViewModel`.
- **`OrdersViewModel → IOrderRepository`** : rechargement de la fiche détaillée (`GetWithItemsAsync`) → l'entité
  `Order` complète alimente `OrderDetailViewModel` puis, via `EditRequested`, le **formulaire d'ÉDITION** (chemin
  d'écriture). Threader un DTO à travers ce chemin n'est pas validable sans recette UI.

## 6. Query use cases créés (3) + réutilisés (1)

| Module (dossier) | Query use case | Sortie | Repository consommé |
|---|---|---|---|
| `Orders/ListOrders` | `IListOrdersUseCase` | `IReadOnlyList<OrderListItemDto>` | `IOrderRepository.GetAllWithItemsAsync` |
| `Customers/ListCustomersForPicker` | `IListCustomersForPickerUseCase` | `IReadOnlyList<CustomerPickerItemDto>` | `ICustomerRepository.GetAllAsync` |
| `Products/ListProductsForOrderPicker` | `IListProductsForOrderPickerUseCase` | `IReadOnlyList<OrderProductPickerDto>` | `IProductRepository.GetAllAsync` |
| *(réutilisé P2D-5)* `Prescriptions/ListPrescriptionsByCustomer` | `IListPrescriptionsByCustomerUseCase` | `IReadOnlyList<PrescriptionListItemDto>` | `IPrescriptionRepository.GetByCustomerIdAsync` |

Chacun : `…Query.cs` (objet d'entrée + garde « query nulle »), `…Dto.cs`, `I…UseCase.cs`, `…UseCase.cs`. Enregistrés
en **`Scoped`** dans `MMV.Application/DependencyInjection.cs`. `MMV.Application` reste **pure** (`MMV.Domain` seul).

## 7. DTO applicatifs créés (5)

- **`OrderListItemDto`** : `OrderId`, `OrderNumber`, `Status` (enum `OrderStatus`), `OrderDate`, `EstimatedDelivery`,
  `Notes`, `Sale` (`OrderSaleSummaryDto?`).
- **`OrderSaleSummaryDto`** : `FinalAmount` (`decimal`), `DepositAmount`/`RemainingAmount` (`decimal?`), `Customer`
  (`OrderCustomerSummaryDto?`).
- **`OrderCustomerSummaryDto`** : `FirstName`, `LastName`.
- **`CustomerPickerItemDto`** : `CustomerId`, `FirstName`, `LastName`, `Phone`, `Email`.
- **`OrderProductPickerDto`** : `ProductId`, `Reference`, `Name`, `SalePrice`, `Category` (enum `ProductCategoryEnum`).

Tous : `sealed`, propriétés `init`-only, aucun type Avalonia/CommunityToolkit, aucune navigation EF. `OrderProductPickerDto`
est **distinct** du `ProductPickerItemDto` de P2D-4 (le sélecteur de commande consomme en plus `SalePrice` + `Category`).
La forme composite d'`OrderListItemDto` est le choix explicite pour rester iso-fonctionnel (chemins de liaison XAML
préservés). Les enums Domain (`OrderStatus`, `ProductCategoryEnum`) sont des valeurs métier stables (pas des entités).

## 8. ViewModels modifiés (4)

- `OrdersListViewModel` → `IListOrdersUseCase` (retrait `IOrderRepository`) ; `Orders`/`FilteredOrders` retypés
  `OrderListItemDto` ; `ViewOrderDetailRequested`/`ViewDetailCommand` retypés DTO. Filtres/recherche/pagination inchangés.
- `OrderKanbanViewModel` → `IListOrdersUseCase` (retrait `IOrderRepository`) ; 6 colonnes + `AdvanceStatusCommand`/
  `ViewDetailCommand`/`ViewOrderDetailRequested` retypés DTO. **`IAdvanceOrderStatusUseCase` (écriture) inchangé.**
- `OrderFormViewModel` (+ `OrderItemLine`) → `IListCustomersForPickerUseCase`, `IListProductsForOrderPickerUseCase`,
  `IListPrescriptionsByCustomerUseCase` (retrait des 3 repositories). Collections/propriétés/commandes de sélecteur
  retypées DTO. **Chemins de sauvegarde (`ICreateOrderUseCase`/`IUpdateOrderUseCase`) inchangés** ; `_existingOrder`
  reste l'entité threadée (édition).
- `OrdersViewModel` → injecte les 4 query use cases, transmet à liste/Kanban/formulaire ; **conserve `IOrderRepository`**
  (recharge du détail, reliquat). `OnViewOrderDetail` retypé `OrderListItemDto` (n'en lit que `OrderId`).

## 9. XAML / code-behind

**1 seul `.axaml` modifié, justifié (typage) : `Views/Orders/OrderFormView.axaml`.** Le DataTemplate du sélecteur
produit portait une **liaison compilée** `x:DataType="entities:Product"` ; le type d'élément devenant
`OrderProductPickerDto`, le `x:DataType` a été mis à jour (`pickers:OrderProductPickerDto`, mêmes noms de propriétés
`Reference`/`Name`/`SalePrice`) et le xmlns `entities` remplacé par `pickers`. **Aucun autre `.axaml` modifié** :
- Liste/Kanban : liaisons **par réflexion** (DataTemplate sans `x:DataType`) sur des DTO composites à **noms/chemins
  identiques** (`OrderNumber`, `Status`, `OrderDate`, `EstimatedDelivery`, `Sale.FinalAmount`, `Sale.DepositAmount`,
  `Sale.RemainingAmount`, `Sale.Customer.FirstName/LastName`) → résolution inchangée.
- Formulaire (client / ordonnance) : liaisons `SelectedCustomer.*` **compilées** (recompilées et vérifiées contre le
  DTO) ; popup client + combo ordonnance par réflexion (noms `FirstName`/`LastName`/`Phone`, `DoctorName`/`IssueDate`).

**Aucun code-behind modifié.**

## 10. Tests créés / modifiés

- **Créés — Application (vrai SQLite temporaire, jamais InMemory)** : `UseCases/Orders/ListOrdersUseCaseTests`
  (projection DTO composite, portage montants/client, tri décroissant par date, cas vide, query nulle, constructeur
  null) **+4** ; `UseCases/Customers/ListCustomersForPickerUseCaseTests` (projection, tri par nom, champs, cas vide,
  query nulle, ctor null) **+4** ; `UseCases/Products/ListProductsForOrderPickerUseCaseTests` (projection, prix +
  catégorie, tri par nom, cas vide, query nulle, ctor null) **+4**. Total **+12** (137 → 149).
- **Créé — App** : `ViewModels/P2D6OrderReadViewModelTests` (liste : délégation, remplissage, **filtre statut**,
  **recherche**, événement détail DTO, ctor null ; Kanban : **répartition par statut**, avancement délégué avec statut
  suivant, ctor null ×2) **+9** (183 → 192).
- **Adaptés — App** : `OrderFormViewModelCreateDelegationTests`, `OrderFormViewModelUpdateDelegationTests`,
  `OrderFormViewModelNumberingTests` (mocks repository → mocks query use case ; `Customer`/`Product` → DTO) ;
  `OrdersViewModelDeleteDelegationTests` (nouvelle signature `OrdersViewModel` ; `ViewDetailCommand` reçoit un
  `OrderListItemDto`) ; `AppUiPersistenceGuardrailTests` (allowlist réduite).

Total : **564** tests (App 192 · Application 149 · Domain 223) — tous verts.

## 11. Réduction exacte d'allowlist (`AppUiPersistenceGuardrailTests`)

| Allowlist | Avant (P2D-5) | Après (P2D-6) | Δ |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 19 | **11** | **−8** |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 0 | 0 | inchangé |
| `AllowedPublicPersistenceProperties` | 0 | 0 | inchangé |
| `AllowedCodeBehindPersistenceFiles` | 1 (`App.axaml.cs`) | 1 (`App.axaml.cs`) | inchangé |

**8 entrées retirées** : `OrderFormViewModel -> ICustomerRepository`, `OrderFormViewModel -> IProductRepository`,
`OrderFormViewModel -> IPrescriptionRepository`, `OrderKanbanViewModel -> IOrderRepository`,
`OrdersListViewModel -> IOrderRepository`, `OrdersViewModel -> ICustomerRepository`,
`OrdersViewModel -> IProductRepository`, `OrdersViewModel -> IPrescriptionRepository`. Aucune entrée ajoutée ;
l'assertion anti-allowlist-obsolète reste verte (les 11 entrées restantes existent toujours).

**11 entrées restantes** (reliquat justifié) : `OrdersViewModel -> IOrderRepository` (détail) ;
`SaleFormViewModel -> I{Product,Prescription}Repository` + pass-through `CustomerDetailViewModel`/`CustomersViewModel ->
I{Product,Prescription}Repository` (Ventes composite) ; `CustomersListViewModel`/`CustomersViewModel -> ICustomerRepository`
(liste clients, P2D-7) ; `ProductsListViewModel`/`ProductsViewModel -> IProductRepository` (liste produit, P2D-4).

## 12. Fichiers modifiés / créés

- **Créés (source, 14)** : `UseCases/Orders/ListOrders/**` (6 : `OrderListItemDto`, `OrderSaleSummaryDto`,
  `OrderCustomerSummaryDto`, `ListOrdersQuery`, `IListOrdersUseCase`, `ListOrdersUseCase`), `UseCases/Customers/
  ListCustomersForPicker/**` (4), `UseCases/Products/ListProductsForOrderPicker/**` (4).
- **Modifiés (source, 6)** : `MMV.Application/DependencyInjection.cs`, `OrdersListViewModel`, `OrderKanbanViewModel`,
  `OrderFormViewModel`, `OrdersViewModel`, `Views/Orders/OrderFormView.axaml`.
- **Créés (tests, 4)** : `ListOrdersUseCaseTests`, `ListCustomersForPickerUseCaseTests`,
  `ListProductsForOrderPickerUseCaseTests`, `P2D6OrderReadViewModelTests`.
- **Modifiés (tests, 5)** : `AppUiPersistenceGuardrailTests`, `OrderFormViewModelCreateDelegationTests`,
  `OrderFormViewModelUpdateDelegationTests`, `OrderFormViewModelNumberingTests`, `OrdersViewModelDeleteDelegationTests`.
- **Docs** : ce rapport ; `docs/architecture/P2D-read-application-roadmap.md` (note d'état P2D-6).

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check` (propre), `dotnet restore`,
`dotnet build --no-restore -c Debug`, `dotnet test --no-build -c Debug`, `dotnet list … --vulnerable --include-transitive`,
`dotnet tool restore`, `ef migrations has-pending-model-changes`, `dotnet list …Application… reference`,
`dotnet list …Application… package`.

## 14. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| Build | vert | ✅ 0 erreur (1 warning préexistant CS1998) |
| Tests | > 543 | ✅ **564** |
| Vulnérabilités | 0 | ✅ 0 |
| `has-pending-model-changes` | false | ✅ false |
| Migration créée | aucune | ✅ aucune |
| Modèle EF modifié | aucun | ✅ aucun |
| `.axaml` modifié | justifié | ✅ 1 (`x:DataType` compilé, §9) |
| Code-behind modifié | aucun | ✅ aucun |
| `MMV.Application` référence / package | `MMV.Domain` / `DI.Abstractions` seuls | ✅ |
| Allowlist repository | ↓ | ✅ **11** (−8) |
| Allowlist UoW / propriétés publiques persistance | 0 | ✅ 0 |
| Code-behind persistant | `App.axaml.cs` seul | ✅ |

## 15. Migrations créées ou non

**Aucune migration créée.** Aucune entité Domain / `DbContext` / migration / configuration EF touchée.
`has-pending-model-changes = false`. Écritures (Create/Update/Delete commande, avancement de statut, encaissement,
`RegisterSale`) **inchangées**. Pin SQLite (P2C-SEC-1) intact.

## 16. Risques résiduels

1. **Liaisons par réflexion imbriquées (liste/Kanban)** : les DTO composites reproduisent les chemins
   `Sale.Customer.*` / `Sale.FinalAmount` avec des **types identiques** ; la parité de noms/types borne le risque,
   mais ces liaisons ne sont **pas vérifiées par le compilateur** (non validées par recette UI, Avalonia non
   exécutable ici). Le formulaire (sélecteur produit + `SelectedCustomer.*`) est, lui, **compilé** (vérifié).
2. **Reliquats conservés** (détail commande + lectures SaleForm) : continuent de manipuler des entités EF via
   repositories injectés — invariant P2C préservé (aucune écriture directe, aucun `IUnitOfWork`, aucune propriété
   publique de persistance).

## 17. Repositories restants sur Commandes / Ventes et justification

**7 dépendances** subsistent sur le périmètre (sur 11 au total dans l'allowlist), verrouillées par l'allowlist qui ne
peut que diminuer :
- `OrdersViewModel → IOrderRepository` : rechargement de la fiche détaillée (`GetWithItemsAsync`) ; l'entité `Order`
  complète est threadée vers `OrderDetailViewModel` puis le **formulaire d'ÉDITION** (chemin d'écriture). Cible :
  `GetOrderDetailsUseCase` + threading DTO à valider par recette UI (P2D-7).
- `SaleFormViewModel → IProductRepository` / `→ IPrescriptionRepository` et les pass-through
  `CustomerDetailViewModel`/`CustomersViewModel → IProductRepository` / `→ IPrescriptionRepository` : formulaire de
  **VENTE** composite (catalogue à graphe `GlassDetail`, panier d'entités `OrderItem` à navigation `Product`). Cible :
  `GetSaleFormReferenceDataQuery` + refonte du modèle de panier, à valider par recette UI (P2D-7).

## 18. Verdict

**GO PARTIEL (local).** Les lectures du module **Commandes** — liste, Kanban et données de référence du formulaire de
commande (clients, produits, ordonnances) — sont sorties vers des *query use cases* Application renvoyant des **DTO
plats / composites** ; l'allowlist repository passe de **19 à 11** (−8), sans jamais grossir. Build vert, **564**
tests verts (> 543), 0 vulnérabilité, `has-pending-model-changes = false`, aucune migration, aucun modèle EF / Domain /
Infrastructure touché (1 `.axaml` adapté au `x:DataType` compilé, §9), `MMV.Application` toujours pure, `IUnitOfWork`
UI = 0, aucune écriture directe UI. Le module **Ventes** (`SaleFormViewModel`, écran composite `GlassDetail` + panier
d'entités `OrderItem`) et le **rechargement de la fiche détaillée** (threading vers le formulaire d'ÉDITION) sont
**documentés en reliquat justifié**, conformément aux clauses §19/§20.

## 19. Validation CI distante (commit applicatif)

Commit `0da5eca` poussé sur `p2d-query-cleanup` ; workflow **CI** déclenché sur `push`, **terminé avec succès**.

| Élément | Valeur |
|---|---|
| Run CI | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/28815010751 |
| Identifiant du run | `28815010751` |
| Commit testé | `0da5eca296f2adda296ad376ece41f815d5b1879` |
| Branche testée | `p2d-query-cleanup` |
| Event | `push` |
| Restore | ✅ success |
| Build | ✅ success |
| Test | ✅ success — **564** (App 192 · Application 149 · Domain 223) |
| Audit NuGet | ✅ success — 0 vulnérabilité |
| Restore .NET tools | ✅ success |
| Check EF Core pending model changes | ✅ success — « No changes have been made to the model since the last migration. » |
| Statut final du workflow | ✅ **completed / success** (`2m49s`) |

## 20. Prochaine étape candidate

**P2D-7 — Clôture P2D** : solder les reliquats à surface d'exécution UI — liste clients (P2D-5), liste/fiche produit
(P2D-4), fiche détaillée commande + formulaire d'ÉDITION (`GetOrderDetailsUseCase`), et formulaire de **vente**
(`GetSaleFormReferenceDataQuery` + refonte du panier) — jusqu'à **allowlist repository = 0**, puis verrouiller le
garde-fou anti-entités-EF en propriété publique de ViewModel. Ne pas ouvrir P3 / SaaS / Organization / Store /
Subscription.
