# Rapport P2D-4 — Produits / Stock : lectures UI → Query Use Cases — VERDICT : **GO PARTIEL (local)**

> Extraction des **lectures** UI du module Produits / Stock vers des *query use cases* Application renvoyant des
> **DTO applicatifs plats** (jamais d'entité EF suivie). Aucun commit, aucun push (conforme à `ALLOW_COMMIT = false`
> / `ALLOW_PUSH = false`).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2D-4 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2d-query-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : **`p2d-query-cleanup`** ✓
- Working tree : **propre** ✓
- Présence de `e81f99b feat(P2D): migrate simple read modules to query use cases` ✓
- Présence de `37067cc docs(P2D): record partial query cleanup CI validation` ✓
- Précondition = **GO** (aucun déclencheur STOP).

## 3. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | ✅ vert (0 erreur ; 1 warning préexistant CS1998 `OrderFormViewModel`) |
| `dotnet test MMV.sln` | ✅ **512** (App 170 · Application 119 · Domain 223) |
| `dotnet list … --vulnerable --include-transitive` | ✅ 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | ✅ `false` |
| `MMV.Application` references | ✅ `MMV.Domain` seul |
| `MMV.Application` packages | ✅ `Microsoft.Extensions.DependencyInjection.Abstractions` seul |

Baseline = **GO**.

## 4. Inventaire Produits / Stock avant extraction

Périmètre scanné : `src/MMV.App/ViewModels` (Produits / Stock) + Views associées. 11 dépendances repository en
constructeur (allowlist P2D-GLOBAL), **exclusivement des lectures d'affichage** :

| ViewModel | Repo injecté | Méthode | Données consommées par l'écran | Entité Domain exposée publiquement | Query use case cible | DTO cible | Risque | Action |
|---|---|---|---|---|---|---|---|---|
| `StockMovementsListViewModel` | `IStockMovementRepository` | `GetAllAsync` | liste mouvements (date, produit.nom/réf, motif, type, qté) | `ObservableCollection<StockMovement>` | `IListStockMovementsUseCase` | `StockMovementListItemDto` (+`…ProductRefDto`) | faible | **migré** |
| `StockMovementsListViewModel` | `IProductRepository` | `GetAllAsync` | filtre « produit » (nom) | `ObservableCollection<Product>` | `IListProductsForPickerUseCase` | `ProductPickerItemDto` | faible | **migré** |
| `StockMovementFormViewModel` | `IProductRepository` | `GetAllAsync` | sélecteur ligne (réf, nom, fournisseur, description) | `ObservableCollection<Product>` | `IListProductsForPickerUseCase` | `ProductPickerItemDto` | faible | **migré** |
| `StockMovementsViewModel` | `IStockMovementRepository`, `IProductRepository` | — (pass-through) | transmis à liste + formulaire | non | (transmet les 2 query use cases) | — | faible | **migré** |
| `InventoryViewModel` | `IProductRepository` | `GetAllAsync` | inventaire (réf, nom, catégorie, stock théorique) | `ObservableCollection<Product>` | `IGetInventoryOverviewUseCase` | `InventoryProductItemDto` | faible | **migré** |
| `ProductFormViewModel` | `ISupplierRepository` | `GetAllAsync` | liste déroulante fournisseurs (nom) | `ObservableCollection<Supplier>` | `IListSuppliersUseCase` (réutilisé P2D-2) | `SupplierListItemDto` | faible | **migré** |
| `ProductsViewModel` | `ISupplierRepository` | — (pass-through) | transmis à `ProductFormViewModel` | non | `IListSuppliersUseCase` | `SupplierListItemDto` | faible | **migré** |
| `ProductsViewModel` | `IStockMovementRepository` | — (pass-through) | transmis à `StockMovementsViewModel` | non | (query use cases stock) | — | faible | **migré** |
| `ProductsViewModel` | `IProductRepository` | — (pass-through) | transmis à `ProductsListViewModel` | non | *(reliquat)* | — | **élevé** | **reporté** |
| `ProductsListViewModel` | `IProductRepository` | `GetAllAsync` | liste produit (graphe complet : réf/nom/cat/fournisseur/prix/stock **+** Verre/Lentille/Accessoire **+** OrderItems) circulant vers fiche & formulaire d'édition | `ObservableCollection<Product>` | *(reliquat)* | *(DTO riche à graphe)* | **élevé** | **reporté** |

## 5. Stratégie appliquée

**Déplacement iso-fonctionnel**, une lecture à la fois, compilation après chaque groupe. Pour chaque écran migré :
la lecture directe `I…Repository` est remplacée par un *query use case* Application qui **projette** l'entité vers un
**DTO plat** (`init`-only, sans navigation EF) ; recherche / filtre / tri / pagination **restent en présentation**
(iso-fonctionnel). **Parité de noms** DTO ⇄ liaisons XAML (résolution par réflexion) pour ne **toucher aucun**
`.axaml` ni code-behind.

**Décision de périmètre (GO PARTIEL).** Les **sélecteurs** produit/fournisseur, l'**inventaire** et la **liste des
mouvements** sont des projections plates auto-portées : migrées intégralement (**9 entrées d'allowlist retirées**). La
**liste produit** (`ProductsListViewModel`) et son porteur (`ProductsViewModel → IProductRepository`) sont **reportés**
(reliquat P2D-4 justifié) : l'entité `Product` y circule **avec son graphe complet** (détails Verre/Lentille/Accessoire
+ historique `OrderItems→Order`) à travers de nombreux événements typés `Product` vers la **fiche détaillée**
(`ProductDetailViewModel`) et le **formulaire d'édition** (`ProductFormViewModel(..., Product)`). Migrer ce graphe
imposerait un DTO riche imbriqué threadé dans écran/fiche/formulaire, **non validable en iso-fonctionnel sans exécution
UI de recette** (environnement non interactif, Avalonia non exécutable ici) — précisément la clause d'échappement §17/§19
(« ne pas forcer ; documenter précisément ; verdict = GO partiel »).

**Réutilisation.** Le sélecteur fournisseur du formulaire produit réutilise `IListSuppliersUseCase` (P2D-2), qui renvoie
déjà un DTO plat portant `SupplierId` + `Name` : aucun *query use case* fournisseur quasi-doublon n'a été créé.

## 6. Query use cases créés (3)

| Module (dossier) | Query use case | Sortie | Repository consommé |
|---|---|---|---|
| `Stock/ListStockMovements` | `IListStockMovementsUseCase` | `IReadOnlyList<StockMovementListItemDto>` | `IStockMovementRepository` |
| `Products/ListProductsForPicker` | `IListProductsForPickerUseCase` | `IReadOnlyList<ProductPickerItemDto>` | `IProductRepository` |
| `Products/GetInventoryOverview` | `IGetInventoryOverviewUseCase` | `IReadOnlyList<InventoryProductItemDto>` | `IProductRepository` |

Chacun : `…Query.cs` (objet d'entrée, garde « query nulle »), `…Dto.cs`, `I…UseCase.cs`, `…UseCase.cs`. Enregistrés en
**`Scoped`** dans `MMV.Application/DependencyInjection.cs` (même portée qu'`OpticDbContext` / repositories).
`MMV.Application` reste **pure** (référence `MMV.Domain` seul).

## 7. DTO applicatifs créés (4)

`StockMovementListItemDto`, `StockMovementProductRefDto` (référence produit imbriquée, préserve `Product.Name` /
`Product.Reference`), `ProductPickerItemDto`, `InventoryProductItemDto`. Tous : `sealed`, propriétés `init`-only, aucun
type Avalonia / CommunityToolkit, aucune navigation EF, uniquement les champs consommés par l'écran. Les enums Domain
(`StockMovementType`, `ProductCategoryEnum`) sont conservées comme valeurs métier stables (ce ne sont pas des entités).
Le formulaire produit réutilise `SupplierListItemDto` (P2D-2).

## 8. ViewModels modifiés (6)

- `StockMovementsListViewModel` → `IListStockMovementsUseCase` + `IListProductsForPickerUseCase` (retrait 2 repos ;
  collections & événement `ShowDetail` retypés DTO — événement sans abonné, retypage sûr).
- `StockMovementFormViewModel` (+ `ProductMovementLine`) → `IListProductsForPickerUseCase` (retrait `IProductRepository` ;
  ligne et recherche retypées `ProductPickerItemDto`, `p.Supplier.Name` → `p.SupplierName`).
- `StockMovementsViewModel` → transmet les 2 query use cases stock (retrait 2 repos).
- `InventoryViewModel` → `IGetInventoryOverviewUseCase` (retrait `IProductRepository` ; `InventoryItem.Product` retypé
  `InventoryProductItemDto`).
- `ProductFormViewModel` → `IListSuppliersUseCase` (retrait `ISupplierRepository` ; `Suppliers` / `SelectedSupplier`
  retypés `SupplierListItemDto`). Le paramètre `Product product` du constructeur d'édition **subsiste** (entité passée,
  non repository — hors garde-fou) : couplage reliquat au module liste non migré.
- `ProductsViewModel` → transmet `IListSuppliersUseCase` au formulaire et les 2 query use cases stock au sous-VM
  mouvements (retrait `ISupplierRepository` + `IStockMovementRepository`). **Conserve `IProductRepository`** (reliquat,
  pour `ProductsListViewModel`).

## 9. XAML / code-behind

**Aucun `.axaml` modifié. Aucun code-behind modifié.** Toutes les liaisons migrées se résolvent par réflexion sur des
DTO à **noms identiques** (y compris imbriqués : `Product.Name`, `Product.Reference`, `Product.Category`). Les casts
code-behind existants restent valides : `ProductsView.axaml.cs` (`is Product`) porte sur la **liste produit non migrée**
(inchangée) ; `InventoryView.axaml.cs` (`is InventoryItem`) porte sur la classe VM `InventoryItem` (inchangée, seul son
membre interne `.Product` a changé de type). Interdiction §10/§16 respectée.

## 10. Tests créés / modifiés

- **Créés — Application (vrai SQLite temporaire, jamais InMemory)** :
  `UseCases/Stock/ListStockMovementsUseCaseTests` (projection DTO, référence produit imbriquée, tri décroissant par
  date, cas vide, query nulle, constructeur null) **+4** ; `UseCases/Products/ProductQueryUseCasesTests`
  (`ListProductsForPicker` + `GetInventoryOverview` : projection, tri par nom, nom fournisseur, stock/catégorie, cas
  vide, query nulle, constructeurs null) **+6**. Total **+10** (119 → 129).
- **Créé — App** : `ViewModels/P2D4ProductStockViewModelTests` (délégation aux query use cases, remplissage d'état,
  conservation des filtres recherche, constructeurs null) **+7** (170 → 177).
- **Adapté — App** : `StockMovementFormViewModelTests` (`IProductRepository` mock → `IListProductsForPickerUseCase` ;
  `ProductMovementLine`/`Product` → `ProductPickerItemDto`).

Total : **529** tests (App 177 · Application 129 · Domain 223) — tous verts.

## 11. Réduction exacte d'allowlist (`AppUiPersistenceGuardrailTests`)

| Allowlist | Avant (P2D-GLOBAL) | Après (P2D-4) | Δ |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 34 | **25** | **−9** |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 0 | 0 | inchangé |
| `AllowedPublicPersistenceProperties` | 0 | 0 | inchangé |
| `AllowedCodeBehindPersistenceFiles` | 1 (`App.axaml.cs`) | 1 (`App.axaml.cs`) | inchangé |

**9 entrées retirées** : `InventoryViewModel→IProductRepository`, `ProductFormViewModel→ISupplierRepository`,
`ProductsViewModel→IStockMovementRepository`, `ProductsViewModel→ISupplierRepository`,
`StockMovementFormViewModel→IProductRepository`, `StockMovementsListViewModel→IProductRepository`,
`StockMovementsListViewModel→IStockMovementRepository`, `StockMovementsViewModel→IProductRepository`,
`StockMovementsViewModel→IStockMovementRepository`. Aucune entrée ajoutée ; la règle « l'allowlist ne fait que
diminuer » est respectée (assertion anti-allowlist-obsolète verte).

## 12. Fichiers modifiés / créés

- **Créés (source, 13 fichiers)** : `UseCases/Stock/ListStockMovements/**` (5, `StockMovementProductRefDto` porte le
  total Stock à 5), `UseCases/Products/ListProductsForPicker/**` (4), `UseCases/Products/GetInventoryOverview/**` (4).
- **Modifiés (source, 7)** : `MMV.Application/DependencyInjection.cs`, `InventoryViewModel`, `ProductFormViewModel`,
  `ProductsViewModel`, `StockMovementFormViewModel`, `StockMovementsListViewModel`, `StockMovementsViewModel`.
- **Créés (tests, 3)** : `ListStockMovementsUseCaseTests`, `ProductQueryUseCasesTests`, `P2D4ProductStockViewModelTests`.
- **Modifiés (tests, 2)** : `AppUiPersistenceGuardrailTests.cs`, `StockMovementFormViewModelTests.cs`.
- **Docs** : ce rapport ; `docs/architecture/P2D-read-application-roadmap.md` (note d'état P2D-4).

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check` (propre), `dotnet restore`,
`dotnet build --no-restore -c Debug`, `dotnet test --no-build -c Debug`, `dotnet list … --vulnerable --include-transitive`,
`dotnet tool restore`, `ef migrations has-pending-model-changes`, `dotnet list …Application… reference`,
`dotnet list …Application… package`.

## 14. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| Build | vert | ✅ 0 erreur |
| Tests | > 512 | ✅ **529** |
| Vulnérabilités | 0 | ✅ 0 |
| `has-pending-model-changes` | false | ✅ false |
| Migration créée | aucune | ✅ aucune |
| Modèle EF modifié | aucun | ✅ aucun |
| `.axaml` modifié | aucun | ✅ aucun |
| Code-behind modifié | aucun | ✅ aucun |
| `MMV.Application` référence | `MMV.Domain` seul | ✅ |
| `MMV.Application` package | `DI.Abstractions` seul | ✅ |
| Allowlist repository | ↓ | ✅ **25** (−9) |
| Allowlist UoW / propriétés publiques persistance | 0 | ✅ 0 |
| Code-behind persistant | `App.axaml.cs` seul | ✅ |

## 15. Migrations créées ou non

**Aucune migration créée.** Aucune entité Domain / `DbContext` / migration / configuration EF touchée.
`has-pending-model-changes = false`. Stock atomique (P2A/P2B/P2C) et écritures (Create/Update/Delete produit,
`CreateStockMovement`) **inchangés**. Pin SQLite (P2C-SEC-1) intact.

## 16. Risques résiduels

1. **Événement `StockMovementsListViewModel.ShowDetailRequested`** retypé DTO : sans abonné ni liaison XAML (détail
   mouvement non câblé) — retypage sans effet observable.
2. **Réutilisation de `SupplierListItemDto`** pour le sélecteur fournisseur du formulaire produit : léger sur-ensemble de
   champs (le formulaire n'affiche que `Name` et lit `SupplierId`) — assumé DRY, aucune entité EF exposée.
3. **Reliquat `Product` non migré** (voir §17) : la fiche produit et le formulaire d'édition continuent de manipuler
   l'entité EF `Product` (non suivie, `AsNoTracking`) via des événements — invariant P2C préservé (aucune écriture
   directe, aucun `IUnitOfWork`, aucune propriété publique de persistance).

## 17. Repositories restants sur Produits / Stock et justification

**2 dépendances** subsistent (reliquat P2D-4 justifié, verrouillées par l'allowlist qui ne peut que diminuer) :

- `ProductsListViewModel → IProductRepository` : la liste produit charge le **graphe complet** de `Product`
  (`GetAllAsync` avec `Include` Verre/Lentille/Accessoire + `OrderItems→Order`). Ce graphe circule, via des événements
  typés `Product`, vers `ProductDetailViewModel` (fiche + historique de commandes) et `ProductFormViewModel(..., Product)`
  (édition avec détails de catégorie). Une migration iso-fonctionnelle exige un DTO riche imbriqué threadé dans
  écran/fiche/formulaire, **non validable sans exécution UI de recette**.
- `ProductsViewModel → IProductRepository` : porteur de la construction de `ProductsListViewModel` ci-dessus.

Cible de reprise : `GetProductDetailsQuery` + `ProductDetailsDto` (à graphe : détails + historique), avec recette UI
Avalonia, à traiter en clôture P2D (P2D-7) une fois une surface d'exécution disponible.

## 18. Verdict

**GO PARTIEL (local).** Les lectures **Stock, Inventaire et sélecteurs produit/fournisseur** du module Produits / Stock
sont sorties vers des *query use cases* Application renvoyant des **DTO plats** ; l'allowlist repository passe de
**34 à 25** (−9), sans jamais grossir. Build vert, **529** tests verts (> 512), 0 vulnérabilité,
`has-pending-model-changes = false`, aucune migration, aucun modèle EF / `.axaml` / code-behind / Domain / Infrastructure
touché, `MMV.Application` toujours pure, `IUnitOfWork` UI = 0, aucune écriture directe UI. La **liste produit à graphe
d'entités** (fiche + formulaire d'édition) est **documentée en reliquat justifié** (allowlist minimale : 2 entrées),
conformément aux clauses §17/§19.

## 19. Validation CI distante (commit applicatif)

Commit `6592d46` poussé sur `p2d-query-cleanup` ; workflow **CI** déclenché sur `push`, **terminé avec succès**.

| Élément | Valeur |
|---|---|
| Run CI | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/28794824116 |
| Identifiant du run | `28794824116` |
| Commit testé | `6592d461230f4293160f8fcda76c67e6c30845ef` |
| Branche testée | `p2d-query-cleanup` |
| Event | `push` |
| Restore | ✅ success |
| Build | ✅ success |
| Test | ✅ success — **529** (App 177 · Application 129 · Domain 223) |
| Audit NuGet | ✅ success — « Aucune vulnerabilite High/Critical detectee. » |
| Restore .NET tools | ✅ success |
| Check EF Core pending model changes | ✅ success — « No changes have been made to the model since the last migration. » |
| Statut final du workflow | ✅ **completed / success** (`3m14s`) |

## 20. Prochaine étape candidate

**P2D-5 — Clients / Ordonnances** : `SearchCustomersQuery`, `GetCustomerDetailsQuery`,
`ListPrescriptionsByCustomerQuery`, `GetCustomerPurchaseHistoryQuery` + DTO dédiés. Le reliquat **liste produit /
fiche** (P2D-4) reste à solder en clôture P2D (P2D-7) avec recette UI. Ne pas ouvrir P3 / SaaS / Organization / Store /
Subscription.
