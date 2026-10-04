# Procédure opérateur — installation de la base centrale MMV (P4-8)

> Décisions : [ADR-PROD-DB-010](../architecture/adr-prod-db-010-provisioning-and-connection-security.md),
> [ADR-PROD-DB-009](../architecture/ADR-PROD-DB-009.md). Outil : `MMV.DatabaseManager` (archive de la release).
> **Aucun secret n'est jamais passé en argument** : l'outil le demande (saisie masquée) ou le lit sur l'entrée
> standard (une ligne par secret, dans l'ordre indiqué).

## 0. Prérequis serveur

1. PostgreSQL installé ; `ssl = on` avec un certificat serveur dont le **nom DNS** est celui que les postes
   utiliseront (VerifyFull vérifie le nom). Conserver l'autorité racine (`ca.crt`) pour les postes.
2. `password_encryption = scram-sha-256`.
3. `pg_hba.conf` : toute règle réseau pouvant viser la base ou les rôles MMV est **`hostssl … scram-sha-256`**.
   Aucune règle `host`/`hostnossl`, ni `trust`/`password`/`md5`/`ident` pour eux (le provisioning les refuse).
4. Volume de données chiffré (BitLocker ou équivalent) — exigence d'infrastructure (Q-P4-8-2).

## 1. Provisioning (administrateur, superutilisateur)

```
MMV.DatabaseManager provision --host <srv.dns> [--port 5432] [--root-certificate C:\…\ca.crt]
  --admin-user <superutilisateur> [--admin-database postgres]
  --database <base> --migrator-role <r1> --app-role <r2> --backup-role <r3> --operator <référence>
```
Secrets demandés : administrateur, migrateur, applicatif, sauvegarde (24–128 caractères ASCII imprimables, sans
espace, tous différents). Les noms sont **choisis par l'opérateur**. Code 0 = succès ; 16 = serveur/état non
conforme, constaté au préflight (**rien n'est écrit**) ; 18 = interruption pendant les écritures ou preuve de
connexion finale en échec (écritures possibles, relance idempotente) ; 20 = serveur injoignable.
Aucun rôle ne doit avoir de membre (pas même l'administrateur) : une appartenance existante est signalée avec ses
options `INHERIT`/`SET`/`ADMIN` et doit être retirée par l'administrateur — l'outil n'en retire aucune. Le
préflight laisse dans le journal du serveur une authentification échouée par rôle (preuve d'admission `pg_hba`).

## 2. Schéma (opérateur, rôle migrateur)

`MMV_MIGRATOR_CONNECTION_STRING` = chaîne du **migrateur** (`SSL Mode` absent ou `VerifyFull`,
`Root Certificate=…`), puis `MMV.DatabaseManager migrate --operator … --app-role <r2> --backup-ref …`.
*Jusqu'à P4-9, `migrate` refuse toute base réelle (sauvegarde vérifiée exigée, Q-24).*

## 3. Premier administrateur (une seule fois)

`MMV.DatabaseManager bootstrap-admin --username <login> --operator <référence>` (même variable migrateur).
Secrets : mot de passe initial, confirmation. Code 17 = déjà initialisée ou schéma non à jour.
**À la première connexion, l'administrateur remplace ce mot de passe** (Mon profil → Changer le mot de passe).

## 4. Postes (sur chaque poste, sous l'utilisateur Windows qui lancera MMV)

```
MMV.DatabaseManager configure-workstation --host <srv.dns> [--port 5432] --database <base> --username <r2>
  [--root-certificate C:\…\ca.crt]
```
Secret : celui du rôle applicatif. La connexion est vérifiée **avant** écriture ; le migrateur ou l'administrateur
sont refusés (code 16). Le fichier protégé DPAPI est `%LOCALAPPDATA%\ManageMyVision\database-connection.json`.
Ne pas définir `MMV_DATABASE_PROVIDER` sur un poste ainsi configuré (refus pour configuration concurrente).

## 5. Rotation d'un secret

`MMV.DatabaseManager rotate-role-password --host … --admin-user … --database <base> --role <r> --operator …`
(secrets : administrateur, nouveau secret), puis `configure-workstation` sur chaque poste et **redémarrage** des
postes : les sessions déjà ouvertes ne sont pas réauthentifiées par PostgreSQL.

## 6. Base injoignable

Le poste s'arrête au démarrage avec une cause explicite (serveur injoignable, certificat refusé, TLS absent,
authentification refusée, méthode non SCRAM, base absente, identité privilégiée). Il ne réessaie pas.
