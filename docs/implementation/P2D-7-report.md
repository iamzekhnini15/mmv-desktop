# Rapport P2D-7 — Clôture P2D : solder les 11 repositories UI restants — VERDICT : **GO COMPLET (local)**

> Élimination des **11 dernières** dépendances `I*Repository` des constructeurs de ViewModels de `src/MMV.App`.
> Toutes les **lectures d'affichage** des modules **Ventes**, **Commandes**, **Clients** et **Produits** passent
> désormais par des *query use cases* Application renvoyant des **DTO applicatifs plats/composites** (jamais
> d'entité EF suivie). L'allowlist de dépendances repository des ViewModels tombe à **0**. Aucun commit, aucun push
> (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`). Migration **iso-fonctionnelle** ; **aucun** fichier
> `.axaml` modifié.

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2D-7 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2d-query-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : **`p2d-query-cleanup`** ✓
- Dernier commit intégré : `fc7e461 docs(P2D-6): record order query cleanup CI validation` ✓
- P2D-6 = **GO PARTIEL DÉFINITIF COMPLET** — CI verte confirmée (run `28822497800`, conclusion `success`).
- Aucune modification apportée à `src/MMV.Domain/**`, `src/MMV.Infrastructure/**`, `migrations/**`, `.github/**`.

## 3. Objectif et cibles finales

| Garde-fou (`AppUiPersistenceGuardrailTests`) | Avant P2D-7 | Après P2D-7 | Cible |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 11 | **0** | 0 ✅ |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 0 | **0** | 0 ✅ |
| `AllowedPublicPersistenceProperties` | 0 | **0** | 0 ✅ |
| `AllowedCodeBehindPersistenceFiles` | 1 (`App.axaml.cs`) | **1** (`App.axaml.cs`) | 1 ✅ |

### Les 11 dépendances soldées

| # | Dépendance retirée | Sous-étape | Query use case cible |
|---|---|---|---|
| 1 | `SaleFormViewModel -> IProductRepository` | 7A | `IGetSaleFormReferenceDataUseCase` |
| 2 | `SaleFormViewModel -> IPrescriptionRepository` | 7A | `IGetSaleFormReferenceDataUseCase` |
| 3 | `CustomerDetailViewModel -> IProductRepository` | 7A/7C | `IGetSaleFormReferenceDataUseCase` (pass-through) |
| 4 | `CustomerDetailViewModel -> IPrescriptionRepository` | 7A/7C | `IGetSaleFormReferenceDataUseCase` (pass-through) |
| 5 | `CustomersViewModel -> IProductRepository` | 7A/7C | `IGetSaleFormReferenceDataUseCase` (pass-through) |
| 6 | `CustomersViewModel -> IPrescriptionRepository` | 7A/7C | `IGetSaleFormReferenceDataUseCase` (pass-through) |
| 7 | `CustomersViewModel -> ICustomerRepository` | 7C | `IListCustomersUseCase` |
| 8 | `CustomersListViewModel -> ICustomerRepository` | 7C | `IListCustomersUseCase` |
| 9 | `OrdersViewModel -> IOrderRepository` | 7B | `IGetOrderDetailsUseCase` |
| 10 | `ProductsListViewModel -> IProductRepository` | 7D | `IListProductsUseCase` |
| 11 | `ProductsViewModel -> IProductRepository` | 7D | `IListProductsUseCase` |

## 4. Query use cases créés (couche Application)

Tous enregistrés `Scoped` dans `DependencyInjection.AddApplication`, renvoyant des DTO **`init`-only**, sans
navigation EF, sans `IQueryable`, sans `DbContext`, sans type Avalonia/CommunityToolkit, sans entité Domain exposée
(les enums Domain restent autorisés comme valeurs métier stables).

### 4.1 `GetSaleFormReferenceDataUseCase` (Ventes — P2D-7A)
- **Query** : `GetSaleFormReferenceDataQuery { CustomerId }`.
- **Résultat** : `SaleFormReferenceDataDto { ActivePrescription: PrescriptionListItemDto?, Products: IReadOnlyList<SaleProductPickerItemDto> }`.
- **Sous-DTO** : `SaleProductPickerItemDto` (+ `SaleGlassDetailDto` pour la compatibilité verre).
- Remplace les deux lectures de `SaleFormViewModel.InitializeForCustomerAsync`
  (`IPrescriptionRepository.GetLatestByCustomerIdAsync` + `IProductRepository.GetAllAsync`).
  Réutilise `PrescriptionListItemDto` (P2D-5).

### 4.2 `GetOrderDetailsUseCase` (Commandes — P2D-7B)
- **Query** : `GetOrderDetailsQuery { OrderId }` → `OrderDetailsDto?` (null si commande introuvable).
- **Composite** : `OrderDetailsSaleDto` → `OrderDetailsCustomerDto` ; `OrderDetailsItemDto` → `OrderDetailsProductDto`.
- Remplace `OrdersViewModel.OnViewOrderDetail` (`IOrderRepository.GetWithItemsAsync`).
- Expose `public static OrderDetailsDto MapToDto(Order)` réutilisé par `OrderDetailViewModel` pour ré-aligner l'état
  affiché après un `AdvanceOrderStatusResult` / `SettleOrderBalanceResult` (use cases d'écriture **inchangés** qui
  renvoient encore une entité `Order`).

### 4.3 `ListCustomersUseCase` (Clients — P2D-7C)
- **Query** : `ListCustomersQuery` (vide) → `IReadOnlyList<CustomerListItemDto>`.
- `CustomerListItemDto` porte tous les champs scalaires de `Customer` (liste, fiche détail, formulaire d'édition).
- Remplace `CustomersListViewModel.LoadCustomersAsync` (`ICustomerRepository.GetAllAsync`).

### 4.4 `ListProductsUseCase` (Produits — P2D-7D)
- **Query** : `ListProductsQuery` (vide) → `IReadOnlyList<ProductListItemDto>`.
- **Sous-DTO** : `ProductSupplierRefDto`, `ProductGlassDetailsDto`, `ProductLensDetailsDto`,
  `ProductAccessoryDetailsDto`, et l'historique de commandes
  `ProductOrderHistoryItemDto → ProductOrderHistoryOrderDto → ProductOrderHistorySaleDto → ProductOrderHistoryCustomerDto`.
- Remplace `ProductsListViewModel.LoadProductsAsync` (`IProductRepository.GetAllAsync`, graphe déjà inclus côté
  `ProductRepository`), alimente la liste, la fiche détaillée (`ProductDetailViewModel` + historique) et le
  formulaire d'édition (`ProductFormViewModel`).

## 5. ViewModels migrés (couche UI)

| ViewModel | Entité EF → DTO |
|---|---|
| `SaleFormViewModel` | catalogue `Product` → `SaleProductPickerItemDto` ; ordonnance → `PrescriptionListItemDto?` |
| `CustomerDetailViewModel`, `CustomersViewModel` | pass-through de la référence de vente via use case |
| `CustomersViewModel`, `CustomersListViewModel`, `CustomerFormViewModel`, `CustomerDetailViewModel`, `CustomerInfoViewModel` | `Customer` → `CustomerListItemDto` |
| `OrdersViewModel`, `OrderDetailViewModel`, `OrderFormViewModel`, `FabricationSheetViewModel` | `Order`/`OrderItem` → `OrderDetailsDto`/`OrderDetailsItemDto` |
| `ProductsViewModel`, `ProductsListViewModel`, `ProductDetailViewModel`, `ProductFormViewModel` | `Product`/`OrderItem` → `ProductListItemDto`/`ProductOrderHistoryItemDto` |

Code-behind adaptés (cast de `DataContext`) : `CustomersView.axaml.cs`, `ProductsView.axaml.cs`
(`is Customer` → `is CustomerListItemDto`, `is Product` → `is ProductListItemDto`).

## 6. Aucune modification `.axaml` — parité de binding

Objectif tenu : **0** fichier `.axaml` modifié. Les bindings résolvent par **parité de nom de propriété** sur les DTO.
Point sensible traité : `ProductOrderHistoryView` lie `Order.Sale.Customer.FirstName/LastName` (binding réflexif).
Le chemin a été **conservé** en structurant le DTO d'historique avec un maillon
`ProductOrderHistoryOrderDto.Sale` (`ProductOrderHistorySaleDto`) porteur du `Customer`, plutôt qu'un `Customer`
aplati — évitant toute retouche XAML. Les bindings compilés (`x:DataType` = ViewModel) sont validés par la
compilation XAML Avalonia (build `MMV.App` vert).

> Note iso-fonctionnelle : `ProductRepository.GetAllAsync` n'inclut pas `OrderItems→Order→Sale→Customer` ; le nom du
> client de l'historique produit était donc déjà vide dans l'écran d'origine — le DTO préserve ce comportement
> (aucun `Include` ajouté, `src/MMV.Infrastructure/**` non modifié).

## 7. Tests créés (couche Application — vrai SQLite, jamais InMemory)

`SqliteConnection` sur base fichier temporaire + `EnsureCreated`, repositories réels. **21 tests** ajoutés :

| Fichier | Cas couverts |
|---|---|
| `ListCustomersUseCaseTests` | projection champs plats, non-entité, vide, query nulle, ctor nul |
| `ListProductsUseCaseTests` | projection scalaires + fournisseur + détails Verre/Lentille/Accessoire, **tri décroissant de l'historique**, non-entité, vide, query nulle, ctor nul |
| `GetSaleFormReferenceDataUseCaseTests` | **ordonnance la plus récente**, catalogue projeté (détails verre), absence d'ordonnance, non-entité, query nulle, ctor nuls |
| `GetOrderDetailsUseCaseTests` | projection composite (vente + client + articles + produit), **commande introuvable → null**, non-entité, query nulle, ctor nul |

Tests de délégation UI existants adaptés aux DTO : `CustomerFormViewModelTests`, `CustomersListViewModelTests`,
`P2D5CustomerReadViewModelTests`, `SaleFormViewModelTransactionTests`, `OrderDetailViewModelAdvanceDelegationTests`,
`OrderDetailViewModelEncashDelegationTests`, `OrderFormViewModelUpdateDelegationTests`,
`OrdersViewModelDeleteDelegationTests`.

## 8. Garde-fou anti-entité Domain (optionnel) — **NO-GO justifié**

Le garde-fou optionnel « aucune propriété publique de ViewModel n'expose une entité Domain » n'a **pas** été ajouté.
`SaleFormViewModel` expose encore `ObservableCollection<OrderItem>` : le **panier de vente** est composé d'entités
`OrderItem` qui constituent l'**entrée du chemin d'écriture** `RegisterSaleUseCase` (hors périmètre « lectures » de
P2D). Un garde-fou global échouerait donc sur une composition d'écriture **légitime et iso-fonctionnelle**.
Décision : garde-fou reporté à la future migration DTO du panier de vente (chemin d'écriture), hors P2D. Les **4**
garde-fous de lecture visés sont, eux, tous atteints (repository = 0, UoW = 0, propriétés publiques = 0,
code-behind = 1).

## 9. Contrôles finaux

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | ✅ **0 erreur, 0 warning** |
| `dotnet test MMV.sln` | ✅ **585** (App **192** · Application **170** · Domain **223**) — dont garde-fous UI verts |
| `AppUiPersistenceGuardrailTests` | ✅ 5/5 (allowlists : repo **0**, UoW **0**, props **0**, code-behind **1**) |
| `dotnet list … --vulnerable --include-transitive` | ✅ 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | ✅ « No changes … since the last migration » |
| Pureté couches (architecture tests) | ✅ `MMV.Application` ⇒ Domain + DI abstractions seulement |
| Fichiers `.axaml` modifiés | ✅ **0** |
| `src/MMV.Domain/**`, `src/MMV.Infrastructure/**`, `migrations/**`, `.github/**` | ✅ intacts |

## 10. Verdict

**GO COMPLET (local).** P2D-7 solde les 11 dernières dépendances repository des ViewModels : **P2D est clôturée**
côté lectures d'affichage — plus aucun ViewModel de `MMV.App` ne dépend d'un `I*Repository` du Domain ni d'
`IUnitOfWork` par constructeur. Migration iso-fonctionnelle, sans retouche XAML, sans nouvelle migration/table/entité,
sans règle métier. **Aucun commit, aucun push** (conforme aux paramètres). Reliquat connu hors périmètre P2D : le
panier d'écriture `SaleFormViewModel.OrderItems` (entités `OrderItem` alimentant `RegisterSaleUseCase`), à traiter
lors d'une future migration DTO du chemin d'écriture.
