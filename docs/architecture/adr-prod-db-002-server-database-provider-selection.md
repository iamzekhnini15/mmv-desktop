# ADR-PROD-DB-002 — Choix du SGBD serveur de production (MMV V1 multi-poste)

> **Statut : ACCEPTÉ (`Accepted`) — décision documentaire approuvée.**
> Cet ADR **retient** le SGBD serveur de production de MMV V1 multi-poste sur la base des preuves du spike P4-1
> (Lots A, B, C, D). Il **ne modifie aucun code source, aucun test, aucun paquet, aucune migration, aucun
> snapshot EF, aucun workflow**. Il **n'implémente rien**.
> **Le statut `ACCEPTÉ` du présent commit ne devient opérationnel qu'après sa propre CI verte sur le SHA exact
> du commit qui le porte.**
>
> Références : [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md) ·
> [roadmap P4](P4-multi-poste-roadmap.md) · [audit P4-0](../implementation/P4-0-multi-poste-initial-audit-report.md) ·
> [spike Lot A](../implementation/P4-1-provider-comparison-spike-report.md) ·
> [Lot B](../implementation/P4-1-technical-completion-lot-b-report.md) ·
> [Lot C](../implementation/P4-1-windows-network-lot-c-report.md) ·
> [Lot D](../implementation/P4-1-lot-d-active-low-stock-portability-report.md) ·
> [adr-candidates ADR-004 / ADR-005 / ADR-010](adr-candidates.md) ·
> [adr-stock-concurrency](adr-stock-concurrency.md) · [adr-sqlite-lifecycle](adr-sqlite-lifecycle.md).
>
> Date : 26 juillet 2026. **Révisé le 27 juillet 2026** — correction matérielle du raisonnement décisionnel
> avant acceptation (§9, §11, §12.6 ; sources **S18** et **S19** ajoutées ; constats décisifs réconciliés à
> **deux**). **Draft proposé enregistré le 31 juillet 2026 par `37812c7`, CI `30656580004` verte sur le SHA
> exact. Accepté le 31 juillet 2026. Le provider retenu est inchangé depuis le draft : PostgreSQL.**
> Branche : `p4-multi-poste`. Base P4-1 (spike) : `e8d054631815c0410de4bb2f209ce3892588e75c`,
> CI `30215445242` verte sur le SHA exact, **1505** tests.
> **Le dépôt réel prime toujours sur ce document.**

---

## 1. Statut

**`ACCEPTÉ` (`Accepted`).**

| Élément | Valeur |
|---|---|
| Identifiant | **ADR-PROD-DB-002** (suivant de [ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md) dans la série `PROD-DB`, seule série numérotée du dépôt) |
| Statut | **`ACCEPTÉ`** |
| Décision | **PostgreSQL** |
| Effet | PostgreSQL est officiellement retenu pour MMV V1 multi-poste ; P4-3 est débloquée |
| Portée | l'acceptation n'implémente aucun travail de P4-3 |
| Réversibilité | SQL Server Express reste non éliminé et réexaminable selon §20 |
| Autorité de la décision | P4-2, sur les preuves de P4-1 — **jamais** sur la mémoire, la préférence ou la familiarité |
| Condition d'acceptation | revue humaine **+** commit **+** CI GitHub Actions verte sur le **SHA exact** du commit — **satisfaite le 31 juillet 2026** |

> **`ACCEPTÉ` signifie que PostgreSQL est officiellement retenu pour la V1.** **SQL Server Express reste non
> éliminé** (§19). **P4-3 devient `READY — NOT STARTED`.** **La V1 multi-poste n'est pas déclarée `GO`.**

---

## 2. Contexte

### 2.1 Ce qui est déjà décidé (et n'est pas rouvert ici)

[ADR-PROD-DB-001](adr-prod-db-001-multi-poste-database-strategy.md) (**`ACCEPTÉ`**) a fixé, et cet ADR **ne
rouvre pas** :

- **MMV V1 production est multi-poste** — exigence produit, pas une option future ;
- **SQLite local reste autorisé** pour dev / test / démo / mono-poste local, et **n'est pas retiré** ;
- **SQLite sur dossier réseau partagé est interdit** comme base de production multi-poste ;
- **production multi-poste ⇒ base client/serveur** (serveur local au magasin) ;
- **cloud / API centrale / SaaS reportés** hors V1 ;
- le **choix final PostgreSQL vs SQL Server Express est différé à un spike technique dédié**.

Ce spike est **P4-1**. Il est **techniquement complet** (Lots A, B, C, D) et **enregistré** (§4). Cet ADR est la
décision que P4-1 avait explicitement laissée ouverte.

### 2.2 Vocabulaire des candidats — pourquoi il ne décide de rien

ADR-PROD-DB-001 §4 qualifiait PostgreSQL de « **candidat principal** » (option C) et SQL Server Express de
« **candidat secondaire** » (option D). **Ces étiquettes datent d'un cadrage produit antérieur à toute mesure et
n'ont aucune valeur probante ici.** Elles ont été posées sans instance testée, sans provider EF exercé, sans
installation Windows et sans vérification officielle des limites. La roadmap P4 le dit explicitement : les deux
« restent des **candidats** », « **aucun gagnant** n'est désigné », « licences, limites et coûts seront
**vérifiés dans les sources officielles**  — jamais affirmés de mémoire ».

**Cet ADR reconstruit donc la comparaison à partir des preuves, et non à partir de ces étiquettes.** Le fait que
la conclusion rejoigne l'orientation de 2026-07-08 est un **résultat**, pas une prémisse — et les §16 et §19
énoncent sans atténuation ce que ce choix coûte.

### 2.3 État réel du dépôt à la date de cet ADR (vérifié, lecture seule)

| Constat | Valeur mesurée | Fichier |
|---|---|---|
| Provider unique en production | **SQLite**, `UseSqlite` sur **4 sites** de composition | [App.axaml.cs:122](../../src/MMV.App/App.axaml.cs#L122) · [App.axaml.cs:204](../../src/MMV.App/App.axaml.cs#L204) · [OpticDbContext.cs:140](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L140) · [OpticDbContextFactory.cs:18](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L18) · [DependencyInjection.cs:37](../../src/MMV.Infrastructure/DependencyInjection.cs#L37) (inerte) |
| Aucun paquet provider serveur | `Npgsql` / `UseSqlServer` : **0 occurrence** dans `src/**` et `tests/**` | — |
| Montants monétaires | `HasColumnType("REAL")` | [SaleConfiguration.cs:28-44](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L28-L44) · `SaleItem` · `Product` · `OrderItem` |
| Index uniques **filtrés** au SQL littéral | **2** | [NotificationConfiguration.cs:64](../../src/MMV.Infrastructure/Data/Configurations/NotificationConfiguration.cs#L64) · [WorkshopSheetConfiguration.cs:62](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L62) (`"IsCurrent" = 1`) |
| Classification d'erreurs | **entièrement couplée** à `SqliteException` (codes primaires + étendus) | [PersistenceErrorMapper.cs:45-96](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs#L45-L96) |
| Upsert de l'alerte LowStock active | **portable** depuis le Lot D : paramètres fabriqués par le provider courant, dialecte choisi sur `Database.ProviderName`, provider inconnu ⇒ `NotSupportedException` | [NotificationRepository.cs:105-201](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L105-L201) |
| Sauvegarde | **copie de fichier** SQLite (+ sidecars `-wal`/`-shm`), sans rétention ni planification | [SqliteDatabaseManager.cs:155-217](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L155-L217) |
| Chaîne de migrations | **14** migrations SQLite, non portables (audit §12) | `src/MMV.Infrastructure/Migrations/**` |
| Configuration | **100 % variables d'environnement**, **aucun `appsettings.json`**, aucun secret au dépôt | [SqliteDatabasePathResolver.cs:17](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs#L17) |
| Frontières | **Domain et Application déjà provider-neutres** (audit §27) | — |

### 2.4 Le problème que cet ADR tranche

Le socle serveur est prouvé **pour les deux candidats** (Lot C) et la portabilité des **14 primitives** est
prouvée **pour les deux candidats** (Lots A+B+D). **Aucun des deux n'est disqualifié par la technique.** Le choix
ne peut donc pas être tranché par un décompte de `PASS` (§13) : il doit l'être par les **différences qui
subsistent** — **plafonds d'édition** et **diagnostic** (§9) — et par le **coût de remédiation** que chacun
impose (§16). L'**exploitation** (sauvegarde planifiée) a été examinée et **écartée comme non décisive** après
correction (§12.6).

---

## 3. Question de décision

> **Quel SGBD serveur MMV V1 multi-poste retient-il en production : PostgreSQL ou SQL Server Express ?**

Réponses admissibles : **PostgreSQL** *ou* **SQL Server Express**. Exactement une.

**Hors question** (déjà tranché par ADR-PROD-DB-001, non rouvert) : SQLite local (**conservé** pour
dev/test/démo/mono-poste), SQLite sur dossier réseau (**interdit** en production), cloud/SaaS (**reporté**).

---

## 4. Décision

### 4.1 Décision acceptée

> **MMV V1 multi-poste retient PostgreSQL comme SGBD serveur de production.**
> **Statut de cette décision : `ACCEPTÉ`.**

Version de référence retenue : **PostgreSQL 17** (série testée : **17.10**, supportée jusqu'au
**2029-11-08** — `OFFICIAL_DOCUMENTATION`, S2). Le **numéro de version mineure n'est pas figé** par cet ADR : la
politique officielle recommande de suivre la dernière mineure de la majeure.

### 4.2 Ce que cette décision **ne** dit **pas**

1. **Elle ne dit pas que MMV fonctionne aujourd'hui sur PostgreSQL.** Trois chantiers mesurés restent ouverts
   (§16) et **la compatibilité applicative complète n'est prouvée sur aucun des deux candidats** (§16.5).
2. **Elle n'élimine pas SQL Server Express** (§19). Son dialecte est **implémenté et prouvé** dans le code de
   production aujourd'hui.
3. **Elle ne choisit ni type monétaire, ni précision, ni mapping `DateTime`, ni forme de chaîne de migrations.**
   Ces choix appartiennent aux phases affectées en §15.
4. **Elle ne retire pas SQLite.**
5. **Elle n'autorise aucune ligne de code.** P4-2 est documentaire ; **P4-3 est désormais `READY — NOT
   STARTED`** ; toute implémentation appartient à P4-3 et aux phases ultérieures ; **la compatibilité
   applicative complète reste `NOT_PROVED`** (§16.5).

### 4.3 Base Git/CI de la décision

| Élément | Valeur |
|---|---|
| Branche | `p4-multi-poste` |
| `HEAD` local = `origin/p4-multi-poste` | **`e8d054631815c0410de4bb2f209ce3892588e75c`** |
| Commit | `fix(P4-1): complete active LowStock portability` — enregistrement du **Lot D** (5 fichiers) |
| CI | run **`30215445242`** · `event=push` · `headSha` **exact** · `conclusion=success` |
| Tests reproduits en CI | **1505** — Domain **649** · Application **617** · App **239** ; 0 échec, 0 ignoré |
| Audit vulnérabilités · contrôle EF | **verts** (7 projets sans vulnérabilité ; aucun changement de modèle en attente) |

---

## 5. Portée

**Inclus** : désignation du SGBD serveur de production V1 ; justification par preuves ; obligations
d'implémentation créées ; risques acceptés ; conditions de réexamen.

**Exclus, explicitement** : toute modification de `src/**`, `tests/**`, `spikes/**`, migrations, snapshot EF,
paquets, `MMV.sln`, `.github/**`, UI, contrats Domain/Application · tout ajout de paquet provider · toute
migration serveur · toute correction de `DateTime`, du mapping monétaire ou des filtres d'index PostgreSQL ·
toute modification de l'implémentation du Lot D · SaaS · multi-tenant · plusieurs magasins · licence produit ·
updater.

---

## 6. Base de preuves et niveaux de preuve

### 6.1 Niveaux employés — définitions strictes

| Niveau | Signification exacte |
|---|---|
| `EXECUTED_PRODUCTION_IMPLEMENTATION` | La **méthode de production de MMV** a été appelée elle-même contre un vrai serveur. Ni SQL recopié, ni substitut, ni réécriture de harness. |
| `EXECUTED_SPIKE` | Mesuré par le harness `spikes/P4.ProviderComparison` contre des conteneurs jetables locaux, **hors `MMV.sln`**. |
| `EXECUTED_WINDOWS_LAB` | Exécuté **par l'opérateur** dans les VM invitées du laboratoire Windows à 2 machines, corroboré par snapshot en lecture seule là où c'est enregistré. |
| `OFFICIAL_DOCUMENTATION` | Établi par une source primaire officielle citée au §24, avec date de consultation. |
| `INFERENCE` | Déduction raisonnée à partir des éléments ci-dessus. **Signalée comme telle, jamais présentée comme mesure.** |
| `NOT_PROVED` | Non mesuré, non vérifié, ou non prouvable en l'état. **Ne peut fonder aucune décision.** |

### 6.2 Ce que la CI GitHub Actions prouve — et ce qu'elle ne prouve pas

| La CI **prouve** | La CI **ne prouve pas** |
|---|---|
| build 0 avertissement / 0 erreur | **E18** — les 4 tests serveur PostgreSQL / SQL Server |
| **1505** tests de `MMV.sln` (649 · 617 · 239) | la mesure de **concurrence 20/20 par provider** |
| audit `dotnet list package --vulnerable` | tout résultat des Lots A, B (E1→E17) |
| contrôle `has-pending-model-changes` | toute preuve du Lot C (Windows/réseau) |

> **Énoncé à ne jamais assouplir.** Le harness `spikes/P4.ProviderComparison` est **délibérément hors de
> `MMV.sln`** — c'est le seul projet référençant les providers serveur. **La CI normale ne rejoue donc pas
> E18**, et le Lot D n'a **pas** modifié `.github/workflows/**`. **Il ne doit jamais être affirmé qu'une CI
> verte a rejoué les tests serveur.** Les 6 tests SQLite du Lot D, eux, **sont** dans `MMV.sln` et **sont**
> rejoués en CI — ils sont la part `+6` du passage 1499 → 1505.

### 6.3 Attribution des preuves manuelles du Lot C

Les tests du Lot C ont été **exécutés par l'opérateur à l'intérieur des VM invitées**, hors d'atteinte de la
session d'assistance. Celle-ci n'a exécuté que des commandes **en lecture** (`VBoxManage snapshot list`,
`gh run view`, `git`). Les résultats sont **rapportés par l'opérateur** et **corroborés indépendamment** par la
lecture des snapshots de validation (`02-POSTGRESQL-PASS`, `03-SQLSERVER-PASS`, descriptions lues directement).
**Ce n'est pas une intégration applicative MMV** (§6.5).

### 6.4 Preuves projet — enregistrement exact

**Lots A et B — E1 → E17, les deux providers** (`EXECUTED_SPIKE`) :

- **E1 → E10** (Lot A) et **E11 → E17** (Lot B) exécutés sur conteneurs jetables, exposés **sur la boucle
  locale uniquement**, versions et éditions relevées **par requête serveur** : PostgreSQL **17.10** ;
  SQL Server **16.0.4265.3**, **Express Edition (64-bit)**, `EngineEdition = 4`.
- Inventaire des primitives **réconcilié à 14** — un **doublon conceptuel** de l'audit §15 (« Numérotation —
  table `DocumentSequences` », sans port ni implémentation ni mécanisme) a été retiré au Lot A. **Le total en
  vigueur est 14 ; « 15 » est un comptage historique.**
- **13 / 14 primitives exercées via leurs implémentations de production** après le Lot B ; **1 en échec
  mesuré** — `NotificationRepository.TryCreateActiveLowStockAsync`, échec **identique** sur les deux providers.
- **Deadlock croisé provoqué** : `40P01` (PostgreSQL, 933 ms) / `1205` (SQL Server, 1 295 ms), victime unique,
  aucun effet partiel des deux côtés.
- **Perte de connexion en transaction** : aucune écriture partielle ne survit ; **purge du pool obligatoire des
  deux côtés** (sans elle, la 1re reconnexion échoue instantanément).
- **Retry borné** (harness uniquement — **le produit reste sans retry**) : reprise en **2** tentatives
  (PostgreSQL) contre **5** (SQL Server, budget entier), avec un **épuisement complet du budget lors d'une
  exécution antérieure** côté SQL Server ; code `10054` distinct de `10061`.
- **RCSI** mesuré **OFF et ON** sur SQL Server ; **Express est livré RCSI = OFF**.
- **Import SQLite réduit** exécuté et **réconcilié** sur les deux providers : **10 tables**, 1 à 3 lignes,
  identifiants conservés, montants exacts, booléens/enums/`NULL` préservés, séquences resynchronisées, donnée
  invalide **refusée** avec rollback propre, `__EFMigrationsHistory` **jamais importé**. **Aucune base MMV
  utilisateur n'a été lue.** Trois obstacles découverts : données de référence `HasData` présentes **des deux
  côtés** (collision de PK), `IDENTITY_INSERT` impossible sur une PK naturelle, `DateTimeKind` non préservé côté
  SQL Server.
- Enregistrement : commits **`0039e5c`** (CI `30128760083`) et **`f9df67a`** (CI `30129393484`), vertes sur SHA
  exacts, baseline **1499** reproduite.

**Lot C — installation Windows native et réseau réel, les deux providers** (`EXECUTED_WINDOWS_LAB`) :

- Laboratoire : `MMV-SRV` `10.20.30.10` / `MMV-CLI` `10.20.30.20`, réseau **interne** VirtualBox `MMV-LAB`,
  **pare-feu Windows maintenu actif** avec règles restreintes (protocole, port, interface `Ethernet 2`,
  adresses locale et distante), **aucune désactivation globale**.
- **Piste PostgreSQL 17.10 — `PASS`** : service `postgresql-x64-17` **Automatique**, écoute
  `10.20.30.10:5432`, accès distant **refusé avant / accepté après** la règle, CRUD distant, redémarrage
  Windows avec retour automatique du service, arrêt/reprise, `pg_dump` / `pg_restore`, données persistantes.
  Snapshot `02-POSTGRESQL-PASS`.
- **Piste SQL Server 2022 Express — `PASS`** : installation **réussie depuis un socle propre**, instance
  `SQLEXPRESS`, service `MSSQL$SQLEXPRESS` **Automatique**, **TCP fixe 1433** (Browser et UDP 1434 non requis),
  règle restreinte équivalente, accès distant **refusé avant / accepté après**, authentification SQL et CRUD
  distants complets, redémarrage Windows, arrêt/reprise, `BACKUP` / `RESTORE` (+ `CHECKSUM`, `VERIFYONLY`),
  données persistantes. Snapshot `03-SQLSERVER-PASS`.
- **Comparabilité** : **les deux pistes de serveur ont été exécutées sur `MMV-SRV` à partir du même état propre
  `01-LAB-READY`** — sur `MMV-SRV`, `03-SQLSERVER-PASS` est un **frère** de `02-POSTGRESQL-PASS`. Sur
  `MMV-CLI`, il est **enchaîné après** : **la topologie de snapshots n'est pas identique entre les deux VM** et
  ne l'est pas revendiquée ; `MMV-CLI` a tenu un rôle **purement client** et n'a hébergé **aucun serveur**.
- L'échec d'installation `0x84C4000E` observé antérieurement sur la station de développement (socle non propre)
  **ne s'est pas reproduit** ; sa **cause racine reste non élucidée**.
- Enregistrement : commit **`6f63460`**, CI `30211361647` verte.
- **Le Lot C ne prouve pas l'intégration applicative MMV** : `mmv_app`, `mmv_p4_lab` et `network_test` sont des
  objets SQL créés à la main ; **aucun code** de `MMV.App`, `MMV.Application`, `MMV.Infrastructure` ni de
  `OpticDbContext` n'a été exercé contre ces serveurs. Le raccourci `db_owner` employé est un **artefact de
  laboratoire jetable**, **pas** un modèle de permissions de production.

**Lot D — portabilité applicative de la 14ᵉ primitive**
(`EXECUTED_PRODUCTION_IMPLEMENTATION` + `EXECUTED_SPIKE`) :

- **Cause racine mesurée** : sept `SqliteParameter` construits **en dur** étaient rejetés **à l'ajout dans la
  collection de commande** du provider monté — donc **avant** toute soumission au moteur. Le problème était le
  **type d'objet paramètre**, pas seulement le dialecte. `InvalidCastException` **identique** des deux côtés :
  **cet échec ne départageait aucun candidat, et sa correction non plus.**
- **Paramètres** : désormais fabriqués par le **provider ADO.NET courant**
  (`Database.GetDbConnection().CreateCommand().CreateParameter()`). **Aucun paquet provider ajouté** —
  `MMV.Infrastructure.csproj` **inchangé** ; seul type manipulé : `System.Data.Common.DbParameter`. **Aucune
  référence provider n'atteint Domain ni Application.**
- **SQLite et PostgreSQL** : `INSERT … ON CONFLICT DO NOTHING` — texte SQLite **inchangé au caractère près**.
- **SQL Server** : forme **à instruction unique**
  `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))`. Provider inconnu ⇒ **`NotSupportedException`
  explicite**, jamais un dialecte deviné.
- **Aucun `check-then-act` applicatif** : test d'existence et insertion dans **une seule instruction**, **une
  seule transaction implicite** ; l'index unique filtré reste le dernier rempart. Vérifié **par interception**
  sur SQLite : **exactement 1** commande, commençant par `INSERT`, **sans aucun `SELECT`**.
- **6 tests SQLite** ciblés passent ; les **43** tests Notifications préexistants passent **sans
  modification** — y compris le test P3-8 verrouillant la propagation de la violation NOT NULL, préservé parce
  que la valeur est affectée **même lorsqu'elle est nulle**.
- **Implémentation de production exercée** contre **SQLite, PostgreSQL et SQL Server** (E18, 4 tests) :
  1re = `true` · doublon = `false` · 1 ligne / 1 active · après résolution, nouvel épisode = `true` ·
  2 lignes / 1 active.
- **Concurrence : 20 / 20 tours conformes sur PostgreSQL et 20 / 20 sur SQL Server** — 0 anomalie, 0 erreur non
  gérée, **0 interblocage observé**, **au plus une alerte active**.
- **Résultat cumulé : 14 / 14 primitives** exercées via leurs implémentations de production sur les deux
  candidats (13 acquises aux Lots A+B, **sans être rejouées**, + 1 résolue par le Lot D).
- **Aucune migration, aucun changement de modèle EF, aucun paquet ajouté.**
- **E18 reste une preuve locale, hors CI normale** (§6.2).

### 6.5 Ce qu'aucune preuve n'établit — `NOT_PROVED`

| Élément | Niveau | Pourquoi |
|---|---|---|
| **Compatibilité applicative complète de MMV** sur l'un ou l'autre serveur | `NOT_PROVED` | ni `MMV.App`, ni les cas d'usage complets, ni l'ensemble du modèle n'ont été exécutés contre un serveur |
| Primitives applicatives contre l'**installation Windows native** du Lot C | `NOT_PROVED` | le Lot C n'a exercé que du SQL brut ; E18 tourne sur conteneurs |
| **Volumétrie réelle de MMV** vs plafond **10 Go** d'Express | `NOT_PROVED` | aucune mesure de volumétrie n'a été faite |
| Import à **volumétrie réelle**, durée, reprise d'un import interrompu | `NOT_PROVED` | prototype réduit (10 tables, 1–3 lignes) |
| **Performance, montée en charge**, coût comparé de `UPDLOCK, HOLDLOCK` vs `ON CONFLICT` | `NOT_PROVED` | hors périmètre du spike ; explicitement non jugé |
| **Absence d'interblocage en charge** | `NOT_PROVED` | 20 tours = mesure **bornée**, rapportée comme telle |
| Cause racine de l'échec d'installation `0x84C4000E` | `NOT_PROVED` | aucun journal exploitable ; non reproduit sur socle propre |
| **Compétences d'équipe / support** sur l'un ou l'autre SGBD | `NOT_PROVED` | aucune preuve au dépôt. **Ce critère est exclu de la décision** (§14, critère 18) |
| Modèle de permissions applicatif de production | `NOT_PROVED` | `db_owner` était un raccourci de laboratoire |

---

## 7. Matrice comparative des candidats

Niveaux : `EPI` = `EXECUTED_PRODUCTION_IMPLEMENTATION` · `ES` = `EXECUTED_SPIKE` ·
`EWL` = `EXECUTED_WINDOWS_LAB` · `OD` = `OFFICIAL_DOCUMENTATION` · `INF` = `INFERENCE` · `NP` = `NOT_PROVED`.

| # | Critère | PostgreSQL 17.10 | SQL Server 2022 Express | Niveau | Écart décisionnel |
|---|---|---|---|---|---|
| 1 | **Intégrité et primitives atomiques** | **14 / 14** via implémentations de production | **14 / 14** via implémentations de production | `EPI` | **Égalité** |
| 2 | **Compatibilité EF Core** | modèle réel ✅ avec **1** adaptation (filtre booléen) ; ❌ sans | modèle réel ✅ **sans aucune adaptation** ; 21 tables / 51 index / 20 FK des deux côtés | `ES` | léger avantage **SQL Server** |
| 3 | **Preuves de spike sur implémentations de production** | E11 + E18 ✅ | E11 + E18 ✅ | `EPI` | **Égalité** |
| 4 | **Concurrence et verrouillage** | 20/20 · MVCC : **lecture ne bloque jamais l'écriture, écriture jamais la lecture** · `read committed` par défaut · **transaction avortée après toute erreur (`25P02`)** | 20/20 · `ReadCommitted` · **RCSI OFF par défaut ⇒ lecteurs bloqués** (5 022 ms → 9 ms avec RCSI ON) · **transaction réutilisable après erreur** | `EPI`+`ES`+`OD` | **partagé** (§12.1) |
| 5 | **Index uniques filtrés / partiels** | partiels supportés, prédicat `WHERE`, **`UNIQUE` sur sous-ensemble** ; `WHERE success` (booléen nu) documenté | filtrés supportés, applicables « aux index avec la propriété `UNIQUE` » ; `[IsCurrent]=(1)` accepté tel quel | `ES`+`OD` | léger avantage **SQL Server** (aucune réécriture) |
| 6 | **Stockage monétaire exact** | `REAL` = `real` → **999 999,99 relu 1 000 000** ; `numeric` **exact**, jusqu'à 1000 de précision déclarable | `REAL` = `real(24,0)` → **idem** ; `decimal/numeric` **précision et échelle fixes**, précision max **38** | `ES`+`OD` | **Égalité — non décisif** (§12.2) |
| 7 | **Comportement `DateTime` et remédiation requise** | **`DateTime.Now` (`Kind = Local`) REFUSÉ par le chemin EF** ; en SQL brut, heure locale **stockée comme si UTC** (2 h d'écart) | chemin EF a conservé la précision ; **chemin SQL brut arrondi** (`datetime` : increments `.000`/`.003`/`.007`) | `ES`+`OD` | **avantage SQL Server** — remédiation PostgreSQL **obligatoire** (§16.1) |
| 8 | **Classification d'erreurs et diagnostic** | **`ConstraintName` structuré** (+ `TableName`, `ColumnName`) ; `23505`/`23503`/`23502`/`3D000`/`57014` ; **3 cas sans `SqlState`** | identification de contrainte **mesurée via le texte du message**, position variable selon le code — **parsing possible mais plus fragile** ; `2601`/`2627`/`547`/`515`/`-2`/`10061`/`4060` ; annulation `Number=0` | `ES` | **DÉCISIF — PostgreSQL** (**D2**, §11.2) |
| 9 | **Installation Windows en magasin** | `PASS` socle propre ; écoute **5432 par défaut** ; installeur graphique officiellement référencé (hébergé par EDB) | `PASS` socle propre ; **instance nommée ⇒ ports dynamiques**, port fixe **à configurer** pour le pare-feu ; échec `0x84C4000E` sur socle non propre, cause **non élucidée** | `EWL`+`OD` | avantage **PostgreSQL** (modéré) |
| 10 | **Fonctionnement TCP multi-poste** | `PASS` — refusé avant / accepté après règle, CRUD distant, pare-feu maintenu actif | `PASS` — idem, CRUD distant complet | `EWL` | **Égalité** |
| 11 | **Sauvegarde et restauration** | `pg_dump -F c` / `pg_restore` ✅ · dump **internement cohérent** et **ne bloque pas** les autres opérations · purge du pool requise | `BACKUP`/`RESTORE` ✅ (+ `CHECKSUM`, `VERIFYONLY`) · `SINGLE_USER` requis · **compression : Non · sauvegarde chiffrée : Non · log shipping : Non** en Express | `ES`+`EWL`+`OD` | avantage **PostgreSQL** (modéré) |
| 12 | **Automatisation d'exploitation** | aucune fonctionnalité retirée par édition ; planification par ordonnanceur **externe au moteur** | **SQL Server Agent : Non** — ni tâches planifiées ni plans de maintenance · Database mail / Performance data collector / Standard performance reports / SQL Profiler : Non — **en Express**. **Mais Microsoft documente l'automatisation des sauvegardes d'Express** par `sp_BackupDatabases` + `sqlcmd` + **Planificateur de tâches Windows** | `OD` (S1, S18) | **NON DÉCISIF** — désavantage d'exploitation **réel** mais capacité **documentée par l'éditeur** ; obligation **symétrique** pour MMV (§12.6) |
| 13 | **Limites produit et marge de croissance** | **taille de base listée `unlimited`** par la documentation officielle ; **aucun plafond d'édition comparable** ; capacité pratique bornée par OS, matériel, stockage et configuration ; relation ≤ **32 To** | **10 Go / base** · **1 410 Mo** de buffer pool · **le moindre de 1 socket ou 4 cœurs** — plafonds **d'édition** | `OD` (S1, S19) | **DÉCISIF — PostgreSQL** (**D1**, §11.1) |
| 14 | **Licence et coût** | **PostgreSQL License** : « use, copy, modify, and distribute … **for any purpose, without fee, and without a written agreement** » | **« entry-level, free database »** (Microsoft Learn) | `OD` | **Égalité sur l'acquisition** ; asymétrie sur la **sortie** (§12.4) |
| 15 | **Charge de maintenance** | purge du pool obligatoire ; sémantique `25P02` à respecter ; autovacuum | RCSI à activer explicitement pour ne pas bloquer les lecteurs ; port fixe à configurer ; pas d'Agent | `ES`+`EWL`+`OD` | quasi-égalité, léger avantage **PostgreSQL** |
| 16 | **Simplicité de déploiement** | port par défaut, un installeur ; **installeur hébergé par EDB**, non par la communauté | Configuration Manager requis (protocoles, port fixe) | `EWL`+`OD` | léger avantage **PostgreSQL** |
| 17 | **Chemin de migration SQLite → serveur** | réconciliation **complète**, PK conservées, séquences resynchronisées | réconciliation **complète**, PK conservées, **`IDENTITY_INSERT` conditionnel requis** | `ES` | **Égalité** (prototype réduit) |
| 18 | **Compétences équipe / support** | `NOT_PROVED` | `NOT_PROVED` | `NP` | **exclu de la décision** |
| 19 | **Risques encore non résolus** | `DateTime` (**refus EF**) + filtres booléens + `REAL` + mapper d'erreurs + compat. applicative | `DateTime` (arrondi SQL brut) + `REAL` + mapper d'erreurs + compat. applicative | `ES` | **avantage SQL Server** — 2 remédiations propres à PostgreSQL (§16) |
| 20 | **Réversibilité de la décision** | élevée | élevée — **dialecte déjà implémenté et prouvé en production** | `EPI`+`INF` | **Égalité** (§19) |

**Aucune note globale n'est attribuée. Aucun score pondéré n'est employé** — le dépôt ne l'exige nulle part, et
un score aurait masqué le fait que le résultat repose sur **deux** critères et non sur une moyenne (§11).

---

## 8. Méthode de décision

Le dépôt **n'impose aucun barème**. La méthode retenue est donc : **matrice de preuves + raisonnement
explicite**, avec quatre règles de discipline.

1. **Un `PASS` n'est pas un point.** Le Lot C et le Lot D produisent des `PASS` **symétriques** : « deux `PASS`
   symétriques ne constituent pas une décision d'architecture ». Compter les `PASS` aurait produit une
   quasi-égalité dénuée de sens.
2. **Une mesure locale n'est pas une vérité générale.** Les chiffres du spike valent pour les versions,
   éditions et conteneurs testés. Aucun n'est extrapolé.
3. **Un critère à égalité ne peut pas départager** — il est classé comme tel (§12) et retiré du raisonnement.
4. **Les critères écartés sont nommés** : familiarité, préférence, étiquettes « principal / secondaire » (§2.2),
   décompte de `PASS`, hypothèses de coût non vérifiées, compétences d'équipe (`NOT_PROVED`).

**Ce qui reste après ce filtrage** : les différences **structurelles** (§11), pesées contre le **coût de
remédiation** propre à chaque candidat (§16). « Structurelles » ne veut pas dire « impossibles à contourner » —
§9 distingue précisément les deux cas.

---

## 9. Constats décisifs

**Deux** critères portent la décision. Chacun est `OFFICIAL_DOCUMENTATION` ou `EXECUTED_SPIKE`.

> Numérotation reprise en §11 pour le détail.
>
> **Correction du 2026-07-27.** Une rédaction antérieure comptait **trois** constats décisifs, dont
> « SQL Server Agent est absent d'Express, et c'est le mécanisme que Microsoft documente lui-même pour
> planifier les sauvegardes ». **Ce constat était matériellement inexact** : Microsoft **documente
> explicitement** l'automatisation des sauvegardes d'Express **sans** SQL Server Agent, par procédure
> stockée `sp_BackupDatabases` + `sqlcmd` + **Planificateur de tâches Windows** (S18). L'absence d'Agent est
> **rétrogradée** en **désavantage d'exploitation réel mais non décisif** (§10, §12.6). Les deux constats
> ci-dessous **subsistent inchangés sur le fond** et sont renumérotés **D1** et **D2**. **Aucun troisième
> critère décisif n'a été fabriqué pour préserver l'ancienne numérotation.**

| # | Constat décisif | Niveau | Pourquoi il est décisif | Contournable par du code MMV ? |
|---|---|---|---|---|
| **D1** | **Plafonds d'édition d'Express** — **10 Go** de taille maximale de base, **1 410 Mo** de buffer pool, **le moindre de 1 socket ou 4 cœurs** — face à un modèle MMV dont les données de santé **ne peuvent pas être supprimées physiquement** | `OD` (S1) + dépôt | Les plafonds portent sur une base **conçue pour croître de façon monotone**. Atteindre celui de 10 Go en magasin est une **panne d'écriture**, pas un ralentissement. La volumétrie réelle de MMV reste **`NOT_PROVED`** : la décision repose sur l'**asymétrie du risque de plafond d'édition**, **pas** sur l'affirmation que MMV dépassera 10 Go. | **Non** — contrainte d'**édition**, hors d'atteinte de tout code MMV |
| **D2** | **Identification des contraintes** : PostgreSQL expose une **preuve structurée** (`ConstraintName`, `TableName`, `ColumnName`) ; côté SQL Server, l'identification a été **mesurée à travers le texte du message** | `ES` (E6, E12, E17) | `PersistenceErrorMapper` doit être **réécrit** de toute façon (P4-4, critère de sortie n° 6) ; le choix décide si ce code neuf s'appuie sur des **métadonnées typées** ou sur du **texte**. C'est une distinction de **qualité d'implémentation et de risque de diagnostic**. | **Partiellement** — le parsing de message est **techniquement implémentable**, mais **moins robuste** que des métadonnées structurées (sensible au provider, à la langue et à la version). La distinction reste pertinente **bien qu'un contournement existe**. |

---

## 10. Constats à égalité ou non décisifs

Explicitement **retirés** du raisonnement, pour qu'aucun ne soit compté deux fois. Détail en §12.

| Critère | Constat | Pourquoi non décisif |
|---|---|---|
| Primitives atomiques (1, 3) | **14 / 14** des deux côtés | strictement symétrique |
| Mapping monétaire (6) | `REAL` **échoue des deux côtés** ; décimal exact **réussit des deux côtés** | obligation **commune**, indépendante du provider |
| TCP multi-poste (10) | `PASS` des deux côtés | symétrique |
| Import SQLite (17) | réconciliation **complète** des deux côtés | symétrique (prototype réduit) |
| Coût d'acquisition (14) | **zéro des deux côtés** | non discriminant |
| Idempotence des primitives | résultat **identique** des deux côtés ; incrément/décrément et numérotation **non idempotents** | conditionne tout retry futur, **ne départage pas** |
| Purge du pool après coupure | **obligatoire des deux côtés** | symétrique |
| Compétences équipe (18) | `NOT_PROVED` | ne peut fonder aucune décision |
| Performance | `NOT_PROVED` | non mesurée, non invoquée |
| EF Core / index filtrés (2, 5) | avantage **SQL Server** | réel mais **faible** : une seule expression de filtre, réécriture triviale et déjà mesurée |
| `DateTime` (7) | avantage **SQL Server** | réel et **retenu comme désavantage accepté** (§16.1) — pas comme départage, car le sujet est le **mapping du modèle**, commun à toutes les colonnes de date, et déjà ouvert par [ADR-005](adr-candidates.md#adr-005) |
| **Automatisation d'exploitation / absence de SQL Server Agent (12)** | avantage **PostgreSQL** — **désavantage d'exploitation réel** d'Express : ni Agent, ni tâches planifiées, ni plans de maintenance | **non décisif à lui seul** : Microsoft **documente** l'automatisation des sauvegardes d'Express par `sp_BackupDatabases` + `sqlcmd` + **Planificateur de tâches Windows** (S18) — ce n'est **ni indocumenté, ni un contournement improvisé, ni non supporté**. **MMV doit de toute façon construire, sécuriser, superviser, doter d'une rétention et documenter sa chaîne de sauvegarde planifiée pour l'un comme pour l'autre candidat.** Détail : **§12.6** |

---

## 11. Détail des constats décisifs

### 11.1 D1 — Plafonds d'édition face à une base conçue pour ne jamais rétrécir

`OFFICIAL_DOCUMENTATION` (S1, S19) **+ constat du dépôt**.

Table « Scale limits » de Microsoft Learn, colonne **Express**, comparée à ce que documente PostgreSQL :

| Limite | SQL Server Express | PostgreSQL |
|---|---|---|
| **Taille maximale d'une base** | **10 Go** — plafond **d'édition** | **`unlimited`** dans la table officielle des limites (S19) |
| Taille maximale d'une relation | — | **32 To** avec le `BLCKSZ` par défaut de 8192 octets (S19) |
| Mémoire maximale du buffer pool par instance | **1 410 Mo** — plafond **d'édition** | **aucun plafond d'édition comparable** |
| Capacité de calcul maximale par instance | **le moindre de 1 socket ou 4 cœurs** — plafond **d'édition** | **aucun plafond d'édition comparable** |

> **Formulation exacte de la capacité PostgreSQL, à ne pas assouplir.** La documentation PostgreSQL liste la
> **taille de base comme `unlimited`** et PostgreSQL **n'a pas de plafonds d'édition comparables à ceux de
> SQL Server Express**. Cela ne signifie **pas** « RAM illimitée », **pas** « CPU illimité », **pas**
> « aucune limite technique d'aucune sorte » : la **capacité pratique reste bornée par le système
> d'exploitation, le matériel, le stockage et la configuration**, et une relation individuelle est plafonnée à
> **32 To** (S19). La différence retenue est **l'absence de plafond d'édition**, pas une absence de limite
> physique.

**Ce constat serait faible s'il s'arrêtait là** — la volumétrie réelle de MMV est `NOT_PROVED`, et cet ADR **ne
prétend pas** que 10 Go seront atteints. Ce qui le rend décisif est **la conjonction avec une propriété du
modèle MMV lui-même** :

- P3 a établi que la **suppression physique d'une ordonnance est refusée inconditionnellement** par
  `PrescriptionRetentionPolicy` — `DeletePrescriptionUseCase` ne détient **aucune dépendance d'écriture** ;
- l'archivage client est **logique**, pas destructif ;
- les fiches atelier sont **persistées et versionnées** (une nouvelle version **s'ajoute**, la précédente est
  conservée) ;
- les alertes conservent leur **historique** (nouvel épisode après résolution : 2 lignes, 1 active — mesuré) ;
- ventes, commandes et mouvements de stock sont un **historique métier**.

**La base de production MMV est donc, par conception, monotone croissante sur ses données les plus
volumineuses.** Un plafond dur y est structurellement différent d'un plafond sur une base qu'on peut purger.

**Pourquoi ce constat n'est pas contournable par du code MMV.** Les plafonds de taille, de buffer pool et de
capacité de calcul d'Express sont des **contraintes d'édition** : aucun code applicatif, aucune configuration
serveur et aucune optimisation de schéma ne les relève. C'est précisément ce qui distingue D1 de D2, où un
contournement existe (§9, dernière colonne).

**Nature de la conséquence en cas d'atteinte du plafond** (`OFFICIAL_DOCUMENTATION` + `INFERENCE` explicite) :
la limite de 10 Go est une limite **d'édition**, pas de configuration. La sortie documentée est la **montée vers
une édition supérieure** — *« SQL Server Express can be seamlessly upgraded to other higher end editions »*.
**Le coût monétaire et les conditions de licence de cette montée ne sont pas établis par les sources
consultées.** Une base atteignant ce plafond dans un magasin en activité produit une **incapacité d'écriture** :
plus de vente enregistrée, plus d'ordonnance saisie. Le remède est alors une opération d'urgence — montée
d'édition ou migration de SGBD — sur des données de santé, sous pression commerciale. **Le risque n'est pas
symétrique : PostgreSQL n'expose pas de plafond d'édition comparable.**

> **Formulation honnête retenue** : ce n'est **pas** « Express est trop petit pour MMV » — cela reste
> `NOT_PROVED`. C'est : **MMV ne dispose d'aucune preuve lui permettant d'accepter ce plafond, et son modèle de
> rétention empêche de résorber ce risque par une simple purge des données métier.** Choisir le candidat sans
> plafond est la décision disponible qui **n'exige aucune preuve de volumétrie**.

### 11.2 D2 — Identification des contraintes : métadonnées structurées contre texte de message

`EXECUTED_SPIKE` (E6, E12, E17).

| Erreur provoquée | PostgreSQL | SQL Server Express |
|---|---|---|
| Unicité (index filtré) | `23505` + **`ConstraintName` = `idx_notifications_active_low_stock_unique`** (champ structuré) | `2601` — nom de contrainte **dans le texte**, **2ᵉ littéral** du message |
| Clé primaire | `23505` + `PK_ProductCategories` (structuré) | **`2627`** — nom **dans le texte**, **1ᵉʳ littéral** |
| Clé étrangère | `23503` + **`FK_Products_Suppliers_SupplierId` nommée** | `547` — colonne dans le texte, 1ᵉʳ littéral |
| NOT NULL | `23502` + colonne | `515` + colonne |
| Timeout client / de verrou | **aucun `SqlState`** (⚠️) | `-2` **dans les deux cas** (⚠️ non distinguables entre eux) |
| Annulation `CancellationToken` | `57014` | **`Number = 0`** (⚠️ non classifiable) |
| Connexion refusée / coupée | aucun `SqlState` (⚠️) | `10061` / **`10054`** |

**Pourquoi c'est décisif, et non un simple confort de développement.**

1. **`PersistenceErrorMapper` est aujourd'hui entièrement couplé à `SqliteException`** et doit être **réécrit**
   pour le serveur (P4-4, critère de sortie n° 6). Le choix de provider décide donc **de quoi sera fait ce
   code neuf**.
2. **PostgreSQL expose `ConstraintName` comme champ structuré.** Côté SQL Server, l'identification de la
   contrainte a été **mesurée à travers le texte du message**, à une position qui **dépend du code d'erreur**
   (`2601` → 2ᵉ littéral ; `2627` / `547` → 1ᵉʳ). **Le parsing est possible** — c'est une technique
   couramment employée — **mais il est plus fragile** et **sensible au provider, à la langue et à la
   version**. **La distinction reste donc pertinente bien qu'un contournement existe** : elle porte sur la
   **robustesse** du code neuf, pas sur sa faisabilité.
   *Considération de localisation, énoncée sans extrapolation* : le laboratoire a employé un **média
   d'installation en français** (`SQLEXPR_x64_FRA`). **La localisation est donc une considération concrète de
   déploiement.** En revanche, **le nom de ce fichier de média ne prouve pas à lui seul la langue effective de
   chaque message d'erreur à l'exécution** — celle-ci n'a **pas** été mesurée et n'est **pas** revendiquée.
3. **Ce que MMV en fait est visible par l'utilisateur.** `PersistenceErrorCategory` distingue
   `UniqueConstraint` (« un enregistrement existe déjà — doublon ») de `ConstraintViolation` (« viole une
   contrainte d'intégrité »). Une identification qui se dégrade en `Unknown` transforme un message métier juste
   en message générique. L'audit P4-0 §28 l'avait anticipé : « classification d'erreurs **muette** côté serveur
   → erreurs métier dégradées en `Unknown` ».
4. **Le décompte brut de classifiabilité favorisait légèrement SQL Server** (7/8 contre 6/8) — et il est
   **écarté comme trompeur** : les trous PostgreSQL portent sur des **erreurs d'infrastructure** (timeout,
   connexion), détectables par **type d'exception et état de connexion** ; le trou SQL Server porte sur
   l'**identification de la contrainte violée**, qui est précisément ce dont dépend la décision métier. **La
   nature du trou compte plus que son nombre.**

> **Contrepartie enregistrée sans atténuation.** PostgreSQL est **plus mauvais** sur trois classifications :
> timeout client, timeout de verrou et connexion refusée donnent **toutes** une `NpgsqlException` **sans
> `SqlState`**. Une politique de reprise PostgreSQL **ne pourra jamais** s'appuyer sur un code pour ces cas —
> uniquement sur le type et l'état de connexion. C'est une obligation d'implémentation réelle (§15, O5), pas un
> détail.

---

## 12. Détail des constats à égalité

### 12.1 Concurrence — avantages croisés, aucun départage

**Ce que les deux tiennent, identiquement** : `14 / 14` primitives ; **20/20** tours conformes ; deadlock
arbitré par le serveur avec **victime unique et aucun effet partiel** ; **aucune écriture partielle** ne survit
à une coupure en transaction ; **aucune survente** sur le décrément du dernier article ; **unicité stricte** de
la numérotation.

**Où PostgreSQL est meilleur** : MVCC — *« reading never blocks writing and writing never blocks reading »*
(`OD`, S6), `read committed` par défaut (`OD`, S7). SQL Server **Express est livré RCSI = OFF**, donc les
lecteurs **bloquent par défaut** : mesuré **5 022 ms puis expiration (`-2`)** contre **9 ms** avec RCSI ON.
Reprise après panne transitoire : **2 tentatives** contre **5** (budget entier), avec **épuisement complet du
budget** lors d'une exécution antérieure côté SQL Server.

**Où SQL Server est meilleur** : après une erreur, **la transaction reste utilisable**. Sur PostgreSQL, **toute
erreur avorte la transaction entière** (`25P02`) jusqu'au `ROLLBACK` — y compris après un deadlock, où la
transaction victime est **entièrement perdue**. « Tout code MMV qui **rattrape** une erreur à l'intérieur d'une
transaction et **poursuit** fonctionnera sur SQL Server et **échouera sur PostgreSQL**. »

**Verdict** : les avantages **se croisent**. Le blocage des lecteurs d'Express est **corrigeable par
configuration** (RCSI ON, mesuré sans affaiblir la primitive CAS) ; l'abandon de transaction PostgreSQL est
**corrigeable par audit de code** (§15, O6). **Aucun des deux ne départage** — mais l'obligation O6 est créée
par la décision et **due**.

### 12.2 Monétaire — obligation commune, jamais un départage

`REAL` est traduit en **flottant simple précision des deux côtés** : `999 999,99` relu **1 000 000** sur
PostgreSQL **et** sur SQL Server (**4/5** chacun). La documentation officielle l'explique indépendamment de la
mesure : PostgreSQL — *« The data types `real` and `double precision` are **inexact**, variable-precision »*,
`real` ayant *« a precision of at least 6 decimal digits »* (S8) ; SQL Server — conversion
`decimal`→`real` = *« Possible loss of precision »* (S9). Un mapping décimal exact donne **5/5 des deux côtés**.

**Fait conservé tel quel, parce qu'il est contre-intuitif** : sur le scénario composite à faible magnitude
(3 × 0,10 € − 0,07 − 0,05), `REAL` donne le **bon** résultat sur les deux providers. **Cela n'innocente pas
`REAL`** — cela montre que le défaut est **silencieux et dépendant des montants** : invisible sur les petites
ventes de test, actif sur les montants réels élevés. **C'est ce qui le rend dangereux.**

**Conclusion** : obligation d'implémentation **commune et bloquante** (§15, O2 ; §17), **pas** un critère de
choix. « Ce point ne départage pas les providers — ils échouent et réussissent identiquement. »

### 12.3 Index filtrés — écart réel mais mince

PostgreSQL a **refusé** le modèle réel sans adaptation : **`42883: operator does not exist: boolean = integer`**,
provoqué par `HasFilter("\"IsCurrent\" = 1")` — filtre écrit pour SQLite, où `bool` est un entier, alors que
PostgreSQL possède un **booléen natif**. SQL Server mappe `bool` sur `bit` : `= 1` y est valide **sans
adaptation**.

**Une seule** adaptation a suffi (`"IsCurrent" = 1` → `"IsCurrent"`), après quoi **les deux providers
produisent une structure identique en nombre** : 21 tables · 51 index · 20 FK. La forme adaptée est
**exactement** celle que documente PostgreSQL : `CREATE UNIQUE INDEX … WHERE success` (S10). Les deux garanties
structurelles — alerte LowStock active unique, fiche atelier courante unique — **tiennent sur les deux
providers**, avec refus du doublon (`23505` / `2601`) et bascule atomique réussie.

L'écart est **réel** (§16.2, obligation O3) mais porte sur **une expression de filtre**, dont la réécriture est
**déjà mesurée et validée**.

### 12.4 Licence — égalité à l'acquisition, asymétrie à la sortie

`OFFICIAL_DOCUMENTATION` (S3, S1).

**Les deux sont gratuits à l'acquisition.** PostgreSQL : *« Permission to use, copy, modify, and distribute this
software and its documentation for any purpose, **without fee**, and **without a written agreement** is hereby
granted »* — licence de type BSD/MIT, usage commercial inclus. SQL Server Express : *« the entry-level, **free**
database »*.

**Aucun coût monétaire d'une montée d'édition n'est établi**, et il n'est affirmé ni que SQL Server Express
coûte de l'argent, ni qu'une édition supérieure est payante. L'asymétrie documentée est ailleurs : la gratuité
d'Express est **bornée par ses plafonds d'édition** (§11.1), et la sortie documentée en cas de dépassement est
la montée vers une édition supérieure, sans conclusion sur son prix. PostgreSQL n'expose **aucun plafond
d'édition comparable** (§11.1) ni contrainte de redevance. **Non décisif sur l'acquisition ; pertinent sur la
trajectoire de sortie d'Express**, où ce point rejoint **D1** — et il est compté **une seule fois**, dans D1.

### 12.5 Cycle de vie — les deux sont supportés bien au-delà de V1

`OFFICIAL_DOCUMENTATION` (S2, S4).

| | Fin de support |
|---|---|
| **PostgreSQL 17** | **2029-11-08** (politique : **5 ans** par version majeure ; une majeure par an environ) |
| **SQL Server 2022** | **Mainstream : 2028-01-11** · **Extended : 2033-01-11** (Fixed Lifecycle Policy) |

**Aucun des deux n'est en fin de vie**, et cet horizon **ne départage pas** — mais PostgreSQL offre une
trajectoire de mises à niveau majeures **annuelle et continue**, là où SQL Server impose des sauts de version
produit. Écart **noté, non décisif**.

### 12.6 Automatisation des sauvegardes et absence de SQL Server Agent — **désavantage réel, non décisif**

`OFFICIAL_DOCUMENTATION` (S1, S5, **S18**). **Cette sous-section corrige une analyse antérieure erronée**
(§9, encadré de correction).

**Ce qui est officiellement établi, sans interprétation.**

1. **SQL Server Express n'inclut pas SQL Server Agent** — table « Management tools », colonne Express :
   **SQL Server Agent = No**. Sont également absents d'Express : SQL Profiler, Database mail, Performance data
   collector, Standard performance reports, Backup compression, Encrypted backup, Log shipping (S1).
2. **Express ne permet donc pas de planifier de tâches ni de plans de maintenance.** Énoncé exact de Microsoft :
   *« SQL Server Express editions don't offer a way to schedule either jobs or maintenance plans because the
   SQL Server Agent component isn't included in these editions. »* (S18)
3. **Microsoft documente néanmoins une méthode d'automatisation des sauvegardes pour Express**, et la documente
   comme une procédure à suivre : *« This article introduces how to use a Transact-SQL script and Windows Task
   Scheduler to automate backups of SQL Server Express databases on a scheduled basis. »* Elle repose sur une
   procédure stockée `sp_BackupDatabases` (script Microsoft), un fichier de commandes invoquant **`sqlcmd`**, et
   une tâche du **Planificateur de tâches Windows** — sauvegardes **complètes, différentielles et de journal**
   couvertes (S18).

**Ce qui ne doit donc plus être affirmé, et ne l'est plus.** Il est **faux** de présenter l'automatisation des
sauvegardes d'Express comme :

- **indocumentée par Microsoft** — elle est documentée par un article dédié (S18) ;
- un **contournement improvisé ou non supporté** — Microsoft en décrit les quatre étapes, fournit le script et
  en énonce les prérequis ;
- **nécessairement plus « artisanale » que la planification PostgreSQL** — les deux passent par un
  **ordonnanceur externe au moteur**, et l'article Microsoft rend la voie Express au moins aussi explicitement
  balisée.

**Reclassement retenu.** L'absence de SQL Server Agent est :

- **un désavantage d'exploitation réel** — l'ordonnanceur intégré au produit, la compression de sauvegarde, la
  sauvegarde chiffrée et le log shipping sont indisponibles en Express, et les sauvegardes planifiées passent
  obligatoirement par un mécanisme externe au moteur ;
- **mais pas un critère décisif à lui seul** — la capacité visée reste atteignable par une voie que l'éditeur
  documente.

**Obligation symétrique, énoncée explicitement.** **Quel que soit le candidat retenu, MMV doit construire,
sécuriser, superviser, doter d'une rétention et documenter sa propre chaîne de sauvegarde planifiée.** Aucun des
deux SGBD ne la fournit prête à l'emploi pour ce contexte :

- l'état actuel du dépôt est une **copie de fichier SQLite**, **sans rétention, sans automatisation, sans
  documentation opérateur** — **tout est à construire dans les deux cas** ;
- l'article Microsoft le dit lui-même pour la rétention : *« We recommend that you clean old files in the
  Backup folder regularly to make sure that you don't run out of disk space. **The script doesn't contain the
  logic to clean up old files.** »* (S18) — la rétention reste donc à la charge de MMV ;
- il signale aussi une contrainte de secret applicable à cette voie : *« If you are using SQL authentication,
  ensure that access to the folder is restricted to authorized users as the passwords are stored in clear
  text. »* (S18). Une chaîne PostgreSQL équivalente pose **le même genre de question** de gestion
  d'identifiants (fichier de mot de passe, variable d'environnement ou authentification intégrée) — traitée en
  §21 et par l'obligation **O10** ;
- la supervision (échec de tâche détecté, sauvegarde vérifiée, restauration réellement testée) n'est fournie
  par **aucun** des deux et relève de **O9** ;
- le Planificateur de tâches doit lui-même être fiable : *« The Task Scheduler service must be running at the
  time that the job is scheduled to run. We recommend that you set the startup type for this service as
  **Automatic**. »* (S18) — contrainte d'exploitation à documenter, applicable à tout ordonnanceur externe.

**Effet sur la décision** : **aucun**. Ce critère est **retiré des constats décisifs** et figure désormais en
§10. Il reste enregistré comme **désavantage d'exploitation d'Express** et comme **obligation O9 commune**.

---

## 13. Pourquoi la symétrie des `PASS` n'a pas décidé

Un décompte naïf donnerait une quasi-égalité : Lot C = `PASS`/`PASS` ; Lot D = `PASS`/`PASS` ;
primitives = 14/14 contre 14/14 ; import = complet des deux côtés ; sauvegarde/restauration = exécutée des deux
côtés ; monétaire = échec des deux côtés puis succès des deux côtés.

**Ce décompte est explicitement rejeté**, pour les raisons déjà écrites dans les rapports : « deux `PASS`
symétriques ne constituent pas une décision d'architecture » ; « la correction elle-même est **neutre** : elle
ne favorise aucun des deux candidats ».

**Ce que la symétrie prouve réellement** : les deux candidats ont franchi le **seuil d'admissibilité**. Elle
transforme la question « lequel fonctionne ? » — les deux — en « lequel impose le moins de risque non
maîtrisable ? ». Cette seconde question se tranche sur les **différences structurelles** (§11) et sur le **coût
de remédiation** (§16), pas sur un score.

---

## 14. Couverture des 20 critères imposés

| # | Critère | Traité en | Résultat |
|---|---|---|---|
| 1 | Intégrité et primitives atomiques | §7.1, §12.1 | **égalité** — 14/14 |
| 2 | Compatibilité EF Core | §7.2, §12.3 | léger avantage SQL Server |
| 3 | Preuves de spike sur implémentations de production | §6.4, §7.3 | **égalité** |
| 4 | Concurrence et verrouillage | §7.4, §12.1 | **partagé** |
| 5 | Index filtrés / partiels uniques | §7.5, §12.3 | léger avantage SQL Server |
| 6 | Stockage monétaire exact | §7.6, §12.2 | **égalité — non décisif** |
| 7 | `DateTime` et remédiation | §7.7, §16.1 | avantage SQL Server — **désavantage accepté** |
| 8 | Classification d'erreurs et diagnostic | §11.2 | **DÉCISIF — PostgreSQL (D2)** |
| 9 | Installation Windows en magasin | §7.9 | avantage PostgreSQL (modéré) |
| 10 | Fonctionnement TCP multi-poste | §7.10 | **égalité** |
| 11 | Sauvegarde et restauration | §7.11, §17 | avantage PostgreSQL (modéré) |
| 12 | Automatisation d'exploitation | §12.6 | **NON DÉCISIF** — désavantage réel d'Express, capacité **documentée par Microsoft** ; obligation symétrique |
| 13 | Limites produit et marge de croissance | §11.1 | **DÉCISIF — PostgreSQL (D1)** |
| 14 | Licence et coût | §12.4 | égalité à l'entrée ; asymétrie à la sortie |
| 15 | Charge de maintenance | §7.15 | quasi-égalité |
| 16 | Simplicité de déploiement | §7.16 | léger avantage PostgreSQL |
| 17 | Chemin de migration SQLite → serveur | §7.17, §18 | **égalité** |
| 18 | Compétences équipe / support | §6.5 | **`NOT_PROVED` — exclu de la décision** |
| 19 | Risques encore non résolus | §16 | avantage SQL Server — **assumé** |
| 20 | Réversibilité | §19 | **égalité** — réversibilité élevée |

---

## 15. Obligations d'implémentation créées par cette décision

**Aucune n'est exécutée par cet ADR.** Chacune est **due** avant que la V1 multi-poste puisse être déclarée.
Les affectations de phase sont **indicatives** — le découpage P4-3 … P4-12 reste réévaluable — mais les
obligations elles-mêmes ne le sont pas.

| # | Obligation | Bloquante ? | Phase indicative |
|---|---|---|---|
| **O1** | Centraliser la sélection du provider : **4 sites `UseSqlite`** à unifier ; ajouter le paquet `Npgsql.EntityFrameworkCore.PostgreSQL` **en Infrastructure uniquement** ; SQLite **inchangé** en dev/test/démo ; **1505** tests verts conservés | **OUI** | P4-3 |
| **O2** | **Mapping monétaire exact** en remplacement de `HasColumnType("REAL")` sur les colonnes **monétaires**. Type et précision **non choisis ici**. **Ne pas confondre** avec les colonnes optiques (`Sphere`, `Cylinder`, `Addition`, `PrismValue`, `Transposed*`), dont `REAL` est un choix distinct **hors du périmètre de cette obligation** | **OUI** — prérequis de tout import fidèle | P4-5 (+ P4-7) |
| **O3** | Réécrire le **filtre d'index booléen** `"IsCurrent" = 1` → forme booléenne PostgreSQL, dans les **mappings et migrations de production** — aujourd'hui appliqué **dans le spike uniquement** (`AdaptedOpticDbContext`) | **OUI** | P4-5 |
| **O4** | Résoudre le **mapping `DateTime`** : PostgreSQL **refuse** `DateTime.Now` (`Kind = Local`) par le chemin EF. Décider entre conversion de valeur (UTC + horloge injectable, cf. [ADR-005](adr-candidates.md#adr-005)) et type de colonne, puis **verrouiller par assertion** — le constat actuel est **observationnel, sans assertion** | **OUI** — MMV ne peut pas écrire de date en l'état | P4-4 et/ou P4-5 |
| **O5** | Doter `PersistenceErrorMapper` d'une classification **PostgreSQL** : `23505` / `23503` / `23502` / `3D000` / `57014` via `SqlState` **et `ConstraintName` structuré** ; **et** traiter explicitement les **3 cas sans `SqlState`** (timeout client, timeout de verrou, connexion refusée) **par type d'exception et état de connexion, jamais par code**. Interdit : modifier `PersistenceException` / `PersistenceErrorCategory` (Domain, neutres) | **OUI** — critère de sortie n° 6 | P4-4 |
| **O6** | **Auditer tout `catch` situé à l'intérieur d'une transaction** : sur PostgreSQL, toute erreur avorte la transaction entière (`25P02`) et toute commande ultérieure échoue. Comportement **non couvert** par les tests P3 (SQLite mono-écrivain) | **OUI** | P4-4 |
| **O7** | **Chaîne de migrations serveur distincte** (baseline propre) : la chaîne SQLite (14 migrations) n'est pas portable. **Les 14 migrations historiques doivent rester utilisables pour SQLite local.** | **OUI** | P4-5 |
| **O8** | **Sérialiser** l'application des migrations en multi-poste et **bloquer proprement un client trop ancien** face à un schéma trop récent | **OUI** | P4-6 |
| **O9** | **Sauvegarde planifiée + rétention + restauration testée** sur la base centrale (`pg_dump`/`pg_restore`), **purge du pool** incluse dans la procédure, + **documentation opérateur** | **OUI** — critères 11, 12, 13 | P4-9 |
| **O10** | **Chaîne de connexion serveur hors dépôt**, démarrage sans configuration = **échec clair**, indisponibilité serveur gérée, **modèle de permissions applicatif réel** en remplacement du `db_owner` de laboratoire | **OUI** — critère 14 | P4-8 |
| **O11** | **Outil / procédure d'import SQLite → PostgreSQL**, ou **procédure d'échec sûre** : traiter les données `HasData` présentes **des deux côtés**, resynchroniser les séquences, **ne jamais importer `__EFMigrationsHistory`**, prouver la complétude (lignes, FK, unicité, **montants exacts**) | **OUI** — critère 7 | P4-7 |
| **O12** | **Re-prouver les 14 primitives** sur PostgreSQL, y compris **contre l'installation Windows native** du Lot C, contre laquelle **aucune primitive applicative n'a encore été exercée**. La preuve de spike **ne vaut pas** cette re-preuve | **OUI** — critère 5 | P4-4 |
| **O13** | **Tests multi-processus et résilience** : scénarios P4-10, perte réseau, reconnexion, timeout, deadlock, retry. **Aucun retry ne doit être introduit sans clé d'idempotence** : incrément/décrément de stock et numérotation sont **mesurés non idempotents** | **OUI** — critères 8, 9, 10 | P4-10 |
| **O14** | Maintenir **Domain et Application provider-neutres** — vérifié par tests d'architecture. Toute proposition introduisant un provider dans ces couches **doit être rejetée** | **OUI** — critère 17 | transverse |
| **O15** | **Conserver le dialecte SQL Server** de `ActiveLowStockInsertSql` et le garde-fou `NotSupportedException`. Le retirer supprimerait la réversibilité établie en §19 **sans aucun gain** | **NON** (mais fortement recommandé) | transverse |

---

## 16. Risques non résolus — connus, non masqués, non résolus par cet ADR

Ces risques sont des **entrées** de cet ADR, pas des travaux qu'il exécute. **P4-2 ne corrige aucun d'entre
eux.** Trois sont des **désavantages acceptés du choix retenu**.

### 16.1 `DateTime` — le désavantage le plus lourd du choix retenu

`EXECUTED_SPIKE`, **mesuré sans assertion**, **non résolu**.

La production fournit `CreatedAt = DateTime.Now`, donc **`Kind = Local`**
([GenerateLowStockNotificationsUseCase.cs:104](../../src/MMV.Application/UseCases/Notifications/GenerateLowStockNotifications/GenerateLowStockNotificationsUseCase.cs#L104)) ;
`DateTime.Now` / `UtcNow` compte **78 occurrences** dans `src/**` hors migrations.

| Provider | Chemin **SQL brut** | Chemin **EF** (`SaveChanges`) |
|---|---|---|
| **PostgreSQL** | envoyé `…+02:00` → relu `…Z` — **l'heure locale est stockée comme si elle était UTC (2 h d'écart)** | **REFUS** — `DbUpdateException` |
| **SQL Server** | `…3451291` → `…3466667` (`Unspecified`) — **arrondi**, compatible avec la granularité `datetime` | `…4029416` → **identique** : précision **supérieure** conservée |

**Corroboration officielle des deux comportements** — ce ne sont pas des accidents de mesure :

- **Npgsql 6.0+** : *« trying to send a non-UTC `DateTime` as `timestamptz` will throw an exception »* ;
  `Utc` → `timestamp with time zone`, `Local`/`Unspecified` → `timestamp without time zone` (S11). Le refus EF
  mesuré est donc le **comportement documenté et voulu** du provider.
- **SQL Server `datetime`** : *« Accuracy — Rounded to increments of `.000`, `.003`, or `.007` seconds »*, et
  *« Avoid using `datetime` for new work. Instead, use … `datetime2` … `datetime2` … provide more seconds
  precision »* (S12).

**Lecture honnête, dans les deux sens.**

- **C'est un désavantage réel et accepté de PostgreSQL** : en l'état, **MMV ne peut pas écrire de `DateTime.Now`
  vers PostgreSQL par le chemin EF**. SQL Server l'aurait toléré. **Ce point favorise SQL Server, et il est
  enregistré comme tel** (§7.7, §10).
- **Il n'est pas retenu comme départage**, pour trois raisons **factuelles** : (a) le refus porte sur le
  **mapping `DateTime` du modèle**, **commun à toutes les colonnes de date**, et non sur une primitive ;
  (b) [ADR-005](adr-candidates.md#adr-005) avait **déjà** rejeté le statu quo `DateTime.Now` et orienté vers
  **horloge injectable + stockage UTC** — la remédiation exigée par PostgreSQL est donc un travail **déjà
  prévu**, pas une dette nouvelle ; (c) SQL Server **n'est pas exempt** : son chemin SQL brut **perd de la
  précision**, et Microsoft **déconseille lui-même `datetime`** pour du travail neuf.
- **Un refus bruyant est opérationnellement moins dangereux qu'un décalage silencieux.** PostgreSQL **lève** ;
  SQL Server **arrondit sans rien dire**. Sur des données de santé horodatées, la première défaillance est
  détectable au premier test, la seconde peut ne se voir jamais. **Ceci est une appréciation de risque
  (`INFERENCE`), pas une mesure** — elle est signalée comme telle et ne fonde aucun des **deux** constats
  décisifs.
- **Le constat est observationnel** : le harness l'a **consigné**, il ne le **verrouille pas**. Aucun test ne
  garantit aujourd'hui cette fidélité (**obligation O4**).
- **Il n'a pas été introduit par le Lot D** : sur SQLite le chemin est inchangé au caractère près, et sur les
  serveurs la primitive **ne s'exécutait pas du tout** auparavant.

### 16.2 Filtres d'index booléens PostgreSQL — adaptés dans le spike uniquement

`EXECUTED_SPIKE`. La réécriture `"IsCurrent" = 1` → `"IsCurrent"` vit dans `AdaptedOpticDbContext`, **côté
spike**. **Les mappings et migrations de production ne l'ont pas.** La création du schéma serveur en dépend
encore. **Risque propre au candidat retenu.** → **O3**.

### 16.3 Mapping monétaire `REAL` — obligatoire quel que soit le provider

`EXECUTED_SPIKE` + `OFFICIAL_DOCUMENTATION`. Perte de valeur mesurée sur **les deux** providers (§12.2).
**Prérequis de tout import fidèle** : avec `REAL`, la source elle-même est déjà altérée. **Ce n'est pas un
risque du choix ; c'est une dette du modèle**, et le plus grave des points durs hérités de P3. → **O2**.

### 16.4 `PersistenceErrorMapper` — encore entièrement couplé à `SqliteException`

Constat du dépôt, vérifié. Aucune erreur serveur n'est classifiée aujourd'hui : toute violation d'unicité
PostgreSQL serait dégradée en `Unknown` ou laissée non traduite. → **O5**.

### 16.5 Compatibilité applicative complète — non prouvée, sur aucun des deux candidats

`NOT_PROVED`, et ce point **ne doit jamais être présenté autrement**.

- Le **Lot D prouve une primitive**. Il n'exécute ni `MMV.App`, ni les cas d'usage complets, ni l'ensemble du
  modèle contre un serveur.
- Le **Lot C prouve le socle serveur**, pas l'intégration applicative : **aucun code** MMV n'a été exercé
  contre les installations Windows natives.
- **`P4-2 ADR = ACCEPTÉ` ne signifie donc pas que MMV tourne sur PostgreSQL.** Cela signifie que PostgreSQL est
  **officiellement retenu** pour la V1 et que P4-3 peut commencer — **pas** que la compatibilité applicative
  complète est prouvée.

### 16.6 Risques additionnels enregistrés

| Risque | Niveau | Statut |
|---|---|---|
| **Purge du pool obligatoire** après coupure — sans elle, la 1re reconnexion échoue instantanément | `ES` | à intégrer aux procédures (**O9**) et au retry (**O13**) |
| **Primitives non idempotentes** : incrément/décrément de stock, numérotation — 3 rejeux de `+5` sur 10 donnent **25** ; 3 tirages donnent 3 numéros | `ES` | **aucun retry sans clé d'idempotence** (**O13**) |
| **Valeur retournée par `IncrementStockAsync`** : relecture post-`UPDATE` — `20`/`15` sur PostgreSQL contre `20`/`20` sur SQL Server. Stock final correct des deux côtés, **valeur retournée trompeuse et différente selon le provider** | `ES` | à traiter en P4-4 |
| **`DateTimeKind` non préservé** à l'import côté SQL Server (`Unspecified` vs `Utc`) | `ES` | lié à **O4** ; sans objet si PostgreSQL est accepté |
| **Installeur Windows hébergé par EDB**, non par les serveurs communautaires | `OD` (S13) | à intégrer à la procédure d'installation et à la chaîne d'approvisionnement (**O10**) |
| Cause racine de `0x84C4000E` **non élucidée** | `NP` | sans objet si PostgreSQL est accepté ; **redevient pertinent** en cas de réexamen (§20) |

---

## 17. Implications exploitation et sauvegarde

| Sujet | État actuel (dépôt) | Cible PostgreSQL retenue | Niveau |
|---|---|---|---|
| Sauvegarde | **copie de fichier** SQLite + sidecars `-wal`/`-shm` | **`pg_dump -F c`** sur la base centrale — dump **internement cohérent**, *« does not block other operations »* | `OD` (S14) + `ES` + `EWL` |
| Restauration | `File.Copy` inverse | **`pg_restore`** après `dropdb --force` + `createdb`, **purge du pool obligatoire** (sinon `57P01`) | `ES` + `EWL` |
| Vérification post-restauration | partielle (PRAGMA au démarrage suivant) | recomptage lignes + FK + index + **somme des montants exacte** ; réconciliation mesurée **identique** (`999999.99` → `999999.99`) | `ES` |
| Rétention | **aucune** | à définir — **O9** | — |
| Planification | **aucune** | ordonnanceur **externe au moteur** (Planificateur de tâches Windows invoquant `pg_dump`). **Cette voie est également celle que Microsoft documente pour Express** (`sp_BackupDatabases` + `sqlcmd` + Planificateur, S18) : la planification est donc **externe dans les deux cas**, et ce point **ne départage pas** (§12.6) | `OD` (S18) + `INF` |
| **Rétention, supervision, secrets de la chaîne planifiée** | **aucune** | **à construire par MMV quel que soit le candidat** — le script Microsoft pour Express ne purge pas les anciens fichiers (*« The script doesn't contain the logic to clean up old files »*, S18), et aucun des deux SGBD ne fournit la supervision d'échec ni la gestion d'identifiants requise ici — **O9**, **O10** | `OD` (S18) |
| Documentation opérateur | **aucune** | requise — critère de sortie n° 13 | — |
| Migration en multi-poste | `Migrate()` **par poste** | **sérialisée** ; sauvegarde préalable conservée ; garde de version applicative — **O8** | — |
| Service Windows | sans objet | `postgresql-x64-17`, démarrage **Automatique**, retour automatique après redémarrage — **vérifié** | `EWL` |
| Exposition réseau | sans objet | TCP **5432** restreint par règle de pare-feu (protocole, port, interface, adresses) ; **pare-feu maintenu actif** — **vérifié** | `EWL` |

**Enseignement d'exploitation conservé tel quel** : le profil de la règle de pare-feu a dû être élargi à `Any`
parce que le réseau interne isolé est **reclassé `Public` par Windows après redémarrage**. Les restrictions de
**protocole, port, interface et adresses sont demeurées en vigueur**. À intégrer à la procédure d'installation
— et **traité identiquement sur les deux pistes**, ce qui les rend comparables.

---

## 18. Implications de migration

| Sujet | Constat | Conséquence |
|---|---|---|
| Prototype d'import | **réconciliation complète** sur les deux providers : 10 tables, PK conservées, montants exacts, dates, booléens, enums, chaînes normalisées, `NULL` préservés, séquences resynchronisées | base de conception de **O11** |
| **Prérequis absolu** | avec `REAL`, la source elle-même est altérée | **O2 précède O11** |
| `HasData` des deux côtés | `SALE` / `ORDER` seedés en source **et** en cible ⇒ collision de PK (`23505`) | l'outil doit traiter ce cas explicitement |
| `__EFMigrationsHistory` | **jamais importé**, vérifié absent en cible | **ne jamais importer** vers une base serveur neuve |
| Séquences | max importé 1 → prochain **2** ✅ | resynchronisation obligatoire |
| Donnée invalide | **refusée** — `23503`, **`FK_Products_Suppliers_SupplierId` nommée** ; rollback **propre** | pas d'import partiel silencieux |
| Chaîne de migrations | SQLite non portable (`GLOB`, `lower(trim())`, `CHECK`-abort, table-rebuild, annotations SQLite au snapshot) | **chaîne serveur distincte** — **O7** |
| SQLite local | **conservé** en dev/test/démo/mono-poste | **les 14 migrations historiques restent utilisables** |
| Volumétrie réelle, durée, reprise sur interruption | `NOT_PROVED` | à couvrir en P4-7 |

**Réversibilité du chemin de migration** : `AdaptedOpticDbContext` et l'import générique du spike ont exercé
**les deux** cibles. Un basculement ultérieur vers SQL Server ne repartirait donc **pas** de zéro (§19).

---

## 19. Alternative rejetée pour V1 — et pourquoi elle n'est **pas** éliminée

### 19.1 SQL Server Express — **rejeté pour V1**

**Rejeté pour V1**, pour **deux** constats seulement :

- **D1** — **plafonds d'édition** d'Express (**10 Go** par base, **1 410 Mo** de buffer pool, **1 socket ou
  4 cœurs**) sur une base MMV **monotone croissante par conception**, alors que la volumétrie réelle reste
  **`NOT_PROVED`** : c'est l'**asymétrie du risque de plafond d'édition** qui pèse, non une prévision de
  dépassement. **Non contournable par du code MMV.**
- **D2** — **identification des contraintes** par **métadonnées structurées** côté PostgreSQL contre
  identification **mesurée à travers le texte du message** côté SQL Server : le parsing est **implémentable**
  mais **moins robuste** (sensible au provider, à la langue et à la version), sur un `PersistenceErrorMapper` à
  réécrire de toute façon.

**Il n'est pas rejeté pour** : incapacité technique, échec de primitive, échec de concurrence, échec
d'installation Windows, échec de sauvegarde/restauration, échec d'import, ou coût de licence. **Sur tous ces
points, il a réussi** — et sur deux points (EF Core sans adaptation ; tolérance de `DateTime.Now` par le chemin
EF) **il a fait mieux que le candidat retenu**.

**Il n'est pas non plus rejeté pour l'absence de SQL Server Agent.** Ce motif figurait dans une rédaction
antérieure et **a été retiré des constats décisifs** : Microsoft **documente** l'automatisation des sauvegardes
d'Express sans Agent (S18). L'absence d'Agent reste un **désavantage d'exploitation réel**, enregistré en §10 et
détaillé en §12.6, **mais elle ne fonde pas le rejet**.

### 19.2 Rejet ≠ élimination — et la différence est matérielle, pas rhétorique

> **SQL Server Express reste une option future pleinement ouverte.**

Ce n'est pas une formule de politesse. C'est un état **vérifiable du code** :

1. **Son dialecte est implémenté et prouvé dans le code de production, aujourd'hui.**
   `ActiveLowStockInsertSql` contient la branche `Microsoft.EntityFrameworkCore.SqlServer` — forme
   `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))` — validée **20/20 tours**. Accepter
   PostgreSQL **ne supprime pas cette branche** (**O15**).
2. **La sélection est faite sur une chaîne**, `Database.ProviderName`, sans dépendance de compilation vers un
   provider. Ajouter ou changer de provider **ne restructure rien**.
3. **Domain et Application sont provider-neutres**, vérifié — la frontière hexagonale tient en amont.
4. **Les paramètres sont fabriqués par le provider courant**, jamais par un type concret : le mécanisme est
   **provider-agnostique par construction**.
5. **Les preuves E1 → E18 des deux candidats sont committées** et rejouables ; le harness porte les deux
   providers.
6. **SQLite reste en place**, ce qui maintient en permanence au moins deux dialectes vivants dans le code — la
   pression structurelle contre un couplage mono-provider est donc **permanente**, pas déclarative.

**Coût estimé d'un basculement ultérieur** (`INFERENCE`, honnêtement bornée) : la composition (**O1**), la
classification d'erreurs (**O5**) et la chaîne de migrations serveur (**O7**) devraient être refaites pour la
nouvelle cible ; **O2** (monétaire), **O4** (`DateTime`), **O8**, **O9**, **O10**, **O11**, **O13** et **O14**
sont **largement indépendantes du provider** et resteraient acquises. **Le basculement serait coûteux mais
non structurel.** Ce chiffrage n'a **pas** été mesuré et ne doit pas être cité comme une mesure.

### 19.3 Autres alternatives — hors question (rappel)

| Alternative | Statut | Source |
|---|---|---|
| **SQLite local** | **conservé** — dev / test / démo / mono-poste. **Non retiré.** | ADR-PROD-DB-001 §3.2 |
| **SQLite sur dossier réseau** | **interdit** en production multi-poste | ADR-PROD-DB-001 §3.3 / §4 option B |
| **Cloud / API centrale / SaaS** | **reporté** hors V1 | ADR-PROD-DB-001 §3.8 / §4 option E |

---

## 20. Conditions de réexamen de cet ADR

Cet ADR **doit** être réexaminé si **l'un** des éléments suivants se produit.

### 20.1 Déclencheurs invalidant un constat décisif

| # | Déclencheur | Constat visé |
|---|---|---|
| R1 | Les **plafonds d'édition** d'Express sont relevés au point de rendre le risque non pertinent pour un magasin (taille de base, buffer pool, cœurs), **ou** une mesure de **volumétrie réelle** de MMV démontre que la trajectoire à 10 ans reste très en deçà de 10 Go | **D1** |
| R2 | Microsoft expose des **métadonnées structurées** d'identification de contrainte sur `SqlException` (équivalent de `ConstraintName`), supprimant la dépendance au texte du message | **D2** |
| R3 | Une implémentation de parsing des messages SQL Server est **démontrée robuste** sur les versions et langues visées (tests reproductibles, couverture des codes `2601`/`2627`/`547`/`515`), ce qui réduirait **D2** à un écart de confort | **D2** |

> **Déclencheur retiré (correction du 2026-07-27).** Une rédaction antérieure listait « Microsoft rend
> SQL Server Agent disponible en édition Express » comme déclencheur invalidant un constat décisif. Ce
> déclencheur est **supprimé** : l'absence d'Agent **n'est plus un constat décisif** (§9, §12.6). Son
> apparition en Express **améliorerait** l'exploitation d'Express, mais **ne suffirait pas** à rouvrir la
> décision — il faudrait R1 ou R2/R3.

### 20.2 Déclencheurs propres au candidat retenu

| # | Déclencheur |
|---|---|
| R4 | La remédiation `DateTime` (**O4**) se révèle **impraticable** sur PostgreSQL sans modifier les contrats Domain/Application — ce qui inverserait §16.1 d'un désavantage accepté en défaut bloquant |
| R5 | La sémantique `25P02` (transaction avortée après erreur) impose une **réécriture étendue** de la logique métier plutôt qu'un simple audit des `catch` (**O6**) |
| R6 | L'obligation **O12** (re-preuve des 14 primitives sur PostgreSQL, y compris contre l'installation Windows native) **échoue** sur une primitive |
| R7 | La chaîne d'approvisionnement de l'installeur Windows PostgreSQL devient inacceptable (installeur EDB indisponible, non signé, ou incompatible avec la politique de déploiement retenue) |
| R8 | PostgreSQL 17 approche sa fin de vie (**2029-11-08**) sans trajectoire de mise à niveau majeure validée |

### 20.3 Déclencheurs de périmètre

| # | Déclencheur |
|---|---|
| R9 | Le périmètre produit change au point d'invalider ADR-PROD-DB-001 : plusieurs magasins dans une installation, multi-tenant, SaaS, ou base hébergée |
| R10 | Une exigence réglementaire (RGPD/BE, loi 09-08 / CNDP MA, gate `G-CNDP`) impose une capacité — chiffrement au repos, audit natif, localisation — que le candidat retenu ne peut pas satisfaire alors que l'autre le peut |
| R11 | Un client existant impose une contrainte d'infrastructure incompatible (politique interne SQL Server exclusive, par exemple) |

> **Un réexamen n'est pas un rejet.** Il rouvre §7 à §13 avec les preuves nouvelles, et **peut** reconduire la
> décision. Il exige un **nouvel ADR** (`ADR-PROD-DB-003`) ; il ne modifie pas celui-ci rétroactivement.

---

## 21. Implications de sécurité et de gestion des secrets

| Sujet | État actuel | Obligation créée | Niveau |
|---|---|---|---|
| **Aucun secret au dépôt** | **vérifié** — aucun secret, hash, mot de passe ni chaîne de connexion complète ; **aucun `appsettings.json`** ; configuration **100 % variables d'environnement** | **maintenir** — critère de sortie n° 14 ; **cet ADR ne contient aucune chaîne de connexion complète ni aucun mot de passe** | dépôt |
| Chaîne de connexion serveur | inexistante | **hors dépôt** ; démarrage sans configuration = **échec clair** — **O10** | — |
| Modèle de permissions | `db_owner` en laboratoire — **raccourci jetable, pas un modèle de production** | modèle applicatif au moindre privilège — **O10** | `EWL` |
| Authentification serveur | `scram-sha-256` lu dans `pg_hba.conf` (instance observée) ; authentification distante `mmv_app` réussie en laboratoire | politique d'authentification à arrêter en P4-8 | `EWL` + `ES` |
| Exposition réseau | sans objet | **pare-feu maintenu actif**, règle restreinte par protocole, port, interface et adresses — **jamais de désactivation globale** | `EWL` |
| Hygiène des secrets de laboratoire | mot de passe `sa` exposé pendant la mise en place **a été remplacé** ; mots de passe de spike générés aléatoirement, stockés **hors dépôt**, supprimés au nettoyage | pratique à conserver | Lots A–D |
| Chaîne d'approvisionnement | — | installeur PostgreSQL **hébergé par EDB** (S13) : vérification de signature et de provenance à intégrer à la procédure — **O10** | `OD` |
| Restauration de sauvegarde | — | traiter la restauration comme une **opération à privilège élevé** ; n'accepter que des sauvegardes issues du **périmètre de confiance** (recommandation officielle, S14) | `OD` |
| Données de santé | RGPD/BE + loi 09-08/CNDP MA ; gate **`G-CNDP`** ouvert ; audit et chiffrement au repos **absents** (ADR-009) | **hors périmètre de cet ADR** ; ni résolu ni aggravé par le choix de provider | — |

---

## 22. Conséquences

### 22.1 Positives

- La **trajectoire d'implémentation P4 devient déterminée** : P4-3 à P4-9 ont une cible unique, au lieu de
  devoir rester bi-provider ou d'être différées.
- **Aucun plafond d'édition** ne pèse sur une base dont les données de santé ne peuvent pas être supprimées
  physiquement (**D1**) — la capacité pratique restant bornée par l'OS, le matériel, le stockage et la
  configuration (S19).
- La **classification d'erreurs neuve** (**O5**) pourra reposer sur des **métadonnées structurées** plutôt que
  sur du texte de message (**D2**).
- Les fonctionnalités d'exploitation **retirées par l'édition Express** (compression de sauvegarde, sauvegarde
  chiffrée, log shipping, SQL Server Agent) **ne se posent pas** — bénéfice **réel mais non décisif** (§12.6).
- **Aucune redevance, aucun accord écrit** requis, y compris en usage commercial (S3).
- **Version supportée jusqu'au 2029-11-08**, avec une trajectoire de majeures continue (S2).
- **SQLite reste intact** en dev/test/démo/mono-poste : aucune régression immédiate, **1505** tests conservés.
- **La réversibilité est matérielle**, pas déclarative (§19.2).

### 22.2 Négatives et dette assumée

- **`DateTime.Now` ne peut pas être écrit vers PostgreSQL par le chemin EF.** Remédiation **obligatoire avant
  toute exécution de MMV sur PostgreSQL** (**O4**). SQL Server l'aurait toléré. **Coût accepté.**
- **Une réécriture de filtre d'index** est due, que SQL Server n'aurait pas exigée (**O3**). **Coût accepté.**
- **Toute erreur avorte la transaction entière** (`25P02`), y compris après deadlock : tout `catch` intra-
  transactionnel doit être audité (**O6**). SQL Server ne l'aurait pas exigé. **Coût accepté.**
- **Trois classes d'erreur sans `SqlState`** (timeout client, timeout de verrou, connexion refusée) : la
  classification devra passer par le **type et l'état de connexion** (**O5**).
- **La purge du pool devient une étape de procédure** (sauvegarde/restauration, reconnexion).
- L'**installeur Windows est hébergé par un tiers** (EDB), non par les serveurs communautaires.
- Le **travail d'infrastructure P4-3 à P4-12 reste entier** : rien n'est implémenté par cet ADR.
- **La compatibilité applicative complète reste non prouvée** (§16.5).

### 22.3 Neutres

- **La chaîne de sauvegarde planifiée** — construction, sécurisation, supervision, rétention, documentation —
  est due **quel que soit le provider** (§12.6, **O9**, **O10**) : la planification est **externe au moteur**
  dans les deux cas.
- **Le mapping monétaire exact** est dû **quel que soit le provider** (§12.2) — la décision ne l'allège ni ne
  l'aggrave.
- **L'import SQLite** est prouvé équivalent des deux côtés.
- **Le coût d'acquisition est nul des deux côtés.**

---

## 23. Position officielle des phases après cet ADR

```
P4-0                         = CLOSE
P4-1 SPIKE                   = COMPLETE
P4-1 LOT C                   = PASS
P4-1 LOT D                   = PASS
P4-1 PRIMITIVES              = 14 / 14
P4-2 ADR DRAFT               = COMPLETE
P4-2 ADR STATUS              = ACCEPTED
P4-2 PROVIDER DECISION       = ACCEPTED — POSTGRESQL
P4-2                         = CLOSE
P4-3                         = READY — NOT STARTED
```

**Lecture stricte.** PostgreSQL est **officiellement retenu** pour MMV V1 multi-poste. **SQL Server Express
n'est pas éliminé** (§19). **P4-3 peut commencer, dans un lot séparé** — **aucune implémentation n'est
contenue dans le présent ADR**. La V1 multi-poste **n'est pas déclarée `GO`** — les 18 critères de sortie de la
roadmap §4 restent à satisfaire.

---

## 24. Registre des sources officielles

Sources **S1 à S17 consultées le 2026-07-26** ; sources **S18 et S19 consultées le 2026-07-27**.
Sources **primaires** uniquement. **Aucun blog, forum, revendeur, agrégateur ni résumé généré** n'est cité
pour une limite, une licence, un cycle de vie ou un comportement de type.

| Réf. | Titre de la page | Éditeur | Lien stable | Fait exact soutenu |
|---|---|---|---|---|
| **S1** | *Editions and Supported Features of SQL Server 2022* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022> | Express : **taille max. d'une base relationnelle 10 Go** · **buffer pool 1 410 Mo** · **le moindre de 1 socket ou 4 cœurs** · **SQL Server Agent : No** · Backup compression / Encrypted backup / Log shipping / Database mail / Performance data collector / Standard performance reports / SQL Profiler : **No** · `MERGE` et upsert : Yes · Date and time data types : Yes · SSMS et `sqlcmd` : Yes · Express = *« entry-level, free database »* · *« can be seamlessly upgraded to other higher end editions »* |
| **S2** | *PostgreSQL: Versioning Policy* | PostgreSQL Global Development Group | <https://www.postgresql.org/support/versioning/> | *« supports a major version for **5 years** after its initial release »* · **17** → mineure **17.10**, fin de vie **2029-11-08** · 18 → 2030-11-14 · 16 → 2028-11-09 · 15 → 2027-11-11 · 14 → 2026-11-12 |
| **S3** | *PostgreSQL: License* | PostgreSQL Global Development Group | <https://www.postgresql.org/about/licence/> | *« Permission to use, copy, modify, and distribute this software and its documentation for any purpose, **without fee**, and **without a written agreement** is hereby granted »* — licence de type BSD/MIT, usage commercial inclus |
| **S4** | *SQL Server 2022 — Microsoft Lifecycle* | Microsoft Learn (Lifecycle) | <https://learn.microsoft.com/en-us/lifecycle/products/sql-server-2022> | **Fixed Lifecycle Policy** · début **2022-11-16** · **Mainstream End 2028-01-11** · **Extended End 2033-01-11** · s'applique à **toutes les éditions** sous Windows et Linux |
| **S5** | *Back up and restore of SQL Server databases* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/back-up-and-restore-of-sql-server-databases> | `BACKUP`/`RESTORE` = mécanisme natif · *« we recommend that you schedule regular backups as part of a database maintenance plan »* · chemins de planification cités sur cette page = **maintenance plan** et **SQL Server Agent job** *(indisponibles en Express — voir **S18** pour la voie que Microsoft documente pour Express)* · `BACKUP CHECKSUM` et `RESTORE VERIFYONLY` · restauration d'une sauvegarde non fiable = **opération à haut risque** |
| **S6** | *PostgreSQL: Documentation: 17: 13.1. Introduction* (MVCC) | PostgreSQL GDG | <https://www.postgresql.org/docs/17/mvcc-intro.html> | *« **reading never blocks writing and writing never blocks reading** »* |
| **S7** | *PostgreSQL: Documentation: 17: 13.2. Transaction Isolation* | PostgreSQL GDG | <https://www.postgresql.org/docs/17/transaction-iso.html> | *« **Read Committed is the default isolation level in PostgreSQL.** »* |
| **S8** | *PostgreSQL: Documentation: 17: 8.1. Numeric Types* | PostgreSQL GDG | <https://www.postgresql.org/docs/17/datatype-numeric.html> | `numeric` : *« Calculations with numeric values yield **exact** results »* · précision déclarable max **1000** · `real`/`double precision` : *« are **inexact**, variable-precision numeric types »* · `real` : *« a precision of **at least 6 decimal digits** »* · *« stored as approximations, so that storing and retrieving a value might show slight discrepancies »* |
| **S9** | *decimal and numeric (Transact-SQL)* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/t-sql/data-types/decimal-and-numeric-transact-sql> | *« numeric data types that have a **fixed precision and scale** »* · précision max **38** (défaut 18) · conversion `decimal`→`float`/`real` : *« **Possible loss of precision** »* |
| **S10** | *PostgreSQL: Documentation: 17: 11.8. Partial Indexes* | PostgreSQL GDG | <https://www.postgresql.org/docs/17/indexes-partial.html> | index partiels *« with arbitrary predicates »* via `WHERE` · **`CREATE UNIQUE INDEX … WHERE success`** — *« enforces uniqueness among the rows that satisfy the index predicate »* (forme booléenne nue, exactement l'adaptation mesurée) |
| **S11** | *Date and Time Handling — Npgsql Documentation* | Npgsql | <https://www.npgsql.org/doc/types/datetime.html> | Npgsql 6.0+ : *« trying to send a non-UTC DateTime as `timestamptz` will **throw an exception**  »* · `Utc` → `timestamp with time zone` · *« Local/Unspecified DateTimes are written as `timestamp without time zone` »* |
| **S12** | *datetime (Transact-SQL)* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetime-transact-sql> | *« Accuracy — **Rounded to increments of `.000`, `.003`, or `.007` seconds** »* · *« **Avoid using datetime for new work.** Instead, use the time, date, **datetime2**, and datetimeoffset data types … provide more seconds precision »* |
| **S13** | *PostgreSQL: Windows installers* | PostgreSQL GDG | <https://www.postgresql.org/download/windows/> | Voie d'installation Windows référencée = **installeur interactif certifié par EDB** · *« This installer is **hosted by EDB** and not on the PostgreSQL community servers »* · inclut serveur, **pgAdmin**, StackBuilder · plateformes **64 bits** uniquement |
| **S14** | *PostgreSQL: Documentation: 17: 25.1. SQL Dump* | PostgreSQL GDG | <https://www.postgresql.org/docs/17/backup-dump.html> | *« Dumps created by pg_dump are **internally consistent**, meaning, the dump represents a snapshot of the database at the time pg_dump began running »* · *« pg_dump **does not block other operations** on the database while it is working »* · *« Non-text file dumps should be restored using the **pg_restore** utility »* |
| **S15** | *Configure SQL Server to Listen on a Specific TCP Port* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/configure-a-server-to-listen-on-a-specific-tcp-port> | *« **Named instances** of the Database Engine … are configured for **dynamic ports**. This means they select an available port when the SQL Server service is started. **When you connect to a named instance through a firewall, configure the Database Engine to listen on a specific port**, so that the appropriate port can be opened in the firewall »* — corrobore la nécessité du port fixe 1433 constatée au Lot C |
| **S16** | *Create Filtered Indexes* | Microsoft Learn | <https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes> | index filtrés via prédicat `WHERE` · *« Filters can't be applied to primary key or unique constraints, but **can be applied to indexes with the `UNIQUE` property** »* · *« only support **simple comparison operators** »* · pas de `LIKE` · pas de colonne calculée dans le filtre |
| **S17** | *Npgsql Entity Framework Core Provider* | Npgsql | <https://www.npgsql.org/efcore/> | correspondance provider **8.x** ↔ **EF Core 8** / `net8.0` — cohérente avec le `Npgsql.EntityFrameworkCore.PostgreSQL` **8.0.11** employé par le harness |
| **S18** | *Schedule and automate backups of databases* | Microsoft Learn | <https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/backup-restore/schedule-automate-backup-database> | **SQL Server Express n'inclut pas SQL Server Agent et ne permet donc ni tâches planifiées ni plans de maintenance** — *« SQL Server Express editions don't offer a way to schedule either jobs or maintenance plans because the SQL Server Agent component isn't included in these editions »* · **et Microsoft documente le Planificateur de tâches Windows comme méthode d'automatisation des sauvegardes d'Express** — *« how to use a Transact-SQL script and Windows Task Scheduler to automate backups of SQL Server Express databases on a scheduled basis »*, via `sp_BackupDatabases` + `sqlcmd` (sauvegardes complètes / différentielles / de journal) · *« **The script doesn't contain the logic to clean up old files.** »* (rétention non fournie) · *« the passwords are stored in clear text »* si authentification SQL (hygiène de secret) · service Task Scheduler recommandé en démarrage **Automatic** |
| **S19** | *PostgreSQL: Documentation: 17: Appendix K. PostgreSQL Limits* | PostgreSQL GDG | <https://www.postgresql.org/docs/17/limits.html> | Table K.1 : **taille de base = `unlimited`** · **taille d'une relation = 32 To** avec le `BLCKSZ` par défaut de 8192 octets · lignes par table limitées par le nombre de tuples tenant sur 4 294 967 295 pages · **1 600** colonnes par table (réduit par la contrainte de tuple sur une page de 8192 octets). Employé pour l'énoncé **« pas de plafond d'édition comparable »**, **jamais** pour affirmer une absence de limite physique |

### 24.1 Affirmations dépendantes d'une source officielle et **NON VÉRIFIÉES**

Énumérées pour qu'aucune ne soit lue comme établie. **Aucune ne fonde un constat décisif.**

| Affirmation | Statut | Traitement |
|---|---|---|
| « PostgreSQL ne possède aucun ordonnanceur de tâches intégré » | **NON VÉRIFIÉ** — aucune source officielle consultée n'énonce cette absence | **non invoqué.** §12.6 et §17 énoncent seulement que la planification est **externe au moteur dans les deux cas**, ce qui est **documenté pour Express** (S18) et constaté pour PostgreSQL |
| « L'automatisation des sauvegardes d'Express est indocumentée / non supportée / plus artisanale que celle de PostgreSQL » | **RÉFUTÉ par une source officielle** (S18) | **retiré de l'ADR.** C'était l'erreur matérielle corrigée le 2026-07-27 : Microsoft documente la voie `sp_BackupDatabases` + `sqlcmd` + Planificateur de tâches. L'absence d'Agent est **rétrogradée** en désavantage non décisif (§9, §10, §12.6) |
| « PostgreSQL n'a aucune limite de taille, de RAM ou de CPU » | **NON VÉRIFIÉ / incorrect sans qualification** | **retiré.** L'énoncé retenu est : la documentation liste la **taille de base comme `unlimited`** et PostgreSQL **n'a pas de plafonds d'édition comparables** à Express ; la **capacité pratique reste bornée par l'OS, le matériel, le stockage et la configuration**, et une relation est plafonnée à **32 To** (S19, §11.1) |
| Coût monétaire d'une montée d'édition SQL Server | **NON VÉRIFIÉ** — aucun tarif consulté | **aucun coût ni caractère payant n'est affirmé.** §11.1 énonce uniquement le fait officiel : la sortie documentée est une **édition supérieure** (S1). |
| Matrice complète Npgsql ↔ versions serveur PostgreSQL | **PARTIELLEMENT VÉRIFIÉ** (S17) | seule la correspondance **majeure provider ↔ majeure EF Core** est invoquée |
| Facilité, durée ou simplicité comparée d'installation | **NON VÉRIFIÉ / NOT_PROVED** | **aucune installation n'est qualifiée de « simple » ou « facile »** : aucun protocole complet n'a été chronométré de bout en bout |
| Comportement du moteur SQL Server **sous Windows** pour E1 → E18 | **NOT_PROVED** | le moteur a été testé **sur Linux** (conteneur) ; le Lot C n'a exercé que du SQL brut sous Windows |
| Volumétrie réelle de MMV | **NOT_PROVED** | §11.1 s'appuie sur l'**asymétrie du risque de plafond d'édition** et sur la **rétention monotone** du modèle, **jamais** sur une prévision de taille |
| Performance comparée des deux dialectes d'upsert | **NOT_PROVED** | *« `UPDLOCK, HOLDLOCK` a été mesuré **correct**, **pas** « au même coût » que `ON CONFLICT DO NOTHING` »* |
| Compétences d'équipe / capacité de support | **NOT_PROVED** | **exclu de la décision** (§6.5, critère 18) |

---

## 25. Liste de contrôle de validation

### 25.1 Discipline documentaire

| # | Contrôle | État |
|---|---|---|
| 1 | Exactement **un** ADR nouveau créé, sous `docs/architecture/` | ✅ |
| 2 | Identifiant **vérifié** contre la convention réelle du dépôt, non inventé — `ADR-PROD-DB-002`, suivant de `ADR-PROD-DB-001`, seule série numérotée | ✅ |
| 3 | **Exactement deux** fichiers de documentation modifiés : ce fichier + `P4-multi-poste-roadmap.md` | ✅ |
| 4 | **Aucun** fichier `src/**`, `tests/**`, `spikes/**`, migration, snapshot EF, `*.csproj`, `MMV.sln`, `.github/**`, UI | ✅ |
| 5 | **Aucune implémentation**, aucun paquet, aucun schéma, aucune migration | ✅ |
| 6 | Statut = **`ACCEPTÉ`** ; exactement un provider officiellement retenu : PostgreSQL ; décision acceptée ; SQL Server Express rejeté pour V1 mais non éliminé ; risques et obligations inchangés ; aucune revendication d'implémentation | ✅ |
| 7 | ADR et roadmap **concordants** (§23 ↔ roadmap §6) | ✅ |
| 8 | Tables Markdown et blocs de code **valides** | ✅ |
| 9 | **Aucun secret, aucun mot de passe, aucune chaîne de connexion complète** | ✅ |

### 25.2 Discipline probatoire

| # | Contrôle | État |
|---|---|---|
| 10 | Niveaux de preuve **séparés explicitement** (§6.1) et appliqués partout | ✅ |
| 11 | **Aucune mesure locale convertie en vérité générale** | ✅ |
| 12 | **Jamais affirmé que la CI a exécuté E18** (§6.2) | ✅ |
| 13 | Preuves manuelles du Lot C attribuées à l'**opérateur**, corroborées par snapshot, **non** présentées comme intégration applicative MMV (§6.3) | ✅ |
| 14 | **Aucune revendication de compatibilité MMV complète** sur l'un ou l'autre serveur (§16.5) | ✅ |
| 15 | Toute affirmation externe porte une **citation officielle** (§24) ; les non vérifiées sont **listées** (§24.1) | ✅ |
| 16 | **Aucun score pondéré arbitraire** ; matrice de preuves + raisonnement (§8) | ✅ |
| 17 | Décision **non** fondée sur « candidat principal / secondaire » (§2.2), la préférence, la familiarité, un décompte de `PASS` (§13) ou un coût non vérifié (§12.4) | ✅ |
| 18 | **Exactement un** provider est retenu : PostgreSQL (§4.1) ; décision acceptée | ✅ |
| 19 | Constats **décisifs** (§9, §11) et **non décisifs** (§10, §12) distingués | ✅ |
| 20 | **Rejet distingué de l'élimination** ; option future **explicitement maintenue** (§19.2) | ✅ |

### 25.3 Complétude imposée

| # | Contrôle | État |
|---|---|---|
| 21 | **20 / 20** critères obligatoires traités (§14) | ✅ |
| 22 | Preuves projet obligatoires enregistrées : E1–E17, **14** primitives, **13/14** initial, deadlock, perte de connexion, retry borné, RCSI, import SQLite réduit (§6.4) | ✅ |
| 23 | Lot C : installation Windows native, TCP, authentification distante, CRUD brut, redémarrage, reprise de service, sauvegarde/restauration — **et sa limite** (§6.4) | ✅ |
| 24 | Lot D : couplage `SqliteParameter` corrigé, paramètres fabriqués par le provider, `ON CONFLICT` SQLite/PostgreSQL, `INSERT … SELECT … WHERE NOT EXISTS` + `UPDLOCK, HOLDLOCK`, **aucun `check-then-act`**, 6 tests SQLite, méthode de production testée localement, **20/20 par provider**, **14/14** cumulés, **E18 hors CI** (§6.4) | ✅ |
| 25 | **5 risques ouverts obligatoires** enregistrés sans résolution prématurée : `DateTime`, filtres booléens PostgreSQL, monétaire `REAL`, `PersistenceErrorMapper`, compatibilité applicative complète (§16) | ✅ |
| 26 | Structure ADR requise **complète** : titre + identifiant · statut · date · contexte · question · décision · portée · base de preuves · matrice · décisifs · non décisifs · risques ouverts · conséquences · obligations · sécurité/secrets · exploitation/sauvegarde · migration · alternative rejetée · déclencheurs de réexamen · registre de sources · liste de contrôle | ✅ |

### 25.4 Correction matérielle du 2026-07-27 — contrôles spécifiques

| # | Contrôle | État |
|---|---|---|
| 27 | Analyse de l'automatisation des sauvegardes d'Express **corrigée** : Microsoft **documente** la voie `sp_BackupDatabases` + `sqlcmd` + Planificateur de tâches Windows (**S18** ajoutée) | ✅ |
| 28 | **Aucune** affirmation résiduelle que l'automatisation Express serait **indocumentée**, un **contournement non supporté**, ou **nécessairement plus artisanale** que la planification PostgreSQL | ✅ (§10, §12.6, §24.1) |
| 29 | Absence de SQL Server Agent **reclassée** : désavantage d'exploitation **réel**, **non décisif à lui seul** | ✅ (§9, §10, §12.6) |
| 30 | Obligation **symétrique** énoncée : MMV doit **construire, sécuriser, superviser, doter d'une rétention et documenter** sa chaîne de sauvegarde planifiée **pour l'un comme pour l'autre** candidat | ✅ (§12.6, §17, §22.3, O9/O10) |
| 31 | Constats décisifs **réconciliés à deux** (D1 plafonds d'édition · D2 identification des contraintes) ; **aucun troisième critère fabriqué** pour préserver l'ancienne numérotation | ✅ (§9) |
| 32 | Renumérotation propagée : décision, matrice (§7.8, §7.12, §7.13), table décisive (§9), non décisifs (§10), §11, §12.4, §14, §19.1, §20.1, §22, §24.1 | ✅ |
| 33 | Langage sur les limites PostgreSQL **qualifié** : taille de base listée `unlimited`, **pas de plafond d'édition comparable**, capacité pratique **bornée par OS/matériel/stockage/configuration**, relation ≤ 32 To (**S19** ajoutée). **Jamais** « RAM illimitée », « CPU illimité » ni « aucune limite technique » | ✅ (§7.13, §11.1, §24.1) |
| 34 | « Non corrigeable par du code MMV » **différencié** : D1 = contrainte d'**édition**, non corrigeable ; D2 = parsing **techniquement implémentable mais moins robuste**, distinction **maintenue malgré l'existence d'un contournement** | ✅ (§8, §9 col. 5, §11.1, §11.2) |
| 35 | Inférence sur le média français **corrigée** : média d'installation en français ⇒ **localisation = considération concrète de déploiement**, mais le **nom du média ne prouve pas** la langue effective de chaque message d'erreur à l'exécution | ✅ (§11.2) |
| 36 | **Préservés** : statut `PROPOSÉ` · provider proposé **PostgreSQL** · SQL Server Express **rejeté pour V1 mais non éliminé** · tous les désavantages PostgreSQL mesurés · risque `DateTime` · adaptation du filtre booléen · risque monétaire `REAL` · travaux `PersistenceErrorMapper` · compatibilité MMV complète **`NOT_PROVED`** · distinction E18 local / CI · **aucune revendication d'implémentation** | ✅ |

---

## 26. Verdict documentaire

```
P4-1 SPIKE                   = COMPLETE
P4-2 ADR DRAFT               = COMPLETE
P4-2 ADR STATUS              = ACCEPTED
P4-2 PROVIDER DECISION       = ACCEPTED — POSTGRESQL
P4-2                         = CLOSE
P4-3                         = READY — NOT STARTED
```

**Contraintes respectées** : aucun `git add .` / `git add -A` ; aucun force-push ; `src/**`, `tests/**`,
`spikes/**`, migrations, snapshot EF, paquets, `MMV.sln`, `.github/**` et UI **intacts** ; implémentation du
Lot D **non modifiée** ; aucun paquet provider ajouté ; aucune migration serveur créée ; `DateTime`, mapping
monétaire et filtres d'index PostgreSQL **non corrigés** ; aucun secret ni chaîne de connexion complète.
**Faits enregistrés** : draft proposé enregistré par `37812c7` ; CI `30656580004` verte sur le SHA exact ;
présent changement **exclusivement documentaire** — aucun code, paquet, migration, snapshot, workflow ou UI
modifié ; acceptation effective après CI verte du commit courant. Statut **`ACCEPTÉ`** ; **SQL Server Express
rejeté pour V1 mais NON éliminé**.

---

*ADR-PROD-DB-002 — rédaction initiale le 2026-07-26 sur la base `e8d0546` / CI `30215445242` (verte, `headSha`
exact, 1505 tests). Révision le 2026-07-27. Draft proposé enregistré le 2026-07-31 par `37812c7`, CI
`30656580004` verte sur le SHA exact. Acceptation formelle le 2026-07-31. PostgreSQL retenu. SQL Server
Express non éliminé.*
