# P2B-2C — Premier vertical slice « Enregistrer une vente en magasin » — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2C**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : extraire le cas d'utilisation « Enregistrer une vente en magasin » de
> [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) vers la couche
> Application (`src/MMV.Application/UseCases/Sales/RegisterSale/`), **sans changement de comportement
> observable**. Déplacement, pas refonte.

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2C` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Prérequis

Phases précédentes **validées (GO définitif)** : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B.

| Élément | Valeur |
|---|---|
| Branche | `p2b-architecture` |
| Dernier commit validé | `7c3324c` (feat P2B-2B) + `d6050d4` (docs P2B-2B CI) |
| Dernier run CI connu | **#23** — id `27449157508` — commit `7c3324c` — **success** |
| Pipeline CI | restore → build → test → audit NuGet → restore .NET tools → `has-pending-model-changes` |

Documents de cadrage lus avant modification (le dépôt prime sur les rapports) :
[adr-application-boundaries](../architecture/adr-application-boundaries.md),
[application-layer-migration-plan](../architecture/application-layer-migration-plan.md),
[application-layer-structure](../architecture/application-layer-structure.md),
[P2B-2A-report](P2B-2A-report.md), [P2B-2B-report](P2B-2B-report.md), ADR P2A
(transaction-idempotency, stock-concurrency, numbering, environments-seeding). Code lu : `SaleFormViewModel`,
`CustomerDetailViewModel`, `CustomersViewModel`, `App.axaml.cs`, ports P2A, interfaces repos, entités
`Sale`/`SaleItem`/`Order`/`OrderItem`/`StockMovement`/`Product`/`DocumentSequence`, tests existants
(`SaleFormViewModelTransactionTests`, `Ef*ServiceTests`).

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → d6050d4 docs(P2B-2B): record application shell CI validation
                            7c3324c feat(P2B-2B): add application layer shell
                            0fca9ae docs(P2B-2A): record EF CI validation
                            32bed23 ci(P2B-2A): check EF pending model changes
                            0815bcf docs(P2B-2A): record CI trigger validation
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ tous les projets à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant, [OrderFormViewModel.cs:458](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458)) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (6 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |

Dépôt propre, suite verte, 0 vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies (**GO baseline**).

---

## 5. Inventaire du flux `SaleFormViewModel` avant extraction

Méthodes concernées : `ExecuteSave()` (garde + orchestration) et `PersistSaleAsync(CancellationToken)`
(corps métier). Le flux dans la ViewModel d'origine :

**Dépendances directes (9 ports/abstractions) injectées dans la VM** : `ISaleRepository`, `IOrderRepository`,
`IProductRepository`, `IPrescriptionRepository`, `IStockMovementRepository`, `IUnitOfWork`,
`ITransactionRunner`, `IStockMutationService`, `INumberSequenceService`.

| Rôle | Détail observé dans `PersistSaleAsync` |
|---|---|
| `ITransactionRunner` | enveloppe **tout** le flux : `RunAsync(PersistSaleAsync)` (commit si succès, rollback total sinon) |
| `INumberSequenceService` | numéro `SALE` (`VTE-…`) en début ; numéro `ORDER` (`CMD-…`) si verres |
| `ISaleRepository` + `IUnitOfWork` | `CreateAsync(sale)` puis `SaveChangesAsync()` (obtenir `SaleId`) |
| Construction `Sale` + `SaleItem` | statut `Delivered` (comptoir) / `AwaitingLenses` (fabrication) ; mapping ItemType `Frame/LensOd/LensOg`→sinon `Accessory` ; paramètres optiques (Sphere/Cylinder/Axis/Addition/Prism/VisualAcuity) |
| Cas **vente comptoir** (`IsCounterSale`) | `EstimatedDelivery = Now` ; **décrément stock** hors verres + `StockMovement` (Out, qty négative) |
| Cas **vente avec verres** | `Order` fournisseur (séquence `ORDER`) + lignes verres uniquement |
| `IStockMutationService` | `DecrementStockAsync(productId, qty)` (atomique conditionnel, P2A-1D) ; exclut `VERRE`/`LENTILLE` |
| `IStockMovementRepository` | `CreateAsync(stockMovement)` (raison `Vente comptoir {SaleNumber} - Client #{CustomerId}`) |
| `SaveChanges` final | `IUnitOfWork.SaveChangesAsync()` (commande + mouvements) |
| Erreurs typées attrapées dans `ExecuteSave` | `InsufficientStockException` → message ; `PersistenceException` → message assaini ; `Exception` → message générique |
| Garde présentation | `IsSaving` (anti double-soumission, P2A-1C) ; panier vide → message ; (ancienne garde « Repository non initialisé ») ; `OrderSaved?.Invoke(this, savedSale)` ; `ClearForm()` |

**Constat** : `PersistSaleAsync` **est** le use case « EnregistrerVente » logé dans l'UI (R-05). L'extraction
reproduit ce comportement **à l'identique** dans la couche Application.

---

## 6. Fichiers créés / modifiés

### 6.1 Créés — couche Application (use case, 5 fichiers)

| Fichier | Rôle |
|---|---|
| [`UseCases/Sales/RegisterSale/RegisterSaleCommand.cs`](../../src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleCommand.cs) | Entrée (DTO) : `CustomerId`, `IsCounterSale`, montants calculés par la VM, `PaymentMethod`, `Notes`, `Lines` |
| [`UseCases/Sales/RegisterSale/RegisterSaleLineCommand.cs`](../../src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleLineCommand.cs) | Ligne (DTO) : `ProductId`, `ItemType` brut, `Quantity`, `UnitPrice` + paramètres optiques OD/OG |
| [`UseCases/Sales/RegisterSale/RegisterSaleResult.cs`](../../src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleResult.cs) | Sortie (DTO) : `SaleId`, `SaleNumber`, `OrderId?`, `OrderNumber?`, `Status`, `FinalAmount`, `RemainingAmount`, `Sale` (transit toléré) |
| [`UseCases/Sales/RegisterSale/IRegisterSaleUseCase.cs`](../../src/MMV.Application/UseCases/Sales/RegisterSale/IRegisterSaleUseCase.cs) | Contrat : `Task<RegisterSaleResult> ExecuteAsync(RegisterSaleCommand, CancellationToken)` |
| [`UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs`](../../src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs) | Orchestration iso-fonctionnelle (ouvre la transaction, réutilise les ports P2A + repos) |

### 6.2 Créés — tests Application (projet + 2 fichiers)

| Fichier | Rôle |
|---|---|
| [`tests/MMV.Application.Tests/MMV.Application.Tests.csproj`](../../tests/MMV.Application.Tests/MMV.Application.Tests.csproj) | `net8.0`, réf. `MMV.Application` + `MMV.Domain` + `MMV.Infrastructure` (tests SQLite) ; `FluentAssertions` épinglé 6.12.0 |
| [`tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs`](../../tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs) | 5 tests d'intégration **vrai SQLite** |
| [`tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs`](../../tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs) | 6 tests d'architecture (contrôle différé depuis P2B-2B) |

### 6.3 Modifiés (6 fichiers)

| Fichier | Changement |
|---|---|
| [`src/MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) | `AddApplication` enregistre `IRegisterSaleUseCase → RegisterSaleUseCase` en portée **Scoped** |
| [`src/MMV.App/ViewModels/SaleFormViewModel.cs`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | délègue au use case ; `PersistSaleAsync` **supprimée** ; 7 dépendances de persistance retirées ; `BuildRegisterSaleCommand` ajouté |
| [`src/MMV.App/ViewModels/CustomerDetailViewModel.cs`](../../src/MMV.App/ViewModels/CustomerDetailViewModel.cs) | constructeur reçoit `IRegisterSaleUseCase` ; 5 dépendances sale-only retirées ; construit `SaleFormViewModel` avec (produits, ordonnances, use case) |
| [`src/MMV.App/ViewModels/CustomersViewModel.cs`](../../src/MMV.App/ViewModels/CustomersViewModel.cs) | constructeur reçoit `IRegisterSaleUseCase` ; 5 dépendances sale-only retirées ; transmet à `CustomerDetailViewModel` |
| [`MMV.sln`](../../MMV.sln) | ajout du projet `MMV.Application.Tests` |
| [`tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs`](../../tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs) | réécrit : tests de **délégation/présentation** (la couverture transactionnelle migre vers Application) |

> **`App.axaml.cs` non modifié** : le composition root appelle déjà `services.AddApplication()` (P2B-2B) ; le
> use case y est enregistré, et `CustomersViewModel` (déjà `AddTransient`) voit sa nouvelle dépendance
> `IRegisterSaleUseCase` résolue automatiquement par DI. Aucune autre adaptation nécessaire.

---

## 7. RegisterSaleCommand / Result / UseCase

**`RegisterSaleCommand`** — porte **uniquement** les données issues de la VM. Les montants
(`TotalAmount`/`FinalAmount`/`RemainingAmount`…) sont **calculés par la VM** (`CalculateFinalAmount`) comme
dans le flux d'origine et transmis tels quels : le use case **ne recalcule pas** (déplacement, pas refonte).
`PaymentMethod` est déjà l'enum (conversion `string → enum` conservée dans la VM, mapping de présentation).
**Aucun** `Money`, **aucune** devise, **aucun** nouveau concept.

**`RegisterSaleResult`** — DTO de sortie. Expose en plus l'entité `Sale` persistée **pendant la migration**
(transit explicitement toléré par l'ADR §10, sans fuite de `DbContext`/`IQueryable`) à seule fin de
préserver, **à l'identique**, l'événement `SaleFormViewModel.OrderSaved` (`EventHandler<Sale>`). À retirer
quand cet événement sera lui-même migré.

**`RegisterSaleUseCase`** — réutilise **les mêmes** ports/repos que le flux d'origine
(`ITransactionRunner`, `INumberSequenceService`, `IStockMutationService`, `ISaleRepository`,
`IOrderRepository`, `IProductRepository`, `IStockMovementRepository`, `IUnitOfWork`), tous **obligatoires**
(constructeur rejette `null`). `ExecuteAsync` **ouvre la transaction** (`_transactionRunner.RunAsync(...)`)
puis exécute le corps **copié** de `PersistSaleAsync` : numéro `SALE` → `Sale` + `SaleItem` (mapping ItemType
identique) → statut `Delivered`/`AwaitingLenses` → `SaveChanges` (SaleId) → si verres : numéro `ORDER` +
`Order` fournisseur → si comptoir : `DecrementStockAsync` (hors verres) + `StockMovement` → `SaveChanges`
final. Exceptions typées P2A **propagées inchangées**.

---

## 8. Modifications de `SaleFormViewModel`

**Retiré** : `ISaleRepository`, `IOrderRepository`, `IStockMovementRepository`, `IUnitOfWork`,
`ITransactionRunner`, `IStockMutationService`, `INumberSequenceService` (7 dépendances) ; méthode
`PersistSaleAsync` ; garde « Repository non initialisé » (devenue sans objet). `using
MMV.Domain.Interfaces.Persistence` et `System.Threading` supprimés.

**Conservé** : `IProductRepository?` + `IPrescriptionRepository?` (chargement d'écran
`InitializeForCustomerAsync` uniquement) ; garde `IsSaving` (anti double-soumission) ; garde panier vide ;
calcul des montants (`CalculateFinalAmount`) ; `ConvertPaymentMethodFromString` (mapping de présentation) ;
événement `OrderSaved` ; `catch` des erreurs typées identiques ; mêmes messages.

**Ajouté** : `IRegisterSaleUseCase` **obligatoire** (rejette `null`) ; `BuildRegisterSaleCommand()` (mapping
état VM → `RegisterSaleCommand`). `ExecuteSave` : `IsSaving` → garde panier → `CalculateFinalAmount` →
`await _registerSaleUseCase.ExecuteAsync(BuildRegisterSaleCommand())` → `OrderSaved(result.Sale)` →
`ClearForm`. **La transaction n'est plus ouverte dans la VM.**

---

## 9. Modifications DI

`AddApplication` (P2B-2B squelette neutre) enregistre désormais :

```csharp
services.AddScoped<IRegisterSaleUseCase, RegisterSaleUseCase>();
```

Portée **Scoped** = même portée que `OpticDbContext` / repos / `ITransactionRunner` /
`INumberSequenceService` / `IStockMutationService` (tous Scoped dans `App.ConfigureServices`) ⇒ **même
`DbContext`, donc même transaction**. La chaîne `CustomersViewModel → CustomerDetailViewModel →
SaleFormViewModel` transmet le use case (paramètre **obligatoire**, pas de fallback non transactionnel,
constructeurs rejettent `null`). **Aucun autre use case enregistré.** Résidu `AddInfrastructure` mort :
inchangé (toujours non appelé) — son nettoyage reste un point différé non comportemental (cf. §16).

---

## 10. Tests ajoutés / adaptés

### 10.1 `MMV.Application.Tests` (nouveau) — 11 tests

`RegisterSaleUseCaseTests` (5, **vrai SQLite temporaire**, jamais InMemory) :
1. vente comptoir (monture) : `VTE-000001`, stock 5→3, `StockMovement` Out −2, aucune commande ;
2. vente avec verres (fabrication) : `VTE-000001` + commande `CMD-000001` (2 lignes), statut
   `AwaitingLenses`, **aucun** décrément de stock ;
3. stock insuffisant : `InsufficientStockException` ⇒ **rollback complet** (0 vente, stock inchangé,
   numéro `SALE` **non consommé** : `DocumentSequences.CurrentValue == 0`) ;
4. commande nulle ⇒ `ArgumentNullException` ;
5. constructeur sans `ITransactionRunner` ⇒ `ArgumentNullException`.

`ApplicationArchitectureTests` (6, **contrôle d'architecture différé depuis P2B-2B**) : par réflexion sur les
assemblies référencés par `MMV.Application` — **référence** `MMV.Domain` (contrôle positif) ; **ne référence
pas** `MMV.Infrastructure`, `MMV.App`, `Avalonia*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite`.

### 10.2 `SaleFormViewModelTransactionTests` (adapté) — 8 tests

Recentrés sur la **présentation/délégation** (espion `IRegisterSaleUseCase`) : (1) délègue + `OrderSaved` ;
(2) panier vide → message, **0** appel ; (3) garde `IsSaving` (double clic ignoré, **1** seul appel) ;
(4) `InsufficientStockException` → message, pas de succès ; (5) `PersistenceException` → message assaini ;
(6) constructeur sans use case → `ArgumentNullException` ; (7) mapping état VM → `RegisterSaleCommand` ;
(8) chemin de production `CustomerDetailViewModel → SaleFormViewModel` transmet le use case.

> **Couverture non perdue, déplacée** : les assertions transactionnelles (transaction, numéro `SALE`,
> décrément de stock, rollback) **migrent au niveau use case** (vrai SQLite), conformément à la stratégie de
> tests du plan de migration §5.

---

## 11. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `MMV.Application` ne
contient **aucun** type EF. `has-pending-model-changes` reste **false**. Les migrations existantes sont
intactes.

---

## 12. Contrôles exécutés (après modification)

| Commande | Résultat |
|---|---|
| `git status --short` | 6 fichiers ` M`, 2 dossiers `??` (`src/MMV.Application/UseCases/`, `tests/MMV.Application.Tests/`) |
| `git diff --stat` | 6 fichiers suivis, **+216 / −491** (hors fichiers non suivis) |
| `git diff --check` | ✅ aucune anomalie (espaces / marqueurs de conflit) |
| `dotnet restore MMV.sln` | ✅ `MMV.Application.Tests` restauré, autres à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **329** (Domain **223** + App **95** + Application **11**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (**7 projets**) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** (« No changes … since the last migration ») |
| `dotnet list src/MMV.Application reference` | ✅ `..\MMV.Domain\MMV.Domain.csproj` **(seule)** |
| `dotnet list src/MMV.Application package` | ✅ `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 **(seul)** |

---

## 13. Résultats

| Critère | Valeur observée | Statut |
|---|---|---|
| Build | Succès, 1 avert. `CS1998` préexistant, 0 erreur | ✅ |
| Tests | **329** (223 + 95 + 11), 0 échec | ✅ |
| Audit NuGet | **0 vulnérabilité** (7 projets) | ✅ |
| `has-pending-model-changes` | **false** | ✅ |
| `MMV.Application` → `MMV.Domain` seul | confirmé | ✅ |
| Aucune migration / aucun modèle EF modifié | confirmé | ✅ |
| Use case ouvre la transaction (pas la VM) | confirmé | ✅ |
| Primitives P2A réutilisées (non réécrites) | confirmé | ✅ |
| Comportement utilisateur identique | confirmé (déplacement iso-fonctionnel) | ✅ |

---

## 14. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les **7** projets, transitifs inclus. Le projet de tests ajouté
réutilise des packages déjà présents (xunit 2.9.3, Microsoft.NET.Test.Sdk 17.12.0, Moq 4.20.72,
Microsoft.EntityFrameworkCore.Sqlite 8.0.27) et **FluentAssertions épinglé 6.12.0** (v7+ commercialement
licenciée — pin respecté). `MMV.Application` reste sans dépendance EF/Avalonia.

---

## 15. Warnings résiduels

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` (async sans `await`) | [`OrderFormViewModel.cs:458`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458) | **Préexistant** (baseline), non modifié, hors périmètre |

**Aucun nouveau warning** introduit par P2B-2C. (Hints d'IDE sur `using` implicites/`static` : préexistants,
non bloquants, hors périmètre.)

---

## 16. Risques résiduels

| # | Risque | Évaluation / mitigation |
|---|---|---|
| 1 | **R-05** | **Réduit** : le 1er parcours (vente) ne loge plus l'orchestration métier dans l'UI. Les autres parcours (commandes, stock, client, ordonnances, atelier) restent à migrer (P2B-2D…2I, strangler). |
| 2 | **Double DI (`AddInfrastructure` mort)** | Inchangé : toujours non appelé, aucune divergence. Nettoyage non comportemental différé (le use case s'appuie sur les enregistrements vivants de `App.ConfigureServices`, pas sur `AddInfrastructure`). |
| 3 | **Entité `Sale` exposée dans `RegisterSaleResult`** | **Transit toléré** (ADR §10) pour préserver `OrderSaved` à l'identique ; **sans** fuite de `DbContext`/`IQueryable`. À retirer à la migration de l'événement. |
| 4 | **Résolution Scoped depuis la racine** | `BuildServiceProvider()` (validateScopes=false) résout les services Scoped depuis la portée racine — **comportement préexistant inchangé** (les repos/runner étaient déjà résolus ainsi par les VM). Le use case partage le même `DbContext` racine que les repos ⇒ transaction correcte. |
| 5 | **R-23** (`UnitOfWork.RollbackAsync` trompeur) | Inchangé, contourné ; assainissement après migration des flux. |
| 6 | **Validation de commande (FluentValidation Application)** | Non ajoutée (le flux d'origine n'en avait pas ; la validation d'entrée reste UI). Évolution compatible ultérieure, hors périmètre iso-fonctionnel. |

Aucun de ces risques ne touche au réglementaire. **Aucune valeur de gate (TVA, devise, barème, magasin,
pays) introduite.** Périmètre fiscalité / facture / devis / Belgique / Maroc / organisation / magasin /
`Money` / SaaS **non touché**. Numérotation / décrément stock / services Infrastructure / migrations /
seeding / SQLite lifecycle **non touchés** (réutilisés tels quels).

---

## 17. État Git final

```
git status --short
 M MMV.sln
 M src/MMV.App/ViewModels/CustomerDetailViewModel.cs
 M src/MMV.App/ViewModels/CustomersViewModel.cs
 M src/MMV.App/ViewModels/SaleFormViewModel.cs
 M src/MMV.Application/DependencyInjection.cs
 M tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs
?? src/MMV.Application/UseCases/Sales/
?? tests/MMV.Application.Tests/

git branch --show-current → p2b-architecture
```

**Aucun commit, aucun push** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). `git diff --check` : **0 anomalie**.
Aucun `bin/`/`obj/`, `*.db`/`*.trx`/`*.zip`, secret ou temporaire. Aucune entité Domain, migration,
repository, `DbContext` ou service Infrastructure modifié.

---

## 17 bis. Validation CI distante (run réel)

Le push du commit `889e663` a **déclenché** le pipeline GitHub Actions via le motif `p2*`.

| Élément | Valeur réelle |
|---|---|
| **Run** | **#25** — id **`27450403328`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27450403328 |
| **Commit testé** | **`889e663`** (`head_sha = 889e663c452085e79efd115297e629d09a86d183`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement / workflow | `push` / `CI` — `Restore / Build / Test / Scan` |
| Runner | `windows-latest` (GitHub-hosted) |
| Durée | ≈ 2 min 38 s (00:12:34 → 00:15:12 UTC) |
| Setup .NET (SDK `global.json`) | ✅ success (step #3) |
| **Restore** (step #5) | ✅ **success** |
| **Build** (step #6) | ✅ **success** |
| **Test** (step #7) | ✅ **success** (suite **329** confirmée localement) |
| **Audit NuGet** (step #8, JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| **Restore .NET tools** (step #9) | ✅ **success** (`dotnet-ef` 8.0.27) |
| **Check EF Core pending model changes** (step #10) | ✅ **success** — `has-pending-model-changes` = **false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore, Build,
Test, Audit des packages vulnérables, Restore .NET tools, Check EF Core pending model changes, Post-steps,
Complete job*. **VALIDATION DISTANTE OBTENUE** : la gate roadmap « pipeline distant vert » est **levée** pour
P2B-2C.

---

## 18. Verdict — GO / NO-GO

| Critère d'acceptation P2B-2C | Statut |
|---|---|
| `RegisterSaleUseCase` existe | ✅ |
| `IRegisterSaleUseCase` existe | ✅ |
| `RegisterSaleCommand` existe | ✅ |
| `RegisterSaleResult` existe | ✅ |
| `AddApplication` enregistre le use case (Scoped) | ✅ |
| `SaleFormViewModel` délègue la sauvegarde au use case | ✅ |
| La transaction est ouverte dans le use case, pas dans la VM | ✅ |
| Primitives P2A réutilisées (non réécrites) | ✅ |
| Comportement utilisateur identique | ✅ |
| Tests Application ajoutés (vrai SQLite) | ✅ (5 + 6 architecture) |
| Tests ViewModel adaptés (couverture déplacée, non perdue) | ✅ (8) |
| Test d'architecture différé ajouté | ✅ |
| Build vert | ✅ (1 avert. préexistant) |
| Tests verts (**329**) | ✅ |
| 0 vulnérabilité | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration / aucun modèle EF modifié | ✅ |
| Aucune règle Belgique/Maroc/fiscalité/devis/facture/`Money` | ✅ |
| Rapport complet (19 sections) | ✅ |
| Commit & push (sur autorisation explicite) | ✅ (`889e663`, branche `p2b-architecture`, sans force) |
| CI distante verte (restore/build/test/audit/tools/EF) | ✅ run **#25** (`27450403328`, commit `889e663`) |

### ✅ **P2B-2C = GO DÉFINITIF**

Le use case « Enregistrer une vente en magasin » est extrait **iso-fonctionnellement** de
`PersistSaleAsync` vers `MMV.Application/UseCases/Sales/RegisterSale/`, réutilisant **exactement** les
primitives P2A et les repositories. `SaleFormViewModel` **délègue** (transaction, numérotation, stock,
mouvements gérés dans le use case) ; la chaîne DI transmet le use case obligatoire jusqu'à la ViewModel.
Build/tests/audit/EF **verts en local et en CI distante** (run #25 `27450403328`, commit `889e663`, branche
`p2b-architecture`) ⇒ la gate « pipeline distant vert » est **levée** (cf. §17 bis). **Aucune** migration,
**aucune** règle métier réglementaire modifiée.

---

## 19. Prochaine étape candidate : **P2B-2D**

**P2B-2D — deuxième vertical slice** (Programme 2, strangler) : migrer le **parcours suivant** (candidat
naturel : **Commandes fournisseur** via `OrderFormViewModel`, ou **Stock** via `StockMovementFormViewModel`)
sur le même modèle : use case Application, réutilisation des ports P2A, délégation de la ViewModel, retrait
de l'accès repo du flux, tests use case (vrai SQLite) + tests de délégation. Le produit reste **utilisable à
chaque étape**.

> **Ne pas démarrer** sans revue humaine du présent rapport et `TARGET_PHASE_ID = P2B-2D` fourni
> explicitement. **Claude ne lance jamais seul l'étape suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2C`. Commit `889e663` + commit documentaire (validation CI) **poussés sur autorisation
explicite** (branche `p2b-architecture`, sans force). **Aucune migration. P2B-2D non commencé.**
