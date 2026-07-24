# Rapport P4-1 — Lot B : complétion technique du spike provider

> **MODE** : `COMPLETE_P4_1_TECHNICAL_SPIKE_LOT_B_NO_COMMIT`
> **Aucun provider n'est choisi. Aucun provider n'est éliminé.** Aucun score global n'est attribué.
> **Aucune** modification de `src/**`, `tests/**`, `Migrations/**`, UI, `MMV.sln` ou `.github/**`.
> **Le dépôt réel et les sorties de commandes priment toujours sur ce document.**

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Date d'exécution | 2026-07-24 |
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` ✅ |
| HEAD local et distant | `1f54f3613119adcf3f46f38a61071a79f4b27482` ✅ |
| `origin/main` | `3ed883634dae83399d3e93dbc1dc65ffaabdf243` |
| CI de référence | run `30046062019` ✅ |
| Phase / cycle | P4-1 — `P4-1-TECHNICAL-COMPLETION-LOT-B` |
| Périmètre | Expérimentations exécutables sur conteneurs jetables, **sans installation Windows** |

---

## 2. Baseline vérifiée

### 2.1 Portes Git (au démarrage)

```
git branch --show-current            → p4-multi-poste                                ✅
git rev-parse HEAD                   → 1f54f3613119adcf3f46f38a61071a79f4b27482      ✅
git rev-parse origin/p4-multi-poste  → 1f54f3613119adcf3f46f38a61071a79f4b27482      ✅
git diff --check / --stat / --name-status → AUCUN fichier suivi modifié              ✅
git status --short → ?? design-handoff/  ?? design/  ?? docs/ui/
                     ?? docs/implementation/P4-1-provider-comparison-spike-report.md
                     ?? spikes/                                                      ✅
```

Aucun changement non autorisé. Les ajouts P4-1 préexistants et les trois dossiers hors périmètre
(`design-handoff/`, `design/`, `docs/ui/`) sont les seuls éléments non suivis.

### 2.2 Porte CI — run `30046062019`

```
headSha    = 1f54f3613119adcf3f46f38a61071a79f4b27482   ✅
headBranch = p4-multi-poste       event = push          ✅
status     = completed            conclusion = success  ✅
```

### 2.3 Baseline locale (avant expérimentations)

| Contrôle | Résultat |
|---|---|
| `dotnet restore MMV.sln` | up-to-date ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning · 0 Error** ✅ |
| `dotnet test MMV.sln --no-build -c Debug` | **1499** — Domain **649** · Application **611** · App **239** ; 0 échec ; 0 ignoré ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité** (7 projets) ✅ |
| `dotnet ef migrations has-pending-model-changes` | *No changes have been made to the model since the last migration* ✅ |

---

## 3. État du rapport initial

Le rapport `P4-1-provider-comparison-spike-report.md` a été **lu intégralement**. Trois catégories de
problèmes ont été identifiées et corrigées (§4) :

1. **Formulations non conformes au protocole** — l'installation Windows y était décrite comme
   « média en cours de téléchargement » et « amorcée mais non exécutée », alors qu'elle avait été
   réellement **tentée** puis avait **échoué** ; la cause était présentée comme « la plus probable »
   alors qu'aucun journal ne l'établit.
2. **Un compte de primitives non réconcilié** — « 5 exécutées sur 15, 10 restantes » (§17/§24), repris
   d'un total qui contient un **doublon conceptuel** et qui assimile « primitive testée » à
   « implémentation de production appelée » (§6).
3. **Deux mesures classées `NOT_TESTED` alors qu'elles étaient réalisables** — `READ_COMMITTED_SNAPSHOT`
   (abandonnée sur un conflit de collation non diagnostiqué) et l'import SQLite.

---

## 4. Corrections documentaires appliquées

| Point | Formulation initiale | Formulation corrigée |
|---|---|---|
| Installation Windows | « média en cours de téléchargement » · « n'a pas été exécutée » · « seulement amorcée » | « L'installation Windows native de SQL Server Express a été tentée avec élévation, mais elle a échoué avant la création d'une instance ou d'un service exploitable. » |
| Cause racine | « Cause la plus probable … conflit de versions de composants partagés » | « Cause racine non déterminée. La coexistence de composants SQL Server 2025 RC et du média RTM est une hypothèse plausible, mais non démontrée faute de journal d'installation exploitable. » |
| Gate ADR | « Motif unique et précis … 10 sur 11 critères » | « Le seul critère manquant du gate minimal *CANDIDATE READY FOR ADR* … est l'installation Windows documentée et testée », **suivi de** « Le spike complet reste néanmoins incomplet tant que les primitives restantes, l'import SQLite, les pannes, les deadlocks, les retries et les tests réseau ne sont pas exécutés. » |
| Désinstallation | trois options dont « désinstaller les composants SQL Server 2025 RC » | **aucune désinstallation n'est recommandée** |
| RCSI | `NOT_TESTED` (« requête en échec sur conflit de collation, non rejouée ») | **mesuré** (§15) — cause du conflit identifiée précisément |
| Import SQLite | `NOT_TESTED` | **exécuté et réconcilié** sur les deux providers (§16) |
| Primitives | « 5 sur 15 · 10 restantes » | inventaire réconcilié à **14 primitives distinctes** (§6) |

Le rapport initial a été mis à jour en conséquence ; ses §16, §22, §24, §25, §26, §27 et §29 portent
désormais les résultats du lot B.

---

## 5. Environnement Docker

Le démon Docker était **arrêté** au démarrage ; il a été démarré (Docker Desktop), **sans installer ni
désinstaller quoi que ce soit**. Aucune instance native n'a été contactée.

| Élément | Valeur relevée |
|---|---|
| Docker | Serveur **29.4.1** |
| Conteneurs préexistants | 6 conteneurs utilisateur sans rapport avec MMV — **jamais touchés** |
| Volumes Docker | **aucun** (`docker volume ls` vide) — avant comme après |
| Images réutilisées | `postgres:17`, `mcr.microsoft.com/mssql/server:2022-latest` (déjà en cache) |

### 5.1 PostgreSQL jetable — vérifié par requête serveur

| Élément | Valeur |
|---|---|
| Conteneur | `mmv-p4-pg`, **sans volume persistant** |
| Exposition | **`127.0.0.1:5433` uniquement** |
| Version (`SELECT version()`) | `PostgreSQL 17.10 (Debian 17.10-1.pgdg13+1) on x86_64-pc-linux-gnu, gcc 14.2.0, 64-bit` |
| Bases présentes | `mmv_p4_spike`, `postgres`, `template0`, `template1` — **aucune base utilisateur inattendue** ✅ |
| Rôle dédié | `mmv_spike` (LOGIN, CREATEDB), mot de passe **aléatoire** |
| Isolation relevée | `read committed` |

### 5.2 SQL Server Express jetable — vérifié par requête serveur

| Élément | Valeur |
|---|---|
| Conteneur | `mmv-p4-mssql`, `MSSQL_PID=Express`, **sans volume persistant** |
| Exposition | **`127.0.0.1:14333` uniquement** |
| Édition (`SERVERPROPERTY`) | **`Express Edition (64-bit)`** — **`EngineEdition = 4`** |
| Version | `16.0.4265.3`, `ProductLevel = RTM` |
| Collation serveur | `SQL_Latin1_General_CP1_CI_AS` |
| Bases présentes | `master`, `tempdb`, `model`, `msdb`, `mmv_p4_spike` — **aucune base utilisateur inattendue** ✅ |
| Isolation relevée | `ReadCommitted` ; `is_read_committed_snapshot_on = 0` |

**Aucun secret n'apparaît dans ce rapport.** Les mots de passe, générés aléatoirement, ont résidé dans le
répertoire temporaire de session et ont été supprimés (§19).

---

## 6. Inventaire exact des primitives — réconciliation

L'inventaire a été **recalculé depuis le code réel**, pas repris du chiffre « 10 restantes ».

### 6.1 Matrice unique

| # | Primitive | Implémentation réelle | Testée avant le lot B ? | Test restant / réalisé en lot B |
|---|---|---|---|---|
| 1 | Stock — décrément | `EfStockMutationService.DecrementStockAsync` | ✅ E8 — **implémentation de production** | re-exercée dans la transaction de vente (E11) |
| 2 | Stock — incrément | `EfStockMutationService.IncrementStockAsync` | ❌ | ✅ **E11** |
| 3 | Stock — ajustement (CAS) | `EfStockMutationService.AdjustStockToAsync` | ❌ | ✅ **E11** |
| 4 | Vente — transaction composée | `EfTransactionRunner` | ⚠️ partiel — E7 exerçait des transactions *génériques*, pas le flux de vente | ✅ **E11** (commit intégral **et** rollback intégral) |
| 5 | Numérotation documentaire | `EfNumberSequenceService.NextNumberAsync` | ✅ E8 — **implémentation de production** | re-exercée (E11, E15) |
| 6 | Commande — transition simple | `OrderRepository.TryTransitionStatusAsync` | ❌ | ✅ **E11** |
| 7 | Commande — transition + fiche | `OrderRepository.TryTransitionWithWorkshopSheetAsync` | ❌ | ✅ **E11** |
| 8 | Fiche atelier — version suivante | `OrderRepository.CreateNextWorkshopSheetVersionAsync` | ❌ — E4 testait l'**index**, pas la méthode | ✅ **E11** (v1 concurrente **et** v2 par CAS) |
| 9 | QC — décision définitive | `OrderRepository.TryTakeWorkshopSheetQcDecisionAsync` | ❌ | ✅ **E11** |
| 10 | Paiement — règlement du solde | `SaleRepository.TrySettleRemainingBalanceAsync` | ⚠️ E8 exerçait un `ExecuteUpdateAsync` **réécrit dans le harness**, pas la méthode de production | ✅ **E11** (méthode de production) |
| 11 | Notification — alerte active | `NotificationRepository.TryCreateActiveLowStockAsync` | ⚠️ E5 mesurait des **stratégies SQL du harness**, pas la méthode de production | ✅ **E11** — la méthode de production **ÉCHOUE sur les deux providers** (§9) |
| 12 | Notification — réconciliation | `NotificationRepository.ResolveActiveLowStockAsync` | ❌ — E3 résolvait par SQL brut | ✅ **E11** (lot + idempotence) |
| 13 | Fournisseur — suppression si inutilisé | `SupplierRepository.TryDeleteIfUnusedAsync` | ⚠️ E8 exerçait un `ExecuteDeleteAsync` **réécrit dans le harness** | ✅ **E11** (méthode de production) |
| 14 | Utilisateur — unicité du login | **index unique** `idx_users_normalized_username_unique` | ✅ E8 — garantie **structurelle** vérifiée | inchangé — voir 6.3 |
| ~~15~~ | ~~Numérotation — table `DocumentSequences`~~ | **—** | — | **DOUBLON de #5** — voir 6.2 |

### 6.2 Doublons conceptuels détectés

| Doublon | Constat |
|---|---|
| **Compteur documentaire vs numérotation** (lignes 5 et 15 de P4-0 §15) | La ligne 15 ne porte **ni port, ni implémentation, ni mécanisme** (colonnes `—` dans P4-0 lui-même) : elle décrit la **table de stockage** de la primitive #5, pas une primitive distincte. Le total réel est donc **14**, pas 15. |
| **Création LowStock vs upsert LowStock** | Une **seule** primitive (#11), testée sous **trois formes** : l'index seul (E3), deux stratégies SQL écrites par le harness (E5), et enfin la méthode de production (E11). Seule la troisième mesure la dette de portage réelle. |
| **Mono-processus vs multi-processus** | E9 n'ajoute **aucune primitive** : il rejoue les primitives #1, #5 et #11 sous une **forme de preuve plus forte** (processus distincts). Compter E8 et E9 séparément gonflerait artificiellement le nombre de primitives couvertes. |

### 6.3 Précision sur la primitive #14

`UserRepository` n'expose **aucune primitive de création atomique** : ses méthodes sont
`GetByNormalizedUsernameAsync`, `ExistsByNormalizedUsernameAsync`, `GetActiveUsersAsync`,
`GetByRoleAsync` — **toutes en lecture**. La garantie d'unicité repose donc **entièrement sur l'index
unique**, arbitré par la base. C'est cette garantie qui a été prouvée en E8, et elle reste valide.
Il n'y a **pas** de méthode de production supplémentaire à exercer.

### 6.4 Compte réconcilié

| Mesure | Valeur |
|---|---|
| Lignes de la matrice P4-0 §15 | 15 |
| **Primitives distinctes après déduplication** | **14** |
| Prouvées **via l'implémentation de production** avant le lot B | **2** (#1, #5) |
| Prouvées seulement sous forme **réécrite par le harness** avant le lot B | 3 (#10, #11, #13) |
| Garantie **structurelle** prouvée sans méthode dédiée | 1 (#14) |
| Non testées avant le lot B | **8** (#2, #3, #4, #6, #7, #8, #9, #12) |
| **Exercées via l'implémentation de production après le lot B** | **13 / 14** |
| **Exercées et en ÉCHEC MESURÉ** | **1** (#11) — voir §9 |

> **L'écart avec le rapport initial est documenté, pas masqué.** « 5 exécutées / 10 restantes » était
> inexact sur les deux termes : le dénominateur contenait un doublon, et 3 des 5 « exécutées » ne
> passaient pas par le code de production.

---

## 7. Primitives exécutées (E11) — résultats

Toutes les primitives ci-dessous appellent **directement les implémentations de production**, sur base
jetable, avec **deux connexions réelles** synchronisées par barrière lorsque la concurrence est
pertinente, et **vérification de l'état final en base**.

| Primitive | PostgreSQL 17.10 | SQL Server Express 16.0.4265.3 | Garantie | Lignes affectées | Différence |
|---|---|---|---|---|---|
| Incrément relatif | stock 10 +5 +5 → **20** | idem → **20** | Aucun incrément perdu | 1 + 1 | **oui, sur la valeur RETOURNÉE** (voir ci-dessous) |
| Ajustement CAS | 1 succès (`7→100`) + 1 `StockConcurrencyConflictException` | 1 succès (`7→200`) + 1 conflit | Pas de last-write-wins | 1 / 0 | aucune |
| Transaction de vente — commit | `VT-000001`, stock 3−2 = **1** | idem | Commit intégral | — | aucune |
| Transaction de vente — rollback | `InsufficientStockException` ; ventes **1**, compteur **1**, stock **1** | idem | **Rollback intégral, numéro consommé annulé** | — | aucune |
| Transition de commande | 1 prise / statut `ToFabricate` | idem | Pas de double avancement | 1 / 0 | aucune |
| Transition + fiche atelier | empreinte périmée → `WorkshopSheetRequirementNotMet` ; 1 `Taken` + 1 `StatusConflict` | idem | EXISTS corrélé correct | 1 / 0 | aucune |
| Fiche atelier v1 concurrente | 1 créée + 1 `WorkshopSheetVersionConflictException` | idem | Arbitrage par index | 1 / 0 | aucune |
| Fiche atelier v2 (CAS) | 1 créée ; 2 versions, **1 courante** | idem | CAS sur la version lue | 1 / 0 | aucune |
| Décision QC | 1 prise (`Failed`) ; rejeu → `False` | 1 prise (`Passed`) ; rejeu → `False` | Décision définitive | 1 / 0 | aucune *(le gagnant diffère : simple aléa de course)* |
| Règlement du solde **(production)** | 1 succès ; reste `0,00` ; acompte `300,00` ; `Paid` | idem | Un seul règlement | 1 / 0 | aucune |
| Suppression fournisseur **(production)** | référencé → `False` (conservé) ; libre → `True` | idem | Historique préservé | 0 / 1 | aucune |
| Réconciliation LowStock **(production)** | lot vide `0`, 1er lot `2`, **rejeu `0`**, 1 seule date | idem | **Idempotente** | 2 / 0 | aucune |
| Création LowStock **(production)** | ❌ **ÉCHEC** | ❌ **ÉCHEC** | — | — | **aucune : échec identique** (§9) |

### 7.1 Différence observée sur l'incrément relatif

`IncrementStockAsync` **relit** le stock après l'`UPDATE` relatif et retourne cette relecture. Sous
concurrence, les deux appels ont retourné :

- **PostgreSQL** : `20` et `15` (l'un des appelants voit un état intermédiaire) ;
- **SQL Server** : `20` et `20` (les deux relectures voient l'état final).

**Le stock final est correct (20) sur les deux providers** : aucun incrément n'est perdu, la garantie
tient. Mais **la valeur retournée n'est pas « le stock après MON incrément »** — c'est une relecture qui
peut inclure l'incrément d'un autre poste, et son contenu **diffère entre providers**. Tout appelant
qui afficherait cette valeur comme un résultat propre à son opération serait trompé, différemment
selon le provider. Fait à porter à P4-2.

---

## 8. Deadlocks (E12) — provoqués et mesurés

Scénario croisé strict sur table jetable, **deux connexions**, synchronisation **déterministe par
barrière**, timeout de sécurité 60 s, aucune boucle infinie.

- Transaction **A** : verrouille R1 → attend → demande R2.
- Transaction **B** : verrouille R2 → attend → demande R1.

| Mesure | PostgreSQL 17.10 | SQL Server Express |
|---|---|---|
| Type d'exception | `Npgsql.PostgresException` | `Microsoft.Data.SqlClient.SqlException` |
| Code | **`SqlState = 40P01`** *(deadlock detected)* | **`Number = 1205`**, `State=51`, `Class=13` |
| Victime choisie | **B** (1 victime / 1 survivant) | **B** (1 victime / 1 survivant) |
| Transaction utilisable **après** le deadlock | ❌ **NON** — `25P02` *(in_failed_sql_transaction)* | ✅ **OUI** — lecture ultérieure réussie (`COUNT = 2`) |
| Durée observée | **933 ms** | **1 295 ms** |
| État final après rollback | `SUM(payload) = 2` — **seul le survivant a écrit** ✅ | idem ✅ |

> **Le deadlock est réellement arbitré par le serveur sur les deux providers**, avec une victime unique
> et aucun effet partiel. La **différence sémantique du §15 initial se confirme jusque dans le
> deadlock** : sur PostgreSQL la transaction victime est **entièrement perdue**, sur SQL Server elle
> reste utilisable. Un code de reprise écrit pour SQL Server ne fonctionnera pas sur PostgreSQL.

---

## 9. Primitive de création LowStock — échec mesuré sur les deux providers

`NotificationRepository.TryCreateActiveLowStockAsync` a été **appelée telle quelle**. Résultat :

| Provider | Exception | Message |
|---|---|---|
| PostgreSQL | `InvalidCastException` | *The value "Microsoft.Data.Sqlite.SqliteParameter" is not of type "NpgsqlParameter" and cannot be used in this parameter collection.* |
| SQL Server | `InvalidCastException` | *The SqlParameterCollection only accepts non-null Microsoft.Data.SqlClient.SqlParameter type objects, not Microsoft.Data.Sqlite.SqliteParameter objects.* |

**Fait important, plus précis que l'analyse P4-0.** L'échec ne vient **pas** du dialecte
`ON CONFLICT DO NOTHING` — il survient **avant**, au niveau des **objets paramètres** : la méthode
construit des `SqliteParameter`
([NotificationRepository.cs:115-133](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L115-L133)).
La dette de portage de cette primitive est donc **double** (paramètres **et** dialecte) et **identique
sur les deux providers** : elle **ne départage pas** PostgreSQL et SQL Server.

---

## 10. Timeouts (E13)

Quatre situations volontairement distinctes, chacune mesurée avec l'état de la **connexion** et de la
**transaction** après l'erreur.

| Situation | PostgreSQL | SQL Server Express |
|---|---|---|
| **Timeout client** (`CommandTimeout=2 s` sur commande 30 s) | `NpgsqlException`, **aucun `SqlState`**, *Exception while reading from stream* — 2 025 ms | `SqlException` **`Number = -2`**, `Class=11` — 2 009 ms |
| **Annulation `CancellationToken`** (2 s) | `PostgresException` **`57014`** — 2 018 ms | `SqlException` **`Number = 0`**, `Class=11` — 2 002 ms |
| **Timeout de verrou** (ligne détenue) | `NpgsqlException`, **aucun `SqlState`** — 3 008 ms | `SqlException` **`Number = -2`** — 3 014 ms |
| **Connexion refusée** (port fermé) | `NpgsqlException`, **aucun `SqlState`** — 2 030 ms | `SqlException` **`Number = 10061`**, `Class=20` — 6 412 ms |
| **Connexion après erreur** | `Open` dans tous les cas | `Open` dans tous les cas |
| **Transaction après erreur** | ❌ **INUTILISABLE** dans tous les cas — `25P02` | ✅ **UTILISABLE** dans tous les cas |
| État final de la table | `payload = 0` — **aucune écriture expirée conservée** ✅ | idem ✅ |

### 10.1 Ce qui n'est PAS distinguable

- **Sur les deux providers**, le **timeout client** et le **timeout de verrou** produisent le **même
  code** (`-2` côté SQL Server ; aucun `SqlState` côté PostgreSQL). Une politique de reprise **ne peut
  pas** distinguer « la requête est trop longue » de « quelqu'un détient le verrou » sur le seul code.
- **Côté PostgreSQL**, trois situations différentes (timeout client, timeout de verrou, connexion
  refusée) donnent **toutes** une `NpgsqlException` **sans `SqlState`**. Seul le **type** et l'état de
  connexion permettent de les séparer — **jamais un code**.
- **Côté SQL Server**, l'annulation par `CancellationToken` rend `Number = 0`, non classifiable.

> **Correction d'un artefact de mesure.** Une première exécution de E13 avait produit, côté SQL Server,
> trois `InvalidOperationException` à 0 ms au lieu de vrais timeouts : le harness ne rattachait pas la
> commande à la transaction ouverte. `Microsoft.Data.SqlClient` **exige** ce rattachement explicite,
> alors que **Npgsql le tolère** — le défaut ne se manifestait donc que sur un provider. Corrigé, puis
> E13 **entièrement rejouée**. Les chiffres ci-dessus proviennent de l'exécution corrigée.

---

## 11. Perte de connexion en transaction, redémarrage et reconnexion (E14)

Scénario : transaction ouverte → 1re écriture non commitée → **arrêt brutal du conteneur** → 2e écriture
→ redémarrage → vérification de l'état réel.
Garde-fou : seul un conteneur dont le nom commence par `mmv-p4-` peut être arrêté ; aucune commande
destructive n'est exposée au harness.

| Mesure | PostgreSQL | SQL Server Express |
|---|---|---|
| 1re écriture avant coupure | 1 ligne, **non commitée** | idem |
| 2e écriture après coupure | `NpgsqlException`, aucun `SqlState` | `SqlException` **`Number = 0`**, `Class = 20` |
| Serveur de nouveau joignable | **0,6 s** | **4,7 s** |
| **État final après redémarrage** | `payload = 100` — **transaction non commitée correctement annulée** ✅ | idem ✅ |
| Connexion **pendant** l'arrêt | `NpgsqlException` *Failed to connect* — 2 019 ms | `SqlException` **`10061`** — 6 380 ms |
| Reconnexion **sans purge du pool** | ❌ **ÉCHEC immédiat (1 ms)** — connexion morte rendue par le pool | ❌ **ÉCHEC immédiat (0 ms)** — `10061` |
| Reconnexion **après purge du pool** | ✅ **0,5 s** | ✅ **3,1 s** |
| Cohérence des données | **préservée** (valeur identique avant/après) ✅ | idem ✅ |

> **Aucune écriture partielle ne survit** à une coupure en pleine transaction, sur les deux providers.
> **La purge du pool est indispensable** des deux côtés : sans elle, la première reconnexion échoue
> systématiquement et instantanément, le pool rendant une connexion morte. C'est le même piège que
> celui déjà relevé pour `pg_restore` au §21 initial, mais il vaut **aussi pour SQL Server**.

---

## 12. Retry — politique expérimentale, harness uniquement

`EfTransactionRunner` **n'est pas modifié** ; le produit ne comporte toujours **aucun retry**. La
politique `SpikeRetryPolicy` (harness) est bornée à **5 essais**, délai fixe **1 s**, journalise chaque
tentative et **ne masque jamais l'échec final**.

### 12.1 Table de décision mesurée

| Erreur | Retry autorisé ? | Motif | Résultat PostgreSQL | Résultat SQL Server |
|---|---|---|---|---|
| Violation d'unicité | **NON** | la base a tranché ; un rejeu la reproduirait | `23505`, contrainte `idx_product_categories_name_unique` **nommée** | `2601`, contrainte extraite du **message** |
| Violation FK / NOT NULL / CHECK | **NON** | donnée invalide | `23503` / `23502` / `23514` | `547` / `515` |
| Erreur métier | **NON** | décision fonctionnelle, pas technique | `InsufficientStockException` | idem |
| Deadlock | **OUI** *(transaction entière)* | victime désignée par le serveur | `40P01` | `1205` |
| Timeout | **OUI** *(si idempotent)* | expiration transitoire | `NpgsqlException` sans `SqlState` | `-2` |
| Transaction avortée PostgreSQL | **OUI** *(en recommençant la transaction)* | `25P02` : rejouer la seule commande échoue toujours | `25P02` | *(sans objet)* |
| Connexion indisponible | **OUI** | indisponibilité potentiellement transitoire | `NpgsqlException` sans `SqlState` | `10061` puis **`10054`** |

### 12.2 Reprise sur panne réellement transitoire

Conteneur arrêté, redémarré **en parallèle** ; rejeu borné à 5.

| Provider | Déroulé | Résultat |
|---|---|---|
| PostgreSQL | tentative 1 **échec**, tentative 2 **succès** | ✅ **REPRISE RÉUSSIE** |
| SQL Server | tentatives 1–4 **échec** (`10061` puis `10054` ×3), tentative 5 **succès** | ✅ **REPRISE RÉUSSIE** |

> **Deux faits mesurés, à retenir tels quels.**
>
> 1. **`10054` n'est pas `10061`.** Une première version de la politique ne connaissait que
>    `53/10060/10061` : elle a **refusé** de rejouer `10054` et **propagé l'échec** — comportement
>    correct de la politique, mais **couverture insuffisante**. Or `10054`
>    (*connexion existante fermée par l'hôte*) est le code **majoritaire** pendant le redémarrage de
>    SQL Server, parce que le port est déjà ouvert (proxy Docker) alors que le moteur ne l'est pas.
>    Toute politique de reprise SQL Server fondée sur le seul `10061` **manquerait le cas réel**.
> 2. **La marge de reprise n'est pas la même.** PostgreSQL revient en **2 tentatives**, SQL Server en
>    **5** — soit la totalité du budget. Lors d'une exécution antérieure avec le même budget,
>    SQL Server **avait épuisé les 5 tentatives sans revenir**, et l'échec avait été correctement
>    propagé. **Le retour de SQL Server Express n'est donc pas fiablement contenu dans un budget de
>    rejeu court** ; celui de PostgreSQL l'est largement. Ce point est un fait d'exploitation, pas un
>    jugement.

---

## 13. Idempotence

Classement des opérations susceptibles d'être rejouées par un retry, **mesuré** et non supposé.

| Opération | Classe | Preuve (identique sur les deux providers) |
|---|---|---|
| Résolution LowStock (`ResolveActiveLowStockAsync`) | **Idempotente par CAS** | 3 exécutions → `1, 0, 0` ; la date de résolution initiale **n'est jamais réécrite** |
| Décision QC (`TryTakeWorkshopSheetQcDecisionAsync`) | **Idempotente par CAS** | 3 exécutions → `True, False, False` ; commentaire conservé = `1` |
| Transitions de commande (`TryTransitionStatusAsync`, `…WithWorkshopSheet…`) | **Idempotente par CAS** | 2e prise → `False`, aucune ligne affectée (E11) |
| Règlement du solde (`TrySettleRemainingBalanceAsync`) | **Idempotente par CAS** | 2e règlement → `False` (E11) |
| Création LowStock (`TryCreateActiveLowStockAsync`) | **Idempotente par clé unique** *(par conception)* | **non vérifiable** : la méthode échoue avant d'atteindre la base (§9) |
| **Incrément de stock** (`IncrementStockAsync`) | ❌ **NON IDEMPOTENTE** | 3 exécutions de `+5` sur 10 → **stock 25** : *un retry aveugle fausse le stock* |
| **Numérotation** (`NextNumberAsync`) | ❌ **NON IDEMPOTENTE** | 3 exécutions → `SQ-000001, SQ-000002, SQ-000003` : *chaque rejeu consomme un numéro* |
| Décrément de stock (`DecrementStockAsync`) | ❌ **NON IDEMPOTENTE** *(relatif)* | même forme que l'incrément ; non rejouable sans clé d'idempotence |
| Paiement / notification externes | **Inconnu** | hors périmètre du spike — non mesuré |

> **Conclusion factuelle.** Les primitives protégées par un **compare-and-swap** sont sûres à rejouer.
> Les primitives à **écriture relative** (incrément, décrément) et la **numérotation** ne le sont
> **pas** : un retry automatique posé sans clé d'idempotence **corromprait le stock et la
> numérotation**. Ce résultat est **identique sur les deux providers** et **ne les départage pas** —
> mais il conditionne toute introduction de retry en P4-2.

---

## 14. *(intégré au §12)*

---

## 15. `READ_COMMITTED_SNAPSHOT` (E16) — SQL Server

### 15.1 Cause exacte de l'échec de mesure initial

Le §16 initial indiquait un « conflit de collation entre `Latin1_General_CI_AS_KS_WS` et
`SQL_Latin1_General_CP1_CI_AS` ». La cause a été **reproduite et identifiée précisément** :

| Élément | Collation relevée |
|---|---|
| Collation **serveur** | `SQL_Latin1_General_CP1_CI_AS` |
| `sys.databases.name` | `SQL_Latin1_General_CP1_CI_AS` |
| **`sys.databases.snapshot_isolation_state_desc`** | **`Latin1_General_CI_AS_KS_WS`** |

Ce n'est **pas** la colonne `name` qui pose problème, mais la colonne **descriptive**
`snapshot_isolation_state_desc`, typée avec la collation du catalogue. La concaténer avec un littéral
lève **`Msg 451`** *(Cannot resolve collation conflict … in concat operator)*.

**Contournement** : sélectionner les colonnes **séparément** (ou forcer `COLLATE DATABASE_DEFAULT`) —
ne jamais concaténer une colonne descriptive du catalogue. La mesure devient alors triviale. Le
classement `NOT_TESTED` n'était donc **pas** dû à une limite du produit.

### 15.2 Mesures, par configuration

| Mesure | **RCSI = OFF** (défaut) | **RCSI = ON** |
|---|---|---|
| `is_read_committed_snapshot_on` | **0** | **1** |
| `snapshot_isolation_state_desc` | `OFF` | `OFF` *(indépendant : snapshot isolation ≠ RCSI)* |
| Collation de la base | `SQL_Latin1_General_CP1_CI_AS` | idem |
| **Lecture pendant écriture non commitée** | ❌ **BLOQUÉE** puis expirée — `Number = -2` après **5 022 ms** | ✅ **NON BLOQUANTE** — valeur **commitée** `100` renvoyée en **9 ms** |
| Valeur après rollback de l'écrivain | `100` — cohérence préservée ✅ | `100` — cohérence préservée ✅ |
| **Primitive CAS** (décrément du dernier article, 2 connexions) | lignes affectées `[1, 0]`, stock final **0** — **aucune survente** ✅ | lignes affectées `[1, 0]`, stock final **0** — **aucune survente** ✅ |

> **Résultat net.** `READ_COMMITTED_SNAPSHOT` supprime le blocage des lecteurs (5 s → 9 ms sur ce
> scénario) **sans affaiblir la primitive CAS** : le décrément conditionnel continue de refuser la
> survente à l'identique. La valeur par défaut d'Express est **OFF**, donc **les lecteurs bloquent
> par défaut**. PostgreSQL, lui, ne bloque **jamais** les lecteurs en `read committed` (MVCC natif) :
> son comportement par défaut équivaut à celui de SQL Server **avec RCSI activé**.

---

## 16. Import SQLite réduit (E17) — exécuté et réconcilié

**Source** : base SQLite **jetable** créée par le harness dans le répertoire temporaire, alimentée par
le **modèle réel**. **Aucune base MMV utilisateur n'a été lue.** Aucun mot de passe ni hash réel n'a
transité. `__EFMigrationsHistory` **n'a jamais été importé** (vérifié sur la cible : absent).

Les montants sont mappés en décimal exact **des deux côtés** : avec le mapping `REAL` de production, la
source elle-même serait déjà altérée (§11 du rapport initial) et l'expérimentation ne mesurerait plus
la fidélité du transfert. **La correction du mapping monétaire reste donc un prérequis d'import.**

L'import est **générique** : les colonnes sont découvertes par lecture, et chaque valeur est convertie
selon le **type attendu par le provider** (obtenu du `ValueConverter` EF, pas du type CLR de l'entité).

### 16.1 Réconciliation par table — **identique sur les deux providers**

| Table | SQLite avant | Serveur après | Identifiants conservés | Écart |
|---|---|---|---|---|
| Suppliers | 1 | 1 | ✅ | 0 |
| ProductCategories | 1 | 1 | ✅ | 0 |
| Products | 1 | 1 | ✅ `[1] → [1]` | 0 |
| Customers | 1 | 1 | ✅ | 0 |
| Sales | 1 | 1 | ✅ | 0 |
| SaleItems | 1 | 1 | ✅ | 0 |
| Orders | 1 | 1 | ✅ | 0 |
| WorkshopSheets | 1 | 1 | ✅ | 0 |
| Notifications | 1 | 1 | ✅ | 0 |
| DocumentSequences | 3 | 3 | ✅ | 0 |

### 16.2 Fidélité des valeurs

| Contrôle | PostgreSQL | SQL Server |
|---|---|---|
| Montant `999999,99` | **`999999,99` EXACT** ✅ | **`999999,99` EXACT** ✅ |
| Date `2026-07-24T09:00:00Z` | `2026-07-24T09:00:00.0000000**Z**` ✅ | `2026-07-24T09:00:00.0000000` ✅ |
| Booléens (`IsCurrent`, `IsActive`) | exacts ✅ | exacts ✅ |
| Enums (`PaymentStatus`, `Category`, `QcStatus`) | exacts ✅ | exacts ✅ |
| Chaîne normalisée (`ref-import-001` → `REF-IMPORT-001`) | exacte ✅ | exacte ✅ |
| `NULL` conservé (`ResolvedAt`) | ✅ | ✅ |
| Séquences après import | max importé 1 → prochain **2** ✅ | max importé 1 → prochain **2** ✅ |
| Donnée invalide (FK inexistante) | **REFUSÉE** — `23503`, contrainte `FK_Products_Suppliers_SupplierId` **nommée** | **REFUSÉE** — `547`, colonne `SupplierId` |
| Rollback après échec | **propre** — 1 produit avant, 1 après ✅ | idem ✅ |
| `__EFMigrationsHistory` importé | **non** ✅ | **non** ✅ |

### 16.3 Trois obstacles d'import découverts par la mesure

1. **Données de référence seedées des deux côtés.** `DocumentSequenceConfiguration` seede `SALE` et
   `ORDER` via `HasData` : ces lignes existent **dans la source SQLite ET dans la cible** créée par
   `EnsureCreated`/migrations. Un import ligne à ligne naïf échoue en **violation de clé primaire**
   (`23505` / `2627`). Il a fallu **vider les données de référence de la cible** (2 lignes) avant
   l'import. Tout outil de migration devra traiter ce cas explicitement.
2. **Clé primaire naturelle sans identité.** `DocumentSequences` a pour PK `SequenceName` (texte) :
   `SET IDENTITY_INSERT` y échoue (*Table … does not have the identity property*). La bascule ne doit
   être émise que pour les tables **réellement à identité** — détection nécessaire, table par table.
3. **`DateTimeKind` non préservé côté SQL Server.** L'**instant** est identique des deux côtés, mais
   PostgreSQL restitue `Kind = Utc` (`…Z`) tandis que SQL Server restitue `Kind = Unspecified`. Les
   comparaisons `DateTime` du code ignorent `Kind`, donc rien n'échoue ici ; mais toute logique qui
   convertirait en heure locale se comporterait **différemment selon le provider**. Fait à porter à P4-2.

### 16.4 Portée de cette preuve

Ce prototype porte sur **10 tables** et **1 à 3 lignes par table**, avec des valeurs choisies pour
exercer les cas de typage. Il **ne constitue pas** une validation d'import à volumétrie réelle, ni une
mesure de durée d'import, ni un test de reprise sur interruption au milieu d'un import.

---

## 17. Résultats PostgreSQL — synthèse du lot B

| Domaine | Résultat |
|---|---|
| Primitives restantes | **13 / 14 exercées via les implémentations de production** ; 1 en échec mesuré (§9) |
| Deadlock | `40P01`, victime unique, **933 ms**, transaction **perdue** (`25P02`) |
| Timeout client / verrou | `NpgsqlException` **sans `SqlState`** — non distinguables par code |
| Annulation | `57014` |
| Connexion refusée | `NpgsqlException` sans `SqlState` — 2,0 s |
| Transaction après erreur | **toujours inutilisable** (`25P02`) |
| Coupure en transaction | écriture non commitée **correctement annulée** |
| Reprise après redémarrage | **0,5–0,6 s** ; purge du pool **obligatoire** |
| Retry sur panne transitoire | **réussi en 2 tentatives** |
| RCSI | *(sans objet — MVCC natif, lecteurs jamais bloqués en `read committed`)* |
| Import SQLite réduit | **réconciliation complète**, PK conservées, séquences resynchronisées |

---

## 18. Résultats SQL Server Express — synthèse du lot B

| Domaine | Résultat |
|---|---|
| Primitives restantes | **13 / 14 exercées via les implémentations de production** ; 1 en échec mesuré (§9) |
| Deadlock | `1205`, victime unique, **1 295 ms**, transaction **encore utilisable** |
| Timeout client / verrou | `Number = -2` dans les deux cas — non distinguables entre eux |
| Annulation | `Number = 0` — **non classifiable** |
| Connexion refusée | `10061` — 6,4 s |
| Connexion coupée en cours | **`10054`** *(code distinct de `10061`)* |
| Transaction après erreur | **toujours utilisable** |
| Coupure en transaction | écriture non commitée **correctement annulée** |
| Reprise après redémarrage | **3,1–4,7 s** ; purge du pool **obligatoire** |
| Retry sur panne transitoire | **réussi en 5 tentatives** (budget entier) ; **avait épuisé le budget** lors d'une exécution antérieure |
| RCSI | **OFF par défaut** → lecteurs bloqués (5 s) ; **ON** → lecture non bloquante (9 ms), **CAS inchangé** |
| Import SQLite réduit | **réconciliation complète**, PK conservées, `IDENTITY_INSERT` conditionnel requis |

---

## 19. Limitations du lot B

| Élément | Statut | Raison |
|---|---|---|
| **Installation Windows native** (les deux providers) | `NOT_TESTED` | **hors périmètre explicite du lot B** — condition du gate ADR, inchangée |
| Accès depuis un **autre poste** du réseau, pare-feu | `NOT_TESTED` | tout est resté sur `127.0.0.1` |
| Moteur SQL Server **sous Windows** | `NOT_TESTED` | moteur testé sur **Linux** (conteneur) |
| Import à **volumétrie réelle**, durée d'import, reprise d'un import interrompu | `NOT_TESTED` | prototype réduit (10 tables, 1–3 lignes) |
| Volumétrie MMV vs limite **10 Go** d'Express | `NOT_TESTED` | aucune mesure de volumétrie |
| Performance, montée en charge | `NOT_TESTED` | hors périmètre du spike |
| Idempotence des paiements/notifications externes | `Inconnu` | hors périmètre |
| Primitive #11 (création LowStock) | **échec mesuré** | non portable en l'état (§9) — sa garantie serveur reste **non prouvée** |
| Durées de reprise | **indicatives** | mesures uniques sur un poste, non moyennées ; variabilité constatée (§12.2) |

---

## 20. Nettoyage

| Élément | État final |
|---|---|
| Bases jetables par expérimentation | ✅ supprimées automatiquement en fin de chaque test |
| Base SQLite source (E17) | ✅ supprimée (`File.Delete`, répertoire temporaire) |
| Conteneur `mmv-p4-pg` | ✅ **supprimé** (`docker rm -f -v`) |
| Conteneur `mmv-p4-mssql` | ✅ **supprimé** (`docker rm -f -v`) |
| Vérification conteneurs | ✅ `docker ps -a --filter name=mmv-p4` → **aucun** |
| Volumes Docker | ✅ `docker volume ls` → **aucun** (état identique à l'avant-spike) |
| Conteneurs utilisateur préexistants (6) | ✅ **intacts, jamais touchés** |
| Fichiers de mots de passe temporaires | ✅ **supprimés** |
| Journaux de preuves temporaires | ✅ **supprimés** |
| Images Docker | conservées (aucune autorisation de suppression demandée) |
| Instance PostgreSQL native 17.4 | ✅ **jamais contactée, jamais modifiée** |
| Média SQL Server Windows | ✅ **non touché** |
| Composants Windows | ✅ **aucun installé, aucun désinstallé** |

---

## 21. Fichiers créés / modifiés

### 21.1 Créés dans le dépôt (non suivis)

```
docs/implementation/P4-1-technical-completion-lot-b-report.md   (ce rapport)

spikes/P4.ProviderComparison/E11_RemainingPrimitivesTests.cs
spikes/P4.ProviderComparison/E12_DeadlockTests.cs
spikes/P4.ProviderComparison/E13_TimeoutTests.cs
spikes/P4.ProviderComparison/E14_ConnectionLossTests.cs
spikes/P4.ProviderComparison/E15_RetryAndIdempotenceTests.cs
spikes/P4.ProviderComparison/E16_ReadCommittedSnapshotTests.cs
spikes/P4.ProviderComparison/E17_SqliteImportTests.cs
spikes/P4.ProviderComparison/Support/SpikeConnections.cs
spikes/P4.ProviderComparison/Support/DockerControl.cs
spikes/P4.ProviderComparison/Support/SpikeRetryPolicy.cs
spikes/P4.ProviderComparison/Support/SpikeSerialCollection.cs
```

### 21.2 Modifié dans le dépôt (non suivi)

```
docs/implementation/P4-1-provider-comparison-spike-report.md    (corrections §4 + résultats du lot B)
```

### 21.3 Hors dépôt

Mots de passe générés et journaux de preuves : répertoire temporaire de session, **supprimés** (§20).

---

## 22. État Git final

```
git status --short
 ?? design-handoff/      (préexistant, hors périmètre)
 ?? design/              (préexistant, hors périmètre)
 ?? docs/ui/             (préexistant, hors périmètre)
 ?? docs/implementation/P4-1-provider-comparison-spike-report.md
 ?? docs/implementation/P4-1-technical-completion-lot-b-report.md
 ?? spikes/

git diff --check   → propre
git diff --stat    → AUCUN fichier SUIVI modifié
```

**Aucun fichier suivi n'est modifié.** Interdits respectés : `src/**`, `tests/**`,
`src/MMV.Infrastructure/Migrations/**`, `src/MMV.App/**`, `.github/**`, `MMV.sln`, tout `*.csproj` hors
`spikes/**`, `docs/ui/**`, `design/**`, `design-handoff/**`.
**Aucun commit. Aucun push.**

### 22.1 Non-régression après expérimentations

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln --no-restore -c Debug` | **0 Warning · 0 Error** ✅ |
| `dotnet test MMV.sln --no-build -c Debug` | **1499** — Domain **649** · Application **611** · App **239** ; 0 échec ; 0 ignoré ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité** (7 projets) ✅ |
| `dotnet ef migrations has-pending-model-changes` | *No changes have been made to the model since the last migration* ✅ |

### 22.2 Harness

| Contrôle | Résultat |
|---|---|
| Build harness + worker | **0 Warning · 0 Error** ✅ |
| Sans variables d'environnement | `Skipped: 4, Passed: 0` — **aucun faux succès** ✅ |
| Exécution complète (passe 1, hors pannes) | **37 réussis / 0 échec / 0 ignoré** ✅ |
| Exécution des pannes (passe 2, sérialisée) | **8 réussis / 0 échec / 0 ignoré** ✅ |
| Journaux de preuves produits | **16 fichiers** (E1 → E17) ✅ |

---

## 23. Verdict

```
P4-1 LOT B  = GO LOCAL

P4-1 SPIKE  = INCOMPLETE
P4-1        = NOT READY FOR ADR
```

### 23.1 Justification de `P4-1 LOT B = GO LOCAL`

| Condition exigée | État |
|---|---|
| Inventaire des primitives réconcilié | ✅ **14** primitives distinctes, doublons documentés (§6) |
| Toutes les primitives techniquement exécutables testées | ✅ **13 / 14** via les implémentations de production ; la 14e est en **échec mesuré**, non contourné (§9) |
| Deadlock exécuté sur les deux providers | ✅ `40P01` / `1205`, victime unique, état final vérifié (§8) |
| Timeout exécuté | ✅ 4 situations distinguées, état connexion/transaction mesuré (§10) |
| Perte de connexion exécutée | ✅ coupure en pleine transaction, état final vérifié après redémarrage (§11) |
| Redémarrage / reconnexion exécuté | ✅ avec et sans purge du pool (§11) |
| Retry expérimenté | ✅ politique bornée, table de décision, reprise réelle mesurée (§12) |
| Idempotence analysée | ✅ 2 opérations sûres et 2 opérations dangereuses **démontrées** (§13) |
| RCSI testé | ✅ OFF **et** ON, avec impact sur le CAS (§15) |
| Import SQLite réduit exécuté | ✅ réconciliation complète sur les deux providers (§16) |
| Rapports complets | ✅ ce rapport + mise à jour du rapport initial |
| Baseline 1499 verte | ✅ avant **et** après (§2.3, §22.1) |
| Aucun changement de production | ✅ (§22) |

### 23.2 Pourquoi le spike reste `INCOMPLETE`

Les verdicts `P4-1 SPIKE = INCOMPLETE` et `P4-1 = NOT READY FOR ADR` sont **conservés**, car :

- l'**installation Windows native reproductible** n'est validée pour **aucun** des deux providers ;
- le test **depuis un autre poste réel** n'est pas réalisé (tout est resté sur `127.0.0.1`) ;
- les critères ADR de la roadmap ne sont donc **pas tous satisfaits**.

### 23.3 Prochaine action minimale

Le lot Windows reste **bloqué sur une décision utilisateur** (rapport initial §19.1 corrigé) :
l'installation native de SQL Server Express a échoué (`0x84C4000E`) **avant** production d'un journal
exploitable. **Aucune désinstallation n'est recommandée.** Les voies possibles, sans préconisation :

1. réaliser l'installation Windows sur une **machine de laboratoire dédiée**, pour l'un ou l'autre
   provider, en la chronométrant et en la documentant ;
2. rejouer l'installation sur ce poste **en collectant un journal** (`/LOG`), afin d'établir une cause
   racine — aujourd'hui **non déterminée** ;
3. traiter d'abord le volet réseau (accès depuis un autre poste, pare-feu), indépendant du blocage.

---

## 24. Confirmations finales

- ✅ **aucun provider n'est choisi ; aucun provider n'est éliminé** ;
- ✅ aucun score global n'est attribué ;
- ✅ aucune donnée utilisateur n'a été lue, écrite ou exposée ;
- ✅ l'instance PostgreSQL native du poste n'a **jamais** été contactée ;
- ✅ aucun code, test, migration, snapshot EF, UI, `MMV.sln` ou workflow de production n'est modifié ;
- ✅ aucun composant Windows n'a été installé ni désinstallé ;
- ✅ aucun port n'a été ouvert au réseau public ;
- ✅ aucun secret n'apparaît dans le dépôt ni dans ce rapport ;
- ✅ conteneurs, bases et secrets temporaires **supprimés** ;
- ✅ **aucun commit, aucun push**.
