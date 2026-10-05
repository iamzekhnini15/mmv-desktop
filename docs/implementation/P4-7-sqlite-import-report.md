# P4-7 — Import contrôlé SQLite → PostgreSQL : rapport de clôture

> Lot P4-7 de la [roadmap P4](../architecture/P4-multi-poste-roadmap.md) — obligation **O11**
> ([ADR-PROD-DB-002](../architecture/adr-prod-db-002-server-database-provider-selection.md)), critère de sortie **7**.
> Procédure opérateur : [P4-7-sqlite-import-procedure.md](../operations/P4-7-sqlite-import-procedure.md).
> **Aucune décision d'architecture nouvelle** : chaque règle ci-dessous est tirée d'un ADR accepté (§2).

## 1. Commits et CI (SHA exacts)

| Commit | Contenu | CI |
|---|---|---|
| `7c06c23` | `feat(P4-7)` : verbe `import-sqlite`, plan, conversions, rapport, tests | **`37299060335`** verte sur le SHA exact — job Windows : unitaires **2441/2441** (App 268 · DatabaseManager 345 · Domain 1200 · Application 628), audit ; job Linux PostgreSQL 17.10 + TLS : **212/212**, 0 ignoré |
| `fbcaf5a` | `test(P4-7)` : l'essai à blanc ne consomme pas la sauvegarde vérifiée (bout en bout, TLS) | CI de la branche après poussée (roadmap §6) |
| *(ce commit)* | `docs(P4-7)` : rapport, procédure opérateur, roadmap | idem |

Base : `origin/p4-multi-poste` = `b3eebaf`. Branche `p4-7`, avance rapide de `p4-multi-poste` après CI verte.

## 2. Ce qui a été livré — règles et origine

`MMV.DatabaseManager import-sqlite --source <copie à froid> --source-time-zone <fuseau> --operator <réf>
--backup-ref <manifeste vérifié> --report <fichier neuf> [--dry-run] [--wait <s>]`, sous le rôle **migrateur**
(`MMV_MIGRATOR_CONNECTION_STRING`, TLS VerifyFull + SCRAM, D-14). Ordre normatif (`SqliteImportRunner`) :

| Étape | Contrôle | Refus | Source de la règle |
|---|---|---|---|
| 0 | options, fuseau **obligatoire** (aucun défaut), rapport créé **neuf** | 10 | ADR-004 décision 8 (« aucune interprétation en silence ») |
| 1 | source **sans contact serveur** : copie à froid (`-journal`/`-wal` non vides refusés), lecture seule, instantané unique, `integrity_check`, `foreign_key_check`, historique = **14 migrations SQLite exactes**, tables et colonnes = modèle (table inconnue **porteuse de données** refusée, vide exclue et consignée), **conversion à blanc de chaque valeur** | 24 | O11 ; « aucun import partiel silencieux » (roadmap) |
| 2 | preuve de sauvegarde vérifiée de la cible (P4-9), avant le verrou | 11 | DP-10, B-1 |
| 3 | verrou de migration (**même clé** que `migrate` : exclusion mutuelle) | 12 | DP-2 |
| 4 | sauvegarde ⇔ base courante (cluster, base + OID, historique, lignes, âge ≤ 2 h), schéma à jour, métadonnée | 11 / 25 | B-4, R-10, DP-3 |
| 5 | marqueur de maintenance posé (un poste qui démarre est bloqué, E-état P4-6C), levé en `finally` | — | DP-6 |
| 6 | **une** transaction : `ACCESS EXCLUSIVE` sur les 21 tables, cible **vierge** (seules les lignes `HasData`), lignes `HasData` remplacées par la source, insertion dans l'ordre des clés étrangères, **identifiants conservés**, séquences d'identité recalées, **relecture ligne à ligne**, totaux numériques **relus du serveur** | 25 / 26 | O11 (HasData des deux côtés, séquences, complétude) |
| 7 | `COMMIT` (ou `ROLLBACK` en `--dry-run`), relecture des comptes | 14 | — |
| ⟲ | après échec : `ROLLBACK`, **relecture** de la cible (session, sinon connexion neuve) comparée à l'état d'avant import → `TargetUnchangedVerified` | 26 | — |
| ⟲ | après validation : `restore-backup` de la sauvegarde citée dans une base **neuve**, jamais en place | — | R-9 |

**Conversions** (`ImportValues`, type physique PostgreSQL décisif, classe de stockage SQLite stricte) :

- **montants** `numeric(p,s)` : valeur du `REAL` relue au plus court (« R »), arrondie à `s` décimales
  **`ToEven`**, débordement refusé ; réconciliation par colonne (total source, total importé, écart, lignes
  arrondies, total **relu du serveur**) — ADR-003 §5.1 ; le rapport dit explicitement que le résultat **n'est pas**
  une restitution fidèle ;
- **instants** : texte EF sans fuseau, **heure locale du magasin d'origine** → UTC — ADR-004 décision 8 ; heure
  inexistante **refusée**, heure ambiguë = décalage standard **listée** ; formes avec fuseau refusées (jamais
  réinterprétées) ; troncature à la **microseconde** (résolution de `timestamptz`, ce que Npgsql ferait en silence)
  **comptée** ;
- **dates civiles** : règle P4-5D-R (`CivilDateFormat`), forme historique tronquée et comptée, forme irréparable
  refusée ;
- **textes** : NUL refusé, longueur en **caractères** ≤ `character varying(n)`, énumérations par **nom exact** ;
- **entiers / booléens / réels** : bornes et classes de stockage exactes, aucun texte interprété.

`__EFMigrationsHistory` n'est **jamais** lu comme donnée ni écrit : l'historique PostgreSQL reste celui de la
chaîne PostgreSQL (ADR-005 §5.8, K-10, RA-6). Rapport JSON : comptes par table, réconciliations, règles appliquées,
valeurs refusées ou notables **localisées** (table, clé, colonne), identifiant de la sauvegarde de retour — **sans
secret**, jamais écrasé.

## 3. Critères de sortie

| Exigence (roadmap §P4-7, demande du lot) | Preuve |
|---|---|
| identifiants conservés, ordre FK | `Populated_source_is_imported_…` (clés non contiguës 7, 10, 13 …), `Plan_orders_every_parent_before_its_children` |
| complétude (comptes avant/après) | comptes indépendants SQLite ⇔ PostgreSQL par table ; relecture ligne à ligne dans la transaction |
| montants **exacts** | `Money_reconciliation_…` (2.675 → 2.68, x.125 → x.12, total relu = total attendu), `Money_is_rounded_half_to_even_…` |
| unicité, contraintes | `Server_constraint_violation_rolls_back_…` (doublon refusé par le serveur : 23505) |
| pas d'import de `__EFMigrationsHistory` | `Empty_source_…_never_touches_the_postgresql_history`, `Plan_covers_…_never_the_ef_history` |
| aucun import partiel silencieux | violation, coupure, ligne manquante, valeur altérée : **tout** annulé, cible relue identique |
| rollback défini et prouvé | transaction unique + relecture (`TargetUnchangedVerified`) ; après validation : `Import_through_the_tool_requires_a_current_verified_backup_and_restore_backup_undoes_it` (vrais `pg_dump`/`pg_restore`) |
| rejouabilité définie | import refusé sur cible non vierge (25) ; après annulation, relance sur la même cible (`Interrupted_import_…_a_rerun_completes`) |

### Tests exigés ↔ tests

| Exigé | Test(s) |
|---|---|
| SQLite vide | `Empty_source_imports_only_the_seed_rows_…` |
| SQLite avec données, FK, 1-N, N-N (`GlassSupplements`), NULL, chaînes non ASCII, énumérations | `Populated_source_is_imported_with_its_identifiers_values_nulls_and_relations` |
| ordre des FK | `Plan_orders_every_parent_before_its_children` (≥ 16 arêtes) |
| valeurs monétaires | `Money_reconciliation_…`, `Money_is_rounded_half_to_even_…` (5 cas), `Money_accepts_…_refuses_overflow_…` |
| DateTime | `Instants_are_read_as_store_local_time_…`, `…_microseconds_…`, `Nonexistent_local_time_…` (unitaire + serveur), `Ambiguous_local_time_…`, `Instant_before_the_representable_utc_range_is_refused` |
| dates civiles | `Civil_dates_follow_the_p4_5d_r_rule` (3 formes), `Unrepairable_civil_date_is_refused` |
| données volumineuses | `Large_source_is_imported_completely_and_verified_row_by_row` (2 500 lignes × 21 tables ≈ 52 500 lignes) |
| doublons, violation de contrainte | `Server_constraint_violation_rolls_back_…` ; doublon de clé détecté au profilage source |
| données invalides | `Every_non_importable_value_is_located_in_the_report_…` (6 cas : énumération, longueur, date, instant, booléen, montant) |
| ligne manquante, incohérence | `Missing_or_altered_row_on_the_server_is_detected_before_commit` (déclencheurs serveur) |
| `__EFMigrationsHistory` | voir §3 |
| rollback, import interrompu, reprise | `Interrupted_import_is_rolled_back_by_the_server_and_a_rerun_completes` (`pg_terminate_backend` en pleine transaction) ; `Dry_run_…` |
| PostgreSQL inaccessible | `Unreachable_postgresql_fails_cleanly_and_leaves_the_source_file_untouched` (code 20) |
| import déjà effectué | `Second_import_is_refused_because_the_target_already_holds_data`, `Target_with_a_workstation_write_or_without_migrations_is_refused` |
| row-count, post-import | relecture dans la transaction + relecture après validation (code 14 si divergence) |
| source : intégrité, orphelins, historique, table/colonne inconnue, copie non fermée | `SqliteImportRunnerTests` (8 tests, **journal d'appels serveur vide**) |
| verrou, sauvegarde | `Import_and_migrate_exclude_each_other_…`, `Busy_lock_…`, `Backup_that_is_not_the_current_state_…`, `Import_is_refused_with_an_outdated_or_unverified_backup_…` (vraie preuve P4-9) |
| essai à blanc | `Dry_run_…` + preuve qu'il **ne consomme pas** la sauvegarde (même manifeste ensuite accepté) |

### Tests vus en échec

Mutations temporaires de l'outil, puis restauration (fichiers identiques) :

- relecture ligne à ligne neutralisée (`mismatch = null`) ⇒ `Missing_or_altered_row_…` **échoue** (la ligne
  supprimée par le déclencheur passe) ;
- arrondi `AwayFromZero` au lieu de `ToEven` ⇒ `Money_is_rounded_half_to_even_…` (unitaire) et
  `Populated_source_…`, `Money_reconciliation_…` (serveur) **échouent**.

Le garde existant `BackupSeamTests.Program_constructs_only_the_proof_verification_with_the_policy_age` a échoué
sur la première version (deux constructions de la vérification de sauvegarde dans `Program`) : corrigé par un point
de construction **unique** partagé par `migrate` et `import-sqlite`, **sans modifier le test**.

## 4. Audit de sécurité

- connexion du **migrateur** seulement, par variable d'environnement, durcie (TLS VerifyFull, SCRAM, D-14) ; aucune
  option ne porte de secret ni ne relâche la sauvegarde (test dédié) ;
- source ouverte en `Mode=ReadOnly` : empreinte SHA-256 inchangée après chaque exécution (tests) ;
- identifiants SQL issus du **modèle**, valeurs en paramètres liés ; séquences recalées par instruction produite par
  le serveur (`format('%I', …)`) ;
- sortie, trace et rapport vérifiés **sans secret** (tests de bout en bout) ;
- `dotnet list package --vulnerable --include-transitive` : **aucune** vulnérabilité ; aucun paquet ajouté.

## 5. Schéma et migrations

Aucun changement de modèle EF, **aucune migration** : 14 SQLite + 1 PostgreSQL, instantanés identiques à `b3eebaf`
(`git diff` vide sur les deux dossiers). `has-pending-model-changes` vert sur les deux chaînes. Le journal serveur
`mmv_meta.migration_run` n'est pas modifié (contrainte `run_kind` inchangée) : l'import est tracé par la trace
locale et par son rapport.

## 6. Limites et questions ouvertes

| # | Sujet | Effet |
|---|---|---|
| **Q-P4-7-1** | Le `Kind` historique est **indétectable** (P4-5D-R Q4). La règle ADR-004 §8 (tout instant = heure locale) est exacte pour une base écrite par la ligne de production actuelle (`main`, P3) ; une base SQLite écrite par une version **≥ P4-5D** (instants déjà UTC) serait décalée. Aucune telle version n'est déployée. | non bloquant ; procédure §1 : copie issue de la version de production |
| **Q-P4-7-2** | Résolution d'une heure ambiguë par le **décalage standard** (comportement de `TimeZoneInfo`), chaque valeur listée au rapport | non bloquant ; confirmation d'architecte demandée |
| **RR4** | `--dry-run` sur une **copie de base de production réelle** avant déploiement | recette P4-11 |
| **O12** | Preuve **Windows native** (poste réel, volumétrie réelle, durée) | recette P4-11 |

## 7. Effet sur la trajectoire

Critère de sortie **7** satisfait sur CI Linux (PostgreSQL 17.10) ; preuve Windows native → P4-11. **P4-10**
débloqué côté données. V1 multi-poste reste **NOT GO** (P4-10, P4-11, P4-12).
