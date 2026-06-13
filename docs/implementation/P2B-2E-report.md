# P2B-2E — Troisième vertical slice « Statut / réception de commande » — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2E**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : extraire le flux d'**avancement de statut / réception** d'une commande de
> [`OrderDetailViewModel.AdvanceStatusAsync`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs) vers la
> couche Application (`src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/`), **sans changement de
> comportement observable**. Déplacement, pas refonte.

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2E` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Prérequis

Phases précédentes **validées (GO définitif)** : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C, P2B-2D.

| Élément | Valeur |
|---|---|
| Branche | `p2b-architecture` |
| Dernier commit P2B-2D | `975067d` (docs CI) + `59a782b` (feat) |
| Dernier run CI connu | **#27** — id `27463450102` — commit `59a782b` — **success** — **340** tests |
| Pipeline CI | restore → build → test → audit NuGet → restore .NET tools → `has-pending-model-changes` |

Documents de cadrage lus avant modification (le dépôt prime sur les rapports) :
[adr-application-boundaries](../architecture/adr-application-boundaries.md),
[application-layer-migration-plan](../architecture/application-layer-migration-plan.md),
[application-layer-structure](../architecture/application-layer-structure.md),
[P2B-2B-report](P2B-2B-report.md), [P2B-2C-report](P2B-2C-report.md), [P2B-2D-report](P2B-2D-report.md),
ADR P2A (transaction-idempotency, stock-concurrency, numbering, environments-seeding). Code lu :
`OrderDetailViewModel`, `OrdersViewModel`, `OrderFormViewModel`, `OrdersListViewModel`
(`StatusEnumToDisplay`), `RegisterSaleUseCase` / `CreateOrderUseCase` (modèles de référence),
`DependencyInjection`, `App.axaml.cs`, ports P2A (`ITransactionRunner`), interfaces repos
(`IOrderRepository`, `IStockMovementRepository`, `INotificationRepository`, `IUnitOfWork`,
`IGenericRepository`), entités `Order`/`OrderItem`/`StockMovement`/`Notification`/`Sale`, enum `OrderStatus`,
`OrderRepository.GetWithItemsAsync`, tests existants (`CreateOrderUseCaseTests`,
`OrderFormViewModelCreateDelegationTests`, `ApplicationArchitectureTests`).

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → 975067d docs(P2B-2D): record supplier order CI validation
                            59a782b feat(P2B-2D): move supplier order creation to application use case
                            fd3e33b docs(P2B-2C): record sale use case CI validation
                            889e663 feat(P2B-2C): move sale registration to application use case
                            d6050d4 docs(P2B-2B): record application shell CI validation
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ tous les projets restaurés |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant, `OrderFormViewModel` ligne 464) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **340** (Domain **223** + App **101** + Application **16**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |

Dépôt propre, suite verte, 0 vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies (**GO baseline**).

---

## 5. Inventaire du flux statut / réception avant extraction

Le flux ciblé est [`OrderDetailViewModel.AdvanceStatusAsync`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs)
(le nom réel confirmé dans le dépôt). Inventaire vérifié :

| Aspect | Constat (avant) |
|---|---|
| Dépendances injectées (VM) | `IOrderRepository`, `IUnitOfWork`, `IStockMovementRepository`, `INotificationRepository?` (optionnel) |
| **Machine à états** | `GetNextStatus()` (VM) : `New→ToFabricate→InProgress→QualityCheck→Ready→Delivered`. Pilote **aussi** l'affichage (`NextStatusDisplay`, `CanAdvanceStatus`) **de façon synchrone**. |
| Ports P2A | **Aucun `ITransactionRunner`**, **aucun `IStockMutationService`** dans ce flux. |
| Rechargement | `GetWithItemsAsync(OrderId)` (recharge « fraîche » incluant `OrderItems.Product` et `Sale.Customer`). |
| Transition | `previousStatus = Order.Status` (entité **en mémoire**, avant reload) ; `fresh.Status = nextStatus` ; `UpdateAsync(fresh)`. |
| **Mouvements de stock** | **Conditionnel** : si `previousStatus == ToFabricate && nextStatus == InProgress` ⇒ pour **chaque** article avec `ProductId` : `StockMovement { Out, Quantity = item.Quantity (positif), Reason = "Fabrication commande {OrderNumber}", CreatedAt = UtcNow }` via `CreateAsync` ; puis `item.Product.StockQuantity -= item.Quantity` (décrément **direct**, pas de `IStockMutationService`). |
| **Notification** | **Conditionnelle** : si `_notificationRepository != null` ⇒ `Notification { Type="OrderStatusChanged", Title, Message (avec `CustomerName` + libellés `StatusEnumToDisplay`), EntityId, EntityType="Order", IsRead=false, CreatedAt=Now }` via `CreateAsync`. |
| **SaveChanges** | **Un seul** `SaveChangesAsync()` final (statut + mouvements + décréments + notification) — déjà **atomique** (transaction implicite EF). |
| Erreurs attrapées | `catch (Exception)` générique → `ErrorMessage = "Erreur lors du changement de statut : {ex.Message}"` + `Debug.WriteLine`. Cas particulier : `fresh == null` ⇒ `ErrorMessage = "Commande introuvable."` (retour anticipé, **sans** exception). |
| Messages utilisateur | « Commande introuvable. » ; « Erreur lors du changement de statut : … ». |
| Refresh / événements UI | `Order = fresh` ; `Items = new ObservableCollection<OrderItem>(fresh.OrderItems)` ; `OrderUpdated?.Invoke(this, fresh)`. Garde de présentation : `IsLoading` (**aucune** garde anti double-soumission de type `IsSaving` sur ce flux — seul `CanAdvanceStatus` gate le bouton). |

Autres flux de `OrderDetailViewModel` **inventoriés** : `ExecuteEncashBalanceAsync` (encaissement du solde) et
la checklist contrôle qualité (`CheckFrameAlignment`/…/`AllChecksComplete`).

---

## 6. Décision de périmètre P2B-2E

**Flux migré : `AdvanceOrderStatus` (avancement de statut / réception) depuis `OrderDetailViewModel.AdvanceStatusAsync`.**

**Reportés** (documentés, hors périmètre, conformément au point de vigilance) :
- **Encaissement du solde** (`OrderDetailViewModel.ExecuteEncashBalanceAsync`) — reste dans la VM, inchangé.
- **Checklist contrôle qualité** (état de présentation `Check*` / `AllChecksComplete`) — pure présentation, reste dans la VM.
- **Édition / suppression / création** de commande — déjà traitées (création P2B-2D) ou reportées (édition, suppression).

Décisions clés iso-fonctionnelles :

1. **Pas de `ITransactionRunner`.** Le flux d'origine effectue plusieurs modifications d'entités mais les
   valide par un **unique** `SaveChangesAsync` final — déjà atomique (EF enveloppe un `SaveChanges` dans sa
   propre transaction implicite). Aucune séquence multi-`SaveChanges` à protéger ⇒ introduire une transaction
   explicite serait une **refonte**, pas un déplacement. Même choix iso-fonctionnel qu'en P2B-2D §6.2.
2. **Machine à états laissée dans la VM.** `GetNextStatus()` pilote, de façon **synchrone**, l'état
   d'affichage (`NextStatusDisplay`, `CanAdvanceStatus`). La transition résolue (`CurrentStatus → NextStatus`)
   est **transmise** au use case via la `Command` ; le use case n'orchestre que la **persistance** de
   l'avancement (la logique R-05 : multi-repo, mouvements de stock, notification, save). *(Tension assumée
   avec « migrer les règles d'avancement » : priorité à l'iso-fonctionnalité et au dépôt réel — même esprit
   que la numérotation laissée à l'ouverture en P2B-2D §6.1.)*
3. **Libellés d'affichage transmis.** Le texte de la notification utilise `OrdersListViewModel.StatusEnumToDisplay`
   (statique **App**) et `CustomerName` (VM). Comme `MMV.Application` **ne peut pas** référencer `MMV.App`, la
   VM calcule ces fragments de présentation et les transmet dans la `Command` (`CustomerDisplayName`,
   `CurrentStatusDisplay`, `NextStatusDisplay`) ⇒ texte de notification **préservé au caractère près**.
4. **Décrément de stock préservé tel quel** : mouvement `Out` de quantité **positive** + décrément **direct**
   de `Product.StockQuantity` (le flux n'utilisait **pas** `IStockMutationService` ; ne pas en introduire).
5. **`Order.SaleId`** : non touché (problème préexistant `SaleId = 0` hors périmètre, cf. P2B-2D §16.2).

---

## 7. Fichiers créés / modifiés

### 7.1 Créés — couche Application (use case, 4 fichiers)

| Fichier | Rôle |
|---|---|
| [`UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusCommand.cs`](../../src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusCommand.cs) | Entrée (DTO) : `OrderId`, `CurrentStatus`, `NextStatus`, `CustomerDisplayName`, `CurrentStatusDisplay`, `NextStatusDisplay` |
| [`UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusResult.cs`](../../src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusResult.cs) | Sortie (DTO) : `OrderFound`, `Order?`, `OldStatus`, `NewStatus`, `HasCreatedStockMovements`, `CreatedStockMovementCount`, `HasNotification` |
| [`UseCases/Orders/AdvanceOrderStatus/IAdvanceOrderStatusUseCase.cs`](../../src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/IAdvanceOrderStatusUseCase.cs) | Contrat : `Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand, CancellationToken)` |
| [`UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs`](../../src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs) | Orchestration iso-fonctionnelle (réutilise `IOrderRepository` + `IStockMovementRepository` + `IUnitOfWork` + `INotificationRepository?`) |

### 7.2 Créés — tests (2 fichiers)

| Fichier | Rôle |
|---|---|
| [`tests/MMV.Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs`](../../tests/MMV.Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs) | **6** tests d'intégration **vrai SQLite** |
| [`tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs`](../../tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs) | **5** tests de délégation / présentation |

### 7.3 Modifiés (3 fichiers)

| Fichier | Changement |
|---|---|
| [`src/MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) | `AddApplication` enregistre `IAdvanceOrderStatusUseCase → AdvanceOrderStatusUseCase` (Scoped) |
| [`src/MMV.App/ViewModels/OrderDetailViewModel.cs`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs) | `AdvanceStatusAsync` **délègue** au use case ; `CreateStockMovementsForFabrication` **supprimée** ; `IStockMovementRepository` retiré ; `IAdvanceOrderStatusUseCase` obligatoire ajouté ; `IOrderRepository`/`IUnitOfWork`/`INotificationRepository?` conservés (encaissement reporté) |
| [`src/MMV.App/ViewModels/OrdersViewModel.cs`](../../src/MMV.App/ViewModels/OrdersViewModel.cs) | reçoit `IAdvanceOrderStatusUseCase` par DI ; le transmet à `OrderDetailViewModel` ; `IStockMovementRepository` retiré (n'était transmis qu'à `OrderDetailViewModel`) |

> **`App.axaml.cs` et `MMV.sln`/`.csproj` non modifiés** : le composition root appelle déjà
> `services.AddApplication()` (P2B-2B) ; `OrdersViewModel` (déjà `AddTransient`) voit sa nouvelle dépendance
> `IAdvanceOrderStatusUseCase` résolue automatiquement, et son ancienne dépendance `IStockMovementRepository`
> (toujours enregistrée, utilisée ailleurs) simplement plus injectée ici. Le projet `MMV.Application.Tests`
> existe déjà (P2B-2C) et globalise les nouveaux fichiers.

---

## 8. Command / Result / UseCase

**`AdvanceOrderStatusCommand`** — porte l'état VM nécessaire : `OrderId`, `CurrentStatus` (statut précédent,
entité en mémoire), `NextStatus` (transition résolue par la machine à états VM), et les **fragments de
présentation** `CustomerDisplayName` / `CurrentStatusDisplay` / `NextStatusDisplay` (calculés par la VM,
repris au caractère près dans la notification). Aucun montant, aucun `Money`, aucune devise, aucune règle
pays/fiscalité.

**`AdvanceOrderStatusResult`** — `OrderFound` (false ⇒ « Commande introuvable. » sans changement d'état),
`Order?` (entité fraîchement rechargée, exposée comme `RegisterSaleResult.Sale` car la VM doit réafficher la
commande et propager `OrderUpdated` avec cette entité), `OldStatus`/`NewStatus`,
`HasCreatedStockMovements`/`CreatedStockMovementCount`, `HasNotification`.

**`AdvanceOrderStatusUseCase`** — dépend de `IOrderRepository` + `IStockMovementRepository` + `IUnitOfWork`
(**obligatoires**, constructeur rejette `null`) et `INotificationRepository?` (**optionnel**, reproduisant la
garde `if (_notificationRepository != null)`). `ExecuteAsync` reproduit **à l'identique** :
recharge `GetWithItemsAsync` ; si null ⇒ `OrderFound = false` ; sinon `fresh.Status = NextStatus` +
`UpdateAsync` ; si `CurrentStatus == ToFabricate && NextStatus == InProgress` ⇒ mouvements `Out` (quantité
positive) + décrément direct du stock produit ; si repo notif présent ⇒ notification (texte préservé) ; **un
seul** `SaveChangesAsync` final ; renvoie le `Result`. Exceptions laissées remonter telles quelles.

---

## 9. Modifications de `OrderDetailViewModel`

**Ajouté** : `IAdvanceOrderStatusUseCase` **obligatoire** (rejette `null`).

**Modifié** : `AdvanceStatusAsync` construit une `AdvanceOrderStatusCommand` (à partir de `Order.OrderId`,
`Order.Status`, `GetNextStatus()`, `CustomerName`, `StatusEnumToDisplay(...)`), appelle
`await _advanceOrderStatusUseCase.ExecuteAsync(command)`, puis mappe le résultat : `OrderFound == false` ⇒
`ErrorMessage = "Commande introuvable."` (retour anticipé) ; sinon `Order = result.Order`,
`Items = new ObservableCollection<OrderItem>(result.Order.OrderItems)`, `OrderUpdated?.Invoke(this, result.Order)`.
`IsLoading`/`catch`/messages **inchangés**.

**Supprimé** : `CreateStockMovementsForFabrication` (déplacée dans le use case) ; dépendance
`IStockMovementRepository` ; `using System.Linq;` (devenu inutile).

**Conservé** : `IOrderRepository` + `IUnitOfWork` + `INotificationRepository?` (utilisés **uniquement** par
l'encaissement reporté `ExecuteEncashBalanceAsync`) ; machine à états `GetNextStatus()` + propriétés
d'affichage ; `CanAdvanceStatus`/`UpdateWorkflowState` ; `OrderUpdated`. **Le flux d'avancement ne met plus à
jour le statut, ne crée plus de mouvements de stock ni de notification, et n'appelle plus `SaveChanges`
directement.**

---

## 10. Modifications DI

`AddApplication` enregistre désormais, en plus de `RegisterSaleUseCase` et `CreateOrderUseCase` :

```csharp
services.AddScoped<IAdvanceOrderStatusUseCase, AdvanceOrderStatusUseCase>();
```

Portée **Scoped** = même portée que `OpticDbContext` / repositories / `IUnitOfWork` ⇒ même `DbContext`, donc
le `SaveChangesAsync` unique reste atomique (cohérent avec le flux d'origine). `OrdersViewModel` reçoit le use
case par DI et le transmet à `OrderDetailViewModel` (paramètre **obligatoire**). **Aucun autre use case
enregistré.**

---

## 11. Tests ajoutés / adaptés

### 11.1 `MMV.Application.Tests` — `AdvanceOrderStatusUseCaseTests` (6, **vrai SQLite**)

1. avancement nominal `New → ToFabricate` : statut persisté, **aucun** mouvement de stock, notification créée
   (texte exact vérifié : `Title`/`Message`/`EntityId`/`EntityType`/`IsRead`), stock produit inchangé ;
2. `ToFabricate → InProgress` : **un** mouvement `Out` (quantité **positive** `2`, motif
   « Fabrication commande CMD-000200 ») + stock produit décrémenté (`5 → 3`) ;
3. **sans** repository de notifications (`null`) : aucune notification, statut avancé quand même ;
4. commande **introuvable** : `OrderFound == false`, `Order == null`, **aucune** écriture (notifs / mouvements vides) ;
5. commande nulle ⇒ `ArgumentNullException` ;
6. constructeur sans `IOrderRepository` ⇒ `ArgumentNullException`.

> **Enforcement FK désactivé** (`Foreign Keys=False`) comme dans `CreateOrderUseCaseTests` — toujours **vrai
> SQLite** (fichier + schéma réels). La seed crée une **vente parente avec client** (`Order → Sale` est une
> navigation **requise** que `GetWithItemsAsync` inclut en **INNER JOIN** : en production une commande est
> toujours liée à une vente), reproduisant fidèlement le contexte réel du flux.

### 11.2 `MMV.App.Tests` — `OrderDetailViewModelAdvanceDelegationTests` (5)

1. l'avancement **délègue** au use case (espion `ExecuteCount == 1`) + `OrderUpdated` levé + `Order` mis à jour ;
2. `OrderFound == false` ⇒ `ErrorMessage == "Commande introuvable."`, **pas** d'`OrderUpdated`, état inchangé ;
3. exception propagée par le use case → `ErrorMessage` commençant par « Erreur lors du changement de statut : »,
   **pas** d'`OrderUpdated` ;
4. constructeur sans use case → `ArgumentNullException` ;
5. mapping état VM → `AdvanceOrderStatusCommand` (`OrderId`, `CurrentStatus`, `NextStatus`,
   `CustomerDisplayName="Jean Dupont"`, `CurrentStatusDisplay="Nouveau"`, `NextStatusDisplay="À fabriquer"`).

> **Couverture non perdue, déplacée** : la persistance (statut, mouvements de stock, décrément, notification)
> est portée au niveau use case (vrai SQLite), conformément à la stratégie de tests du plan §5. Le flux
> d'origine n'a **aucune** garde anti double-soumission (type `IsSaving`) — aucune n'est ajoutée
> (iso-fonctionnel) ; le bouton reste gardé par `CanAdvanceStatus`.

---

## 12. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `MMV.Application` ne
contient **aucun** type EF. `has-pending-model-changes` reste **false**. Migrations existantes intactes.

---

## 13. Contrôles exécutés (après modification)

| Commande | Résultat |
|---|---|
| `git status --short` | 3 fichiers ` M`, 3 entrées `??` (dossier use case, 2 fichiers tests) |
| `git diff --stat` | 3 fichiers suivis, **+38 / −75** |
| `git diff --check` | ✅ aucune anomalie (seul un avis LF→CRLF bénin) |
| `dotnet restore MMV.sln` | ✅ à jour |
| `dotnet build MMV.sln --no-incremental -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **351** (Domain **223** + App **106** + Application **22**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |
| `dotnet list src/MMV.Application reference` | ✅ `..\MMV.Domain\MMV.Domain.csproj` **(seule)** |
| `dotnet list src/MMV.Application package` | ✅ `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 **(seul)** |

---

## 14. Résultats

| Critère | Valeur observée | Statut |
|---|---|---|
| Build | Succès, 1 avert. `CS1998` préexistant, 0 erreur | ✅ |
| Tests | **351** (223 + 106 + 22), 0 échec (+11 vs baseline) | ✅ |
| Audit NuGet | **0 vulnérabilité** (7 projets) | ✅ |
| `has-pending-model-changes` | **false** | ✅ |
| `MMV.Application` → `MMV.Domain` seul | confirmé | ✅ |
| Aucune migration / aucun modèle EF modifié | confirmé | ✅ |
| Use case existe / interface / Command / Result | confirmé | ✅ |
| `AddApplication` enregistre le use case (Scoped) | confirmé | ✅ |
| VM délègue l'avancement ; statuts / stock / notification préservés | confirmé | ✅ |
| Comportement utilisateur identique | confirmé (déplacement iso-fonctionnel) | ✅ |
| Tests d'architecture Application (P2B-2C) toujours verts | confirmé (6/6, inclus dans les 22) | ✅ |

---

## 15. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les **7** projets, transitifs inclus. Les tests ajoutés réutilisent
des packages déjà présents (xunit, Moq, EF Core Sqlite 8.0.27, **FluentAssertions épinglé 6.12.0** — v7+
commercialement licenciée, pin respecté). `MMV.Application` reste sans dépendance EF/Avalonia.

---

## 16. Warnings résiduels

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` (async sans `await`) | `OrderFormViewModel` ligne 464 (`LoadExistingOrderAsync`) | **Préexistant** (baseline), non touché, hors périmètre. |

**Aucun nouveau warning** introduit par P2B-2E. (La suppression de `using System.Linq;` dans
`OrderDetailViewModel` évite un `using` inutile après le retrait de `CreateStockMovementsForFabrication`.)

---

## 17. Risques résiduels

| # | Risque | Évaluation / mitigation |
|---|---|---|
| 1 | **R-05** | **Réduit** : le 3ᵉ parcours (avancement de statut) ne loge plus la persistance dans l'UI. Encaissement, checklist qualité, édition, suppression restent à migrer (strangler). |
| 2 | **Machine à états conservée dans la VM** | **Tension assumée** avec « migrer les règles d'avancement » : priorité à l'iso-fonctionnalité (la transition pilote l'affichage synchrone) ; le use case porte l'orchestration de persistance (la vraie logique R-05). Évolution possible (centraliser la transition côté Application) hors iso-fonctionnel, différée. |
| 3 | **Fragments de présentation dans la `Command`** | `CustomerDisplayName` / `*StatusDisplay` transitent dans la commande pour préserver le texte de notification au caractère près (App ne peut être référencée par Application). Acceptable et documenté ; alternative (déplacer `StatusEnumToDisplay`) = refonte, écartée. |
| 4 | **Pas de `ITransactionRunner`** | Le flux fait **un seul** `SaveChangesAsync` (déjà atomique) ; introduire une transaction explicite serait une refonte. Choix iso-fonctionnel cohérent avec P2B-2D §6.2. |
| 5 | **Transit d'entité `Order` dans le `Result`** | Toléré pendant la migration (comme `RegisterSaleResult.Sale`) : la VM doit réafficher la commande et propager `OrderUpdated` avec l'entité, à l'identique. Aucun type EF / `IQueryable` exposé. |
| 6 | **`Order.SaleId = 0`** (commande autonome) | **Préexistant**, orthogonal, non corrigé (cf. P2B-2D §16.2). Tests : FK relâchée + vente parente seedée, documenté. |
| 7 | **Résolution Scoped depuis la racine** | Inchangé vs P2B-2C/2D (préexistant) : le use case partage le `DbContext` de portée comme les repos. |

Aucun de ces risques ne touche au réglementaire. **Aucune valeur de gate (TVA, devise, barème, magasin, pays)
introduite.** Périmètre fiscalité / facture / devis / Belgique / Maroc / organisation / magasin / `Money` /
SaaS **non touché**. Numérotation / stock mutation service / services Infrastructure / migrations / seeding /
SQLite lifecycle **non touchés** (réutilisés tels quels).

---

## 18. État Git final

```
git status --short
 M src/MMV.App/ViewModels/OrderDetailViewModel.cs
 M src/MMV.App/ViewModels/OrdersViewModel.cs
 M src/MMV.Application/DependencyInjection.cs
?? src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/
?? tests/MMV.App.Tests/ViewModels/OrderDetailViewModelAdvanceDelegationTests.cs
?? tests/MMV.Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs

git branch --show-current → p2b-architecture
```

**Aucun commit, aucun push** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). `git diff --check` : **0 anomalie**.
Aucun `bin/`/`obj/`, `*.db`/`*.trx`/`*.zip`, secret ou temporaire. Aucune entité Domain, migration,
repository, `DbContext` ou service Infrastructure modifié.

---

## 19. Verdict — GO / NO-GO

| Critère d'acceptation P2B-2E | Statut |
|---|---|
| UseCase `AdvanceOrderStatus` existe (`AdvanceOrderStatusUseCase`) | ✅ |
| Interface du use case existe (`IAdvanceOrderStatusUseCase`) | ✅ |
| Command existe (`AdvanceOrderStatusCommand`) | ✅ |
| Result existe (`AdvanceOrderStatusResult`) | ✅ |
| `AddApplication` enregistre le use case (Scoped) | ✅ |
| `OrderDetailViewModel` délègue le flux ciblé (avancement de statut) | ✅ |
| Transaction dans le use case **si nécessaire** | ✅ (non nécessaire : un seul `SaveChangesAsync` atomique — documenté) |
| Statuts existants préservés | ✅ |
| Mouvements de stock existants préservés (Out, quantité positive, motif, décrément direct) | ✅ |
| Notifications existantes préservées (texte au caractère près) | ✅ |
| Comportement utilisateur identique | ✅ |
| Tests Application ajoutés (vrai SQLite) | ✅ (6) |
| Tests ViewModel adaptés / ajoutés | ✅ (5 délégation/présentation) |
| Tests d'architecture non affaiblis | ✅ (6/6 verts) |
| Build vert | ✅ (1 avert. préexistant) |
| Tests verts (**351**) | ✅ |
| 0 vulnérabilité | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration / aucun modèle EF modifié | ✅ |
| Aucune règle Belgique/Maroc/fiscalité/devis/facture/`Money` | ✅ |
| Rapport complet (20 sections) | ✅ |

### ✅ **P2B-2E = GO DÉFINITIF**

Le flux d'**avancement de statut / réception** d'une commande est extrait **iso-fonctionnellement** de
`OrderDetailViewModel.AdvanceStatusAsync` vers `MMV.Application/UseCases/Orders/AdvanceOrderStatus/`,
réutilisant les repositories existants. `OrderDetailViewModel` **délègue** l'avancement ; statuts, mouvements
de stock de fabrication et notification sont **préservés à l'identique**. Build/tests/audit/EF **verts en
local et en CI distante** (run **#29** `27464267299`, commit `0a76bb9`, branche `p2b-architecture`) ⇒ la gate
« pipeline distant vert » est **levée** (cf. §19 bis). **Aucune** migration, **aucune** règle réglementaire
modifiée.

---

## 19 bis. Validation CI distante (run réel)

Le push du commit `0a76bb9` a **déclenché** le pipeline GitHub Actions via le motif `p2*`.

| Élément | Valeur réelle |
|---|---|
| **Run** | **#29** — id **`27464267299`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27464267299 |
| **Commit testé** | **`0a76bb9`** (`head_sha = 0a76bb992782b0cf2e41f0c44ba28a9e82cec36e`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement / workflow | `push` / `CI` — `Restore / Build / Test / Scan` |
| **Restore** | ✅ **success** |
| **Build** | ✅ **success** |
| **Test** | ✅ **success** (suite **351** confirmée localement) |
| **Audit NuGet** (JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| **Restore .NET tools** | ✅ **success** (`dotnet-ef` 8.0.27) |
| **Check EF Core pending model changes** | ✅ **success** — `has-pending-model-changes` = **false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore, Build,
Test, Audit des packages vulnérables, Restore .NET tools, Check EF Core pending model changes, Post-steps,
Complete job*. **VALIDATION DISTANTE OBTENUE** : la gate roadmap « pipeline distant vert » est **levée** pour
P2B-2E.

---

## 20. Prochaine étape candidate : **P2B-2F**

**P2B-2F — quatrième vertical slice** (Programme 2, strangler). Candidats naturels, sur le même modèle
(use case Application, réutilisation des ports P2A, délégation de la VM, tests use case + délégation) :
- **Encaissement du solde** (`OrderDetailViewModel.ExecuteEncashBalanceAsync` — mise à jour paiement sur la
  vente liée + notification ; bon prochain candidat dans le même écran) ;
- **Édition de commande** (`OrderFormViewModel`, branche `_isEditMode` reportée en P2B-2D) ;
- **Stock manuel** (`StockMovementFormViewModel` — déjà transactionnel, `ITransactionRunner` réel).

> **Ne pas démarrer** sans revue humaine du présent rapport et `TARGET_PHASE_ID = P2B-2F` fourni
> explicitement. **Claude ne lance jamais seul l'étape suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2E`. Commit `0a76bb9` (feat) + commit documentaire (validation CI) **poussés sur autorisation
explicite** (branche `p2b-architecture`, sans force). CI distante **#29** (`27464267299`) **verte** (351
tests, 0 vulnérabilité, `has-pending=false`). **Aucune migration. P2B-2F non commencé.**
