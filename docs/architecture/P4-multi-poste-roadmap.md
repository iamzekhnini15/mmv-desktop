# Roadmap P4 — MMV multi-poste (base centrale client/serveur)

> **P4 est consacré au multi-poste.** Cette roadmap est créée **après la clôture de P3** sur `main`
> (`3ed883634dae83399d3e93dbc1dc65ffaabdf243`, P3 fusionné `89ccc918…`, baseline 1499 tests verte).
>
> **Le dépôt réel prime toujours sur ce document.**
>
> - **PostgreSQL** (candidat principal) et **SQL Server Express** (candidat secondaire), **issus de l'ADR P3-0B**,
>   restent des **candidats**. **Aucun choix définitif n'est encore pris**, **aucun gagnant** n'est désigné,
>   **aucune notation** n'est attribuée, et **licences, limites et coûts seront vérifiés dans les sources officielles
>   pendant P4-1** — jamais affirmés de mémoire.
> - **SQLite sur dossier réseau est interdit** comme base de production multi-poste.
> - **SQLite local reste utile** pour dev / test / démo / mono-poste, et n'est pas retiré.
> - **P4 n'est ni une phase SaaS ni multi-tenant** : un seul magasin, une seule base centrale, plusieurs postes.
>
> Sources : [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md) ·
> [Audit P4-0](../implementation/P4-0-multi-poste-initial-audit-report.md) (constats et numéros de section cités ci-dessous).

---

## 1. Étape officielle figée

Au départ, **une seule** étape est officiellement figée :

### **P4-0 — Audit initial multi-poste et roadmap** ✅ *étape officielle **close** (commit + CI vertes)*

- **Objectif** : vérifier la clôture P3, créer la branche `p4-multi-poste`, établir la baseline, inventorier
  l'ensemble du couplage SQLite et des garanties P3, produire l'audit et cette roadmap, puis **activer la CI sur
  les branches `p4*`** (le filtre `push` ne les couvrait pas).
- **Dépendances** : P3 clos et fusionné sur `main`.
- **Changements autorisés** : création de branche + 2 documents Markdown + **ajout du seul motif `p4*`** au filtre
  `push` de `.github/workflows/ci.yml`.
- **Changements interdits** : code, tests, migrations, snapshot EF, UI, packages, provider, toute autre partie du
  workflow.
- **Statut** : **définitivement close.** La réserve « sous réserve du commit et de sa CI » est **levée** :

  | Élément | Valeur |
  |---|---|
  | Commit de contenu | **`8cf09193758c8d74a7f98ca01beb7e22e61ff66c`** — `chore(P4-0): establish multi-poste audit and CI` |
  | Fichiers | `M .github/workflows/ci.yml` (+1 ligne `'p4*'`) · `A` roadmap P4 · `A` rapport d'audit P4-0 (3 fichiers, 1075 insertions) |
  | CI | run **`30045502517`** · `event=push` · `headSha` **exact** · `status=completed` · **`conclusion=success`** |
  | URL | <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30045502517> |
  | Jobs | Restore ✅ · Build ✅ · Test ✅ · Audit vulnérabilités ✅ · Contrôle EF ✅ — aucun job obligatoire en échec |
  | Tests | **1499** (Domain 649 · Application 611 · App 239), 0 échec, 0 ignoré — **reproduits en CI** |
  | Contenu | **aucun** code, test, migration, snapshot EF, UI, package ni provider |

- **Sortie obtenue** : `P4-BRANCH-CREATION = GO`, `P4-0-CI = GO`, `P4 = STARTED`, `P4-1 = READY`.

**Toutes les étapes ci-dessous sont dérivées de l'audit réel** mais **ne sont pas définitives** : leur découpage
final dépend des résultats du spike (P4-1) et de l'ADR (P4-2).

---

## 2. Étapes suivantes

### P4-1 — Spike comparatif des providers — **étape officielle engagée, non close**

- **Statut courant** : **`P4-1 SPIKE = INCOMPLETE`** — le spike est **engagé**. **Lots A, B et C exécutés**
  (Lot C = **`PASS`** sur les deux providers) ; **Lot D requis** avant clôture (voir *P4-1 — Lot D* ci-dessous).
  **Aucun provider n'est choisi ni éliminé.** État consolidé en §6.
- **Statut initial — historique (2026-07-23)** : `P4-1 = READY` — prochaine étape officielle de P4, prête à
  commencer et **non commencée** ; aucun spike démarré, aucun serveur de base de données installé, aucun package
  provider ajouté. **Cet énoncé est périmé** ; il est conservé uniquement comme trace de l'état de départ.
- **Objectif** : mesurer PostgreSQL et SQL Server Express sur les points réellement bloquants identifiés par l'audit,
  **sans désigner de gagnant à l'avance**.
- **Dépendances** : P4-0 close (commit + CI verte).
- **Domaines probables** : branche/prototype jetable hors `src` de production, ou `MMV.Infrastructure` isolé.
- **Autorisé** : prototype non fusionné, scripts de mesure, documentation de résultats.
- **Interdit** : **toute modification définitive du produit** ; modifier les primitives P3 ; **toute décision de
  provider avant la fin du spike** ; migrer des données réelles.
- **Points de mesure imposés par l'audit** :
  - **précision monétaire** — les montants sont aujourd'hui `HasColumnType("REAL")` (audit §17) : **expérimenter**
    un mapping monétaire exact ; **ni le type ni la précision ne sont choisis d'avance** ;
  - **index uniques filtrés** — `HasFilter("\"IsCurrent\" = 1")` et le filtre LowStock actif (audit §11) ;
  - **upsert anti-doublon** — `INSERT … ON CONFLICT DO NOTHING` (audit §15) ; SQL Server n'a pas `ON CONFLICT` ;
  - **codes d'erreur** — classification hors `SqliteException` (audit §16) ;
  - **transactions/isolation**, deadlocks, timeouts (audit §14) ;
  - **installation Windows en magasin**, sauvegarde/restauration, limites produit
    (**à vérifier auprès de la documentation officielle pendant le spike**).
- **Tests attendus** : prototype connecté + preuves reproductibles par critère.
- **Sortie** : matrice de preuves complétée, sans score inventé. **Aucun provider n'est choisi par P4-1 elle-même** —
  la décision appartient à l'ADR P4-2.

#### P4-1 — état après Lots A et B *(exécution locale ; enregistrement **historique**)*

> **Sous-section historique.** Elle enregistre l'état **au terme des Lots A et B**, avant l'exécution du Lot C.
> Ses deux manques déclarés (installation Windows native non validée ; accès depuis un autre poste non testé)
> ont **depuis été couverts par le Lot C**, désormais **`PASS`** — et non plus `READY`. Les blocs d'état
> ci-dessous sont **conservés tels quels** comme trace des Lots A et B ; l'**état courant** est celui de la
> sous-section *« Lot C … exécuté »* et du tableau §6.

État factuel enregistrant l'exécution **locale** des Lots A et B du spike. Il **ne clôt pas** P4-1 et
**ne choisit aucun provider**.

- **Lot A** — spike comparatif exécuté **localement** (E1 → E10 sur les deux providers, conteneurs jetables).
- **Lot B** — complétion technique exécutée **localement** (E11 → E17 sur les deux providers).
- Résultats **E1 à E17** disponibles ([rapport Lot A](../implementation/P4-1-provider-comparison-spike-report.md) ·
  [rapport Lot B](../implementation/P4-1-technical-completion-lot-b-report.md)).
- Inventaire des primitives **réconcilié à 14** (doublon conceptuel de l'audit §15 retiré).
- **13 / 14** primitives exercées **via les implémentations de production** ; **1 en échec mesuré**
  (`NotificationRepository.TryCreateActiveLowStockAsync` — `SqliteParameter`, échec **identique** sur les deux providers).
- Import **SQLite réduit** exécuté et réconcilié (prototype 10 tables, sans volumétrie réelle).
- **Deadlocks, pertes de connexion, retries bornés et RCSI** exécutés et mesurés.
- **Aucun provider choisi ; aucun provider éliminé.**
- P4-1 **reste incomplète** : installation Windows native non validée (les deux providers) ; accès **depuis un
  autre poste** non testé.
- P4-2 (ADR) **reste bloquée**.
- **Lot C — installation Windows native et tests réseau réels — requis** avant clôture de P4-1.

```
P4-1 LOT A  = GO LOCAL
P4-1 LOT B  = GO LOCAL
P4-1 SPIKE  = INCOMPLETE
P4-2 ADR    = BLOCKED
P4-1 LOT C  = READY            [HISTORIQUE — Lot C exécuté depuis : PASS]
```

**Enregistrement — commit et CI (Lots A + B).** Les preuves des Lots A et B sont enregistrées par le commit
**`0039e5c410f85937a3eb70f29f2dd5545f285f1d`** — `test(P4-1): capture provider spike evidence` (32 fichiers : la
présente roadmap modifiée + les 2 rapports P4-1 + `spikes/P4.ProviderComparison/**`, **aucun** changement de
production). CI **verte sur le SHA exact** : run
[`30128760083`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30128760083) · `event=push` ·
`status=completed` · **`conclusion=success`** · job *Restore / Build / Test / Scan* ✅ (Restore · Build ·
**1499 tests** · audit vulnérabilités · contrôle EF — tous verts). Le harness est validé **localement** sans
provider (45 tests explicitement *Skipped*, aucun secret) ; la CI valide la **solution MMV**, pas le harness situé
hors `MMV.sln`.

```
P4-1 LOT A     = GO
P4-1 LOT B     = GO
P4-1 LOT B CI  = GO
P4-1 SPIKE     = INCOMPLETE
P4-2 ADR       = BLOCKED
P4-1 LOT C     = READY            [HISTORIQUE — Lot C exécuté depuis : PASS]
```

#### P4-1 — Lot C : porte de disponibilité du laboratoire (revalidation 2026-07-25) *(historique)*

Une première tentative de porte de disponibilité (2026-07-25, matin) avait conclu `BLOCKED — LAB ENVIRONMENT
NOT AVAILABLE` (aucune seconde machine Windows, pas de capacité de snapshot — voir
[rapport Lot C](../implementation/P4-1-windows-network-lot-c-report.md) §1–§14, conservé comme trace
historique).

Un laboratoire conforme a ensuite été provisionné et **vérifié indépendamment** (`VBoxManage`, lecture seule).
**État constaté au 2026-07-25**, avant toute installation de provider :
deux VM VirtualBox distinctes **`MMV-SRV`** et **`MMV-CLI`** (Windows 11 Enterprise Evaluation, MAC uniques),
adaptateur 1 NAT + adaptateur 2 réseau interne **`MMV-LAB`** sur les deux, IP privées `10.20.30.10` (SRV,
confirmée en direct) et `10.20.30.20` (CLI, attestée par snapshot), sans passerelle ni DNS sur l'adaptateur
interne, profil réseau **Privé**, **Pare-feu Windows actif** avec règles **ICMP restreintes** à l'autre VM et à
Ethernet 2, **ping bidirectionnel réussi**, snapshot **`01-LAB-READY`** courant sur les deux VM (rollback
natif VirtualBox confirmé), **aucun provider de base de données installé à cette date**, **aucune donnée de
production ou personnelle**. Détail complet, y compris ce qui a été vérifié en direct par cette session versus attesté par
l'opérateur via la description du snapshot : [rapport Lot C](../implementation/P4-1-windows-network-lot-c-report.md) §15–§16.

**Au 2026-07-25, ceci fermait la porte de disponibilité, pas le Lot C lui-même** : aucune installation de
PostgreSQL ni de SQL Server Express n'avait alors eu lieu sur ce laboratoire. *(Constat historique — les deux
installations ont depuis été réalisées ; voir la sous-section suivante.)*

```
P4-1 LOT C READINESS = READY TO EXECUTE
P4-1 LOT C            = NOT STARTED      [HISTORIQUE — remplacé ci-dessous par : PASS]
P4-1 SPIKE            = INCOMPLETE
P4-2 ADR              = BLOCKED
```

#### P4-1 — Lot C : installation Windows native et réseau réel — **exécuté et `PASS`** (2026-07-26)

Le Lot C a été **exécuté** dans le laboratoire ci-dessus, **piste par piste**, à partir de l'état serveur propre
`01-LAB-READY`. Les tests ont été menés **par l'opérateur à l'intérieur des VM invitées** et sont corroborés
indépendamment (lecture seule `VBoxManage`) par leurs snapshots de validation. Détail complet, preuves et
limites : [rapport Lot C](../implementation/P4-1-windows-network-lot-c-report.md) §17–§20.

- **Piste PostgreSQL 17.10 — `PASS`.** Installation Windows native sur `MMV-SRV`, service `postgresql-x64-17`
  en démarrage **Automatique**, écoute `10.20.30.10:5432`, **pare-feu maintenu actif** avec règle restreinte
  (TCP · 5432 · Ethernet 2 · `10.20.30.10` ↔ `10.20.30.20`), accès distant **refusé avant / accepté après** la
  règle, connexion et CRUD distants depuis `MMV-CLI`, redémarrage Windows, arrêt/reprise de service,
  `pg_dump` / `pg_restore`, données persistantes. Snapshot **`02-POSTGRESQL-PASS`**.
- **Piste SQL Server 2022 Express — `PASS`.** Installation Windows native **réussie depuis un socle propre**
  (l'échec `0x84C4000E` observé antérieurement sur la station de dev **ne s'est pas reproduit** ; sa cause
  racine reste **non élucidée**), instance `SQLEXPRESS`, service `MSSQL$SQLEXPRESS` **Automatique**, TCP **fixe
  1433** (Browser et UDP 1434 non requis), **pare-feu maintenu actif** avec règle restreinte équivalente, accès
  distant **refusé avant / accepté après** la règle, authentification SQL et CRUD distants complets depuis
  `MMV-CLI`, redémarrage Windows, arrêt/reprise de service, `BACKUP` / `RESTORE` (+ `CHECKSUM`, `VERIFYONLY`),
  données persistantes. Snapshot **`03-SQLSERVER-PASS`**.
- **Comparabilité des deux pistes** : **les deux pistes de serveur de base de données ont été exécutées sur
  `MMV-SRV` à partir du même état serveur propre `01-LAB-READY`** — sur `MMV-SRV`, `03-SQLSERVER-PASS` est un
  **frère** de `02-POSTGRESQL-PASS` sous `01-LAB-READY`, la piste SQL Server est donc bien partie d'un état
  **sans PostgreSQL installé**. Sur `MMV-CLI`, `03-SQLSERVER-PASS` est en revanche **enchaîné après**
  `02-POSTGRESQL-PASS` : **la topologie de snapshots n'est pas identique entre les deux VM** et ne l'est pas
  revendiquée. Cette différence **n'affecte pas la comparaison des installations natives de serveur de base de
  données**, `MMV-CLI` ayant un rôle **purement client** et n'ayant hébergé **aucun serveur de base de
  données** ([rapport Lot C](../implementation/P4-1-windows-network-lot-c-report.md) §19.12).
- **Ce que le Lot C prouve** : installation Windows, connectivité TCP, authentification brute, CRUD,
  redémarrage, reprise de service, sauvegarde/restauration — **pour les deux providers**.
- **Ce que le Lot C ne prouve pas** : l'**intégration applicative MMV**. Aucun code de `MMV.App`,
  `MMV.Application`, `MMV.Infrastructure` ni de l'`OpticDbContext` n'a été exercé contre ces serveurs ;
  `mmv_app`, `mmv_p4_lab` et `network_test` sont des objets SQL créés à la main.
- **Aucun provider n'est choisi ni éliminé** : deux `PASS` symétriques ne constituent pas une décision.
- **P4-1 reste incomplète** et **P4-2 (ADR) reste bloquée** — voir *Lot D* ci-dessous.

```
P4-1 LOT A                  = GO
P4-1 LOT B                  = GO
P4-1 LOT C READINESS        = READY TO EXECUTE
P4-1 LOT C POSTGRESQL TRACK = PASS
P4-1 LOT C SQL SERVER TRACK = PASS
P4-1 LOT C                  = PASS
P4-1 SPIKE                  = INCOMPLETE
P4-2 ADR                    = BLOCKED
```

#### P4-1 — Lot D : complétion de la portabilité applicative — **prochain lot bloquant**

Le socle Windows/réseau étant couvert par le Lot C, le **seul** critère du spike P4-1 encore non satisfait est
la **portabilité applicative**. Le **Lot D** en est le **prochain lot technique bloquant** : il est **requis**
avant la clôture de P4-1, donc avant l'ADR P4-2. Définition détaillée :
[rapport Lot C](../implementation/P4-1-windows-network-lot-c-report.md) §21.

- **Objectif** : résoudre `NotificationRepository.TryCreateActiveLowStockAsync`, **seule primitive en échec**
  (**1 / 14**) après les Lots A et B — échec **identique** sur PostgreSQL et sur SQL Server.
- **Périmètre** :
  1. **Résoudre** `NotificationRepository.TryCreateActiveLowStockAsync`
     ([NotificationRepository.cs:90-131](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L90-L131)).
  2. **Supprimer la construction d'objets `SqliteParameter`** avant l'exécution spécifique au provider.
  3. **Préserver la garantie d'unicité de l'alerte LowStock active** — décision prise **par la base en une
     seule instruction**, jamais par un `check-then-act` applicatif.
  4. **Exercer l'implémentation de production** contre **PostgreSQL** *et* **SQL Server**.
  5. **Prouver le comportement en concurrence** sur les **deux** providers.
  6. **Ne sélectionner aucun provider pendant le Lot D.**
- **Interdit** : choisir ou éliminer un provider ; créer l'ADR P4-2 ; affaiblir ou déplacer la garantie
  d'unicité vers l'application ; déclarer la primitive portée sans son test **sur chaque** provider.
- **Sortie attendue — cumulative, pas un nouveau départ** : le Lot D ne rejoue pas les preuves déjà acquises aux
  Lots A + B — les **13 primitives déjà exercées via leurs implémentations de production restent acquises
  telles quelles**. Il ajoute uniquement la **14ᵉ** (`TryCreateActiveLowStockAsync`, résolue, exercée via son
  implémentation de production sur les deux candidats, preuve de concurrence **propre à cette primitive** à
  l'appui). **Résultat cumulé attendu : 14 / 14** primitives exercées via leurs implémentations de production
  sur les deux candidats (13 acquises + 1 résolue par le Lot D) — **condition d'entrée** de l'ADR P4-2.

```
P4-1 LOT D = REQUIRED — NEXT BLOCKING LOT
P4-1 SPIKE = INCOMPLETE
P4-2 ADR   = BLOCKED
```

---

> **Statut des étapes P4-2 à P4-12.** Elles constituent la **trajectoire officielle de travail issue de l'audit
> P4-0**, et non des implémentations déjà engagées. Leur **ordre est fondé sur les dépendances observées** dans le
> dépôt (composition provider → primitives et erreurs → schéma → exploitation → preuves). Leur **découpage est
> réévaluable après P4-1 et P4-2**, à la lumière du provider retenu. En revanche, **aucune étape ne peut ignorer les
> critères de sortie multi-poste** listés en §4 : un découpage différent reste admissible, un critère de sortie non
> satisfait ne l'est pas.

### P4-2 — ADR de choix du provider

- **Objectif** : trancher PostgreSQL vs SQL Server Express **sur les preuves du spike**.
- **Dépendances** : P4-1.
- **Autorisé** : un ADR documentaire.
- **Interdit** : implémentation, packages, migrations.
- **Critères** (audit §26) : intégrité transactionnelle · EF Core · portabilité des primitives P3 · concurrence réelle ·
  installation en magasin · maintenance · backup/restore · diagnostic · sécurité · coût/licence **vérifiés** ·
  Windows · migration SQLite · limites opérationnelles · compétences de support.
- **Sortie (GO)** : ADR accepté, provider désigné, décision justifiée par des preuves — **pas** par la mémoire.

### P4-3 — Fondation Infrastructure multi-provider

- **Objectif** : centraliser la sélection du provider et rendre `MMV.Infrastructure` multi-provider **sans** toucher
  Domain/Application.
- **Dépendances** : P4-2.
- **Fichiers probables** : `App.axaml.cs` (composition root), `MMV.Infrastructure/DependencyInjection.cs` (aujourd'hui
  **inerte**), `OpticDbContext.OnConfiguring`, `OpticDbContextFactory` — soit **4 sites `UseSqlite`** à unifier (audit §8).
- **Autorisé** : abstraction de composition, configuration, package provider.
- **Interdit** : toute référence provider dans Domain/Application (audit §27) ; changement de règle métier.
- **Tests attendus** : SQLite conserve **1499** verts ; sélection provider testée.
- **Sortie (GO)** : SQLite inchangé en dev/test, provider serveur sélectionnable par configuration.

### P4-4 — Traduction des erreurs et portage des primitives atomiques

- **Objectif** : porter les **14 primitives** de l'inventaire **réconcilié** sans perte de garantie, et doter le
  provider serveur de sa **propre classification d'erreurs**.
  *(L'audit §15 recensait **15** primitives ; un **doublon conceptuel** a été retiré au Lot A. Le total en
  vigueur est **14** ; « 15 » est un comptage **historique**.)*
- **Dépendances** : P4-3.
- **État réel réconcilié (Lots A + B)** : **13 des 14 primitives ont une surface d'API favorable au portage**
  (ports Domain + API EF `ExecuteUpdateAsync`/`ExecuteDeleteAsync`/transaction/index) et disposent d'une
  **preuve de spike exercée contre les deux candidats réels** (PostgreSQL et SQL Server, conteneurs jetables)
  via leurs implémentations de production. **Cette preuve de spike n'équivaut ni à une validation de mise en
  production finale, ni à la re-preuve exigée par la chaîne de portage P4-4 sur le provider retenu** — cette
  dernière reste requise, y compris contre l'installation Windows native validée au Lot C, contre laquelle
  **aucune primitive applicative n'a encore été exercée** (§20). **Une primitive contient du SQL
  et des paramètres explicitement SQLite** et reste **en échec sur les deux providers**
  (`NotificationRepository.TryCreateActiveLowStockAsync` : `INSERT … ON CONFLICT DO NOTHING` + `SqliteParameter`)
  — sa résolution constitue le **Lot D** de P4-1, préalable à P4-2.
  Les dépendances restant à prouver sont explicitées en colonne 5 de l'audit §15 : index filtré, type monétaire,
  niveau d'isolation, lignes affectées, classification d'erreur, transaction et retry, comportement de concurrence.
- **Fichiers probables** : `PersistenceErrorMapper` (aujourd'hui **entièrement couplé** à `SqliteException` — audit §16),
  `NotificationRepository.TryCreateActiveLowStockAsync`, `EfTransactionRunner` (isolation, retry — audit §14).
- **Autorisé** : classification provider-spécifique en Infrastructure ; upsert provider ; isolation explicite.
- **Interdit** : modifier `PersistenceException`/`PersistenceErrorCategory` (Domain, neutres) ; introduire un retry
  qui rejouerait un effet **non idempotent** ; **déclarer une primitive portée sans son test serveur**.
- **Tests attendus** : chaque primitive re-prouvée sur le provider retenu.
- **Sortie (GO)** : les **14** primitives (inventaire réconcilié) prouvées sur le provider retenu — dont les 13 à
  API favorable **effectivement validées par test** et l'upsert SQLite (`TryCreateActiveLowStockAsync`) réécrit et
  prouvé — + erreurs traduites.

### P4-5 — Schéma et migrations serveur

- **Objectif** : produire une **chaîne de migrations serveur distincte** (baseline propre).
- **Dépendances** : P4-3.
- **Justification (audit §12)** : la chaîne SQLite actuelle (14 migrations) **n'est pas portable** — `GLOB`,
  `lower(trim())`, tables `CHECK`-abort, ADD COLUMN NOT NULL + défaut transitoire, table-rebuild `AlterColumn`,
  snapshot porteur d'annotations SQLite.
- **Forme non figée** : P4-0 **ne choisit pas** entre projet de migrations distinct, contexte de migrations distinct,
  ou toute autre solution supportée et testée par le provider — cette décision suit P4-2.
- **Autorisé** : nouvelle chaîne serveur, **type monétaire exact décidé après le spike** (ni type ni précision
  retenus à ce stade), index filtrés provider.
- **Interdit** : casser les migrations SQLite existantes (**les 14 migrations historiques doivent rester utilisables
  pour SQLite local** en dev/test/démo/mono-poste).
- **Tests attendus** : migration d'une base serveur **neuve** reproductible ; index/contraintes vérifiés **physiquement**.
- **Sortie (GO)** : base serveur neuve créée par migrations, contraintes prouvées.

### P4-6 — Application des migrations en multi-poste

- **Objectif** : **sérialiser** l'application des migrations et garder la compatibilité version app ↔ schéma.
- **Dépendances** : P4-5.
- **Justification (audit §9)** : `SqliteDatabaseManager.PrepareDatabase` applique `Migrate()` **par poste** — inadapté
  à une base centrale (plusieurs postes ne doivent pas migrer concurremment).
- **Autorisé** : verrou/poste désigné/phase de maintenance ; garde de version bloquant un client trop ancien.
- **Interdit** : migration automatique concurrente par tous les postes.
- **Tests attendus** : migration pendant qu'un poste est connecté ; client obsolète bloqué proprement.

### P4-7 — Procédure/outil de migration SQLite → serveur

- **Objectif** : transférer une base SQLite existante, ou **échouer sûrement**.
- **Dépendances** : P4-5.
- **Justification (audit §22)** : **aucun outil n'existe**.
- **Autorisé** : outil d'import, ordre FK, preuve de complétude (comptes avant/après), rollback.
- **Interdit** : import partiel silencieux ; perte d'identifiants ; import de `__EFMigrationsHistory` vers une base neuve.
- **Tests attendus** : import vérifié (lignes, FK, unicité, montants **exacts**), rejouabilité définie, rollback testé.

### P4-8 — Configuration, secrets et déploiement de la base centrale

- **Objectif** : chaîne de connexion serveur sécurisée + installation de la base au magasin.
- **Dépendances** : P4-2.
- **Justification (audit §19/§20)** : configuration **100 % variables d'environnement**, **aucun `appsettings.json`**,
  **aucun** artefact de déploiement (ni Docker, ni script d'installation, ni service, ni health check).
- **Autorisé** : configuration par poste, secret hors dépôt, procédure d'installation, health check, retry de connexion.
- **Interdit** : **committer un secret ou une chaîne de connexion complète**.
- **Tests attendus** : démarrage sans configuration = échec clair ; indisponibilité serveur gérée.

### P4-9 — Sauvegarde et restauration centrales

- **Objectif** : sauvegarde serveur planifiée, rétention, et **restauration testée**.
- **Dépendances** : P4-8.
- **Justification (audit §21)** : la sauvegarde actuelle est une **copie de fichier SQLite** ; pas de rétention,
  pas d'automatisation, pas de documentation opérateur.
- **Tests attendus** : exercice complet sauvegarde → restauration → vérification d'intégrité.

### P4-10 — Tests multi-processus et résilience

- **Objectif** : prouver les garanties en **concurrence réelle**.
- **Dépendances** : P4-4, P4-5.
- **Justification (audit §23/§24)** : tout le corpus concurrence actuel est **même processus / multi-connexion SQLite** ;
  **0** test multi-processus, **0** test provider serveur, **0** test de panne réseau.
- **Scénarios minimaux** : deux postes vendant le dernier produit · avançant la même commande · générant la fiche
  courante · validant le QC · réglant le même solde · créant la même alerte LowStock · créant le même login normalisé ·
  suppression fournisseur vs création produit · migration pendant qu'un poste travaille · perte de connexion en
  transaction · reconnexion après redémarrage · timeout · deadlock · retry · sauvegarde puis restauration.

### P4-11 — Recette plusieurs postes

- **Objectif** : recette fonctionnelle réelle sur plusieurs postes d'un même magasin.
- **Dépendances** : P4-6 … P4-10.
- **Tests attendus** : parcours métier complets simultanés (comptoir + atelier + back-office).

### P4-12 — Audit final P4

- **Objectif** : vérifier **tous** les critères de sortie et prononcer le verdict V1 multi-poste.
- **Dépendances** : toutes les étapes retenues.
- **Sortie** : `P4 = CLOSED` / `V1 MULTI-POSTE = GO` **uniquement** si §4 est intégralement satisfait.

> **Note de découpage** : P4-4 fusionne « portage des primitives » et « traduction des erreurs » car l'audit montre
> qu'ils portent sur les **mêmes fichiers** et la **même frontière** (Infrastructure), et qu'un portage sans
> classification d'erreurs laisserait les courses non traduites. P4-6 a été **subdivisé** hors de P4-5 car
> l'application des migrations en multi-poste est un problème **opérationnel** distinct du schéma lui-même
> (audit §9/§12). Aucune étape n'a été créée artificiellement pour remplir la liste.

---

## 3. Hors périmètre P4 (explicite)

SaaS · multi-tenant · plusieurs magasins dans une même installation · cloud obligatoire · API publique ·
application web · abonnement · facturation légale · TVA · devis · paiement en ligne · notifications email/push ·
**redesign UI global** · rendez-vous · dashboard fonctionnel · recherche globale · exports métier ·
nouvelles fonctionnalités commerciales.

Une **modification UI minimale** pourra être autorisée **plus tard** uniquement si indispensable à :
la configuration de connexion · l'affichage d'une indisponibilité serveur · une opération de migration clairement
décidée. Elle **n'est pas** incluse dans P4-0.

---

## 4. Critères de sortie de P4 (vérifiables)

| # | Critère | Vérification |
|---|---|---|
| 1 | Provider serveur choisi **par ADR** | ADR accepté, fondé sur le spike |
| 2 | Base neuve installable | procédure reproductible |
| 3 | Application connectable **depuis plusieurs postes** | recette réelle |
| 4 | Migrations serveur reproductibles | base neuve migrée deux fois à l'identique |
| 5 | **Primitives P3 portées sans perte de garantie** | **14** primitives re-prouvées — inventaire **réconcilié** (l'audit §15 en recensait 15 : comptage **historique**, doublon conceptuel retiré au Lot A) |
| 6 | Erreurs provider traduites | classification testée hors `SqliteException` (audit §16) |
| 7 | Données SQLite migrables **ou procédure d'échec sûre** | import prouvé (lignes, FK, montants exacts) |
| 8 | Tests multi-processus verts | scénarios §P4-10 |
| 9 | Perte réseau testée | panne simulée |
| 10 | Reconnexion testée | redémarrage serveur |
| 11 | Sauvegarde testée | exercice complet |
| 12 | Restauration testée | intégrité vérifiée après restauration |
| 13 | Documentation opérateur | procédure d'exploitation |
| 14 | **Secrets non committés** | revue du dépôt |
| 15 | Build et tests verts | CI verte |
| 16 | **Aucune vulnérabilité** | `dotnet list package --vulnerable` |
| 17 | **Domain/Application provider-neutres** | tests d'architecture (audit §27) |
| 18 | Recette complète sur plusieurs postes | validation métier |

> **La V1 multi-poste ne sera pas déclarée prête** tant que **tous** les critères officiels retenus ne sont pas
> satisfaits.

---

## 5. Atouts et points durs hérités de P3 (résumé de l'audit)

**Atouts (déjà acquis)**

- **Domain et Application sont déjà provider-neutres** au niveau de leurs dépendances et de leur code — la frontière
  hexagonale attendue est respectée en amont.
- **13 des 14 primitives atomiques** (inventaire **réconcilié** ; l'audit §15 lisait « 14 sur 15 » avant retrait
  du doublon conceptuel — comptage **historique**) utilisent des **ports ou des API EF provider-neutres au niveau
  du code** (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, transaction, index) : leur surface d'API est **favorable
  au portage**, et elles disposent d'une **preuve de spike exercée contre les deux candidats réels** (PostgreSQL
  et SQL Server, conteneurs jetables, Lots A + B) via leurs implémentations de production. **Cette preuve de
  spike n'équivaut ni à une validation de mise en production finale, ni à la re-preuve exigée par la chaîne de
  portage P4-4** sur le provider serveur retenu — celle-ci reste requise, y compris contre l'installation
  Windows native validée au Lot C, contre laquelle **aucune primitive applicative n'a encore été exercée**
  (§20). **Une primitive contient du SQL et des paramètres explicitement SQLite** et reste **en échec sur
  les deux candidats** (point dur n° 3 ci-dessous) — objet du **Lot D**.
- Les garanties métier sont exprimées comme **décisions prises par la base en une instruction** (CAS, update/delete
  conditionnel), pas comme des `check-then-act` applicatifs.
- **Aucun secret** dans le dépôt ; défaut de configuration **sûr** (Production, sans seed).

**Points durs (travail réel de P4)**

1. **Montants en `REAL`** (flottant) — risque de **précision monétaire** sur serveur *(le plus grave)*.
2. **`PersistenceErrorMapper` entièrement couplé** à `SqliteException` et ses codes.
3. **Upsert `ON CONFLICT DO NOTHING`** + `SqliteParameter` — **seule primitive dont le SQL et les paramètres sont
   explicitement SQLite**.
4. **Deux index uniques filtrés** au SQL littéral (`"IsCurrent" = 1`, filtre LowStock actif).
5. **Chaîne de migrations non portable** → chaîne serveur distincte requise.
6. **Cycle de vie mono-processus** (`Migrate()` par poste, backup par copie de fichier, adoption PRAGMA).
7. **Aucun déploiement, aucun outil d'import, aucun test multi-processus ou serveur.**

---

## 6. État officiel de P4

| Phase | État |
|---|---|
| **P4-0** — audit initial et roadmap | **CLOSE** — commit `8cf0919`, CI `30045502517` verte sur le SHA exact |
| **P4-1** — spike comparatif des providers | **ENGAGÉE, NON CLOSE** — Lots A, B et C exécutés (**Lot C = `PASS`**, les deux providers) ; **Lot D requis** avant clôture |
| **P4-2 … P4-12** | trajectoire officielle issue de l'audit, **découpage réévaluable** après P4-1 et P4-2 |

**État courant en vigueur** *(les blocs d'état antérieurs marqués `[HISTORIQUE]` plus haut sont remplacés par
celui-ci)* :

```
P4-BRANCH-CREATION          = GO
P4-0-CI                     = GO
P4                          = STARTED
P4-1 LOT A                  = GO
P4-1 LOT B                  = GO
P4-1 LOT C READINESS        = READY TO EXECUTE
P4-1 LOT C POSTGRESQL TRACK = PASS
P4-1 LOT C SQL SERVER TRACK = PASS
P4-1 LOT C                  = PASS
P4-1 LOT D                  = REQUIRED — NEXT BLOCKING LOT
P4-1 SPIKE                  = INCOMPLETE
P4-2 ADR                    = BLOCKED
```

**Aucun provider n'est choisi ni éliminé.** Le spike P4-1 est **engagé et incomplet** : Lots A et B exécutés
localement, **Lot C exécuté et `PASS`** (installation Windows native, réseau réel multi-poste, sauvegarde et
restauration — pour les **deux** candidats), **Lot D restant** (portabilité applicative :
`NotificationRepository.TryCreateActiveLowStockAsync`). Le Lot C **ne prouve pas** l'intégration applicative
MMV. **P4-2 reste bloquée.** La CI couvre les branches `p4*` : tout commit de la phase P4 est vérifié à
distance (restore, build, 1499 tests, audit de vulnérabilités, contrôle EF des changements de modèle non
matérialisés).
