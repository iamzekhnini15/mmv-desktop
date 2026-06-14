# P2B-2I — Septième vertical slice : Édition de commande

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2B-2I |
| `EXECUTION_MODE` | IMPLEMENT |
| `ALLOW_COMMIT` | true |
| `ALLOW_PUSH` | true |

**Objectif strict** : extraire iso-fonctionnellement le flux d'**édition d'une commande existante** depuis
`OrderFormViewModel.SaveAsync` (branche `UpdateExistingOrderAsync`, isolée en P2B-2D) vers la couche Application
(`src/MMV.Application/UseCases/Orders/UpdateOrder/`), poursuite du strangler pattern
(P2B-2C / 2D / 2E / 2F / 2G / 2H). Déplacement, **pas** refonte. Aucun module financier
(Payment/Invoice/Quote/Money/TVA/règles pays/devis/facture) n'est ouvert.

## 2. Prérequis

Phases validées (GO définitif) : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C, P2B-2D, P2B-2E, P2B-2F, P2B-2G, P2B-2H.

Documents d'architecture (ADR frontières, plan/structure de migration, ADR idempotence/concurrence/numérotation/seeding)
et rapports P2B-2B…2H relus. Le dépôt réel prime sur les rapports — vérification faite sur le code
(`OrderFormViewModel`, `OrdersViewModel`, use cases `CreateOrder`/`DeleteOrder` comme patron, `IOrderRepository`,
`IGenericRepository`, `BaseRepository`, `OrderRepository.GetWithItemsAsync`, entités `Order`/`OrderItem`/`Sale`).

## 3. État Git initial

- Branche : `p2b-architecture`
- Working tree : propre
- Derniers commits : `a471bab` (docs P2B-2H CI), `871fa1a` (feat P2B-2H), `fa8c4e4` (docs P2B-2G)…
- `dotnet --version` : `8.0.417`

## 4. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `git status --short` | propre |
| `dotnet restore MMV.sln` | OK |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 erreur**, 1 avertissement (CS1998 préexistant, `OrderFormViewModel.cs:464`) |
| `dotnet test MMV.sln --no-build -c Debug` | **384 tests verts** (App 118 / Application 43 / Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** |
| `dotnet tool restore` | OK (`dotnet-ef 8.0.27`) |
| `dotnet ef migrations has-pending-model-changes` | **false** (No changes since last migration) |

Baseline conforme → **GO** pour modifier.

## 5. Inventaire du flux édition avant extraction

Méthode ciblée : `OrderFormViewModel.UpdateExistingOrderAsync()` (appelée par `SaveAsync` quand `_isEditMode`).

```csharp
private async Task UpdateExistingOrderAsync()
{
    var order = _existingOrder ?? new Order();
    order.OrderNumber = OrderNumber;
    order.EstimatedDelivery = EstimatedDelivery;
    order.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;

    order.OrderItems.Clear();
    foreach (var line in OrderItems.Where(i => i.IsValid))
        order.OrderItems.Add(new OrderItem { /* ProductId, ItemType, Quantity, UnitPrice, optique si IsLens */ });

    await _orderRepository.UpdateAsync(order);
    await _unitOfWork.SaveChangesAsync();
}
```

| Aspect | Constat |
|---|---|
| Dépendances actuelles | `IOrderRepository`, `IUnitOfWork` (uniquement pour ce flux), plus l'état UI |
| Ports P2A utilisés | aucun (pas de `ITransactionRunner`, pas de `IStockMutationService`, pas de `INumberSequenceService` ici) |
| Repositories utilisés | `IOrderRepository.UpdateAsync` + `IUnitOfWork.SaveChangesAsync` |
| Mode édition | `_existingOrder` (chargé via `GetWithItemsAsync` en amont par `OnViewOrderDetail`) passé au constructeur ; `_isEditMode = existingOrder != null` |
| Rôle de `_existingOrder` | entité cible de la mise à jour ; le `?? new Order()` est un **chemin mort** jamais atteint en édition (le formulaire n'ouvre l'édition qu'avec une commande réelle) |
| Champs `Order` mis à jour | `OrderNumber` (réaffecté à l'identique — lecture seule en édition), `EstimatedDelivery`, `Notes` (blanches→`null`). **Statut / SaleId / OrderDate inchangés**. |
| Lignes (`OrderItems`) | **reconstruction totale** : `Clear()` puis ré-ajout des lignes valides (orphelins supprimés via cascades EF, nouvelles lignes insérées dans le même `SaveChanges`) |
| Paramètres optiques | conservés **uniquement pour les verres** (`IsLens` → OD/OG), `null` sinon |
| `SaveChanges` | **un seul** `SaveChangesAsync` (mono-écriture atomique, reconstruction des lignes incluse) |
| Erreurs attrapées | aucune dans `UpdateExistingOrderAsync` ; `SaveAsync` enveloppe d'un `try/catch (Exception)` qui mappe vers `ErrorMessage` |
| Messages utilisateur | `ErrorMessage = "Erreur lors de la sauvegarde : {ex.Message}"` (inchangé, porté par la VM) |
| Événements / refresh UI | `OrderSaved?.Invoke` levé après succès (inchangé, porté par la VM) |
| **Migré en P2B-2I** | mise à jour des champs + reconstruction des lignes + `Update`/`SaveChanges` |
| **Reporté / hors périmètre** | création (P2B-2D), avancement statut (P2B-2E), encaissement (P2B-2G), suppression (P2B-2H), chargement/`LoadExistingOrderAsync`, navigation Kanban/fiche fabrication, listing, filtrage, facturation/devis |

## 6. Décision de périmètre P2B-2I

Un **seul** use case migré : **`UpdateOrder`** (`UpdateOrderUseCase`). Édition d'une commande existante uniquement.

- **`ITransactionRunner` non requis** : le flux d'origine effectue une seule écriture atomique (`UpdateAsync` + un
  unique `SaveChangesAsync`, la suppression/ré-ajout des lignes faisant partie du même `SaveChanges` via les
  cascades EF existantes). Aucune séquence multi-étapes à protéger → aucun runner introduit (cohérent avec
  `CreateOrderUseCase`/`DeleteOrderUseCase`).
- **Numérotation / statut inchangés** : l'édition ne touche ni la séquence `ORDER` ni le statut. Le numéro est
  réaffecté à l'identique (fidélité au flux d'origine) ; le statut courant est préservé et renvoyé dans le résultat.
- **Chargement via `GetWithItemsAsync`** : nécessaire pour que la reconstruction `Clear()` + ré-ajout soit suivie
  par EF exactement comme dans le flux d'origine (où `_existingOrder` était déjà chargé avec ses lignes).
- **Comportement introuvable** : le `?? new Order()` mort est remplacé par un contrat sûr et explicite —
  `OrderFound = false` sans écriture (cohérent avec `DeleteOrderUseCase`), plutôt que de persister une commande
  fantôme. En édition réelle, la commande existe toujours → `true` en pratique.

## 7. Fichiers créés / modifiés

**Créés** (use case) :

| Fichier | Rôle |
|---|---|
| `UseCases/Orders/UpdateOrder/UpdateOrderCommand.cs` | DTO d'entrée (`OrderId`, `OrderNumber`, `EstimatedDelivery`, `Notes`, `Lines`) |
| `UseCases/Orders/UpdateOrder/UpdateOrderLineCommand.cs` | DTO de ligne (produit, type, quantité, prix, optique OD/OG) |
| `UseCases/Orders/UpdateOrder/UpdateOrderResult.cs` | DTO de sortie (`OrderFound`, `OrderId`, `OrderNumber`, `Status`, `EstimatedDelivery`) |
| `UseCases/Orders/UpdateOrder/IUpdateOrderUseCase.cs` | Port applicatif `Task<UpdateOrderResult> ExecuteAsync(UpdateOrderCommand, CancellationToken)` |
| `UseCases/Orders/UpdateOrder/UpdateOrderUseCase.cs` | Implémentation (charge, met à jour, reconstruit les lignes, persiste) |

**Créés** (tests) :

| Fichier | Rôle |
|---|---|
| `tests/MMV.Application.Tests/UseCases/Orders/UpdateOrderUseCaseTests.cs` | 6 tests sur **vrai SQLite** |
| `tests/MMV.App.Tests/ViewModels/OrderFormViewModelUpdateDelegationTests.cs` | 6 tests de délégation/présentation |

**Modifiés** :

| Fichier | Changement |
|---|---|
| `src/MMV.Application/DependencyInjection.cs` | `AddApplication` enregistre `IUpdateOrderUseCase → UpdateOrderUseCase` (Scoped) |
| `src/MMV.App/ViewModels/OrderFormViewModel.cs` | branche édition **déléguée** au use case ; `UpdateExistingOrderAsync` remplacée par `BuildUpdateOrderCommand` ; `IUpdateOrderUseCase` obligatoire ; **retrait** de `IOrderRepository`/`IUnitOfWork` (devenus inutilisés dans cette VM) |
| `src/MMV.App/ViewModels/OrdersViewModel.cs` | reçoit `IUpdateOrderUseCase` par DI ; le transmet aux deux constructions de `OrderFormViewModel` (qui ne reçoivent plus `IOrderRepository`/`IUnitOfWork`) |
| `tests/MMV.App.Tests/ViewModels/OrderFormViewModelCreateDelegationTests.cs` | constructeur VM mis à jour (signature allégée + `IUpdateOrderUseCase`) |
| `tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs` | constructeur VM mis à jour ; couverture numérotation **préservée** |
| `tests/MMV.App.Tests/ViewModels/OrdersViewModelDeleteDelegationTests.cs` | constructeur `OrdersViewModel` mis à jour (param `IUpdateOrderUseCase`) |

## 8. Command / Result / UseCase

- **`UpdateOrderCommand`** : DTO neutre (aucune dépendance EF/Avalonia/MVVM). Porte `OrderId` (commande à charger),
  `OrderNumber` (réaffecté tel quel, lecture seule en édition), `EstimatedDelivery`, `Notes`, et `Lines`
  (`UpdateOrderLineCommand` : `ProductId`, `ItemType`, `Quantity`, `UnitPrice`, `Sphere`/`Cylinder`/`Axis`/`Addition`).
  Structurellement parallèle à `CreateOrderCommand` (la branche édition reconstruit les lignes comme la création).
- **`UpdateOrderResult`** : `OrderFound`, `OrderId`, `OrderNumber`, `Status`, `EstimatedDelivery`. Pas d'entité ni de
  type EF exposé.
- **`UpdateOrderUseCase`** : charge la commande avec ses lignes (`GetWithItemsAsync`) ; si absente →
  `OrderFound = false` sans écrire ; sinon met à jour les champs (notes blanches→`null`), `Clear()` + reconstruit les
  lignes (optique conservée pour les verres uniquement), puis `UpdateAsync` + un unique `SaveChangesAsync`.
  Constructeur garde-fou : rejette `IOrderRepository`/`IUnitOfWork` `null`.

## 9. Modifications ViewModel

`OrderFormViewModel` :
- **Ajouté** : `IUpdateOrderUseCase` **obligatoire** (rejette `null`) ; `BuildUpdateOrderCommand()` (mapping
  état VM → `UpdateOrderCommand`, `OrderId = _existingOrder?.OrderId`).
- **Branche édition de `SaveAsync`** : `await _updateOrderUseCase.ExecuteAsync(BuildUpdateOrderCommand())`
  (remplace l'appel direct à `UpdateExistingOrderAsync`).
- **Supprimé** : `UpdateExistingOrderAsync()` (mise à jour directe de l'`Order`, reconstruction directe des
  `OrderItem`, `UpdateAsync`/`SaveChangesAsync` directs).
- **Retiré** : champs/paramètres `IOrderRepository _orderRepository` et `IUnitOfWork _unitOfWork`, devenus
  **inutilisés** dans cette VM après migration (conformément à la consigne : retrait autorisé car aucun autre flux
  non migré de cette VM ne les utilise ; éviterait sinon des avertissements CS0414 « champ assigné jamais utilisé »).
- **Inchangé** : validation UI (`CanSave`), `IsSaving`, `OrderSaved`, messages d'erreur, `Title`, chargement
  (`LoadExistingOrderAsync`/`InitializeAsync`), branche **création** (toujours déléguée à `ICreateOrderUseCase`).

`OrdersViewModel` : reçoit `IUpdateOrderUseCase` par DI et le transmet aux deux constructions de
`OrderFormViewModel` (création et édition). Conserve `IOrderRepository`/`IUnitOfWork` pour ses propres usages
(listing, détail, `OrderDetailViewModel`).

## 10. Modifications DI

```csharp
services.AddScoped<IUpdateOrderUseCase, UpdateOrderUseCase>();
```

Portée **Scoped** (même portée que `OpticDbContext`, les repositories et `IUnitOfWork`) → même `DbContext`, cohérent
avec le flux d'origine. `OrdersViewModel` (`AddTransient`) reçoit automatiquement `IUpdateOrderUseCase` par injection.

## 11. Tests ajoutés / adaptés

**`UpdateOrderUseCaseTests` (Application, vrai SQLite, 6 tests)** :
1. édition nominale — champs mis à jour, **lignes reconstruites** (2 anciennes → 1 nouvelle), optique verre conservée, **statut préservé** ;
2. ligne monture — paramètres optiques **ignorés** ;
3. notes blanches → `null` ;
4. commande **introuvable** → `OrderFound = false`, **aucune écriture** ;
5. commande nulle → `ArgumentNullException` ;
6. constructeur sans `IOrderRepository` → `ArgumentNullException`.

> Note technique : la commande semée est rattachée à une **vente parente réelle** car `GetWithItemsAsync` charge via
> la navigation **requise** `Order → Sale` (jointure interne) ; en production toute commande possède sa vente.
> Enforcement FK désactivé (`Foreign Keys=False`), comme `CreateOrderUseCaseTests` (problème latent `SaleId`
> préexistant et orthogonal). Toujours du **vrai SQLite** (fichier + schéma réels), jamais le provider InMemory.

**`OrderFormViewModelUpdateDelegationTests` (App, 6 tests)** :
0. pré-condition : la VM d'édition est prête à sauvegarder après `InitializeAsync` ;
1. l'édition **délègue au use case d'édition** (et **pas** à la création) + `OrderSaved` ;
2. garde anti double-soumission (`IsSaving`) ;
3. exception du use case → message affiché, pas de notification ;
4. constructeur sans `IUpdateOrderUseCase` → `ArgumentNullException` ;
5. mapping état VM → `UpdateOrderCommand` (`OrderId`, `OrderNumber`, lignes).

**Tests adaptés** (signature constructeur) : `OrderFormViewModelCreateDelegationTests`,
`OrderFormViewModelNumberingTests`, `OrdersViewModelDeleteDelegationTests`. Couverture **préservée** ; la couverture
métier de l'édition est désormais portée par la couche Application (vrai SQLite).

**Test d'architecture** : `ApplicationArchitectureTests` (réflexion) inchangé et **vert** — `MMV.Application` ne
référence toujours que `MMV.Domain`.

## 12. Migrations créées ou non

**Aucune migration.** Aucun modèle EF modifié. `has-pending-model-changes = false`.

## 13. Contrôles exécutés (après modification)

`git status --short`, `git diff --stat`, `git diff --check`, `dotnet restore`, `dotnet build --no-restore`,
`dotnet test --no-build`, `dotnet list package --vulnerable --include-transitive`, `dotnet tool restore`,
`dotnet ef migrations has-pending-model-changes`, `dotnet list MMV.Application reference`,
`dotnet list MMV.Application package`.

## 14. Résultats

| Contrôle | Résultat |
|---|---|
| `git diff --check` | **OK** (aucune erreur d'espaces) |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **396 tests verts** (App 124 / Application 49 / Domain 223), **0 échec** |
| `dotnet list package --vulnerable` | **0 vulnérabilité** |
| `dotnet ef migrations has-pending-model-changes` | **false** |
| `dotnet list MMV.Application reference` | **uniquement `MMV.Domain`** |
| `dotnet list MMV.Application package` | uniquement `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` (aucun EF/Avalonia) |

Tests : **+12** vs baseline (384 → 396) : +6 Application (`UpdateOrderUseCaseTests`), +6 App (`OrderFormViewModelUpdateDelegationTests`).

## 15. Vulnérabilités

**Aucune** (0 sur les 7 projets, transitives incluses).

## 16. Warnings résiduels

1 avertissement **préexistant** : `CS1998` sur `OrderFormViewModel.cs` (`LoadExistingOrderAsync`, méthode `async`
sans `await` — flux de **chargement**, hors périmètre P2B-2I). Inchangé par cette phase. Aucun nouvel
avertissement introduit (le retrait des dépendances mortes évite notamment un CS0414).

## 17. Risques résiduels

- **`Order.SaleId = 0` (création autonome)** : problème latent **préexistant** (cf. P2B-2D), orthogonal à
  l'édition. Non corrigé ici (consigne). En édition réelle, la commande possède toujours sa vente.
- **`GetWithItemsAsync` charge plus que nécessaire** (Sale + Customer) : conforme au flux d'origine (`_existingOrder`
  venait de `GetWithItemsAsync`). Optimisation possible mais hors périmètre iso-fonctionnel.
- **Contrat « introuvable »** légèrement durci (`OrderFound = false` au lieu du `?? new Order()` mort) : choix plus
  sûr, sans impact observable en édition réelle.
- **Résolution Scoped depuis la racine / `AddInfrastructure` mort / dépendances mortes diverses** : hors périmètre
  (consigne), inchangés.

## 18. État Git final

- Branche : `p2b-architecture`
- Commit : `3d784e4` — `feat(P2B-2I): move order update to application use case`
- Push : effectué vers `origin/p2b-architecture` (`a471bab..3d784e4`)
- 14 fichiers, 1248 insertions, 62 suppressions

## 19. Verdict local

**GO local.** Tous les critères d'acceptation sont satisfaits :

| Critère | État |
|---|---|
| UseCase `UpdateOrder` existe | ✅ |
| Interface du use case (`IUpdateOrderUseCase`) | ✅ |
| Command / Result existent | ✅ |
| `AddApplication` enregistre le use case (Scoped) | ✅ |
| `OrderFormViewModel` délègue le flux édition | ✅ |
| Création toujours déléguée à `ICreateOrderUseCase` | ✅ |
| Édition existante / reconstruction lignes / optique préservées | ✅ |
| `OrderSaved` / refresh UI / messages préservés | ✅ |
| Comportement utilisateur identique | ✅ |
| Tests Application ajoutés (vrai SQLite) | ✅ |
| Tests ViewModel adaptés/ajoutés | ✅ |
| Test d'architecture vert | ✅ |
| Build vert / tests verts (396) | ✅ |
| 0 vulnérabilité | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration / aucun modèle EF modifié | ✅ |
| Aucune règle Belgique/Maroc/fiscalité/devis/facture/Money | ✅ |

## 20. Validation CI distante

| Champ | Valeur |
|---|---|
| Lien du run CI | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27471914579 |
| Identifiant du run | `27471914579` |
| Commit testé | `3d784e40a1b895c28c7c653df447b284f1e98555` |
| Branche testée | `p2b-architecture` |
| Événement déclencheur | `push` |

| Étape CI | Résultat | Heure (UTC) |
|---|---|---|
| Restore | ✅ success | 16:09:13Z |
| Build | ✅ success | 16:09:41Z |
| Test | ✅ success | 16:10:28Z |
| Audit NuGet | ✅ success | 16:10:46Z |
| Restore .NET tools | ✅ success | 16:10:48Z |
| Check EF Core pending model changes | ✅ success | 16:10:51Z |

- Nombre de tests CI : **396** (App 124 / Application 49 / Domain 223)
- Statut final du workflow : **success**

**P2B-2I = GO DÉFINITIF**

## 21. Prochaine étape candidate

**P2B-2J** — poursuite du strangler sur les flux restants d'`OrdersViewModel`/`OrderDetailViewModel` non encore
migrés (p. ex. impression de la fiche de fabrication, ou un flux de notification résiduel), à confirmer après
inventaire. Aucune ouverture des modules financiers (Payment/Invoice/Quote/Money/TVA/pays) tant que les gates
réglementaires restent fermés.

> Phase **P2B-2I terminée** — commit poussé, CI vert, GO DÉFINITIF. Pas de phase suivante entamée.
