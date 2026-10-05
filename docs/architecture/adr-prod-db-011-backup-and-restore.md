# ADR-PROD-DB-011 — Sauvegarde et restauration centrales

> **Statut : ACCEPTÉ — 5 octobre 2026.** Les décisions **B-1 … B-6** découlent d'ADR acceptés (DP-5, DP-8, DP-10
> d'[ADR-PROD-DB-009](ADR-PROD-DB-009.md), [ADR-PROD-DB-010](adr-prod-db-010-provisioning-and-connection-security.md),
> Q-24) ; les décisions de politique **R-1 … R-10**, qu'aucun document ne fixait, ont été **soumises en une seule
> demande et acceptées telles que recommandées par l'architecte principal le 5 octobre 2026**.
> Implémentation : lot **P4-9** ([rapport](../implementation/P4-9-backup-and-restore-report.md),
> [procédure opérateur](../operations/P4-9-backup-restore-procedure.md)). **Le dépôt réel prime toujours sur ce
> document.**

---

## 1. Contexte

- DP-10 (ADR-009) : **aucune migration sans sauvegarde vérifiée** ; « une sauvegarde non vérifiée vaut absence de
  sauvegarde ». Ce qu'est une vérification suffisante appartenait à P4-9 (O9). Jusqu'ici,
  `RefusingBackupVerification` refusait toute migration sur une base réelle (Q-24).
- DP-5 / ADR-010 : quatre identités — **administrateur** (provisioning, **restauration**), **migrateur** (DDL, journal,
  métadonnée), **applicatif** (postes, DML), **sauvegarde** (lecture seule, `pg_dump`). Le migrateur ne reçoit pas
  `CREATEDB`.
- §4.6.2 / §4.6.3 d'ADR-009 : sauvegarde à l'étape 2 de la montée de version ; le seul retour arrière réaliste est la
  restauration (U-7, DR-11).
- La sauvegarde historique est une **copie de fichier SQLite** (audit §21) : rien n'existait côté serveur.

## 2. Décisions d'architecture (déduites des ADR acceptés)

### B-1 — Sauvegarde vérifiée = contrôle structurel + restauration réelle

Une sauvegarde est **vérifiée** si et seulement si :

1. son **manifeste** est conforme (format strict, versionné) ;
2. le fichier a exactement la **taille** et l'empreinte **SHA-256** du manifeste, et `pg_restore --list` le lit ;
3. il a été **réellement restauré** (`pg_restore --single-transaction --exit-on-error`, propriétaires et droits
   compris) dans une base **neuve et isolée**, et le contenu restauré — historique EF, ensemble des tables, nombre
   exact de lignes de chaque table — est **identique** au manifeste ;
4. une **preuve** l'atteste, liée aux octets exacts du manifeste et du fichier (deux SHA-256).

L'existence d'un fichier ou un `pg_restore --list` réussi **ne suffisent pas**.

### B-2 — Cohérence sauvegarde ⇔ manifeste par instantané exporté

`backup` ouvre une transaction `REPEATABLE READ READ ONLY`, exporte son instantané (`pg_export_snapshot()`), y lit
l'empreinte de la base (identifiant de cluster `pg_control_system()`, nom et OID de la base, `now()` du serveur,
historique EF, nombre de lignes par table), puis lance `pg_dump --snapshot` sur **le même** instantané. Les postes
peuvent écrire pendant la sauvegarde sans désaccorder manifeste et fichier.

### B-3 — Séparation des rôles (DP-5 inchangé)

| Identité | P4-9 |
|---|---|
| **Sauvegarde** | `backup` : lecture seule ; ne crée, n'écrit et ne restaure rien |
| **Administrateur** | `verify-backup`, `restore-backup` : crée et supprime les bases de restauration |
| **Migrateur** | **vérifie la preuve** (`migrate`) ; ne restaure rien ; **jamais** `CREATEDB` |
| **Applicatif** | **aucune** capacité de sauvegarde ni de restauration ; ne peut se connecter à aucune base restaurée |

### B-4 — Rapprochement sous verrou (`migrate`, `adopt-compatibility`)

1. **Avant** le verrou, sans serveur : manifeste, SHA-256, preuve (un refus n'immobilise jamais la base).
2. **Sous** le verrou, premier contrôle, en lecture seule : **même installation** (identifiant de cluster), **même
   base** (nom **et** OID — une base recréée sous le même nom n'est pas la base sauvegardée), **même historique
   EF**, **mêmes nombres de lignes** (aucune écriture depuis la sauvegarde), âge serveur ≤ R-10. Tout écart ⇒
   code 11, **aucune écriture**.
3. Le journal serveur (DP-8) porte l'**identifiant** de la sauvegarde vérifiée.

Conséquence assumée : une migration réussie « consomme » la sauvegarde — la suivante exige une nouvelle sauvegarde
vérifiée.

### B-5 — Isolation et reprise des bases de restauration

Base de vérification : neuve (`template0`), `REVOKE ALL … FROM PUBLIC`, marquée par commentaire
(`mmv-restore-check:<exécution>`), **toujours** supprimée. Les vérifications et restaurations sont sérialisées par
un verrou consultatif distinct de celui des migrations ; une base marquée laissée par une exécution interrompue est
supprimée par la suivante (reconnue au **marqueur**, jamais au nom seul). Une base que l'outil n'a pas créée n'est
**jamais** supprimée ni écrasée.

### B-6 — Outils clients

`pg_dump` / `pg_restore` de la **même version majeure** que le serveur ; lancés sans shell (arguments un par un) ;
variables `PG*` héritées retirées ; secret dans l'environnement du **seul** processus enfant ; `verify-full` +
`scram-sha-256` exigés (D-14) ; diagnostics bornés et expurgés.

## 3. Décisions de politique (acceptées le 5 octobre 2026)

| # | Sujet | Décision |
|---|---|---|
| **R-1** | Fréquence | sauvegarde **quotidienne** automatique (Planificateur de tâches Windows, après la fermeture du magasin) sous le **rôle de sauvegarde** ; plus une sauvegarde **obligatoire** à l'étape 2 de chaque montée de version |
| **R-2** | Vérification | `verify-backup` lancé par l'**opérateur administrateur** avant **chaque** migration, plus un **exercice mensuel** de restauration ; **jamais** planifié avec le secret superutilisateur |
| **R-3** | Rétention | toutes les sauvegardes des **14 derniers jours** ; la **première de chacun des 12 derniers mois** civils (UTC) ; toute sauvegarde **vérifiée** de moins de **12 mois** ; **toujours** la dernière sauvegarde vérifiée. Référence = la sauvegarde la plus récente (aucune horloge de poste) |
| **R-4** | Emplacement | volume **dédié** du serveur, **distinct** du répertoire de données PostgreSQL ; ACL NTFS : `SYSTEM`, `Administrators`, compte Windows de la tâche de sauvegarde |
| **R-5** | Copie secondaire | copie **hebdomadaire** de la dernière sauvegarde vérifiée sur un support **hors ligne** chiffré (BitLocker To Go), conservé hors du magasin ; **aucun cloud** (loi 09-08, transferts internationaux — CNDP) |
| **R-6** | Chiffrement | chiffrement de **volume** (BitLocker) du volume de sauvegarde et du support secondaire, cohérent avec D-14.7 / Q-P4-8-2 ; **aucun chiffrement applicatif** en V1 (une clé perdue rendrait les sauvegardes irrestaurables) |
| **R-7** | Suppression | **uniquement** par `prune-backups`, après chaque sauvegarde planifiée ; suppression manuelle interdite par la procédure |
| **R-8** | Accès | secret du rôle de sauvegarde enregistré par `configure-backup` (DPAPI CurrentUser du compte de la tâche, enveloppe D-13) ; secrets administrateur et migrateur **jamais** enregistrés |
| **R-9** | Restauration (sinistre) | `restore-backup` : sauvegarde **vérifiée** seulement, base **neuve** propriété du migrateur, fermée aux postes jusqu'à `provision` ; **jamais** d'écrasement en place |
| **R-10** | Audit / âge | trace locale de chaque verbe (sans secret) + identifiant de sauvegarde dans le journal serveur ; âge maximal d'une sauvegarde acceptée par `migrate` : **2 heures** (horloge serveur) |

Ces valeurs sont déclarées en **un seul point** (`Backup/BackupPolicy`) ; aucune option de ligne de commande ne les
relâche.

## 4. Conséquences

- **DP-10 est exécutable** : `migrate` accepte une base réelle dès qu'une sauvegarde vérifiée et actuelle existe.
  Q-24 est **réalisée** ; **P4-6C est débloqué** côté sauvegarde.
- Le serveur doit disposer de `pg_dump` / `pg_restore` de sa version majeure (installés avec PostgreSQL), et d'un
  espace disque suffisant pour une base de vérification temporaire.
- `verify-backup` restaure sur **le même** serveur (les rôles référencés par les droits doivent exister).
- Exit codes ajoutés : **21** sauvegarde en échec, **22** vérification/restauration en échec, **23** rétention en échec.

## 5. Alternatives écartées

- **`pg_restore --list` seul** comme vérification : ne prouve pas la restaurabilité (contenu, droits, contraintes).
- **`CREATEDB` au migrateur** pour qu'il vérifie lui-même : contraire à DP-5 / RL-3.
- **Restauration en place** (`--clean`) : destructive et irréversible au premier échec ; contraire à R-9.
- **Preuve en base** (table `mmv_meta`) : le schéma appartient au migrateur, qui pourrait la réécrire ; la preuve
  fichier liée par empreintes suffit au modèle de menace (erreur d'opérateur, fichier altéré ou tronqué).
- **Chiffrement applicatif** : rejeté pour V1 (R-6).
- **Vérification planifiée** : exigerait le secret superutilisateur dans une tâche (R-2, ADR-010 §DP-5 amendé).

## 6. Questions ouvertes

| # | Question | Effet |
|---|---|---|
| **Q-P4-9-1** | Preuve **Windows native** (Planificateur de tâches, ACL NTFS, BitLocker) | procédure prouvée en CI Linux + poste Windows local ; recette sur le laboratoire Lot C / P4-11 (O12) |
| **Q-P4-9-2** | Supervision de l'échec d'une sauvegarde planifiée (alerte) | aujourd'hui : code de sortie + trace + historique du Planificateur ; alerte active → P4-10/P8 |
