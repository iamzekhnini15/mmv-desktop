# P4-6B — Rapport de lot : cycle de vie de la base en multi-poste

> **Statut : COMPLETE — CLOSED** — commit `3d00af8`, CI [`37226388128`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/37226388128) verte sur le SHA exact (§5).
> *(Statut initial, historique : implémenté — preuve locale — non commité — CI non exécutée.)*
> Plan de référence : [P4-6B-implementation-plan.md](P4-6B-implementation-plan.md) (révision du 04/10/2026).
> Base : branche `p4-5f-integration-tests`, HEAD `692b6da`. Date : 4 octobre 2026.
> ADR-PROD-DB-009 reste **ACCEPTED** ; aucune décision rouverte. **Aucune migration EF, aucun changement de modèle.**

## 1. Ce qui a été livré

| Tranche | Contenu | Preuves |
|---|---|---|
| **A** — version applicative | `<Version>1.0.0</Version>` dans [Directory.Build.props](../../Directory.Build.props) (source unique) ; [`ApplicationVersion`](../../src/MMV.Infrastructure/Configuration/ApplicationVersion.cs) lit l'assembly **Infrastructure**, retire `+sha`, refuse les pré-versions ; `SettingsViewModel.Version` ; le littéral `1.0.0` de `SettingsView.axaml` a disparu | U-A1, U-A2, U-A3 |
| **B** — garde en lecture seule | `ServerSchemaCompatibility` (8 états, 5 cas), `ServerCompatibilityMetadataNames`, `ServerCompatibilityMetadataReader` (SQL brut, `to_regclass` paramétré), `ServerSchemaCompatibilityGuard` (fonction pure + collecte en lecture). **Aucun site d'appel** dans `MMV.App` | U-B1 … U-B7 |
| **C** — `MMV.DatabaseManager` | exécutable `net10.0`, verbes `status` / `migrate` / `adopt-compatibility`, séquence normative §2.3 (0 → 13), verrou consultatif de session, journal `mmv_meta.migration_run`, métadonnée `mmv_meta.schema_compatibility` au minimum **calculé**, `GRANT` idempotents au rôle `--app-role`, vérification post-migration (dont droits Q-23), `RefusingBackupVerification` seule en production | U-C1 … U-C9, U-D1 … U-D4, I-1 … I-14 |
| **D** — documentation | [CONTRIBUTING.md](../../CONTRIBUTING.md) (« Étendre → migrer → contracter », SemVer, Interdits 10–11, revue 13–14) ; [ARCHITECTURE.md](../../ARCHITECTURE.md) ; [roadmap](../architecture/P4-multi-poste-roadmap.md) (état, verdicts, P4-6C → P4-9) ; ce rapport | — |
| **E** — CI | **aucune modification de `ci.yml`** (voir §5) | — |

**D4 — addendum ADR-PROD-DB-009 : INSCRIT** ([§11](../architecture/ADR-PROD-DB-009.md)) après autorisation de
l'architecte du 04/10/2026 (deuxième revue) : forme du verrou en session unique (§11.1), calcul de la
métadonnée amendé (§11.2), Q-6 … Q-24 fermées. Aucune décision du §5.2 rouverte.

## 2. Résultats

| Contrôle | Résultat |
|---|---|
| `dotnet restore` / `dotnet build MMV.sln -c Debug` | OK — **0 avertissement, 0 erreur** |
| Tests unitaires (filtre CI Windows) | **2148 / 2148** — Domain 1124 · Application 628 · App 266 · DatabaseManager 130 — 0 ignoré. Baseline 1953 intacte (+195) |
| Tests d'intégration PostgreSQL 17.10 (`MMV_INTEGRATION_REQUIRED=true`) | **104 / 104**, 0 ignoré (TRX : 104 `Passed`) — 74 existants + 30 nouveaux ; comptés **séparément** ; 3 relances Lifecycle stables ; aucune base ni rôle résiduel |
| Dérive EF SQLite (`has-pending-model-changes`) | « No changes have been made to the model since the last migration. » |
| Dérive EF PostgreSQL (variable design-time) | idem ; chaîne = `20260922001219_InitialPostgreSqlBaseline` seule |
| Migrations | **aucune créée** ; 14 SQLite + 1 PostgreSQL inchangées ; snapshots inchangés ; `MigrationChainsTests`, `ServerStartupGuardTests`, `App.axaml.cs`, `ci.yml` **non modifiés** |
| Vulnérabilités (`dotnet list package --vulnerable --include-transitive`) | aucun paquet vulnérable, tous projets |
| Paquets NuGet | **aucun nouveau** (projets de tests : mêmes paquets, mêmes versions) |
| `git diff --check` | propre |

Le serveur des preuves locales est un conteneur `postgres:17.10` (Linux) sur le poste de développement : même
image que le job CI, **pas** une preuve Windows native (O12 reste séparée).

## 3. Correspondance des tests

| Test | Emplacement | Ce qu'il prouve |
|---|---|---|
| U-A1, U-A2 | `Domain.Tests/Configuration/ApplicationVersionTests` | triplet sans suffixe ; assembly Infrastructure ; aucun appel `GetEntryAssembly(` dans la source |
| U-A3 | `App.Tests/Architecture/ApplicationVersionDisplayTests` | aucun littéral `x.y.z` dans l'axaml ; `{Binding Version}` ; VM = source unique |
| U-B1 … U-B7 | `Domain.Tests/Data/ServerSchemaCompatibilityGuardTests`, `…MetadataReaderTests` | 5 cas, ordre d'évaluation, fenêtre N-1, métadonnée absente/illisible, maintenance sans durée, E4/E7, initialisation ; U-B6 sur connexion scriptée : SELECT seuls (détecteur éprouvé par mutation) |
| U-C1 … U-C9 | `DatabaseManager.Tests` | options ; refus de sauvegarde (code 11 **avant** tout contact serveur, prouvé contre un hôte `.invalid`) ; exemple de référence §4.3 ; aucune surface publique « minimum » ; champs DP-8 ; clé de verrou unique ; `--app-role` hostile lié en paramètre et cité par le serveur ; ordre §2.3 sur doubles enregistreurs ; aucun contournement (réflexion + source) |
| U-D1 (H11) | `App.Tests/Architecture/NoServerDdlInApplicationTests` | fermeture des références de `MMV.App` sans l'outil ; tas de chaînes utilisateur des assemblys atteignables sans DDL serveur / verrou / écriture de métadonnée (3 mutations détectées) |
| U-D2 … U-D4 | existants | verts **sans modification** |
| U-E1 (Q-21) | `Domain.Tests/Data/Migrations/ExpandMigrateContractTests` | 10 contractions historiques SQLite déclarées ; baseline PostgreSQL : aucune |
| I-1 … I-4 | `IntegrationTests/Lifecycle/LifecycleLockTests` | une seule application du DDL, l'autre exécution en code 12 ; verrou sur base vide ; verrou tenu pendant 2 migrations en 2 transactions distinctes (`xmin`) ; plantage : verrou libéré, base à la dernière migration réussie, ligne `open`, marqueur laissé |
| I-5, I-6, I-12 | `LifecycleRoleTests` | rôle applicatif : 8 DDL refusés (`42501`), lecture historique + métadonnée, journal illisible ; `GRANT` idempotent (ACL identiques) ; `REVOKE` en cours de migration ⇒ code 14, métadonnée figée ; rôle inexistant ⇒ 10 sans écriture ; rôle hostile existant cité exactement |
| I-7, I-9, I-10 | `LifecycleWindowTests` | N-1 démarre **et écrit** sur N ; N-2 bloqué ; plus récent bloqué ; exemple de référence de bout en bout avec variante d'échec ; adoption puis migration |
| I-8, I-11, I-13, I-14 | `LifecycleJournalTests` | 12 colonnes, `current_user`, horodatages serveur, trace d'échec après annulation ; `lock_timeout` ⇒ `55P03` propre ; marqueur ancien bloque, intouché sans verrou, nettoyé et tracé sous verrou ; base vide → échec → relance sans adoption |
| M-1, M-3, M-9 | `LifecycleMeasurementTests` | voir §4 |
| — | `LifecycleJournalTests` (2 tests « entry point ») | `Program` réel : `status` en lecture seule avec âge **serveur** ; `migrate` ⇒ **11**, rien écrit |

## 4. Mesures (§3.4 du plan)

| # | Résultat |
|---|---|
| M-1 | `LockReleaseBehavior` de Npgsql = **`Transaction`** : le verrou natif ne couvre pas une montée multi-migrations — LK-1 confirmé |
| M-2 | **SUPERSEDED / NON BLOCKING** — mesurait la fenêtre créer → verrouiller de LK-5 sur base vide ; LK-5 n'est pas retenu (LK-1 en session unique, ADR-009 §11.1), la mesure est sans objet |
| M-3 | un verrou de session **survit** au retour de la connexion dans le pool Npgsql : `pg_advisory_unlock` explicite en `finally` **obligatoire** (implémenté) |
| M-4 | **OPERATIONAL / NON BLOCKING** — délai de détection d'une session morte (keepalive TCP) : paramètre d'exploitation du serveur, hors code de l'outil ; I-4 prouve la libération à la fin de session (`pg_terminate_backend`) |
| M-5 | une transaction par migration (I-3, `xmin` distincts) ; base à la dernière migration réussie après échec (I-4, I-8) |
| M-6 | `lock_timeout` produit `55P03` et un échec propre (I-11) |
| M-7 | rôle applicatif : lecture oui, DDL non (I-5) |
| M-8 | `pg_locks` lisible par le rôle migrateur (sondes I-3) |
| M-9 | **écart avec l'hypothèse du plan** : `Migrate()` dans une transaction utilisateur n'est pas refusé par l'avertissement EF traité en erreur ; il échoue côté PostgreSQL (`25P02`). Conséquence inchangée : l'outil n'enveloppe jamais `Migrate()` |

## 5. CI (tranche E)

`ci.yml` **n'est pas modifié**, et c'est suffisant :
- job Windows : `dotnet build MMV.sln` compile l'outil et ses tests ; `dotnet test MMV.sln --filter "FullyQualifiedName!~MMV.Infrastructure.PostgreSQL.IntegrationTests"` exécute `MMV.DatabaseManager.Tests` (le nom ne correspond pas au filtre) ; l'audit de vulnérabilités couvre les nouveaux projets ; les **deux** contrôles de dérive existants restent les seuls (aucun troisième) ;
- job `postgresql-integration` : le projet d'intégration référence l'outil (C16) et exécute I-1 … I-14 sous la garde TRX existante (0 résultat ou 1 non-`Passed` ⇒ échec). Les rôles de test sont créés par les tests avec l'utilisateur éphémère `mmv_it` (superutilisateur de l'image).

**CI exécutée** : run `37226388128` (push, branche `p4-5f-integration-tests`, SHA `3d00af80c3a4…`) — jobs
`Restore / Build / Test / Scan` et `PostgreSQL integration (P4-5F)` en **succès**. Windows : 0 warning,
0 erreur ; unitaires **2148** (Domain 1124 · Application 628 · App 266 · DatabaseManager 130), 0 échec, 0 ignoré ;
les **deux** contrôles de dérive « No changes » ; aucun paquet vulnérable. Linux : PostgreSQL 17.10, **104 / 104**,
garde TRX « 104 résultat(s), 0 non 'Passed' ». `ci.yml` non modifié.

## 6. Écarts et décisions d'exécution

1. **Fichiers de support** hors de la forme §2.1 : `MigrationExitCode.cs`, `EfSchemaMigrator.cs`, `PostgreSqlMigrationSession.cs`, `ServerCommand.cs` ; les interfaces de ports sont déclarées à côté de chaque classe du plan (doubles enregistreurs de U-C8).
2. **Session unique** (décision d'architecte, 2ᵉ revue) : verrou, DDL, `GRANT`, marqueur, journal **et `Migrate()`** passent par **une seule** connexion, ouverte par l'acquisition du verrou ; les écritures de journal restent hors des transactions de migration (avant et après `Migrate()`, en validation automatique). `SingleSessionGuard` (intercepteur EF) refuse toute ouverture initiée par EF : une session perdue n'est jamais remplacée.
3. **Défaut corrigé pendant les preuves serveur** : la description du détenteur du verrou (code 12) ne filtrait pas `pg_locks` sur la base courante ; les verrous consultatifs étant propres à chaque base, elle aurait pu nommer une exécution d'une **autre** base MMV du même serveur. Corrigé (test unitaire RED → GREEN).
4. **Âge du marqueur** (`status`) calculé par le **serveur** (`now() - maintenance_started_at`) : la règle d'architecture sur le temps ambiant (ADR-004) a refusé `DateTimeOffset.UtcNow`.
5. **Étape 3** : E7 (tables sans historique) n'est **pas** refusé par `migrate` — I-14 exige que l'échec survienne à l'étape 8.
6. **`adopt-compatibility`** refuse (code 15) une métadonnée déjà initialisée ou un historique EF vide ; le calcul du §4.3 traite la dernière ancre `adopt` comme **nouveau point de départ**.
7. **Étape 9** échoue aussi si le rôle applicatif détient un droit sur `migration_run`.
8. Valeurs non fixées par le plan : `--wait` 10 s par défaut (max 3600 s) ; `lock_timeout` 15 s ; variable `MMV_MIGRATOR_CONNECTION_STRING` absente ⇒ code 10 ; trace locale dans `%LOCALAPPDATA%\ManageMyVision\DatabaseManager\migration-trace.log` (emplacement définitif : P8, DI-9).
9. **Règle d'incrément SemVer** (DP-7.4 la délègue à P4-6B) inscrite dans CONTRIBUTING.md — **à valider en revue**.
10. **Chaînes de migrations de test** (I-3, I-7, I-9 … I-12) : migrations écrites à la main, **dans le seul projet d'intégration**, injectées par `ReplaceService<IMigrationsAssembly>` (API interne EF, `EF1001` supprimé localement). Les chaînes de production, le modèle et les snapshots sont intacts.
11. Interdit n° 2 de CONTRIBUTING.md et ARCHITECTURE.md : la phrase « application des migrations PostgreSQL non décidée » est remplacée par la décision DP-1.

## 7. Revue de code finale et problèmes ouverts

Revue indépendante (contexte neuf) de l'arbre de travail. Corrigés, chacun par un test RED → GREEN :

- **Faux succès** : une relance pouvait sortir en **0** en laissant la ligne en état d'initialisation. Filet
  conservé : jamais de succès si la ligne reste en initialisation (code 14, §4.3.1) —
  `Run_that_leaves_the_row_in_initialization_state_is_never_a_success`. Depuis l'amendement de la règle d'ancre,
  la relance après une première installation en échec **réussit** (scénario 1 ci-dessous) ; le filet ne couvre
  plus qu'un état incohérent.
- **I-12** n'affirmait que `NotBeNull` : il affirme désormais la ligne exacte.
- **Annulation** : la relecture des Appliquées après migration utilise un jeton neutre — la ligne de journal se
  clôt même si l'appelant annule. Test : `Cancellation_during_migration_still_closes_the_journal_row`.
- **`Program`** ne laisse plus échapper d'exception : chaîne mal formée ⇒ 10 (jamais restituée), serveur ⇒ 20,
  autre ⇒ 13. Test : `Malformed_connection_string_exits_10_without_echoing_it`.

**Décisions d'architecte de la 2ᵉ revue (04/10/2026) — appliquées :**

1. **Règle d'ancre amendée** (ADR-009 §11.2) : l'état physique vérifié est comparé à celui de la dernière ancre ;
   chaque migration est attribuée à la release qui l'a physiquement appliquée (journal, échecs et plantages
   compris). `IServerMigrationJournal.ReadRunsAsync` lit désormais **toutes** les exécutions antérieures.
   Scénarios exigés, tous prouvés en unitaire (`CompatibilityMinimumTests`, RED observé sur l'ancienne règle)
   **et** sur serveur :

   | # | Scénario | Test serveur | Résultat |
   |---|---|---|---|
   | 1 | première installation, échec étape 9, relance | `I14_variant_first_install_failing_at_step_9_then_relaunch_anchors_the_verified_baseline` | (`1.0.0`, `1.0.0`), maintenance `NULL`, sans adoption |
   | 2 | migration appliquée, vérification en échec, relance | `I12_grant_is_idempotent_and_mandatory_for_success` | (`1.1.0`, `1.0.0`) |
   | 3 | relance par une release plus récente sans migration | `Scenario3_relaunch_by_a_newer_release_without_migration_keeps_the_producer_version` | `schema_version` = `1.1.0`, pas `1.2.0` |
   | 4 | release sans migration | `I9_reference_example_end_to_end` | ligne inchangée |
   | 5 | N → échec → nouvelle release | `Scenario5_N_then_failure_then_new_release_keeps_N_minus_2_blocked` | (`1.3.0`, `1.2.0`) ; poste 1.1.0 (N-2) **E3b** ; 1.2.0 E3a |

2. **Session unique** (ADR-009 §11.1) : `PostgreSqlMigrationSession` ne construit plus qu'un contexte ;
   `EfSchemaMigrator` n'ouvre jamais la connexion ; `SingleSessionGuard` refuse toute ouverture par EF. Preuves :
   `SingleSessionTests` (unitaires) ; `Single_session_holds_the_lock_and_runs_the_migration` (une seule session
   du migrateur, pid du verrou = pid de `Migrate()`) ;
   `Killing_the_lock_holder_stops_the_migration_and_no_concurrent_run_cleans_or_continues_meanwhile` (exécution
   concurrente refusée en 12 sans toucher au marqueur ; seul le détenteur tué ⇒ migration interrompue, aucune
   session ne la poursuit ; la migration interrompue n'est pas validée, tandis que les migrations validées avant l'interruption restent appliquées — leur provenance est reconstruite depuis le journal ; marqueur intact ; puis une nouvelle exécution nettoie) ; la sonde
   d'I-3 vérifie désormais que **la session qui migre** détient le verrou pendant chaque migration.

**Mineurs différés** : un outil plus ancien que la base n'est refusé qu'à l'étape 9 (pourrait l'être en 15 à
l'étape 3, sans écriture) ; U-E1 ne détecte ni les suppressions en SQL brut ni le rétrécissement de type ; les
refus d'arguments de `Program` n'écrivent pas la trace locale ; I-14 évalue la garde sous le rôle migrateur ;
`ApplicationVersion.Normalize` accepte des composantes au-delà d'`int`.

**Autres points ouverts :**
- M-2 : SUPERSEDED / NON BLOCKING ; M-4 : OPERATIONAL / NON BLOCKING (§4).
- O12 (PostgreSQL natif Windows) : hors P4-6B, inchangé.

## 8. Critères de sortie (§10 du plan)

| # | Critère | État |
|---|---|---|
| 1 | Version unique SemVer, affichée, journalisée (H7) | **prouvé** (U-A1 … U-A3 ; `app_version` journalisée, I-8) |
| 2 | Garde en lecture seule, 5 cas, 8 états | **prouvé** (U-B1 … U-B7) |
| 3 | Aucun chemin de DDL PostgreSQL dans `MMV.App` (H11) | **prouvé** (U-D1, U-D2) |
| 4 | Aucun changement de modèle, aucune migration, deux dérives vertes (H16) | **prouvé**  ; CI `37226388128` verte |
| 5 | SQLite sans régression, 14 migrations, 1953 tests | **prouvé**  ; CI `37226388128` verte |
| 6 | « Étendre → migrer → contracter » inscrit et outillé | **prouvé** (D1, U-E1) |
| 7 | Journal DP-8 | **prouvé sur serveur (local + CI)** (U-C5, I-8)  ; CI `37226388128` verte |
| 8 | Minimum calculé (H17) | **prouvé sur serveur (local + CI)** (U-C3, U-C4, I-9, I-10, I-14)  ; CI `37226388128` verte |
| 9 | Action explicite, opérateur exigé et journalisé (H13) | **prouvé sur serveur (local + CI)** (U-C1, I-8)  ; CI `37226388128` verte |
| 10 | Aucune migration sans sauvegarde vérifiée (H12) | **prouvé** (U-C2, U-C9, entry point ⇒ 11) |
| 11 | Sérialisation (H2) | **prouvé sur serveur (local + CI)** (I-1 … I-4)  ; CI `37226388128` verte |
| 12 | Rôles DP-5, Q-23 | **prouvé sur serveur (local + CI)** (I-5, I-6, I-12, U-C7)  ; CI `37226388128` verte |
| 13 | Fenêtre N-1 (H14) | **prouvé sur serveur (local + CI)** (U-B2, I-7)  ; CI `37226388128` verte |
| 14 | Commit + CI verte sur le SHA exact | **PROUVÉ** — `3d00af8`, CI `37226388128` |

```
P4-6B APPLICATION VERSION     = IMPLEMENTED — LOCAL PROOF — CI 37226388128 GREEN
P4-6B COMPATIBILITY GUARD     = IMPLEMENTED — READ-ONLY, NOT WIRED — LOCAL PROOF — CI 37226388128 GREEN
P4-6B DATABASE MANAGER        = IMPLEMENTED — LOCAL PROOF — CI 37226388128 GREEN
P4-6B MIGRATION LOCK          = IMPLEMENTED — LK-1, SINGLE SESSION (LOCK = MIGRATE) — LOCAL SERVER PROOF — CI 37226388128 GREEN
P4-6B SERVER JOURNAL          = IMPLEMENTED — LOCAL SERVER PROOF — CI 37226388128 GREEN
P4-6B APP ROLE GRANT          = IMPLEMENTED — LOCAL SERVER PROOF — CI 37226388128 GREEN
P4-6B EXPAND MIGRATE CONTRACT = DOCUMENTED + TOOLED — CI 37226388128 GREEN
P4-6B SERVER PROOFS           = LOCAL PG 17.10 104/104 (0 SKIPPED) — CI 37226388128 GREEN
P4-6B ADR ADDENDUM            = WRITTEN — ADR-009 §11 — AUTHORIZED 2026-10-04
P4-6B DATABASE LIFECYCLE      = COMPLETE — CLOSED — 3d00af8 — CI 37226388128 SUCCESS
V1 MULTI-POSTE                = NOT GO
```

## Annexe A — Addendum

Inscrit dans [ADR-PROD-DB-009 §11](../architecture/ADR-PROD-DB-009.md) le 04/10/2026.
