# P2A-1A — Cycle de vie SQLite et chemin de base unique — RAPPORT D'IMPLÉMENTATION

> **Révision R2 (11 juin 2026)** — renforcement obligatoire de l'adoption des bases historiques :
> ajout d'un **portail de compatibilité de schéma** ([`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs))
> qui **inspecte le schéma SQLite réel avant toute écriture de `__EFMigrationsHistory`** et **refuse**
> l'adoption d'une base historique incompatible. **P2A-1A n'accepte QUE les bases historiques compatibles
> avec le modèle courant ; les bases incompatibles sont refusées sans baseline et relèvent de P2A-1B.**
> Sections corrigées : §8, §11, §12, §13, §16, §18.

> **Date : 11 juin 2026.** Branche : `phase2a-stabilization`.
> Périmètre **strictement limité à `P2A-1A`** ([roadmap §P2A-1A](../roadmap/MMV-master-professionalization-roadmap.md),
> [migration-roadmap §Étape 1](../architecture/migration-roadmap.md)).
> ADR opérationnel : [`adr-sqlite-lifecycle.md`](../architecture/adr-sqlite-lifecycle.md) (opérationnalise
> [ADR-003](../architecture/adr-candidates.md#adr-003)).
> **Aucun modèle monétaire, aucune couche Application, aucune entité métier, aucune valeur réglementaire
> n'a été touché.** Aucune phase `P2A-1B+` commencée.

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1A
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun
push**. Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-0B` = GO définitif | [P2A-0B-report.md §22](P2A-0B-report.md) : commit `5676fd9`, CI run #27328934716 vert | ✅ |
| Dépôt propre au démarrage | `git status --short` vide | ✅ |
| Baseline reproductible | restore/build OK, **193/193**, **0 vuln** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |

Aucun prérequis manquant → poursuite autorisée.

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `03bfe65 docs(P2A-0B): record green CI run …`
- `git status --short` : **vide** (arbre propre).
- `git diff` / `git diff --stat` : **vides**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **193 ✅ / 0 ❌** (App 79 ; Domain 114) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |

## 5. Inventaire des chemins SQLite (état AVANT)

**2 chemins physiques distincts** sur **4 sites de configuration** (confirmé par lecture du code) :

| # | Site | Chemin / chaîne | Rôle | Risque | Décision P2A-1A |
|---|---|---|---|---|---|
| 1 | [`App.axaml.cs:108`](../../src/MMV.App/App.axaml.cs#L108) | `Data Source=mmv-optic.db` (relatif) | **Runtime réel** | Chemin relatif ≠ migrations ; dépend du dossier courant | **Unifié** via résolveur |
| 2 | [`OpticDbContext.OnConfiguring`](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L117) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | Fallback (non déclenché si options DI) | Logique de chemin dupliquée | **Délègue** au résolveur |
| 3 | [`OpticDbContextFactory`](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L11) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | Design-time (`dotnet ef`) | Logique dupliquée | **Délègue** au résolveur |
| 4 | [`DependencyInjection.ResolveConnectionString`](../../src/MMV.Infrastructure/DependencyInjection.cs#L53) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | `AddInfrastructure` (jamais appelé) | Logique dupliquée | **Délègue** au résolveur |

Tous les usages de `LocalApplicationData` / `AppData` / `ConnectionString` ont été recensés par recherche
globale (grep) ; aucun autre site de chaîne de connexion n'existe dans `src/`.

## 6. Inventaire `EnsureCreated` / `Migrate` (état AVANT)

| Emplacement | Appel | Rôle | Décision P2A-1A |
|---|---|---|---|
| [`DbInitializer.cs:16`](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16) | `context.Database.EnsureCreated()` | Crée le schéma au runtime (contourne les 7 migrations) + seed admin/démo | **Conservé** (interdiction de suppression prématurée). Devient un **no-op** en prod (la base existe après `Migrate`). Toujours utilisé par les tests. |
| `tests/MMV.Domain.Tests/**` (DbContextTests, 5 ServiceTests, UserRepositoryTests) | `EnsureCreated()` sur SQLite in-memory/temporaire | Schéma de test | **Inchangé** (193 tests préservés) |
| *(aucun)* | `Database.Migrate()` | — | **Introduit** par le service de cycle de vie (prod + nouveaux tests) |

- **7 migrations** présentes (`InitialCreate` → `RestoreSaleOrderSeparation`) + `OpticDbContextModelSnapshot`.
- `dotnet ef migrations has-pending-model-changes` ⇒ *« Changes have been made to the model »* : dérive
  **uniquement** due au défaut figé `HasDefaultValue(DateTime.UtcNow)` (R-19) — **structurellement neutre**,
  **prouvé** par le test d'équivalence (§13). Correctif R-19 hors périmètre.
- `__EFMigrationsHistory` : **absent** des bases `EnsureCreated` (d'où la nécessité d'une adoption).

## 7. ADR et décision retenue

[`adr-sqlite-lifecycle.md`](../architecture/adr-sqlite-lifecycle.md), **ACCEPTÉ**. Quatre options comparées :
1. conserver `EnsureCreated` — **rejetée** (bloque R-04) ;
2. remplacer directement par `Migrate()` partout — **rejetée** (casse bases historiques + tests, viole les interdictions) ;
3. **adoption progressive avec détection des bases historiques** — **RETENUE** ;
4. outil/installeur/pipeline dédié — **différée** (packaging absent R-25 ; trajectoire SaaS, service réutilisable).

**Décision (option 3)** : chemin unique configurable + service de cycle de vie au démarrage
(sauvegarde → détection → `Migrate()` ou **baseline+`Migrate()`** → vérification → journal → échec
explicite), `EnsureCreated` conservé pour les tests, rollback fondé sur la **sauvegarde de fichier** +
forward-fix (pas `Down()`), **aucun seed** par le service.

## 8. Fichiers modifiés / ajoutés

**Modifiés (4) — unification du chemin :**
- [`src/MMV.App/App.axaml.cs`](../../src/MMV.App/App.axaml.cs) — chemin via résolveur ; appel du service de cycle de vie avant le seed ; échec explicite (`DatabaseMigrationException`).
- [`src/MMV.Infrastructure/Data/OpticDbContext.cs`](../../src/MMV.Infrastructure/Data/OpticDbContext.cs) — `OnConfiguring` délègue au résolveur.
- [`src/MMV.Infrastructure/Data/OpticDbContextFactory.cs`](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs) — design-time délègue au résolveur.
- [`src/MMV.Infrastructure/DependencyInjection.cs`](../../src/MMV.Infrastructure/DependencyInjection.cs) — fallback délègue au résolveur.

**Ajoutés — implémentation (5) :**
- [`src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) — source unique du chemin, configurable par environnement (`MMV_DATABASE_PATH`), testable.
- [`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) — sauvegarde/détection/migration/adoption/restauration/vérification ; **portail de compatibilité avant baseline** ; écriture d'historique encapsulée (`WriteMigrationHistory`, point unique).
- [`src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) **(R2)** — compare le schéma SQLite réel au modèle EF courant ; refuse l'adoption si divergence.
- [`src/MMV.Infrastructure/Data/MigrationJournal.cs`](../../src/MMV.Infrastructure/Data/MigrationJournal.cs) — journal de migration (mémoire + fichier).
- [`src/MMV.Infrastructure/Data/DatabaseMigrationException.cs`](../../src/MMV.Infrastructure/Data/DatabaseMigrationException.cs) — échec explicite.

**Ajoutés — ADR + tests + rapport (4) :**
- [`docs/architecture/adr-sqlite-lifecycle.md`](../architecture/adr-sqlite-lifecycle.md)
- [`tests/MMV.Domain.Tests/Data/SqliteDatabasePathResolverTests.cs`](../../tests/MMV.Domain.Tests/Data/SqliteDatabasePathResolverTests.cs)
- [`tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs`](../../tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs)
- `docs/implementation/P2A-1A-report.md` (ce rapport)

**Aucune entité, aucun ViewModel, aucune configuration EF, aucun `DbInitializer`, aucune migration** modifiés.

## 9. Migrations créées ou non créées

**Aucune migration créée.** Les **7 migrations existantes** sont **adoptées** telles quelles (baseline pour
les bases historiques, application normale pour les bases vierges/gérées). `OpticDbContextModelSnapshot`
**inchangé**. La dérive R-19 (`has-pending-model-changes`) n'est **pas** corrigée ici (hors périmètre ;
structurellement neutre, prouvé §13).

```
dotnet ef migrations list →
  20260127184542_InitialCreate
  20260129192001_AddProductEntryDate
  20260201181136_ProductSchemaRefactoring
  20260202005050_AddNotifications
  20260212164646_AddCounterSaleFieldsToOrder
  20260212173313_AddDepositAndRemainingAmountToOrder
  20260212220902_RestoreSaleOrderSeparation
```

## 10. Stratégie base vierge (installation neuve)

Aucun fichier de base ⇒ état `Empty` ⇒ `context.Database.Migrate()` : crée le schéma **via les
migrations** + `__EFMigrationsHistory` (toutes appliquées). **Aucune donnée seedée par le service**
(le seed admin/démo reste l'affaire de `DbInitializer`, inchangé). Création du dossier de données si
nécessaire. Couvert par tests (§13).

## 11. Stratégie base historique (créée par `EnsureCreated`) — renforcée (R2)

Tables applicatives présentes **sans** `__EFMigrationsHistory` ⇒ état `HistoricalWithoutMigrationsHistory`.
Séquence **renforcée** (R2) :

1. **sauvegarde** préalable (déjà créée avant détection) ;
2. **portail de compatibilité de schéma** ([`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs))
   — ouverture en lecture, inspection du schéma **réel** (PRAGMA `table_info`/`foreign_key_list`/`index_list`),
   comparaison au schéma **attendu** dérivé du modèle EF (`IModel.GetRelationalModel()`) ;
3. **si divergence significative** (table/colonne/type-affinité/nullabilité/clé primaire/clé étrangère
   essentielle/index unique essentiel) ⇒ **adoption REFUSÉE** : `DatabaseMigrationException`, **aucune
   écriture** de `__EFMigrationsHistory`, base + sauvegarde **conservées**, **journal motivé** ;
4. **si compatible** ⇒ baseline : inscription des migrations dans `__EFMigrationsHistory` (point unique
   `WriteMigrationHistory`, via `IHistoryRepository`, en transaction), **sans ré-exécuter le DDL** ⇒
   **aucune perte de données** ⇒ application des migrations réellement en attente ⇒ **vérifications
   post-adoption** : `__EFMigrationsHistory` présent, aucune migration en attente, **agrégats principaux
   lisibles par EF**.

**Décision claire : P2A-1A n'accepte QUE les bases historiques compatibles avec le modèle courant.** Une
base **incompatible/ancienne n'est jamais transformée en base « migrée »** par simple inscription : elle
est **refusée proprement**. Son **diagnostic, sa réparation et sa migration** relèvent de **P2A-1B**.
La compatibilité du cas nominal (`EnsureCreated` du modèle courant) est prouvée par le test d'équivalence
structurelle et le test de non-faux-positif (§13). Testé **sur une copie** d'une base historique peuplée.

## 12. Sauvegarde et restauration

- **Sauvegarde** : avant toute mutation d'une base existante, copie horodatée du `.db` (+ `-wal`/`-shm`
  si présents) dans `…/backups/`. Réalisée **avant** la détection, le portail de compatibilité et
  l'adoption ⇒ une base **refusée** dispose toujours d'une sauvegarde.
- **Restauration** : `Restore(backup, cible)` (mécanisme « restauration du fichier SQLite sauvegardé »,
  ADR-003 §C). **`Down()` n'est pas** utilisé pour restaurer des données.
- **Aucune suppression silencieuse** : en cas d'échec **ou de refus d'adoption**, base + sauvegarde
  **conservées**, `__EFMigrationsHistory` **non inscrite**, `DatabaseMigrationException` explicite et
  journal motivé. Le support peut restaurer la sauvegarde ; la base incompatible relève de P2A-1B.

## 13. Tests ajoutés (25, fichiers temporaires isolés)

`SqliteDatabasePathResolverTests` (**7**) : défaut LOCALAPPDATA ; surcharge par variable
d'environnement ; priorité du chemin explicite ; extraction depuis chaîne de connexion ; format
`Data Source=` ; création de dossier ; no-op `:memory:`.

`SqliteDatabaseManagerTests` (**18**) :

| Test | Cas couvert |
|---|---|
| `PrepareDatabase_FreshInstall_AppliesMigrations_AndRecordsHistory` | base vierge |
| `PrepareDatabase_FreshInstall_DoesNotSeedAnyDemoData` | absence de données démo implicites |
| `Migrate_And_EnsureCreated_ProduceEquivalentSchema` | **preuve** : migrations ≡ modèle (non destructif) |
| `PrepareDatabase_HistoricalDatabaseOnCopy_IsAdopted_WithoutDataLoss` | base historique **compatible** + copie |
| `PrepareDatabase_AlreadyMigratedDatabase_IsIdempotent_AndBacksUp` | base avec `__EFMigrationsHistory` |
| `PrepareDatabase_ExistingDatabase_CreatesVerifiableBackupBeforeMutation` | sauvegarde avant mutation |
| `Restore_AfterUnwantedMutation_RecoversBackedUpState` | restauration après erreur simulée |
| `PrepareDatabase_InvalidDatabaseFile_ThrowsControlledException_AndPreservesFile` | échec contrôlé + aucune suppression |
| `PrepareDatabase_WritesMigrationJournal` / `MigrationJournal_WithFile_AppendsEntriesToDisk` | journal |
| **`SchemaVerifier_ValidEnsureCreatedDatabase_IsCompatible`** (R2) | **anti-faux-positif** : base valide acceptée |
| **`PrepareDatabase_Historical_MissingTable_RefusesAdoption`** (R2) | table manquante ⇒ refus |
| **`PrepareDatabase_Historical_MissingColumn_RefusesAdoption`** (R2) | colonne manquante ⇒ refus |
| **`PrepareDatabase_Historical_IncompatibleColumnType_RefusesAdoption`** (R2) | type incompatible ⇒ refus |
| **`PrepareDatabase_Historical_MissingForeignKey_RefusesAdoption`** (R2) | clé étrangère manquante ⇒ refus |
| **`PrepareDatabase_Historical_MissingUniqueIndex_RefusesAdoption`** (R2) | index unique manquant ⇒ refus |
| **`PrepareDatabase_PartiallyOldSchema_RefusesAdoption_WithoutWritingHistory`** (R2) | schéma partiellement ancien ⇒ refus |
| **`Baseline_WritesAndReadsBackMigrationHistory`** (R2) | intégration : écriture **+ relecture** de `__EFMigrationsHistory` |

Chaque test de refus (R2) vérifie : **sauvegarde créée**, **exception contrôlée**, **`__EFMigrationsHistory`
non inscrite**, **base préservée**, **journal indiquant la raison** (`ADOPT REFUSED`).

« Chemin configuré en environnement test » : couvert transversalement (chemins temporaires injectés,
jamais `%LOCALAPPDATA%`). Non-régression des 193 : §15.

## 14. Commandes exécutées (principales)

```bash
# Baseline + analyse
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff
dotnet --version ; dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet ef migrations list --project src/MMV.Infrastructure --no-build --no-connect
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build

# Contrôles finaux (après implémentation)
dotnet restore MMV.sln
dotnet build  MMV.sln --no-restore -c Debug
dotnet test   MMV.sln --no-build  -c Debug
dotnet list   MMV.sln package --vulnerable --include-transitive
git status --short ; git diff --stat
```

## 15. Résultats / contrôles finaux

| Contrôle | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur** (avert. `CS1998` préexistant, hors périmètre) |
| `dotnet test -c Debug` | **218 ✅ / 0 ❌** — App **79/79** ; Domain **139/139** (114 + **25** nouveaux) |
| Non-régression des 193 | ✅ (114 Domain + 79 App préservés) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (5 projets) |
| Preuve `Migrate()` ≡ `EnsureCreated()` | ✅ schémas structurellement identiques |
| Portail de compatibilité (R2) | ✅ 6 catégories de divergence refusées ; base valide acceptée (anti-faux-positif) |
| `git diff --stat` (suivi) | 4 fichiers modifiés + 8 fichiers ajoutés |

## 16. Risques résiduels

1. **Dérive R-19** (`HasDefaultValue(DateTime.UtcNow)`) — `has-pending-model-changes` reste *true* ;
   cosmétique (valeur par défaut), **structurellement neutre** (prouvé) ; correctif différé (Étape 1 / R-19).
2. **Bascule du chemin runtime** `mmv-optic.db` → `%LOCALAPPDATA%\ManageMyVision\mmv.db` : un éventuel
   fichier `mmv-optic.db` préexistant (données de démo de dev) n'est **pas** supprimé et **pas** repris
   automatiquement ; sa reprise éventuelle relève de **P2A-1B**. Aucune perte (fichier conservé).
3. **Bases à schéma antérieur/incompatible au modèle courant** — **refusées** par le portail de
   compatibilité (R2), sans baseline ni perte (sauvegarde + journal). Leur **diagnostic, réparation et
   migration** relèvent de **P2A-1B** (non traités ici). La comparaison est **pragmatique** (affinité
   SQLite ; ne signale que le manquant/incompatible attendu) : un schéma divergent **non couvert** par les
   contrôles resterait théoriquement accepté — atténué par la preuve d'équivalence et l'anti-faux-positif ;
   l'analyse fine relève de P2A-1B.
4. **Seed de démo en production** (R-16) — **inchangé** (le service n'ajoute aucune donnée) ; suppression
   différée à l'**Étape 1F**. Le compte admin reste seedé par `DbInitializer` (login préservé).
5. **Échec de migration au démarrage** propagé (app stoppée) : explicite et compréhensible ; une boîte de
   dialogue conviviale relève de l'UI (hors périmètre).
6. **Verrou mono-instance** : non implémenté ici (SQLite desktop mono-poste) ; à traiter avec le
   packaging/MAJ (R-25) ou un futur déclencheur installeur/pipeline (ADR option 4). Sauvegarde réalisée
   avant mutation atténue le risque.
7. **`CS1998`** préexistant — inchangé, hors périmètre.

Aucun de ces risques ne touche au métier, au réglementaire, ni aux interdictions de la phase.

## 17. État Git final

`git status --short` :

```
 M src/MMV.App/App.axaml.cs
 M src/MMV.Infrastructure/Data/OpticDbContext.cs
 M src/MMV.Infrastructure/Data/OpticDbContextFactory.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
?? docs/architecture/adr-sqlite-lifecycle.md
?? docs/implementation/P2A-1A-report.md
?? src/MMV.Infrastructure/Data/DatabaseMigrationException.cs
?? src/MMV.Infrastructure/Data/MigrationJournal.cs
?? src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs
?? src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs
?? src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs
?? tests/MMV.Domain.Tests/Data/SqliteDatabaseManagerTests.cs
?? tests/MMV.Domain.Tests/Data/SqliteDatabasePathResolverTests.cs
```

**4 fichiers modifiés** (unification du chemin + portail/encapsulation dans le manager) + **8 fichiers
ajoutés** (1 ADR, 5 sources, 2 tests — le rapport étant le 9ᵉ). **Aucun commit, aucun push** (conforme à
`ALLOW_COMMIT=false`, `ALLOW_PUSH=false`).

## 18. Verdict — GO / NO-GO

### **P2A-1A = GO**

| Critère d'acceptation (consigne §11) | État |
|---|---|
| Build vert | ✅ 0 erreur |
| Tests verts (> 210, tests d'échec de schéma ajoutés) | ✅ **218/218** (193 préservés + 25) |
| Aucune vulnérabilité High/Critical | ✅ 0 |
| Chemin SQLite unique et documenté | ✅ résolveur unique, 4 sites unifiés, ADR + §5 |
| Base vierge testée | ✅ |
| Base historique **compatible** testée sur copie | ✅ |
| **Base historique incompatible refusée sans baseline** (R2) | ✅ 6 catégories + schéma partiellement ancien |
| **Vérification du schéma réel avant écriture de `__EFMigrationsHistory`** (R2) | ✅ portail de compatibilité |
| Sauvegarde avant migration testée | ✅ |
| Restauration / reprise testée | ✅ |
| Aucun code métier hors périmètre modifié | ✅ (entités, ViewModels, Money, Application, DbInitializer, migrations intacts ; version EF Core inchangée) |
| Aucun début de P2A-1B ou autre phase | ✅ |
| Rapport complet | ✅ (ce document) |

## 19. Prochaine étape candidate (NON exécutée)

`P2A-1B` — « Migration des données historiques » : outil de diagnostic de schéma, traitement des bases à
schéma antérieur, contrôle avant/après, procédure de reprise. **Non commencée.** Ne pas démarrer sans
verdict `P2A-1A` confirmé par revue humaine, puis `TARGET_PHASE_ID = P2A-1B` explicite.

> **Arrêt obligatoire.** Fin de `P2A-1A`. Aucun commit, aucun push, aucune amorce de `P2A-1B`.
