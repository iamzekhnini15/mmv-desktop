# P4-10 — Tests multi-processus et résilience : rapport de clôture

> Lot P4-10 de la [roadmap P4](../architecture/P4-multi-poste-roadmap.md) — obligation **O13**
> ([ADR-PROD-DB-002](../architecture/adr-prod-db-002-server-database-provider-selection.md)), critères de sortie
> **8** (multi-processus), **9** (perte réseau), **10** (reconnexion après redémarrage serveur). Cadre : D-15
> ([ADR-PROD-DB-010](../architecture/adr-prod-db-010-provisioning-and-connection-security.md) — « retry, reconnexion
> et résilience appartiennent à P4-10 »), ADR-PROD-DB-008 §5.11, Q-13 d'ADR-PROD-DB-009.
> **Aucune décision d'architecture nouvelle** ; trois défauts trouvés par les preuves, corrigés (§3).

## 1. Commits et CI (SHA exacts)

| Commit | Contenu | CI |
|---|---|---|
| `c2f6395` | `fix(P4-10)` : issue de validation inconnue, ouverture de transaction classée, unité de travail en échec abandonnée | voir `2c647bb` |
| `2c647bb` | `test(P4-10)` : processus poste, proxy de panne réseau, serveur de redémarrage, preuves sous charge, étape CI | **`37302429560`** verte sur le SHA exact — job Linux : serveur de redémarrage démarré, intégration **232/232** ; job Windows : unitaires **2457/2457**, audit ; 0 ignoré |
| `c1f4516` | `docs(P4-10, P4-11, P4-12)` : rapport, roadmap, checklist, préparation de l'audit | **`37303113348` ROUGE** : `Workstation_starting_while_the_server_is_down_…` — le test arrêtait le serveur puis, dans son `finally`, appelait le redémarrage qui envoyait un **second** SIGINT ; sur le runner le conteneur était déjà arrêté (`container is not running`). Défaut du test, pas du produit |
| `9c94f42` | `fix(P4-10)` : arrêt et démarrage séparés, jamais de second signal ; démarrage seulement après arrêt complet (3/3 en local) | CI du SHA final (roadmap §6) |

Base : `origin/p4-multi-poste` = `3745be6` (P4-7 clos). Branche `p4-10`.

## 2. Moyens de preuve

| Moyen | Ce qu'il apporte |
|---|---|
| `tests/MMV.MultiProcess.Worker` | **un poste = un processus OS** (son pool, sa connexion), qui exécute une opération par les implémentations de **production** (`EfTransactionRunner`, `EfStockMutationService`, `EfNumberSequenceService`, dépôts) puis écrit une ligne `RESULT` |
| barrière serveur (`Workstations`) | départ **simultané** : le test tient un verrou consultatif exclusif, chaque poste l'attend en partagé, libération quand **tous** attendent (`pg_locks`) ; seconde barrière au milieu d'une transaction pour forcer un interblocage |
| `TcpFaultProxy` | **perte réseau réelle** entre poste et serveur : sockets coupées, connexions refusées, ou coupure armée **juste après** que `COMMIT\0` a atteint le serveur (réponse jamais relayée) |
| `start-restart-server.sh` + `ServerRestartTests` | **redémarrage réel** d'un serveur dédié (arrêt rapide SIGINT du postmaster puis démarrage, comme un service Windows), étape CI dédiée |
| relectures indépendantes | chaque état final est relu par une connexion directe, hors proxy, jamais déduit du seul résultat de l'opération |

Aucune nouvelle tentative automatique n'existe (O13) : la stratégie d'exécution configurée ne réessaie pas
(`RetriesOnFailure = false`, testé) et les tests de panne vérifient qu'**une seule** connexion a été ouverte.
« Retry autorisé lorsqu'il est prévu » : **aucun** retry n'est prévu par les ADR — sans objet.

## 3. Défauts trouvés et corrigés (`c2f6395`)

| # | Défaut (preuve qui l'a fait apparaître) | Correction |
|---|---|---|
| **D1** | **Validation perdue présentée comme « aucune modification conservée ».** Coupure armée après `COMMIT\0` : la ligne **est** validée, l'utilisateur lit « La base de données est inaccessible. Aucune modification n'a été conservée. » — et ressaisirait (double vente). | catégorie `CommitOutcomeUnknown` ; `PersistenceErrorMapper.MapCommitFailure` : réponse perdue (transport Npgsql, `08xxx`, `57Pxx`) ⇒ « il a pu être conservé ou non. Vérifiez avant de le saisir à nouveau. » ; refus structuré du serveur ⇒ classification ordinaire (échec certain). Toujours **aucune** nouvelle tentative. |
| **D2** | **Serveur injoignable à l'ouverture de la transaction** : `BeginTransactionAsync` était hors de la zone classée ; l'exception brute Npgsql (message citant hôte:port) atteignait l'écran. | ouverture classée comme le reste (`ConnectionFailure`, message assaini). |
| **D3** | **Unité de travail en échec rejouée plus tard.** L'application compose **un seul** `OpticDbContext` pour toute la session (fournisseur racine, `App.axaml.cs`). Après une annulation, les entités restaient suivies : une entité `Added` était **réinsérée par le `SaveChanges` suivant, sans rapport** (écriture silencieuse après « aucune modification conservée ») ; une entité acceptée restait comme **fantôme** d'une ligne annulée. Le dépôt connaissait le risque : `CreateUserUseCase` détachait déjà son entité rejetée. | `EfTransactionRunner` vide le suivi après annulation ; `OpticDbContext` fait de même pour un `SaveChanges` autonome refusé. Généralisation du précédent, sans changement de modèle. |

Tests vus **en échec avant** correction : `Lost_commit_acknowledgement_…` (message), `Server_unreachable_…`
(exception brute), `After_the_network_comes_back_…` (« Pendant la panne » réinséré), `FailedUnitOfWorkTests` (2 cas).

## 4. Exigences ↔ preuves

### Scénarios de la roadmap §P4-10 (processus distincts sauf mention)

| Scénario | Test | Résultat prouvé |
|---|---|---|
| deux postes vendant le dernier produit | `Four_workstations_selling_the_last_product_…` | 1 vente, 3 refus, stock 0, numéro `SALE` = 1 |
| avançant la même commande | `Four_workstations_advancing_the_same_order_…` | 1 transition, 3 perdues |
| générant la fiche courante | `Four_workstations_generating_the_current_workshop_sheet_…` | 1 version 2, 3 conflits, versions (1, non courante) (2, courante) |
| validant le QC | `Two_workstations_deciding_the_qc_…` | 1 décision définitive, celle du gagnant |
| réglant le même solde | `Four_workstations_settling_the_same_balance_…` | 1 règlement, reste 0 |
| créant la même alerte LowStock | `Four_workstations_raising_the_same_low_stock_alert_…` | 1 alerte active |
| créant le même login normalisé | `Four_workstations_creating_the_same_normalized_login_…` | 1 utilisateur, 3 `UniqueConstraint` |
| suppression fournisseur vs création produit | `Supplier_deletion_racing_a_product_creation_…` (6 manches) | jamais d'orphelin |
| migration pendant qu'un poste travaille | `Additive_migration_applied_while_workstations_sell_…` | migration additive sur `Products` appliquée **pendant** les ventes (chevauchement prouvé), aucune vente perdue ni dupliquée |
| perte de connexion en transaction | `Network_loss_inside_a_transaction_…`, `Lost_commit_acknowledgement_…` | rien conservé avant `COMMIT` ; issue inconnue annoncée après |
| reconnexion après redémarrage | `Server_restart_aborts_the_in_flight_transaction_…` | transaction en vol annulée ; **même** contexte repris ; mesure : **0** échec avant reprise |
| timeout | `Statement_blocked_beyond_the_command_timeout_…` | `DatabaseBusy`, transaction entière annulée |
| deadlock | `Crossed_writes_from_two_workstations_deadlock_…` | 40P01 ⇒ `Concurrency`, écritures du perdant toutes annulées |
| retry | `No_automatic_retry_strategy_…` + « une seule connexion » dans les tests de panne | aucun retry (O13) |
| sauvegarde puis restauration | `Backup_taken_while_workstations_sell_restores_exactly_its_manifest` | sauvegarde prise **pendant** les ventes, restauration réelle = manifeste (B-2) |

### Situations de panne demandées

| Situation | Preuve |
|---|---|
| PostgreSQL indisponible / serveur arrêté | `Server_unreachable_…`, `Workstation_starting_while_the_server_is_down_…` (sonde D-15 refuse sans secret, puis accepte) ; écran de blocage : P4-6C |
| connexion interrompue, perte réseau | `NetworkFaultTests` (proxy) |
| erreur pendant opération / transaction | `FailedUnitOfWorkTests`, P04 (N6), `Network_loss_inside_a_transaction_…` |
| verrou | timeout (verrou de ligne) ; verrou de migration P4-6B ; verrou d'import P4-7 |
| concurrence | `MultiProcessTests` (10) |
| reprise | réseau rétabli, redémarrage serveur, poste tué (`Workstation_killed_in_the_middle_of_a_sale_…`), import interrompu puis relancé (P4-7) |
| redémarrage du poste | poste tué en pleine vente : rien conservé, verrous libérés, vente suivante normale ; démarrage après panne : sonde D-15 |
| corruption / incohérence détectée | vérification par restauration (P4-9), relecture ligne à ligne de l'import (P4-7) |
| sauvegarde indisponible | `migrate`/`import-sqlite` refusés (code 11) : P4-9, P4-7 |
| migration impossible, état incompatible | P4-6B (code 13, `lock_timeout`), P4-6C (garde de compatibilité, états de blocage) |
| journaux sans secret | `AssertNoConnectionDetail` (hôte, port, base, utilisateur, mot de passe absents des messages), message de blocage sans secret |

### Tests vus en échec (mutation)

Décrément de stock rendu **inconditionnel** ⇒ `Four_workstations_selling_the_last_product_…` échoue (quatre ventes
`VTE-000001` … `VTE-000004` du même dernier article). Une exécution complète accidentelle avec ce binaire muté
(horodatage restauré, compilation incrémentale) a aussi fait échouer **de nombreux tests existants**, unitaires et
serveur — dont `P01` et `P04` (N6) et les tests de vente et de stock d'Application : la garantie est verrouillée en
plusieurs points. Constat de méthode : restaurer un fichier par copie conserve son horodatage ; reconstruire sans
incrémental après toute mutation.

## 5. Constats sans correction

- **Les ventes se sérialisent sur la ligne `SALE` de `DocumentSequences`** (verrou de ligne pris en premier) : deux
  ventes ne peuvent pas s'interbloquer ; l'interblocage reste possible pour des transactions multi-lignes sans
  numérotation, et il est alors classé et annulé proprement.
- **Reprise après redémarrage** : mesurée à **0** échec sur le serveur de test (les connexions du pool mortes sont
  écartées par Npgsql) ; le test tolère jusqu'à 2 échecs, **jamais** de doublon.

## 6. Validation

- build : 0 avertissement, 0 erreur ;
- unitaires : **2457 / 2457** (App 268 · DatabaseManager 345 · Application 628 · Domain 1216 ; +16), 0 ignoré ;
- intégration PostgreSQL 17.10 : **232 / 232** (+20 : multi-processus 10, réseau 6, redémarrage 2, charge 2), 0 ignoré ;
- dérive EF : verte sur SQLite et sur PostgreSQL ; **aucune migration**, instantanés identiques ;
- `dotnet list package --vulnerable --include-transitive` : aucune vulnérabilité (12 projets) ;
- `git diff --check` : propre.

## 7. Questions ouvertes (non bloquantes, regroupées pour l'architecte)

| # | Question | Recommandation |
|---|---|---|
| **Q-P4-10-1** | Issue de validation inconnue : la protection complète contre la double saisie exige une **clé d'idempotence** (O13) | V1 : message honnête + aucune nouvelle tentative (livré) ; clé d'idempotence après V1 |
| **Q-P4-10-2** | Contexte EF unique pour la session (cause racine de D3) | V1 : abandon de l'unité de travail en échec (livré) ; contexte par opération après V1 |
| **Q-P4-10-3** | Alerte d'échec de sauvegarde planifiée (reprend Q-P4-9-2) | journal d'événements Windows + historique du Planificateur, vérifiés en recette P4-11 ; alerte active en P8 |
| **Q-P4-10-4** | Dérive d'horloge des postes (ADR-004 décision 9) | exigence NTP (P4-8) vérifiée en recette P4-11 ; aucun code V1 |
| **O12 / Windows natif** | ces preuves tournent en CI Linux et sur poste Windows contre conteneurs | recette P4-11 sur le laboratoire Windows (MMV-SRV / MMV-CLI) |

## 8. Effet sur la trajectoire

Critères **8, 9, 10** prouvés en CI (Linux, PostgreSQL 17.10) ; la recette multi-postes Windows réelle reste
**P4-11**. V1 multi-poste reste **NOT GO** (P4-11, P4-12).
