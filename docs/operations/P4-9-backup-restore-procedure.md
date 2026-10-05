# Procédure opérateur — sauvegarde et restauration de la base centrale MMV (P4-9)

> Décisions : [ADR-PROD-DB-011](../architecture/adr-prod-db-011-backup-and-restore.md). Tous les chemins sont
> **absolus**. Aucun secret n'est jamais un argument : ils sont lus sur l'entrée standard (saisie masquée en
> console). Codes de sortie : 0 succès · 10 arguments · 11 sauvegarde non vérifiée · 12 verrou occupé · 16 refus de
> sécurité · 20 serveur injoignable · **21** sauvegarde en échec · **22** vérification/restauration en échec ·
> **23** rétention en échec.

## 0. Prérequis serveur (une fois)

1. `pg_dump` / `pg_restore` de la **version majeure du serveur** (installés avec PostgreSQL :
   `C:\Program Files\PostgreSQL\<version>\bin`), passés par `--pg-bin`.
2. Volume **dédié** aux sauvegardes, distinct du répertoire de données PostgreSQL, chiffré **BitLocker** (R-4, R-6).
   Dossier, par exemple `E:\MMV\Backups`, avec ACL NTFS limitées à `SYSTEM`, `Administrators` et au compte de la
   tâche (héritage désactivé) :
   ```
   icacls E:\MMV\Backups /inheritance:r /grant:r SYSTEM:(OI)(CI)F Administrators:(OI)(CI)F "<SERVEUR>\mmv-backup":(OI)(CI)M
   ```
3. Compte Windows **dédié** à la tâche (ex. `mmv-backup`), non administrateur.
4. Espace libre suffisant sur le serveur PostgreSQL pour une **base de vérification temporaire** (taille de la base).

## 1. Tâche de sauvegarde quotidienne (R-1, R-7, R-8)

Sous le compte `mmv-backup`, une seule fois (et après chaque rotation du secret du rôle de sauvegarde) :

```
MMV.DatabaseManager configure-backup --host <serveur> --root-certificate <ca.crt> --database <base> --username <rôle de sauvegarde>
```

Le secret est vérifié (TLS, SCRAM, identité **non privilégiée** — le superutilisateur est refusé, code 16) puis
enregistré chiffré DPAPI pour ce seul compte. Planificateur de tâches, **chaque jour après la fermeture**, sous
`mmv-backup` (« exécuter même si l'utilisateur n'est pas connecté ») :

```
MMV.DatabaseManager backup --output-directory E:\MMV\Backups --operator TACHE-QUOTIDIENNE --pg-bin "C:\Program Files\PostgreSQL\17\bin"
MMV.DatabaseManager prune-backups --directory E:\MMV\Backups --operator TACHE-QUOTIDIENNE
```

`prune-backups` est le **seul** moyen de supprimer une sauvegarde (rétention R-3 : 14 jours, première de chacun des
12 derniers mois, vérifiées de moins de 12 mois, toujours la dernière vérifiée). **Ne jamais supprimer un fichier à
la main.** Dans l'historique de la tâche, un code ≠ 0 est un incident (trace :
`%LOCALAPPDATA%\ManageMyVision\DatabaseManager\migration-trace.log` du compte).

## 2. Avant chaque migration (R-1, R-2 ; ADR-009 §4.6.2)

1. Annoncer la maintenance, **fermer tous les postes**.
2. Sauvegarder (rôle de sauvegarde ; manifeste affiché en sortie) :
   `MMV.DatabaseManager backup --host … --database … --username <rôle de sauvegarde> --output-directory E:\MMV\Backups --operator <vous> --pg-bin …`
3. Vérifier par **restauration réelle** (identifiant **administrateur**, jamais enregistré) :
   `MMV.DatabaseManager verify-backup --host … --admin-user <superutilisateur> --manifest <…manifest.json> --operator <vous> --pg-bin …`
   → `…verification.json` écrit à côté du manifeste. Code 22 : la sauvegarde **n'est pas** un point de retour ;
   corriger et recommencer à l'étape 2.
4. Migrer **dans les 2 heures** (R-10), **sans qu'aucun poste n'ait écrit** :
   `MMV.DatabaseManager migrate --operator <vous> --app-role <rôle applicatif> --backup-ref <…manifest.json>`
   Code 11 « la base a changé » : un poste a écrit — recommencer à l'étape 2. Une migration réussie consomme la
   sauvegarde : la suivante en exige une nouvelle.

## 3. Exercice mensuel de restauration (R-2)

Une fois par mois, `verify-backup` sur la **dernière sauvegarde quotidienne**. Consigner date, opérateur,
code et nombre de lignes affiché.

## 4. Copie hors ligne hebdomadaire (R-5)

Chaque semaine, copier le trio `mmv-backup-…` (`.dump`, `.manifest.json`, `.verification.json`) de la **dernière
sauvegarde vérifiée** sur un support amovible **BitLocker To Go**, conservé **hors du magasin**. Aucun cloud.

## 5. Reprise après sinistre (R-9 ; ADR-009 §4.6.3)

1. Fermer les postes. Choisir la **dernière sauvegarde vérifiée** (ou recopier celle du support hors ligne dans
   `E:\MMV\Backups`, puis `verify-backup`).
2. Restaurer dans une base **neuve** (jamais d'écrasement ; la base d'origine reste intacte pour analyse) :
   `MMV.DatabaseManager restore-backup --host … --admin-user <superutilisateur> --manifest <…> --target-database <nouvelle base> --migrator-role <rôle migrateur> --operator <vous> --pg-bin …`
3. Rouvrir les droits de connexion : `MMV.DatabaseManager provision … --database <nouvelle base> …` (mêmes rôles —
   convergence idempotente).
4. Sauvegarde + `verify-backup` de la nouvelle base (elle n'a pas l'identité de l'ancienne : les anciennes
   sauvegardes ne permettent pas d'y migrer).
5. Sur chaque poste : `configure-workstation … --database <nouvelle base>` ; mettre à jour la tâche (`configure-backup`).
6. L'ancienne base n'est supprimée qu'après décision écrite (analyse terminée).

## 6. Incidents

| Code | Cause probable | Action |
|---|---|---|
| 21 | rôle sans droit de lecture, `pg_dump` absent ou d'une autre version, disque plein | vérifier `--pg-bin`, l'espace, le rôle (code 21 sous le rôle applicatif = attendu) ; aucun fichier n'est laissé |
| 22 | fichier altéré/tronqué, restauration impossible, contenu différent, identifiant non administrateur | **ne pas migrer** ; refaire une sauvegarde ; la preuve précédente est retirée |
| 12 | une autre vérification/restauration est en cours | attendre ; rien n'a été modifié |
| 11 | sauvegarde absente, non vérifiée, d'une autre base/installation, trop ancienne, ou base modifiée depuis | §2 depuis l'étape 2 |
