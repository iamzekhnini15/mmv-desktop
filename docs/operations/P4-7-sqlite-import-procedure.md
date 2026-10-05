# Procédure opérateur — reprise d'une base SQLite dans la base centrale PostgreSQL (P4-7)

> Outil : `MMV.DatabaseManager import-sqlite`. Règles : O11 (ADR-PROD-DB-002), ADR-PROD-DB-003 §5.1 (montants),
> ADR-PROD-DB-004 décision 8 (instants), ADR-PROD-DB-005 §5.8 (historique EF), DP-10 / B-4 / R-9
> ([ADR-PROD-DB-011](../architecture/adr-prod-db-011-backup-and-restore.md)). Tous les chemins sont **absolus**.
> Connexion : rôle **migrateur**, variable `MMV_MIGRATOR_CONNECTION_STRING` (TLS VerifyFull + SCRAM, D-14) — jamais
> un argument. Codes de sortie : 0 succès · 10 arguments · 11 sauvegarde non vérifiée ou plus actuelle · 12 verrou
> occupé (migration ou import en cours) · 14 relecture après validation divergente · 20 serveur injoignable ·
> **24** source refusée · **25** cible refusée · **26** import annulé.

## 0. Quand et dans quel ordre

L'import se fait **une seule fois**, sur une base centrale **neuve** : provisionnée (P4-8), migrée, **sans** premier
administrateur et **sans** aucun poste configuré. Les utilisateurs viennent de la base SQLite ; `bootstrap-admin`
devient inutile (et refusé, code 17, après l'import).

```
provision → backup → verify-backup → migrate → backup → verify-backup → import-sqlite --dry-run
          → import-sqlite → backup → verify-backup → configure-workstation (chaque poste)
```

La migration **consomme** sa sauvegarde (B-4) : la sauvegarde exigée par l'import est celle de la base **migrée et
vide**. C'est aussi le **point de retour** de l'import (§5).

## 1. Copie à froid de la base SQLite

1. Sur le poste d'origine, **fermer MMV** (aucune fenêtre ouverte, aucun processus `MMV.App`).
2. Copier `%LOCALAPPDATA%\ManageMyVision\mmv.db` (ou le chemin désigné par `MMV_DATABASE_PATH`) vers le serveur, par
   exemple `E:\MMV\Import\mmv.db`. **Ne pas** copier une base ouverte : si un fichier `mmv.db-journal` ou
   `mmv.db-wal` non vide l'accompagne, l'outil refuse (code 24) — rouvrir puis refermer MMV sur le poste d'origine
   et recopier.
3. La base doit porter les **14 migrations SQLite** de la version courante. Sinon (code 24, « historique SQLite
   différent ») : ouvrir une fois la base avec la version courante de MMV sur le poste d'origine, la refermer, recopier.
4. La copie n'est **jamais modifiée** par l'outil (ouverture en lecture seule) ; son SHA-256 figure au rapport.

## 2. Fuseau du magasin d'origine (obligatoire)

Les dates-heures SQLite n'ont pas de fuseau. L'outil les interprète comme **heure locale du magasin** puis les
convertit en UTC (ADR-004 décision 8) — **il n'y a pas de valeur par défaut** : `--source-time-zone` est
obligatoire. Pour un magasin au Maroc : `Africa/Casablanca` ; en France : `Europe/Paris`.

- heure **inexistante** (saut d'horloge) : refusée, code 24, valeur localisée au rapport ;
- heure **ambiguë** (retour d'horloge) : décalage **standard** retenu, chaque valeur listée au rapport
  (`AmbiguousInstants`) ;
- précision : la microseconde (les 100 ns de SQLite sont tronquées, comptées dans `SubMicrosecondTruncatedInstants`).

## 3. Sauvegarde vérifiée de la cible

Comme avant une migration (procédure [P4-9](P4-9-backup-restore-procedure.md) §2, étapes 2 et 3), **après** `migrate` :

```
MMV.DatabaseManager backup --host … --database … --username <rôle de sauvegarde> --output-directory E:\MMV\Backups --operator <vous> --pg-bin …
MMV.DatabaseManager verify-backup --host … --admin-user <superutilisateur> --manifest <…manifest.json> --operator <vous> --pg-bin …
```

## 4. Import

1. **Essai à blanc** (recommandé, obligatoire avant le premier déploiement réel — RR4) :
   ```
   MMV.DatabaseManager import-sqlite --source E:\MMV\Import\mmv.db --source-time-zone Africa/Casablanca --operator <vous> --backup-ref <…manifest.json> --report E:\MMV\Import\essai.json --dry-run
   ```
   Tout est importé et vérifié **dans une transaction annulée** ; la cible est relue identique. Lire le rapport :
   `RejectedValues` (doit être vide), `NumericColumns` (écarts d'arrondi), `AmbiguousInstants`, `CivilDates`.
   L'essai à blanc **ne consomme pas** la sauvegarde.
2. **Import réel**, dans les **2 heures** qui suivent la sauvegarde (R-10), avec un **nouveau** fichier de rapport :
   ```
   MMV.DatabaseManager import-sqlite --source E:\MMV\Import\mmv.db --source-time-zone Africa/Casablanca --operator <vous> --backup-ref <…manifest.json> --report E:\MMV\Import\import.json
   ```
3. Code 0 : conserver `import.json` avec le manifeste de la sauvegarde (preuve de reprise : comptes par table,
   réconciliation des montants, règles appliquées). Puis **nouvelle sauvegarde vérifiée** (§3) avant de configurer
   les postes.

Les montants `REAL` de SQLite étaient **déjà altérés à la source** : ils sont arrondis à deux décimales au pair
(`ToEven`, comme `Money`), et la réconciliation par colonne figure au rapport. Le résultat **n'est pas** présenté
comme une restitution fidèle de valeurs qui ne l'étaient plus (ADR-003 §5.1).

## 5. Retour arrière

- **Pendant l'import** : tout refus ou incident annule l'**unique** transaction ; la cible est **relue** et le
  rapport l'atteste (`TargetUnchangedVerified`). Rien n'est partiellement importé. Une coupure réseau ou un arrêt
  du serveur annulent aussi la transaction (côté serveur) ; relancer simplement l'import.
- **Après validation** (import réussi mais à refaire, par exemple mauvais fuseau ou mauvaise copie) : **jamais**
  d'effacement en place. Restaurer la sauvegarde citée par le rapport dans une base **neuve** :
  `MMV.DatabaseManager restore-backup … --manifest <manifeste d'avant import> --target-database <nouvelle base> --migrator-role <migrateur> …`
  puis `provision` sur cette base (procédure P4-9 §5), et recommencer au §3.

## 6. Incidents

| Code | Signification | Action |
|---|---|---|
| 24 | Source refusée (copie non fermée, intégrité, orphelins, historique, table/colonne inconnue porteuse de données, valeur non importable) — **aucune écriture** | lire le message et `RejectedValues` ; corriger la source sur le poste d'origine (avec MMV) ou recopier |
| 25 | Cible refusée : non migrée, métadonnée absente, **déjà des données** (import déjà fait, administrateur créé, poste ayant écrit) — **aucune écriture** | repartir d'une base neuve (provision + migrate) |
| 11 | Sauvegarde absente, non vérifiée, d'une autre base, trop ancienne, ou la base a changé depuis | refaire §3 |
| 12 | Une migration ou un autre import tient le verrou | attendre, relancer |
| 26 | Import annulé (contrainte serveur, ligne manquante ou altérée à la relecture, coupure) ; rapport : `TargetUnchangedVerified` | si `true` : corriger la cause, relancer ; si `false` ou absent : §5 après validation |
| 14 | Validé mais relecture divergente | §5 (restauration de la sauvegarde d'avant import) |
| 20 | Serveur injoignable | vérifier réseau, TLS, identifiant ; la source n'est pas modifiée |
