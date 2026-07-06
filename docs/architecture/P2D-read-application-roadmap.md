# P2D — Feuille de route : sortir les **lectures** de l'UI vers des *query use cases*

> Document de cadrage produit à l'issue de **P2C-GLOBAL** (sortie de toutes les **écritures** UI vers
> la couche Application). Aucune étape ci-dessous n'est implémentée ici : ce document fixe l'objectif,
> les règles et l'ordre des étapes P2D. **Le dépôt réel prime toujours sur ce document.**
>
> **Hors périmètre absolu (inchangé) :** aucune introduction de `Payment`, `Invoice`, `Quote`, `Money`,
> TVA, règles Belgique/Maroc, SaaS, multi-`Store`, `Organization`, `Subscription`, `Plan`. P2D est un
> **nettoyage architectural** (déplacement de lecture existante), pas une évolution fonctionnelle.

## 1. État final P2C (point de départ P2D)

À l'issue de P2C-GLOBAL :

- **Écritures UI** : 100 % extraites vers des use cases *command* de `MMV.Application` (client, ordonnance,
  produit, fournisseur, utilisateur, notification, mouvement de stock, avancement de commande).
- **`IUnitOfWork` dans l'UI** : **0** (allowlist vidée). Aucun VM ne détient de frontière transactionnelle.
- **Propriétés publiques de persistance** : **0**.
- **Code-behind avec persistance** : uniquement `App.axaml.cs` (composition root).
- **`MMV.Application`** : pure (référence `MMV.Domain` seul ; package `DI.Abstractions` seul).
- **Dette restante = lectures.** ~40 dépendances `I…Repository` subsistent en **constructeur de ViewModel**,
  **exclusivement pour des lectures d'affichage**. Elles renvoient encore des **entités EF suivies**
  directement à l'UI — c'est précisément ce que P2D doit supprimer.

## 2. Query use cases déjà créés

P2C a créé uniquement des use cases *command* (écritures) ; P2D a introduit la **première** famille de
*query use cases* de la solution. Créés à ce jour :

- **P2D-GLOBAL (partiel)** : `ListUsersQuery`, `ListSuppliersQuery`, `GetSupplierWithProductsQuery`,
  `ListNotificationsQuery`, `CountUnreadNotificationsQuery`.
- **P2D-4** : `ListStockMovementsQuery`, `ListProductsForPickerQuery`, `GetInventoryOverviewQuery`
  (le sélecteur fournisseur du formulaire produit réutilise `ListSuppliersQuery`).
- **P2D-5** : `GetCustomerPurchaseHistoryQuery`, `ListPrescriptionsByCustomerQuery`
  (reliquat : liste clients reportée en P2D-7 ; lectures de référence `SaleFormViewModel` reportées en P2D-6).

## 3. Inventaire des lectures restantes à extraire (cible P2D)

| Module | ViewModels (lecture) | Repositories lus | Query use cases cibles (indicatifs) |
|---|---|---|---|
| Clients | `CustomersListViewModel`, `CustomerDetailViewModel`, `CustomerInfoViewModel`, `CustomerPurchaseHistoryViewModel`, `CustomerPrescriptionsViewModel` | `ICustomerRepository`, `IPrescriptionRepository`, `IProductRepository`, `ISaleRepository` | `SearchCustomersQuery`, `GetCustomerDetailsQuery`, `ListPrescriptionsByCustomerQuery`, `GetCustomerPurchaseHistoryQuery` |
| Produits / Stock | `ProductsListViewModel`, `ProductFormViewModel`, `ProductsViewModel`, `InventoryViewModel`, `StockMovementsListViewModel`, `StockMovementsViewModel`, `StockMovementFormViewModel` | `IProductRepository`, `ISupplierRepository`, `IStockMovementRepository` | `ListProductsQuery`, `GetProductDetailsQuery`, `ListSuppliersForPickerQuery`, `GetInventoryOverviewQuery`, `ListStockMovementsQuery` |
| Fournisseurs | `SuppliersListViewModel`, `SuppliersViewModel` | `ISupplierRepository` | `ListSuppliersQuery`, `GetSupplierWithProductsQuery` |
| Utilisateurs | `UsersListViewModel`, `UsersViewModel` | `IUserRepository` | `ListUsersQuery` |
| Notifications | `NotificationsListViewModel`, `NotificationsViewModel` | `INotificationRepository` | `ListNotificationsQuery`, `CountUnreadNotificationsQuery` |
| Commandes | `OrdersListViewModel`, `OrdersViewModel`, `OrderKanbanViewModel`, `OrderFormViewModel` | `IOrderRepository`, `ICustomerRepository`, `IPrescriptionRepository`, `IProductRepository` | `ListOrdersQuery`, `GetOrdersKanbanQuery`, `GetOrderFormReferenceDataQuery` |
| Ventes | `SaleFormViewModel` | `IProductRepository`, `IPrescriptionRepository` | `GetSaleFormReferenceDataQuery` |

> La liste des noms est **indicative** ; le découpage réel se fait au vu du dépôt, en une étape par module.

## 4. Règles de DTO applicatifs

1. Un *query use case* renvoie **uniquement des DTO définis dans `MMV.Application`** (records/classes plates),
   jamais une entité `MMV.Domain.Entities.*`.
2. Les DTO sont **en lecture seule** (`init`-only), sans navigation EF, sans `virtual`, sans collection suivie.
3. Le DTO porte **exactement** les champs consommés par l'écran (pas de sur-exposition « au cas où »).
4. Le mapping entité → DTO vit **dans le use case** (ou un mapper `internal` de la couche Application), jamais dans l'UI.
5. Pas de logique métier dans le DTO : formatage et libellés d'affichage restent côté ViewModel (présentation).
6. Les *query use cases* sont enregistrés en `Scoped` (même portée que le `DbContext`), comme les *command*.

## 5. Règles anti-retour d'entités EF vers l'UI

- ❌ Aucun `I…Repository` en **constructeur de ViewModel** (cible P2D = **0**).
- ❌ Aucune méthode de VM ne manipule une entité `MMV.Domain.Entities.*` **suivie** issue d'un repository.
- ❌ Aucune projection EF (`Include`, `AsNoTracking`, `IQueryable`) exposée à l'UI.
- ✅ L'UI ne dépend que d'`I…Query`/`I…UseCase` de `MMV.Application` et manipule des **DTO applicatifs**.
- ✅ Lecture de référence pour formulaires (listes déroulantes) = *query use case* dédié renvoyant des DTO.
- Le garde-fou `AppUiPersistenceGuardrailTests` reste l'invariant : l'allowlist repository **diminue** à
  chaque étape P2D et doit atteindre **0**. Un test complémentaire pourra interdire tout type
  `MMV.Domain.Entities.*` en **propriété publique** de ViewModel (retour d'entité EF vers la vue).

## 6. Plan P2D par étapes

> Une étape = un module = un commit + un rapport + une validation CI distante verte (protocole P2B/P2C).
> Ordre : du plus simple (peu de lectures, faible couplage) au plus composite.

| Étape | Périmètre | Query use cases (indicatifs) |
|---|---|---|
| **P2D-1** | *Query use cases* : socle + conventions + 1er module pilote (**Utilisateurs**) | `ListUsersQuery` |
| **P2D-2** | **Fournisseurs** | `ListSuppliersQuery`, `GetSupplierWithProductsQuery` |
| **P2D-3** | **Notifications** | `ListNotificationsQuery`, `CountUnreadNotificationsQuery` |
| **P2D-4** | **Produits / Stock** — *GO PARTIEL (cf. `docs/implementation/P2D-4-report.md`)* : Stock/Inventaire/sélecteurs migrés (`ListStockMovementsQuery`, `ListProductsForPickerQuery`, `GetInventoryOverviewQuery` ; sélecteur fournisseur = `ListSuppliersQuery` réutilisé). Reliquat : liste produit à graphe (`ProductsListViewModel`/`ProductsViewModel→IProductRepository`) + `GetProductDetailsQuery`, à solder en P2D-7 avec recette UI. | `ListProductsQuery`, `GetProductDetailsQuery`, `ListSuppliersForPickerQuery`, `GetInventoryOverviewQuery`, `ListStockMovementsQuery` |
| **P2D-5** | **Clients / Ordonnances** — *GO PARTIEL (cf. `docs/implementation/P2D-5-report.md`)* : historique d'achats (`GetCustomerPurchaseHistoryQuery` — réutilisé par l'onglet Infos) et liste des ordonnances d'un client (`ListPrescriptionsByCustomerQuery`) migrés. Reliquat : liste clients (`CustomersListViewModel`/`CustomersViewModel→ICustomerRepository`, entité `Customer` threadée vers le formulaire d'ÉDITION) à solder en P2D-7 ; lectures de référence `SaleFormViewModel` (`IProductRepository`/`IPrescriptionRepository`) reportées en P2D-6. | `GetCustomerPurchaseHistoryQuery`, `ListPrescriptionsByCustomerQuery` |
| **P2D-6** | **Commandes / Ventes** | `ListOrdersQuery`, `GetOrdersKanbanQuery`, `GetOrderFormReferenceDataQuery`, `GetSaleFormReferenceDataQuery` |
| **P2D-7** | **Bilan** : allowlist repository = 0, garde-fou anti-entités-EF verrouillé, dette de lecture soldée | rapport de clôture P2D |

## 7. Critères d'entrée P2D

- P2C-GLOBAL **GO** (local) : écritures UI extraites, `IUnitOfWork` UI = 0, build/tests verts, 0 vulnérabilité,
  `has-pending-model-changes = false`, `MMV.Application` pure.
- **CI distante P2C-GLOBAL verte** (à confirmer — cf. réserve du rapport P2C-GLOBAL §3/§16).
- Aucune migration en attente ; pin SQLite (P2C-SEC-1) intact.

## 8. Critères de sortie P2D

- **0** dépendance `I…Repository` en constructeur de ViewModel (allowlist repository vidée).
- Aucune entité EF suivie ne franchit la frontière UI ; l'UI ne consomme que des **DTO applicatifs**.
- *Query use cases* couverts par des tests (données attendues, filtre/recherche/tri si existant, résultat
  vide, requête nulle si applicable, constructeur null) sur **vrai SQLite** (jamais EF InMemory).
- ViewModels de lecture testés (délégation au *query use case*, mapping DTO → état d'écran).
- Build vert, tests verts (> total P2C), 0 vulnérabilité, `has-pending-model-changes = false`,
  aucune migration, `MMV.Application` toujours pure.
- Garde-fou d'architecture renforcé (repository = 0 ; interdiction d'entités EF en propriété publique de VM).

## 9. Risques

1. **Requêtes de détail composites** (fiche client, Kanban) agrègent plusieurs entités : risque de
   sur-fetch ou de N+1. Mitigation : DTO ciblés + projections `Select` côté repository/Infrastructure,
   sans exposer d'`IQueryable` à l'UI.
2. **Régression d'affichage** lors du passage entité → DTO (champs oubliés, formats). Mitigation :
   déplacement iso-fonctionnel, tests de ViewModel, revue écran par écran.
3. **Pagination / recherche / tri** aujourd'hui faits **en mémoire** dans les VM (ex. `ProductsListViewModel`,
   `UsersListViewModel`) : décider explicitement s'ils restent en présentation (DTO « liste complète ») ou
   descendent dans le *query use case*. Choix documenté par module ; par défaut, **iso-fonctionnel** (rester
   en présentation) pour limiter le risque.
4. **Volume** : ~40 lectures sur 6 modules. Mitigation : découpage strict une étape = un module, chaque étape
   verte avant la suivante (comme P2B/P2C).
5. **CI distante** : maintenir la parité local/CI ; ne pas ouvrir P2D tant que la CI P2C-GLOBAL n'est pas verte.
