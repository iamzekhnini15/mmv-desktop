# P2B-2J — Nettoyage technique / dépendances mortes / composition root

## 1. Paramètres reçus

| Paramètre | Valeur |
|-----------|--------|
| TARGET_PHASE_ID | P2B-2J |
| EXECUTION_MODE | IMPLEMENT |
| ALLOW_COMMIT | true |
| ALLOW_PUSH | true |

Objectif strict : nettoyer les dépendances mortes laissées par les extractions applicatives P2B-2C → P2B-2I,
simplifier les constructeurs des ViewModels concernées, adapter le wiring DI si nécessaire, vérifier la
composition root et documenter les éléments volontairement conservés — **sans nouveau use case métier ni
changement de comportement utilisateur**.

## 2. Prérequis

Phases validées : P2A, P2B-2A (+CI, +CI-R2), P2B-2B, P2B-2C, P2B-2D, P2B-2E, P2B-2F, P2B-2G, P2B-2H, P2B-2I =
GO définitif.

Documents d'architecture et rapports P2B-2B → P2B-2I relus ; code réel des ViewModels ciblées, des trois fichiers
DI et des tests App/Architecture inspecté. **Le dépôt réel a primé sur les rapports.**

## 3. État Git initial

- Branche : `p2b-architecture`
- Dernier commit : `4c69d54 docs(P2B-2I): record order update CI validation`
- `git status --short` : propre (working tree clean)
- 5 derniers commits :
  - `4c69d54 docs(P2B-2I): record order update CI validation`
  - `3d784e4 feat(P2B-2I): move order update to application use case`
  - `a471bab docs(P2B-2H): record order deletion CI validation`
  - `871fa1a feat(P2B-2H): move order deletion to application use case`
  - `fa8c4e4 docs(P2B-2G): record order balance CI validation`

## 4. Baseline (avant modification)

| Contrôle | Résultat |
|----------|----------|
| `dotnet --version` | 8.0.417 |
| `dotnet restore MMV.sln` | OK (up-to-date) |
| `dotnet build MMV.sln --no-restore -c Debug` | **Build succeeded** — 1 warning (CS1998 préexistant), 0 error |
| `dotnet test MMV.sln --no-build -c Debug` | **396 tests OK** (App 124 / Application 49 / Domain 223), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK (dotnet-ef 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | **false** (« No changes… ») |

Baseline propre et verte → poursuite autorisée.

## 5. Inventaire des dépendances mortes

Analyse par lecture du code réel (constructeurs, champs privés, usages effectifs) des six ViewModels ciblées.

### OrderDetailViewModel
| Dépendance injectée | Réellement lue ? | Transmise à un enfant ? | Statut | Décision |
|---------------------|------------------|--------------------------|--------|----------|
| `IOrderRepository` | **Non** (assignée seulement) | Non | **Morte** (depuis P2B-2E/2G) | **Supprimer** |
| `IUnitOfWork` | **Non** (assignée seulement) | Non | **Morte** (depuis P2B-2E/2G) | **Supprimer** |
| `INotificationRepository?` | **Non** (assignée seulement) | Non | **Morte** (depuis P2B-2E/2G) | **Supprimer** |
| `IAdvanceOrderStatusUseCase` | Oui (`AdvanceStatusAsync`) | — | Vivante | Conserver |
| `ISettleOrderBalanceUseCase` | Oui (`ExecuteEncashBalanceAsync`) | — | Vivante | Conserver |

Confirmation : `grep` sur `_orderRepository|_unitOfWork|_notificationRepository` → uniquement déclaration +
assignation, **aucune lecture** dans les méthodes.

### OrdersViewModel
| Dépendance | Réellement lue / transmise ? | Statut | Décision |
|------------|------------------------------|--------|----------|
| `IOrderRepository` | Oui : `OrdersListViewModel`, `GetWithItemsAsync`, `OrderKanbanViewModel` | Vivante | **Conserver** |
| `IUnitOfWork` | Oui : transmise à `OrderKanbanViewModel` (hors périmètre) | Vivante | **Conserver** |
| `INotificationRepository` | **Uniquement** transmise à `OrderDetailViewModel` | **Morte par cascade** (une fois la VM de détail nettoyée) | **Supprimer** |
| `ICustomer/Product/PrescriptionRepository`, `IDialogService`, `INumberSequenceService`, use cases | Oui | Vivantes | Conserver |

### OrderFormViewModel
Toutes les dépendances utilisées (`ICustomerRepository`, `IProductRepository`, `IPrescriptionRepository`,
`INumberSequenceService`, `ICreateOrderUseCase`, `IUpdateOrderUseCase`). `IOrderRepository`/`IUnitOfWork` **déjà
absents** (retirés en P2B-2I). → **Rien à nettoyer.**

### SaleFormViewModel
`IProductRepository?` et `IPrescriptionRepository?` utilisés dans `InitializeForCustomerAsync` (chargement
d'écran), `IRegisterSaleUseCase` utilisé. Déjà documentés comme **volontairement conservés** (P2B-2C). → **Rien à
nettoyer.**

### StockMovementFormViewModel
`IProductRepository` (chargement produits), `IDialogService` (dialogues), `ICreateStockMovementUseCase`
(délégation P2B-2F) tous utilisés. → **Rien à nettoyer.**

### ProductsViewModel
`IProductRepository`, `ISupplierRepository`, `IUnitOfWork`, `IDialogService`, `ICreateStockMovementUseCase` tous
utilisés (formulaires produit, fournisseurs, mouvements de stock). → **Rien à nettoyer.**

## 6. Décisions de nettoyage

1. **Supprimer** `IOrderRepository`, `IUnitOfWork`, `INotificationRepository` de `OrderDetailViewModel`
   (champs + paramètres + assignations). Suppression du `using MMV.Domain.Interfaces.Repositories;` devenu inutile.
2. **Supprimer** `INotificationRepository` de `OrdersViewModel` (cascade : ne servait plus qu'au pass-through vers
   la VM de détail). `IOrderRepository`/`IUnitOfWork` **conservés** (encore utilisés : rechargement détail, Kanban).
3. **Conserver** les dépendances de SaleFormViewModel / StockMovementFormViewModel / ProductsViewModel /
   OrderFormViewModel : aucune n'est morte.
4. **Documenter** (sans supprimer) `AddInfrastructure` : inerte mais inoffensif (cf. §9).
5. **Aucun** use case métier modifié.

## 7. Fichiers modifiés

| Fichier | Nature |
|---------|--------|
| `src/MMV.App/ViewModels/OrderDetailViewModel.cs` | Retrait 3 dépendances mortes + using |
| `src/MMV.App/ViewModels/OrdersViewModel.cs` | Retrait `INotificationRepository` + adaptation construction VM détail |
| `src/MMV.Infrastructure/DependencyInjection.cs` | Documentation (XML remarks) du wiring inerte `AddInfrastructure` — **aucun changement de comportement** |
| `tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs` | Adaptation des constructions de VM + test ctor null |
| `tests/MMV.App.Tests/ViewModels/OrderDetailViewModelEncashDelegationTests.cs` | Adaptation des constructions de VM (3 sites) + test ctor null |
| `tests/MMV.App.Tests/ViewModels/OrdersViewModelDeleteDelegationTests.cs` | Retrait du mock `INotificationRepository` (2 sites) |
| `tests/MMV.App.Tests/ViewModels/OrderViewModelDependencyHygieneTests.cs` | **Nouveau** — garde-fou réflexion (constructeurs sans ports morts) |

## 8. Composition root : état constaté

`src/MMV.App/App.axaml.cs` → `ConfigureServices()` est le **composition root unique**. Il enregistre directement :

- `OpticDbContext` (SQLite, chemin résolu par `SqliteDatabasePathResolver`, P2A-1A) ;
- `IUnitOfWork` + tous les repositories (`ICustomer/Product/ProductCategory/Supplier/Notification/Prescription/
  Order/Sale/StockMovement/UserRepository`) en `Scoped` ;
- les services techniques `ITransactionRunner`, `IStockMutationService`, `INumberSequenceService` (Scoped) ;
- `AddApplication()` (use cases, cf. §9) ;
- les services Auth/Session/Permission/Theme/Navigation/Dialog ;
- les ViewModels (`MainWindowViewModel` Singleton ; les autres Transient, dont `OrdersViewModel` et
  `ProductsViewModel` ligne 165/163).

`OrdersViewModel` étant résolu par le conteneur (réflexion sur le constructeur), le retrait du paramètre
`INotificationRepository` **n'a nécessité aucune modification d'App.axaml.cs** : `INotificationRepository` reste
enregistré et continue d'être injecté là où il est réellement requis (`MainWindow`, ligne 73).

- **Doublons** : aucun doublon nuisible détecté. Les repositories/services techniques figurent à la fois dans
  `App.ConfigureServices` (utilisé) et dans `MMV.Infrastructure.AddInfrastructure` (non appelé, cf. §9) — il ne
  s'agit pas d'un double enregistrement actif puisque `AddInfrastructure` n'est jamais invoquée.
- **Dépendances mortes** dans la composition root : aucune registration active morte. Aucune registration
  supprimée (pas de refonte DI).

## 9. AddApplication / AddInfrastructure : décision

- **`AddApplication`** (`src/MMV.Application/DependencyInjection.cs`) : **appelée** par App.axaml.cs (ligne 147).
  Enregistre les 7 use cases (RegisterSale, CreateOrder, AdvanceOrderStatus, CreateStockMovement,
  SettleOrderBalance, DeleteOrder, UpdateOrder) en `Scoped`. **Conservée telle quelle.**
- **`AddInfrastructure`** (`src/MMV.Infrastructure/DependencyInjection.cs`) : **non appelée** par le composition
  root (`grep AddInfrastructure` → uniquement sa propre définition + docs ; aucun appelant en production ni en
  test). C'est un wiring **inerte mais inoffensif** : aucun effet de bord au démarrage. Conformément aux règles de
  la phase (« Si AddInfrastructure est mort mais inoffensif, documenter plutôt que supprimer » / « Ne pas supprimer
  AddInfrastructure s'il peut servir à une future phase »), il est **documenté** via un bloc `<remarks>` et
  **conservé** comme point d'extension. **Non supprimé.**

## 10. Modifications ViewModels

- **OrderDetailViewModel** : constructeur passé de 5 à 2 paramètres
  `(IAdvanceOrderStatusUseCase, ISettleOrderBalanceUseCase)`. Champs `_orderRepository`, `_unitOfWork`,
  `_notificationRepository` supprimés. Logique métier (avancement de statut, encaissement) **inchangée** (toujours
  intégralement déléguée aux use cases). Commentaire de cleanup ajouté.
- **OrdersViewModel** : paramètre/champ `INotificationRepository` supprimés ; construction de
  `OrderDetailViewModel` adaptée à la nouvelle signature. `IOrderRepository`/`IUnitOfWork` conservés. Comportement
  de navigation/liste/Kanban/détail **inchangé**. Commentaire de justification ajouté.
- Les 4 autres ViewModels ciblées : **aucune modification** (aucune dépendance morte).

## 11. Modifications tests

- **OrderDetailViewModelAdvanceDelegationTests** / **…EncashDelegationTests** : helpers et tests de constructeur
  adaptés à la signature à 2 paramètres ; mocks `IOrderRepository`/`IUnitOfWork`/`INotificationRepository`
  retirés ; `using` désormais inutile supprimé. Couverture comportementale (délégation, OrderUpdated, introuvable,
  erreur, mapping, gardes) **préservée**.
- **OrdersViewModelDeleteDelegationTests** : mock `INotificationRepository` retiré des 2 sites de construction
  (`using` toujours nécessaire pour les autres repositories → conservé). Scénarios de suppression inchangés.
- **OrderViewModelDependencyHygieneTests** (nouveau) : 2 tests par réflexion verrouillant l'absence des ports
  morts dans les constructeurs de `OrderDetailViewModel` (3 ports) et `OrdersViewModel` (`INotificationRepository`).
  Simple et stable, conforme à la latitude « petit test d'architecture App » de la phase.
- **ApplicationArchitectureTests** : **non modifiés**, restent verts (invariant de pureté Application intact).

## 12. Migrations créées ou non

**Aucune migration créée.** Aucun modèle EF touché. `has-pending-model-changes` = false avant et après.

## 13. Contrôles exécutés (après modification)

```
git status --short
git diff --stat
git diff --check
dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build  -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list src/MMV.Application/MMV.Application.csproj reference
dotnet list src/MMV.Application/MMV.Application.csproj package
```

## 14. Résultats

| Contrôle | Résultat |
|----------|----------|
| Build | **succeeded** — 0 error, 1 warning (CS1998 préexistant, hors périmètre) |
| Tests | **398 OK** (App **126** [+2 hygiène] / Application 49 / Domain 223), 0 échec |
| `git diff --check` | propre (uniquement avertissements LF→CRLF informatifs) |
| Vulnérabilités | **0** (7 projets) |
| `has-pending-model-changes` | **false** |
| Référence MMV.Application | **uniquement** `..\MMV.Domain\MMV.Domain.csproj` |
| Packages MMV.Application | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` uniquement (aucun EF/Infra/Avalonia) |

## 15. Vulnérabilités

Aucune. Audit NuGet (transitif inclus) vert sur les 7 projets.

## 16. Warnings résiduels

- **CS1998** dans `OrderFormViewModel.LoadExistingOrderAsync` (ligne 464) : **préexistant**, explicitement **hors
  périmètre** de cette phase. Le nettoyage n'a pas touché cette méthode → laissé tel quel.
- Aucun nouveau warning introduit. Les `using` rendus inutiles par le nettoyage ont été retirés (3 fichiers de
  test + OrderDetailViewModel).

## 17. Risques résiduels

- **Très faibles.** Suppression de dépendances **prouvées mortes** (assignées, jamais lues) ; aucune dépendance
  encore utile retirée. Comportement utilisateur strictement identique (la logique métier était déjà déléguée aux
  use cases avant cette phase).
- `OrdersViewModel` résolu par le conteneur DI : le changement de signature est transparent au démarrage.
- `AddInfrastructure` reste inerte ; sa documentation ne modifie rien à l'exécution.
- `Order.SaleId = 0`, règles fiscales/TVA/facturation/devis/paiement complet, multi-magasin, SaaS : **non
  abordés** (hors périmètre, conformément aux interdictions).

## 18. État Git final

Commit créé : `a42bb1b refactor(P2B-2J): prune dead order viewmodel dependencies`

Fichiers committés :
```
A  docs/implementation/P2B-2J-report.md
M  src/MMV.App/ViewModels/OrderDetailViewModel.cs
M  src/MMV.App/ViewModels/OrdersViewModel.cs
M  src/MMV.Infrastructure/DependencyInjection.cs
M  tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs
M  tests/MMV.App.Tests/ViewModels/OrderDetailViewModelEncashDelegationTests.cs
A  tests/MMV.App.Tests/ViewModels/OrderViewModelDependencyHygieneTests.cs
M  tests/MMV.App.Tests/ViewModels/OrdersViewModelDeleteDelegationTests.cs
```

`git diff --stat` (code) : 6 fichiers modifiés, 21 insertions, 42 suppressions. Push : `4c69d54..a42bb1b p2b-architecture -> p2b-architecture`.

## 19. Verdict local

**GO local.**

- Inventaire des dépendances mortes réalisé sur les 6 ViewModels.
- Dépendances mortes confirmées retirées (OrderDetailViewModel ×3, OrdersViewModel ×1) ; conservations justifiées.
- Aucune dépendance encore utile supprimée ; constructeurs cohérents ; tests adaptés.
- Composition root documentée ; `AddApplication` (appelée) / `AddInfrastructure` (inerte, conservée, documentée).
- Aucun use case métier modifié ; build vert ; **398 tests verts** ; 0 vulnérabilité ;
  `has-pending-model-changes` = false ; aucune migration ; aucun modèle EF modifié.
- Aucune règle Belgique/Maroc/fiscalité/devis/facture/Money touchée.

## 20. Validation CI distante

| Champ | Valeur |
|-------|--------|
| Lien du run | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27472724654 |
| Identifiant du run | 27472724654 |
| Commit testé | `a42bb1bfcf68db70962e3f7e4a0a0a3f9c26285e` |
| Branche testée | `p2b-architecture` |
| Résultat Restore | **success** |
| Résultat Build | **success** |
| Résultat Test | **success** |
| Nombre de tests | **398** (App 126 / Application 49 / Domain 223) |
| Résultat Audit NuGet | **success** — 0 vulnérabilité |
| Résultat Restore .NET tools | **success** |
| Résultat Check EF Core pending model changes | **success** — No changes |
| Statut final du workflow | **completed — success** |

**P2B-2J = GO DÉFINITIF**

## 21. Prochaine étape candidate

**P2B-2K** — au choix (à arbitrer hors de cette phase) :
- traitement ciblé du warning préexistant **CS1998** dans `OrderFormViewModel.LoadExistingOrderAsync` (nettoyage
  technique mineur, sans changement de comportement) ; **ou**
- poursuite de la stratégie strangler sur un éventuel flux non encore migré.

> La phase P2B-2J s'arrête ici. Ne pas commencer P2B-2K.
