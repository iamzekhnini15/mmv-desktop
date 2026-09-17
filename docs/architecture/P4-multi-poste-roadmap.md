# Roadmap P4 — MMV multi-poste (base centrale client/serveur)

> **P4 est consacré au multi-poste.** Cette roadmap est créée **après la clôture de P3** sur `main`
> (`3ed883634dae83399d3e93dbc1dc65ffaabdf243`, P3 fusionné `89ccc918…`, baseline 1499 tests verte).
>
> **Le dépôt réel prime toujours sur ce document.**
>
> - **PostgreSQL** et **SQL Server Express**, **issus de l'ADR P3-0B**, ont été mesurés par le spike P4-1
>   (Lots A → D). L'ADR [ADR-PROD-DB-002](adr-prod-db-002-server-database-provider-selection.md) est
>   **`ACCEPTÉE`** — **PostgreSQL est officiellement retenu pour la V1**. **SQL Server Express n'est pas
>   éliminé** — rejeté pour V1, réexaminable selon l'ADR §20. **Aucune notation pondérée** n'est attribuée.
>   **Licences, limites et cycles de vie ont été vérifiés dans les sources officielles** (ADR §24) — jamais
>   affirmés de mémoire. Les étiquettes « candidat principal / secondaire » de P3-0B sont **antérieures à toute
>   mesure** et **n'ont aucune valeur probante** (ADR §2.2). **P4-3 est implémentée, enregistrée et validée
>   par CI** — commit **`b312a6c`**, CI [**`30823399148`**](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30823399148)
>   **verte sur le SHA exact**, **1529 tests** — donc **`P4-3 = CLOSE`**. **`P4-4` est depuis passée en
>   `IN PROGRESS`** — quatre sous-lots livrés et commités (`P4-4A0`, `P4-4A1`, `P4-4B0`, `P4-4B1`), voir §6 ;
>   **la V1 multi-poste n'est toujours pas déclarée `GO`**.
> - **SQLite sur dossier réseau est interdit** comme base de production multi-poste.
> - **SQLite local reste utile** pour dev / test / démo / mono-poste, et n'est pas retiré.
> - **P4 n'est ni une phase SaaS ni multi-tenant** : un seul magasin, une seule base centrale, plusieurs postes.
>
> Sources : [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md) ·
> [**ADR-PROD-DB-002**](adr-prod-db-002-server-database-provider-selection.md) *(statut `ACCEPTÉ`)* ·
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

### P4-1 — Spike comparatif des providers — **COMPLÈTE et ENREGISTRÉE** (commit + CI verte sur le SHA exact)

- **Statut courant** : **`P4-1 SPIKE = COMPLETE`, enregistrement inclus.** Les **Lots A, B, C et D** sont
  exécutés. Le Lot D **passe** sur SQLite, PostgreSQL et SQL Server : **14 / 14 primitives** exercées via leurs
  implémentations de production, et **20 / 20 tours de concurrence conformes par provider serveur**.
  L'**enregistrement officiel** du Lot D est **acquis** — commit **`e8d054631815c0410de4bb2f209ce3892588e75c`**
  (`fix(P4-1): complete active LowStock portability`, **5 fichiers**), CI
  [`30215445242`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30215445242) · `event=push` ·
  `headSha` **exact** · **`conclusion=success`** · **1505 tests** (Domain 649 · Application 617 · App 239).
  **La « réserve d'enregistrement » antérieure est donc levée.** **`P4-2 ADR = ACCEPTED`** : le draft de l'ADR
  [ADR-PROD-DB-002](adr-prod-db-002-server-database-provider-selection.md) a été **enregistré par `37812c7`**,
  CI **`30656580004`** verte sur le SHA exact, et l'ADR est désormais **acceptée par le présent commit** —
  **PostgreSQL est officiellement retenu**, **SQL Server Express n'est pas éliminé**. P4-3, alors ouverte en
  `READY — NOT STARTED`, a **depuis été implémentée et close** (commit `b312a6c`, CI `30823399148`) ; **P4-4**
  est désormais **`IN PROGRESS`** (sous-lots `A0`, `A1`, `B0`, `B1` livrés). État consolidé en §6.
- **Statut antérieur — historique (2026-07-26, avant le commit du Lot D)** : « techniquement complète ;
  enregistrement du Lot D **en attente de commit et de CI** ». **Cet énoncé est périmé** — le commit et la CI
  existent (ci-dessus) ; il est conservé uniquement comme trace.
- **Statut antérieur — historique (jusqu'au 2026-07-26)** : `P4-1 SPIKE = INCOMPLETE`, `P4-2 ADR = BLOCKED`,
  Lot D requis. **Cet énoncé est périmé** : il est remplacé par le statut courant ci-dessus et conservé
  uniquement comme trace. Les blocs d'état antérieurs marqués `[HISTORIQUE]` plus bas suivent la même règle —
  leur **contenu évidentiel est préservé**, seule leur **valeur courante** est retirée.
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

> **Sous-section historique.** Elle enregistre l'état **au terme des Lots A et B**, avant l'exécution des
> Lots C et D. Ses deux manques déclarés (installation Windows native non validée ; accès depuis un autre poste
> non testé) ont **depuis été couverts par le Lot C**, désormais **`PASS`** — et non plus `READY`. Le
> **13 / 14** et la primitive **en échec** énoncés ci-dessous ont **depuis été résolus par le Lot D**
> (**14 / 14**, `PASS`), et les mentions « P4-1 reste incomplète » / « P4-2 reste bloquée » sont **périmées**.
> Les blocs d'état ci-dessous sont **conservés tels quels** comme trace des Lots A et B ; l'**état courant**
> est celui de la sous-section *« Lot D … exécuté et `PASS` »* et du tableau §6.

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
P4-1 SPIKE  = INCOMPLETE       [HISTORIQUE — Lot D exécuté depuis : SPIKE = COMPLETE]
P4-2 ADR    = BLOCKED          [HISTORIQUE — remplacé depuis par : ACCEPTED — CLOSE]
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
P4-1 SPIKE     = INCOMPLETE       [HISTORIQUE — Lot D exécuté depuis : SPIKE = COMPLETE]
P4-2 ADR       = BLOCKED          [HISTORIQUE — remplacé depuis par : ACCEPTED — CLOSE]
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
P4-1 SPIKE            = INCOMPLETE       [HISTORIQUE — Lot D exécuté depuis : SPIKE = COMPLETE]
P4-2 ADR              = BLOCKED          [HISTORIQUE — remplacé depuis par : ACCEPTED — CLOSE]
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
- **P4-1 restait incomplète** et **P4-2 (ADR) restait bloquée** à la clôture du Lot C — voir *Lot D* ci-dessous.
  *(Constat historique : le Lot D a depuis été exécuté et est `PASS`.)*

```
P4-1 LOT A                  = GO
P4-1 LOT B                  = GO
P4-1 LOT C READINESS        = READY TO EXECUTE
P4-1 LOT C POSTGRESQL TRACK = PASS
P4-1 LOT C SQL SERVER TRACK = PASS
P4-1 LOT C                  = PASS
P4-1 SPIKE                  = INCOMPLETE       [HISTORIQUE — Lot D exécuté depuis : SPIKE = COMPLETE]
P4-2 ADR                    = BLOCKED          [HISTORIQUE — remplacé depuis par : ACCEPTED — CLOSE]
```

#### P4-1 — Lot D : complétion de la portabilité applicative — **exécuté et `PASS`** (2026-07-26)

Le Lot D a **résolu le dernier échec applicatif mesuré du spike**,
`NotificationRepository.TryCreateActiveLowStockAsync`. Preuves détaillées, limites et verdict :
[rapport Lot D](../implementation/P4-1-lot-d-active-low-stock-portability-report.md).

- **Cause racine mesurée** : sept `SqliteParameter` construits **en dur** étaient rejetés par les collections
  de paramètres Npgsql et SQL Server **avant toute exécution SQL** — le moteur ne voyait jamais la requête. Le
  problème était donc le **type d'objet paramètre**, pas seulement le dialecte.
- **Paramètres** : désormais fabriqués par le **provider ADO.NET courant**, via
  `Database.GetDbConnection().CreateCommand().CreateParameter()`. **Aucun paquet provider n'est ajouté** à
  `MMV.Infrastructure`, et **aucune référence provider n'atteint Domain ni Application**.
- **SQLite et PostgreSQL** : conservent la forme `INSERT … ON CONFLICT DO NOTHING` — texte SQLite **inchangé
  au caractère près**.
- **SQL Server** : forme **à instruction unique** `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK,
  HOLDLOCK))`. Un provider non reconnu **lève** plutôt que de deviner un dialecte.
- **Aucun `check-then-act` applicatif** : la décision « créer ou refuser » reste prise **par la base**, en une
  seule instruction. La garantie d'unicité n'est ni déplacée vers l'application ni affaiblie.
- **Implémentation de production exercée contre SQLite, PostgreSQL et SQL Server** — les tests appellent la
  méthode elle-même, sans SQL recopié ni substitut.
- **Compatibilité SQLite** : 6 nouveaux tests ciblés passent ; les **43** tests Notifications préexistants
  passent **sans modification**.
- **Concurrence** : **20 / 20 tours conformes sur PostgreSQL** et **20 / 20 sur SQL Server** — 0 anomalie,
  0 erreur non gérée, 0 interblocage observé, **au plus une alerte active**.
- **Résultat cumulé : 14 / 14 primitives** exercées via leurs implémentations de production sur les deux
  candidats (13 acquises aux Lots A + B, 1 résolue par le Lot D).
- **Aucune migration, aucun changement de modèle EF, aucun paquet provider ajouté.**
- **Aucun provider n'est choisi ni éliminé** : la correction est **neutre** entre les deux candidats.

**Portée des preuves — à ne pas confondre :**

- **preuves locales** : les 4 tests serveur E18 et la mesure de concurrence dépendent de conteneurs
  PostgreSQL/SQL Server locaux ;
- **preuves de solution** : `MMV.sln` totalise **1505 tests** (Domain 649 · Application 617 · App 239), build
  0 avertissement / 0 erreur, aucune vulnérabilité, aucun changement de modèle EF en attente ;
- le harness `spikes/P4.ProviderComparison` est **hors `MMV.sln`** : **la CI normale ne rejouera pas E18** tant
  que `.github/workflows/**` n'est pas explicitement modifié — ce que le Lot D **ne fait pas**.

**Constats mesurés encore ouverts — entrées obligatoires de l'ADR P4-2 :**

1. **`DateTime.Now` / `Kind = Local`** : **refusé** par le chemin **EF** PostgreSQL mesuré ; par le chemin SQL
   brut, l'heure locale est stockée **comme si elle était UTC**. Sur SQL Server, le chemin SQL brut montre un
   **arrondi de type `datetime`** là où le chemin EF mesuré a conservé une précision supérieure. Mesuré **sans
   assertion**, **non résolu**, et **non introduit par le Lot D**.
2. **Filtres d'index booléens PostgreSQL** : adaptation encore appliquée **dans le spike uniquement**.
3. **Mapping monétaire `REAL`** : perte de valeur mesurée sur **les deux** providers.

Ces trois constats doivent être **évalués** par l'ADR P4-2 et leur **implémentation affectée** aux phases P4
ultérieures appropriées — vraisemblablement **P4-3** (composition/configuration), **P4-4** (comportement des
primitives à l'exécution, conversions de valeurs) et/ou **P4-5** (types de colonnes, index, schéma, migrations)
selon la décision retenue. **Aucune correction de code n'appartient à P4-2 elle-même**, qui est une décision
**documentaire**.

**Ce que le Lot D ne prouve pas** : la **compatibilité applicative complète** de MMV sur l'un ou l'autre
serveur. Il prouve **une** primitive ; il n'exécute ni `MMV.App`, ni les cas d'usage complets, ni l'ensemble du
modèle contre un serveur.

```
P4-1 LOT C                  = PASS
P4-1 LOT D SQLITE TRACK     = PASS
P4-1 LOT D POSTGRESQL TRACK = PASS
P4-1 LOT D SQL SERVER TRACK = PASS
P4-1 LOT D CONCURRENCY      = PASS
P4-1 PRIMITIVES             = 14 / 14
P4-1 LOT D                  = PASS
P4-1 SPIKE                  = COMPLETE
P4-2 ADR                    = READY            [HISTORIQUE — remplacé depuis par : ACCEPTED — CLOSE]
```

**Enregistrement — commit et CI (Lot D).** La **réserve d'enregistrement** qui accompagnait le verdict ci-dessus
est **levée** :

| Élément | Valeur |
|---|---|
| Commit | **`e8d054631815c0410de4bb2f209ce3892588e75c`** — `fix(P4-1): complete active LowStock portability` |
| Fichiers | **5** — `M` [NotificationRepository.cs](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs) (+87 / −24, **seul** fichier de production) · `A` tests SQLite Lot D · `A` `spikes/…/E18_ActiveLowStockPortabilityTests.cs` · `A` rapport Lot D · `M` cette roadmap |
| CI | run **`30215445242`** · `event=push` · `headSha` **exact** · `status=completed` · **`conclusion=success`** |
| URL | <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30215445242> |
| Job | *Restore / Build / Test / Scan* ✅ — Restore · Build · Test · Audit vulnérabilités · Contrôle EF, tous verts |
| Tests | **1505** (Domain **649** · Application **617** · App **239**), 0 échec, 0 ignoré — **reproduits en CI** |
| Portée | la CI a rejoué les **1505 tests de `MMV.sln`** ; elle **n'a pas** rejoué **E18** ni la mesure de concurrence serveur (harness **hors `MMV.sln`**, workflow non modifié) |

##### Mandat d'origine du Lot D *(historique — satisfait)*

> **Sous-section historique.** Elle enregistre la **définition** du Lot D telle qu'énoncée à la clôture du
> Lot C, quand il était le **prochain lot bloquant**. Son contenu est **conservé tel quel** comme trace du
> mandat ; **tous ses points ont depuis été satisfaits** — l'état courant est celui de la sous-section
> ci-dessus et du tableau §6.

Le socle Windows/réseau étant couvert par le Lot C, le **seul** critère du spike P4-1 alors non satisfait était
la **portabilité applicative**. Le **Lot D** en était le **prochain lot technique bloquant** : il était
**requis** avant la clôture de P4-1, donc avant l'ADR P4-2. Définition détaillée :
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
P4-1 LOT D = REQUIRED — NEXT BLOCKING LOT   [HISTORIQUE — Lot D exécuté depuis : PASS]
P4-1 SPIKE = INCOMPLETE                     [HISTORIQUE — remplacé depuis par : COMPLETE]
P4-2 ADR   = BLOCKED                        [HISTORIQUE — remplacé depuis par : READY]
```

---

> **Statut des étapes P4-2 à P4-12.** Elles constituent la **trajectoire officielle de travail issue de l'audit
> P4-0**, et non des implémentations déjà engagées. Leur **ordre est fondé sur les dépendances observées** dans le
> dépôt (composition provider → primitives et erreurs → schéma → exploitation → preuves). Leur **découpage est
> réévaluable après P4-1 et P4-2**, à la lumière du provider retenu. En revanche, **aucune étape ne peut ignorer les
> critères de sortie multi-poste** listés en §4 : un découpage différent reste admissible, un critère de sortie non
> satisfait ne l'est pas.

### P4-2 — ADR de choix du provider — **`ACCEPTÉE`**

- **Statut courant** : **`ACCEPTED — CLOSE`.** L'ADR
  [**ADR-PROD-DB-002**](adr-prod-db-002-server-database-provider-selection.md) — *Choix du SGBD serveur de
  production (MMV V1 multi-poste)* — a été **rédigée** sur la base `e8d0546` / CI `30215445242` verte
  (1505 tests), **enregistrée en draft par `37812c7`** (CI `30656580004` verte sur le SHA exact), puis
  **acceptée par le présent commit**.
  - **Provider retenu : PostgreSQL** (série testée **17.10**) — **décision acceptée**.
  - **Constats décisifs — deux, inchangés** (ADR §9, §11) : **D1** — **plafonds d'édition** de SQL Server Express
    (**10 Go** par base, **1 410 Mo** de buffer pool, **1 socket ou 4 cœurs**), face à un modèle MMV
    **monotone croissant par conception** (suppression physique d'ordonnance refusée inconditionnellement en
    P3, archivage logique, fiches versionnées, historique d'alertes conservé) ; la **volumétrie réelle de MMV
    reste `NOT_PROVED`** — la décision repose sur l'**asymétrie du risque de plafond d'édition**, **pas** sur
    l'affirmation que MMV dépassera 10 Go. **Non contournable par du code MMV.**
    **D2** — **identification des contraintes** par **métadonnées structurées** (`ConstraintName`) côté
    PostgreSQL contre identification **mesurée à travers le texte du message** côté SQL Server, sur un
    `PersistenceErrorMapper` qui doit être **réécrit** de toute façon. Le parsing est **techniquement
    implémentable mais moins robuste** (sensible au provider, à la langue et à la version) : la distinction
    porte sur la **qualité d'implémentation et le risque de diagnostic**, et **reste pertinente bien qu'un
    contournement existe**.
  - **Correction matérielle du 2026-07-27, à ne pas réintroduire.** Une rédaction antérieure comptait **trois**
    constats décisifs, dont l'absence de `SQL Server Agent` en Express. **Ce constat était matériellement
    inexact** : Microsoft **documente** l'automatisation des sauvegardes d'Express **sans** Agent, par
    `sp_BackupDatabases` + `sqlcmd` + **Planificateur de tâches Windows**
    ([*Schedule and automate backups of databases*](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/backup-restore/schedule-automate-backup-database),
    Microsoft Learn). L'absence d'Agent — réelle, comme l'absence de tâches planifiées et de plans de
    maintenance — est **rétrogradée en désavantage d'exploitation non décisif** (ADR §10, §12.6). Elle **ne
    fonde plus le rejet**. **MMV doit de toute façon construire, sécuriser, superviser, doter d'une rétention
    et documenter sa chaîne de sauvegarde planifiée pour l'un comme pour l'autre candidat.**
  - **Désavantages explicitement acceptés, inchangés** (ADR §16, §22.2) : PostgreSQL **refuse** `DateTime.Now`
    (`Kind = Local`) par le chemin **EF** — remédiation **obligatoire** ; réécriture du **filtre d'index
    booléen** due (SQL Server n'en exigeait aucune) ; **toute erreur avorte la transaction entière** (`25P02`),
    imposant un audit des `catch` intra-transactionnels ; trois classes d'erreur **sans `SqlState`**. **Trois
    chantiers mesurés restent ouverts** : mapping `DateTime`, filtres d'index booléens PostgreSQL, précision
    monétaire `REAL`. Les **obligations O1–O15** de l'ADR (§15) sont **inchangées**. **La compatibilité
    applicative complète reste `NOT_PROVED`** (ADR §16.5).
  - **SQL Server Express est rejeté pour V1, mais NON éliminé** (ADR §19) : son dialecte
    `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))` est **implémenté et prouvé dans le code
    de production**, la sélection se fait sur `Database.ProviderName`, et Domain/Application restent
    provider-neutres. Il **reste une option future**.
- **Ce que P4-2 n'autorise pas** : **aucun code n'est produit par P4-2** — aucun paquet provider, aucune
  migration serveur, aucune correction de `DateTime`, du mapping monétaire ou des filtres d'index. Toute
  implémentation appartient à **P4-3** et aux phases ultérieures.
- **Condition de sortie de P4-2** : revue humaine **+** commit **+** CI GitHub Actions verte sur le
  **SHA exact** du commit portant l'acceptation — **atteinte, sous réserve de la CI verte du présent commit
  d'acceptation sur son SHA exact**.
- **Statut antérieur — historique (jusqu'au 2026-07-26)** : `P4-2 ADR = READY` — « les preuves nécessaires
  existent, l'ADR n'est ni créée ni acceptée ». **Cet énoncé est périmé** : l'ADR est désormais **rédigée**
  (statut `PROPOSÉ`). Il est conservé uniquement comme trace.
- **Objectif** : trancher PostgreSQL vs SQL Server Express **sur les preuves du spike**.
- **Dépendances** : P4-1.
- **Autorisé** : un ADR documentaire.
- **Interdit** : implémentation, packages, migrations.
- **Entrées obligatoires issues du spike** : preuves E1–E18 (Lots A + B), Lot C (installation Windows native,
  réseau réel, sauvegarde/restauration), Lot D (14 / 14 primitives, concurrence 20 / 20 par provider), **plus
  les trois constats encore ouverts** : mapping `DateTime`, filtres d'index booléens PostgreSQL, précision
  monétaire `REAL`.
- **Nature exacte de P4-2** : une décision **documentaire uniquement**. Elle **évalue** l'impact de ces
  constats sur le choix du provider et **affecte leur implémentation** aux phases P4 ultérieures ; elle
  **n'implémente elle-même aucune correction**, aucun paquet, aucun schéma, aucune migration.
- **Critères** (audit §26) : intégrité transactionnelle · EF Core · portabilité des primitives P3 · concurrence réelle ·
  installation en magasin · maintenance · backup/restore · diagnostic · sécurité · coût/licence **vérifiés** ·
  Windows · migration SQLite · limites opérationnelles · compétences de support.
- **Sortie (GO)** : ADR accepté, provider désigné, décision justifiée par des preuves — **pas** par la mémoire.
  **Atteinte, sous réserve de la CI verte du commit d'acceptation courant** : le statut est désormais
  **`ACCEPTÉ`**.

### P4-3 — Fondation Infrastructure multi-provider — **`CLOSE — COMMIT + CI SUCCESS`**

- **Statut courant** : **`CLOSE — COMMIT + CI SUCCESS`.** La fondation, implémentée sur la base `724ffecc…`
  puis **validée localement**, est désormais **enregistrée et vérifiée à distance** — voir
  [rapport P4-3](../implementation/P4-3-multi-provider-foundation-report.md) §14. Sa condition de clôture
  (**revue humaine + commit + CI GitHub Actions verte sur le SHA exact**) est **satisfaite** :

  | Élément | Valeur |
  |---|---|
  | Commit | **`b312a6c901b1e620a3694a32ce0cd14e2f0fc707`** — `feat(P4-3): add multi-provider infrastructure foundation` |
  | Fichiers | **11** — 5 modifiés + 6 ajoutés |
  | CI | run **`30823399148`** · `event=push` · `headBranch=p4-multi-poste` · `headSha` **exact** · `status=completed` · **`conclusion=success`** |
  | URL | <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30823399148> |
  | Jobs | Restore ✅ · Build ✅ · Test ✅ · Audit vulnérabilités ✅ · Contrôle EF ✅ |
  | Tests | **1529** (Domain **673** · Application **617** · App **239**), **0 échec**, **0 ignoré** |
  | Build | **0 erreur, 0 avertissement** |
  | Migrations | **aucune créée** — contrôle EF vert, aucun changement de modèle |

  **`P4-3 = CLOSE`.** **`P4-4` était alors `READY — NOT STARTED` ; elle est aujourd'hui `IN PROGRESS`**
  (voir §6 et la fiche P4-4 ci-dessous). **La V1 multi-poste n'est PAS `GO`.**
  **Les obligations de l'ADR (O1–O15, §15) restent contraignantes.**
- **Portée exacte de la CI** : elle a rejoué les **1529 tests de `MMV.sln`**. Elle **n'a exécuté ni E18 ni le
  harness `spikes/P4.ProviderComparison`**, qui reste **hors `MMV.sln`** — les preuves serveur du spike
  demeurent **locales**.
- **Statut antérieur — historique (avant le commit `b312a6c`)** : « `IMPLEMENTED LOCALLY — PENDING HUMAN
  REVIEW / COMMIT / CI` » — la fondation était alors implémentée et validée **en local**, **non commitée, non
  poussée, non passée en CI, non close**. **Cet énoncé est périmé** ; il est conservé uniquement comme trace
  de la passe locale.
- **Ce qui est acquis** : le provider est **sélectionnable par configuration**
  (`MMV_DATABASE_PROVIDER`, défaut **SQLite** ; `MMV_DATABASE_CONNECTION_STRING` obligatoire pour PostgreSQL,
  secret hors dépôt) ; les **3 sites réellement multi-provider** (composition root, `OnConfiguring`, factory
  design-time) passent par un **configurateur central unique** ; le **4ᵉ site** — `DbContext` ad hoc de
  `LegacyDatabaseRecoveryService`, qui ouvre l'ancien **fichier** `mmv-optic.db` — reste **délibérément
  SQLite-only** ; le **5ᵉ site**, `DependencyInjection.AddInfrastructure` (**inerte**, sans appelant), **n'a
  pas été touché**. `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11` est ajouté **au seul** `MMV.Infrastructure`.
  Suite : **1529 verts** (1505 baseline **+ 24** nouveaux tests), 0 échec, 0 ignoré, 0 avertissement —
  d'abord mesurés localement, puis **reproduits par la CI `30823399148`** ; `has-pending-model-changes`
  **vert**, **aucune migration créée** ; **aucune vulnérabilité**.
- **Garde-fou volontaire** : sélectionner PostgreSQL **bloque explicitement le démarrage** (exception levée
  **avant** toute opération dépendante de la base, hors du `try` afin qu'aucun `catch` générique ne la
  masque). Le provider est **correctement sélectionné côté EF** ; ce qui manque est la **chaîne de
  préparation et de migrations serveur (P4-5/P4-6)**. **Aucun repli sur SQLite**, et **jamais** de migration
  SQLite exécutée contre PostgreSQL. La factory design-time reste **figée sur SQLite** (la CI exécute le
  contrôle EF sans configuration serveur).
- **Portée exacte de la clôture** : **PostgreSQL est sélectionnable par configuration**, **SQLite reste le
  défaut**, **aucun fallback** d'un provider vers l'autre n'existe, le **site legacy demeure SQLite-only**,
  **`Npgsql` n'est référencé directement que dans `MMV.Infrastructure`** et **Domain / Application restent
  provider-neutres**. **Aucune migration serveur** n'est créée et **aucun travail P4-4 ni P4-5 n'est engagé**
  par P4-3.
- **Restent ouverts, non traités par P4-3** : conversion `DateTime` (constat Lot D), mapping monétaire,
  index filtrés booléens PostgreSQL, classification d'erreurs (`PersistenceErrorMapper` toujours couplé à
  `SqliteException`, **à porter en P4-4**), isolation et retry. **Aucune compatibilité applicative complète
  n'est revendiquée.**
  *(Mise à jour P4-4C : la **classification d'erreurs** a depuis été portée par **P4-4A1** (`dc4c492`) et le
  **rollback défensif de `UnitOfWork`** par **P4-4B1** (`2976401`). `DateTime`, mapping monétaire et index
  filtrés **restent ouverts**. Cette puce décrit l'état **au terme de P4-3** et est conservée comme trace.)*
- **Objectif** : centraliser la sélection du provider et rendre `MMV.Infrastructure` multi-provider **sans** toucher
  Domain/Application.
- **Dépendances** : P4-2 **acceptée** — **satisfaite**.
- **Fichiers probables** : `App.axaml.cs` (composition root), `MMV.Infrastructure/DependencyInjection.cs` (aujourd'hui
  **inerte**), `OpticDbContext.OnConfiguring`, `OpticDbContextFactory` — soit **4 sites `UseSqlite`** à unifier (audit §8).
- **Autorisé** : abstraction de composition, configuration, package provider.
- **Interdit** : toute référence provider dans Domain/Application (audit §27) ; changement de règle métier.
- **Tests attendus** : SQLite conserve **1505** tests verts de la baseline courante ; sélection provider testée.
- **Sortie (GO)** : SQLite inchangé en dev/test, provider serveur sélectionnable par configuration.

### P4-4 — Traduction des erreurs et portage des primitives atomiques — **`IN PROGRESS`**

- **Statut courant** : **`IN PROGRESS` — commencée, partiellement livrée, `NOT CLOSE`.** Sa **dépendance P4-3
  est satisfaite** (P4-3 `CLOSE`, commit `b312a6c`, CI `30823399148` verte sur le SHA exact). **Quatre
  sous-lots sont implémentés, documentés et commités** sur `p4-multi-poste` :

  | Sous-lot | Objet | Commit | CI | Rapport |
  |---|---|---|---|---|
  | **P4-4A0** | Preuve du comportement des exceptions PostgreSQL (forme structurelle Npgsql, sondes E19/E20) | **`b84c7e3`** | **`33411724068`** — SUCCESS sur le SHA exact | [rapport](../implementation/P4-4A0-npgsql-exception-shape-report.md) |
  | **P4-4A1** | Portage de la classification d'erreurs de persistance PostgreSQL (`PersistenceErrorMapper`) — **obligation ADR O5** | **`dc4c492`** | **`33436908075`** — SUCCESS sur le SHA exact | [rapport](../implementation/P4-4A1-postgresql-persistence-error-mapper-report.md) |
  | **P4-4B0** | Preuve du comportement transactionnel PostgreSQL (spike E21, `25P02`, isolation, retry) | **`d5a3656`** | **numéro non enregistré dans le dépôt** | [rapport](../implementation/P4-4B0-postgresql-transaction-behavior-report.md) |
  | **P4-4B1** | Sûreté d'annulation du rollback de `UnitOfWork.CommitAsync` | **`2976401`** | **numéro non enregistré dans le dépôt** | [rapport](../implementation/P4-4B1-unitofwork-defensive-rollback-report.md) |

  **Deux de ces sous-lots modifient du code de production** (`PersistenceErrorMapper` en A1, `UnitOfWork` en
  B1) ; A0 et B0 sont des **passes de mesure** dont les sondes restent **hors `MMV.sln`**. La suite de tests
  est passée de **1529** à **1569** (+36 en A1, +4 en B1). **Les numéros de CI de `d5a3656` et `2976401` ne
  figurent nulle part dans le dépôt** : ils sont déclarés **`NOT FOUND IN REPOSITORY`** et **ne doivent pas
  être supposés verts** tant qu'ils ne sont pas enregistrés ici.

- **Restant à faire dans P4-4** (aucune de ces lignes n'est engagée à ce jour) :
  - **O2 — décision de mapping monétaire** (remplacement de `HasColumnType("REAL")`) : type et précision
    **non tranchés**. *L'ADR-PROD-DB-002 §15 affecte O2 à **P4-5** ; l'obligation reste ouverte et sa phase
    d'exécution n'est pas modifiée par le présent réalignement.*
  - **O3 — adaptation PostgreSQL des index uniques filtrés** (`"IsCurrent" = 1`, filtre LowStock actif).
    *Affectée à **P4-5** par l'ADR §15 ; ouverte.*
  - **O4 — stratégie `DateTime`** (PostgreSQL refuse `Kind = Local` par le chemin EF) : conversion de valeur
    (UTC + horloge injectable, ADR-005) **ou** type de colonne — **non tranché**. *L'ADR §15 l'affecte à
    **P4-4 et/ou P4-5** : cette alternative n'est **pas** arbitrée par le présent lot documentaire.*
  - **Validation PostgreSQL au runtime** — dont la **re-preuve des 14 primitives** sur le provider retenu :
    **impossible tant que le schéma serveur n'est pas disponible** (P4-5). Le rapport P4-4B0 §15 l'établit
    explicitement (`BLOCKED_BY_P4_5_SCHEMA`). **P4-4 ne peut donc pas être déclarée `CLOSE` en l'état** ;
    l'arbitrage de cette inversion de dépendance P4-4 ↔ P4-5 appartient à l'architecte et **n'est pas rendu
    par le présent lot**.
- **Statut antérieur — historique (jusqu'au commit `b84c7e3`)** : « `READY — NOT STARTED` — prochaine étape
  officielle de P4, prête à commencer et non commencée, aucune implémentation engagée ». **Cet énoncé est
  périmé** : il était déjà contredit par le dépôt au moment de l'audit de reprise
  ([MMV-PROJECT-RECOVERY-AUDIT](../reports/MMV-PROJECT-RECOVERY-AUDIT.md)) et n'est conservé que comme trace.
  Sa correction est l'objet du lot **P4-4C**
  ([rapport](../implementation/P4-4C-state-reconciliation-report.md)).
- **Objectif** : porter les **14 primitives** de l'inventaire **réconcilié** sans perte de garantie, et doter le
  provider serveur de sa **propre classification d'erreurs**.
  *(L'audit §15 recensait **15** primitives ; un **doublon conceptuel** a été retiré au Lot A. Le total en
  vigueur est **14** ; « 15 » est un comptage **historique**.)*
- **Dépendances** : P4-3.
- **État réel réconcilié (après Lots A à D)** : les **14 primitives** disposent désormais d'une **preuve de
  spike exercée contre les deux candidats réels** (PostgreSQL et SQL Server, conteneurs jetables) via leurs
  implémentations de production. Les 13 primitives à surface d'API favorable au portage (ports Domain + API EF
  `ExecuteUpdateAsync`/`ExecuteDeleteAsync`/transaction/index) l'avaient été aux Lots A + B ; la **14ᵉ**,
  `NotificationRepository.TryCreateActiveLowStockAsync` — seule primitive alors **en échec sur les deux
  providers** (`INSERT … ON CONFLICT DO NOTHING` + `SqliteParameter`) — a été **résolue et prouvée au Lot D**,
  y compris en concurrence. **Cette preuve de spike n'équivaut ni à une validation de mise en production
  finale, ni à la re-preuve exigée par la chaîne de portage P4-4 sur le provider retenu** — cette dernière
  reste requise, y compris contre l'installation Windows native validée au Lot C, contre laquelle **aucune
  primitive applicative n'a encore été exercée** (§20).
  Les dépendances restant à prouver sont explicitées en colonne 5 de l'audit §15 : index filtré, type monétaire,
  niveau d'isolation, lignes affectées, classification d'erreur, transaction et retry, comportement de concurrence.
  S'y ajoutent, **si l'ADR P4-2 les lui affecte**, la **conversion des valeurs `DateTime`** (constat Lot D,
  non résolu) et la **classification d'erreurs** propre au provider retenu.
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
- **Les 14 primitives atomiques** (inventaire **réconcilié** ; l'audit §15 lisait « 14 sur 15 » avant retrait
  du doublon conceptuel — comptage **historique**) disposent désormais d'une **preuve de spike exercée contre
  les deux candidats réels** (PostgreSQL et SQL Server, conteneurs jetables) via leurs implémentations de
  production. **13** d'entre elles utilisent des **ports ou des API EF provider-neutres au niveau du code**
  (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`, transaction, index) — surface **favorable au portage**, acquise
  aux **Lots A + B**. La **14ᵉ**, `NotificationRepository.TryCreateActiveLowStockAsync — seule primitive dont
  le SQL et les paramètres étaient explicitement SQLite** (point dur n° 3 ci-dessous), a été **corrigée et
  prouvée au Lot D**, y compris en concurrence, sur les deux candidats. **Cette preuve de spike n'équivaut ni
  à une validation de mise en production finale, ni à la re-preuve exigée par la chaîne de portage P4-4** sur
  le provider serveur retenu — celle-ci reste requise, y compris contre l'installation Windows native validée
  au Lot C, contre laquelle **aucune primitive applicative n'a encore été exercée** (§20).
- Les garanties métier sont exprimées comme **décisions prises par la base en une instruction** (CAS, update/delete
  conditionnel), pas comme des `check-then-act` applicatifs.
- **Aucun secret** dans le dépôt ; défaut de configuration **sûr** (Production, sans seed).

**Points durs (travail réel de P4)**

1. **Montants en `REAL`** (flottant) — risque de **précision monétaire** sur serveur *(le plus grave)*.
2. **`PersistenceErrorMapper` entièrement couplé** à `SqliteException` et ses codes. **✅ Porté en
   `P4-4A1`** (`dc4c492`, CI `33436908075`) : classification PostgreSQL ajoutée **sans régression SQLite**
   et **sans exposer `Npgsql`** à Domain/Application (obligation ADR **O5** traitée).
3. **Upsert `ON CONFLICT DO NOTHING`** + `SqliteParameter` — **seule primitive dont le SQL et les paramètres
   étaient explicitement SQLite**. **✅ Résolu au Lot D** (paramètres fabriqués par le provider courant,
   dialecte SQL Server à instruction unique) : prouvé sur les deux candidats, concurrence incluse. La
   **re-preuve P4-4** sur le provider retenu reste due.
4. **Deux index uniques filtrés** au SQL littéral (`"IsCurrent" = 1`, filtre LowStock actif).
5. **Chaîne de migrations non portable** → chaîne serveur distincte requise.
6. **Cycle de vie mono-processus** (`Migrate()` par poste, backup par copie de fichier, adoption PRAGMA).
7. **Aucun déploiement, aucun outil d'import, aucun test multi-processus ou serveur.**

---

## 6. État officiel de P4

| Phase | État |
|---|---|
| **P4-0** — audit initial et roadmap | **CLOSE** — commit `8cf0919`, CI `30045502517` verte sur le SHA exact |
| **P4-1** — spike comparatif des providers | **COMPLÈTE ET ENREGISTRÉE** — Lots A, B, C et D exécutés (**Lot C = `PASS`**, **Lot D = `PASS`**, les deux providers) ; **14 / 14 primitives** ; clôture enregistrée par le commit **`e8d0546`** + CI **`30215445242`** verte sur le SHA exact (**1505** tests). **Réserve d'enregistrement levée.** |
| **P4-2** — ADR de choix du provider | **`ACCEPTED — CLOSE`** — [ADR-PROD-DB-002](adr-prod-db-002-server-database-provider-selection.md) **acceptée**, **PostgreSQL officiellement retenu**, **SQL Server Express non éliminé** |
| **P4-3** — fondation Infrastructure multi-provider | **CLOSE** — commit **`b312a6c`**, CI **`30823399148`** verte sur le SHA exact, **1529** tests (Domain 673 · Application 617 · App 239). Provider sélectionnable par configuration (défaut **SQLite**), 3 sites centralisés, `Npgsql` en Infrastructure seule ; **démarrage PostgreSQL volontairement bloqué** jusqu'à P4-5/P4-6. ([rapport](../implementation/P4-3-multi-provider-foundation-report.md)) |
| **P4-4** — traduction des erreurs et portage des primitives | **`IN PROGRESS — NOT CLOSE`** — dépendance P4-3 **satisfaite** ; **étape officielle en cours**. **Livré** : `P4-4A0` (`b84c7e3`, CI `33411724068`), `P4-4A1` (`dc4c492`, CI `33436908075`), `P4-4B0` (`d5a3656`, CI **non enregistrée**), `P4-4B1` (`2976401`, CI **non enregistrée**) — **1569** tests. **Restant** : O2 mapping monétaire, O3 index filtrés PostgreSQL, O4 stratégie `DateTime`, **validation PostgreSQL au runtime** (dont re-preuve des 14 primitives) **après disponibilité du schéma serveur (P4-5)**. ([réconciliation P4-4C](../implementation/P4-4C-state-reconciliation-report.md)) |
| **P4-5 … P4-12** | trajectoire officielle issue de l'audit, **découpage réévaluable** après acceptation de P4-2 |

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
P4-1 LOT D SQLITE TRACK     = PASS
P4-1 LOT D POSTGRESQL TRACK = PASS
P4-1 LOT D SQL SERVER TRACK = PASS
P4-1 LOT D CONCURRENCY      = PASS
P4-1 PRIMITIVES             = 14 / 14
P4-1 LOT D                  = PASS
P4-1 LOT D CI               = GO
P4-1 SPIKE                  = COMPLETE
P4-2 ADR DRAFT              = COMPLETE
P4-2 ADR STATUS             = ACCEPTED
P4-2 PROVIDER DECISION      = ACCEPTED — POSTGRESQL
P4-2                        = CLOSE
P4-3 IMPLEMENTATION LOCAL   = PASS
P4-3 COMMIT                 = b312a6c
P4-3 CI                     = 30823399148 — SUCCESS
P4-3 TESTS                  = 1529
P4-3                        = CLOSE
P4-4A0                      = RECORDED — b84c7e3 — CI 33411724068 SUCCESS
P4-4A1                      = RECORDED — dc4c492 — CI 33436908075 SUCCESS
P4-4B0                      = COMMITTED — d5a3656 — CI NOT FOUND IN REPOSITORY
P4-4B1                      = COMMITTED — 2976401 — CI NOT FOUND IN REPOSITORY
P4-4 TESTS                  = 1569
P4-4 OBLIGATION O5          = DONE
P4-4 OBLIGATIONS O2, O3, O4 = OPEN
P4-4 RUNTIME VALIDATION     = BLOCKED BY P4-5 SCHEMA
P4-4                        = IN PROGRESS — NOT CLOSE
P4-5 … P4-12                = NOT STARTED
V1 MULTI-POSTE              = NOT GO
```

> **P4-3 — nature exacte de la clôture.** La fondation multi-provider a été **implémentée et validée
> localement** (build 0 avertissement, **1529** tests verts, contrôle EF vert, aucune vulnérabilité), puis
> **enregistrée par le commit `b312a6c`** et **vérifiée par la CI `30823399148`**, verte sur le **SHA exact**
> (`event=push`, `headBranch=p4-multi-poste`, `conclusion=success`, **1529** tests — Domain 673 ·
> Application 617 · App 239 — 0 échec, 0 ignoré). **P4-3 est `CLOSE`.** Cette clôture est **administrative** :
> le provider serveur est **sélectionnable dans la composition EF** ; le **démarrage applicatif sur PostgreSQL
> reste volontairement bloqué** jusqu'à la chaîne de migrations serveur (**P4-5/P4-6**). **Aucune
> compatibilité applicative complète n'est revendiquée** (`DateTime`, mapping monétaire, index filtrés,
> classification d'erreurs — `PersistenceErrorMapper` **restait alors à porter en P4-4**, ce qui a depuis
> été fait par **P4-4A1** —, isolation/retry restent ouverts). **P4-4 était `READY — NOT STARTED` à la
> clôture de P4-3 ; elle est aujourd'hui `IN PROGRESS`** (quatre sous-lots livrés) et **P4-5 n'est pas
> commencée**. **La V1 multi-poste n'est pas `GO`.**

**Réserve d'enregistrement — LEVÉE.** La clôture enregistrée de P4-1 exigeait le commit des cinq fichiers du
Lot D, son push sur `p4-multi-poste` et une CI verte sur le nouveau SHA exact. **Les trois conditions sont
remplies** : commit **`e8d054631815c0410de4bb2f209ce3892588e75c`**, CI **`30215445242`** — `event=push`,
`headSha` exact, `conclusion=success`, **1505** tests (Domain 649 · Application 617 · App 239), 0 échec,
0 ignoré, audit de vulnérabilités et contrôle EF verts.

La CI couvre les branches `p4*` : tout commit de la phase P4 est vérifié à distance (restore, build, tests de
`MMV.sln` — **1505** depuis le Lot D, **1529** depuis P4-3, audit de vulnérabilités, contrôle EF des
changements de modèle non
matérialisés). En revanche, le harness `spikes/P4.ProviderComparison` est **hors `MMV.sln`** : les résultats
**E18** (PostgreSQL, SQL Server, concurrence) sont des **preuves locales du spike** et **ne doivent pas** être
présentés comme reproduits par la CI tant que le workflow n'est pas explicitement modifié pour les exécuter.

**PostgreSQL est officiellement retenu comme SGBD serveur de production pour MMV V1 multi-poste ; SQL Server
Express n'est pas éliminé.** Le spike P4-1 est **complet et enregistré** : Lots A et B exécutés localement,
**Lot C exécuté et `PASS`** (installation Windows native, réseau réel multi-poste, sauvegarde et restauration —
pour les **deux** candidats), **Lot D exécuté et `PASS`** (portabilité applicative de
`NotificationRepository.TryCreateActiveLowStockAsync`, **14 / 14** primitives, concurrence **20 / 20** par
provider serveur). Le Lot C **ne prouve pas** l'intégration applicative MMV, et le Lot D prouve **une**
primitive : **la compatibilité applicative complète de MMV sur serveur n'est pas prouvée**.

**État de P4-2.** L'ADR [ADR-PROD-DB-002](adr-prod-db-002-server-database-provider-selection.md) est **`ACCEPTÉE`**
et **retient PostgreSQL**. Elle a **évalué** les trois constats encore ouverts — mapping `DateTime`, filtres
d'index booléens PostgreSQL, précision monétaire `REAL` — et **affecté leur implémentation** aux phases
ultérieures sous forme d'obligations (ADR §15 : O2 → P4-5, O3 → P4-5, O4 → P4-4 et/ou P4-5, O5 → P4-4).
**P4-2 n'a corrigé aucun de ces sujets** : elle est **documentaire**, et **n'engageait elle-même aucune
implémentation P4-3** — celle-ci a été **réalisée ensuite**, puis **close** (commit `b312a6c`, CI
`30823399148`). **Ces trois chantiers restent ouverts** : P4-3 ne les a pas traités.

**État consolidé, strictement.** **PostgreSQL est officiellement retenu** comme SGBD serveur de production
pour la V1 multi-poste. **SQL Server Express n'est pas éliminé** — son dialecte reste implémenté et prouvé
dans le code de production. **P4-2 est `CLOSE`.** **P4-3 est `CLOSE`** : la fondation multi-provider est
implémentée, **enregistrée par le commit `b312a6c`** et **validée par la CI `30823399148`**, verte sur le
SHA exact (**1529** tests) ([rapport](../implementation/P4-3-multi-provider-foundation-report.md)).
**P4-4 est `IN PROGRESS — NOT CLOSE`** : elle est **commencée et partiellement livrée** — `P4-4A0`
(`b84c7e3`), `P4-4A1` (`dc4c492`), `P4-4B0` (`d5a3656`) et `P4-4B1` (`2976401`) sont commités, **1569**
tests, **obligation ADR O5 traitée**. **Les CI de `d5a3656` et `2976401` ne sont pas enregistrées dans le
dépôt** et ne doivent pas être supposées vertes.
Le provider serveur est **sélectionnable dans la composition EF** ; le **démarrage applicatif sur PostgreSQL
reste volontairement bloqué** jusqu'à la chaîne de migrations serveur (P4-5/P4-6).
**Trois chantiers techniques restent ouverts** (`DateTime` — O4, filtres d'index booléens PostgreSQL — O3,
mapping monétaire `REAL` — O2), **la validation PostgreSQL au runtime — dont la re-preuve des 14 primitives —
reste due et demeure bloquée par la disponibilité du schéma serveur (P4-5)**, et **aucune compatibilité
applicative complète n'est revendiquée**. La **V1 multi-poste n'est pas déclarée `GO`** : les 18 critères de
sortie du §4 restent à satisfaire.
