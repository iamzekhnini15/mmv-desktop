# P2B-2F — Quatrième vertical slice « Stock manuel / mouvement de stock » — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2F**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : extraire le flux de **création d'un mouvement manuel de stock** de la boucle par ligne de
> [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) vers la
> couche Application (`src/MMV.Application/UseCases/Stock/CreateStockMovement/`), **sans changement de
> comportement observable**. Déplacement, pas refonte.

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2F` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Prérequis

Phases précédentes **validées (GO définitif)** : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C, P2B-2D, P2B-2E.

| Élément | Valeur |
|---|---|
| Branche | `p2b-architecture` |
| Dernier commit P2B-2E | `bfa1cff` (docs CI) + `0a76bb9` (feat) |
| Dernier run CI connu | **#29** — id `27464267299` — commit `0a76bb9` — **success** — **351** tests |
| Pipeline CI | restore → build → test → audit NuGet → restore .NET tools → `has-pending-model-changes` |

Documents de cadrage lus avant modification (le dépôt prime sur les rapports) :
[adr-application-boundaries](../architecture/adr-application-boundaries.md),
[application-layer-migration-plan](../architecture/application-layer-migration-plan.md),
[application-layer-structure](../architecture/application-layer-structure.md),
[P2B-2B-report](P2B-2B-report.md), [P2B-2C-report](P2B-2C-report.md), [P2B-2D-report](P2B-2D-report.md),
[P2B-2E-report](P2B-2E-report.md), ADR P2A (transaction-idempotency, stock-concurrency, numbering,
environments-seeding). Code lu : `StockMovementFormViewModel`, `StockMovementsViewModel`, `ProductsViewModel`
(chaîne réelle de construction), `ProductDetailViewModel`, `RegisterSaleUseCase` / `AdvanceOrderStatusUseCase`
(modèles de référence), `DependencyInjection`, `App.axaml.cs` (DI), ports P2A
(`ITransactionRunner`, `IStockMutationService`), interfaces repos (`IProductRepository`,
`IStockMovementRepository`, `IUnitOfWork`, `IGenericRepository`), entités `Product`/`StockMovement`,
enum `StockMovementType`, exceptions (`InsufficientStockException`, `PersistenceException`), tests existants
(`StockMovementFormViewModelTests`, `AdvanceOrderStatusUseCaseTests`, `OrderDetailViewModelAdvanceDelegationTests`,
`ApplicationArchitectureTests`).

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → bfa1cff docs(P2B-2E): record order status CI validation
                            0a76bb9 feat(P2B-2E): move order status advancement to application use case
                            975067d docs(P2B-2D): record supplier order CI validation
                            59a782b feat(P2B-2D): move supplier order creation to application use case
                            fd3e33b docs(P2B-2C): record sale use case CI validation
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ tous les projets restaurés |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant, `OrderFormViewModel` ligne 464) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **351** (Domain **223** + App **106** + Application **22**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |

Dépôt propre, suite verte, 0 vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies (**GO baseline**).

---

## 5. Inventaire du flux stock manuel avant extraction

Le flux ciblé est la boucle par ligne de
[`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) (le nom réel
confirmé dans le dépôt). Inventaire vérifié :

| Aspect | Constat (avant) |
|---|---|
| **Quelle VM crée un mouvement manuel** | `StockMovementFormViewModel.SaveAsync` (formulaire **multi-produits** : N lignes `ProductMovementLine`). |
| **Quelle VM ouvre le formulaire** | `StockMovementsViewModel.OnCreateMovementRequested` (construit la `StockMovementFormViewModel`). Elle-même créée par `ProductsViewModel.OnManageStockMovementsRequested`. **Aucune** `StockViewModel` n'existe ; `ProductDetailViewModel` ne touche **pas** ce flux (le dépôt prime sur le prompt). |
| Dépendances injectées (form, avant) | `IStockMovementRepository`, `IProductRepository`, `IUnitOfWork`, `IDialogService`, `ITransactionRunner`, `IStockMutationService`. |
| Repositories utilisés | `IProductRepository` (recharge `GetByIdAsync` + `GetAllAsync` pour la liste déroulante), `IStockMovementRepository` (`CreateAsync`). |
| Ports P2A utilisés | **Oui** : `ITransactionRunner.RunAsync` (frontière transactionnelle) **et** `IStockMutationService.DecrementStockAsync` (décrément sûr) — flux **transactionnel** P2A-1D-R2. |
| Types de mouvements existants | `In` (entrée), `Out` (sortie), `Adjustment` (ajustement) — `StockMovementType`. |
| Sens des quantités | Quantité saisie **toujours positive** (`Quantity ≥ 1`, validée UI). Le `StockMovement.Quantity` enregistré reste **positif** ; le **sens** de l'effet découle du `MovementType`. |
| Effet sur `Product.StockQuantity` | `Out` ⇒ décrément atomique conditionnel via `DecrementStockAsync` (jamais négatif) ; `In` ⇒ `StockQuantity += qty` + `UpdateAsync` ; `Adjustment` ⇒ `StockQuantity = qty` (valeur absolue) + `UpdateAsync`. |
| SaveChanges actuels | **Un** `SaveChangesAsync` par ligne, **à l'intérieur** de `ITransactionRunner.RunAsync` (décrément/incrément + création mouvement = tout ou rien). |
| Erreurs attrapées (par ligne) | `InsufficientStockException` ⇒ `errorCount++`, message contrôlé ; `PersistenceException` ⇒ `errorCount++`, message contrôlé ; `Exception` générique ⇒ `errorCount++` + `Debug.WriteLine` ; produit introuvable (`GetByIdAsync == null`) ⇒ `errorCount++` + `continue` (**sans** exception). |
| Messages utilisateur | Synthèse : « {N} mouvement(s) enregistré(s) avec succès. » / « Aucun mouvement enregistré. » + « {M} erreur(s) détectée(s). » + message contrôlé éventuel ; `ErrorMessage` = message contrôlé ; `_dialogService.ShowInformationAsync("Résultat", …)`. |
| Événements / refresh UI | `MovementSaved?.Invoke` si `successCount > 0` (la `StockMovementsViewModel` recharge alors la liste) ; garde `IsSaving` (anti double-soumission) ; `CanSave` gate le bouton (≥ 1 ligne valide). |
| **Migré en P2B-2F** | La création d'**un** mouvement manuel : rechargement produit, frontière transactionnelle, décrément sûr / incrément / ajustement, création du `StockMovement`, `SaveChanges`. |
| **Reporté** | Boucle multi-lignes, validation UI, comptage succès/erreurs, messages de synthèse, dialogue, garde `IsSaving`, chargement de la liste déroulante (`LoadProductsAsync`) ⇒ restent dans la VM. |

Autres flux du module stock **inventoriés mais hors périmètre** (point de vigilance) : stock automatique de
**vente** (déjà `RegisterSaleUseCase`, P2B-2C), mouvement de **fabrication** de commande (déjà
`AdvanceOrderStatusUseCase`, P2B-2E), **historique** (`StockMovementsListViewModel`), **édition / suppression de
produit** (`ProductFormViewModel`), **alertes de stock**, **inventaire complet** (`InventoryViewModel`).

---

## 6. Décision de périmètre P2B-2F

**Flux migré : `CreateStockMovement` (création d'un mouvement manuel de stock) depuis la boucle par ligne de
`StockMovementFormViewModel.SaveAsync`.**

**Reportés** (documentés, hors périmètre) : stock automatique de vente, fabrication de commande, historique,
édition / suppression de produit, alertes de stock, inventaire complet — voir §5.

Décisions clés iso-fonctionnelles :

1. **`ITransactionRunner` conservé dans le use case.** Contrairement à P2B-2D/2E, le flux d'origine est
   **réellement transactionnel** : chaque ligne effectue plusieurs écritures liées (décrément/incrément du stock
   **puis** création du mouvement) protégées par `ITransactionRunner.RunAsync` autour d'un `SaveChangesAsync`. La
   frontière est donc **déplacée** telle quelle **dans** le use case (P2A-1C, R-23), plus jamais dans la VM.
2. **`IStockMutationService` conservé.** La sortie standard utilise le décrément atomique conditionnel
   (`DecrementStockAsync`) : un stock négatif reste **impossible** (P2A-1D-R2). Sémantique préservée à l'identique.
3. **Granularité : un mouvement par appel.** Le use case crée **un seul** mouvement (une ligne). La VM **conserve**
   la boucle multi-lignes du formulaire multi-produits, le comptage succès/erreurs et les messages de synthèse —
   c'est l'orchestration de **présentation**, pas la logique métier. La VM appelle le use case **une fois par
   ligne valide** (comportement utilisateur identique).
4. **Quantité positive + motif préservés.** `StockMovement.Quantity` reste positif (sens porté par le type) ; le
   motif (`Reason`) est **calculé par la VM** (note saisie, ou libellé par défaut « Mouvement {type} » utilisant
   la chaîne d'UI) et transmis tel quel, afin de préserver le motif enregistré **au caractère près**.
5. **Produit introuvable = résultat, pas exception.** `GetByIdAsync == null` renvoie
   `CreateStockMovementResult { ProductFound = false }` ; la VM compte la ligne en erreur et passe à la suivante
   (reproduit l'ancien `continue`, **sans** exception).
6. **Chaîne de wiring réelle adaptée.** Le prompt nommait `StockViewModel` / `ProductDetailViewModel` ; le dépôt
   réel injecte le formulaire via `ProductsViewModel → StockMovementsViewModel → StockMovementFormViewModel`. Ces
   **deux** parents réels sont adaptés « uniquement pour injecter / transmettre le use case » (cf. consigne « le
   dépôt prime »). `ProductDetailViewModel` n'est **pas** touchée (elle ne participe pas à ce flux).

---

## 7. Fichiers créés / modifiés

### 7.1 Créés — couche Application (use case, 4 fichiers)

| Fichier | Rôle |
|---|---|
| [`UseCases/Stock/CreateStockMovement/CreateStockMovementCommand.cs`](../../src/MMV.Application/UseCases/Stock/CreateStockMovement/CreateStockMovementCommand.cs) | Entrée (DTO) : `ProductId`, `MovementType`, `Quantity`, `Reason` |
| [`UseCases/Stock/CreateStockMovement/CreateStockMovementResult.cs`](../../src/MMV.Application/UseCases/Stock/CreateStockMovement/CreateStockMovementResult.cs) | Sortie (DTO) : `ProductFound`, `StockMovementId`, `ProductId`, `MovementType`, `Quantity`, `NewStockQuantity` |
| [`UseCases/Stock/CreateStockMovement/ICreateStockMovementUseCase.cs`](../../src/MMV.Application/UseCases/Stock/CreateStockMovement/ICreateStockMovementUseCase.cs) | Contrat : `Task<CreateStockMovementResult> ExecuteAsync(CreateStockMovementCommand, CancellationToken)` |
| [`UseCases/Stock/CreateStockMovement/CreateStockMovementUseCase.cs`](../../src/MMV.Application/UseCases/Stock/CreateStockMovement/CreateStockMovementUseCase.cs) | Orchestration iso-fonctionnelle (réutilise `IProductRepository` + `IStockMovementRepository` + `IUnitOfWork` + `ITransactionRunner` + `IStockMutationService`) |

### 7.2 Créés — tests (1 fichier)

| Fichier | Rôle |
|---|---|
| [`tests/MMV.Application.Tests/UseCases/Stock/CreateStockMovementUseCaseTests.cs`](../../tests/MMV.Application.Tests/UseCases/Stock/CreateStockMovementUseCaseTests.cs) | **8** tests d'intégration **vrai SQLite** |

### 7.3 Modifiés (4 fichiers)

| Fichier | Changement |
|---|---|
| [`src/MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) | `AddApplication` enregistre `ICreateStockMovementUseCase → CreateStockMovementUseCase` (Scoped) |
| [`src/MMV.App/ViewModels/StockMovementFormViewModel.cs`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) | `SaveAsync` **délègue** la création par ligne au use case ; orchestration de persistance **supprimée** (transaction, décrément/incrément, `CreateAsync`, `SaveChanges`) ; deps réduites à `IProductRepository` (liste) + `IDialogService` + `ICreateStockMovementUseCase` (obligatoire) ; `IStockMovementRepository`/`IUnitOfWork`/`ITransactionRunner`/`IStockMutationService` retirés |
| [`src/MMV.App/ViewModels/StockMovementsViewModel.cs`](../../src/MMV.App/ViewModels/StockMovementsViewModel.cs) | reçoit `ICreateStockMovementUseCase` par DI ; le transmet au formulaire ; `IUnitOfWork`/`ITransactionRunner`/`IStockMutationService` retirés (n'étaient transmis qu'au formulaire) ; `IStockMovementRepository`/`IProductRepository` conservés (liste) |
| [`src/MMV.App/ViewModels/ProductsViewModel.cs`](../../src/MMV.App/ViewModels/ProductsViewModel.cs) | reçoit `ICreateStockMovementUseCase` par DI ; le transmet à `StockMovementsViewModel` ; `ITransactionRunner`/`IStockMutationService` retirés (n'étaient transmis qu'à `StockMovementsViewModel`) |

### 7.4 Réécrits — tests (1 fichier)

| Fichier | Changement |
|---|---|
| [`tests/MMV.App.Tests/ViewModels/StockMovementFormViewModelTests.cs`](../../tests/MMV.App.Tests/ViewModels/StockMovementFormViewModelTests.cs) | Anciens tests de persistance (vrai SQLite via la VM) **remplacés** par **7** tests de **délégation / présentation** (espion de use case + `Mock<IProductRepository>`). Couverture de persistance **déplacée** vers `CreateStockMovementUseCaseTests`. |

> **`App.axaml.cs` et `MMV.sln`/`.csproj` non modifiés** : le composition root appelle déjà
> `services.AddApplication()` (P2B-2B) ; `ProductsViewModel` (déjà `AddTransient`) voit sa nouvelle dépendance
> `ICreateStockMovementUseCase` (Scoped) résolue automatiquement, et ses anciennes dépendances
> `ITransactionRunner`/`IStockMutationService` (toujours enregistrées, utilisées ailleurs) simplement plus
> injectées ici. Le projet `MMV.Application.Tests` existe déjà (P2B-2C) et globalise le nouveau fichier.

---

## 8. Command / Result / UseCase

**`CreateStockMovementCommand`** — porte l'état d'**une** ligne du formulaire : `ProductId`, `MovementType`
(enum déjà résolu par la VM), `Quantity` (positive), `Reason` (déjà résolu : note ou libellé par défaut). Aucun
montant, aucun `Money`, aucune devise, aucune règle pays/fiscalité.

**`CreateStockMovementResult`** — `ProductFound` (false ⇒ ligne comptée en erreur, sans exception),
`StockMovementId`, `ProductId`, `MovementType`, `Quantity`, `NewStockQuantity` (nouveau stock après application,
exposé pour usage futur ; **aucune** entité Domain ni `IQueryable` exposée).

**`CreateStockMovementUseCase`** — dépend de `IStockMovementRepository` + `IProductRepository` + `IUnitOfWork` +
`ITransactionRunner` + `IStockMutationService` (**tous obligatoires**, constructeur rejette `null`).
`ExecuteAsync` reproduit **à l'identique** : recharge `GetByIdAsync` ; si null ⇒ `ProductFound = false` ; sinon
construit le `StockMovement` (quantité positive, motif repris) puis, **dans** `ITransactionRunner.RunAsync` :
`Out` ⇒ `DecrementStockAsync` (jamais négatif) ; `In` ⇒ `StockQuantity += qty` + `UpdateAsync` ; `Adjustment` ⇒
`StockQuantity = qty` + `UpdateAsync` ; `CreateAsync(movement)` ; **un** `SaveChangesAsync`. Refus métier
(`InsufficientStockException`) et erreurs techniques (`PersistenceException`) **propagés inchangés**.

---

## 9. Modifications de `StockMovementFormViewModel`

**Ajouté** : `ICreateStockMovementUseCase` **obligatoire** (rejette `null`).

**Modifié** : la boucle de `SaveAsync` construit, pour chaque ligne valide, une `CreateStockMovementCommand` (à
partir de `line.Product.ProductId`, `Enum.Parse<StockMovementType>(line.MovementType)`, `line.Quantity`, motif
résolu), appelle `await _createStockMovementUseCase.ExecuteAsync(command)`, puis mappe : `ProductFound == false`
⇒ `errorCount++` + `continue` ; sinon `successCount++`. Le `try/catch` par ligne
(`InsufficientStockException`/`PersistenceException`/`Exception`), le comptage, les messages de synthèse, le
dialogue, `MovementSaved` et la garde `IsSaving` sont **inchangés**.

**Supprimé** : rechargement produit + `Enum.Parse` interne + construction du `StockMovement` + `RunAsync` +
`switch` décrément/incrément/ajustement + `CreateAsync` + `SaveChangesAsync` (tout déplacé dans le use case) ;
dépendances `IStockMovementRepository`, `IUnitOfWork`, `ITransactionRunner`, `IStockMutationService` ; `using
MMV.Domain.Interfaces.Persistence;` (devenu inutile).

**Conservé** : `IProductRepository` (`LoadProductsAsync` → liste déroulante), `IDialogService` ; validation UI
(`IsValid`/`CanSave`), `IsSaving`, `MovementLines`, `MovementTypes`, `AddLine`/`RemoveLine`/`Cancel`, événements
`MovementSaved`/`CancelRequested`. **Le flux de création ne recharge plus le produit, n'ouvre plus de
transaction, ne modifie plus directement `Product.StockQuantity`, ne crée plus le `StockMovement` et n'appelle
plus `SaveChanges`.**

---

## 10. Modifications DI

`AddApplication` enregistre désormais, en plus de `RegisterSaleUseCase`, `CreateOrderUseCase` et
`AdvanceOrderStatusUseCase` :

```csharp
services.AddScoped<ICreateStockMovementUseCase, CreateStockMovementUseCase>();
```

Portée **Scoped** = même portée que `OpticDbContext` / repositories / `IUnitOfWork` / `ITransactionRunner` /
`IStockMutationService` ⇒ même `DbContext`, donc la frontière transactionnelle (décrément/incrément + mouvement)
reste **atomique**, à l'identique du flux d'origine. `ProductsViewModel` reçoit le use case par DI et le transmet
le long de la chaîne `→ StockMovementsViewModel → StockMovementFormViewModel` (paramètre **obligatoire**).
**Aucun autre use case enregistré.**

---

## 11. Tests ajoutés / adaptés

### 11.1 `MMV.Application.Tests` — `CreateStockMovementUseCaseTests` (8, **vrai SQLite**)

1. **In** : stock incrémenté (`5 → 9`), mouvement créé, **motif préservé** (`Reason`), `NewStockQuantity = 9` ;
2. **Out** suffisant : décrément sûr (`10 → 7`), mouvement `Out` (**quantité positive `3`**), motif préservé ;
3. **Out** exactement égal au stock : atteint **zéro** (`5 → 0`) ;
4. **Out** supérieur au stock : `InsufficientStockException` levée, **rollback total** (stock inchangé `5`,
   **jamais négatif**, **aucun** mouvement persisté) — prouve l'atomicité transactionnelle ;
5. **Adjustment** : stock fixé en **valeur absolue** (`5 → 8`), mouvement créé, motif préservé ;
6. **Produit introuvable** : `ProductFound == false`, `StockMovementId == 0`, **aucune** écriture, **aucune**
   exception ;
7. commande **nulle** ⇒ `ArgumentNullException` ;
8. constructeur **sans** `ITransactionRunner` **et** sans `IStockMutationService` ⇒ `ArgumentNullException`.

> **Vrai SQLite** (fichier + schéma réels), **jamais** InMemory. FK **enforcées** (un mouvement référence un
> produit réel, le produit un fournisseur réel) — contexte fidèle au flux manuel de production. Ports P2A réels
> (`EfTransactionRunner`, `EfStockMutationService`) partageant un unique `OpticDbContext`.

### 11.2 `MMV.App.Tests` — `StockMovementFormViewModelTests` réécrits (7, délégation / présentation)

1. ligne valide ⇒ **délègue** (`ExecuteCount == 1`), `MovementSaved` levé, **mapping ligne → commande** vérifié
   (`ProductId`, `MovementType`, `Quantity`, `Reason`), message de succès exact ;
2. **sans note** ⇒ motif par défaut « Mouvement {type} » (préservé) ;
3. `InsufficientStockException` propagée par le use case ⇒ message **contrôlé** dans `ErrorMessage` + dialogue,
   **pas** de `MovementSaved` ;
4. **produit introuvable** (`ProductFound == false`) ⇒ ligne comptée en erreur, **pas** de `MovementSaved`,
   message de synthèse exact ;
5. **plusieurs lignes valides** ⇒ une délégation par ligne (`ExecuteCount == 2`) + message de synthèse ;
6. constructeur **sans** use case ⇒ `ArgumentNullException` ;
7. garde **`IsSaving`** remise à `false` en fin de flux.

> **Couverture non perdue, déplacée** : la persistance (incrément, décrément sûr, ajustement, rollback, motif,
> jamais négatif) est portée au niveau use case (vrai SQLite), conformément à la stratégie de tests du plan §5.
> La VM est désormais testée par **espion** (`ICreateStockMovementUseCase`) + `Mock<IProductRepository>`, sans
> toucher la persistance.

### 11.3 Test d'architecture

`ApplicationArchitectureTests` (P2B-2C, 6 tests) **inchangés et toujours verts** : `MMV.Application` ne référence
toujours que `MMV.Domain` (ni Infrastructure, ni App, ni Avalonia/EF/Sqlite). Non affaiblis.

---

## 12. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `MMV.Application` ne
contient **aucun** type EF. `has-pending-model-changes` reste **false**. Migrations existantes intactes.

---

## 13. Contrôles exécutés (après modification)

| Commande | Résultat |
|---|---|
| `git status --short` | 5 fichiers ` M`, 2 entrées `??` (dossier use case, dossier tests use case) |
| `git diff --stat` | 5 fichiers suivis, **+174 / −283** |
| `git diff --check` | ✅ aucune anomalie (seuls des avis LF→CRLF bénins) |
| `dotnet restore MMV.sln` | ✅ à jour |
| `dotnet build MMV.sln --no-restore --no-incremental -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **360** (Domain **223** + App **107** + Application **30**), 0 échec |
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
| Tests | **360** (223 + 107 + 30), 0 échec (+9 vs baseline) | ✅ |
| Audit NuGet | **0 vulnérabilité** (7 projets) | ✅ |
| `has-pending-model-changes` | **false** | ✅ |
| `MMV.Application` → `MMV.Domain` seul | confirmé | ✅ |
| Aucune migration / aucun modèle EF modifié | confirmé | ✅ |
| Use case existe / interface / Command / Result | confirmé | ✅ |
| `AddApplication` enregistre le use case (Scoped) | confirmé | ✅ |
| VM délègue la création par ligne ; stock final / mouvement préservés | confirmé | ✅ |
| Transaction dans le use case (flux multi-écritures) | confirmé (`ITransactionRunner` déplacé dans le use case) | ✅ |
| Comportement utilisateur identique | confirmé (déplacement iso-fonctionnel) | ✅ |
| Tests d'architecture Application (P2B-2C) toujours verts | confirmé (6/6, inclus dans les 30) | ✅ |

---

## 15. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les **7** projets, transitifs inclus. Les tests ajoutés réutilisent des
packages déjà présents (xunit, Moq 4.20.72, EF Core Sqlite 8.0.27, **FluentAssertions épinglé 6.12.0** — v7+
commercialement licenciée, pin respecté). `MMV.Application` reste sans dépendance EF/Avalonia.

---

## 16. Warnings résiduels

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` (async sans `await`) | `OrderFormViewModel` ligne 464 (`LoadExistingOrderAsync`) | **Préexistant** (baseline), non touché, hors périmètre. |

**Aucun nouveau warning** introduit par P2B-2F. (Les `using` devenus inutiles —
`MMV.Domain.Interfaces.Persistence` dans `StockMovementFormViewModel`/`StockMovementsViewModel`/`ProductsViewModel`
— ont été retirés après le retrait des ports P2A de ces VMs.)

---

## 17. Risques résiduels

| # | Risque | Évaluation / mitigation |
|---|---|---|
| 1 | **R-05** | **Réduit** : le 4ᵉ parcours (stock manuel) ne loge plus la persistance dans l'UI. Historique, édition / suppression produit, alertes, inventaire restent à migrer (strangler). |
| 2 | **Boucle multi-lignes conservée dans la VM** | **Choix iso-fonctionnel assumé** : le use case crée **un** mouvement (cohésion) ; la boucle, le comptage et les messages de synthèse sont de la **présentation** (formulaire multi-produits). Centraliser la boucle côté Application serait une refonte, écartée. |
| 3 | **`ITransactionRunner` dans le use case** | Le flux fait **plusieurs écritures liées par ligne** (décrément/incrément + mouvement) : la frontière transactionnelle est **réellement nécessaire** et **déplacée** telle quelle (≠ P2B-2D/2E qui n'avaient qu'un `SaveChanges`). Décrément sûr (`IStockMutationService`) préservé ⇒ stock jamais négatif. |
| 4 | **Produit introuvable = `ProductFound=false`** | Reproduit l'ancien `continue` (sans exception). La VM compte la ligne en erreur, à l'identique. Acceptable et testé. |
| 5 | **`NewStockQuantity` pour `Out`** | Calculé comme `stock lu − quantité` (le décrément SQL atomique ayant réussi). Valeur **non utilisée** par la VM actuelle (exposée pour usage futur) ; les tests vérifient le stock réel en base (`AsNoTracking`). Sans impact comportemental. |
| 6 | **Chaîne de wiring élargie** (`ProductsViewModel` adaptée) | Le dépôt réel injecte le formulaire via `ProductsViewModel → StockMovementsViewModel`. Les **deux** parents ont été adaptés « uniquement pour transmettre le use case » ; deux ports P2A devenus morts y ont été retirés (même esprit que le retrait d'`IStockMovementRepository` d'`OrdersViewModel` en P2B-2E). Aucun test ne construisait ces VMs directement. |
| 7 | **Résolution Scoped depuis la racine** | Inchangé vs P2B-2C/2D/2E (préexistant) : le use case partage le `DbContext` de portée comme les repos. |

Aucun de ces risques ne touche au réglementaire. **Aucune valeur de gate (TVA, devise, barème, magasin, pays)
introduite.** Périmètre fiscalité / facture / devis / Belgique / Maroc / organisation / magasin / `Money` / SaaS
**non touché**. Numérotation / stock mutation service / services Infrastructure / migrations / seeding / SQLite
lifecycle **non touchés** (réutilisés tels quels).

---

## 18. État Git final

```
git status --short
 M src/MMV.App/ViewModels/ProductsViewModel.cs
 M src/MMV.App/ViewModels/StockMovementFormViewModel.cs
 M src/MMV.App/ViewModels/StockMovementsViewModel.cs
 M src/MMV.Application/DependencyInjection.cs
 M tests/MMV.App.Tests/ViewModels/StockMovementFormViewModelTests.cs
?? src/MMV.Application/UseCases/Stock/
?? tests/MMV.Application.Tests/UseCases/Stock/

git branch --show-current → p2b-architecture
```

**Aucun commit, aucun push** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). `git diff --check` : **0 anomalie**.
Aucun `bin/`/`obj/`, `*.db`/`*.trx`/`*.zip`, secret ou temporaire. Aucune entité Domain, migration, repository,
`DbContext` ou service Infrastructure modifié.

---

## 19. Verdict — GO / NO-GO

| Critère d'acceptation P2B-2F | Statut |
|---|---|
| UseCase `CreateStockMovement` existe (`CreateStockMovementUseCase`) | ✅ |
| Interface du use case existe (`ICreateStockMovementUseCase`) | ✅ |
| Command existe (`CreateStockMovementCommand`) | ✅ |
| Result existe (`CreateStockMovementResult`) | ✅ |
| `AddApplication` enregistre le use case (Scoped) | ✅ |
| `StockMovementFormViewModel` délègue le flux ciblé (création manuelle) | ✅ |
| Transaction dans le use case (flux multi-écritures) | ✅ (`ITransactionRunner` déplacé) |
| Stock final identique au comportement existant | ✅ |
| Mouvement de stock identique (type, quantité positive, motif, effet produit) | ✅ |
| Comportement utilisateur identique | ✅ |
| Tests Application ajoutés (vrai SQLite) | ✅ (8) |
| Tests ViewModel adaptés (délégation / présentation) | ✅ (7) |
| Tests d'architecture non affaiblis | ✅ (6/6 verts) |
| Build vert | ✅ (1 avert. préexistant) |
| Tests verts (**360**) | ✅ |
| 0 vulnérabilité | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration / aucun modèle EF modifié | ✅ |
| Aucune règle Belgique/Maroc/fiscalité/devis/facture/`Money` | ✅ |
| Rapport complet (20 sections) | ✅ |

### ✅ **P2B-2F = GO LOCAL**

Le flux de **création d'un mouvement manuel de stock** est extrait **iso-fonctionnellement** de la boucle par
ligne de `StockMovementFormViewModel.SaveAsync` vers `MMV.Application/UseCases/Stock/CreateStockMovement/`,
réutilisant les repositories et ports P2A existants. `StockMovementFormViewModel` **délègue** la création (la
frontière transactionnelle et le décrément sûr vivent désormais dans le use case) ; stock final, mouvement,
quantités, motif et messages utilisateur sont **préservés à l'identique**. Build / tests / audit / EF **verts en
local**.

> **Gate roadmap « pipeline distant vert »** : la validation CI distante (push) **n'a pas** été exécutée
> (`ALLOW_PUSH=false`). Le GO ci-dessus est **local** ; la levée de la gate distante reste à confirmer après push
> autorisé (motif `p2*`), comme aux phases précédentes.

---

## 20. Prochaine étape candidate : **P2B-2G**

**P2B-2G — cinquième vertical slice** (Programme 2, strangler). Candidats naturels, sur le même modèle (use case
Application, réutilisation des ports P2A, délégation de la VM, tests use case + délégation) :
- **Encaissement du solde** (`OrderDetailViewModel.ExecuteEncashBalanceAsync` — reporté depuis P2B-2E) ;
- **Édition de commande** (`OrderFormViewModel`, branche `_isEditMode` reportée en P2B-2D) ;
- **Ajustement d'inventaire complet** (`InventoryViewModel` — crée aussi des `StockMovement`).

> **Ne pas démarrer** sans revue humaine du présent rapport et `TARGET_PHASE_ID = P2B-2G` fourni explicitement.
> **Claude ne lance jamais seul l'étape suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2F`. **Aucun commit, aucun push** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). Build / tests (**360**) /
audit (**0 vulnérabilité**) / `has-pending=false` **verts en local**. **Aucune migration. P2B-2G non commencé.**
