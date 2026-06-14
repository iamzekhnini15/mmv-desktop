# P2C — Feuille de route : retrait de la persistance directe de l'UI

> Document de cadrage produit en **P2B-2K** (audit de sortie P2B). Aucune phase ci-dessous
> n'est implémentée ici. Ce document fixe l'objectif, les règles et l'ordre des phases P2C.
> Le dépôt réel prime toujours sur ce document.

## 1. Objectif P2C

À l'issue de P2B, la couche **Application** existe et les principaux flux **transactionnels**
(vente, commandes fournisseur : création / avancement de statut / encaissement de solde /
suppression / modification, mouvement de stock manuel) ont été extraits vers des use cases.

Il reste néanmoins, dans `src/MMV.App` (ViewModels **et** code-behind `*.axaml.cs`), de la
**persistance directe** : appels à `ICustomerRepository`, `IProductRepository`,
`IUnitOfWork.SaveChangesAsync()`, etc., pour les flux CRUD non transactionnels (clients,
produits, ordonnances, fournisseurs, utilisateurs, notifications) et pour quelques lectures.

**But de P2C : l'UI ne doit plus contenir de logique de persistance directe.** Toute écriture
(et, à terme, toute lecture orchestrée) passe par un use case de la couche Application.

### Règle cible

```
UI (ViewModels + code-behind)  ──►  MMV.Application (use cases)  ──►  MMV.Domain (ports)
                                                                  ──►  MMV.Infrastructure (EF/SQLite)
```

L'UI **dépend des interfaces de use cases** (`I…UseCase`) — jamais des repositories, ni de
`IUnitOfWork`, ni de `DbContext`, ni d'EF Core.

## 2. Règles autorisées dans l'UI (cible P2C)

- Injection et appel d'interfaces de use cases (`I…UseCase`) de `MMV.Application`.
- Construction de `Command` et lecture de `Result` (DTO applicatifs).
- Services purement UI : `INavigationService`, `IDialogService`, `IThemeService`,
  `ISessionService`, `IPermissionService`.
- Mapping ViewModel ↔ DTO, validation de surface (champ requis, format), formatage d'affichage.
- Code-behind : abonnement aux événements UI, navigation, ouverture de dialogue, binding.
- **Lecture en lecture seule pour affichage** : tolérée temporairement via un port de lecture,
  mais cible = requêtes applicatives (query use cases) — voir §6, dette ouverte.

## 3. Règles interdites dans l'UI (cible P2C)

- ❌ Injecter ou appeler un `I…Repository` (`ICustomerRepository`, `IProductRepository`, …).
- ❌ Injecter ou appeler `IUnitOfWork` / `SaveChangesAsync()`.
- ❌ Référencer `DbContext` / `OpticDbContext` / EF Core / `Microsoft.Data.Sqlite`.
- ❌ Exposer publiquement un repository ou un `IUnitOfWork` depuis un ViewModel
  (cf. `CustomersListViewModel.Repository` / `.UnitOfWork`, consommés par du code-behind).
- ❌ Persister ou orchestrer une transaction depuis un fichier `*.axaml.cs`.
- ❌ `MMV.App` ne doit, à terme, plus référencer `MMV.Infrastructure` autrement que comme
  **composition root** isolé (objectif P2C-8 ; voir dette ouverte §6).

## 4. Ordre des phases P2C

> Ordre conçu pour livrer d'abord les **garde-fous**, puis migrer les flux du plus à risque
> au plus simple, et finir par la composition root. Chaque phase = un commit + un rapport +
> une validation CI distante verte, selon le même protocole que P2B.

| Phase | Périmètre | Cibles principales |
|-------|-----------|--------------------|
| **P2C-0** | Finaliser / merger P2B vers `main` si GO de sortie | branche `p2b-architecture` → `main` |
| **P2C-1** | **Tests garde-fous** UI sans persistance (réflexion sur constructeurs + dépendances assembly) | nouveaux tests d'architecture App |
| **P2C-2** | `OrderKanbanViewModel` (flux commandes restant en lecture/déplacement) | `OrderKanbanViewModel`, `OrdersListViewModel` |
| **P2C-3** | **Clients** | `CustomersListViewModel`, `CustomerFormViewModel`, `CustomerFormView.axaml.cs`, `CustomersView.axaml.cs`, `CustomerPrescriptionsViewModel`, `CustomerDetailViewModel` |
| **P2C-4** | **Ordonnances** | `PrescriptionFormViewModel`, `PrescriptionDetailViewModel`, `CustomerPrescriptionsViewModel` (reliquat) |
| **P2C-5** | **Produits** | `ProductFormViewModel`, `ProductsListViewModel` |
| **P2C-6** | **Inventaire / stock restant** | `InventoryViewModel`, `StockMovementsListViewModel` |
| **P2C-7** | **Fournisseurs / Utilisateurs / Notifications** (si nécessaire) | `SupplierFormViewModel`, `SuppliersListViewModel`, `UserFormViewModel`, `UsersListViewModel`, `NotificationsListViewModel`, `MainWindowViewModel` (alertes stock) |
| **P2C-8** | **Composition root** : isoler/assainir, décider `AddInfrastructure` (rebrancher ou supprimer), réduire le couplage `MMV.App → MMV.Infrastructure` | `App.axaml.cs`, `MMV.Infrastructure/DependencyInjection.cs`, Domain services orphelins |
| **P2C-9** | **Bilan final** : UI sans persistance, garde-fous verrouillés, dette résolue | rapport de clôture P2C |

> P2C-1 (tests garde-fous) doit précéder toute migration de flux : il fige l'invariant à
> atteindre et empêche les régressions pendant les phases suivantes.

## 5. Inventaire de départ (constaté en P2B-2K)

ViewModels avec opérations de persistance (`CreateAsync` / `UpdateAsync` / `DeleteAsync` /
`SaveChangesAsync` / `GetByIdAsync` / `GetAllAsync` / `GetWithItemsAsync`) — 20 fichiers :

`CustomerFormViewModel`, `CustomerPrescriptionsViewModel`, `CustomersListViewModel`,
`InventoryViewModel`, `MainWindowViewModel`, `NotificationsListViewModel`,
`OrderKanbanViewModel`, `OrderFormViewModel`, `PrescriptionFormViewModel`, `OrdersViewModel`,
`ProductsListViewModel`, `ProductFormViewModel`, `StockMovementsListViewModel`,
`StockMovementFormViewModel`, `SaleFormViewModel`, `SupplierFormViewModel`,
`SuppliersListViewModel`, `SuppliersViewModel`, `UserFormViewModel`, `UsersListViewModel`.

Code-behind avec persistance directe (bloquant — priorité P2C-3) :
`CustomerFormView.axaml.cs` et `CustomersView.axaml.cs`
(`Repository.CreateAsync/UpdateAsync` + `UnitOfWork.SaveChangesAsync`).

> Note : les occurrences résiduelles dans les ViewModels déjà migrés (`OrderFormViewModel`,
> `OrdersViewModel`, `SaleFormViewModel`, `StockMovementFormViewModel`) correspondent
> majoritairement à des **lectures d'écran** (chargement de listes déroulantes, rechargement
> de détail) volontairement conservées en P2B — voir dette §6.

## 6. Dettes ouvertes reportées vers P2C

1. **Lectures d'affichage** encore servies par des repositories dans certains ViewModels déjà
   migrés (ex. `SaleFormViewModel.InitializeForCustomerAsync`, rechargement de détail dans
   `OrdersViewModel`). Cible : query use cases / ports de lecture applicatifs.
2. **Domain services orphelins** : `CustomerService`, `ProductService`, `PrescriptionService`,
   `OrderService`, `SaleService` injectent `IUnitOfWork` + repositories et font
   `CreateAsync`/`SaveChangesAsync` — comportement **applicatif** logé dans `MMV.Domain`. Ils ne
   sont enregistrés que dans `AddInfrastructure` (inerte) et **consommés nulle part**. Décision
   P2C-8 : migrer vers Application **ou** supprimer.
3. **`AddInfrastructure` inerte** : module DI jamais appelé, doublon de `App.ConfigureServices`.
   Décision P2C-8 : rebrancher proprement (composition root unifié) **ou** supprimer.
4. **Couplage `MMV.App → MMV.Infrastructure`** : aujourd'hui nécessaire car la composition root
   vit dans `App.axaml.cs`. Cible P2C-8 : confiner ce couplage à un seul point d'amorçage.
5. **`CustomersListViewModel`** expose publiquement `Repository` et `UnitOfWork` — fuite de
   persistance consommée par le code-behind. À supprimer en P2C-3.

## 7. Critères de sortie P2C

- Aucun ViewModel n'injecte de `I…Repository`, `IUnitOfWork` ou `DbContext`.
- Aucun code-behind `*.axaml.cs` ne persiste ni n'orchestre de transaction.
- Aucun ViewModel n'expose publiquement un repository / `IUnitOfWork`.
- Tests garde-fous (P2C-1) verts et verrouillant ces invariants.
- Domain services orphelins traités (migrés ou supprimés).
- `AddInfrastructure` assaini ; composition root unifié et documenté.
- Build vert, tests verts, CI distante verte, 0 vulnérabilité,
  `has-pending-model-changes = false`, aucune migration pending.
- `MMV.Application` toujours pure (ne référence que `MMV.Domain`).

## 8. Hors périmètre P2C (reste interdit)

Aucune introduction de logique métier nouvelle : Payment, Invoice, Quote, Money, TVA,
règles Belgique / Maroc, SaaS, multi-Store, Organization. P2C est un **nettoyage
architectural** (déplacement de logique existante), pas une évolution fonctionnelle.
