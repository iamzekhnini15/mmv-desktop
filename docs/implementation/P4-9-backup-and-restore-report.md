# P4-9 — Sauvegarde et restauration centrales : rapport de clôture

> **Verdict : `P4-9 = COMPLETE — CLOSED`** (5 octobre 2026). Branche `p4-9`, base `bb4fd1d` (clôture P4-8).
> Décisions : [ADR-PROD-DB-011](../architecture/adr-prod-db-011-backup-and-restore.md) ·
> procédure : [P4-9-backup-restore-procedure](../operations/P4-9-backup-restore-procedure.md).

## 1. Commits et CI (SHA exacts)

| Commit | Contenu | CI | Résultat |
|---|---|---|---|
| `20cb116` | cœur : `backup`, `verify-backup`, `ProofBackupVerification`, rapprochement sous verrou, CI PGDG 17 | `37247312258` | **success** — Windows : unitaires 2356, audit, dérive EF ×2 ; Linux : `pg_dump` 17.11, PostgreSQL 17.10 IT 180/180, garde P4-9 23 |
| `1562713` | politique R-1…R-10 : `prune-backups`, `configure-backup`, `restore-backup` | `37248458196` | **success** — Windows : unitaires **2373** (DatabaseManager 288 · App 267 · Application 628 · Domain 1190), aucune vulnérabilité High/Critical, dérive EF SQLite + PostgreSQL verte ; Linux : IT **185/185**, 0 ignoré, garde P4-9 **28** |

Base de comparaison P4-8 : unitaires 2289, IT 157. Écart : **+84** unitaires, **+28** IT (toutes P4-9).

## 2. Ce qui a été livré

| Verbe | Identité | Effet |
|---|---|---|
| `backup` | sauvegarde | instantané `REPEATABLE READ` exporté ; empreinte (cluster, base, OID, `now()`, historique EF, lignes par table) lue dans l'instantané ; `pg_dump -Fc --snapshot` ; SHA-256 ; fichier provisoire renommé ; manifeste écrit en dernier ; aucun fichier en cas d'échec (code 21) |
| `verify-backup` | administrateur | SHA-256 + `pg_restore --list` ; verrou consultatif ; nettoyage des bases marquées résiduelles ; restauration réelle `--single-transaction --exit-on-error` dans une base neuve fermée à `PUBLIC` ; comparaison au manifeste ; preuve ; base toujours supprimée (code 22 / 12) |
| `restore-backup` | administrateur | sauvegarde vérifiée seulement ; base **neuve** propriété du migrateur (rôle privilégié refusé, 16) ; jamais d'écrasement (10) ; fermée aux postes jusqu'à `provision` |
| `prune-backups` | aucune connexion | rétention R-3 ; manifeste supprimé d'abord ; illisibles et fichiers étrangers jamais touchés ; orphelins > 24 h (code 23) |
| `configure-backup` | sauvegarde | probe TLS + SCRAM + identité non privilégiée, puis fichier DPAPI CurrentUser ; `backup` sans options de connexion l'utilise |
| `migrate` / `adopt-compatibility` | migrateur | `ProofBackupVerification` : preuve avant verrou ; sous verrou, premier contrôle lecture seule : installation, base (nom + OID), historique, lignes, âge ≤ 2 h ; journal = identifiant de sauvegarde |

## 3. Critères de sortie

| # | Exigence | Preuve |
|---|---|---|
| 1-2 | sauvegarde PostgreSQL, `pg_dump` | `BackupCreator` ; IT `First_installation…`, `Manifest_records…` (vrai `pg_dump` 17) |
| 3-8 | manifeste, intégrité, version du schéma, horodatage serveur, migrations, lignes | `BackupFiles` (format strict), IT `Manifest_records_the_snapshot_identity_history_and_exact_row_counts` (identifiant de cluster et OID relus sur le serveur) |
| 9-11 | restauration scratch, vérification, preuve | IT `Restore_is_real_isolated_from_workstations_compared_and_always_dropped` |
| 12-13 | « vérifiée » déterminable par l'outil, aucune migration sans elle | `ProofBackupVerification`, `MigrationRunner` étape 3a ; IT `First_installation…` (11 avant preuve, 0 après) |
| 14-16 | rôles séparés, pas d'élévation du migrateur, l'application ne restaure pas | IT `Migrator_cannot_verify_by_restoring_and_is_never_granted_createdb`, `Application_and_backup_roles_can_neither_restore_nor_write_nor_create_databases`, `Application_role_cannot_back_up…` |
| 17 | aucune fuite de secret | chaque exécution IT de l'outil vérifie sortie + trace ; fichiers JSON vérifiés ; unitaire `Client_tools_get_a_hardened_environment…` |
| 18 | protection des fichiers | 0600 sous Linux (IT) ; ACL NTFS + BitLocker par procédure (R-4, R-6) |
| 19-20 | erreurs, reprise, idempotence | IT `Scratch_database_left_by_an_interrupted_run…`, `Verification_is_idempotent…`, `Incomplete_backup_fails…`, `Missing_pg_dump…` |

### Tests exigés ↔ tests

| Exigé | Test(s) |
|---|---|
| backup valide | IT `First_installation…`, `Manifest_records…` |
| corrompu / checksum incorrect | unit `Corrupted_backup_file_is_refused`, `Incorrect_checksum_in_the_manifest_is_refused` ; IT `Corrupted_backup_fails_the_structural_check…` |
| manifeste incorrect | unit `BackupDocumentsTests` (19 cas), `Manifest_changed_after_its_verification_is_refused` |
| incomplet | unit `Incomplete_backup_file_is_refused` ; IT `Incomplete_backup_fails_and_leaves_neither_proof_nor_scratch_database` |
| mauvais schéma / mauvais historique | IT `Restored_history_different_from_the_manifest_is_refused` ; unit `Different_migration_history_is_refused` |
| restauration réussie / échouée | IT `Restore_is_real…`, `Disaster_recovery…` / `Incomplete_backup…`, `Restored_row_counts…` |
| base scratch invalide | IT `Existing_scratch_database_is_never_overwritten`, `Scratch_database_equal_to_the_backed_up_database_is_refused` ; unit `Invalid_scratch_database_is_refused…` (6) |
| trop ancien | IT `Verified_backup_older_than_the_maximum_age_is_refused_on_the_server_clock` (horloge serveur) ; unit bornes exactes |
| autre base / autre installation | IT `Verified_backup_of_another_database_is_refused` ; unit `Backup_of_another_installation_is_refused` (*) |
| absence / non vérifié / migration sans backup | unit `Missing_backup_reference…`, `Backup_without_proof…` ; IT `Proof_removed_after_verification…`, `First_installation…` |
| rôles insuffisants / escalade | IT `Migrator_cannot_verify…`, `Application_role_cannot_back_up…`, `Application_and_backup_roles…`, `Restore_of_an_unverified_backup_or_to_a_privileged_owner…`, `Stored_backup_connection_accepts_only_a_non_privileged_role…` |
| secrets | assertion systématique de `ToolAsync` (IT), unitaires `BackupCommandTests` |
| concurrence | IT `Verification_already_running_is_refused_with_code_12…`, `Concurrent_verifications_serialize…`, `Concurrent_backups_produce_distinct_complete_backups` |
| reprise après échec / idempotence | IT `Scratch_database_left_by_an_interrupted_run…`, `Verification_is_idempotent…` ; unit `BackupPrunerTests` (orphelins) |

(*) « Autre installation » en IT exigerait un second cluster TLS valide ; l'IT prouve que l'identifiant comparé est
bien le `system_identifier` réel du serveur, et la comparaison est couverte en unitaire.

### Tests vus en échec

- **Mutations injectées puis retirées** : contrôle SHA-256 supprimé (3 unitaires rouges) ; rapprochement sous verrou
  neutralisé (non détecté au départ en unitaire → test `Verified_backup_is_confirmed_only_against…` ajouté, rouge
  sur le mutant) ; comparaison du contenu restauré supprimée et `REVOKE … FROM PUBLIC` supprimé (4 IT rouges) ;
  règle « dernière vérifiée » supprimée (1 unitaire rouge).
- **Premier passage IT** : 21/22, puis un échec intermittent — un verrou consultatif de session tenu par une
  connexion de test **rendue au pool** (mesure M-3 de P4-6B) bloquait les vérifications suivantes. Test corrigé
  (connexion sans pool) ; 3 passages consécutifs verts. La production n'est pas concernée (connexions sans pool,
  déverrouillage explicite).
- **Course corrigée avant commit** : une vérification refusée (code 12) retirait la preuve qu'une vérification
  concurrente venait d'écrire ; la preuve n'est désormais retirée **que sous le verrou**.

## 4. Audit de sécurité

| Sujet | Constat |
|---|---|
| Secrets | jamais en argument ; stdin ou DPAPI ; `PGPASSWORD` dans l'environnement du seul processus enfant ; variables `PG*` héritées retirées ; diagnostics expurgés et bornés ; aucun secret dans manifeste, preuve, trace (vérifié) |
| Commandes / injection | aucun shell, `ArgumentList` ; identifiants SQL cités par le serveur (`format('%I')`) ; nom de base cible `^[a-z_][a-z0-9_]{0,62}$` (aucune expansion conninfo) ; fichier de sauvegarde = nom simple, jamais un chemin (traversée refusée) ; chemins absolus exigés |
| TLS | `verify-full` + `scram-sha-256` pour `pg_dump`/`pg_restore` ; Npgsql durci (D-14) |
| Privilèges | sauvegarde : lecture seule (écriture, `CREATE DATABASE` refusés — IT) ; migrateur sans `CREATEDB` ; applicatif ne peut ni sauvegarder le journal, ni créer de base, ni supprimer de table, ni se connecter à une base restaurée |
| Bases restaurées | fermées à `PUBLIC` ; vérification toujours supprimée ; jamais d'écrasement ; suppression des résiduelles par marqueur seulement |
| Suppression | `prune-backups` seul ; manifeste d'abord ; inconnu jamais supprimé |
| Fichiers | 0600 sous Linux ; Windows : ACL du dossier (procédure) ; volume BitLocker (R-6) |
| NuGet | aucune vulnérabilité (11 projets, CI) ; aucun paquet ajouté |

## 5. Schéma et migrations

Aucune modification du modèle EF ; aucune migration ni instantané modifié ; `has-pending-model-changes` vert sur
les chaînes SQLite et PostgreSQL (local et CI) ; la chaîne PostgreSQL reste `InitialPostgreSqlBaseline` seule.
Aucune base de production touchée, aucun `database update`.

## 6. Limites et questions ouvertes

- **Q-P4-9-1** : preuve Windows native (Planificateur de tâches, ACL NTFS, BitLocker) — la CI prouve Linux ; le poste
  de développement Windows a exécuté la suite avec `pg_dump`/`pg_restore` 17.4 Windows contre PostgreSQL 17.10
  conteneurisé ; recette native : laboratoire Lot C / P4-11.
- **Q-P4-9-2** : alerte active sur échec d'une sauvegarde planifiée (aujourd'hui : code, trace, historique).
- `configure-backup` / connexion enregistrée : DPAPI réel non exercé en CI Linux (double réversible en IT, comme
  P4-8) ; le chemin DPAPI est celui de `configure-workstation`, déjà prouvé sous Windows.

## 7. Effet sur la trajectoire

Q-24 est réalisée : **P4-6C est débloqué** (P4-6B et P4-9 `COMPLETE — CLOSED`). V1 multi-poste reste **NOT GO**.
