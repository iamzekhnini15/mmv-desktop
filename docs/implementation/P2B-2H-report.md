# P2B-2H — Sixième vertical slice : Suppression de commande

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2B-2H |
| `EXECUTION_MODE` | IMPLEMENT |
| `ALLOW_COMMIT` | true |
| `ALLOW_PUSH` | true |

**Objectif strict** : extraire iso-fonctionnellement le flux de suppression d'une commande depuis
`OrdersViewModel.OnDeleteOrderRequested` vers la couche Application
(`src/MMV.Application/UseCases/Orders/DeleteOrder/`), poursuite du strangler pattern
(P2B-2C / 2D / 2E / 2F / 2G). Déplacement, **pas** refonte. Aucun module financier
(Payment/Invoice/Quote/Money/TVA/règles pays) n'est ouvert.

## 2. Prérequis

Phases validées (GO définitif) : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C, P2B-2D, P2B-2E, P2B-2F, P2B-2G.

Documents d'architecture et rapports P2B-2B…2G relus. Le dépôt réel prime sur les rapports — vérification faite
sur le code (`OrdersViewModel`, `OrderDetailViewModel`, `BaseRepository`, `DeleteOrderUseCase`s précédents comme patron,
interfaces de persistance, entités `Order`/`Sale`).

## 3. État Git initial

- Branche : `p2b-architecture`
- Working tree : propre
- Derniers commits : `fa8c4e4` (docs P2B-2G CI), `e683bc3` (feat P2B-2G), `a67aa4d` (docs P2B-2F)…
- `dotnet --version` : `8.0.417`

## 4. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `git status --short` | propre |
| `dotnet restore MMV.sln` | OK |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 erreur**, 1 avertissement (CS1998 préexistant, `OrderFormViewModel.cs:464`) |
| `dotnet test MMV.sln --no-build -c Debug` | **373 tests verts** (App 113 / Application 37 / Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** |
| `dotnet tool restore` | OK (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | **false** (« No changes… ») |

Baseline conforme → GO pour modification.

## 5. Inventaire du flux de suppression avant extraction

Source : `OrdersViewModel.OnDeleteOrderRequested` (souscrit par `DetailViewModel.DeleteRequested +=
OnDeleteOrderRequested` dans `OnViewOrderDetail`).

| Aspect | Constat |
|---|---|
| **Méthode exacte ciblée** | `OrdersViewModel.OnDeleteOrderRequested(object? sender, Order order)` — `private async void` |
| **Déclencheur** | `OrderDetailViewModel.DeleteCommand.Execute(null)` → `DeleteRequested?.Invoke(this, Order)` |
| **Garde d'entrée** | `if (order == null) return;` |
| **Confirmation utilisateur** | `_dialogService.ShowConfirmationAsync("Confirmation de suppression", $"…{order.OrderNumber}…")` |
| **Dépendances utilisées** | `IOrderRepository.DeleteAsync(order.OrderId)`, `IUnitOfWork.SaveChangesAsync()` |
| **Ports P2A** | **Aucun** (`ITransactionRunner`, `IStockMutationService`, `INumberSequenceService` non utilisés) |
| **Suppression effectuée par** | id (`order.OrderId`) — `BaseRepository.DeleteAsync(id)` charge l'entité en interne puis la marque |
| **Comportement si commande introuvable** | Silencieux : `BaseRepository.DeleteAsync(id)` ne lève pas d'exception si l'entité est null (ne marque simplement rien) ; `SaveChangesAsync` sauvegarde 0 changements ; `CloseDetail` + `LoadOrdersAsync` sont appelés normalement |
| **Effets de cascade** | EF cascade : suppression de `OrderItems` liés (configuration EF existante, non modifiée) |
| **SaveChanges** | **Un seul** appel (mono-écriture) |
| **Erreurs attrapées** | `catch (Exception ex)` → `ErrorMessage = $"Erreur de suppression : {ex.Message}"` + `_dialogService.ShowErrorAsync("Erreur", $"Impossible de supprimer la commande :\n{ex.Message}")` |
| **Messages utilisateur** | « Erreur de suppression : {message} » + dialog « Impossible de supprimer la commande : {message} » |
| **Refresh UI** | `CloseDetail()` (ferme le détail) + `ListViewModel.LoadOrdersAsync()` |

**Migré en P2B-2H** : suppression de la commande par id, `SaveChangesAsync`, résultat `OrderFound`.

**Non migré (reporté)** : avancement de statut (P2B-2E), encaissement du solde (P2B-2G), création (P2B-2D),
stock manuel (P2B-2F), édition, navigation Kanban/fiche de fabrication, chargement de liste,
filtrage/recherche, suppression de vente, facturation/devis.

## 6. Décision de périmètre P2B-2H

Un **seul** flux migré : suppression d'une commande. Nom retenu : **`DeleteOrder`**. Use case cible :
`DeleteOrderUseCase`. Aucun autre use case touché.

**Pas de `ITransactionRunner`** : le flux d'origine effectue une seule suppression + un seul `SaveChangesAsync`
(mono-écriture). Une frontière transactionnelle explicite n'est pas nécessaire — l'opération est atomique par
nature. Ce choix est documenté dans le code source (`DeleteOrderUseCase.cs`, bloc `<remarks>`).

## 7. Fichiers créés / modifiés

**Créés**
- `src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderCommand.cs`
- `src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderResult.cs`
- `src/MMV.Application/UseCases/Orders/DeleteOrder/IDeleteOrderUseCase.cs`
- `src/MMV.Application/UseCases/Orders/DeleteOrder/DeleteOrderUseCase.cs`
- `tests/MMV.Application.Tests/UseCases/Orders/DeleteOrderUseCaseTests.cs`
- `tests/MMV.App.Tests/ViewModels/OrdersViewModelDeleteDelegationTests.cs`
- `docs/implementation/P2B-2H-report.md`

**Modifiés**
- `src/MMV.Application/DependencyInjection.cs` (enregistrement du use case)
- `src/MMV.App/ViewModels/OrdersViewModel.cs` (injection + délégation)

## 8. Command / Result / UseCase

**`DeleteOrderCommand`** : DTO neutre minimal — `long OrderId`. Seule donnée nécessaire venant de la ViewModel
(la commande détail expose déjà son `OrderId` via l'entité `Order` passée en paramètre de l'événement).

**`DeleteOrderResult`** : `bool OrderFound` (commande trouvée avant suppression), `long OrderId` (echo de
l'entrée). La ViewModel ne différencie pas le cas trouvé/introuvable (même comportement dans les deux cas :
fermeture + rafraîchissement), mais le résultat permet la vérification en test.

**`DeleteOrderUseCase`** :
1. Validation de la commande (non nulle).
2. `GetByIdAsync(command.OrderId)` — si null, renvoie `OrderFound = false` (aucune écriture).
3. `DeleteAsync(order)` (par entité, évite la double recherche interne de `BaseRepository.DeleteAsync(id)`).
4. `SaveChangesAsync()`.
5. Renvoie `OrderFound = true, OrderId`.

Les cascades EF existantes (`OrderItems`) sont préservées sans modification.

## 9. Modifications ViewModel

`OrdersViewModel.OnDeleteOrderRequested` ne contient plus de logique de persistance. La méthode :
- Conserve la garde `if (order == null) return`
- Conserve la confirmation utilisateur (`_dialogService.ShowConfirmationAsync`)
- Construit `new DeleteOrderCommand { OrderId = order.OrderId }`
- Appelle `_deleteOrderUseCase.ExecuteAsync(command)` (await, portée Scoped partagée)
- Conserve `CloseDetail()` + `await ListViewModel.LoadOrdersAsync()` en cas de succès
- Conserve `ErrorMessage = $"Erreur de suppression : {ex.Message}"` + `ShowErrorAsync` en cas d'exception

Nouveau paramètre de constructeur **obligatoire** `IDeleteOrderUseCase deleteOrderUseCase` (rejet de `null`).

`IOrderRepository` et `IUnitOfWork` restent injectés dans `OrdersViewModel` — ils sont encore nécessaires pour
les flux non migrés (`OnViewOrderDetail` : `GetWithItemsAsync` ; `OnShowKanban` : `OrderKanbanViewModel`).

`OrderDetailViewModel` **non modifié** : le flux de suppression vit entièrement dans `OrdersViewModel` (handler
de l'événement), pas dans `OrderDetailViewModel` (qui se borne à lever l'événement `DeleteRequested`).

## 10. Modifications DI

`AddApplication` enregistre `services.AddScoped<IDeleteOrderUseCase, DeleteOrderUseCase>();` (portée `Scoped` :
même que `OpticDbContext` / repositories / `IUnitOfWork` — même `DbContext`). Aucun autre enregistrement modifié.

## 11. Tests ajoutés / adaptés

**Application** — `DeleteOrderUseCaseTests` (vrai SQLite temporaire, jamais InMemory), 6 tests :
- suppression nominale (`OrderFound = true`) ;
- commande introuvable (`OrderFound = false`, aucune écriture) ;
- persistance effective (commande absente de la base après exécution) ;
- commande nulle → `ArgumentNullException` ;
- constructeur sans `IOrderRepository` → `ArgumentNullException` ;
- constructeur sans `IUnitOfWork` → `ArgumentNullException`.

**App** — `OrdersViewModelDeleteDelegationTests`, 5 tests :
- suppression confirmée → use case appelé + détail fermé ;
- suppression annulée → use case non appelé ;
- mapping état VM → `DeleteOrderCommand.OrderId` ;
- exception du use case → `ErrorMessage` affiché, détail non fermé ;
- constructeur sans `IDeleteOrderUseCase` → `ArgumentNullException`.

Tests d'architecture (`ApplicationArchitectureTests`) inchangés et toujours verts.
Les tests `OrderDetailViewModelAdvanceDelegationTests` et `OrderDetailViewModelEncashDelegationTests`
restent verts sans modification (constructeur de `OrderDetailViewModel` inchangé).

## 12. Migrations créées ou non

**Aucune migration.** Aucune entité Domain modifiée, aucun changement de modèle EF.
`has-pending-model-changes` = **false**.

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check`, `dotnet restore`, `dotnet build --no-restore`,
`dotnet test --no-build`, `dotnet list … --vulnerable --include-transitive`, `dotnet tool restore`,
`dotnet ef migrations has-pending-model-changes`, `dotnet list MMV.Application … reference`,
`dotnet list MMV.Application … package`.

## 14. Résultats

| Contrôle | Résultat |
|---|---|
| Build | **0 erreur**, 1 avertissement (CS1998 préexistant, hors périmètre) |
| Tests | **384 verts** (App 118 / Application 43 / Domain 223), 0 échec, 0 ignoré |
| `git diff --check` | OK (warnings LF→CRLF informatifs, pré-existants) |
| `has-pending-model-changes` | **false** |
| Référence `MMV.Application` | **uniquement** `MMV.Domain` |
| Packages `MMV.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (inchangé) |

Delta tests : **+11** (373 → 384) : +6 Application, +5 App.

## 15. Vulnérabilités

**0 vulnérabilité** (tous projets), transitives incluses.

## 16. Warnings résiduels

1 seul : `CS1998` dans `OrderFormViewModel.cs:464` — **préexistant**, hors périmètre P2B-2H (interdiction de
correction). Aucun nouveau warning introduit.

## 17. Risques résiduels

1. **Dépendances partiellement inutilisées dans `OrdersViewModel`** : `IOrderRepository` et `IUnitOfWork` ne sont
   plus utilisés par le flux de suppression mais restent nécessaires pour `OnViewOrderDetail`
   (`GetWithItemsAsync`) et `OnShowKanban` (`OrderKanbanViewModel`). Aucun warning compilateur.
   Prunables uniquement une fois ces flux migrés (hors périmètre P2B-2H).
2. **Dépendances inutilisées dans `OrderDetailViewModel`** (rapport P2B-2G §17.2) : `IOrderRepository`,
   `IUnitOfWork`, `INotificationRepository` toujours injectés mais non lus. Non corrigés (interdiction).
3. **`Order.SaleId = 0`** et autres problèmes préexistants : non corrigés (interdiction explicite).
4. Aucun module financier introduit (pas de `Money`/devise/TVA/facture/devis/règles pays) — invariant respecté.

## 18. État Git final

Commit créé et poussé (conforme `ALLOW_COMMIT=true`, `ALLOW_PUSH=true`).

```
commit 871fa1a — feat(P2B-2H): move order deletion to application use case
branche : p2b-architecture
9 files changed, 845 insertions(+), 3 deletions(-)
```

## 19. Verdict local

**P2B-2H = GO LOCAL**

Tous les critères d'acceptation locaux sont satisfaits :
- `DeleteOrderUseCase` existe, interface `IDeleteOrderUseCase` existe, `DeleteOrderCommand` existe,
  `DeleteOrderResult` existe ;
- `AddApplication` enregistre le use case (portée `Scoped`) ;
- `OrdersViewModel` délègue la suppression au use case ;
- confirmation utilisateur conservée dans la VM ;
- comportement utilisateur identique (fermeture + rafraîchissement liste) ;
- comportement introuvable silencieux préservé (iso-fonctionnel) ;
- tests Application ajoutés (6 tests, vrai SQLite) ;
- tests ViewModel ajoutés (5 tests, mocks) ;
- build vert (0 erreur) ;
- **384 tests verts** (0 échec, 0 ignoré) ;
- 0 vulnérabilité ;
- `has-pending-model-changes` = false ;
- aucune migration ;
- aucun modèle EF modifié ;
- aucune règle Belgique/Maroc/fiscalité/devis/facture/Money ;
- `MMV.Application` ne référence que `MMV.Domain`.

## 20. Validation CI distante

| Champ | Valeur |
|---|---|
| Lien du run CI | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27471122846 |
| Identifiant du run | `27471122846` |
| Commit testé | `871fa1a9005cb70a4100b5ea872e4f3839d8196d` |
| Branche testée | `p2b-architecture` |
| Résultat Restore | **success** |
| Résultat Build | **success** |
| Résultat Test | **success** |
| Nombre de tests CI | **384** |
| Résultat Audit NuGet | **success** |
| Résultat Restore .NET tools | **success** |
| Résultat Check EF Core pending model changes | **success** |
| Statut final du workflow | **success** (durée : 4 min 01 s) |

**P2B-2H = GO DÉFINITIF**

## 21. Prochaine étape candidate

**P2B-2I** — Septième vertical slice. Candidats restants dans la sphère Commandes/Ventes :
- **Édition de commande** (`OrderFormViewModel`, branche édition) — flux d'édition de commande existante.
- **Nettoyage technique** : prune des dépendances de persistance désormais mortes dans `OrderDetailViewModel`
  (`IOrderRepository`, `IUnitOfWork`, `INotificationRepository` non lus) et dans `OrdersViewModel`
  (dépendances résiduelles du flux migré), à isoler dans une phase dédiée.

La sélection définitive reste soumise au gating (une seule étape à la fois).
