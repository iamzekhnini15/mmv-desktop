# P4-5B PostgreSQL Architecture Decisions

**Lot :** P4-5B — formalisation des décisions d'architecture PostgreSQL
**Nature :** passe **STRICTEMENT DOCUMENTAIRE**. **Aucun fichier de code, de test, de migration, de projet,
de configuration ou de CI n'a été modifié.** Aucun `DbContext`, aucune migration, aucun mapping EF n'a été
créé ni touché.
**Statut :** `P4_5B_DECISIONS = COMPLETE — ADRs ACCEPTED`

---

## 0. Base Git et discipline de preuve

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| SHA de départ | `b49f2ff7c4e3af665a767f508268377be9d19943` |
| Dernier commit en entrée | `docs(P4-4C): reconcile P4-4 implementation state` |
| Arbre de travail à l'ouverture | un seul fichier non suivi : le rapport P4-5A |
| Fichiers produits par ce lot | **6 ADR** + **ce rapport** + **mise à jour de la roadmap P4** |

### 0.1 Ce que ce lot prouve, et ce qu'il ne prouve pas

- **Aucun test n'a été exécuté.** Ce lot ne produit ni build, ni baseline, ni CI. La baseline enregistrée du
  dépôt reste **1569** tests (P4-4C) : elle est **citée**, jamais revendiquée ici.
- **Aucune preuve runtime n'a été produite.** Tout ce que l'audit P4-5A étiquetait `NEEDS_RUNTIME_PROOF` le
  reste intégralement. **La compatibilité applicative complète de MMV sur PostgreSQL demeure `NOT_PROVED`.**
- **Ce lot décide.** C'est sa seule production : six décisions, écrites, motivées, avec leurs options
  rejetées et leurs conséquences négatives.
- **Deux faits nouveaux ont été établis par lecture du dépôt** pendant ce lot (`STATIC_CODE_PROOF`) et ont
  **modifié une décision** — voir §ADR-1. Ils ne figuraient pas dans l'audit P4-5A.

---

## Objective

Transformer les constats de l'audit [P4-5A](P4-5A-postgresql-schema-audit-report.md) en **décisions
d'architecture officielles**, et répondre à :

> **Quelle architecture PostgreSQL devons-nous implémenter pour supporter le multi-poste magasin de manière
> professionnelle ?**

L'audit P4-5A posait six décisions requises (`AD-1` … `AD-6`) **sans en trancher aucune**. P4-5B les tranche,
les documente au format ADR du projet, et **prépare** l'implémentation sans la commencer.

---

## Input Documents

| Document | Ce qui en a été repris |
|---|---|
| [P4-5A — audit du schéma de production](P4-5A-postgresql-schema-audit-report.md) | inventaires (14 colonnes monétaires, 21 colonnes `DateTime`, 17 `DateTime.Now`, 16 index, 30 `HasColumnType`, 14 migrations), trois blocages `B1`/`B2`/`B3`, six décisions requises `AD-1` … `AD-6` |
| [ADR-PROD-DB-002 — choix du provider](../architecture/adr-prod-db-002-server-database-provider-selection.md) | obligations **O2, O3, O4, O7, O12, O14** ; §12.2 (monétaire), §16.1 (`DateTime`), §16.2 (index filtrés), §19.2 (SQL Server non éliminé) |
| [ADR-PROD-DB-001 — stratégie multi-poste](../architecture/adr-prod-db-001-multi-poste-database-strategy.md) | production multi-poste = exigence produit ; SQLite local conservé pour dev/test/démo |
| [ADR-005 — Temps & fuseaux](../architecture/adr-candidates.md#adr-005) | orientation différée « horloge injectable + stockage UTC », **close** par ADR-PROD-DB-004 |
| [P2A-1R19](P2A-1R19-report.md) | suppression des défauts SQL de date, mention explicite d'un « futur `IClock` » |
| [Roadmap P4](../architecture/P4-multi-poste-roadmap.md) | 18 critères de sortie, découpage P4-5 … P4-12 |
| Dépôt au SHA `b49f2ff` | vérifications directes : `SaleRepository`, `NotificationRepository`, `WorkshopSheetConfiguration`, `OpticDbContextFactory`, `DatabaseProviderResolver`, `ci.yml`, configurations EF |

---

## Decisions Summary

**Six ADR créées, toutes `ACCEPTED`.** Elles poursuivent la numérotation de la série production-base du
projet et vivent dans `docs/architecture/`, au format des ADR existantes.

| Audit P4-5A | ADR créée | Décision retenue | Obligation ADR-002 close | Lot d'implémentation |
|---|---|---|---|---|
| `AD-1` | [**ADR-PROD-DB-003** — persistance monétaire](../architecture/adr-prod-db-003-money-persistence.md) | `decimal` + `HasPrecision(12,2)` ⇒ **`numeric(12,2)`** en production ; **type physique SQLite inchangé**, par un point de sélection unique | **O2** | P4-5C |
| `AD-2` | [**ADR-PROD-DB-004** — stratégie temporelle](../architecture/adr-prod-db-004-datetime-strategy.md) | **UTC pour tout instant** + **`IClock`** + **convertisseur validant** (lève si `Kind != Utc`) + test d'architecture ; **`DateOnly`** pour les dates civiles | **O4** | P4-5D |
| `AD-3` | [**ADR-PROD-DB-005** — architecture des migrations](../architecture/adr-prod-db-005-migration-architecture.md) | **Deux assemblys de migrations, un seul `DbContext`, un seul modèle** ; 14 migrations SQLite **intouchées** | **O7** | P4-5E |
| `AD-4` | [**ADR-PROD-DB-006** — index et portabilité du modèle](../architecture/adr-prod-db-006-index-and-model-portability.md) | **Filtre d'index sélectionné par provider** (motif déjà prouvé en production) ; retrait des 18 littéraux `"REAL"` optiques | **O3** | P4-5C |
| `AD-5` | [**ADR-PROD-DB-007** — prévention de la dérive](../architecture/adr-prod-db-007-schema-drift-prevention.md) | **Double contrôle de dérive** (une exécution par chaîne, sans serveur) + **vérification physique** du schéma + `EnsureCreated()` interdit en intégration | — | P4-5E / P4-5F |
| `AD-6` | [**ADR-PROD-DB-008** — tests d'intégration](../architecture/adr-prod-db-008-postgresql-integration-testing.md) | **Projet d'intégration dans `MMV.sln`**, serveur désigné par variable d'environnement, **fourni par la CI** ; **un test ignoré en CI = échec** | prépare **O12** | P4-5F |

**Le fil conducteur des six décisions** : les garanties métier sont arbitrées **par la base** et doivent le
rester ; ce qui dépend du moteur est **sélectionné explicitement en un point unique d'Infrastructure**,
jamais deviné ; et **rien n'est déclaré porté sans preuve exécutée contre un vrai serveur**.

---

## ADR-1 Money

→ [**ADR-PROD-DB-003 — Persistance monétaire**](../architecture/adr-prod-db-003-money-persistence.md)

### Problème

14 colonnes monétaires, **deux mappings incohérents** : 11 en `HasColumnType("REAL")`, 3 sans mapping (donc
`TEXT` sur SQLite). **Aucune précision déclarée.** Et surtout : `HasColumnType("REAL")` est transmis
**verbatim** au provider actif, or `REAL` désigne **8 octets** en SQLite et **4 octets** en PostgreSQL.
Porter tel quel **dégraderait** une précision déjà insuffisante.

### Fait nouveau, établi pendant ce lot

Deux sites où **la base, et non l'application, opère sur un montant** :

- [SaleRepository.cs:118](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L118) —
  `UPDATE … WHERE "RemainingAmount" > 0`, la **primitive CAS** du règlement de solde ;
- [SaleRepository.cs:92](../../src/MMV.Infrastructure/Repositories/SaleRepository.cs#L92) —
  `SUM("FinalAmount")` côté serveur.

Tous les autres calculs monétaires de `src/**` sont **en mémoire**.

**Conséquence, et elle a changé la décision** : basculer les colonnes SQLite sur `TEXT` — ce que
produirait le mapping `decimal` par défaut — transformerait `"RemainingAmount" > 0` en **comparaison de
chaînes**, où `'0.00' > '0'` est **vrai**. Un solde **déjà réglé** redeviendrait réglable, **sans erreur ni
signal**. La recommandation de l'audit P4-5A (`AD-1`, option 1, mapping unique) aurait **introduit cette
régression sur le provider de dev/test/démo**.

### Options et décision

| Option | Verdict |
|---|---|
| A — `HasPrecision(12,2)` pour les deux providers, mapping unique | **rejetée** : bascule SQLite en `TEXT` ⇒ régression ci-dessus + rebuild de table sur les bases déployées |
| B — entier de centimes (`long`) | **rejetée** : réécriture Domain/Application/UI, données illisibles en base, hors proportion |
| **C — `HasPrecision(12,2)` + type physique SQLite conservé, sélection en un point unique** | **RETENUE** |

**Décidé** : `decimal` en CLR (inchangé) ; `HasPrecision(12, 2)` sur les 14 colonnes ⇒ **`numeric(12,2)`**
sur PostgreSQL ; **aucun littéral `"REAL"` transmis à PostgreSQL** ; **mapping physique SQLite identique à
aujourd'hui**, appliqué par une **seule** fonction d'Infrastructure ; la **dette monétaire SQLite est
enregistrée et acceptée**, elle n'est pas un critère de sortie P4 ; **O2 précède O11**.

### Conséquences

- **Positives** : exactitude financière en production ; régression `float8` → `float4` évitée ; **aucune
  migration SQLite et aucun rebuild de table** — le risque R-6 de l'audit disparaît pour le volet monétaire ;
  les deux sites SQL conservent leur sémantique.
- **Négatives, assumées** : le modèle EF devient dépendant du provider (d'où ADR-PROD-DB-005) ; la dette
  monétaire SQLite subsiste en dev/test/démo/mono-poste ; **l'exactitude monétaire ne peut être prouvée que
  contre PostgreSQL** — un test vert sur SQLite ne vaut pas preuve.

---

## ADR-2 DateTime

→ [**ADR-PROD-DB-004 — Stratégie temporelle**](../architecture/adr-prod-db-004-datetime-strategy.md)

### Problème

Npgsql ≥ 6 **exige `Kind = Utc`** : **MMV ne peut écrire aucune date vers PostgreSQL par le chemin EF**, et
par SQL brut une heure locale serait **stockée comme si elle était UTC** (2 h d'écart silencieuses).
**17 sites `DateTime.Now`** subsistent, dont le défaut d'entité `Notification.CreatedAt` et une **comparaison
traduite en SQL**. Et le multi-poste ajoute un risque **structurellement nouveau** : l'horodatage dépend du
**fuseau et de l'horloge de chaque poste**, donc la **chronologie d'une base commune** peut être fausse —
mouvements de stock, alertes, transitions de commande, traçabilité d'ordonnance.

### Décisions

| Sujet | Décision |
|---|---|
| **Stockage des instants** | **UTC**, `timestamp with time zone`, mapping Npgsql par défaut, aucun littéral de type |
| **Génération** | **`IClock`** injectable, interface en Domain (neutre), implémentation en Infrastructure ; `DateTime.UtcNow` n'est plus appelé qu'à cet endroit |
| **Les 17 `DateTime.Now`** | supprimés de `src/**`, en commençant par `Notification.CreatedAt` et `OrderRepository:167` |
| **Défense** | **convertisseur de valeur *validant*** : écriture ⇒ **lève** si `Kind != Utc` ; lecture ⇒ `SpecifyKind(Utc)`. Rejet explicite du convertisseur **silencieux**, qui masquerait la faute |
| **Garde** | **test d'architecture bloquant** interdisant `DateTime.Now` / `Today` / `DateTimeOffset.Now` dans `src/**`, liste blanche UI explicite |
| **Affichage** | conversion en heure locale **à l'affichage seulement** ; les 6 conversions `DateTimeOffset` de l'UI sont auditées une à une |
| **Dates civiles** | `Customer.BirthDate` et `Prescription.IssueDate` ⇒ **`DateOnly`** (`date` PostgreSQL, `TEXT` `yyyy-MM-dd` SQLite) ; **ni converties en UTC, ni soumises au convertisseur** |
| **Reprise des données** | règle fixée ici, **exécutée en P4-7** : valeurs existantes interprétées comme heure locale du magasin d'origine, conversion consignée dans le rapport d'import, **aucune interprétation silencieuse** |
| **Horloge des postes** | **synchronisation NTP** = exigence d'exploitation documentée (P4-8), dérive supervisée (P4-9) |

**Deux choix méritent d'être signalés au Lead Architect :**

1. **Le convertisseur validant** (lever plutôt que corriger) est une **variante** de ce que proposait l'audit
   `AD-2`. Motif : un convertisseur silencieux transformerait un bogue en donnée fausse ; celui-ci fait
   échouer l'écriture **y compris sur SQLite**, donc **la suite de tests existante détecte la faute sans
   serveur**. Sa branche lecture supprime en outre la divergence de `Kind` entre providers.
2. **Le passage à `DateOnly` exige une migration de données écrite à la main.** Le type de colonne SQLite
   reste `TEXT` : **EF ne détectera aucun changement de schéma** alors que le **format** des valeurs change.
   **C'est le point le plus dangereux du lot P4-5D** — l'oublier corromprait la lecture des dates de
   naissance et d'ordonnance sur les bases existantes.

### Faut-il une ADR ? — oui, et elle est écrite

[ADR-005](../architecture/adr-candidates.md#adr-005) avait **déjà** rejeté `DateTime.Now` et orienté vers
horloge injectable + UTC, mais laissait trois points ouverts : dates civiles, mécanisme de défense, reprise
des données. **ADR-PROD-DB-004 les tranche et clôt ADR-005** pour le périmètre V1.

---

## ADR-3 Migration Architecture

→ [**ADR-PROD-DB-005 — Architecture des migrations**](../architecture/adr-prod-db-005-migration-architecture.md)

**C'est la décision la plus structurante du lot.**

### Problème

EF Core entretient **un** `ModelSnapshot` par couple (`DbContext`, assembly de migrations). Il faut **deux
chaînes** — SQLite historique **intouchable**, PostgreSQL neuve — pour **un seul** modèle métier. Or les
décisions ADR-PROD-DB-003 et ADR-PROD-DB-006 rendent **le modèle lui-même** dépendant du provider : ce n'est
donc pas seulement l'historique qui diffère, **l'instantané diffère**.

### Options et décision

| Option | Verdict |
|---|---|
| **1 — deux assemblys de migrations, un seul `DbContext`, un seul modèle** | **RETENUE** |
| 2 — deux `DbContext` de migrations | **rejetée** : deux types de contexte pour une base unique, alors que repositories, `UnitOfWork`, `EfTransactionRunner` et la DI dépendent tous d'`OpticDbContext` ; aucun gain sur la contrainte réelle |
| 3 — baseline SQL écrite à la main | **rejetée** : supprime `has-pending-model-changes`, donc la seule garde automatique ; schéma de production non reproductible par l'outillage |

**Décidé** : un seul modèle et un seul contexte ; la chaîne SQLite **reste exactement où elle est, sans la
moindre modification** ; une assembly de migrations PostgreSQL dédiée porte la **baseline propre et son
instantané** ; la sélection se fait **dans `DatabaseProviderResolver`**, là où le provider est déjà
sélectionné depuis P4-3 ; la **factory design-time devient sélective, avec SQLite par défaut** — donc la CI
existante est inchangée ; **aucune connexion serveur n'est requise pour générer une migration** ; **tout
changement de modèle produit ses migrations sur les deux chaînes dans le même commit**, ou la preuve
explicite que l'autre chaîne est inchangée.

**Non décidé ici, et à ne pas confondre** : **qui applique** les migrations en multi-poste, et comment les
postes sont sérialisés. `Migrate()` par poste est **inadapté** à une base centrale. Ce sujet reste **entier**
et appartient à **P4-6 (O8)**.

---

## ADR-4 Index Strategy

→ [**ADR-PROD-DB-006 — Index et portabilité du modèle**](../architecture/adr-prod-db-006-index-and-model-portability.md)

### Problème

Sur 16 index déclarés, **deux sont filtrés** et **un seul est invalide** :

- `idx_workshop_sheets_current_unique`, filtre `"IsCurrent" = 1` : PostgreSQL crée une colonne **`boolean`**
  et **refuse** de la comparer à un entier. **La création de l'index échoue, donc la création du schéma
  échoue.**
- `idx_notifications_active_low_stock_unique` : comparaisons de texte et tests `IS NULL` — **portable tel
  quel**. Cette précision **réduit le périmètre d'O3 à un seul index**.

L'index invalide porte une **garantie métier** : au plus **une** version de fiche atelier courante par
commande. **Sa perte serait une perte de garantie, pas une perte d'index.**

### Décisions

1. **Filtre sélectionné par provider** dans la configuration EF (`"IsCurrent" = 1` / `"IsCurrent"`), avec
   **échec explicite sur provider inconnu** — exactement le motif **déjà en production et prouvé** de
   `NotificationRepository.ActiveLowStockInsertSql`. Options rejetées : filtre relégué aux migrations (dérive
   permanente modèle ↔ base), suppression du filtre au profit d'un contrôle applicatif (**contraire au
   principe P3 « la base décide »**), index unique non filtré (interdirait tout historique de fiches).
2. **Le nom du provider est fourni au modèle en un point unique** ; aucune configuration d'entité ne
   l'interroge pour son compte.
3. **Les 18 `HasColumnType("REAL")` optiques sont retirés** (`double` → `double precision`, sans perte).
   `Product.TechnicalSpecs` **garde** `TEXT` ; **`jsonb` est rejeté pour V1**.
4. **Un index filtré se vérifie physiquement** (`pg_index`), jamais par lecture du modèle : un index absent
   ne fait échouer aucun test fonctionnel — il ne se voit **qu'en concurrence, et trop tard**.
5. **La divergence de `lower()` est actée** : sur PostgreSQL la recherche remontera **davantage** de
   résultats (accents, Unicode). **Ce comportement devient le comportement attendu**, verrouillé par test sur
   chaque provider.

---

## ADR-5 Schema Drift Prevention

→ [**ADR-PROD-DB-007 — Prévention de la dérive de schéma**](../architecture/adr-prod-db-007-schema-drift-prevention.md)

### Problème

La CI exécute `has-pending-model-changes` **une seule fois, sans configuration serveur**, donc **contre
SQLite**. Avec deux chaînes, un développeur pourrait modifier le modèle, générer la migration SQLite,
oublier la migration PostgreSQL — **et la CI resterait verte**. La dérive du schéma de **production**
deviendrait invisible (risque R-5).

**Trois formes de dérive**, distinctes et toutes nommées : **D-A** modèle ↔ migrations · **D-B** migrations ↔
schéma réellement créé · **D-C** base de production ↔ migrations.

### Décisions

1. **Le contrôle de dérive est exécuté une fois par chaîne, et les deux sont bloquants.** **Aucun serveur
   n'est requis** : ni la génération ni la comparaison ne se connectent.
2. **Un seul chemin de génération** : la factory design-time sélective, SQLite par défaut.
3. **Vérification physique** du schéma PostgreSQL créé par migrations, **par lecture des catalogues**
   (`information_schema`, `pg_index`) : colonnes et types, index uniques, **les deux index filtrés et leur
   prédicat**, FK et comportements de suppression.
4. **Reproductibilité prouvée** : base neuve migrée **deux fois**, schéma identique (critère de sortie n° 4).
5. **`EnsureCreated()` interdit** en production et dans **tout** test d'intégration PostgreSQL. Les **114**
   sites `EnsureCreated()` existants restent en place — mais il est **acté** qu'ils ne prouvent **rien** sur
   les migrations et ne doivent jamais être cités comme tels.
6. **La dérive D-C est traitée au démarrage** par une garde de version — **P4-6 (O8)**, pas P4-5.
7. **Le schéma de production ne se modifie jamais à la main.**

**Conséquence à assumer** : `ci.yml` sera modifié. C'est la **première modification du workflow depuis
P4-0**, et elle doit être faite **sans toucher au reste du fichier**.

---

## ADR-6 Integration Testing

→ [**ADR-PROD-DB-008 — Tests d'intégration PostgreSQL**](../architecture/adr-prod-db-008-postgresql-integration-testing.md)

### Problème

**Zéro test d'intégration serveur dans `MMV.sln`.** Les 5 fichiers mentionnant PostgreSQL testent la
résolution, la classification, l'architecture et le dialecte — **aucun ne se connecte à un serveur**. Le
harness du spike est **hors solution** et la CI ne le rejoue pas. Or **des critères de sortie P4 dépendent
désormais de preuves serveur**.

**Le risque à conjurer en premier est le faux vert** : un test qui s'ignore faute de serveur et qu'on lit
comme un succès est **pire que l'absence de test**.

### Décisions

| Sujet | Décision |
|---|---|
| **Emplacement** | **projet d'intégration dédié, inscrit dans `MMV.sln`**, PostgreSQL uniquement. Promouvoir le harness du spike est **rejeté** : il compare des providers et porte un `AdaptedOpticDbContext` **divergent** du modèle de production |
| **Serveur** | désigné par une **variable d'environnement de test**, distincte de la production. **La CI fournit réellement le serveur**, par un service conteneurisé, dans un **job distinct** |
| **Testcontainers** | **rejeté comme mécanisme obligatoire** — impose Docker sur chaque poste et un paquet NuGet nouveau, **aujourd'hui non restaurable** (P4-5A §0.2). **Non éliminé** : adoptable plus tard derrière le **même** contrat de variable, sans réécrire un test |
| **Anti-faux-vert** | en local, ignorance **explicite et actionnable** ; en **CI, un test ignoré est un échec**, et un nombre de tests nul aussi |
| **Schéma de test** | créé **par migrations**, jamais par `EnsureCreated()` — les migrations sont **l'objet du test** |
| **Isolation** | base créée puis supprimée à chaque exécution ; aucun état partagé |
| **Baseline** | le compte **1569** désigne la suite **unitaire** ; les tests d'intégration sont comptés **séparément** — les fusionner ferait passer un serveur absent pour une baseline en hausse |
| **Corpus obligatoire** | **N1** migrations reproductibles · **N2** vérification physique du schéma · **N3** fidélité monétaire · **N4** fidélité `DateTime` avec assertion · **N5** CRUD complet par les repositories de production · **N6** **re-preuve des 14 primitives (O12)** · **N7** rollback et `25P02` · **N8** divergence `lower()` |
| **Hors périmètre** | concurrence multi-processus, perte réseau, reconnexion, timeout, interblocage → **P4-10 (O13)** |

**Effet notable** : **N6 débloque P4-4**, dont la validation runtime est aujourd'hui
`BLOCKED_BY_P4_5_SCHEMA`. Et **N7 fait passer O6 du statique au runtime** — l'audit P4-5A la jugeait
« satisfaite par construction », ce qui est un constat de lecture, pas une preuve d'exécution.

---

## Impact On Implementation

### Découpage P4-5 confirmé et affiné

| Sous-lot | Contenu | ADR appliquées | Dépend de |
|---|---|---|---|
| **P4-5A** ✅ | audit — **terminé**, aucune modification de code | — | P4-4 partiel |
| **P4-5B** ✅ | **ce lot** — six ADR acceptées, roadmap mise à jour | — | P4-5A |
| **P4-5C** | neutralisation du modèle : `HasPrecision(12,2)` + point de sélection monétaire, filtre d'index par provider, retrait des 18 littéraux optiques | 003, 006 | P4-5B |
| **P4-5D** | stratégie temporelle : `IClock`, UTC partout, convertisseur validant, test d'architecture, `DateOnly` + **migration de données écrite à la main** | 004 | P4-5B |
| **P4-5E** | chaîne de migrations PostgreSQL + factory design-time sélective + **double contrôle de dérive en CI** | 005, 007 | P4-5C, P4-5D |
| **P4-5F** | projet de tests d'intégration + job CI avec serveur + **N1 … N8** | 007, 008 | P4-5E |
| **P4-5G** | **levée du garde-fou de démarrage** ([App.axaml.cs:198](../../src/MMV.App/App.axaml.cs#L198)) — **uniquement après P4-5F vert** | toutes | P4-5F |
| *(retour P4-4)* | **O12** — re-preuve runtime des 14 primitives, **débloquée par P4-5E/P4-5F** | 008 | P4-5F |

### Ce que ces décisions changent par rapport à l'audit P4-5A

| Point | Audit P4-5A | Décision P4-5B |
|---|---|---|
| Mapping monétaire | recommandait un **mapping unique** `HasPrecision(12,2)` des deux côtés | **type physique SQLite conservé** — la recommandation initiale aurait cassé la primitive CAS de `SaleRepository:118` |
| « 15ᵉ migration SQLite » | présentée comme **inévitable** pour O2/O3/O4 | **le volet monétaire et le volet index n'en produisent aucune** ; seul le passage à `DateOnly` exige une migration, **de données**, écrite à la main |
| Convertisseur `DateTime` | proposé, avec réserve (il **masque**) | **convertisseur validant** — il **lève** au lieu de corriger ; la réserve est levée |
| Dates civiles | « à arbitrer » | **tranché : `DateOnly`** |
| Testcontainers | non évalué | **évalué et rejeté comme obligatoire**, non éliminé |

### Prérequis avant d'ouvrir P4-5C

1. **Validation des six ADR par le Lead Software Architect** — elles sont `ACCEPTED` en tant que décisions
   proposées et formalisées par ce lot ; l'arbitrage final lui appartient.
2. **Restauration NuGet opérationnelle** : sans elle, aucun lot d'implémentation ne peut ni construire ni
   tester (P4-5A §0.2). **C'est un prérequis matériel, pas une formalité.**
3. **Confirmation de l'ordre P4-5 avant clôture de P4-4** (voir §Risks, R-5B-6).

---

## Risks

| # | Risque | Probabilité | Impact | Atténuation décidée |
|---|---|---|---|---|
| **R-5B-1** | **Migration de données `DateOnly` oubliée** : EF ne détecte aucun changement de schéma, le format des valeurs change quand même ⇒ dates de naissance et d'ordonnance illisibles sur les bases existantes | moyenne | **critique** — donnée de santé | ADR-004 §7.2 et obligation **T5** : migration écrite à la main, **prouvée par test de non-destruction** |
| **R-5B-2** | **Les deux chaînes de migrations divergent en silence** | moyenne | élevé | ADR-007 : **double contrôle bloquant** + règle de double migration par commit |
| **R-5B-3** | **Faux vert d'intégration** : tests ignorés faute de serveur, lus comme un succès | moyenne | **élevé** — fausse confiance | ADR-008 §5.4 : **un test ignoré en CI est un échec** ; la CI **fournit** le serveur |
| **R-5B-4** | **La dette monétaire SQLite est prise pour un défaut résiduel anodin** et finit par être « corrigée » sans mesure, réintroduisant la régression de `SaleRepository:118` | moyenne | élevé | ADR-003 §9 : condition de réexamen explicite ; toute opération monétaire **traduite en SQL** rouvre la décision |
| **R-5B-5** | **Le convertisseur validant révèle des sites d'écriture non recensés** et fait échouer des tests jusqu'ici verts | **élevée** | moyen — coût, pas danger | effet **recherché** ; à absorber **en P4-5D**, jamais en production |
| **R-5B-6** | **Inversion de dépendance P4-4 ↔ P4-5** : P4-4 est `IN PROGRESS` mais sa validation runtime est `BLOCKED_BY_P4_5_SCHEMA`. **C'est P4-5 qui débloque P4-4**, pas l'inverse | **certaine** | organisationnel | **constatée, non tranchée** — arbitrage du Lead Architect requis |
| **R-5B-7** | **Modification de `ci.yml`** (deux fois : ADR-007 puis ADR-008) touchant par inadvertance le reste du workflow | faible | élevé | obligation **S2** : ne rien modifier d'autre ; revue de diff exigée |
| **R-5B-8** | **Décisions validées mais non appliquées** : le modèle continue d'évoluer pendant que P4-5C/D attendent | moyenne | moyen | P4-5C et P4-5D sont **courts et indépendants** ; ne pas les laisser vieillir |
| **R-5B-9** | **Restauration NuGet toujours indisponible** ⇒ P4-5C ne peut ni construire, ni tester, ni prouver la conservation des 1569 tests | **élevée** | **bloquant** | prérequis explicite ci-dessus ; **ne pas ouvrir P4-5C sans l'avoir levé** |
| **R-5B-10** | **Version PostgreSQL de CI différente de la version d'exploitation** ⇒ preuves obtenues sur un moteur qui n'est pas celui du magasin | moyenne | moyen | ADR-008 obligation **Q8** : alignement décidé en P4-8 |

---

## Next Recommended Step

**Faire valider les six ADR par le Lead Software Architect**, puis ouvrir **P4-5C — neutralisation du
modèle** (ADR-PROD-DB-003 + ADR-PROD-DB-006) : c'est le plus petit lot d'implémentation, il est **entièrement
couvert par des décisions écrites**, il ne touche **ni la CI, ni les migrations, ni l'architecture des
projets**, et sa sortie est vérifiable sans serveur — **chaîne SQLite inchangée, contrôle de dérive vert,
1569 tests conservés**.

**P4-5C ne doit pas être ouverte tant que la restauration NuGet n'est pas rétablie sur le poste
d'implémentation** (R-5B-9) : sans build ni test, sa sortie serait invérifiable.

**P4-5C n'est pas ouverte par ce lot.**

```
P4-5B DECISIONS               = COMPLETE
P4-5B ADRs CREATED            = 6 (ADR-PROD-DB-003 … 008) — ALL ACCEPTED
P4-5B CODE CHANGED            = NONE
P4-5B TESTS EXECUTED          = NONE (lot documentaire)
P4-5B MIGRATIONS CREATED      = NONE
P4-5B CI CHANGED              = NONE
AD-1 MONEY                    = DECIDED — numeric(12,2), SQLite physique inchangé
AD-2 DATETIME                 = DECIDED — UTC + IClock + convertisseur validant + DateOnly
AD-3 MIGRATIONS               = DECIDED — deux assemblys, un seul DbContext
AD-4 INDEX                    = DECIDED — filtre sélectionné par provider
AD-5 DRIFT                    = DECIDED — double contrôle bloquant + vérification physique
AD-6 INTEGRATION TESTS        = DECIDED — projet dans MMV.sln, serveur fourni par la CI
OBLIGATIONS ADR-002 ADDRESSED = O2, O3, O4, O7 (décidées, non implémentées) · O12 (préparée)
POSTGRESQL CLEAN START        = BLOCKED (B1 garde-fou · B2 migrations · B3 modèle)
P4-5C IMPLEMENTATION          = NOT STARTED
V1 MULTI-POSTE                = NOT GO
```
