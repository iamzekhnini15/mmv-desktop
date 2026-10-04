# ADR-PROD-DB-010 — Provisioning, secrets et sécurité de connexion de la base centrale

> **Statut : ACCEPTÉ — 4 octobre 2026, arbitrage de l'architecte principal (directive « Pilotage accéléré du
> programme P4 »).** Cet ADR enregistre les décisions **D-09, D-12, D-13, D-14, D-15** ouvertes par l'audit
> [P4-5E-A](../implementation/P4-5E-A-postgresql-migration-architecture-audit-report.md) (§Installation,
> §Configuration) et **les noms de rôles** laissés à P4-8 par [ADR-PROD-DB-009](ADR-PROD-DB-009.md) DP-5.
> Implémentation : lot **P4-8** ([rapport](../implementation/P4-8-provisioning-and-connection-security-report.md),
> [procédure opérateur](../operations/P4-8-provisioning-procedure.md)).
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**Accepted.** Les décisions ont été fournies par l'architecte ; aucune n'a été inventée par le lot P4-8. Les
points que le dépôt ne permettait pas de trancher sont listés au §6 comme **questions ouvertes**.

## 2. Contexte

- La configuration serveur était **100 % variables d'environnement** (audit P4-0 §19/§20) ; aucun artefact
  d'installation, aucun TLS exigé (R-E14), aucune procédure de création de la base ni des rôles (R-E13).
- Une base PostgreSQL neuve **n'a aucun administrateur applicatif** : la baseline n'insère aucun utilisateur
  (R-E12). Le bootstrap historique (`MMV_BOOTSTRAP_ADMIN_PASSWORD`, P2A-1F) est un mécanisme **SQLite / poste**.
- ADR-PROD-DB-009 a arrêté **RL-3** (administrateur d'installation, migrateur, applicatif) et **MA-B2**
  (`MMV.DatabaseManager` seul migrateur), mais a laissé à P4-8 les **noms** et le **provisioning**.

## 3. Décisions

### D-09 — Création de la base : provisioning explicite

1. La base de production est créée **exclusivement** par une procédure de provisioning **exécutée explicitement
   par un administrateur** : verbe `MMV.DatabaseManager provision`, avec l'identifiant **superutilisateur** de
   l'installation.
2. `MMV.App` ne crée **jamais** la base ; `MMV.DatabaseManager migrate` ne la crée **jamais** implicitement.
3. Le provisioning établit la base (propriété du migrateur), les trois rôles, les droits et privilèges par défaut,
   et l'historique EF **vide** ; il **n'applique aucune migration** (la baseline reste à `migrate`, DP-1).
4. Il est **idempotent** (relance = convergence, rien n'est détruit) et **refuse avant toute écriture** un
   serveur ou un état non conformes.

### Rôles PostgreSQL — noms paramètres (DP-5, complété)

1. Les noms du **rôle migrateur**, du **rôle applicatif** et du **rôle de sauvegarde** sont des **paramètres** du
   provisioning (`--migrator-role`, `--app-role`, `--backup-role`). **Aucun nom de rôle n'est écrit** dans
   `MMV.App`, `MMV.DatabaseManager`, `MMV.Infrastructure`, les migrations ni aucune configuration embarquée.
2. **Amendement de DP-5** : RL-3 reçoit un **rôle de sauvegarde** en lecture seule, distinct de l'administrateur.
   L'administrateur d'installation reste seul détenteur de la **restauration** et du provisioning. Le rôle de
   sauvegarde lit toutes les tables, séquences et schémas du migrateur, et n'écrit rien. Motif : moindre privilège
   pour `pg_dump` (P4-9), sans exposer le superutilisateur à une tâche planifiée.
3. Attributs imposés aux trois rôles : `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION
   NOBYPASSRLS`, membres d'**aucun** rôle. Un rôle existant privilégié ou membre est **refusé**, jamais « corrigé ».
4. Droits : applicatif = `CONNECT`, `USAGE` sur `public`, DML sur les tables de `public` par privilèges par défaut,
   **lecture seule** de `__EFMigrationsHistory`, aucun DDL, aucun accès au journal (DP-8). `PUBLIC` perd tout droit
   sur la base et sur `public`. `search_path = public` pour l'applicatif et le migrateur (P4-5E-B D-08) ; un schéma
   au nom d'un rôle MMV est refusé.

### D-12 — Premier administrateur : par la procédure d'installation

1. Créé par `MMV.DatabaseManager bootstrap-admin`, sous le **rôle migrateur**, **une seule fois** (refus dès qu'un
   compte existe, sous `LOCK TABLE "Users" IN EXCLUSIVE MODE`).
2. Le secret initial est lu sur l'entrée standard, **jamais** en argument, variable, fichier ou journal ; seul son
   hachage BCrypt (service d'authentification du produit) est écrit.
3. `MMV.App` **n'est pas** le mécanisme de bootstrap en production multi-poste. Le seed P2A-1F reste propre au
   cycle de vie SQLite.
4. Le secret initial **doit être remplacé** à la première connexion — voir **Q-P4-8-1** (contrainte technique).

### D-13 — Configuration de connexion : fichier par poste, DPAPI CurrentUser

1. Stockage de production : `%LOCALAPPDATA%\ManageMyVision\database-connection.json`, **hors du dossier
   d'installation** (ADR-APP-DISTRIBUTION-001 ND-10, DI-9).
2. **Tout** le contenu (hôte, port, base, rôle, secret, autorité racine) est un blob **DPAPI CurrentUser** ;
   l'enveloppe ne porte que le format et le schéma de protection. Fichier corrompu, altéré, tronqué ou d'un autre
   utilisateur ⇒ **refus explicite**, aucun repli sur SQLite.
3. Écrit par `MMV.DatabaseManager configure-workstation`, lancé **par l'utilisateur Windows du poste**, **après**
   vérification de la connexion et du caractère **non privilégié** de l'identité.
4. Les variables d'environnement restent admises pour développement, CI et tests contrôlés ; elles sont **durcies**
   par la même politique (D-14). Variable de fournisseur **et** fichier présents ⇒ **refus** (pas d'arbitrage).

### D-14 — Sécurité des connexions

1. **TLS obligatoire, `VerifyFull`** (chaîne et nom d'hôte) pour **toute** connexion de production — postes et outil.
2. **SCRAM-SHA-256 seul**, imposé côté client (`Require Auth=ScramSHA256`) ; MD5 et mot de passe en clair refusés.
3. **Aucun repli** : un réglage explicitement plus faible est refusé ; seuls les défauts Npgsql sont relevés.
4. Le provisioning **audite** le serveur : `ssl = on`, `password_encryption = scram-sha-256`, et aucune règle
   `pg_hba` réseau pouvant admettre un rôle MMV hors `hostssl` ou par `trust`/`password`/`md5`/`ident`.
5. Les secrets de rôle sont envoyés au serveur sous forme de **vérificateur SCRAM calculé par l'outil**.
6. **Rotation** : `rotate-role-password` (identifiant administrateur) puis `configure-workstation` sur chaque poste.
7. **Chiffrement du stockage serveur** : exigence d'**infrastructure** (BitLocker/volume chiffré), hors code — voir
   **Q-P4-8-2**.

### D-15 — Base injoignable : arrêt explicite

1. Au démarrage, `MMV.App` vérifie la base **une fois** (`PostgreSqlConnectivityProbe`), avant tout le reste.
2. Échec ⇒ `DatabaseUnavailableException` avec cause classée et message sans secret ; **aucune** opération métier,
   **aucune** nouvelle tentative. Retry, reconnexion et résilience appartiennent à **P4-10**.
3. L'**affichage** de ce message dans un écran de blocage appartient à **P4-6C** (avec E2, E3b, E5) ; d'ici là,
   le garde-fou P4-3 reste en place.

## 4. Conséquences

- Trois secrets de service à distribuer : migrateur (opérateur, jamais sur un poste), applicatif (postes, DPAPI),
  sauvegarde (serveur, P4-9).
- Les installations existantes de laboratoire (`host … scram-sha-256` sans TLS) sont **refusées** par le
  provisioning : la procédure opérateur impose `hostssl`.
- Une base provisionnée mais non migrée est lue par la garde P4-6B comme **E2** (« la base doit être mise à jour
  par l'opérateur ») et non E4 : l'historique EF existe, vide. Les deux états bloquent.
- Changer le secret d'un rôle ne coupe pas les sessions déjà ouvertes (PostgreSQL ne réauthentifie pas) :
  redémarrer les postes après rotation.

## 5. Alternatives écartées

- **Création de la base par `Migrate()`** (D-09 b) : exige `CREATEDB` au migrateur, contraire à RL-3.
- **Secret en clair / `pgpass`** (D-13) : interdit par la directive.
- **Arbitrage silencieux env ↔ fichier** : un poste croirait être sur un serveur et serait sur un autre.
- **Script SQL `psql` de provisioning** : identifiants à concaténer côté client, non testable comme le code ; le
  provisioning C# fait citer chaque identifiant par le serveur (`format('%I')`).

## 6. Questions ouvertes

| # | Question | Pourquoi elle bloque quoi |
|---|---|---|
| **Q-P4-8-1** | **Remplacement forcé du secret initial** à la première connexion | Exige un état persistant (« doit changer de mot de passe ») : **changement du modèle EF** (colonne + migrations SQLite et PostgreSQL), que P4-8 n'a pas le droit de générer sans revue. Aujourd'hui : consigne + message de l'outil. **Recommandation** : colonne `MustChangePassword` (étendre → migrer → contracter), contrôle à la connexion. |
| **Q-P4-8-2** | **Chiffrement du stockage serveur** | Fait d'infrastructure, non vérifiable depuis le dépôt ; à attester par la recette P4-11. |
| **Q-P4-8-3** | **Preuve Windows native** de la procédure (O12) | La CI prouve PostgreSQL Linux en conteneur ; le laboratoire Lot C reste l'environnement de preuve Windows. |
