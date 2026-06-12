# P2A-1R19 — Correction du bruit EF Core « pending model changes » — RAPPORT D'IMPLÉMENTATION

> **Date : 11 juin 2026.** Branche : `phase2a-stabilization`.
> Mini-phase technique **strictement limitée** à la correction du bruit
> `dotnet ef migrations has-pending-model-changes = true` causé par l'anti-pattern **R-19**
> (`HasDefaultValue(DateTime.UtcNow)`), documenté comme risque résiduel par
> [`P2A-1A-report.md §16.1`](P2A-1A-report.md), [`P2A-1B-report.md §11/§16.1`](P2A-1B-report.md) et
> [`adr-sqlite-lifecycle.md §7.1`](../architecture/adr-sqlite-lifecycle.md). Correctif prévu par la
> [feuille de route de migration §Étape 1](../architecture/migration-roadmap.md) (« Corriger les valeurs
> par défaut figées (R-19) »).
>
> **Aucun modèle monétaire, aucune couche Application, aucune transaction métier, aucune concurrence de
> stock, aucune numérotation, aucune entité métier, aucune valeur réglementaire** n'a été touché.
> **Aucune phase `P2A-1C+` commencée.**
>
> **Révision R2 (11 juin 2026)** — durcissement obligatoire du cas des **bases historiques sans
> `__EFMigrationsHistory` contenant les anciens DEFAULT `DateTime` figés** (créées par `EnsureCreated`
> avant P2A-1R19). Le portail de compatibilité `SqliteSchemaVerifier` ne comparant pas les valeurs
> DEFAULT, une telle base serait baselinée à tort (marquant `FixDateTimeDefaultValues` appliquée sans
> l'exécuter). Ajout d'un vérificateur physique dédié et d'une **réparation contrôlée** à l'adoption.
> Sections ajoutées/corrigées : §6 bis, §7, §9, §11, §12, §14. Aucune valeur reçue (paramètres) ne change.

---

## 1. Paramètres reçus

```text
TARGET_PHASE_ID = P2A-1R19
EXECUTION_MODE  = IMPLEMENT
ALLOW_COMMIT    = false
ALLOW_PUSH      = false
```

Conséquences : modifications de fichiers autorisées dans le périmètre exact ; **aucun commit, aucun
push**. Le changeset reste dans l'arbre de travail.

## 2. Prérequis

| Prérequis | Vérification | État |
|---|---|---|
| `P2A-1B` = GO définitif | [P2A-1B-report.md §19](P2A-1B-report.md) : commit `a39c855`, CI run #27343372219 vert, 233/233 | ✅ |
| Dépôt propre au démarrage | `git status --short` **vide** | ✅ |
| Baseline reproductible | restore/build OK, **233/233**, **0 vuln** (cf. §4) | ✅ |
| SDK piloté par `global.json` | `dotnet --version` = **8.0.417** | ✅ |
| Origine R-19 documentée | 7 occurrences de `HasDefaultValue(DateTime.UtcNow)` (6 configs), cf. [P2A-1B §5/§11](P2A-1B-report.md) | ✅ |

Aucun prérequis manquant → poursuite autorisée.

## 3. État Git initial

- Branche : `phase2a-stabilization` · `HEAD` : `3c7bea7 docs(P2A-1B): record green CI run 27343372219 (GO definitif)`
- `git status --short` : **vide** (arbre propre).
- `git diff` / `git diff --stat` : **vides**.

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur** (1 avert. préexistant `CS1998` [OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453), hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **233 ✅ / 0 ❌** (App **79** ; Domain **154**) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list … --no-build --no-connect` | **7 migrations** (`InitialCreate` → `RestoreSaleOrderSeparation`) |
| `dotnet ef migrations has-pending-model-changes … --no-build` | **true** (« Changes have been made to the model since the last migration ») — **bruit R-19 reproduit** |

> Note outillage : `dotnet-ef` est un **outil global** (`10.0.2`) lancé via
> `~/.dotnet/tools/dotnet-ef.exe`. Il pilote les opérations design-time mais utilise le package
> **`Microsoft.EntityFrameworkCore.Design` 8.0.27 du projet** : la migration et le snapshot générés sont
> au **format EF Core 8.0.27** (`ProductVersion = 8.0.27`), cohérents avec les 7 migrations existantes.

## 5. Analyse obligatoire (occurrences trouvées)

Recherche globale (`grep` sur `*.cs`) des motifs : `HasDefaultValue(DateTime.UtcNow)`,
`HasDefaultValue(DateTime.Now)`, `HasDefaultValueSql`, `ValueGeneratedOnAdd`, `CreatedAt`, `UpdatedAt`,
`CreatedDate`, `UpdatedDate`, `EntryDate`.

### 5.1 Cause racine — défauts `DateTime` figés au build (R-19)

**7 occurrences** de `HasDefaultValue(DateTime.UtcNow)` dans **6 configurations EF** :

| # | Fichier | Propriété | Comportement actuel | Problème | Décision |
|---|---|---|---|---|---|
| 1 | [`UserConfiguration.cs:44`](../../src/MMV.Infrastructure/Data/Configurations/UserConfiguration.cs) | `User.CreatedAt` | `ValueGeneratedOnAdd` + défaut = constante `DateTime.UtcNow` figée au build | Constante recalculée à chaque compilation ⇒ snapshot ≠ modèle ⇒ `has-pending` = true | **Retiré** (Option B) |
| 2 | [`CustomerConfiguration.cs:49`](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs) | `Customer.CreatedAt` | idem | idem | **Retiré** |
| 3 | [`CustomerConfiguration.cs:52`](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs) | `Customer.UpdatedAt` | idem | idem | **Retiré** |
| 4 | [`OrderConfiguration.cs:26`](../../src/MMV.Infrastructure/Data/Configurations/OrderConfiguration.cs) | `Order.OrderDate` | idem | idem | **Retiré** |
| 5 | [`SaleConfiguration.cs:26`](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs) | `Sale.SaleDate` | idem | idem | **Retiré** |
| 6 | [`PrescriptionConfiguration.cs:72`](../../src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs) | `Prescription.CreatedAt` | idem | idem | **Retiré** |
| 7 | [`StockMovementConfiguration.cs:31`](../../src/MMV.Infrastructure/Data/Configurations/StockMovementConfiguration.cs) | `StockMovement.CreatedAt` | idem | idem | **Retiré** |

**Preuve de la cause** : le snapshot figeait des constantes du type
`HasDefaultValue(new DateTime(2026, 2, 12, 22, 9, 2, 282, DateTimeKind.Utc).AddTicks(4997))`. Le modèle,
lui, ré-évalue `DateTime.UtcNow` à **chaque build** ⇒ divergence permanente détectée par
`has-pending-model-changes`. La dérive est **structurellement neutre** (valeur par défaut d'une colonne,
pas la structure tables/colonnes/types/index), conformément à la preuve d'équivalence P2A-1A
`Migrate_And_EnsureCreated_ProduceEquivalentSchema`.

### 5.2 Constat clé — chaque propriété est **déjà** horodatée par l'application

Les 7 propriétés portent **déjà** un initialiseur C# dans l'entité :

| Propriété | Initialiseur d'entité |
|---|---|
| `User.CreatedAt` | [`User.cs:53`](../../src/MMV.Domain/Entities/User.cs#L53) `= DateTime.UtcNow` |
| `Customer.CreatedAt` / `Customer.UpdatedAt` | [`Customer.cs:71,76`](../../src/MMV.Domain/Entities/Customer.cs#L71) `= DateTime.UtcNow` |
| `Order.OrderDate` | [`Order.cs:35`](../../src/MMV.Domain/Entities/Order.cs#L35) `= DateTime.UtcNow` |
| `Sale.SaleDate` | [`Sale.cs:34`](../../src/MMV.Domain/Entities/Sale.cs#L34) `= DateTime.UtcNow` |
| `Prescription.CreatedAt` | [`Prescription.cs:110`](../../src/MMV.Domain/Entities/Prescription.cs#L110) `= DateTime.UtcNow` |
| `StockMovement.CreatedAt` | [`StockMovement.cs:43`](../../src/MMV.Domain/Entities/StockMovement.cs#L43) `= DateTime.UtcNow` |

⇒ EF envoie **toujours** la valeur applicative (la propriété n'est jamais à la sentinelle CLR). Le défaut
SQL au niveau base est donc **redondant et jamais utilisé** par EF pour ces entités.

### 5.3 Autres motifs recensés — **hors périmètre, non touchés**

| Motif | Emplacement(s) | Pourquoi hors périmètre |
|---|---|---|
| `HasDefaultValue(true / 0 / 5 / 0m)` et `HasDefaultValue(<enum>)` | `UserConfiguration`, `ProductConfiguration`, `SaleConfiguration`, `OrderConfiguration`, `OrderItemConfiguration`, `SaleItemConfiguration` | **Constantes stables** : pas de dérive de build, non concernées par R-19 |
| `Notification.CreatedAt = DateTime.Now` | [`Notification.cs:48`](../../src/MMV.Domain/Entities/Notification.cs#L48) ; `NotificationConfiguration` **sans** `HasDefaultValue` | Aucun défaut SQL ⇒ **aucune dérive** ; colonne déjà `TEXT` simple dans le snapshot |
| `EntryDate` / `CreatedAt` dans `DbSeeder`, `DbInitializer`, tests | seeders/tests | Données de **runtime/seed**, pas une configuration EF ; hors périmètre |
| `HasDefaultValue(DateTime.Now)` / `HasDefaultValueSql` | *(aucune occurrence)* | — |

`ValueGeneratedOnAdd` résiduel dans le snapshot (clés `INTEGER`, `bool`/`int`/`decimal` à défaut stable)
provient des **autres** défauts stables et des clés auto-incrémentées : **non concerné** par R-19, inchangé.

## 6. Décision technique — Option retenue

### Comparaison

| | **Option A — Valeur gérée par la base** | **Option B — Valeur gérée par l'application** *(retenue)* |
|---|---|---|
| Mise en œuvre | `HasDefaultValueSql("CURRENT_TIMESTAMP")` | Retirer le défaut figé ; s'appuyer sur l'initialiseur d'entité (futur `IClock`) |
| `has-pending` | devient false (chaîne SQL stable) | devient false (colonne sans défaut, stable) |
| Dépendance provider | **Ajoute** du SQL spécifique SQLite | **Supprime** toute dépendance provider |
| Format de date | `CURRENT_TIMESTAMP` SQLite = `yyyy-MM-dd HH:mm:ss` (sans fraction, `Kind=Unspecified` à la relecture) ≠ format ISO d'EF ⇒ **changement de comportement subtil** | Aucun : l'app fournit déjà la valeur (ISO complet) |
| Coût migration | `AlterColumn` (reconstruction SQLite) | `AlterColumn` (reconstruction SQLite) — **identique** |
| Trajectoire SaaS/PostgreSQL | recouple au provider juste avant une éventuelle bascule | **aligné** sur la couche Application / `IClock` (roadmap Étapes 1 & 4) |
| Risque comportemental | non nul (défaut SQL réellement émis si la valeur n'est pas fournie) | **quasi nul** : la valeur applicative était déjà systématiquement envoyée |

### Décision : **Option B (valeur gérée par l'application)**

**Justification (option la plus sûre pour cette étape) :**
1. **Aucun changement de comportement observable** : les 7 propriétés sont **déjà** initialisées côté
   application (§5.2) ; le défaut SQL était redondant. Le retirer ne modifie pas ce qui est inséré.
2. **Suppression** d'une dépendance provider plutôt qu'**ajout** (Option A introduit du SQL SQLite et un
   format `TEXT` lossy `Kind=Unspecified`).
3. **Coût de migration identique** : les deux options génèrent un `AlterColumn` (reconstruction de table
   SQLite). Option A n'apporte donc aucun avantage de coût.
4. **Alignement** avec la trajectoire documentée : [migration-roadmap §Étape 1](../architecture/migration-roadmap.md)
   (« Corriger les valeurs par défaut figées (R-19) ») et §Étape 4 (« Horloge injectable `IClock` »).
   Conserver l'affectation côté application est la **couture** où un futur `IClock` se branchera.
5. **Minimal et portable** : élimine définitivement l'anti-pattern **et** le bruit de build.

## 6 bis. R2 — Bases historiques sans `__EFMigrationsHistory` contenant les anciens DEFAULT (R-19)

### Problème traité

La migration `FixDateTimeDefaultValues` corrige le **modèle courant** et les **bases gérées par
migrations**. Mais une **base SQLite historique** créée par `EnsureCreated` **avant** P2A-1R19, **sans**
`__EFMigrationsHistory`, porte encore **physiquement** les anciens DEFAULT `DateTime` figés sur les 7
colonnes. Or le portail de compatibilité [`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs)
compare la **structure** (tables/colonnes/types/nullabilité/PK/FK/index) mais **pas** les valeurs DEFAULT.

**Risque (séquence dangereuse, désormais corrigée)** : base jugée « compatible » → baseline de **toutes**
les migrations dans `__EFMigrationsHistory` → `FixDateTimeDefaultValues` marquée **appliquée** sans être
exécutée → **anciens DEFAULT physiques conservés** → `__EFMigrationsHistory` **ment** sur l'état réel
(« baseline mensonger »). Ce cas ne doit **jamais** être accepté silencieusement.

### Stratégie retenue — **Option A (réparation contrôlée)**, justifiée

| Option | Décision |
|---|---|
| **A — Baseline partiel puis exécution réelle de `FixDateTimeDefaultValues`** | **RETENUE** |
| B — Refuser l'adoption et reporter au support | Rejetée (voir ci-dessous) |

**Justification (option la plus sûre) :**
1. La migration `FixDateTimeDefaultValues` est **prouvée non destructive** (P2A-1R19 §8/§9.4 :
   reconstruction SQLite préservant toutes les lignes). L'exécuter sur une base historique est donc **sûr**.
2. Une **sauvegarde** est **toujours** créée avant toute mutation (cycle de vie P2A-1A).
3. Une **vérification physique post-adoption** garantit l'absence de baseline mensonger : si un ancien
   DEFAULT subsiste, l'adoption **échoue explicitement** (base + sauvegarde conservées) — **pas
   d'acceptation silencieuse**.
4. Option A **répare automatiquement et sans perte** la base existante de l'utilisateur ; Option B
   **rejetterait toute** base `EnsureCreated` antérieure (mauvaise expérience) alors qu'une réparation
   sûre existe, en contradiction avec la philosophie « adoption progressive sûre » de P2A-1A/1B.

### Mise en œuvre

1. **Vérificateur physique dédié** [`SqliteDateTimeDefaultVerifier`](../../src/MMV.Infrastructure/Data/SqliteDateTimeDefaultVerifier.cs)
   (lecture seule) : interroge `PRAGMA table_info(...)` et renvoie, parmi les 7 colonnes corrigées,
   celles dont le `dflt_value` est **non nul** (héritage R-19). Liste vide ⇒ base saine.
2. **Adoption durcie** dans [`SqliteDatabaseManager.AdoptHistoricalDatabase`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) :
   - portail de compatibilité structurel (inchangé) ;
   - **si** des DEFAULT hérités sont détectés ⇒ **baseline limité** aux migrations **antérieures** à
     `FixDateTimeDefaultValues`, puis `Migrate()` **exécute réellement** `FixDateTimeDefaultValues`
     (suppression des DEFAULT physiques) ;
   - **sinon** (base `EnsureCreated` actuelle, aucun DEFAULT) ⇒ baseline de **toutes** les migrations
     (comportement P2A-1A inchangé) ;
   - **vérification physique post-adoption** : si un DEFAULT hérité subsiste ⇒ `DatabaseMigrationException`
     (échec explicite, base + sauvegarde conservées, journal motivé) ;
   - **journal** : `ADOPT: legacy DateTime DEFAULT detected … FixDateTimeDefaultValues will be executed (R-19 repair)`.
3. **Reprise de l'ancien `mmv-optic.db`** : la même réparation s'applique, car
   [`LegacyDatabaseRecoveryService`](../../src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs)
   **délègue** l'adoption à `SqliteDatabaseManager.PrepareDatabase` (couvert par le test §9, scénario 6).

### Résultat garanti après adoption d'une base historique

- `__EFMigrationsHistory` **cohérent** avec le schéma physique (toutes les migrations appliquées) ;
- **aucune** migration en attente ; `has-pending-model-changes` du modèle inchangé (false) ;
- les 7 colonnes **ne portent plus** d'ancien DEFAULT `DateTime` figé ;
- **données conservées** (reconstruction non destructive) ;
- sinon : **échec explicite**, jamais d'acceptation silencieuse.

## 7. Fichiers modifiés

**Modifiés — configurations EF (6) :** retrait du `HasDefaultValue(DateTime.UtcNow)` (remplacé par un
commentaire d'intention « horodatage géré par l'application, futur IClock — R-19 / P2A-1R19 »).
- [`UserConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/UserConfiguration.cs) — `CreatedAt`
- [`CustomerConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs) — `CreatedAt`, `UpdatedAt`
- [`OrderConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/OrderConfiguration.cs) — `OrderDate`
- [`SaleConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs) — `SaleDate`
- [`PrescriptionConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs) — `CreatedAt`
- [`StockMovementConfiguration.cs`](../../src/MMV.Infrastructure/Data/Configurations/StockMovementConfiguration.cs) — `CreatedAt`

**Modifié — snapshot (1) :** [`OpticDbContextModelSnapshot.cs`](../../src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs)
— les 7 colonnes passent de `ValueGeneratedOnAdd().HasColumnType("TEXT").HasDefaultValue(<constante>)`
à `HasColumnType("TEXT")` simple (régénéré par `dotnet ef migrations add`).

**Ajoutés — migration (2) :**
- [`20260611114307_FixDateTimeDefaultValues.cs`](../../src/MMV.Infrastructure/Migrations/20260611114307_FixDateTimeDefaultValues.cs)
- [`20260611114307_FixDateTimeDefaultValues.Designer.cs`](../../src/MMV.Infrastructure/Migrations/20260611114307_FixDateTimeDefaultValues.Designer.cs)

**Modifié — cycle de vie (R2, 1) :** [`SqliteDatabaseManager.cs`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs)
— `AdoptHistoricalDatabase` durci : détection des DEFAULT hérités, baseline partiel + exécution réelle de
`FixDateTimeDefaultValues`, vérification physique post-adoption (cf. §6 bis).

**Ajouté — composant R2 (1) :** [`SqliteDateTimeDefaultVerifier.cs`](../../src/MMV.Infrastructure/Data/SqliteDateTimeDefaultVerifier.cs)
— contrôle physique (`PRAGMA table_info`) des anciens DEFAULT `DateTime` sur les 7 colonnes (lecture seule).

**Ajoutés — tests (2) :**
- [`DateTimeDefaultValuesMigrationTests.cs`](../../tests/MMV.Domain.Tests/Data/DateTimeDefaultValuesMigrationTests.cs) (4, R19)
- [`SqliteHistoricalDateTimeDefaultsTests.cs`](../../tests/MMV.Domain.Tests/Data/SqliteHistoricalDateTimeDefaultsTests.cs) (6, R2)

**Ajouté — rapport (1) :** `docs/implementation/P2A-1R19-report.md` (ce document).

**Note ADR :** [`adr-sqlite-lifecycle.md`](../architecture/adr-sqlite-lifecycle.md) complété d'une note
§5 ter (vérification des DEFAULT hérités avant baseline).

**Aucune entité métier, aucun ViewModel, aucun `DbInitializer`/`DbSeeder`, aucune valeur réglementaire,
aucune des 7 migrations existantes** modifiés. La logique d'adoption R2 **ne change pas** le comportement
pour une base `EnsureCreated` actuelle (baseline de toutes les migrations, inchangé).

## 8. Migration créée

**Créée : `20260611114307_FixDateTimeDefaultValues`** (8ᵉ migration). Elle ne contient **que** des
`AlterColumn` techniques :

- **`Up()`** : 7 `AlterColumn<DateTime>` (Users.CreatedAt, StockMovements.CreatedAt, Sales.SaleDate,
  Prescriptions.CreatedAt, Orders.OrderDate, Customers.UpdatedAt, Customers.CreatedAt) qui **retirent
  l'ancien défaut** (`oldDefaultValue`) ; la colonne reste `TEXT NOT NULL`, **sans nouveau défaut**.
- **`Down()`** : restaure les anciens défauts (migration **réversible**).

**Non destructive** : aucun `DropTable`, `DropColumn`, `DeleteData`, `UpdateData` ni transformation de
données. Sous SQLite, `AlterColumn` déclenche une **reconstruction de table** (création + copie
`INSERT … SELECT` + bascule) qui **préserve toutes les lignes** (prouvé par le test §9.4). Aucune donnée
métier transformée ; seules les **valeurs par défaut techniques** des colonnes concernées changent.

## 9. Tests ajoutés (10, fichiers temporaires isolés)

Chaque test utilise un fichier SQLite temporaire (`Path.GetTempPath()` + GUID, `Pooling=False`),
nettoyé en `Dispose`.

**`DateTimeDefaultValuesMigrationTests` (4, R19)** :

| # | Test | Preuve |
|---|---|---|
| 9.1 | `Model_HasNoPendingModelChanges_AfterFixMigration` | `context.Database.HasPendingModelChanges()` = **false** (équivalent programmatique du CLI) |
| 9.2 | `Migrate_FreshDatabase_AppliesFixMigration_NoPending_NoDemoData` | **base vierge créée via migrations** : `FixDateTimeDefaultValues` appliquée, aucune migration en attente, **aucune donnée de démonstration** (0 user/customer/product) |
| 9.3 | `MigratedDatabase_AffectedDateColumns_AcceptInsertion_AndPersistApplicationTimestamps` | les **7 colonnes de date** acceptent une insertion (User, Customer×2, Sale, Order, Prescription, StockMovement) et **conservent la valeur applicative** (≈ maintenant), sans défaut SQL |
| 9.4 | `FixMigration_IsNonDestructive_PreservesExistingRows` | données créées **avant** le correctif (via `IMigrator.Migrate(MigrationBeforeFix)`) **conservées** après application (reconstruction SQLite) ; horodatage existant préservé |

**`SqliteHistoricalDateTimeDefaultsTests` (6, R2)** — bases historiques sans `__EFMigrationsHistory` :

| # | Test | Scénario obligatoire (consigne §5) |
|---|---|---|
| 9.5 | `Verifier_FreshMigratedDatabase_ReportsNoLegacyDefaults` | **1** — base vierge créée par migrations après R19 : aucun DEFAULT hérité |
| 9.6 | `Verifier_CurrentEnsureCreatedDatabase_ReportsNoLegacyDefaults` | **3** — base `EnsureCreated` actuelle : aucun DEFAULT hérité |
| 9.7 | `Verifier_PreR19HistoricalDatabase_DetectsLegacyDefaultsOnAllSevenColumns` | **4** (prémisse) — base pré-R19 : les **7** DEFAULT hérités détectés |
| 9.8 | `Adopt_PreR19HistoricalWithLegacyDefaults_RepairsByExecutingFixMigration` | **4 + 5** — sauvegarde créée ; **pas de baseline mensonger** (`FixDateTimeDefaultValues` exécutée, non baselinée) ; `__EFMigrationsHistory` cohérent ; **anciens DEFAULT absents** ; données conservées ; journal motivé |
| 9.9 | `Adopt_CurrentEnsureCreatedHistorical_NoLegacyDefaults_BaselinesAllMigrations` | **2 + 3** — base saine : baseline de toutes les migrations (comportement P2A-1A inchangé), données conservées |
| 9.10 | `Recover_LegacyMmvOpticDbWithLegacyDefaults_RepairsCurrentDatabase` | **6** — reprise de l'ancien `mmv-optic.db` pré-R19 : copie + **réparation** au chemin courant ; ancien fichier conservé intact |

Non-régression des 233 tests préexistants (dont la suite `SqliteDatabaseManagerTests` d'adoption P2A-1A) : §11.

## 10. Commandes exécutées (principales)

```powershell
# Baseline + analyse
git status --short ; git branch --show-current ; git log -5 --oneline ; git diff --stat ; git diff
dotnet --version ; dotnet restore MMV.sln ; dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list --project src/MMV.Infrastructure --no-build --no-connect
& ~/.dotnet/tools/dotnet-ef.exe migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build  # => true

# Implémentation (R19)
#  - retrait des 7 HasDefaultValue(DateTime.UtcNow) dans 6 configurations
dotnet build src/MMV.Infrastructure/MMV.Infrastructure.csproj -c Debug
& ~/.dotnet/tools/dotnet-ef.exe migrations add FixDateTimeDefaultValues `
    --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build
#  - ajout du fichier de tests R19 (bases temporaires)

# Implémentation (R2)
#  - ajout de SqliteDateTimeDefaultVerifier (contrôle physique PRAGMA table_info)
#  - durcissement de SqliteDatabaseManager.AdoptHistoricalDatabase (baseline partiel + exécution réelle + post-check)
#  - ajout du fichier de tests R2 (bases historiques temporaires)
#  - note ADR §5 ter

# Contrôles finaux
dotnet build MMV.sln --no-restore -c Debug
dotnet test  MMV.sln --no-build  -c Debug
dotnet list  MMV.sln package --vulnerable --include-transitive
& ~/.dotnet/tools/dotnet-ef.exe migrations list … ; … has-pending-model-changes …  # => false
git status --short ; git diff --stat
```

## 11. Résultat de `has-pending-model-changes`

| Moment | Résultat |
|---|---|
| **Avant** (baseline) | `has-pending-model-changes` = **true** — « Changes have been made to the model since the last migration » |
| **Après** (correctif + migration) | `has-pending-model-changes` = **false** — « **No changes have been made to the model since the last migration** » (exit 0) |

**Objectif strict atteint** : `dotnet ef migrations has-pending-model-changes = false`.

### Contrôles finaux

| Contrôle | Résultat |
|---|---|
| `dotnet restore` | **Succès** |
| `dotnet build -c Debug` | **Succès — 0 erreur, 0 avertissement introduit** (`CS1998` préexistant inchangé, hors périmètre) |
| `dotnet test -c Debug` | **243 ✅ / 0 ❌** — App **79/79** ; Domain **164/164** (154 + **4** R19 + **6** R2) |
| Non-régression des 233 | ✅ (79 App + 154 Domain préservés, dont adoption P2A-1A) |
| `dotnet list … --vulnerable` | **0 package vulnérable** (5 projets) |
| `dotnet ef migrations list` | **8 migrations** (7 + `FixDateTimeDefaultValues`) |
| `dotnet ef migrations has-pending-model-changes` | **false** ✅ |

## 12. Risques résiduels (documentés, hors périmètre)

1. **Reconstruction de table SQLite à l'application de la migration** — `AlterColumn` reconstruit 6 tables
   (Users, StockMovements, Sales, Prescriptions, Orders, Customers) en copiant les données. **Non
   destructive** (testé §9.4) ; sur un poste desktop le volume est faible. Le cycle de vie P2A-1A
   **sauvegarde** toujours la base avant migration ⇒ reprise possible.
2. **Bases historiques `EnsureCreated` antérieures avec anciens DEFAULT** — **traité (R2, §6 bis)** :
   le portail `SqliteSchemaVerifier` ne compare pas les valeurs DEFAULT, mais
   [`SqliteDateTimeDefaultVerifier`](../../src/MMV.Infrastructure/Data/SqliteDateTimeDefaultVerifier.cs)
   détecte les DEFAULT hérités et l'adoption **exécute réellement** `FixDateTimeDefaultValues` (baseline
   partiel) au lieu de la baseliner à tort ; une vérification physique post-adoption interdit toute
   acceptation silencieuse. Risque résiduel : un schéma divergent **non couvert** par le portail
   structurel resterait du ressort de P2A-1B (inchangé).
3. **Horodatage applicatif sans `IClock`** — la valeur reste fixée par l'initialiseur `= DateTime.UtcNow`.
   L'introduction d'une **horloge injectable** est explicitement prévue à l'**Étape 4** (hors périmètre).
   Le présent correctif établit la couture (valeur côté application) sans l'implémenter.
4. **`Notification.CreatedAt = DateTime.Now`** (heure locale, non UTC) — **inchangé** (aucune dérive
   `has-pending`, pas de défaut SQL). Harmonisation UTC = sujet futur, hors périmètre R-19.
5. **`CS1998`** préexistant ([OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453))
   — inchangé, hors périmètre.

Aucun de ces risques ne touche au métier, au réglementaire, ni aux interdictions de la phase.

## 13. État Git final

Étape finale **`ALLOW_COMMIT=true`, `ALLOW_PUSH=true`** (correction documentaire ADR + commit + push) ⇒
le changeset R19 + R2 est **commité** puis **poussé** (sans force-push).

- Commit : **`f93379b`** — `fix(P2A-1R19): remove unstable DateTime defaults and repair historical baselines`
  (+ trailer `Co-Authored-By`). Sur `3c7bea7`.
- Push : `git push origin phase2a-stabilization` (sans force) → `3c7bea7..f93379b`.
- `git status --short` après commit : **vide** (arbre propre).
- `git diff --check` : **0 anomalie** d'espaces (seuls des avis LF→CRLF, normaux sous Windows).

Contenu commité (`git show --stat`), **15 fichiers, +2420 / −43** :

```
M  docs/architecture/adr-sqlite-lifecycle.md
A  docs/implementation/P2A-1R19-report.md
M  src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs
M  src/MMV.Infrastructure/Data/Configurations/OrderConfiguration.cs
M  src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs
M  src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs
M  src/MMV.Infrastructure/Data/Configurations/StockMovementConfiguration.cs
M  src/MMV.Infrastructure/Data/Configurations/UserConfiguration.cs
M  src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs
A  src/MMV.Infrastructure/Data/SqliteDateTimeDefaultVerifier.cs
A  src/MMV.Infrastructure/Migrations/20260611114307_FixDateTimeDefaultValues.cs
A  src/MMV.Infrastructure/Migrations/20260611114307_FixDateTimeDefaultValues.Designer.cs
M  src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs
A  tests/MMV.Domain.Tests/Data/DateTimeDefaultValuesMigrationTests.cs
A  tests/MMV.Domain.Tests/Data/SqliteHistoricalDateTimeDefaultsTests.cs
```

**9 fichiers modifiés** (6 configs + `SqliteDatabaseManager.cs` + snapshot + ADR) et **6 fichiers ajoutés**
(2 migration + 1 composant R2 + 2 tests + ce rapport). **Aucun** `bin/`/`obj/`, `*.db`, `*.zip`, `*.trx`,
temporaire, secret ou donnée utilisateur. **Aucune entité métier, aucun ViewModel** modifié. Le bruit
`has-pending-model-changes` est levé et les bases historiques pré-R19 sont **réparées** à l'adoption.

## 14. Validation CI distante (run réel)

Push sur `phase2a-stabilization` ⇒ workflow `CI` déclenché sur l'événement `push`. Détails consignés
depuis l'API GitHub Actions :

| Élément | Valeur réelle |
|---|---|
| Run | **#27346008843** (run number 7) |
| Lien | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27346008843 |
| Commit testé | **`f93379b`** (`head_sha = f93379bd63979f0a2b2c7a8ad08dbfe7357ff2f4`) |
| Workflow / job | `CI` / `Restore / Build / Test / Scan` |
| Runner / durée | `windows-latest` (GitHub-hosted) — ≈ 2 min 44 s (12:14:52 → 12:17:36 UTC) |
| SDK utilisé | **8.0.417** (step « Setup .NET (SDK verrouillé par global.json) » → success) |
| Restore | ✅ **success** |
| Build | ✅ **success** |
| Test | ✅ **success** (suite **243** ; le step échoue si un test échoue) |
| `has-pending-model-changes` | ✅ **false** (confirmé localement, exit 0 ; le modèle == dernier snapshot) |
| Audit NuGet (JSON + sévérité) | ✅ **success** (0 High/Critical, scan concluant) |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps (`Set up job`, `Checkout`, `Setup .NET`, `Diagnostic SDK`, `Restore`, `Build`, `Test`,
`Audit des packages vulnérables`, `Complete job`) sont en conclusion `success`.

## 15. Verdict — GO / NO-GO

### **P2A-1R19 = GO DÉFINITIF** (CI distante verte, run #27346008843, commit `f93379b`)

| Critère d'acceptation (consigne §11) | État |
|---|---|
| Build vert | ✅ 0 erreur, 0 avertissement introduit |
| Tests verts (> 237) | ✅ **243/243** (237 + **6** R2) |
| Aucune vulnérabilité High/Critical | ✅ 0 (5 projets) |
| `has-pending-model-changes = false` | ✅ « No changes have been made to the model… » |
| Base vierge créée via migrations | ✅ testé (§9.2, §9.5) |
| Colonnes de date acceptent l'insertion | ✅ testé, 7 colonnes (§9.3) |
| Migration non destructive | ✅ `AlterColumn` seuls + reconstruction préservant les lignes (§8, §9.4) |
| **Base historique pré-R19 (DEFAULT hérités) non acceptée silencieusement** | ✅ réparée (baseline partiel + exécution réelle) ou échec explicite (§6 bis, §9.8) |
| **`__EFMigrationsHistory` cohérent avec le schéma physique** | ✅ vérification physique post-adoption (§6 bis, §9.8) |
| **Reprise `mmv-optic.db` pré-R19 réparée** | ✅ testé (§9.10) |
| Aucune donnée de démonstration introduite | ✅ testé (§9.2) |
| Aucune modification métier hors périmètre | ✅ entités, ViewModels, `DbInitializer`/`DbSeeder`, réglementaire intacts |
| Aucune nouvelle fonctionnalité | ✅ correctif technique uniquement |
| **CI distante verte** | ✅ run **#27346008843** = success (commit `f93379b`) |
| Aucune phase P2A-1C+ commencée | ✅ |
| Rapport complet | ✅ (ce document, §6 bis pour R2) |

La gate « VALIDATION DISTANTE REQUISE » est **levée** : le pipeline distant est **vert** sur le commit testé.

## 16. Prochaine étape candidate (NON exécutée)

`P2A-1C` — « Transactions, idempotence et erreurs de persistance » (frontière transactionnelle d'écriture,
protection double-soumission, erreurs utilisateur maîtrisées). **Non commencée.** Ne pas démarrer sans
verdict `P2A-1R19` confirmé par revue humaine, puis `TARGET_PHASE_ID = P2A-1C` explicite.

> **Arrêt obligatoire.** Fin de `P2A-1R19` — commit `f93379b` poussé, **CI distante verte (#27346008843)**,
> verdict **GO définitif**. Aucune amorce de `P2A-1C`.
