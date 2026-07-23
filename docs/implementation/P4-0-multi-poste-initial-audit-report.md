# Rapport P4-0 — Audit initial multi-poste et cadrage de P4

> **MODE initial** : `CREATE_P4_BRANCH_AND_AUDIT_MULTI_POSTE_NO_IMPLEMENTATION_NO_COMMIT`
> **MODE de clôture** : `FINALIZE_P4_0_ENABLE_CI_COMMIT_PUSH_AND_CLOSE` (cf. §36)
> **Aucune** modification de code, de test, de migration, de snapshot EF, d'UI ou de package.
> Seule modification de workflow autorisée à la clôture : l'ajout du motif `p4*` au filtre `push` de
> `.github/workflows/ci.yml`, afin que la branche P4 soit réellement couverte par la CI (§36).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Date | 2026-07-23 |
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche source | `main` |
| Branche créée | `p4-multi-poste` |
| SHA de départ | `3ed883634dae83399d3e93dbc1dc65ffaabdf243` |
| Commit P3 fusionné (attendu ancêtre) | `89ccc9180e43052559c8699931c67a0a20020c4c` |
| CI `main` de référence | run `29968668277` |
| Tests attendus | 1499 (Domain 649 · Application 611 · App 239) |
| Phase | P4-0 — Audit initial multi-poste et roadmap |

Périmètre : audit **lecture seule** + création de branche + rédaction de deux documents + (à la clôture) activation
minimale de la CI sur `p4*`.
Interdictions : aucun code/test/migration/snapshot/UI/package/provider modifié ; aucun choix définitif de provider ;
aucune base serveur ; aucun outil de migration ; aucune autre modification du workflow CI.

---

## 2. Décision produit (rappel de cadrage)

P4 est officiellement consacré à la transformation de MMV en application **multi-poste** sur **base centrale
client/serveur**, en préparation de la **V1 multi-poste**. Décisions déjà acquises (source :
[ADR-PROD-DB-001](../architecture/adr-prod-db-001-multi-poste-database-strategy.md), **cadrage P3-0B**) :

- plusieurs postes d'un même magasin partagent **une** base centrale ;
- multi-poste **≠** multi-tenant (un seul magasin, une seule base) ;
- **SQLite local reste autorisé** pour dev / test / démo / mono-poste ;
- **SQLite sur dossier réseau : non supporté** (verrouillage de fichiers non fiable → corruption) ;
- **PostgreSQL = candidat principal** et **SQL Server Express = candidat secondaire**, **issus de l'ADR P3-0B** ;
- le **choix final reste conditionné à un spike réel** ; **aucun provider serveur choisi ni implémenté**.

Statut du choix de provider à l'issue de P4-0 :

- **aucun gagnant** n'est désigné ;
- **aucune notation**, aucun score, aucun classement n'est attribué ;
- **aucune conclusion fondée sur la mémoire** n'est retenue ;
- **licences, limites produit et coûts sont à vérifier dans les sources officielles pendant P4-1** — ils ne sont
  ni cités ni supposés ici.

---

## 3. Clôture P3 (vérifiée sur le dépôt)

Commandes exécutées et résultats :

```
git rev-parse origin/main                 → 3ed883634dae83399d3e93dbc1dc65ffaabdf243   ✅
git rev-parse origin/p3-business-rules    → 89ccc9180e43052559c8699931c67a0a20020c4c   ✅
git merge-base --is-ancestor 89ccc91… origin/main   → exit 0 (ancêtre confirmé)          ✅
git status --short                        → seuls design-handoff/ design/ docs/ui/ (untracked)  ✅
git diff --check                          → propre                                        ✅
```

`89ccc91` (P3 fusionné) est **ancêtre** de `origin/main` `3ed8836` (docs-only closeout). Aucun fichier suivi
modifié. Les seuls dossiers non suivis autorisés sont présents et hors périmètre.

---

## 4. Création de branche

| Étape | Commande | Résultat |
|---|---|---|
| Absence branche locale | `git show-ref --verify --quiet refs/heads/p4-multi-poste` | exit **1** (absente) ✅ |
| Absence branche distante | `git ls-remote --exit-code --heads origin p4-multi-poste` | exit **2** (aucune réf) ✅ |
| Création | `git switch --create p4-multi-poste origin/main` | HEAD = `3ed8836` ✅ |
| Push | `git push --set-upstream origin p4-multi-poste` | `* [new branch]` (aucun force) ✅ |
| Vérification distante | `git rev-parse origin/p4-multi-poste` | `3ed8836` ✅ |
| Divergence | `git rev-list --left-right --count origin/main...origin/p4-multi-poste` | **`0 0`** ✅ |

Aucune branche préexistante n'a été écrasée, réinitialisée ou forcée.
**`P4-BRANCH-CREATION = GO`**.

---

## 5. CI de départ

### 5.1 CI `main` (baseline distante autoritaire) — run `29968668277`

```
gh run view 29968668277 --json …
headSha    = 3ed883634dae83399d3e93dbc1dc65ffaabdf243   ✅
headBranch = main    event = push    status = completed    conclusion = success   ✅
Jobs (Restore/Build/Test/Scan) : tous verts →
  Restore ✅ · Build ✅ · Test ✅ · Audit vulnérabilités ✅ · Check EF pending model changes ✅
```

### 5.2 CI de la branche `p4-multi-poste` — **Cas B (aucun workflow déclenché)**

`gh run list --branch p4-multi-poste` → `[]`.

**Raison réelle** (lecture de [.github/workflows/ci.yml](../../.github/workflows/ci.yml#L6-L15)) :

```yaml
on:
  push:
    branches: [ main, 'phase*', 'p2*', 'p3*' ]   # ← aucun motif 'p4*'
  pull_request:
    branches: [ main ]
  workflow_dispatch:
```

Le filtre `push` **ne contient pas** `p4*` : un push sur `p4-multi-poste` **ne déclenche pas** le workflow.
`pull_request` ne se déclenche que pour une PR ciblant `main` (aucune ouverte) ; `workflow_dispatch` est manuel.
Aucun run inventé. **Au moment de la création de branche, la CI de `main` sur le même SHA était la baseline distante
autoritaire.** Ce constat est **corrigé à la clôture de P4-0** par l'ajout minimal du motif `p4*` (cf. **§36**) :
sans lui, aucun commit de la phase P4 ne serait vérifié à distance.

---

## 6. Baseline locale P4 (branche `p4-multi-poste`, commit `3ed8836`)

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | up-to-date ✅ |
| Build | `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning · 0 Error** ✅ |
| Tests Domain | `dotnet test tests/MMV.Domain.Tests …` | **649** passed, 0 failed, 0 skipped ✅ |
| Tests Application | `dotnet test tests/MMV.Application.Tests …` | **611** passed, 0 failed, 0 skipped ✅ |
| Tests App | `dotnet test tests/MMV.App.Tests …` | **239** passed, 0 failed, 0 skipped ✅ |
| Tests solution | `dotnet test MMV.sln --no-build -c Debug` | **1499** total, 0 failed, 0 skipped ✅ |
| Vulnérabilités | `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune** ✅ |
| Modèle EF | `dotnet ef migrations has-pending-model-changes …` | *No changes… since last migration* (exit 0) ✅ |
| Migrations | `dotnet ef migrations list …` | **14** migrations (cf. §12) |
| Frontière Application | `dotnet list src/MMV.Application… reference / package` | ref = `MMV.Domain` seul ; package = `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` ✅ |
| Diff | `git diff --check` | propre ✅ |

> **Note sur `migrations list`** : les deux dernières migrations apparaissent `(Pending)`. Cela signifie
> « non appliquées à la base de conception locale du design-time factory » (base absente/ancienne), et **non**
> « changement de modèle non matérialisé » — ce dernier contrôle (`has-pending-model-changes`) est **vert (exit 0)**.
> Le modèle compilé est aligné sur la dernière migration.

**`P4-0 AUDIT = GO LOCAL`** (baseline reproduite à l'identique).

---

## 7. Projets et dépendances

Solution : 7 projets (`dotnet sln MMV.sln list`) → `MMV.Domain`, `MMV.Application`, `MMV.Infrastructure`,
`MMV.App`, `MMV.Domain.Tests`, `MMV.Application.Tests`, `MMV.App.Tests`.

| Projet | Dépendances provider | Dépendances SQLite | Provider-neutre ? | Impact P4 |
|---|---|---|---|---|
| **MMV.Domain** | — | — | **Oui** (FluentValidation 11.9.0 seul) | À conserver strictement neutre |
| **MMV.Application** | — | — | **Oui** (`Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` + ref `MMV.Domain`) | À conserver strictement neutre |
| **MMV.Infrastructure** | `Microsoft.EntityFrameworkCore.Design 8.0.27` | `Microsoft.EntityFrameworkCore.Sqlite 8.0.27`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.0` | **Non** | **Site principal de P4** : provider, migrations, mapper d'erreurs, primitives, backup |
| **MMV.App** | `Microsoft.EntityFrameworkCore.Design 8.0.27` (design-time) | Provider **transitif** via Infrastructure ; `UseSqlite` au composition root | Partiel | Sélection provider par configuration ; état de connexion (à décider) |
| **MMV.Domain.Tests** | — | `Microsoft.EntityFrameworkCore.Sqlite 8.0.27` | Non (test) | Tests d'intégration SQLite fichier/`:memory:` |
| **MMV.Application.Tests** | — | `Microsoft.EntityFrameworkCore.Sqlite 8.0.27` | Non (test) | Tests d'intégration SQLite fichier |

Autres packages Infrastructure : `BCrypt.Net-Next 4.0.3` (hachage mot de passe, neutre),
`Microsoft.Extensions.Configuration.Abstractions 8.0.0`, `System.Text.Json 8.0.6`.

**Constats clés :**
- **Aucun** package PostgreSQL (`Npgsql`) ni SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`) dans `src`/`tests`
  (grep : 0 occurrence).
- **Aucun** package de résilience/retry (`Polly`, execution strategy custom) — 0 occurrence.
- Domain et Application sont **déjà provider-neutres au niveau de leurs dépendances et de leur code** (aucune
  référence EF/provider, aucun SQL, aucune chaîne de connexion) — la frontière hexagonale attendue par P4 est
  respectée en amont. Cette neutralité porte sur les **dépendances**, pas sur le comportement runtime des
  garanties, qui reste à prouver côté Infrastructure (§15).

---

## 8. Composition SQLite (câblage actuel)

Inventaire `src` : **37 occurrences** d'API SQLite réparties sur **12 fichiers**
(`UseSqlite`/`SqliteConnection`/`SqliteException`/`SqliteParameter`/`Microsoft.Data.Sqlite`/`SqliteErrorCode`…).

| Fichier | Ligne(s) | Usage SQLite | Runtime/Test | Portabilité | Action P4 probable |
|---|---|---|---|---|---|
| [App.axaml.cs](../../src/MMV.App/App.axaml.cs#L121-L122) | 118-122, 203-204 | `UseSqlite(...)` (composition root **réel**) + résolution chemin + factory de recovery | Runtime | Spécifique | Sélection provider par environnement/configuration |
| [DependencyInjection.cs](../../src/MMV.Infrastructure/DependencyInjection.cs#L37) | 33-38 | `AddDbContext + UseSqlite` (**inerte** — non appelée par le runtime) | Runtime (mort) | Spécifique | Réutiliser comme point d'extension multi-provider |
| [OpticDbContext.cs](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L140) | 133-141 | `OnConfiguring → UseSqlite` (fallback design-time) | Runtime/design | Spécifique | Rendre provider-neutre (config injectée) |
| [OpticDbContextFactory.cs](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L18) | 18 | `UseSqlite` (design-time `dotnet ef`) | Design-time | Spécifique | Factory paramétrable par provider |
| [PersistenceErrorMapper.cs](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs) | 1, 45, 65-95 | `SqliteException` + `SqliteErrorCode`/`SqliteExtendedErrorCode` | Runtime | **Spécifique (bloquant)** | Classification provider-spécifique (cf. §16) |
| [NotificationRepository.cs](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L115-L133) | 1, 115-133 | `SqliteParameter` + `INSERT … ON CONFLICT DO NOTHING` | Runtime | **Spécifique (bloquant)** | Upsert anti-doublon provider (cf. §15/§16) |
| [SqliteDatabaseManager.cs](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) | tout le fichier | Cycle de vie base (backup fichier, `Migrate`, adoption, PRAGMA) | Runtime | Spécifique | Réécriture provider serveur (cf. §9/§12/§13) |
| [SqliteSchemaVerifier.cs](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs) | PRAGMA | Vérification physique schéma | Runtime | Spécifique | Équivalent `information_schema`/catalog serveur |
| [SqliteDateTimeDefaultVerifier.cs](../../src/MMV.Infrastructure/Data/SqliteDateTimeDefaultVerifier.cs) | PRAGMA | Contrôle des DEFAULT hérités | Runtime | Spécifique | Sans objet côté serveur (ou équivalent) |
| [SqliteHistoricalDatabaseDiagnostic.cs](../../src/MMV.Infrastructure/Data/SqliteHistoricalDatabaseDiagnostic.cs) | 1, 149-385 | Diagnostic fichier historique | Runtime | Spécifique | Sans objet côté serveur |
| [SqliteDatabasePathResolver.cs](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs) | tout | Résolution chemin fichier `Data Source=` | Runtime | Spécifique | Sans objet côté serveur (chaîne serveur) |
| [LegacyDatabaseRecoveryService.cs](../../src/MMV.Infrastructure/Data/LegacyDatabaseRecoveryService.cs) | 1, 86-186 | `SqliteConnection.ClearAllPools`, reprise `mmv-optic.db` | Runtime | Spécifique | Sans objet côté serveur |

**Sélection du provider aujourd'hui dispersée sur 4 sites `UseSqlite`** (App composition root, DI inerte,
`OnConfiguring`, `OpticDbContextFactory`) + la lambda de recovery. P4 devra centraliser la sélection provider.

---

## 9. Cycle de vie de la base locale

Séquence réelle au démarrage ([App.axaml.cs:112-244](../../src/MMV.App/App.axaml.cs#L112-L244)) :

1. `SqliteDatabasePathResolver.ResolveDatabasePath()` — choisit le fichier :
   `MMV_DATABASE_PATH` → config `OpticDatabase` (`Data Source=`) → défaut `%LOCALAPPDATA%\ManageMyVision\mmv.db`.
2. `AddDbContext<OpticDbContext>(UseSqlite(...))`.
3. `LegacyDatabaseRecoveryService.Recover(...)` — reprise contrôlée de l'ancien `mmv-optic.db` (jamais destructive).
4. `SqliteDatabaseManager.PrepareDatabase(dbContext)` :
   - `Backup(databasePath)` = **copie de fichier** (+ sidecars `-wal`/`-shm`) ;
   - `DetectState` via probing table `Users` + `__EFMigrationsHistory` ;
   - **fresh** → `context.Database.Migrate()` ; **pending** → `Migrate()` ; **historique** (`EnsureCreated`, sans
     historique EF) → **adoption/baselining** par vérification **physique PRAGMA** de chaque migration
     (`table_info`, `index_list`, `foreign_key_list`) puis écriture ciblée de `__EFMigrationsHistory` ;
   - `VerifyAfterPreparation` (PRAGMA).
5. `DatabaseSeeder.Seed(dbContext, seedOptions)` — gouverné par l'environnement (cf. §19).

Réponses aux questions du brief §13 :

| # | Question | Réponse (dépôt réel) |
|---|---|---|
| 1 | Qui choisit le fichier de base ? | `SqliteDatabasePathResolver` (env → config → défaut LOCALAPPDATA) |
| 2 | Où la chaîne de connexion est-elle construite ? | `SqliteDatabasePathResolver.GetConnectionString` (`Data Source=…`) |
| 3 | Qui appelle `Migrate()` ? | `SqliteDatabaseManager` (`ApplyFreshInstall`/`ApplyPendingMigrations`/adoption) |
| 4 | Qui contrôle les migrations partielles ? | `SqliteDatabaseManager` (détection d'état + adoption physique PRAGMA) |
| 5 | Qui exécute le seed ? | `DatabaseSeeder` (via `SeedOptionsResolver`) |
| 6 | Hypothèses « fichier local » ? | backup = copie fichier ; PRAGMA ; chemin fichier ; pools SQLite ; **un seul processus prépare la base** |
| 7 | Responsabilités qui disparaissent côté serveur | résolution chemin fichier, backup par copie, reprise `mmv-optic.db`, diagnostic fichier, pools |
| 8 | Responsabilités à conserver | détection d'état, application ordonnée des migrations, seed gouverné, garde version app↔schéma |
| 9 | Diagnostics réutilisables ailleurs ? | **Non** (PRAGMA/fichier) → équivalents catalogue serveur à concevoir |
| 10 | Composants à rendre provider-spécifiques | `SqliteDatabaseManager`, `*SchemaVerifier`, `*Diagnostic`, `PersistenceErrorMapper`, upsert Notification |

**Conclusion §9** : le cycle de vie actuel est **conçu pour un seul processus sur une base fichier locale**
([ADR-PROD-DB-001 §2.2/§6](../architecture/adr-prod-db-001-multi-poste-database-strategy.md)). En multi-poste, plusieurs
postes **ne doivent pas** appliquer une migration concurremment : l'application des migrations devra être **sérialisée**
(poste désigné / verrou / phase de maintenance).

---

## 10. SQL brut et PRAGMA

Inventaire **runtime (hors migrations)** : **32 occurrences** SQLite-dialecte réparties sur **6 fichiers**.

| Primitive | Fichier | But | Spécifique SQLite ? | Équivalent à étudier | Risque |
|---|---|---|---|---|---|
| `PRAGMA table_info / index_list / foreign_key_list` | `SqliteDatabaseManager` (19), `SqliteSchemaVerifier` (6), `SqliteDateTimeDefaultVerifier` (3), `SqliteHistoricalDatabaseDiagnostic` (1) | Vérification physique de schéma (adoption/diagnostic) | **Oui** | `information_schema` / catalogues serveur | Adoption non transposable telle quelle |
| `INSERT … ON CONFLICT DO NOTHING` | `NotificationRepository` (2) | Upsert anti-doublon alerte active | **Partiel** | PostgreSQL `ON CONFLICT` **oui** ; SQL Server `MERGE`/`IF NOT EXISTS` | Nécessite expérimentation |
| Migration `GLOB`, `lower(trim())`, tables `CHECK`-abort | `20260721134634_AddNormalizedUsername…` (7) | Backfill « exact ou échec sûr » | **Oui** | Regex PostgreSQL `~` / `LIKE`, `citext` ; SQL Server `LIKE` | Chaîne serveur distincte requise |

Les PRAGMA sont concentrés dans le **cycle de vie/adoption**, pas dans les règles métier. Les règles métier
utilisent majoritairement l'API EF provider-neutre (cf. §15). Formulation retenue pour les équivalents :
*« nécessite une expérimentation »* / *« équivalent provider à confirmer »* — **aucune** équivalence affirmée sans spike.

---

## 11. Index, contraintes, collations

| Contrainte | Garantie métier | Définition actuelle | Spécifique SQLite ? | Test existant | Spike requis |
|---|---|---|---|---|---|
| `NormalizedReference` unique | Référence produit non ambiguë | `ProductConfiguration` `HasIndex().IsUnique()` sur colonne **normalisée en C#** | **Non** (normalisation applicative, pas de collation SQL) | Oui (P3-4B) | Non |
| `NormalizedUsername` unique | Login non ambigu entre postes | `UserConfiguration` `HasIndex().IsUnique()`, index `idx_users_normalized_username_unique` | **Non** (normalisation applicative) | Oui (P3-10) | Non |
| Alerte LowStock active unique | Anti-doublon d'alerte | `NotificationConfiguration` **index unique FILTRÉ** `HasFilter("\"Type\" = 'LowStock' AND \"EntityType\" = 'Product' AND \"EntityId\" IS NOT NULL AND \"ResolvedAt\" IS NULL")` | **Oui** (SQL du filtre, identifiants entre guillemets) | Oui (P3-8) | **Oui** |
| Fiche atelier courante unique | Une seule version courante | `WorkshopSheetConfiguration` **index unique FILTRÉ** `HasFilter("\"IsCurrent\" = 1")` (booléen = entier) | **Oui** (filtre + booléen 1/0) | Oui (P3-6B) | **Oui** |
| `(OrderId, Version)` unique | Versioning fiche atelier | `WorkshopSheetConfiguration` `HasIndex().IsUnique()` | **Non** | Oui | Non |
| `SaleNumber` / `OrderNumber` unique | Unicité document | `idx_sales_sale_number_unique`, `OrderConfiguration` unique | **Non** | Oui | Non |
| `ProductCategory.Name` unique | Catégorie unique | unique | **Non** | Oui | Non |
| FK `Restrict` (Customer→Sales, Product→…, Prescription→Customer) | Refus de suppression avec historique | `OnDelete(DeleteBehavior.Restrict)` | **Non** (concept EF) | Oui (P3-2B/P3-12) | Non |
| FK `Cascade` (SaleItems, OrderItems, Orders/Sale, WorkshopSheet items) | Suppression composée | `OnDelete(DeleteBehavior.Cascade)` | **Non** | Oui | Non |
| FK `SetNull` (Staff, catégorie) | Détachement souple | `OnDelete(DeleteBehavior.SetNull)` | **Non** | Oui | Non |

**Deux index filtrés sont provider-spécifiques** (SQL littéral dans `HasFilter`, booléen `= 1`, identifiants
`"…"`). PostgreSQL supporte les index partiels mais **avec sa propre syntaxe** (`WHERE "IsCurrent"` sur booléen
natif, pas `= 1`) ; SQL Server supporte les *filtered indexes* avec une syntaxe et des restrictions différentes.
**Aucune portabilité conclue sur la seule base de l'API EF commune.**

---

## 12. Migrations

**14 migrations** (`dotnet ef migrations list`) :

```
InitialCreate · AddProductEntryDate · ProductSchemaRefactoring · AddNotifications ·
AddCounterSaleFieldsToOrder · AddDepositAndRemainingAmountToOrder · RestoreSaleOrderSeparation ·
FixDateTimeDefaultValues · AddDocumentSequences · AddCustomerArchivingAndProtectHistory ·
AddProductNormalizedReferenceAndProtectHistory · AddWorkshopSheets ·
AddNotificationResolution · AddNormalizedUsernameAndSecureLocalUsers
```

| Migration (extrait significatif) | Fonction | SQL spécifique | Adoption historique | Vérif. physique | Risque provider |
|---|---|---|---|---|---|
| `AddNormalizedUsernameAndSecureLocalUsers` | Login normalisé + sécurisation | **`GLOB`, `lower(trim())`, tables `CHECK`-abort, ADD COLUMN NOT NULL + défaut transitoire, table-rebuild `AlterColumn`** | Oui (PRAGMA) | Oui | **Élevé** |
| `FixDateTimeDefaultValues` | Retrait de DEFAULT `DateTime` hérités | Table-rebuild SQLite | Oui | Oui | Élevé |
| `AddNotificationResolution` | `ResolvedAt` + index filtré actif | Index unique filtré | Oui | Oui | Élevé |
| `AddWorkshopSheets` | Fiches versionnées + index `IsCurrent` filtré | Index unique filtré | Oui | Oui | Élevé |
| `AddDocumentSequences` | Table compteurs `DocumentSequences` | Table applicative (portable) | Oui | Oui | Faible |
| `InitialCreate` → autres | Schéma de base | Types SQLite dans snapshot | — | — | Moyen |

Réponses au brief §16 :

1. **Utilisables telles quelles côté serveur ?** → **Non.** Raw SQL SQLite (GLOB, table-rebuild, PRAGMA d'adoption).
2. **Annotations/types SQLite dans les classes ?** → Oui (snapshot `OpticDbContextModelSnapshot` porte des
   annotations SQLite ; types `REAL`/`TEXT`).
3. **Opérations SQL brutes portables ?** → **Non** pour P3-10/FixDateTime ; **partiel** pour DocumentSequences.
4. **Snapshot EF provider-neutre ?** → **Non** (annotations relationnelles SQLite).
5. **Chaîne de migrations serveur distincte ?** → **Oui, requise** (nouvelle baseline générée par le provider serveur).
   **La forme exacte n'est pas choisie en P4-0** (cf. encadré ci-dessous).
6. **Conserver les migrations SQLite pour l'existant ?** → Oui (dev/test/démo/mono-poste conservent SQLite).
7. **Adoption SQLite active côté serveur ?** → **Non** (une base serveur neuve n'a pas d'historique `EnsureCreated`).
8. **Quel outil migre les données SQLite → serveur ?** → **Aucun n'existe** (à concevoir en P4, hors P4-0).

> **Ce que P4-0 fixe et ne fixe pas sur les migrations**
>
> Fixé (constaté) :
> - **14 migrations SQLite historiques existent** et sont réelles ;
> - elles **doivent rester utilisables pour SQLite local** (dev / test / démo / mono-poste) ;
> - une **stratégie de schéma/migrations serveur distincte est nécessaire** — la chaîne SQLite actuelle n'est pas
>   applicable telle quelle à un provider serveur.
>
> Non fixé (volontairement) :
> - sa **forme exacte dépend du provider choisi** (P4-2) ;
> - **P4-0 ne choisit pas** entre un **projet de migrations distinct**, un **contexte de migrations distinct**, ou
>   **une autre solution supportée et testée par le provider** ;
> - **aucune architecture de migrations n'est figée avant P4-2/P4-3.**

---

## 13. Adoption historique

`SqliteDatabaseManager.AdoptHistoricalDatabase` reconnaît une base créée par `EnsureCreated()` (sans
`__EFMigrationsHistory`) et **baseline** chaque migration **uniquement** après vérification **physique** de sa
présence réelle (colonnes/index/FK via PRAGMA) — jamais sur la seule foi du nom. Les suffixes gérés incluent
`FixDateTimeDefaultValues`, `AddDocumentSequences`, `AddWorkshopSheets`, `AddCustomerArchiving…`,
`AddNotificationResolution`, `AddNormalizedUsername…`. Ce mécanisme est **intrinsèquement SQLite** (PRAGMA) et
**sans objet** sur une base serveur **neuve**. Il reste requis pour l'existant SQLite.

---

## 14. Transactions et isolation

`ITransactionRunner` (Domain, port) → [`EfTransactionRunner`](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs)
(Infrastructure). Constats :

1. **Niveau d'isolation** : `BeginTransactionAsync(cancellationToken)` **sans niveau explicite** → **laissé au
   provider** (SQLite : sérialisation de fait). Non demandé explicitement.
2. **Transactions imbriquées** : rattachement à la transaction courante si présente
   ([lignes 58-61](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs#L58-L61)) — SQLite ne supporte pas
   l'imbrication. Comportement à revérifier côté serveur (savepoints disponibles).
3. **Stratégie de retry** : **aucune** (commentaire explicite : « relèvera d'un futur provider serveur »).
4. **Retry idempotent ?** → sans objet aujourd'hui (pas de retry). En P4, un éventuel *execution strategy* de retry
   **ne doit pas** rejouer un effet non idempotent → à cadrer avec les primitives CAS.
5. **Deadlocks** : non gérés (SQLite mono-écrivain). À prévoir côté serveur.
6. **SQL brut enrôlé dans la transaction** : oui — services et repositories **partagent le `OpticDbContext` de la
   portée** (`AddScoped`), donc l'`UPDATE`/`INSERT` participe à la transaction ouverte par le runner.
7. **Primitives correctes multi-processus ?** → **prouvées** multi-connexion **même processus** (cf. §23) ;
   **non prouvées** multi-processus/serveur.
8. **Tests à concurrence simulée dans un même processus** : oui — c'est **tout** le corpus concurrence actuel (§23).

---

## 15. Primitives atomiques (matrice réelle)

La matrice distingue **deux choses différentes** : la **surface d'API** utilisée par le code (colonne 4) et les
**dépendances physiques ou sémantiques** que cette API laisse à la base et qui **restent à prouver** sur chaque
provider serveur (colonne 5). Une API neutre **ne prouve pas** un comportement neutre.

| Primitive (flux) | Port → implémentation | Mécanisme | API/port provider-neutre ? | Dépendance physique ou sémantique à vérifier |
|---|---|---|---|---|
| **Stock décrément** | `IStockMutationService` → `EfStockMutationService.DecrementStockAsync` | `ExecuteUpdateAsync` `WHERE StockQuantity >= q` ; rows==1 | **Oui** (API EF) | sémantique des **lignes affectées** (rows==1) ; **comportement de concurrence** sous l'isolation serveur ; **classification d'erreur** en cas de conflit |
| **Stock incrément** | `IStockMutationService` → `…IncrementStockAsync` | `ExecuteUpdateAsync` relatif | **Oui** | **lignes affectées** ; **concurrence** (update relatif sous verrous serveur) |
| **Stock ajustement** | `IStockMutationService` → `…AdjustStockToAsync` | read + `UPDATE … WHERE StockQuantity == previous` ; rows==0 → conflit | **Oui** | **lignes affectées** (rows==0 = conflit) ; **niveau d'isolation** entre le read et le CAS ; **concurrence** |
| **Vente** | `ITransactionRunner` → `EfTransactionRunner` | transaction explicite + rollback + mapper | **Oui** | **niveau d'isolation** (non demandé explicitement, §14) ; **transaction et retry** (aucun retry aujourd'hui) ; deadlock/timeout serveur ; **classification d'erreur** au rollback |
| **Numérotation** | `INumberSequenceService` → `EfNumberSequenceService.NextNumberAsync` | CAS `WHERE CurrentValue = V` + retry (max 50) sur `DocumentSequences` | **Oui** (table applicative) | **lignes affectées** ; **niveau d'isolation** ; **transaction et retry** (boucle bornée face à un deadlock serveur) ; **concurrence** multi-processus |
| **Commande — transition** | `IOrderRepository` → `TryTransitionStatusAsync` | `UPDATE … WHERE Status = expected` ; rows==1 | **Oui** | **lignes affectées** ; **concurrence** |
| **Commande — prêt+fiche** | `IOrderRepository` → `TryTransitionWithWorkshopSheetAsync` | `UPDATE` avec `EXISTS/NOT EXISTS` corrélé | **Oui** | traduction **SQL spécifique** du corrélé par le provider ; **niveau d'isolation** (lecture corrélée concurrente) ; **lignes affectées** |
| **Fiche atelier — version** | `IOrderRepository` → `CreateNextWorkshopSheetVersionAsync` | CAS `IsCurrent` + `AddAsync`/`SaveChanges`, `DbUpdateException` → conflit | **Oui** | **index unique filtré** `"IsCurrent" = 1` (§11, booléen 1/0 vs booléen natif) ; **classification d'erreur** (arbitrage par exception) ; **concurrence** |
| **QC — décision** | `IOrderRepository` → `TryTakeWorkshopSheetQcDecisionAsync` | `UPDATE … WHERE IsCurrent AND QcStatus=Pending` | **Oui** | **index unique filtré** `IsCurrent` ; **lignes affectées** ; **concurrence** |
| **Paiement — solde** | `ISaleRepository` → `TrySettleRemainingBalanceAsync` | `UPDATE … WHERE RemainingAmount > 0` | **Oui** | **type monétaire** (`REAL` aujourd'hui, §17 — comparaison `> 0` sur flottant) ; **lignes affectées** ; **concurrence** |
| **Notification — alerte active** | `INotificationRepository` → `TryCreateActiveLowStockAsync` | **`INSERT … ON CONFLICT DO NOTHING`** + `SqliteParameter` | **Non** — **SQL et paramètres explicitement SQLite** | **SQL spécifique** à réécrire par provider (SQL Server n'a pas `ON CONFLICT`) ; **index unique filtré** LowStock actif (§11) ; **classification d'erreur** |
| **Notification — réconciliation** | `INotificationRepository` → `ResolveActiveLowStockAsync` | `ExecuteUpdateAsync … WHERE ResolvedAt IS NULL` | **Oui** | **lignes affectées** (lot) ; **index filtré** associé ; **concurrence** avec la création d'alerte |
| **Fournisseur — suppression** | `ISupplierRepository` → `TryDeleteIfUnusedAsync` | `ExecuteDeleteAsync … WHERE NOT products.Any()` ; détache le tracker | **Oui** | traduction **SQL spécifique** du `NOT EXISTS` ; **niveau d'isolation** (course « suppression vs création ») ; **lignes affectées** |
| **Utilisateur — unicité login** | `IUserRepository` → `UserRepository` + index unique `NormalizedUsername` | normalisation C# + index unique + arbitrage base | **Oui** côté port/index | **classification d'erreur** (§16 — arbitrage repose sur `SqliteExtendedErrorCode` 2067) ; collation/casse serveur ; **concurrence** |
| **Numérotation — table** | — → `DocumentSequences` (compteur applicatif) | — | **Oui** (table, pas de séquence native) | **type** de colonne compteur ; séquence native serveur (candidate, §18) ; **concurrence** multi-processus |

**Synthèse §15** : sur **15** primitives, **14 utilisent des ports ou des API EF provider-neutres au niveau du code
(`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, transaction EF, index), mais leur garantie complète doit encore être
prouvée sur chaque provider serveur. Une primitive contient actuellement du SQL et des paramètres explicitement
SQLite** : `NotificationRepository.TryCreateActiveLowStockAsync` (`INSERT … ON CONFLICT DO NOTHING` +
`SqliteParameter`). Toutes ont été **prouvées** en concurrence **même processus / multi-connexion SQLite** ;
**aucune** n'a été prouvée **multi-processus** ni sur **provider serveur**. Aucune primitive n'est donc considérée
**validée serveur** à ce stade — c'est le cœur du travail de validation P4.

---

## 16. Traduction des erreurs de persistance

[`PersistenceErrorMapper`](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs) transforme
`SqliteException`/`DbUpdateException` en `PersistenceException` (Domain, neutre) + `PersistenceErrorCategory`.

| Erreur métier | Détection actuelle | Code SQLite | Provider-neutre ? | Travail P4 |
|---|---|---|---|---|
| Doublon (unicité) | `SqliteExtendedErrorCode` | 2067 `CONSTRAINT_UNIQUE`, 1555 `CONSTRAINT_PRIMARYKEY` | **Non** | Classification Npgsql/SqlServer |
| Violation de contrainte | `SqliteErrorCode` | 19 `CONSTRAINT` | **Non** | idem |
| Base occupée | `SqliteErrorCode` | 5 `BUSY`, 6 `LOCKED` | **Non** | mapping deadlock/lock serveur |
| Base inaccessible | `SqliteErrorCode` | 10 `IOERR`, 14 `CANTOPEN` | **Non** | erreurs connexion serveur |
| Générique persistance | `DbUpdateException` | — | Partiel | conservé |

**Constat bloquant** : la classification dépend **directement** de `SqliteException` et de ses codes. Un provider
serveur lèvera `NpgsqlException`/`SqlException` — **aucune** branche actuelle ne matcherait, l'erreur remonterait en
`Unknown`. **`PersistenceException`/`PersistenceErrorCategory` vivent dans Domain (neutres)** : la cible P4 est une
classification **provider-spécifique en Infrastructure**, **sans** toucher Domain/Application.

---

## 17. Types et précision

| Type métier | Mapping SQLite actuel | Risque serveur | Test nécessaire |
|---|---|---|---|
| Montants (`Sale.TotalAmount/DiscountAmount/FinalAmount/DepositAmount/RemainingAmount`, `SaleItem`, `OrderItem`, `Product` prix) | **`HasColumnType("REAL")`** (virgule flottante) | **Élevé** : `REAL` = float simple précision côté PostgreSQL/SQL Server → **perte de précision monétaire** | **Oui (critique)** |
| Valeurs optiques (`Prescription` sphère/cylindre/axe…, `WorkshopSheetItem`) | `HasColumnType("REAL")` | Précision/représentation | Oui |
| Valeurs optiques (Glass/Lens/PricingTier) | `HasPrecision(x, 2)` → `decimal` | Faible (numeric portable) | Oui |
| Enums (`PaymentMethod`, `Status`, `QcStatus`, catégories…) | `HasConversion<string>()` → TEXT | Faible (varchar) | Oui |
| JSON (Product) | `HasColumnType("TEXT")` | Faible (text/jsonb) | Oui |
| Identifiants | `long` (INTEGER PK) | Séquences/identités serveur | Oui |
| Dates (`CreatedAt`, `SaleDate`, `ResolvedAt`, `QcCompletedAt`…) | horodatage applicatif (pas de DEFAULT SQL figé, R-19) | Fuseau/type timestamp | Oui |
| Booléens (`IsRead`, `IsCurrent`, `IsActive`, `IsArchived`) | INTEGER 0/1 | **Booléen natif PostgreSQL** (impact index filtré `= 1`) | Oui |

**Constat le plus grave (§17)** : les **montants sont stockés en `REAL`** (flottant, `HasColumnType("REAL")`).
Sur un provider serveur, `REAL` désigne un **flottant** (simple précision côté PostgreSQL/SQL Server) — inadapté à
la monnaie. Statut de ce constat en P4-0 :

- **risque critique identifié** ;
- **mapping serveur à expérimenter** pendant le spike (P4-1) ;
- **aucun type définitif ni précision finale n'est choisi pendant P4-0** — en particulier, aucune écriture du type
  `decimal(18,2)` ou `numeric(p,s)` n'est retenue ici : le couple type/précision relève de la décision de modèle
  postérieure au spike ;
- **aucune migration n'est proposée pendant cet audit** ;
- l'égalité des montants avant/après devra être **prouvée**, pas supposée.

---

## 18. Séquences documentaires

`INumberSequenceService` → `EfNumberSequenceService` sur table applicative **`DocumentSequences`**
(`SequenceName`, `CurrentValue`, `Prefix`, `UpdatedAt`), seedée par la migration `AddDocumentSequences`.

1. **Table applicative** (pas de séquence SQLite native).
2. **Atomicité entre connexions** : CAS `WHERE CurrentValue = V` + boucle de retry (max 50) — atomique par la base.
3. **Rollback** : si dans une transaction (vente), l'incrément est **annulé avec elle** → numéro non consommé.
   Hors transaction (ouverture de formulaire), chaque `UPDATE` s'auto-valide.
4. **Provider serveur** : peut **conserver la même table** (portable) — ou adopter une **séquence native**
   (candidate, à confirmer par spike).
5. **Séquence native nécessaire ?** → **seulement candidate**, pas nécessaire.
6. **Tests multi-processus à créer** : deux postes tirant un numéro simultanément (unicité stricte).

---

## 19. Configuration et secrets

| Donnée de configuration | Emplacement actuel | Adaptée au serveur ? | Risque |
|---|---|---|---|
| Chemin/chaîne base | env `MMV_DATABASE_PATH` → config `OpticDatabase` (`Data Source=`) → défaut LOCALAPPDATA | **Non** (fichier) | Chaîne serveur à introduire |
| Environnement | env `MMV_ENVIRONMENT` (défaut sûr = Production) | Oui | — |
| Seed démo | env `MMV_ENABLE_DEMO_SEED` (jamais hors Dev/Demo) | Oui | — |
| Admin bootstrap (login) | env `MMV_BOOTSTRAP_ADMIN_USERNAME` (défaut `admin`) | Oui | — |
| **Admin bootstrap (mot de passe)** | env `MMV_BOOTSTRAP_ADMIN_PASSWORD` — **secret, hors dépôt** ; invalide ⇒ démarrage bloqué | Oui | À sécuriser côté déploiement |

Constats : **aucun `appsettings.json`** dans le dépôt — configuration **entièrement par variables d'environnement**.
`SeedOptionsResolver` applique un **défaut sûr** (Production, sans seed, sans compte faible). **Aucun secret, hash,
mot de passe ou chaîne de connexion complète n'est présent dans le dépôt** ni recopié ici. P4 devra ajouter la
**chaîne de connexion serveur** (secret hors dépôt) et son chargement sécurisé.

---

## 20. État du déploiement

Recherche exhaustive (`find` + `rg`) : **aucun** artefact de déploiement multi-poste.

| Capacité | État |
|---|---|
| Script d'installation | **Absente** |
| Docker / Docker Compose | **Absente** |
| Scripts SQL serveur | **Absente** |
| Service Windows | **Absente** |
| Documentation réseau | **Absente** |
| Health check / retry connexion | **Absente** |
| Configuration par poste | **Partielle** (env vars génériques) |
| Mise à jour du schéma | **Présente** mais **mono-poste** (`PrepareDatabase` par poste) |

Le seul « backup » trouvé est un fichier de layout Visual Studio + une migration nommée `RestoreSaleOrderSeparation`
(faux positifs). **Tout le déploiement multi-poste reste à concevoir.**

---

## 21. Sauvegarde / restauration

| Question | Réponse (dépôt réel) |
|---|---|
| Sauvegarde SQLite actuelle ? | **Oui** — `SqliteDatabaseManager.Backup` = copie de fichier (+ sidecars `-wal`/`-shm`) |
| Export complet ? | Non (copie fichier seulement) |
| Restauration testée ? | `Restore(backupPath, databasePath)` existe ; testée sur SQLite fichier |
| Vérification d'intégrité post-restauration ? | Partielle (`VerifyAfterPreparation` PRAGMA au prochain démarrage) |
| Politique de rétention ? | **Non** |
| Sauvegarde automatique périodique ? | **Non** |
| Sauvegarde **avant migration** ? | **Oui** (backup préalable dans `PrepareDatabase`) |
| Documentation opérateur ? | **Non** |

La sauvegarde est **fichier-SQLite-spécifique**. Côté serveur, elle devra devenir `pg_dump` / sauvegarde SQL Server
**sur la base centrale**, avec rétention, planification, **et test de restauration** (aucun outil aujourd'hui).

---

## 22. Migration SQLite → serveur

**Aucun outil ni procédure d'import n'existe** dans le dépôt. Entités à transférer (source : `OpticDbContext`) :
Users, Suppliers, ProductCategories, Products (+ Glass/Lens/Accessory/Supplement/GlassSupplement/GlassPricingTier),
Customers, Prescriptions, Orders, OrderItems, WorkshopSheets, WorkshopSheetItems, Sales, SaleItems, StockMovements,
Notifications, DocumentSequences, et l'historique `__EFMigrationsHistory`.

Réponses au brief §25 (à traiter en P4, **pas** en P4-0) :

1. **Identifiants à conserver** : toutes les PK `long` (référencées par FK et par l'historique métier).
2. **Ordre d'import imposé par FK** : Suppliers/Categories → Products → Customers → Prescriptions → Sales →
   Orders → WorkshopSheets → items → StockMovements → Notifications.
3. **Données historiquement invalides** : logins non normalisables (cf. garde P3-10), montants `REAL` imprécis.
4. **Backfills « exact ou échec sûr » existants** : P3-10 (username), P3-4B (référence), FixDateTime — modèles à reprendre.
5. **Importer `__EFMigrationsHistory` ?** → **Non** vers une base serveur neuve (baseline serveur propre).
6. **Prouver le nombre de lignes** : comptes par table avant/après + réconciliation.
7. **Vérifier les contraintes après import** : unicité, FK, index filtrés re-vérifiés.
8. **Rejouabilité** : opération explicitement **non rejouable** ou idempotente (à décider).
9. **Aucune écriture pendant la migration** : postes en maintenance (verrou/arrêt).
10. **Rollback** : à expérimenter (snapshot serveur avant import).

---

## 23. Tests multi-poste existants et manquants

Corpus : **1499 tests** (Domain 649 · Application 611 · App 239).

| Garantie | Test actuel | Même processus ? | Plusieurs connexions ? | Plusieurs processus ? | Provider serveur ? |
|---|---|---|---|---|---|
| Stock CAS | `EfStockMutationServiceTests` (fichier, `Pooling=False`) | Oui | Oui | **Non** | **Non** |
| Numérotation CAS | `EfNumberSequenceServiceTests` | Oui | Oui | **Non** | **Non** |
| Transaction/rollback | `EfTransactionRunnerTests` | Oui | Oui | **Non** | **Non** |
| Fiche atelier / QC / version | `WorkshopSheetConcurrencyHardeningTests` (`contextA`/`contextB`, décorateur d'interleaving) | Oui | Oui | **Non** | **Non** |
| Alerte LowStock anti-doublon | `LowStockConcurrencyAndQueryCountTests` | Oui | Oui | **Non** | **Non** |
| Suppression fournisseur vs produit | `SupplierDeletionSqlAndTrackerTests` | Oui | Oui | **Non** | **Non** |
| Migrations / adoption | `*MigrationTests`, `*AdoptionTests` (PRAGMA) | Oui | Oui | **Non** | **Non** |

**Modèle de concurrence actuel** : plusieurs `DbContext`/connexions sur **le même fichier SQLite, dans le même
processus**, avec **interleaving déterministe** (décorateur de repository, sans délai). C'est la forme la plus forte
présente — mais **aucun test multi-processus** et **aucun test provider serveur** n'existe (impossible sans provider).

**Scénarios minimaux futurs (à créer en P4, pas maintenant)** : deux postes vendant le dernier produit ; deux postes
avançant la même commande ; deux postes générant la fiche courante ; deux postes validant le QC ; deux postes réglant
le même solde ; deux postes créant la même alerte LowStock ; deux postes créant le même login normalisé ; suppression
fournisseur vs création produit ; migration pendant qu'un poste travaille ; perte de connexion en transaction ;
reconnexion après redémarrage serveur ; timeout ; deadlock ; retry ; sauvegarde puis restauration.

---

## 24. Tests manquants (synthèse)

- **Multi-processus** (deux exécutables/connexions concurrentes réelles) : **0**.
- **Provider serveur** (PostgreSQL / SQL Server) : **0**.
- **Panne réseau / timeout / reconnexion / deadlock / retry** : **0**.
- **Sauvegarde→restauration serveur vérifiée** : **0**.
- **Import SQLite→serveur avec preuve de complétude** : **0**.

---

## 25. Méthode du spike provider (aucun gagnant désigné)

| Critère | PostgreSQL | SQL Server Express | Preuve attendue |
|---|---|---|---|
| Installation Windows | à mesurer | à mesurer | procédure reproductible |
| Administration magasin | à mesurer | à mesurer | test opérateur |
| EF Core (`Npgsql` / `SqlServer`) | à mesurer | à mesurer | prototype connecté |
| Transactions / isolation | à mesurer | à mesurer | tests |
| Index filtrés/partiels (§11) | à mesurer | à mesurer | migration réelle |
| Upsert anti-doublon (`ON CONFLICT` vs `MERGE`) | à mesurer | à mesurer | primitive réelle |
| Codes d'erreur (§16) | à mesurer | à mesurer | tests de classification |
| Précision monétaire (§17) — type et précision **à expérimenter, non choisis** | à mesurer | à mesurer | égalité montants avant/après |
| Sauvegarde/restauration | à mesurer | à mesurer | exercice complet |
| Limites produit (taille/RAM/CPU) | à vérifier (doc officielle) | à vérifier (doc officielle) | documentation officielle |
| Plusieurs postes | à mesurer | à mesurer | test multi-processus |
| Migration SQLite | à mesurer | à mesurer | prototype d'import |
| Maintenance / logs / diagnostic | à mesurer | à mesurer | panne simulée |

> **Toute** information externe (prix, limites de taille, licences, versions) est marquée
> **« À vérifier auprès de la documentation officielle pendant le spike »** et **n'est pas affirmée de mémoire**.
> Aucun score n'est attribué sans preuve.

---

## 26. Critères de décision du provider (ADR à venir)

Intégrité transactionnelle · compatibilité EF Core · portabilité des primitives P3 (§15) · concurrence réelle
multi-processus · facilité d'installation en magasin · maintenance · sauvegarde/restauration · diagnostic · sécurité ·
coût/licence **vérifiés** · comportement sous Windows · migration des bases SQLite · limites opérationnelles ·
compétences de support. **Prix, tailles limites, licences et versions ne seront pas affirmés de mémoire.**

---

## 27. Frontières d'architecture P4

| Couche | Règle | État actuel |
|---|---|---|
| **Domain** | Aucune dépendance provider/EF ; invariants inchangés | **Conforme** (FluentValidation seul ; `PersistenceException`/catégorie neutres) |
| **Application** | Aucune dépendance provider/EF ; ports = garanties ; aucun SQL/chaîne | **Conforme** (ref `MMV.Domain` seul ; `Microsoft.Extensions.DependencyInjection.Abstractions`) |
| **Infrastructure** | Composition provider, SQL spécifique, migrations, mapper d'erreurs, résilience, backup, import | **Site du travail P4** |
| **App** | Config utilisateur minimale ; état de connexion (si décidé) ; aucune règle d'intégrité UI-only | Sélection provider au composition root |

**Toute** proposition P4 introduisant un provider dans Domain/Application **doit être rejetée**. La neutralité amont
est **déjà** acquise — atout majeur.

---

## 28. Risques P4

- **Précision monétaire** : montants en `REAL` (§17) → **risque élevé** de perte sur serveur.
- **Double chaîne de migrations** (SQLite existant + serveur neuf) et divergence de comportement.
- **Index filtrés non équivalents** (`HasFilter` littéral, booléen `= 1`, §11).
- **Upsert non portable** (`ON CONFLICT DO NOTHING`, §15/§16) — SQL Server sans `ON CONFLICT`.
- **Classification d'erreurs muette** côté serveur (§16) → erreurs métier dégradées en `Unknown`.
- **Retries non idempotents** si une execution strategy est activée sans revue des CAS (§14).
- **Migration concurrente** par plusieurs postes (cycle de vie mono-processus, §9).
- **Collation/casse** : normalisation applicative OK, mais requêtes serveur à revérifier.
- **Données historiques incompatibles** ; **perte d'identifiants** à l'import (§22).
- **Deadlocks / timeouts / indisponibilité réseau / serveur arrêté** non gérés (§14/§20).
- **Sauvegarde inutilisable / restauration non testée** côté serveur (§21).
- **Mot de passe base exposé** si mal déployé (§19).
- **Versions d'app divergentes** connectées au même schéma ; **upgrade pendant qu'un poste travaille** ;
  **horloges de postes différentes** ; **installation trop complexe** pour un magasin.

Aucun risque n'est minimisé faute de test.

---

## 29. Périmètre P4

Multi-poste sur base centrale client/serveur : spike provider → ADR → fondation Infrastructure multi-provider →
portage des primitives → schéma/migrations serveur → import SQLite → configuration/secrets → déploiement base centrale
→ backup/restore → tests multi-processus → recette multi-postes → audit final. **SQLite local reste** pour
dev/test/démo/mono-poste. **PostgreSQL et SQL Server Express restent candidats ; aucun choix définitif.**

---

## 30. Hors périmètre P4

SaaS · multi-tenant · plusieurs magasins dans une installation · cloud obligatoire · API publique · application web ·
abonnement · facturation légale/TVA · devis · paiement en ligne · notifications email/push · **redesign UI global** ·
rendez-vous · dashboard fonctionnel · recherche globale · exports métier · nouvelles fonctionnalités commerciales.
Une modification UI minimale ne pourra être autorisée **plus tard** que si indispensable à la configuration de
connexion, l'affichage d'une indisponibilité serveur, ou une opération de migration décidée — **jamais en P4-0**.

---

## 31. Roadmap résultante

Voir [docs/architecture/P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md). Seule **P4-0** est
figée officiellement ; les étapes suivantes sont **dérivées de cet audit** (dépendances observées) et resteront
conditionnées au spike.

---

## 32. Critères de sortie de P4 (mesurables)

Provider choisi par **ADR** · base neuve installable · application connectable depuis plusieurs postes · migrations
serveur reproductibles · **primitives P3 portées sans perte de garantie** (§15) · erreurs provider traduites (§16) ·
données SQLite migrables ou **procédure d'échec sûr** (§22) · tests multi-processus verts (§23) · perte réseau testée ·
reconnexion testée · sauvegarde testée · restauration testée · documentation opérateur · **secrets non committés** ·
build et tests verts · **aucune vulnérabilité** · **Domain/Application provider-neutres** (§27) · recette complète
multi-postes.

---

## 33. Fichiers créés

- `docs/implementation/P4-0-multi-poste-initial-audit-report.md` (ce rapport).
- `docs/architecture/P4-multi-poste-roadmap.md`.

Fichier **modifié** à la clôture (et lui seul) :

- `.github/workflows/ci.yml` — ajout du motif `p4*` au filtre `push` (§36).

Aucun autre fichier créé ou modifié : **aucun** changement sous `src/**`, `tests/**`, `Migrations/**`, `*.csproj`,
`*.sln`, `docs/ui/**`, `design/**`, `design-handoff/**`.

---

## 34. État Git

- Branche courante : `p4-multi-poste`, créée au SHA exact de `main` `3ed8836` (divergence `0 0` à la création).
- Contenu de la phase P4-0 : **2 documents** + **1 modification de workflow** (motif `p4*`), committés et poussés
  à la clôture (§36 et §37).
- Aucun fichier de code, de test, de migration, de snapshot EF, d'UI ou de projet n'est modifié.
- Dossiers restant **non suivis** et hors périmètre : `design-handoff/`, `design/`, `docs/ui/`.

---

## 35. Verdict

```
P4-BRANCH-CREATION = GO
P4-0 AUDIT         = GO LOCAL
P4                 = STARTED
```

Justification (état à la revue finale, **avant** le commit de clôture — verdict définitif en §37) : branche
`p4-multi-poste` créée au SHA exact de `main` (`3ed8836`), branche distante identique, aucune
branche écrasée, CI `main` verte, baseline **1499** verte, aucun code/test/migration/snapshot/UI/package modifié,
inventaire SQLite complet (37 occ./12 fichiers ; 32 SQL/PRAGMA runtime/6 fichiers), **15 primitives atomiques
recensées — 14 sur des ports ou API EF provider-neutres au niveau du code mais non prouvées serveur, 1 au SQL et aux
paramètres explicitement SQLite**, 14 migrations analysées (chaîne SQLite non portable → stratégie serveur distincte
requise, forme non choisie), tests multi-processus/serveur manquants identifiés, **aucun provider choisi**, méthode
du spike définie, roadmap P4 créée, critères de sortie mesurables. **P4 reste exclusivement centré sur le
multi-poste.**

---

## 36. Revue finale et activation de la CI P4

### 36.1 Pourquoi aucune CI n'a été déclenchée à la création de la branche

À la création de `p4-multi-poste`, `gh run list --branch p4-multi-poste` renvoyait `[]`. La cause est **structurelle,
pas accidentelle** : le filtre `push` de [.github/workflows/ci.yml](../../.github/workflows/ci.yml) listait
`main`, `phase*`, `p2*`, `p3*` — **aucun motif ne couvrait `p4*`**. Un push sur une branche P4 ne pouvait donc
déclencher aucun workflow ; `pull_request` ne se déclenche que pour une PR ciblant `main` (aucune ouverte) et
`workflow_dispatch` est manuel. Aucun run n'a été inventé : la CI `main` du même SHA a servi de baseline distante
autoritaire (run `29968668277`), mais **elle ne vérifie pas les commits de la phase P4**.

### 36.2 Ajout minimal effectué

Un seul fichier est modifié, et une seule ligne y est ajoutée :

```yaml
on:
  push:
    branches:
      - main
      - 'phase*'
      - 'p2*'
      - 'p3*'
      - 'p4*'      # ← seul ajout
```

Le résultat est conceptuellement équivalent à `branches: [ main, 'phase*', 'p2*', 'p3*', 'p4*' ]`, écrit dans le
**style YAML existant du fichier** (liste en blocs, un motif par ligne, guillemets simples sur les motifs).

### 36.3 Ce qui n'a pas été touché dans le workflow

Aucun job, aucun nom de job, aucun runner, aucune version d'action, aucune commande, aucune variable, aucun step
d'audit, aucun step EF, aucune permission, aucun événement `pull_request`, aucun événement `workflow_dispatch`.
Les branches existantes `main`, `phase*`, `p2*`, `p3*` sont **conservées à l'identique**.
L'avertissement Node.js 20 émis par les actions **n'a délibérément pas été corrigé** : ce serait un changement
distinct, hors périmètre de cette étape.

### 36.4 Baseline locale rejouée avant commit

| Contrôle | Résultat |
|---|---|
| `dotnet restore MMV.sln` | up-to-date ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning · 0 Error** ✅ |
| Tests Domain | **649** ✅ |
| Tests Application | **611** ✅ |
| Tests App | **239** ✅ |
| Tests solution | **1499** total · 0 échec · 0 ignoré ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité** ✅ |
| `dotnet ef migrations has-pending-model-changes` | *No changes… since last migration* ✅ |
| Frontière Application | référence **`MMV.Domain` seul** + `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` ✅ |

### 36.5 Correction de la notion « provider-neutre »

La formulation initiale « **14 primitives provider-neutres** » était **trop forte** et a été corrigée partout
(§15 de ce rapport, §5 et P4-4 de la roadmap). Formulation retenue :

> **14 primitives utilisent des ports ou des API EF provider-neutres au niveau du code, mais leur garantie complète
> doit encore être prouvée sur chaque provider serveur. Une primitive contient actuellement du SQL et des paramètres
> explicitement SQLite.**

La matrice §15 sépare désormais **deux colonnes distinctes** — « API/port provider-neutre ? » et « Dépendance
physique ou sémantique à vérifier » — de sorte qu'aucune dépendance ne soit masquée : index filtré, type monétaire,
niveau d'isolation, sémantique des lignes affectées, classification d'erreur, transaction et retry, comportement de
concurrence, SQL spécifique. `TryCreateActiveLowStockAsync` **reste explicitement SQLite** ; les autres primitives
**restent non prouvées sur serveur**.

### 36.6 État des deux documents

- [P4-0-multi-poste-initial-audit-report.md](P4-0-multi-poste-initial-audit-report.md) : relu intégralement,
  corrigé sur les primitives (§15), la précision monétaire (§17), le statut du provider (§2) et la stratégie de
  migrations (§12) ; aucun constat réel affaibli.
- [P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) : P4-0 marquée exécutée localement sous
  réserve du commit et de sa CI ; P4-1 marquée **prochaine étape officielle** (spike, sans décision de provider) ;
  P4-2 à P4-12 présentées comme **trajectoire officielle réévaluable**, non comme des implémentations engagées.

### 36.7 Verdict avant commit

```
P4-0 = GO LOCAL, SOUS RÉSERVE DU COMMIT ET DE LA CI
```

Aucun provider n'est choisi, aucun spike n'est commencé, aucun package n'est ajouté, aucun code, test, migration,
snapshot EF ou fichier d'UI n'est modifié.

*(Cette réserve est levée en §37 : le commit de contenu et sa CI ont été obtenus et vérifiés.)*

---

## 37. Clôture de P4-0 (commit de contenu et CI vérifiés)

### 37.1 Commit de contenu

| Élément | Valeur |
|---|---|
| SHA complet | `8cf09193758c8d74a7f98ca01beb7e22e61ff66c` |
| Message | `chore(P4-0): establish multi-poste audit and CI` |
| Branche | `p4-multi-poste` (parent `3ed883634dae83399d3e93dbc1dc65ffaabdf243`) |
| `M` | `.github/workflows/ci.yml` (**+1 ligne** : motif `'p4*'`) |
| `A` | `docs/architecture/P4-multi-poste-roadmap.md` |
| `A` | `docs/implementation/P4-0-multi-poste-initial-audit-report.md` |
| Total | **3 fichiers, 1075 insertions, 0 suppression** |

### 37.2 CI du commit de contenu

| Élément | Valeur |
|---|---|
| Run | **`30045502517`** |
| URL | <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30045502517> |
| `headSha` | `8cf09193758c8d74a7f98ca01beb7e22e61ff66c` (**exact**) |
| `headBranch` | `p4-multi-poste` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |
| Durée | 6 min 16 s |

Résultats des jobs et steps — **tous `success`, aucun job obligatoire en échec** :

| Step | Résultat |
|---|---|
| Checkout · Setup .NET · Diagnostic SDK | ✅ |
| **Restore** | ✅ |
| **Build** | ✅ |
| **Test** | ✅ — App **239** · Application **611** · Domain **649** → **1499**, 0 échec, 0 ignoré |
| **Audit des packages vulnérables (JSON + sévérité)** | ✅ — *« Aucune vulnerabilite High/Critical detectee. »* |
| Restore .NET tools | ✅ |
| **Check EF Core pending model changes** | ✅ — *« No changes have been made to the model since the last migration. »* |

> La seule annotation du run est l'avertissement **« Node.js 20 is deprecated »** (`actions/checkout@v4`,
> `actions/setup-dotnet@v4`). C'est un **avertissement d'annotation, pas un échec** ; sa correction est
> **délibérément hors périmètre** de P4-0.

**La baseline distante est désormais produite par la branche P4 elle-même** : la CI `main` `29968668277` n'est plus
le seul témoin, `p4*` est réellement couvert.

### 37.3 Ce que ce commit ne contient pas

Aucun fichier sous `src/**`, `tests/**`, `src/MMV.Infrastructure/Migrations/**`, `*.csproj`, `*.sln`,
`src/MMV.App/**`, `docs/ui/**`, `design/**`, `design-handoff/**`. **Aucun code**, **aucun test**, **aucune
migration**, **aucun snapshot EF**, **aucune UI**, **aucun package**, **aucun provider**. La seule modification non
documentaire est l'ajout du motif `'p4*'` au filtre `push` du workflow.

### 37.4 État officiel des phases

- **P4-0 : définitivement close.** Audit produit et corrigé, roadmap produite, CI `p4*` activée, commit poussé,
  CI verte sur le SHA exact.
- **P4-1 : prête à commencer**, **non commencée**. Aucun spike n'a été démarré, aucun serveur installé, aucun
  package provider ajouté.
- **Aucun provider n'est choisi** : PostgreSQL (candidat principal) et SQL Server Express (candidat secondaire),
  issus de l'ADR P3-0B, restent tous deux candidats, sans notation ni gagnant.

### 37.5 Verdicts documentaires finaux

```
P4-BRANCH-CREATION = GO
P4-0-CI            = GO
P4                 = STARTED
P4-1               = READY
```
