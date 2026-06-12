# P2A-1E — Numérotation fiable des ventes et commandes — RAPPORT D'IMPLÉMENTATION

> **Date : 12 juin 2026.** Branche : `phase2a-stabilization`.
> Phase de **stabilisation** : remplace la génération **aléatoire** (`new Random().Next(10000)`) des
> `SaleNumber`/`OrderNumber` — et le comptage `count+1` côté commande — par une **séquence transactionnelle
> déterministe** unique sous concurrence (R-03 / ADR-006), en réutilisant la frontière transactionnelle de
> P2A-1C. **Sans** fiscalité, format légal national, facture, devis, avoir, organisation/magasin, exercice,
> ni couche Application complète. ADR dédié : [adr-numbering.md](../architecture/adr-numbering.md).

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1E
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun push**.
Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-1D` = GO définitif | commit `94ff998`, CI run `27399900384`, 276/276 | ✅ |
| Dépôt propre au démarrage | `git status --short` **vide** | ✅ |
| Baseline reproductible | restore/build OK, **276/276**, **0 vuln**, `has-pending` = **false** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |
| `ITransactionRunner` / `IStockMutationService` présents (P2A-1C/1D) | lus et **préservés** (cf. §8) | ✅ |

Aucun prérequis manquant → poursuite autorisée.

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `bf690d2 docs(P2A-1D): record green CI run 27399900384 (GO definitif)`
- `git status --short` : **vide** (arbre propre) · `git diff --stat` : **vide**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** (projets à jour) |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel](../../src/MMV.App/ViewModels/OrderFormViewModel.cs), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **276 ✅ / 0 ❌** (App **93** ; Domain **183**) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list … --no-build --no-connect` | **8 migrations** (`InitialCreate` → `FixDateTimeDefaultValues`) |
| `dotnet ef migrations has-pending-model-changes … --no-build` | **false** |

> **Note outillage.** L'outil **global** `dotnet-ef` (10.0.2) ne correspond pas au runtime EF 8.0.27 et n'est
> pas sur le PATH par défaut. Pour générer une migration **identique** aux conventions EF 8, un **manifeste
> d'outil local** (`.config/dotnet-tools.json`) **épingle `dotnet-ef` à 8.0.27** ; `dotnet ef` y résout
> désormais la version exacte du runtime (commandes reproductibles). Justifié et non destructif.

## 5. Inventaire des numérotations (recensement réel du dépôt)

Recherche : `Random`, `SaleNumber`, `OrderNumber`, `Generate`, `Number`, `Reference`, `Sequence`, `Next`,
`Guid`, `DateTime.Now/UtcNow`.

| # | Fichier · méthode | Type | Stratégie AVANT | Collision | Unicité existante | Concurrence | Décision P2A-1E |
|---|---|---|---|---|---|---|---|
| 1 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | `SaleNumber` | `VTE-{yyyy}-{Random.Next(10000):D4}` | **OUI** | index UNIQUE `idx_sales_sale_number_unique` | aléatoire | **REMPLACÉ** (séquence `SALE`, dans la transaction) |
| 2 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | `OrderNumber` (verres) | `CMD-{yyyy}-{Random.Next(10000):D4}` | **OUI** | index UNIQUE `idx_orders_order_number_unique` | aléatoire | **REMPLACÉ** (séquence `ORDER`, même transaction) |
| 3 | [`OrderFormViewModel.GenerateOrderNumberAsync`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) | `OrderNumber` | `CMD-{year}-{count(year)+1:D4}` | **OUI** (concurrent) | idem | lecture→écriture | **REMPLACÉ** (séquence `ORDER`, à l'ouverture) |
| — | `DbInitializer` | seed démo | `VTE-{yyyy}-{i:D4}` (déterministe) | non | idem | mono-thread | **Inchangé** (démo ; hors flux ; suite P2A-1F) |
| — | `UserValidator.cs:89` | mot de passe | `new Random()` | n/a | n/a | n/a | **Inchangé** (R-18, sécurité) |

Autres occurrences (`Guid.NewGuid` dans les **tests**, `GetBySaleNumberAsync`/`GetByOrderNumberAsync` lecture
seule, `OrderNumber` d'affichage) : sans rapport avec la **génération**. Cibles : **#1, #2, #3**.

## 6. ADR et décision

ADR créé : [docs/architecture/adr-numbering.md](../architecture/adr-numbering.md). **Options comparées** (exigées) :

| # | Option | Verdict |
|---|---|---|
| 1 | `Random` actuel | **Rejeté** (cause de R-03) |
| 2 | GUID | **Rejeté** (non lisible/séquentiel) |
| 3 | Timestamp seul | **Rejeté** (collisions même ms) |
| 4 | **Table de séquences (par type de document)** | **RETENU** |
| 5 | Séquence SGBD native | **Différé** (non portable SQLite) |
| 6 | Séquence par magasin/pays/exercice | **Différé** (Étape 4 ; encodable dans la clé) |

**Décision** : solution **transactionnelle déterministe compatible SQLite**, sans fiscalité ni pays, mais
**extensible** vers magasin/pays/exercice via la clé `SequenceName`. Format `{Prefix}-{valeur:D6}`
(`VTE-000001`, `CMD-000001`). Unicité + concurrence par **compare-and-swap** ; rollback = numéro non consommé.

## 7. Stratégie retenue

Incrément **atomique conditionnel** (compare-and-swap) d'un compteur persistant, exécuté **dans la transaction
P2A-1C** pour la vente :

```sql
UPDATE DocumentSequences SET CurrentValue = V+1, UpdatedAt = @now
WHERE SequenceName = @name AND CurrentValue = V        -- ExecuteUpdateAsync
```

**1 ligne** ⇒ numéro `V+1` ; **0 ligne** ⇒ re-lecture + retry (un autre appel a incrémenté). Composants
(périmètre minimal) :

- `DocumentSequence` (Domain) — `SequenceName` (PK), `Prefix`, `CurrentValue` (départ 0), `UpdatedAt` ;
- `INumberSequenceService` (Domain, neutre) — `NextNumberAsync(sequenceName, ct)` ; clés `DocumentSequenceNames` (`SALE`,`ORDER`) ;
- `EfNumberSequenceService` (Infrastructure) — CAS via `ExecuteUpdateAsync`, partage le `OpticDbContext` de portée ;
- `NumberSequenceException` (Domain, contrôlée) — séquence introuvable / contention.

`SaleFormViewModel.PersistSaleAsync` génère `SaleNumber` (`SALE`) puis, si verres, `OrderNumber` (`ORDER`),
**dans la transaction** (rollback ⇒ numéros non consommés). `OrderFormViewModel` génère `OrderNumber`
(`ORDER`) à l'ouverture (numérotation non fiscale : trou autorisé en cas d'abandon). Le service est
**obligatoire** (constructeurs rejettent `null`), injecté `AddScoped` (même portée que le runner) et **fileté**
(`CustomersViewModel → CustomerDetailViewModel → SaleFormViewModel` ; `OrdersViewModel → OrderFormViewModel`).

**Sûreté des bases existantes.** La table étant **additive**, les bases clientes **antérieures à P2A-1E** ne
la possèdent pas. Pour les adopter sans perte : (a) le portail de compatibilité
[`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) **tolère** l'absence de la
table *additive* `DocumentSequences` (toute autre table manquante reste **bloquante**) ; (b) l'adoption
[`SqliteDatabaseManager`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) **exécute** réellement
`AddDocumentSequences` quand la table est absente (généralisation du mécanisme R-19) puis **vérifie
physiquement** sa présence (pas de *baseline mensonger*).

## 8. Fichiers modifiés / créés

**Créés (8 + 4 docs/outillage)**
- `src/MMV.Domain/Entities/DocumentSequence.cs` — compteur de séquence.
- `src/MMV.Domain/Interfaces/Persistence/INumberSequenceService.cs` — abstraction neutre.
- `src/MMV.Domain/Interfaces/Persistence/DocumentSequenceNames.cs` — clés `SALE`/`ORDER`.
- `src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs` — CAS EF/SQLite.
- `src/MMV.Infrastructure/Data/Configurations/DocumentSequenceConfiguration.cs` — mapping + seed `HasData`.
- `src/MMV.Infrastructure/Migrations/20260612071633_AddDocumentSequences.cs` (+ `.Designer.cs`) — migration additive.
- `tests/MMV.Domain.Tests/Persistence/EfNumberSequenceServiceTests.cs` — preuves vrai SQLite (séquences, concurrence, rollback).
- `tests/MMV.Domain.Tests/Data/DocumentSequencesAdoptionTests.cs` — adoption sûre d'une base pré-P2A-1E.
- `tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs` — VM commande via le service.
- `docs/architecture/adr-numbering.md` · `docs/implementation/P2A-1E-report.md` — ADR + ce rapport.
- `.config/dotnet-tools.json` — épingle `dotnet-ef` 8.0.27 (migrations reproductibles).

**Modifiés (13)**
- `src/MMV.Domain/Exceptions/DomainExceptions.cs` — ajout `NumberSequenceException` (contrôlée).
- `src/MMV.Infrastructure/Data/OpticDbContext.cs` — `DbSet<DocumentSequence>` + `ApplyConfiguration`.
- `src/MMV.Infrastructure/DependencyInjection.cs` — `AddScoped<INumberSequenceService, EfNumberSequenceService>()`.
- `src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs` — tolère l'absence de la table **additive** `DocumentSequences`.
- `src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs` — exécute `AddDocumentSequences` si la table est absente + vérification physique post-adoption.
- `src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs` — snapshot régénéré (DocumentSequence, additif).
- `src/MMV.App/ViewModels/SaleFormViewModel.cs` — dépendance obligatoire + génération `SALE`/`ORDER` dans la transaction.
- `src/MMV.App/ViewModels/OrderFormViewModel.cs` — dépendance obligatoire + génération `ORDER` via le service.
- `src/MMV.App/ViewModels/CustomerDetailViewModel.cs` · `CustomersViewModel.cs` · `OrdersViewModel.cs` · `App.axaml.cs` — filetage/DI du service.
- `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` — signatures + tests P2A-1E.

**Points préservés (§8 consigne)** : `ITransactionRunner`, `IStockMutationService`, transaction de vente
P2A-1C, décrément sûr P2A-1D, `has-pending-model-changes = false`. **Aucune** entité métier (Sale/Order)
modifiée. **Aucune** règle de stock, montant, prix ou taxe touchée.

## 9. Migration créée

**`20260612071633_AddDocumentSequences`** (générée par EF 8.0.27 local). **Additive et non destructive** :
- `Up` : `CreateTable(DocumentSequences)` (PK `SequenceName`) + `InsertData` des compteurs `ORDER`/`SALE`
  (`CurrentValue = 0`, préfixes `CMD`/`VTE`) — issus du seed `HasData` ;
- `Down` : `DropTable(DocumentSequences)` uniquement.

Aucune migration antérieure modifiée ; aucune entité métier, fiscalité, magasin, pays ou exercice ajouté.
**9 migrations** au total. `has-pending-model-changes = false` (cf. §12).

## 10. Tests ajoutés (+17 ⇒ 293)

**`EfNumberSequenceServiceTests`** (Domain, **vrai SQLite**, +12) : (1) premier `VTE-000001`, (2) second
`VTE-000002`, (3) premier `CMD-000001`, (4) séquences `SALE`/`ORDER` **indépendantes**, (5) **50 appels
successifs** tous distincts, (6) **10 appels concurrents** tous uniques + compteur exact, (7) **rollback** :
numéro non consommé si la transaction est annulée, (7-bis) transaction validée ⇒ consommé, (8) hors
transaction ⇒ consommé (trou autorisé), (9) séquence introuvable / nom vide ⇒ erreurs contrôlées, (10) **50
ventes réelles** persistées sans violation de l'index UNIQUE.

**`DocumentSequencesAdoptionTests`** (Domain, **vrai SQLite**, +1) : base **post-R19/pré-P2A-1E** (sans la
table) ⇒ adoption **exécute** `AddDocumentSequences`, crée la table, seede `SALE`/`ORDER`, `has-pending` false,
données conservées, attribution immédiate (`VTE-000001`).

**`SaleFormViewModelTransactionTests`** (App, Moq, +2) : constructeur sans `INumberSequenceService` ⇒
`ArgumentNullException` (plus de repli `Random`) ; `ExecuteSave` attribue le `SaleNumber` **via le service**
(et non `Random`).

**`OrderFormViewModelNumberingTests`** (App, Moq, +2) : `InitializeAsync` obtient l'`OrderNumber` **du
service** (séquence `ORDER`), l'ancien comptage `GetAllAsync` n'est plus utilisé ; constructeur sans service
⇒ `ArgumentNullException`.

Couverture vs liste obligatoire (§9 consigne) : (1)→T1, (2)→T2, (3)→T3, (4)→T4, (5)→T5, (6)→T6, (7)→T7+T7bis,
(8)→SaleForm, (9)→OrderForm, (10)→T10 (index UNIQUE), (11) tests existants verts, (12) `has-pending` false.

## 11. Commandes exécutées

```text
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff --stat
dotnet --version
dotnet restore MMV.sln
dotnet build  MMV.sln --no-restore -c Debug
dotnet test   MMV.sln --no-build  -c Debug
dotnet list   MMV.sln package --vulnerable --include-transitive
dotnet new tool-manifest ; dotnet tool install dotnet-ef --version 8.0.27
dotnet ef migrations add AddDocumentSequences --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
dotnet ef migrations list                      --project src/MMV.Infrastructure --no-build --no-connect
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
```

## 12. Résultats

| Contrôle final | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur** (1 avert. `CS1998` préexistant, hors périmètre) |
| `dotnet test -c Debug` | **293 ✅ / 0 ❌** (App **97** ; Domain **196**) — **> 276** (+17) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (aucun High/Critical) |
| `migrations list` | **9 migrations** (`…AddDocumentSequences` en tête de liste finale) |
| `has-pending-model-changes` | **false** |

## 13. Vulnérabilités

`dotnet list MMV.sln package --vulnerable --include-transitive` ⇒ **aucun** package vulnérable sur les 5
projets. **Aucune dépendance runtime ajoutée** (CAS via `ExecuteUpdateAsync`, déjà fourni par EF Core 8.0.27).
Le manifeste d'outil local (`dotnet-ef` 8.0.27) est un outil **de build**, pas une dépendance d'exécution.
Pins de sécurité P2A-0 (`System.Text.Json` 8.0.6) intacts.

## 14. Risques résiduels (documentés, hors périmètre)

1. **Continuité légale (facture/avoir)** — trous autorisés (numérotation non fiscale). *Suite : fiscalité + gates `G-FACT-MA`/BE (Étape 8).*
2. **Découpage magasin/pays/exercice** — absent (R-20). *Suite : Étape 4, encodé dans `SequenceName`.*
3. **R-05 non clos** — génération encore déclenchée par la VM. *Suite : use case Application (Étape 2).*
4. **Seed démo `DbInitializer`** — format `VTE-{yyyy}-{i}` distinct (pas de collision). *Suite : P2A-1F (seeds).*
5. **`Random` mot de passe (R-18)** — inchangé (hors numérotation).
6. **SQLite écrivain unique** — concurrence réelle limitée ; le CAS garantit néanmoins la correction.

## 15. État Git final

`git status --short` :

```text
 M src/MMV.App/App.axaml.cs
 M src/MMV.App/ViewModels/CustomerDetailViewModel.cs
 M src/MMV.App/ViewModels/CustomersViewModel.cs
 M src/MMV.App/ViewModels/OrderFormViewModel.cs
 M src/MMV.App/ViewModels/OrdersViewModel.cs
 M src/MMV.App/ViewModels/SaleFormViewModel.cs
 M src/MMV.Domain/Exceptions/DomainExceptions.cs
 M src/MMV.Infrastructure/Data/OpticDbContext.cs
 M src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs
 M src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
 M src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs
 M tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs
?? .config/
?? docs/architecture/adr-numbering.md
?? docs/implementation/P2A-1E-report.md
?? src/MMV.Domain/Entities/DocumentSequence.cs
?? src/MMV.Domain/Interfaces/Persistence/DocumentSequenceNames.cs
?? src/MMV.Domain/Interfaces/Persistence/INumberSequenceService.cs
?? src/MMV.Infrastructure/Data/Configurations/DocumentSequenceConfiguration.cs
?? src/MMV.Infrastructure/Migrations/20260612071633_AddDocumentSequences.Designer.cs
?? src/MMV.Infrastructure/Migrations/20260612071633_AddDocumentSequences.cs
?? src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs
?? tests/MMV.App.Tests/ViewModels/OrderFormViewModelNumberingTests.cs
?? tests/MMV.Domain.Tests/Data/DocumentSequencesAdoptionTests.cs
?? tests/MMV.Domain.Tests/Persistence/EfNumberSequenceServiceTests.cs
```

`git diff --stat` (fichiers suivis) : **13 fichiers modifiés, +266 / −38**. **Aucun commit, aucun push.**

## 16. Verdict

**GO.** Tous les critères d'acceptation P2A-1E sont satisfaits :

- ✅ build vert ; ✅ tests verts (**293 > 276**) ; ✅ aucune vulnérabilité High/Critical ;
- ✅ `has-pending-model-changes = false` ; ✅ migration **additive non destructive** (justifiée, testée) ;
- ✅ `Random` **n'est plus utilisé** pour `SaleNumber` / `OrderNumber` (ni le comptage `count+1`) ;
- ✅ les numéros sont **uniques** (50 successifs + 50 ventes réelles sans violation d'index) ;
- ✅ un cas **concurrent** est testé (10 appels simultanés ⇒ numéros tous distincts) ;
- ✅ **rollback documenté et testé** (transaction annulée ⇒ numéro non consommé ; validée ⇒ consommé) ;
- ✅ `SaleFormViewModel` **et** `OrderFormViewModel` utilisent le service ;
- ✅ bases clientes existantes **adoptées sans perte** (table créée par exécution, pas de baseline mensonger) ;
- ✅ aucune phase P2A-1F+ commencée ; ✅ rapport complet.

## 17. Prochaine étape candidate (NON exécutée)

**P2A-1F — Environnements / configuration / seeds** : séparer données de **démo** (`DbInitializer`) et données
de production, configuration par environnement, et alignement éventuel du format de seed sur la numérotation
P2A-1E. **Ne pas démarrer** sans paramètres de phase explicites.
