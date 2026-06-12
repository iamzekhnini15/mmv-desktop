# P2B-2A — ADR et frontières de couches — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2A**. Branche `p2b-architecture`.
> Date : 12 juin 2026. **Mode : ANALYZE_AND_DOCUMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> Phase de **cadrage** : définir les frontières Domain / Application / Infrastructure / UI **avant** de créer
> la couche Application. **Aucune couche Application implémentée, aucun vertical slice commencé, aucune refonte
> de `SaleFormViewModel`, aucune migration, aucune règle métier modifiée.**

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2A` |
| `EXECUTION_MODE` | `ANALYZE_AND_DOCUMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

**Objectif strict** : produire une décision claire sur les responsabilités de chaque couche, la place des
transactions, repositories, validations, mapping, erreurs, le style applicatif et le plan de migration
progressif — sous forme d'ADR + plan + rapport. **Préparer** l'architecture, **pas** déplacer le code.

---

## 2. Prérequis

**P2A terminée — verdict définitif** (préalables satisfaits) :

| Étape | Verdict | Preuve |
|---|---|---|
| P2A-1C | GO définitif | run #27367443700, commit `186d995` |
| P2A-1D | GO définitif | run #27399900384, commit `94ff998` |
| P2A-1E | GO définitif | run #27402041800, commit `813d115` |
| P2A-1F | GO définitif | run **#27431491510** (run #15), commit `29a80a6` |

Dernier commit validé : `29a80a6 feat(P2A-1F): gate demo seeds by environment`. Branche de travail :
`p2b-architecture` (issue de `phase2a-stabilization` mergée via PR #11, `0ad514a`). Dépôt **propre**, tests
**verts**, `has-pending=false` ⇒ démarrage autorisé.

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → 0ad514a Merge pull request #11 from iamzekhnini15/phase2a-stabilization
                            10d5bf2 docs(P2A-1F): record green CI run 27431491510 (GO definitif)
                            29a80a6 feat(P2A-1F): gate demo seeds by environment
                            c3f1f55 docs(P2A-1E): record green CI run 27402041800 (GO definitif)
                            813d115 feat(P2A-1E): replace random document numbers with transactional sequences
git diff --stat           → (vide)
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ Succès (projets à jour) |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avertissement** (`CS1998` préexistant, [OrderFormViewModel.cs:458](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec, 0 ignoré |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list --project src/MMV.Infrastructure --no-build --no-connect` | **9 migrations** (`InitialCreate` → `AddDocumentSequences`) |
| `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | ✅ **false** (« No changes have been made to the model… ») |

Dépôt propre, suite verte, aucune vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies.

---

## 5. Analyse des responsabilités actuelles (cartographie vérifiée — le dépôt prime)

### 5.1 Carte des couches

| Couche | Projet | Responsabilités réelles | Dépendances (`.csproj`) |
|---|---|---|---|
| **UI/App** | `MMV.App` (WinExe) | Vues `.axaml`, ViewModels (**orchestration métier**), validation d'entrée, mapping, **composition root** unique ([App.axaml.cs](../../src/MMV.App/App.axaml.cs)), services UI (Navigation, Dialog, Session, Permission, Theme) | → `MMV.Domain`, `MMV.Infrastructure` ; Avalonia 11.2.8, MVVM, MS.DI/Hosting, EF.Design |
| **Application future** | *(inexistant)* | — | — |
| **Domain** | `MMV.Domain` (classlib) | Entités anémiques, enums, VO **morts**, validators (FluentValidation), services de domaine **transaction-script** (contournés), exceptions, **interfaces repos**, **ports de persistance** (P2A), entité `DocumentSequence` | → **(aucune réf. projet)** ; **FluentValidation 11.9.0** seul → **domaine pur confirmé** |
| **Infrastructure** | `MMV.Infrastructure` (classlib) | `OpticDbContext`, configs EF, repositories, `UnitOfWork`, implémentations P2A (`Persistence/`), cycle de vie SQLite (`Data/`), seeding/config (`Configuration/`), `AuthenticationService` | → `MMV.Domain` ; EF Sqlite 8.0.27, BCrypt, Config.Abstractions, System.Text.Json 8.0.6 (pin) |
| **Tests** | `MMV.Domain.Tests`, `MMV.App.Tests` | Domain.Tests = domaine **+ Infrastructure** (tests **vrai SQLite** : `Persistence/`, `Data/`, `Configuration/`) ; App.Tests = ViewModels/services UI | Domain.Tests → Domain + Infra ; App.Tests → App |

### 5.2 Où se trouve la logique métier
- **Majoritairement dans les ViewModels** (R-05). Preuve : [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L909-L1057)
  orchestre un **cas d'utilisation complet** (numéro de vente, agrégat `Sale`/`SaleItem`, statut, commande
  fournisseur `Order`, décrément de stock + `StockMovement`, transaction).
- **Dans les services de domaine** (`SaleService`, `OrderService`, …) mais **contournés** : non enregistrés
  dans le composition root réel ([App.axaml.cs:105-166](../../src/MMV.App/App.axaml.cs#L105-L166)) → **morts
  au runtime**, utilisés seulement par les tests. Et **incomplets** : [`SaleService.CreateSaleAsync`](../../src/MMV.Domain/Services/SaleService.cs#L46-L61)
  ne gère ni commande, ni stock, ni transaction.
- **Entités anémiques** : aucun invariant encapsulé (champs publics `get; set;`).

### 5.3 ViewModels qui orchestrent trop
[`SaleFormViewModel`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) (le pire, cible prioritaire),
[`OrderFormViewModel`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs),
[`StockMovementFormViewModel`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs),
[`OrderDetailViewModel`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs) (stock de fabrication),
[`InventoryViewModel`](../../src/MMV.App/ViewModels/InventoryViewModel.cs).

### 5.4 Services de domaine utilisés ou contournés
**Contournés.** `SaleService`/`OrderService`/`CustomerService`/`ProductService`/`PrescriptionService`
enregistrés **uniquement** par [`AddInfrastructure`](../../src/MMV.Infrastructure/DependencyInjection.cs#L55-L61),
**jamais appelé** ⇒ double module de DI résiduel (V1/V2). Le composition root réel ne les enregistre pas.

### 5.5 Où sont les transactions
Port [`ITransactionRunner`](../../src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs) (Domain) +
[`EfTransactionRunner`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs) (Infra), **invoqué
depuis la VM** (`SaleFormViewModel.ExecuteSave` → `RunAsync(PersistSaleAsync)`). `UnitOfWork.BeginTransaction/
Commit/Rollback` existent mais `RollbackAsync` **dispose ≠ rollback** (R-23) et est **contourné**.

### 5.6 Où sont les repositories
Interfaces dans `MMV.Domain.Interfaces.Repositories`, implémentations dans `MMV.Infrastructure.Repositories`,
**consommés directement par les ViewModels** (V3/V6). `SaleFormViewModel` dépend de 5 repos + `IUnitOfWork`.

### 5.7 Où sont les erreurs métier / techniques
- **Métier** : hiérarchie `DomainException` ([DomainExceptions.cs](../../src/MMV.Domain/Exceptions/DomainExceptions.cs)) —
  `BusinessRuleException`, `InsufficientStockException`, `NumberSequenceException`, `EntityNotFoundException`,
  `DuplicateEntityException`. **Attrapées dans les VM**.
- **Techniques** : [`PersistenceException`](../../src/MMV.Domain/Exceptions/PersistenceException.cs) (type Domain)
  + [`PersistenceErrorMapper`](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs) (Infra,
  mappe EF/SQLite → message assaini). **Attrapées dans les VM**.

### 5.8 Où sont les validations
Trois lieux, **dispersés** : (a) **UI** (VM : `ErrorMessage`, panier non vide) ; (b) **Domain validators**
FluentValidation (`CustomerValidator`, `ProductValidator`, `PrescriptionValidator`, `UserValidator`) ;
(c) **services de domaine** (`SaleService.CalculateSaleAsync` : `BusinessRuleException` remise négative/>
total).

### 5.9 Où l'UI dépend directement d'Infrastructure
- **Composition root** ([App.axaml.cs](../../src/MMV.App/App.axaml.cs)) référence les types **concrets**
  d'Infrastructure (`UnitOfWork`, repos, `Ef*`, `SqliteDatabaseManager`, `DatabaseSeeder`) — **acceptable**
  pour la racine.
- **Structurellement problématique** : les **ViewModels** consomment des abstractions de persistance
  (`I*Repository`, `IUnitOfWork`, ports P2A) **résolues vers Infrastructure** et orchestrent l'écriture.

### 5.10 Dépendances violant une architecture propre
| # | Violation | Preuve | Statut |
|---|---|---|---|
| V3 | logique métier dans l'UI (R-05) | `SaleFormViewModel.PersistSaleAsync` | **ouvert** (cible P2B) |
| V5 | absence de couche Application | aucun projet `MMV.Application` | **ouvert** (cible P2B) |
| V6 | UI → repos directement | `SaleFormViewModel` (5 repos + UoW) | **ouvert** (cible P2B) |
| V1/V2 | double DI, `AddInfrastructure` mort | non appelé, services de domaine morts | **ouvert** (assainir P2B-2B) |
| R-23 | `UnitOfWork.RollbackAsync` trompeur | dispose ≠ rollback | contourné (assainir après migration) |
| V7 | value objects morts | `Money`/`Address`/`Email`/`PhoneNumber` non instanciés en `src/` | différé (Étape 3+) |
| ✅ | **domaine pur** | `.csproj` Domain : 0 réf. projet, FluentValidation seul | **préservé** |

---

## 6. Problèmes d'architecture restants

1. **R-05 (CRITICAL)** — orchestration métier dans les ViewModels (non testable hors UI, non réutilisable,
   bloque la variabilité nationale future).
2. **Couche Application absente (V5)** — pas de réceptacle pour use cases, politiques datées, horloge, mapping.
3. **UI → persistance directe (V6)** — couplage cross-couche.
4. **Double DI / services de domaine morts (V1/V2)** — `AddInfrastructure` jamais appelé.
5. **R-23** — primitive `UnitOfWork` de rollback défaillante, contournée mais présente.
6. **Modèle anémique + VO morts (V7)** — relèvent du modèle monétaire et de l'enrichissement (hors P2B).
7. **Validation/erreurs dispersées** — à clarifier par couche (sans les déplacer en P2B-2A).

---

## 7. Options comparées (détail dans l'ADR)

| # | Option | Verdict |
|---|---|---|
| 1 | Garder la logique dans les ViewModels (statu quo) | **Rejeté** (cause de R-05) |
| 2 | Services de domaine appelés directement par l'UI | **Rejeté** (réceptacle inadéquat ; services transaction-script incomplets) |
| 3 | **Couche Application avec services applicatifs simples / use cases** | **RETENU** |
| 4 | CQRS léger (Commands/Handlers + dispatcher) | **Différé** (option 3 y reste compatible, sans rupture) |
| 5 | Vertical slices complets | **Différé** (1er slice = P2B-2C, après P2B-2B) |
| 6 | Clean Architecture stricte (big-bang) | **Rejeté comme palier** (cible asymptotique, par lots) |

---

## 8. Décision retenue

**Créer une couche Application progressive `MMV.Application`**, en **use cases / services applicatifs
simples**, **compatible CQRS léger** ultérieur, migrée par **strangler pattern** (un parcours à la fois),
**sans dépendance Avalonia/EF**, en **réutilisant** les primitives P2A (`ITransactionRunner`,
`IStockMutationService`, `INumberSequenceService`) inchangées. Règles de dépendances cibles :

```
MMV.Domain        → (rien)                                  [pur]
MMV.Application   → MMV.Domain                              [sans EF/Avalonia]
MMV.Infrastructure→ MMV.Domain                             [implémente les ports]
MMV.App           → MMV.Domain, MMV.Application, MMV.Infrastructure   [composition root]
UI → Application → Domain ← Infrastructure
```

Répartition : **Domain** (entités, VO, règles pures, interfaces, exceptions) ; **Application**
(orchestration des use cases, Command/Result, mapping, validation de commande, ouverture de transaction) ;
**Infrastructure** (EF, repos, UoW, implémentations des ports, SQLite, seeding, auth) ; **UI** (vues, VM
réduits à présentation/validation d'entrée/mapping, services UI, composition root). Transactions via le port
runner appelé **par le use case** ; repos consommés **par Application** ; erreurs via les types P2A
conservés ; validation à 3 niveaux **non redondants** ; mapping VM↔Command↔Domain↔Result.
**Premier use case cible : `EnregistrerVente`** (non implémenté ici). Détails :
[adr-application-boundaries.md](../architecture/adr-application-boundaries.md) et
[application-layer-migration-plan.md](../architecture/application-layer-migration-plan.md).

---

## 9. Documents créés

| Fichier | Rôle |
|---|---|
| [`docs/architecture/adr-application-boundaries.md`](../architecture/adr-application-boundaries.md) | ADR — frontières de couches + style applicatif (6 options comparées, décision, règles de dépendances, transactions/repos/erreurs/validations/mapping/tests/migration) |
| [`docs/architecture/application-layer-migration-plan.md`](../architecture/application-layer-migration-plan.md) | Plan de migration (ordre de création, dépendances, 1er use case, stratégie `SaleFormViewModel`, tests, GO/NO-GO, anti-patterns) |
| [`docs/implementation/P2B-2A-report.md`](P2B-2A-report.md) | Ce rapport |

**Aucun fichier de code (`src/`, `tests/`) créé ou modifié.**

---

## 10. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `has-pending-model-changes`
reste **false** ; les **9** migrations existantes sont intactes. P2B-2A ne touche pas au schéma.

---

## 11. Tests exécutés

Baseline uniquement (phase documentaire, aucun code modifié) :
`dotnet restore` ✅ · `dotnet build -c Debug` ✅ (1 avert. `CS1998` préexistant) · `dotnet test --no-build -c
Debug` ✅ **320** · `dotnet list … --vulnerable` ✅ 0 · `dotnet ef migrations list` (9) · `has-pending-model-changes`
✅ false. **Aucun test ajouté ni modifié.**

---

## 12. Résultats

| Contrôle | Résultat |
|---|---|
| `restore` | ✅ à jour |
| `build -c Debug` | ✅ Succès — 1 avert. `CS1998` (préexistant, hors périmètre) |
| `test --no-build` | ✅ **320** (Domain 223 + App 97), 0 échec |
| `list package --vulnerable` | ✅ **0 vulnérabilité** |
| `migrations list` | **9** migrations (inchangé) |
| `has-pending-model-changes` | ✅ **false** |

(Build et tests inchangés par la phase : seuls des fichiers Markdown ont été ajoutés.)

---

## 13. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les 5 projets, transitifs inclus. Aucun paquet ajouté. Pins de
sécurité P2A intacts (`System.Text.Json` 8.0.6, `Tmds.DBus.Protocol` 0.21.3). Pin `FluentAssertions`
maintenu en 6.x.

---

## 14. Warnings résiduels

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` | [`OrderFormViewModel.cs:458`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458) | **Préexistant**, hors périmètre P2B-2A (méthode async sans `await`). Non modifié. Candidate d'hygiène future. |

---

## 15. Risques résiduels

1. **R-05 non clos** — la décision le **cadre** ; la fermeture est P2B-2B (couche) / P2B-2C (slice). 
2. **Double DI (V1/V2)** — `AddInfrastructure` mort ; **à assainir** en P2B-2B (DI unifiée).
3. **R-23** — `UnitOfWork.RollbackAsync` trompeur, contourné ; assainissement après migration des flux.
4. **VO morts / entités anémiques (V7)** — différés (modèle monétaire Étape 3, enrichissement par lots).
5. **Perf (over-fetching, filtrage mémoire)** — non aggravée, non traitée en P2B.
6. **Autorisation UI-only** — point d'application futur (Application/Domain), Programmes 3/7.
7. **`CS1998`** résiduel — hygiène, hors périmètre.

Aucun ne touche au réglementaire ni aux interdictions de la phase. **Aucune valeur de gate introduite.**

---

## 16. État Git final

**Clôture Git (autorisation explicite `ALLOW_COMMIT=true`, `ALLOW_PUSH=true`)** : les **3 fichiers Markdown**
P2B-2A ont été committés tels quels puis la branche poussée (sans force-push).

```
git add docs/architecture/adr-application-boundaries.md \
        docs/architecture/application-layer-migration-plan.md \
        docs/implementation/P2B-2A-report.md
git commit -m "docs(P2B-2A): define application layer boundaries"
  → 19ace13  3 files changed, 911 insertions(+)
git push origin p2b-architecture
  → [new branch] p2b-architecture -> p2b-architecture (sans force)
```

`git show --stat 19ace13` (3 fichiers, +911) :

```
docs/architecture/adr-application-boundaries.md          | 359 +++
docs/architecture/application-layer-migration-plan.md    | 214 +++
docs/implementation/P2B-2A-report.md                     | 338 +++
```

`git status --short` après commit : **propre**. `git diff --check` : **0 anomalie** (seuls des avis
LF→CRLF normaux sous Windows). Le commit ne contient **que** les 3 documents P2B-2A ; **aucun** fichier
`src/`/`tests/`, `bin/`/`obj/`, `*.db`/`*.trx`/`*.zip`, secret ou temporaire. **Aucun projet
`MMV.Application`, aucun vertical slice, aucune migration.**

> Une mise à jour documentaire ultérieure (cette section 16/17 + §17 bis) a été committée séparément pour
> consigner le résultat réel de la vérification CI distante.

---

## 17. Verdict — GO / NO-GO

### Critères d'acceptation P2B-2A

| Critère | Statut |
|---|---|
| Build vert | ✅ (1 avert. `CS1998` préexistant) |
| Tests verts (**320**) | ✅ |
| Aucune vulnérabilité High/Critical | ✅ (0) |
| `has-pending-model-changes = false` | ✅ |
| Aucune migration créée | ✅ |
| Aucune couche Application implémentée massivement | ✅ (aucun code `src/`) |
| Aucun vertical slice commencé | ✅ |
| ADR application boundaries créé | ✅ [adr-application-boundaries.md](../architecture/adr-application-boundaries.md) |
| Plan de migration application créé | ✅ [application-layer-migration-plan.md](../architecture/application-layer-migration-plan.md) |
| Rapport complet | ✅ (ce document) |
| Aucune règle métier modifiée | ✅ |
| Commit & push (sur autorisation explicite) | ✅ (`19ace13`, branche `p2b-architecture`) |

### ✅ **P2B-2A = GO DÉFINITIF** — **validation distante obtenue** via le run déclenché après extension des triggers CI (run #19, `27441381061`, commit `3042e52`, branche `p2b-architecture`).

La phase a produit la décision d'architecture (frontières + style applicatif + règles de dépendances), le
plan de migration progressif et le rapport, **sans** implémenter la couche Application ni commencer de
vertical slice, **sans** migration, **sans** modification de code ni de règle métier. La gate « pipeline
distant vert » est **levée** (cf. §17 bis).

### 17 bis. Validation CI distante (run réel)

**Mise à jour** : initialement, le push de `p2b-architecture` n'avait **pas** déclenché la CI (la branche
n'était pas dans les déclencheurs du workflow). La micro-phase **P2B-2A-CI** a **étendu les triggers** à
`main`/`phase*`/`p2*` (commit `3042e52`, cf. [P2B-2A-CI-report](P2B-2A-CI-report.md)). Le push de ce commit
a **déclenché** le pipeline, qui est **vert** et **couvre** les documents P2B-2A (ancêtres `19ace13` /
`ea382c5` sur la même branche).

| Élément | Valeur réelle |
|---|---|
| **Run** | **#19** — id **`27441381061`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27441381061 |
| **Commit testé** | **`3042e52`** (tip de `p2b-architecture` ; ancêtres = `19ace13` ADR/plan/rapport P2B-2A, `ea382c5`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement / workflow | `push` / `CI` — `Restore / Build / Test / Scan` |
| Runner | `windows-latest` |
| SDK | verrouillé par `global.json` (8.0.x) — *Setup .NET* ✅ |
| Restore | ✅ **success** |
| Build | ✅ **success** (`-c Debug` ; `CS1998` préexistant visible, non bloquant) |
| Test | ✅ **success** (suite **320**) |
| Audit NuGet (High/Critical) | ✅ **success** — **0 vulnérabilité** |
| `has-pending-model-changes` | **non exécuté en CI** — **confirmé localement = false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps en conclusion `success`. **VALIDATION DISTANTE OBTENUE** : la gate roadmap « pipeline distant
vert » est levée pour P2B-2A.

---

## 18. Prochaine étape candidate (NON exécutée)

**`P2B-2B` — Création de la couche Application** (Programme 2) : créer le projet `MMV.Application`
(`classlib net8.0`, → `MMV.Domain` seul), poser l'arborescence des use cases, **unifier la DI** (assainir le
résidu `AddInfrastructure` mort), brancher `MMV.App → MMV.Application` — **sans** changement de comportement
utilisateur et **sans** encore migrer `SaleFormViewModel` (lequel relève de **P2B-2C**, premier vertical
slice `EnregistrerVente`).

> **Ne pas démarrer** sans revue humaine du présent rapport, accord explicite sur les frontières/le style/
> les règles de dépendances, et `TARGET_PHASE_ID = P2B-2B` fourni. **Claude ne lance jamais seul l'étape
> suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2A`. Aucun commit, aucun push, aucune amorce de couche Application, aucun vertical slice.
