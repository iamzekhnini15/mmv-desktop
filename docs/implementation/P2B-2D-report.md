# P2B-2D — Deuxième vertical slice « Créer une commande fournisseur » — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2D**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : extraire le flux de **création** d'une commande fournisseur de
> [`OrderFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) vers la couche
> Application (`src/MMV.Application/UseCases/Orders/CreateOrder/`), **sans changement de comportement
> observable**. Déplacement, pas refonte.

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2D` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Prérequis

Phases précédentes **validées (GO définitif)** : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2, P2B-2B, P2B-2C.

| Élément | Valeur |
|---|---|
| Branche | `p2b-architecture` |
| Dernier commit P2B-2C | `889e663` (feat) + `fd3e33b` (docs CI) |
| Dernier run CI connu | **#25** — id `27450403328` — commit `889e663` — **success** — **329** tests |
| Pipeline CI | restore → build → test → audit NuGet → restore .NET tools → `has-pending-model-changes` |

Documents de cadrage lus avant modification (le dépôt prime sur les rapports) :
[adr-application-boundaries](../architecture/adr-application-boundaries.md),
[application-layer-migration-plan](../architecture/application-layer-migration-plan.md),
[application-layer-structure](../architecture/application-layer-structure.md),
[P2B-2B-report](P2B-2B-report.md), [P2B-2C-report](P2B-2C-report.md), ADR P2A
(transaction-idempotency, stock-concurrency, numbering, environments-seeding). Code lu : `OrderFormViewModel`,
`OrderDetailViewModel`, `OrdersViewModel`, `RegisterSale*` (modèle de référence), `App.axaml.cs`,
`DependencyInjection`, ports P2A, interfaces repos (`IOrderRepository`, `IUnitOfWork`…), entités
`Order`/`OrderItem`/`Product`, configs EF `OrderConfiguration`/`OrderItemConfiguration`, tests existants
(`OrderFormViewModelNumberingTests`, `RegisterSaleUseCaseTests`).

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → fd3e33b docs(P2B-2C): record sale use case CI validation
                            889e663 feat(P2B-2C): move sale registration to application use case
                            d6050d4 docs(P2B-2B): record application shell CI validation
                            7c3324c feat(P2B-2B): add application layer shell
                            0fca9ae docs(P2B-2A): record EF CI validation
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ tous les projets restaurés |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant, `OrderFormViewModel` `LoadExistingOrderAsync`) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **329** (Domain **223** + App **95** + Application **11**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |

Dépôt propre, suite verte, 0 vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies (**GO baseline**).

---

## 5. Inventaire du flux Commandes avant extraction

Trois ViewModels couvrent le module Commandes / Atelier. Inventaire vérifié dans le dépôt :

### 5.1 `OrderFormViewModel` — création / édition d'une commande

| Aspect | Constat |
|---|---|
| Dépendances injectées | `IOrderRepository`, `ICustomerRepository`, `IProductRepository`, `IPrescriptionRepository`, `IUnitOfWork`, `INumberSequenceService` |
| **Numérotation** | numéro `ORDER` (`CMD-…`) attribué **à l'ouverture du formulaire** (`InitializeAsync` → `GenerateOrderNumberAsync` → `NextNumberAsync(ORDER)`), **affiché en lecture seule** (`OrderFormView.axaml` : `TextBox Text="{Binding OrderNumber}" IsReadOnly`). Comportement P2A-1E, **testé** (`OrderFormViewModelNumberingTests`). |
| **Création** (`SaveAsync`, branche `!_isEditMode`) | `new Order()` (⚠️ `SaleId` laissé à 0), `OrderNumber` = numéro déjà attribué, `EstimatedDelivery`, `Notes` (blanc→null), `OrderDate = UtcNow`, `Status = New`, construction des `OrderItem` (lignes valides ; optique conservée pour verres), `CreateAsync` + **un seul** `SaveChangesAsync`. Pas de transaction, pas de stock, pas de produit chargé. |
| **Édition** (`SaveAsync`, branche `_isEditMode`) | réutilise `_existingOrder`, met à jour champs + reconstruit les lignes, `UpdateAsync` + `SaveChangesAsync`. |
| Erreurs | `catch (Exception)` générique → `ErrorMessage`. |
| Garde présentation | `IsSaving` (anti double-soumission) ; `CanSave` (client + ≥1 ligne valide + `!IsSaving`). |
| Événement | `OrderSaved` (`EventHandler`, **sans charge utile**). |

### 5.2 `OrderDetailViewModel` — workflow d'une commande existante (NON migré)

Actions métier distinctes inventoriées (toutes **reportées**) : `AdvanceStatusAsync` (changement de statut +
mouvements de stock de fabrication + notification), `ExecuteEncashBalanceAsync` (encaissement du solde sur la
vente liée), checklist contrôle qualité. Dépendances : `IOrderRepository`, `IUnitOfWork`,
`IStockMovementRepository`, `INotificationRepository?`.

### 5.3 `OrdersViewModel` — coordinateur de navigation

Construit `OrderFormViewModel` (création **et** édition), `OrderDetailViewModel`, Kanban, fiche fabrication ;
gère la suppression (`DeleteAsync` + `SaveChangesAsync`). Injecté par DI (`AddTransient`).

| Sujet | Réponse |
|---|---|
| Quelle VM **crée** une commande | `OrderFormViewModel` (branche création) → **migrée P2B-2D** |
| Quelle VM **modifie** une commande | `OrderFormViewModel` (édition) → **reporté** |
| Quelle VM **reçoit** une commande | `OrderDetailViewModel.AdvanceStatusAsync` (réception/statuts) → **reporté** |
| Quelle VM **change les statuts** | `OrderDetailViewModel` → **reporté** |
| Repositories utilisés (création) | `IOrderRepository` + `IUnitOfWork` |
| Services P2A utilisés (création) | `INumberSequenceService` (à l'**ouverture**, pas au save) ; **pas** de `ITransactionRunner`, **pas** de `IStockMutationService` |
| `SaveChanges` actuel (création) | **un seul** `SaveChangesAsync` (écriture atomique) |

---

## 6. Décision de périmètre P2B-2D

**Flux migré : `CreateOrder` (création d'une commande fournisseur depuis `OrderFormViewModel`).**

**Reportés** (documentés, hors périmètre, conformément au point de vigilance) :
- **Édition** d'une commande existante (`SaveAsync` branche `_isEditMode`) — reste dans la VM, inchangée.
- **Changement de statut / réception / fabrication** (`OrderDetailViewModel.AdvanceStatusAsync`).
- **Encaissement du solde** (`OrderDetailViewModel.ExecuteEncashBalanceAsync`).
- **Suppression** (`OrdersViewModel.OnDeleteOrderRequested`).

Décisions clés iso-fonctionnelles :

1. **Numérotation laissée à l'ouverture du formulaire.** Le numéro `ORDER` est attribué dans
   `InitializeAsync` et **affiché en lecture seule** (comportement P2A-1E **testé**). Le déplacer au
   `SaveAsync` viderait le champ « Numéro » à l'ouverture ⇒ **changement de comportement utilisateur** et
   rupture de `OrderFormViewModelNumberingTests`. Le numéro déjà attribué est donc **transmis** au use case
   via `CreateOrderCommand.OrderNumber` et persisté tel quel. *(Tension assumée avec « pas de numérotation
   directe dans la VM » : priorité donnée à l'iso-fonctionnalité et au dépôt réel ; cf. §16.)*
2. **Pas de `ITransactionRunner`.** Le flux d'origine fait **une seule** écriture atomique
   (`CreateAsync` + un `SaveChangesAsync`) ; aucune séquence multi-étapes à protéger ⇒ aucun runner
   introduit (déplacement, pas refonte).
3. **`SaleId` laissé à 0** (comme l'original) — voir la **découverte** §16.

---

## 7. Fichiers créés / modifiés

### 7.1 Créés — couche Application (use case, 5 fichiers)

| Fichier | Rôle |
|---|---|
| [`UseCases/Orders/CreateOrder/CreateOrderCommand.cs`](../../src/MMV.Application/UseCases/Orders/CreateOrder/CreateOrderCommand.cs) | Entrée (DTO) : `OrderNumber` (déjà attribué), `EstimatedDelivery`, `Notes`, `Lines` |
| [`UseCases/Orders/CreateOrder/CreateOrderLineCommand.cs`](../../src/MMV.Application/UseCases/Orders/CreateOrder/CreateOrderLineCommand.cs) | Ligne (DTO) : `ProductId`, `ItemType`, `Quantity`, `UnitPrice`, optique (Sphere/Cylinder/Axis/Addition) |
| [`UseCases/Orders/CreateOrder/CreateOrderResult.cs`](../../src/MMV.Application/UseCases/Orders/CreateOrder/CreateOrderResult.cs) | Sortie (DTO) : `OrderId`, `OrderNumber`, `Status`, `EstimatedDelivery` |
| [`UseCases/Orders/CreateOrder/ICreateOrderUseCase.cs`](../../src/MMV.Application/UseCases/Orders/CreateOrder/ICreateOrderUseCase.cs) | Contrat : `Task<CreateOrderResult> ExecuteAsync(CreateOrderCommand, CancellationToken)` |
| [`UseCases/Orders/CreateOrder/CreateOrderUseCase.cs`](../../src/MMV.Application/UseCases/Orders/CreateOrder/CreateOrderUseCase.cs) | Orchestration iso-fonctionnelle (réutilise `IOrderRepository` + `IUnitOfWork`) |

### 7.2 Créés — tests (2 fichiers)

| Fichier | Rôle |
|---|---|
| [`tests/MMV.Application.Tests/UseCases/Orders/CreateOrderUseCaseTests.cs`](../../tests/MMV.Application.Tests/UseCases/Orders/CreateOrderUseCaseTests.cs) | **5** tests d'intégration **vrai SQLite** |
| [`tests/MMV.App.Tests/ViewModels/OrderFormViewModelCreateDelegationTests.cs`](../../tests/MMV.App.Tests/ViewModels/OrderFormViewModelCreateDelegationTests.cs) | **6** tests de délégation / présentation |

### 7.3 Modifiés (4 fichiers)

| Fichier | Changement |
|---|---|
| [`src/MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) | `AddApplication` enregistre `ICreateOrderUseCase → CreateOrderUseCase` (Scoped) |
| [`src/MMV.App/ViewModels/OrderFormViewModel.cs`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) | branche création **déléguée** au use case ; `BuildCreateOrderCommand` + `UpdateExistingOrderAsync` (édition extraite, inchangée) ajoutés ; `ICreateOrderUseCase` obligatoire |
| [`src/MMV.App/ViewModels/OrdersViewModel.cs`](../../src/MMV.App/ViewModels/OrdersViewModel.cs) | reçoit `ICreateOrderUseCase` par DI ; le transmet aux deux constructions de `OrderFormViewModel` |
| [`tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs`](../../tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs) | constructeur mis à jour (param `ICreateOrderUseCase`) ; couverture numérotation **préservée** |

> **`App.axaml.cs` et `MMV.sln`/`.csproj` non modifiés** : le composition root appelle déjà
> `services.AddApplication()` (P2B-2B) ; `OrdersViewModel` (déjà `AddTransient`) voit sa nouvelle dépendance
> `ICreateOrderUseCase` résolue automatiquement. Le projet `MMV.Application.Tests` existe déjà (P2B-2C) et
> globalise les nouveaux fichiers. `OrderDetailViewModel` **non modifié** (flux reportés).

---

## 8. Command / Result / UseCase

**`CreateOrderCommand`** — porte uniquement l'état de la VM nécessaire à la création : `OrderNumber`
(numéro déjà attribué à l'ouverture, **préservé**), `EstimatedDelivery`, `Notes`, `Lines`. Aucun montant
(le flux d'origine ne persiste aucun total sur `Order`), aucun `Money`, aucune devise, aucun fournisseur/pays.

**`CreateOrderLineCommand`** — `ProductId` (toujours renseigné : `CanSave` l'exige), `ItemType`, `Quantity`,
`UnitPrice` et les paramètres optiques bruts (Sphere/Cylinder/Axis/Addition). Pas d'`UsageType`/Prism/
`VisualAcuity` : le flux d'origine ne les renseigne pas pour les commandes (iso-fonctionnel, aucun champ
ajouté).

**`CreateOrderResult`** — `OrderId`, `OrderNumber`, `Status`, `EstimatedDelivery`. **Aucune entité exposée** :
contrairement à `RegisterSaleResult.Sale`, l'événement `OrderSaved` de cette VM est **sans charge utile**, donc
aucun transit d'entité n'est nécessaire.

**`CreateOrderUseCase`** — dépend de `IOrderRepository` + `IUnitOfWork` (tous deux **obligatoires**,
constructeur rejette `null`). `ExecuteAsync` reproduit **à l'identique** la branche création :
`Order { OrderNumber, EstimatedDelivery, Notes (blanc→null), OrderDate = UtcNow, Status = New }`, articles
construits avec gating optique **dérivé du type** (`LensOd`/`LensOg` ⇒ optique conservée, sinon `null` —
équivalent exact de `OrderItemLine.IsLens`), puis `CreateAsync` + un unique `SaveChangesAsync`. Exceptions
laissées remonter telles quelles (mêmes `catch` dans la VM).

---

## 9. Modifications de `OrderFormViewModel`

**Ajouté** : `ICreateOrderUseCase` **obligatoire** (rejette `null`) ; `BuildCreateOrderCommand()` (mapping
état VM → command, lignes valides uniquement) ; `UpdateExistingOrderAsync()` (édition extraite, **inchangée**).

**Modifié** : `SaveAsync` branche désormais — `_isEditMode` ⇒ `UpdateExistingOrderAsync` (reporté) ; sinon
⇒ `await _createOrderUseCase.ExecuteAsync(BuildCreateOrderCommand())`. `OrderSaved` et `IsSaving`/`catch`
**inchangés**.

**Conservé** : `IOrderRepository` + `IUnitOfWork` (utilisés **uniquement** par l'édition reportée) ;
`ICustomerRepository`/`IProductRepository`/`IPrescriptionRepository` (chargement d'écran) ;
`INumberSequenceService` (numéro `ORDER` à l'ouverture, **comportement P2A-1E préservé et affiché**) ;
`CanSave`, `IsSaving`, calculs UI, `OrderSaved`. **La branche création ne construit/sauvegarde plus
directement la commande** (plus de `new Order()` + `CreateAsync` + `SaveChangesAsync` dans ce flux).

---

## 10. Modifications DI

`AddApplication` enregistre désormais, en plus de `RegisterSaleUseCase` :

```csharp
services.AddScoped<ICreateOrderUseCase, CreateOrderUseCase>();
```

Portée **Scoped** = même portée que `OpticDbContext` / `IOrderRepository` / `IUnitOfWork` ⇒ même `DbContext`
(cohérent avec le flux d'origine). `OrdersViewModel` reçoit le use case par DI (déjà `AddTransient`) et le
transmet à `OrderFormViewModel` (paramètre **obligatoire**). **Aucun autre use case enregistré.**

---

## 11. Tests ajoutés / adaptés

### 11.1 `MMV.Application.Tests` — `CreateOrderUseCaseTests` (5, **vrai SQLite**)

1. création avec ligne verre : `OrderId` attribué, **numéro `CMD-000042` préservé**, statut `New`,
   `EstimatedDelivery` préservée, optique conservée pour le verre ;
2. ligne monture : paramètres optiques **ignorés** (gating verres uniquement) ;
3. notes blanches → `null` ;
4. commande nulle ⇒ `ArgumentNullException` ;
5. constructeur sans `IOrderRepository` ⇒ `ArgumentNullException`.

> **Enforcement FK désactivé** dans ces tests (`Foreign Keys=False`) — toujours **vrai SQLite** (fichier +
> schéma réels, jamais InMemory). Justification : le flux d'origine laisse `Order.SaleId = 0` (commande
> autonome), ce qui viole la FK `Order → Sale` sous l'enforcement par défaut de Microsoft.Data.Sqlite. Ce
> problème est **préexistant et orthogonal** (cf. §16) ; on l'isole pour valider le **contrat réel** du use
> case (numéro, statut, lignes, optique, notes).

### 11.2 `MMV.App.Tests` — `OrderFormViewModelCreateDelegationTests` (6)

1. la création **délègue** au use case (espion `ExecuteCount==1`) + `OrderSaved` levé ;
2. garde `IsSaving` (2ᵉ clic ignoré, **1** seul appel) ;
3. exception propagée par le use case → `ErrorMessage`, **pas** de notification de succès ;
4. sans article valide → **0** appel (garde `CanSave`) ;
5. constructeur sans use case → `ArgumentNullException` ;
6. mapping état VM → `CreateOrderCommand` (dont **numéro `CMD-000001` préservé**, notes, ligne).

### 11.3 `OrderFormViewModelNumberingTests` (adapté, 2)

Couverture **préservée** (numéro `ORDER` à l'ouverture depuis la séquence ; rejet sans
`INumberSequenceService`) ; constructeur mis à jour avec le nouveau paramètre.

> **Couverture non perdue, déplacée** : la persistance (numéro préservé, statut, lignes, optique) est portée
> au niveau use case (vrai SQLite), conformément à la stratégie de tests du plan de migration §5.

---

## 12. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `MMV.Application` ne
contient **aucun** type EF. `has-pending-model-changes` reste **false**. Migrations existantes intactes.

---

## 13. Contrôles exécutés (après modification)

| Commande | Résultat |
|---|---|
| `git status --short` | 4 fichiers ` M`, 3 entrées `??` (use case, test délégation, dossier tests Orders) |
| `git diff --stat` | 4 fichiers suivis, **+96 / −37** |
| `git diff --check` | ✅ aucune anomalie (seul un avis LF→CRLF bénin) |
| `dotnet restore MMV.sln` | ✅ à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **340** (Domain **223** + App **101** + Application **16**), 0 échec |
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
| Tests | **340** (223 + 101 + 16), 0 échec (+11 vs baseline) | ✅ |
| Audit NuGet | **0 vulnérabilité** (7 projets) | ✅ |
| `has-pending-model-changes` | **false** | ✅ |
| `MMV.Application` → `MMV.Domain` seul | confirmé | ✅ |
| Aucune migration / aucun modèle EF modifié | confirmé | ✅ |
| Use case existe / interface / Command / Result | confirmé | ✅ |
| `AddApplication` enregistre le use case (Scoped) | confirmé | ✅ |
| VM délègue la création ; numérotation `ORDER` préservée | confirmé | ✅ |
| Comportement utilisateur identique | confirmé (déplacement iso-fonctionnel) | ✅ |
| Tests d'architecture Application (P2B-2C) toujours verts | confirmé (6/6, inclus dans les 16) | ✅ |

---

## 15. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les **7** projets, transitifs inclus. Les tests ajoutés réutilisent
des packages déjà présents (xunit, Moq, EF Core Sqlite 8.0.27, **FluentAssertions épinglé 6.12.0** — v7+
commercialement licenciée, pin respecté). `MMV.Application` reste sans dépendance EF/Avalonia.

---

## 16. Warnings résiduels & découvertes

### 16.1 Warning

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` (async sans `await`) | `OrderFormViewModel.LoadExistingOrderAsync` | **Préexistant** (baseline), non modifié, hors périmètre. (Le n° de ligne s'est décalé de 458 → 464 du fait des lignes ajoutées ; même méthode, même cause.) |

**Aucun nouveau warning** introduit par P2B-2D.

### 16.2 Découverte (pré-existante, hors périmètre) — `Order.SaleId = 0`

Le flux de création de commande crée une commande **autonome** (`new Order()` sans `SaleId`). Sous
l'enforcement FK par défaut de Microsoft.Data.Sqlite, persister une telle commande **viole** la clé étrangère
`Order → Sale` (`OnDelete: Cascade`, `SaleId` non nullable). **Comportement préexistant et inchangé** : le
code d'origine de `SaveAsync` faisait exactement cela ; le use case est strictement iso-fonctionnel. **Non
corrigé** (interdictions : entités Domain, migrations, schéma EF ; principe : déplacement, pas refonte). Les
tests d'intégration relâchent l'enforcement FK pour valider le contrat du use case (cf. §11.1). À traiter dans
une phase ultérieure dédiée (lien `Sale`/`Order` ou commande fournisseur autonome assumée).

---

## 17. Risques résiduels

| # | Risque | Évaluation / mitigation |
|---|---|---|
| 1 | **R-05** | **Réduit** : le 2ᵉ parcours (création de commande) ne loge plus la persistance dans l'UI. Édition, statuts, réception, encaissement, suppression restent à migrer (strangler). |
| 2 | **Numérotation `ORDER` à l'ouverture conservée dans la VM** | **Tension assumée** avec « pas de numérotation directe dans la VM » : priorité à l'iso-fonctionnalité (numéro **affiché** en lecture seule, comportement P2A-1E testé) et au dépôt réel. Le use case persiste le numéro déjà attribué. Évolution possible (numérotation au save) hors iso-fonctionnel, différée. |
| 3 | **`Order.SaleId = 0`** (commande autonome) | **Préexistant**, orthogonal, non corrigé (cf. §16.2). Tests d'intégration : FK relâchée, documentée. |
| 4 | **Use case « anémique »** | Le flux d'origine n'est ni transactionnel ni multi-repo ; le use case est volontairement mince (palier transaction-script assumé par l'ADR §2.1 / plan anti-pattern #9). L'intention métier est nommée. |
| 5 | **Édition non migrée dans `OrderFormViewModel`** | `SaveAsync` conserve une branche édition (repo + `SaveChanges` directs) ; `IOrderRepository`/`IUnitOfWork` retenus **uniquement** pour ce flux reporté. Sera migré ultérieurement. |
| 6 | **Résolution Scoped depuis la racine** | Inchangé vs P2B-2C (préexistant) : le use case partage le `DbContext` de portée comme les repos. |

Aucun de ces risques ne touche au réglementaire. **Aucune valeur de gate (TVA, devise, barème, magasin,
pays) introduite.** Périmètre fiscalité / facture / devis / Belgique / Maroc / organisation / magasin /
`Money` / SaaS **non touché**. Numérotation / stock mutation / services Infrastructure / migrations / seeding /
SQLite lifecycle **non touchés** (réutilisés tels quels).

---

## 18. État Git final

```
git status --short
 M src/MMV.App/ViewModels/OrderFormViewModel.cs
 M src/MMV.App/ViewModels/OrdersViewModel.cs
 M src/MMV.Application/DependencyInjection.cs
 M tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs
?? src/MMV.Application/UseCases/Orders/
?? tests/MMV.App.Tests/ViewModels/OrderFormViewModelCreateDelegationTests.cs
?? tests/MMV.Application.Tests/UseCases/Orders/

git branch --show-current → p2b-architecture
```

**Aucun commit, aucun push** (`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`). `git diff --check` : **0 anomalie**.
Aucun `bin/`/`obj/`, `*.db`/`*.trx`/`*.zip`, secret ou temporaire. Aucune entité Domain, migration,
repository, `DbContext` ou service Infrastructure modifié.

---

## 19. Verdict — GO / NO-GO

| Critère d'acceptation P2B-2D | Statut |
|---|---|
| UseCase commande existe (`CreateOrderUseCase`) | ✅ |
| Interface du use case existe (`ICreateOrderUseCase`) | ✅ |
| Command existe (`CreateOrderCommand` + `CreateOrderLineCommand`) | ✅ |
| Result existe (`CreateOrderResult`) | ✅ |
| `AddApplication` enregistre le use case (Scoped) | ✅ |
| ViewModel délègue le flux commande ciblé (création) | ✅ |
| Transaction dans le use case **si nécessaire** | ✅ (non nécessaire : une seule écriture atomique) |
| Numérotation `ORDER` préservée | ✅ (à l'ouverture, affichée, transmise au use case) |
| Comportement utilisateur identique | ✅ |
| Tests Application ajoutés (vrai SQLite) | ✅ (5) |
| Tests ViewModel adaptés / ajoutés | ✅ (6 délégation + 2 numérotation préservées) |
| Tests d'architecture non affaiblis | ✅ (6/6 verts) |
| Build vert | ✅ (1 avert. préexistant) |
| Tests verts (**340**) | ✅ |
| 0 vulnérabilité | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration / aucun modèle EF modifié | ✅ |
| Aucune règle Belgique/Maroc/fiscalité/devis/facture/`Money` | ✅ |
| Rapport complet (20 sections) | ✅ |
| Commit & push (sur autorisation explicite) | ✅ (`59a782b`, branche `p2b-architecture`, sans force) |
| CI distante verte (restore/build/test/audit/tools/EF) | ✅ run **#27** (`27463450102`, commit `59a782b`) |

### ✅ **P2B-2D = GO DÉFINITIF**

Le flux de **création** d'une commande fournisseur est extrait **iso-fonctionnellement** de
`OrderFormViewModel.SaveAsync` vers `MMV.Application/UseCases/Orders/CreateOrder/`, réutilisant les
repositories existants. `OrderFormViewModel` **délègue** la création ; la numérotation `ORDER` reste attribuée
et **affichée** à l'ouverture (P2A-1E préservé) puis transmise au use case. Build/tests/audit/EF **verts en
local et en CI distante** (run #27 `27463450102`, commit `59a782b`, branche `p2b-architecture`) ⇒ la gate
« pipeline distant vert » est **levée** (cf. §19 bis). **Aucune** migration, **aucune** règle réglementaire
modifiée.

---

## 19 bis. Validation CI distante (run réel)

Le push du commit `59a782b` a **déclenché** le pipeline GitHub Actions via le motif `p2*`.

| Élément | Valeur réelle |
|---|---|
| **Run** | **#27** — id **`27463450102`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27463450102 |
| **Commit testé** | **`59a782b`** (`head_sha = 59a782bd7f8d5925aa7e44ba61aa5cbd1664b9c1`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement / workflow | `push` / `CI` — `Restore / Build / Test / Scan` |
| Runner | `windows-latest` (GitHub-hosted) |
| Durée | ≈ 3 min 10 s (09:54:04 → 09:57:14 UTC) |
| Setup .NET (SDK `global.json`) | ✅ success (step #3) |
| **Restore** (step #5) | ✅ **success** |
| **Build** (step #6) | ✅ **success** |
| **Test** (step #7) | ✅ **success** (suite **340** confirmée localement) |
| **Audit NuGet** (step #8, JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| **Restore .NET tools** (step #9) | ✅ **success** (`dotnet-ef` 8.0.27) |
| **Check EF Core pending model changes** (step #10) | ✅ **success** — `has-pending-model-changes` = **false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore, Build,
Test, Audit des packages vulnérables, Restore .NET tools, Check EF Core pending model changes, Post-steps,
Complete job*. **VALIDATION DISTANTE OBTENUE** : la gate roadmap « pipeline distant vert » est **levée** pour
P2B-2D.

---

## 20. Prochaine étape candidate : **P2B-2E**

**P2B-2E — troisième vertical slice** (Programme 2, strangler). Candidats naturels, sur le même modèle
(use case Application, réutilisation des ports P2A, délégation de la VM, tests use case + délégation) :
- **Statut / réception de commande** (`OrderDetailViewModel.AdvanceStatusAsync`, **transactionnel** :
  statut + mouvements de stock de fabrication + notification — bon candidat pour `ITransactionRunner`) ;
- **Édition de commande** (`OrderFormViewModel`, branche `_isEditMode` reportée ici) ;
- **Stock** (`StockMovementFormViewModel`).

> **Ne pas démarrer** sans revue humaine du présent rapport et `TARGET_PHASE_ID = P2B-2E` fourni
> explicitement. **Claude ne lance jamais seul l'étape suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2D`. Commit `59a782b` (feat) + commit documentaire (validation CI) **poussés sur autorisation
explicite** (branche `p2b-architecture`, sans force). CI distante **#27** (`27463450102`) **verte**.
**Aucune migration. P2B-2E non commencé.**
