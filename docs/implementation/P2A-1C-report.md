# P2A-1C — Transactions, idempotence et erreurs de persistance — RAPPORT D'IMPLÉMENTATION

> **Date : 11 juin 2026.** Branche : `phase2a-stabilization`.
> Phase de **stabilisation** des écritures existantes (R-23 transactions, R-05 frontière) **sans** refonte
> métier. Construit la **primitive de transaction** prévue par la [migration-roadmap §Étape 1](../architecture/migration-roadmap.md)
> et l'applique au flux prioritaire `SaleFormViewModel.ExecuteSave`, **sans** bâtir la couche Application
> (Étape 2), **sans** concurrence de stock (R-09/P2A-1D), **sans** numérotation fiable (R-03/P2A-1E),
> **sans** modèle monétaire, organisation/magasin, fiscalité, facturation, ni règle nationale.
>
> **Révision R2 (11 juin 2026) — suppression du repli non transactionnel.** La première version laissait
> `SaleFormViewModel.ExecuteSave` retomber sur un **comportement inline non transactionnel** lorsque
> `ITransactionRunner` était absent — ce qui **réintroduisait** le risque d'écriture partielle (2
> `SaveChanges` sans transaction) que la phase doit éliminer. **R2 supprime ce repli** : le runner est
> désormais **obligatoire** (paramètre requis, non nullable) sur `SaleFormViewModel`,
> `CustomerDetailViewModel` et `CustomersViewModel` ; chaque constructeur **rejette `null`**. Le flux
> prioritaire **ne peut plus s'exécuter sans frontière transactionnelle**. Sections mises à jour : §6, §7,
> §8, §10, §12, §16. Aucun paramètre reçu ne change.

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1C
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun push**.
Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-1R19` = GO définitif | [P2A-1R19-report.md §15](P2A-1R19-report.md) : commit `f93379b`, CI run #27346008843, 243/243 | ✅ |
| Dépôt propre au démarrage | `git status --short` **vide** | ✅ |
| Baseline reproductible | restore/build OK, **243/243**, **0 vuln**, `has-pending` = **false** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |

Aucun prérequis manquant → poursuite autorisée.

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `1d8c44f docs(P2A-1R19): record green CI run 27346008843 (GO definitif)`
- `git status --short` : **vide** (arbre propre) · `git diff --stat` : **vide**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **243 ✅ / 0 ❌** (App **79** ; Domain **164**) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list … --no-build --no-connect` | **8 migrations** (`InitialCreate` → `FixDateTimeDefaultValues`) |
| `dotnet ef migrations has-pending-model-changes … --no-build` | **false** (« No changes have been made to the model… ») |

> Outillage : `dotnet-ef` est l'outil **global** `10.0.2` lancé via `~/.dotnet/tools/dotnet-ef.exe` ; il
> pilote le design-time mais utilise `Microsoft.EntityFrameworkCore.Design 8.0.27` du projet (format EF 8).

## 5. Inventaire des écritures (analyse obligatoire)

Recherche globale sur `src/**/*.cs` des motifs : `SaveChanges(Async)`, `BeginTransaction(Async)`, `Commit`,
`Rollback`, `ExecuteSave`, `ExecuteCreate`, `Submit`, `IsBusy`, `IsSaving`, `CanSave`, `DbUpdateException`,
`SqliteException`.

| Fichier / méthode | `SaveChanges` | Transaction | Double-soumission | Écriture partielle possible | Erreur visible utilisateur | Décision P2A-1C |
|---|---|---|---|---|---|---|
| [`SaleFormViewModel.ExecuteSave`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | **2** (l.887, 965 d'origine) | **absente** | **non gardée** (`new RelayCommand(ExecuteSave)`, pas de `CanExecute`, pas de test `IsSaving`) | **OUI** (vente persistée, commande/stock non) | brute (`ex.Message`) | **PROTÉGÉ** : transaction + garde `IsSaving` + `PersistenceException` |
| [`OrderFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) | 1 (l.644) | implicite | **gardée** (`CanSave()`→`!IsSaving` + `if(!CanSave()) return;`) | non (mono-écriture) | brute | inchangé |
| [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) | 1 (l.324) | implicite | **non gardée** (`CanSave()` ne teste pas `IsSaving`) | non (mono-écriture) | brute | risque résiduel documenté (§14) |
| [`CustomerFormViewModel.ExecuteSave`](../../src/MMV.App/ViewModels/CustomerFormViewModel.cs) | 1 | implicite | gardée (`CanExecuteSave`→`!IsSaving`) | non | brute | inchangé |
| [`SupplierFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/SupplierFormViewModel.cs) | 1 (branche if/else) | implicite | gardée (`!IsSaving` + early return) | non | brute | inchangé |
| [`PrescriptionFormViewModel`](../../src/MMV.App/ViewModels/PrescriptionFormViewModel.cs), [`CustomerPrescriptionsViewModel`](../../src/MMV.App/ViewModels/CustomerPrescriptionsViewModel.cs) | `CommitAsync` (sans `BeginTransaction` préalable → SaveChanges simple) | implicite | `IsSaving` | non | brute | inchangé |
| `OrderDetail`, `OrderKanban`, `Inventory`, `NotificationsList`, `ProductsList`, `Orders`… | 1 chacun | implicite | variable | non | brute | inchangé |
| Services Domain ([`SaleService`](../../src/MMV.Domain/Services/SaleService.cs), [`OrderService`](../../src/MMV.Domain/Services/OrderService.cs), `Product`, `Customer`, `Prescription`, `Authentication`) | 1 chacun | implicite | n/a | non | n/a | inchangés (non utilisés par `ExecuteSave`) |
| [`UnitOfWork`](../../src/MMV.Infrastructure/Repositories/UnitOfWork.cs) | `BeginTransactionAsync`/`CommitAsync` présents mais **inutilisés** ; `RollbackAsync` **dispose** le contexte (ne rollback pas, **R-23**) | — | — | — | — | contourné (non assaini ici, Étape 2) |
| [`SqliteDatabaseManager`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) | `BeginTransaction`/`Commit` pour l'écriture de l'historique de migration | explicite | n/a | non | journalisée | inchangé (cycle de vie P2A-1A) |

**Conclusion** : le **seul** flux multi-`SaveChanges` **sans** transaction est `SaleFormViewModel.ExecuteSave`
⇒ **cible prioritaire** unique. Un `SaveChanges` unique est déjà atomique (transaction implicite EF).

## 6. Priorisation des flux

1. **`SaleFormViewModel.ExecuteSave`** (multi-écriture, sans transaction, sans garde) → **traité intégralement**
   (transaction + rollback + garde double-soumission + erreurs contrôlées).
2. `StockMovement`, `OrderForm` → **non multi-écriture** (mono-`SaveChanges`, atomiques) ; `OrderForm` déjà
   gardé ; `StockMovement` documenté en risque résiduel (garde à généraliser Étape 2).
3. Services Domain → hors flux `ExecuteSave`, inchangés (relèvent de la couche Application, Étape 2).

## 6 bis. R2 — Constructions de `SaleFormViewModel` et suppression du repli non transactionnel

Recherche globale `new SaleFormViewModel` / `SaleFormViewModel(` (et de la chaîne de production
`CustomersViewModel → CustomerDetailViewModel → SaleFormViewModel`) :

| Occurrence | Contexte | Prod/Test | Runner fourni ? | Décision R2 |
|---|---|---|---|---|
| [`CustomerDetailViewModel.cs:130`](../../src/MMV.App/ViewModels/CustomerDetailViewModel.cs#L130) `new SaleFormViewModel(...)` | **seule** construction de production | Production | **OUI** (`_transactionRunner`, désormais requis) | **runner requis** transmis ; constructeur rejette `null` |
| [`CustomersViewModel.cs:201`](../../src/MMV.App/ViewModels/CustomersViewModel.cs#L201) `new CustomerDetailViewModel(...)` | crée le `CustomerDetailViewModel` | Production | **OUI** (`_transactionRunner`, requis) | **runner requis** transmis |
| DI ([`App.axaml.cs`](../../src/MMV.App/App.axaml.cs), [`DependencyInjection.cs`](../../src/MMV.Infrastructure/DependencyInjection.cs)) → `CustomersViewModel` | `AddScoped<ITransactionRunner, EfTransactionRunner>` + `AddTransient<CustomersViewModel>` | Production | **OUI** (résolu par DI, même portée que `OpticDbContext`/repos) | runner **injecté** ; param requis ⇒ échec DI explicite si non enregistré |
| [`SaleFormViewModelTransactionTests.cs`](../../tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs) | tests P2A-1C/R2 | Test | **OUI** (spy/pass-through) sauf le test « null » dédié | conforme |

**Chemin de production prouvé** : le test #14 `ProductionChain_CustomerDetail_TransmitsRunnerToSaleForm`
exécute `CustomerDetailViewModel.InitializeAsync` (construction réelle du `SaleFormViewModel`) puis une
sauvegarde, et vérifie que le **même** runner (spy) est utilisé (`RunCount == 1`).

**Suppression du repli** : `ExecuteSave` n'a plus de branche `_transactionRunner != null ? … : inline`.
Il appelle **toujours** `await _transactionRunner.RunAsync(PersistSaleAsync)`. `_transactionRunner` est un
champ **non nullable** garanti par le constructeur (`?? throw new ArgumentNullException`). **Le flux
prioritaire ne possède donc plus de chemin non transactionnel** : l'absence de runner est une **erreur de
configuration** qui empêche toute construction du ViewModel, et donc toute persistance.

## 7. ADR et décision

Créé/mis à jour : [`docs/architecture/adr-transaction-idempotency.md`](../architecture/adr-transaction-idempotency.md).
**R2** : section §2 durcie — **runner obligatoire, aucun repli non transactionnel** (construction
impossible sans frontière transactionnelle).
Compare **4 options** : (1) transactions dans les VM — *rejeté* (aggrave R-05) ; (2) transactions dans les
services Domain — *rejeté pour P2A-1C* (services contournés ; relève de l'Étape 2) ; (3) **service
Infrastructure de transaction `ITransactionRunner` + `EfTransactionRunner`** — **RETENU** ; (4) couche
Application / use case complet — *différé* (Étape 2). L'ADR précise frontière transactionnelle, rollback,
idempotence (minimale), double soumission, erreurs techniques, limites SQLite, et migration future vers la
couche Application (réutilisation de la **même** primitive neutre).

## 8. Fichiers modifiés

**Ajoutés — Domain (sans dépendance EF) :**
- [`src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs`](../../src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs) — abstraction de frontière transactionnelle.
- [`src/MMV.Domain/Exceptions/PersistenceException.cs`](../../src/MMV.Domain/Exceptions/PersistenceException.cs) — erreur contrôlée + `PersistenceErrorCategory`.

**Ajoutés — Infrastructure (EF/SQLite) :**
- [`src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs) — implémentation (transaction explicite, rollback, mapping).
- [`src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs`](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs) — mapping `DbUpdate`/`Sqlite` → `PersistenceException`.

**Modifiés :**
- [`src/MMV.App/ViewModels/SaleFormViewModel.cs`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) — garde `IsSaving`, corps déplacé dans `PersistSaleAsync(ct)` exécuté via `ITransactionRunner`, `catch (PersistenceException)`. **R2** : `ITransactionRunner` **requis** (non nullable, `?? throw`) ; **suppression du repli inline non transactionnel** (`ExecuteSave` appelle toujours `_transactionRunner.RunAsync`).
- [`src/MMV.App/ViewModels/CustomerDetailViewModel.cs`](../../src/MMV.App/ViewModels/CustomerDetailViewModel.cs) — filetage du runner jusqu'à `SaleFormViewModel`. **R2** : runner **requis** (`?? throw`).
- [`src/MMV.App/ViewModels/CustomersViewModel.cs`](../../src/MMV.App/ViewModels/CustomersViewModel.cs) — filetage du runner jusqu'à `CustomerDetailViewModel`. **R2** : runner **requis** (`?? throw`), injecté par DI.
- [`src/MMV.App/App.axaml.cs`](../../src/MMV.App/App.axaml.cs) — `AddScoped<ITransactionRunner, EfTransactionRunner>()`.
- [`src/MMV.Infrastructure/DependencyInjection.cs`](../../src/MMV.Infrastructure/DependencyInjection.cs) — idem (DI Infrastructure).

**Ajoutés — tests :**
- [`tests/MMV.Domain.Tests/Persistence/EfTransactionRunnerTests.cs`](../../tests/MMV.Domain.Tests/Persistence/EfTransactionRunnerTests.cs) (9, vrai SQLite temporaire).
- [`tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs`](../../tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs) (3, ViewModel).

**Ajoutés — docs :** cet ADR + ce rapport.

**Aucune entité métier, aucune configuration EF, aucun `DbContext`, aucun `DbInitializer`/`DbSeeder`,
aucune des 8 migrations, aucune valeur réglementaire** modifiés.

## 9. Migrations créées ou non

**Aucune migration créée.** Aucune entité ni configuration EF n'a changé. `has-pending-model-changes`
reste **false** (cf. §12).

## 10. Tests ajoutés (14)

**`EfTransactionRunnerTests` (9, vrai SQLite temporaire — fichiers isolés, jamais InMemory) :**

| # | Test | Preuve |
|---|---|---|
| 1 | `RunAsync_MultiWriteSuccess_CommitsAllWrites` | opération **multi-écriture réussie** (2 SaveChanges) → 2 ventes persistées |
| 2 | `RunAsync_ExceptionAfterFirstSave_RollsBackEverything` | **exception au milieu** (après le 1er SaveChanges) → **rollback complet** → 0 vente (aucune écriture partielle) |
| 3 | `RunAsync_UniqueConstraintViolation_ThrowsControlledPersistenceException_AndRollsBack` | violation d'unicité (`idx_sales_sale_number_unique`) → **`PersistenceException` (UniqueConstraint)** + écriture bénigne du 1er SaveChanges **annulée** |
| 4 | `RunAsync_NonPersistenceException_IsRethrownUnchanged_AndRollsBack` | exception non-persistance **propagée inchangée** + rollback complet |
| 5 | `RunAsync_NestedWithinExistingTransaction_DoesNotOpenSecondTransaction` | frontière **imbriquée** : rattachement à la transaction externe (limite SQLite) |
| 6 | `Mapper_MapsSqliteUniqueException_ToUniqueConstraintCategory` | mapping `SqliteException(19/2067)` → `UniqueConstraint` |
| 7 | `Mapper_MapsSqliteBusyException_ToDatabaseBusyCategory` | mapping `SqliteException(5)` → `DatabaseBusy` |
| 8 | `Mapper_MapsDbUpdateExceptionWithSqliteInner_ToPersistenceException` | `DbUpdateException` ⊃ `SqliteException` → `PersistenceException` |
| 9 | `Mapper_LeavesNonPersistenceException_Unchanged` | exception métier **non transformée** (neutralité) |

**`SaleFormViewModelTransactionTests` (5, ViewModel) :**

| # | Test | Preuve |
|---|---|---|
| 10 | `ExecuteSave_RoutesMultiWriteThroughTransactionRunner` | la sauvegarde **passe toujours par** la frontière transactionnelle (`RunCount == 1`) ; succès |
| 11 | `ExecuteSave_WhileSaving_SecondInvocationIsIgnored` | **double soumission empêchée** : 1er clic bloqué sur le SaveChanges, 2e clic **ignoré** (1 seule vente créée) |
| 12 | `ExecuteSave_WhenPersistenceExceptionThrown_ShowsControlledMessage` | `PersistenceException` → **message contrôlé** affiché, pas de notification de succès |
| 13 | **`Constructor_WithoutTransactionRunner_Throws_NoMultiWriteWithoutTransaction`** (R2) | sans runner ⇒ **`ArgumentNullException`** au constructeur ⇒ **aucune vente créée, aucun `SaveChanges`** (pas de chemin non transactionnel) |
| 14 | **`ProductionChain_CustomerDetail_TransmitsRunnerToSaleForm`** (R2) | le chemin de production `CustomerDetailViewModel → SaleFormViewModel` **transmet** le runner (spy `RunCount == 1` à la sauvegarde) |

Couverture des cas obligatoires P2A-1C / R2 : multi-écriture réussie (1, 10) ; exception au milieu (2) ;
rollback complet (2, 3) ; aucune écriture partielle (2, 3, 4) ; double soumission empêchée (11) ;
`SqliteException`/`DbUpdateException` transformée (3, 6-8, 12) ; **`ExecuteSave` utilise toujours le runner
quand disponible (10, 14)** ; **sans runner aucune écriture / aucun `SaveChanges` (13)** ; **message d'erreur
contrôlé (12)** ; **chemin production transmet le runner (14)** ; non-régression (§12) ; aucune migration
(§9) ; `has-pending` = false (§12).

## 11. Commandes exécutées (principales)

```powershell
# Baseline + analyse
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff --stat
dotnet --version ; dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list  --project src/MMV.Infrastructure --no-build --no-connect
& ~/.dotnet/tools/dotnet-ef.exe migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build

# Implémentation (Domain → Infrastructure → DI → ViewModel → tests)
#  - ITransactionRunner + PersistenceException (Domain, sans EF)
#  - EfTransactionRunner + PersistenceErrorMapper (Infrastructure)
#  - AddScoped<ITransactionRunner, EfTransactionRunner> (App + Infrastructure)
#  - SaleFormViewModel : garde IsSaving + PersistSaleAsync via runner + catch PersistenceException
#  - filetage runner : CustomersViewModel -> CustomerDetailViewModel -> SaleFormViewModel
#  - tests runner (vrai SQLite) + tests ViewModel (Moq)

# Contrôles finaux
dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build  -c Debug
dotnet list  MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list … ; … has-pending-model-changes …  # => false
git status --short ; git diff --stat
```

## 12. Résultats (contrôles finaux)

| Contrôle | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur, 0 avertissement introduit** (`CS1998` préexistant inchangé, hors périmètre) |
| `dotnet test -c Debug` | **257 ✅ / 0 ❌** — App **84** (79 + 5) ; Domain **173** (164 + 9) |
| Non-régression des 243 | ✅ (79 App + 164 Domain préservés) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list` | **8 migrations** (inchangé) |
| `dotnet ef migrations has-pending-model-changes` | **false** ✅ (« No changes have been made to the model… ») |

**257 > 243** (14 tests ajoutés, dont **+2 en R2**).

## 13. Vulnérabilités

`dotnet list MMV.sln package --vulnerable --include-transitive` ⇒ **0 package vulnérable** sur les 5 projets.
Aucune dépendance ajoutée (les types ajoutés réutilisent EF Core / Microsoft.Data.Sqlite déjà présents en
Infrastructure ; aucun nouveau `PackageReference`).

## 14. Risques résiduels (documentés, hors périmètre)

1. **R-05 non clos** — l'orchestration d'écriture reste dans `SaleFormViewModel` (la VM accède encore aux
   repos). *Suite : use case Application `EnregistrerVente` réutilisant le **même** `ITransactionRunner`
   (Étape 2).*
2. **R-23 partiellement traité** — `UnitOfWork.RollbackAsync` (dispose ≠ rollback) et les primitives
   `BeginTransactionAsync`/`CommitAsync` restent présentes mais **contournées** sur le flux protégé.
   *Suite : assainissement à l'Étape 2 une fois tous les flux migrés.*
3. **R-03 (numérotation `Random`)** — `SaleNumber`/`OrderNumber` toujours via `new Random()` ; une
   collision est désormais **annulée atomiquement** et **signalée** (message contrôlé), mais la cause
   demeure. *Suite : ADR-006 (P2A-1E).*
4. **R-09 (concurrence stock)** — aucun concurrency token (SQLite n'a pas de `rowversion`). *Suite :
   ADR-010 (P2A-1D).*
5. **`StockMovementFormViewModel`** — mono-écriture (atomique) mais **sans garde** de double-soumission.
   *Suite : généraliser la garde `IsSaving` à l'Étape 2.*
6. **Idempotence complète** — non couverte (dépend de R-03 + couche Application).
7. **`CS1998`** préexistant ([OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453)) — inchangé, hors périmètre.

Aucun de ces risques ne touche au métier, au réglementaire, ni aux interdictions de la phase.

## 15. État Git final

`git status --short` (changeset dans l'arbre de travail, **non commité**, conformément à `ALLOW_COMMIT=false`) :

```
 M src/MMV.App/App.axaml.cs
 M src/MMV.App/ViewModels/CustomerDetailViewModel.cs
 M src/MMV.App/ViewModels/CustomersViewModel.cs
 M src/MMV.App/ViewModels/SaleFormViewModel.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
?? docs/architecture/adr-transaction-idempotency.md
?? docs/implementation/P2A-1C-report.md
?? src/MMV.Domain/Exceptions/PersistenceException.cs
?? src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs
?? src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs
?? src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs
?? tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs
?? tests/MMV.Domain.Tests/Persistence/EfTransactionRunnerTests.cs
```

`git diff --stat` (fichiers suivis modifiés, **R2 inclus**) : **5 fichiers, +202 / −141** (l'essentiel =
réorganisation de `SaleFormViewModel.ExecuteSave` en `ExecuteSave` + `PersistSaleAsync`, et runner rendu
obligatoire dans les 3 ViewModels). **Aucun** `bin/`/`obj/`, `*.db`, `*.trx`, temporaire, secret ou donnée
utilisateur. **Aucun commit, aucun push.**

## 16. Verdict — GO / NO-GO

### **P2A-1C = GO** (local vert ; validation CI distante à confirmer avant tout commit/push)

| Critère d'acceptation (consigne §12) | État |
|---|---|
| Build vert | ✅ 0 erreur, 0 avertissement introduit |
| Tests verts (> 243 ; > 255 après R2) | ✅ **257/257** (243 + 14, dont +2 R2) |
| Aucune vulnérabilité High/Critical | ✅ 0 (5 projets) |
| Aucune migration EF ajoutée | ✅ 8 migrations inchangées |
| `has-pending-model-changes = false` | ✅ « No changes have been made to the model… » |
| ≥ 1 flux critique multi-écriture protégé par transaction | ✅ `SaleFormViewModel.ExecuteSave` |
| **R2 — aucun repli non transactionnel sur le flux prioritaire** | ✅ runner **obligatoire** (construction impossible sans) ; tests 13, 14 |
| Rollback testé | ✅ tests 2, 3 (vrai SQLite, 0 écriture partielle) |
| Double soumission traitée (flux prioritaire) | ✅ garde `IsSaving` + test 11 |
| Erreurs de persistance transformées ou documentées | ✅ `PersistenceException`/mapper + tests 3, 6-8, 12 |
| Aucun début de P2A-1D / autre phase | ✅ (ni concurrency token, ni numérotation, ni Money, ni Application layer) |
| Rapport complet | ✅ (ce document, §6 bis pour R2) |

> **Note de gate** : `ALLOW_COMMIT=false`/`ALLOW_PUSH=false` ⇒ le changeset n'est **pas** commité. La
> « validation distante CI verte » (gate des phases précédentes) sera produite **après** décision humaine
> d'autoriser commit + push, sur le commit réel. Le verdict local est **GO** ; le GO **définitif** suivra
> le run CI vert, comme pour P2A-1A/1B/1R19.

## 17. Prochaine étape candidate (NON exécutée)

`P2A-1D` — « Concurrence de stock » (concurrency token applicatif, gestion de
`DbUpdateConcurrencyException`, update conditionnel `qty >= n`, cf. [ADR-010](../architecture/adr-candidates.md#adr-010)),
**OU** `P2A-1E` — « Numérotation fiable » ([ADR-006](../architecture/adr-candidates.md#adr-006)), selon
priorisation humaine. **Non commencée.** Ne pas démarrer sans verdict `P2A-1C` confirmé par revue humaine,
puis `TARGET_PHASE_ID` explicite.

> **Arrêt obligatoire.** Fin de `P2A-1C`. Aucun commit, aucun push, aucune amorce de `P2A-1D`/`P2A-1E`.
