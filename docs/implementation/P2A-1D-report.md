# P2A-1D — Concurrence de stock et intégrité des quantités — RAPPORT D'IMPLÉMENTATION

> **Date : 11 juin 2026.** Branche : `phase2a-stabilization`.
> Phase de **stabilisation** des écritures de stock existantes (R-09 concurrence) **sans** refonte du
> module stock. Sécurise le **décrément de stock du flux de vente** par un **décrément atomique
> conditionnel** réutilisant la frontière transactionnelle de P2A-1C (`ITransactionRunner`), **sans**
> couche Application (Étape 2), **sans** numérotation fiable (R-03/P2A-1E), **sans** modèle monétaire,
> organisation/magasin, stock par magasin, fiscalité, facturation, ni règle nationale.
> Concrétise, pour le décrément de stock, l'orientation différée d'[ADR-010](../architecture/adr-candidates.md#adr-010)
> (option « (3) update conditionnel atomique »). ADR dédié :
> [adr-stock-concurrency.md](../architecture/adr-stock-concurrency.md).
>
> **Révision R2 (11 juin 2026) — sécurisation de la sortie manuelle de stock.** La première version laissait
> [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs)
> autoriser un stock **négatif** sur une sortie manuelle *après confirmation opérateur*, en contradiction avec
> le critère « mouvement de stock manuel : stock non négatif ». **R2** route la **sortie manuelle standard**
> par le **même décrément atomique conditionnel** que la vente (`IStockMutationService.DecrementStockAsync`),
> enveloppé dans `ITransactionRunner` (décrément **et** mouvement atomiques) ; **la confirmation autorisant un
> stock négatif est supprimée**. Sections mises à jour : §5, §7, §8, §10, §12, §14, §15, §16.

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1D
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun push**.
Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-1C` = GO définitif | commit `186d995`, CI run #27367443700, 257/257 | ✅ |
| Dépôt propre au démarrage | `git status --short` **vide** | ✅ |
| Baseline reproductible | restore/build OK, **257/257**, **0 vuln**, `has-pending` = **false** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |
| `ITransactionRunner` / `PersistenceException` présents (P2A-1C) | lus et préservés (cf. §8) | ✅ |

Aucun prérequis manquant → poursuite autorisée.

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `6f1a030 docs(P2A-1C): record green CI run 27367443700 (GO definitif)`
- `git status --short` : **vide** (arbre propre) · `git diff --stat` : **vide**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** (projets à jour) |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **257 ✅ / 0 ❌** (App **84** ; Domain **173**) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list … --no-build --no-connect` | **8 migrations** (`InitialCreate` → `FixDateTimeDefaultValues`) |
| `dotnet ef migrations has-pending-model-changes … --no-build` | **false** (« No changes have been made to the model… ») |

> Note outillage : `dotnet-ef` n'est pas un outil de solution ; l'outil **global** `dotnet-ef` (10.0.2)
> a été utilisé en lecture seule (`migrations list` / `has-pending-model-changes`) contre le projet EF 8.0.27.
> Aucune commande d'écriture de schéma (`add`/`database update`) n'a été exécutée.

## 5. Inventaire des flux de stock (recensement réel du dépôt)

Recherche : `StockQuantity`, `Quantity`, `StockMovement`, `MovementType`, `SaveChanges(Async)`,
`ExecuteSql`, `ExecuteUpdate`, `DbUpdateConcurrencyException`, `SqliteException`, et tous les sites
`StockQuantity (+=|-=|=)`.

| # | Fichier · méthode | Opération | Contrôle dispo. | Transaction | Négatif ? | Concurrence | Erreur actuelle | Décision P2A-1D |
|---|---|---|---|---|---|---|---|---|
| 1 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L1001) | **Sortie vente comptoir** | **aucun** | oui (`ITransactionRunner`) | **OUI** | **lost update** | générique | **SÉCURISÉ** (décrément atomique conditionnel) |
| 2 | [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) | Entrée/Sortie/Ajustement manuels | **OUI** (sortie : décrément atomique) | **oui** (`ITransactionRunner`, R2) | **NON** (sortie standard, R2) | sécurisé (sortie) | `InsufficientStockException` | **SÉCURISÉ (R2)** : sortie standard via décrément atomique ; In = incrément ; Adjustment = correction absolue ≥ 0 |
| 3 | [`InventoryViewModel.ConfirmItemAdjustmentAsync`](../../src/MMV.App/ViewModels/InventoryViewModel.cs#L214) | Ajustement **absolu** | n/a | implicite | non | read→set | dialogue | **Inchangé** (set absolu) |
| 4 | [`OrderDetailViewModel.CreateStockMovementsForFabrication`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs#L409) | Sortie fabrication | **aucun** | appelant | **OUI** | RMW | générique | **Inchangé** (hors vente ; suite Étape 2) |
| 5 | [`ProductFormViewModel`](../../src/MMV.App/ViewModels/ProductFormViewModel.cs#L668-L689) | Saisie initiale | n/a | implicite | non | édition | validation | **Inchangé** (stock initial) |
| — | `DbInitializer` / `DbSeeder` | Seed démo | n/a | seed | non | n/a | n/a | **Inchangé** |

**Cible prioritaire : flux #1** (décrément **relatif non contrôlé** sur le flux vente, déjà transactionnel).
Menaces avant correctif : **stock négatif** (`qty -= n` sans borne) et **mise à jour perdue**
(`UPDATE … SET qty = <valeur absolue>` écrasant une décision concurrente). **R2** applique la **même
protection** au flux #2 (sortie manuelle standard). Détail des autres occurrences (`SaveChanges`,
`SqliteException`) : flux de persistance/diagnostic déjà couverts par P2A-1A/P2A-1C, sans rapport avec le
décrément de stock.

## 6. ADR et décision

ADR créé : [docs/architecture/adr-stock-concurrency.md](../architecture/adr-stock-concurrency.md).
**Options comparées** (exigées) :

| # | Option | Verdict |
|---|---|---|
| 1 | `rowversion` classique (SQL Server) | **Rejeté** (indisponible en SQLite) |
| 2 | Token applicatif entier `Version`/`ConcurrencyStamp` | **Différé** (Étape 2, agrégats financiers ; migration requise) |
| 3 | **Update conditionnel SQL atomique** (`UPDATE … WHERE qty >= n`) | **RETENU** |
| 4 | Verrou applicatif en mémoire | **Rejeté** (non inter-processus, non durable) |
| 5 | Traitement complet plus tard (couche Application) | **Différé** (Étape 2, réutilisera la primitive) |

**Décision** : pour SQLite, **éviter `rowversion`** ; sécuriser le décrément critique par **update
conditionnel atomique** (option 3, alignée [ADR-010](../architecture/adr-candidates.md#adr-010)) ; **aucun
token de ligne ni migration** ; préparer la couche Application via une abstraction neutre. L'ADR précise :
éviter le stock négatif (clause `WHERE StockQuantity >= quantity`), détecter un stock modifié entre-temps
(décrément relatif conditionnel, pas de lecture-puis-écriture aveugle), mapper les erreurs (stock
insuffisant → `InsufficientStockException` ; base occupée → `PersistenceException(DatabaseBusy)`), limites
SQLite (pas de `rowversion`, écrivain unique), risques résiduels et migration future.

## 7. Stratégie retenue

Décrément **atomique conditionnel** exécuté **dans la transaction P2A-1C** :

```sql
UPDATE Products SET StockQuantity = StockQuantity - @q
WHERE ProductId = @id AND StockQuantity >= @q
```

(via EF Core 8 `ExecuteUpdateAsync`). **1 ligne affectée** ⇒ décrément appliqué ; **0 ligne** ⇒ stock
insuffisant / introuvable / modifié ⇒ `InsufficientStockException` (relecture du stock courant pour le
message) ⇒ le `ITransactionRunner` **annule toute la vente**. Composants ajoutés (périmètre minimal) :

- `IStockMutationService` (Domain, neutre) — `DecrementStockAsync(productId, quantity, ct)` ;
- `EfStockMutationService` (Infrastructure, EF/SQLite) — partage le `OpticDbContext` de portée ;
- `InsufficientStockException` (Domain, contrôlée, sous-classe de `DomainException`).

Le flux de vente (`SaleFormViewModel.PersistSaleAsync`) remplace `StockQuantity -= …`/`UpdateAsync` par
`DecrementStockAsync` ; le `StockMovement` d'audit reste créé ; `ExecuteSave` attrape
`InsufficientStockException` (message contrôlé). Le service est **fileté** par DI comme le runner
(`CustomersViewModel` → `CustomerDetailViewModel` → `SaleFormViewModel`), paramètre **obligatoire**
(constructeurs rejettent `null`).

**R2 — sortie manuelle de stock.** `StockMovementFormViewModel.SaveAsync` est réécrit : chaque ligne est
traitée **atomiquement** dans `ITransactionRunner.RunAsync` (décrément/mise à jour **et** création du
`StockMovement` validés ensemble, ou rien). Par type : **Out** → `DecrementStockAsync` (atomique
conditionnel, jamais négatif ; refus = `InsufficientStockException`, aucun mouvement) ; **In** → incrément ;
**Adjustment** → correction **absolue** (≥ 0). La **confirmation autorisant un stock négatif est supprimée** ;
le message d'erreur contrôlé est exposé via `ErrorMessage` et aucune notification de succès n'est émise pour
une ligne refusée. `ITransactionRunner` + `IStockMutationService` sont **filetés** et **obligatoires**
(`ProductsViewModel` → `StockMovementsViewModel` → `StockMovementFormViewModel` ; constructeurs rejettent
`null`).

## 8. Fichiers modifiés / créés

**Créés**
- `docs/architecture/adr-stock-concurrency.md` — ADR de la phase (+ révision R2).
- `src/MMV.Domain/Interfaces/Persistence/IStockMutationService.cs` — abstraction neutre du décrément sûr.
- `src/MMV.Infrastructure/Persistence/EfStockMutationService.cs` — implémentation EF/SQLite (update conditionnel atomique).
- `tests/MMV.Domain.Tests/Persistence/EfStockMutationServiceTests.cs` — preuves sur **vrai SQLite** (service + vente).
- `tests/MMV.App.Tests/ViewModels/StockMovementFormViewModelTests.cs` — **R2** : preuves sur **vrai SQLite** (sortie manuelle).
- `docs/implementation/P2A-1D-report.md` — ce rapport.

**Modifiés**
- `src/MMV.Domain/Exceptions/DomainExceptions.cs` — ajout `InsufficientStockException` (contrôlée).
- `src/MMV.App/ViewModels/SaleFormViewModel.cs` — dépendance obligatoire + décrément atomique + `catch`.
- `src/MMV.App/ViewModels/CustomerDetailViewModel.cs` — filetage du service jusqu'au `SaleFormViewModel`.
- `src/MMV.App/ViewModels/CustomersViewModel.cs` — filetage du service (DI).
- `src/MMV.App/ViewModels/StockMovementFormViewModel.cs` — **R2** : sortie manuelle atomique + décrément sûr, suppression de la confirmation négative.
- `src/MMV.App/ViewModels/StockMovementsViewModel.cs` — **R2** : filetage runner + service jusqu'au formulaire.
- `src/MMV.App/ViewModels/ProductsViewModel.cs` — **R2** : filetage runner + service (DI).
- `src/MMV.Infrastructure/DependencyInjection.cs` — `AddScoped<IStockMutationService, EfStockMutationService>()`.
- `src/MMV.App/App.axaml.cs` — même enregistrement DI côté application.
- `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` — signatures + 3 tests P2A-1D.

**Points préservés (§8 de la consigne)** : `ITransactionRunner`, `PersistenceException`, la transaction de
vente P2A-1C, `has-pending-model-changes = false`, les **8 migrations** existantes. **Aucune** entité métier
modifiée. **Aucune migration ajoutée** par R2.

## 9. Migrations créées ou non

**Aucune migration créée.** La solution n'ajoute **aucune colonne** (pas de token de ligne) : elle exploite
un `UPDATE` conditionnel sur le schéma existant. `has-pending-model-changes` reste **false** (cf. §12). Les
**8** migrations sont inchangées. Justification : la consigne privilégie d'abord une solution **sans
migration** si elle suffit à empêcher le stock négatif — c'est le cas de l'update conditionnel.

## 10. Tests ajoutés

**`EfStockMutationServiceTests`** (Domain.Tests, **vrai SQLite temporaire**, +10 cas) :
1. décrément réussi (10 − 3 = 7) ;
2. décrément **exactement égal** au stock (5 − 5 = 0) ;
3. décrément **supérieur** refusé → `InsufficientStockException` (Requested/Available exposés), stock inchangé ;
4. **aucune quantité négative** persistée (cas type sortie manuelle, stock 2 − 3 refusé, reste ≥ 0) ;
5. **deux décréments concurrents** sur 1 unité → **une seule** réussite, stock final = 0 ;
6. produit introuvable → erreur contrôlée (disponible 0) ;
7. quantité non positive (0 et −1, `[Theory]`) → `ArgumentOutOfRangeException`, aucune écriture ;
8. **vente transactionnelle** : stock insuffisant → **vente non persistée** (0 vente) **et** stock inchangé (rollback vente + stock) ;
9. vente transactionnelle réussie → vente validée **et** décrément validé ensemble.

**`SaleFormViewModelTransactionTests`** (App.Tests, Moq, +3 cas) :
- constructeur sans `IStockMutationService` → `ArgumentNullException` (aucune sortie de stock non sûre) ;
- vente comptoir : la sortie de stock passe par `DecrementStockAsync` (et **non** l'ancien `UpdateAsync`) ;
- stock insuffisant : `InsufficientStockException` ⇒ message contrôlé affiché, **pas** de notification de succès.

**`StockMovementFormViewModelTests`** (App.Tests, **vrai SQLite temporaire**, **R2**, +6 cas) :
1. sortie manuelle suffisante → stock décrémenté (10 − 3 = 7) + mouvement enregistré, aucune confirmation ;
2. sortie manuelle **exactement égale** au stock → stock final 0 + mouvement ;
3. sortie manuelle **supérieure** → **refus contrôlé** : stock inchangé (≥ 0), **0 mouvement** (atomique),
   `ErrorMessage` contient « Stock insuffisant », pas de notification de succès, **0 confirmation** ;
4. entrée (In) → incrément (5 + 4 = 9) + mouvement ;
5. ajustement (Adjustment) → correction absolue (= 8, ≥ 0) + mouvement ;
6. constructeur sans `ITransactionRunner` **ou** sans `IStockMutationService` → `ArgumentNullException`.

Couverture vs liste obligatoire R2 (§6 consigne) : (1)→MO-1, (2)→MO-2, (3)→MO-3, (4)→MO-3 (stock ≥ 0),
(5)→MO-3 (0 mouvement), (6)→MO-3 (décrément+mouvement atomiques), (7)→MO-3 (`ErrorMessage` contrôlé),
(8) tests existants verts, (9) aucune migration, (10) `has-pending` false.

## 11. Commandes exécutées

```text
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff --stat
dotnet --version
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build  -c Debug
dotnet list  MMV.sln package --vulnerable --include-transitive
dotnet-ef migrations list                     --project src/MMV.Infrastructure --no-build --no-connect
dotnet-ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
```

## 12. Résultats

| Contrôle final | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur** (1 avert. `CS1998` préexistant, hors périmètre) |
| `dotnet test -c Debug` | **276 ✅ / 0 ❌** (App **93** ; Domain **183**) — **> 270** (R2 : +6) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (aucun High/Critical) |
| `migrations list` | **8 migrations** (inchangé) |
| `has-pending-model-changes` | **false** |

## 13. Vulnérabilités

`dotnet list MMV.sln package --vulnerable --include-transitive` ⇒ **aucun** package vulnérable sur les 5
projets. Aucune dépendance ajoutée (le décrément utilise `ExecuteUpdateAsync`, déjà fourni par
`Microsoft.EntityFrameworkCore` 8.0.27 présent). Pins de sécurité P2A-0 (`System.Text.Json` 8.0.6) intacts.

## 14. Risques résiduels (documentés, hors périmètre)

> **Résolu par R2** : la sortie manuelle #2 (`StockMovementFormViewModel`) ne peut plus rendre le stock
> négatif (décrément atomique + transaction ; confirmation négative supprimée).

1. **Sortie fabrication #4** (`OrderDetailViewModel`) — décrément non borné hors écran stock / flux vente.
   *Suite : use case `EnregistrerVente`/fabrication (Étape 2), réutilisant `IStockMutationService`.*
2. **Ajustement d'inventaire #3** (`InventoryViewModel`) — correction **absolue** (set ≥ 0), distincte d'une
   sortie standard ; concurrence non gérée (read→set). *Suite : Étape 2 si nécessaire.*
3. **Agrégats financiers concurrents** — aucun token de ligne (R-09 partiel). *Suite : ADR-010 option 2/5
   au choix du SGBD SaaS.*
4. **R-05 non clos** — orchestration encore dans la VM. *Suite : couche Application (Étape 2).*
5. **Numérotation `Random` (R-03)** — inchangée. *Suite : ADR-006 (P2A-1E).*
6. **SQLite écrivain unique** — concurrence réelle limitée sur poste mono-fichier ; l'update conditionnel
   garantit néanmoins la correction (sérialisé ou entrelacé).

## 15. État Git final

`git status --short` :

```text
 M src/MMV.App/App.axaml.cs
 M src/MMV.App/ViewModels/CustomerDetailViewModel.cs
 M src/MMV.App/ViewModels/CustomersViewModel.cs
 M src/MMV.App/ViewModels/ProductsViewModel.cs
 M src/MMV.App/ViewModels/SaleFormViewModel.cs
 M src/MMV.App/ViewModels/StockMovementFormViewModel.cs
 M src/MMV.App/ViewModels/StockMovementsViewModel.cs
 M src/MMV.Domain/Exceptions/DomainExceptions.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
 M tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs
?? docs/architecture/adr-stock-concurrency.md
?? docs/implementation/P2A-1D-report.md
?? src/MMV.Domain/Interfaces/Persistence/IStockMutationService.cs
?? src/MMV.Infrastructure/Persistence/EfStockMutationService.cs
?? tests/MMV.App.Tests/ViewModels/StockMovementFormViewModelTests.cs
?? tests/MMV.Domain.Tests/Persistence/EfStockMutationServiceTests.cs
```

`git diff --stat` (fichiers suivis) : **10 fichiers modifiés, +263 / −58**. **Aucun commit, aucun push.**

## 16. Verdict

**GO.** Tous les critères d'acceptation P2A-1D **et R2** sont satisfaits :

- ✅ build vert ; ✅ tests verts (**276 > 270**) ; ✅ aucune vulnérabilité High/Critical ;
- ✅ `has-pending-model-changes = false` ; ✅ aucune migration ajoutée (justifié) ;
- ✅ le flux de vente **ne peut plus** rendre le stock négatif (update conditionnel, prouvé) ;
- ✅ **R2** : la **sortie manuelle standard** ne peut plus rendre le stock négatif (MO-1/2/3) ;
- ✅ **R2** : sortie manuelle refusée ⇒ **0 mouvement** persisté, décrément + mouvement **atomiques** (MO-3) ;
- ✅ un cas **concurrent** est testé (une seule réussite, stock final 0) ;
- ✅ stock insuffisant ⇒ **erreur contrôlée** (`InsufficientStockException`, message ViewModel) ;
- ✅ **rollback vente + stock** testé (0 vente, stock inchangé) ;
- ✅ aucune phase P2A-1E+ commencée ; ✅ rapport complet.

### 16.1 Commit & push

- **Commit** : `94ff998` (`94ff99844685330c65bfae279a2595efbdc9ede7`) — `feat(P2A-1D): protect stock
  decrements with atomic conditional updates` — **16 fichiers** (10 modifiés, 6 créés).
- **Branche** : `phase2a-stabilization` poussée vers `origin` (`6f1a030..94ff998`, **aucun force-push**).

### 16.2 Validation CI distante (GitHub Actions)

| Élément | Résultat |
|---|---|
| Run | [#27399900384](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27399900384) (workflow `CI`, event `push`) |
| Commit testé | `94ff99844685330c65bfae279a2595efbdc9ede7` |
| Runner | `windows-latest` |
| SDK | piloté par `global.json` (**8.0.417**, `rollForward: latestFeature`) — step *Setup .NET* ✅ |
| Restore | ✅ success |
| Build | ✅ success (`-c Debug`, sans `-warnaserror` ; `CS1998` préexistant visible, non bloquant) |
| Test | ✅ success (`dotnet test --no-build`, **276** tests — App 93 / Domain 183 ; `.trx` produit) |
| Audit NuGet (High/Critical, JSON + sévérité) | ✅ success — **0 vulnérabilité** |
| `has-pending-model-changes` | **non exécuté en CI** (hors workflow) — **confirmé localement = false** |
| **Statut final du workflow** | ✅ **success** (tous les steps verts) |

**`P2A-1D = GO définitif`** — workflow distant vert sur le commit `94ff998`.

## 17. Prochaine étape candidate (NON exécutée)

**P2A-1E — Numérotation fiable** (R-03 / ADR-006) : remplacer la génération `Random` des `SaleNumber` /
`OrderNumber` par une **séquence transactionnelle déterministe** unique sous concurrence, en réutilisant la
frontière transactionnelle (P2A-1C) et, le cas échéant, le mécanisme de concurrence (P2A-1D).
**Ne pas démarrer** sans paramètres de phase explicites.
