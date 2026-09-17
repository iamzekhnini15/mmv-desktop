# P4-5A PostgreSQL Production Schema Audit

**Lot :** P4-5A — audit du schéma de production PostgreSQL
**Nature :** passe d'**AUDIT STRICT**. **Aucun fichier de code, de test, de migration, de projet, de
configuration ou de CI n'a été modifié.** Aucune migration EF n'a été créée. Aucun commit de code.
**Statut :** `P4_5A_SCHEMA_AUDIT = COMPLETE — DECISIONS REQUIRED`

---

## 0. Base Git et convention de preuve

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| SHA audité | `b49f2ff7c4e3af665a767f508268377be9d19943` |
| Dernier commit | `docs(P4-4C): reconcile P4-4 implementation state` |
| Arbre de travail à l'ouverture | **propre** (`git status --short` vide) |
| Seul fichier produit par ce lot | ce rapport |

### 0.1 Niveaux de preuve employés

Repris de la convention P4-4B0, sans mélange et sans surclassement.

| Étiquette | Signification exacte |
|---|---|
| `STATIC_CODE_PROOF` | Fait établi par **lecture du dépôt au SHA ci-dessus**. Vrai par construction, non re-prouvé au runtime. |
| `OFFICIAL_SEMANTICS` | Sémantique de type ou de provider **documentée** (PostgreSQL / Npgsql), citée par l'ADR-PROD-DB-002 ou par la documentation officielle du moteur. |
| `EXECUTED_SPIKE` | Mesuré par le harness `spikes/P4.ProviderComparison` contre un vrai serveur. Prouve le **serveur**, pas MMV. Preuve **locale** — la CI ne la rejoue pas. |
| `NEEDS_RUNTIME_PROOF` | Conséquence **attendue** d'un fait statique + d'une sémantique documentée, **non exécutée** contre PostgreSQL à ce jour. |
| `NOT_PROVED` | Non mesuré et non déduit. |

### 0.2 Limite d'exécution de cette passe — déclarée sans détour

**Aucun test n'a été exécuté par ce lot.** Une tentative de `dotnet test MMV.sln` a échoué au **restore** :
la seule source NuGet enregistrée sur ce poste est `Microsoft Visual Studio Offline Packages`, et
`nuget.org` n'est pas atteignable (`NU1101` / `NU1102` sur `xunit`, `Microsoft.NET.Test.Sdk`,
`Npgsql.EntityFrameworkCore.PostgreSQL`, `Tmds.DBus.Protocol`, …). **Ce rapport est donc un audit
strictement statique**, ce qui correspond exactement au mandat P4-5A, mais il ne produit **aucune**
baseline de tests propre.

La baseline **enregistrée** dans le dépôt reste **1569 tests** (P4-4C / roadmap §6 : 1529 après P4-3,
**+36** en P4-4A1, **+4** en P4-4B1). Elle est **citée**, pas revendiquée par ce lot.

---

## Objective

Répondre à une seule question :

> **Quelles modifications sont nécessaires pour rendre MMV réellement compatible PostgreSQL production
> multi-poste ?**

Ce lot **analyse, audite, documente et propose**. Il **ne décide rien** et **n'implémente rien** :
l'arbitrage appartient au Lead Software Architect, l'exécution aux lots P4-5B et suivants.

---

## Current Database Architecture

### 1. Ce qui existe réellement aujourd'hui (`STATIC_CODE_PROOF`)

La sélection du provider est **centralisée** depuis P4-3 et fonctionne.

| Site | Fichier | État |
|---|---|---|
| Composition root | [App.axaml.cs:119-129](../../src/MMV.App/App.axaml.cs#L119-L129) | **multi-provider** via `DatabaseProviderResolver` |
| `OnConfiguring` | [OpticDbContext.cs:138-158](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L138-L158) | **multi-provider** via `DatabaseProviderResolver` |
| Factory design-time | [OpticDbContextFactory.cs:14-29](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L14-L29) | **figée sur SQLite, volontairement** |
| Reprise de l'ancien fichier | [App.axaml.cs:233](../../src/MMV.App/App.axaml.cs#L233) via `LegacyDatabaseRecoveryService` | **SQLite-only, volontairement** (ouvre un *fichier* `mmv-optic.db`) |
| `DependencyInjection.AddInfrastructure` | [DependencyInjection.cs](../../src/MMV.Infrastructure/DependencyInjection.cs) | **inerte** — aucun appelant |

- `MMV_DATABASE_PROVIDER` (`sqlite` défaut · `postgresql` / alias `postgres`, `npgsql`) et
  `MMV_DATABASE_CONNECTION_STRING` (secret, hors dépôt, **obligatoire** pour PostgreSQL).
- Une valeur inconnue **lève** (`DatabaseConfigurationException`) : **aucun repli silencieux** sur SQLite.
- `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11` est référencé **uniquement** dans
  [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) — Domain et
  Application restent neutres (obligation **O14**, vérifiée par
  `ProviderNeutralityArchitectureTests`).
- Le **dialecte provider** existe déjà pour une primitive :
  [NotificationRepository.cs:179-201](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L179-L201)
  sélectionne le SQL sur `Database.ProviderName` (SQLite/PostgreSQL → `ON CONFLICT DO NOTHING` ;
  SQL Server → `INSERT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK))` ; provider inconnu → **lève**).
- La **classification d'erreurs PostgreSQL** existe (P4-4A1) :
  [PersistenceErrorMapper.cs](../../src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs)
  classe par `SqlState` structuré, parcourt toute la chaîne `InnerException`, **ne lit jamais le texte**,
  et n'expose aucun type Npgsql au Domain.

### 2. Différences développement ↔ production visées

| Axe | Développement / test / démo (aujourd'hui) | Production multi-poste (cible) |
|---|---|---|
| Provider | SQLite (défaut) | PostgreSQL (ADR-PROD-DB-002) |
| Schéma | 14 migrations SQLite, ou `EnsureCreated()` en test | **inexistant** |
| Cycle de vie | `SqliteDatabaseManager.PrepareDatabase` : sauvegarde fichier + détection + `Migrate()` **par poste** | **inexistant** ; `Migrate()` par poste est **inadapté** à une base centrale (P4-6) |
| Sauvegarde | `File.Copy` du fichier `.db` + sidecars `-wal` / `-shm` | `pg_dump` / `pg_restore` planifiés (P4-9) |
| Concurrence | mono-écrivain SQLite, même processus | N postes, N connexions, READ COMMITTED |

### 3. PostgreSQL peut-il actuellement démarrer une base propre ?

> **NON.** `POSTGRESQL_CLEAN_START = BLOCKED`

Trois blocages **cumulatifs et indépendants** — lever le premier ne suffirait pas.

| # | Blocage | Nature | Preuve |
|---|---|---|---|
| **B1** | Garde-fou de démarrage explicite : sélectionner PostgreSQL **lève avant toute opération base**, hors du `try`, pour qu'aucun `catch` générique ne la masque | **volontaire** (P4-3) | [App.axaml.cs:198-206](../../src/MMV.App/App.axaml.cs#L198-L206) — `STATIC_CODE_PROOF` |
| **B2** | **Aucune chaîne de migrations PostgreSQL.** Les 14 migrations existantes sont SQLite (`Sqlite:Autoincrement`, `GLOB`, `lower(trim())`, tables `CHECK`-abort, rebuilds `AlterColumn`) | **dette** | §Migration Analysis — `STATIC_CODE_PROOF` |
| **B3** | **Le modèle EF lui-même n'est pas portable** : un index filtré au littéral booléen SQLite et des `HasColumnType` littéraux transmis verbatim au provider actif | **dette** | §PostgreSQL Index Analysis, §Money Type Analysis — `STATIC_CODE_PROOF` + `OFFICIAL_SEMANTICS` |

**Ce qui existe déjà et n'est pas à refaire** : sélection du provider, paquet Npgsql en Infrastructure
seule, neutralité Domain/Application, classification d'erreurs PostgreSQL (**O5 fait**), dialecte
d'upsert provider, sûreté d'annulation de `UnitOfWork` (P4-4B1).

---

## EF Core Persistence Analysis

### 4. Distinction structurante — deux familles de « types de colonne »

C'est **la** clé de lecture de tout ce qui suit, et elle change l'ampleur du travail P4-5.

| Famille | Où | Comportement face à un changement de provider |
|---|---|---|
| **(A) `HasColumnType` écrit à la main** dans `Data/Configurations/*.cs` | **30 appels** : 29 × `"REAL"` + 1 × `"TEXT"` | La chaîne est passée **verbatim** au provider actif. **Elle suit le modèle sur PostgreSQL.** ⇒ **problème de code** |
| **(B) Types inscrits dans `OpticDbContextModelSnapshot.cs`** (115 × `TEXT`, 61 × `INTEGER`, 33 × `REAL`) | snapshot + `*.Designer.cs` | **Inférés par le provider SQLite au design-time.** Reconstruits par Npgsql si le modèle est généré contre PostgreSQL. ⇒ **problème de baseline de migrations, pas de code** |

Conséquence directe : **on ne « corrige » pas le snapshot** ; on produit une **baseline distincte**
(§Migration Analysis). En revanche, les **30 `HasColumnType` littéraux de la famille (A) doivent être
traités dans le code**, car ils survivent au changement de provider.

Détail de la famille (A) :

- `"REAL"` × **29** — [OrderItemConfiguration](../../src/MMV.Infrastructure/Data/Configurations/OrderItemConfiguration.cs) 5 ·
  [PrescriptionConfiguration](../../src/MMV.Infrastructure/Data/Configurations/PrescriptionConfiguration.cs) 8 ·
  [ProductConfiguration](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs) 3 ·
  [SaleConfiguration](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs) 5 ·
  [SaleItemConfiguration](../../src/MMV.Infrastructure/Data/Configurations/SaleItemConfiguration.cs) 2 ·
  [WorkshopSheetItemConfiguration](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetItemConfiguration.cs) 6.
- `"TEXT"` × **1** — `Product.TechnicalSpecs` (JSON stocké en texte).
  **`text` est un type PostgreSQL valide** : cet appel est le seul de la famille (A) qui ne casse rien.

### 5. Primitives atomiques — état

Les garanties métier sont exprimées comme **décisions prises par la base en une instruction**
(`ExecuteUpdateAsync` / `ExecuteDeleteAsync` conditionnels, upsert, index uniques), jamais comme
`check-then-act` applicatif. C'est l'atout structurel majeur hérité de P3 et il **survit** au changement
de provider : 22 sites `ExecuteUpdateAsync` / `ExecuteDeleteAsync` recensés en Infrastructure, tous sous
forme CAS. **14 / 14 primitives** sont prouvées **en spike** sur PostgreSQL (P4-1 Lots A→D) ;
la **re-preuve runtime (O12) reste due** et reste bloquée par l'absence de schéma.

### 6. `catch` intra-transactionnels (obligation **O6**) — bien meilleur que redouté

Sur PostgreSQL, toute erreur **avorte la transaction entière** (`25P02`) : un `catch` qui avale une
erreur puis **continue à écrire dans la même transaction** échouerait en cascade. Les 12 sites `catch`
concernés ont été relus un à un.

| Site | Position | Verdict |
|---|---|---|
| `CreateProductUseCase` (2), `UpdateProductUseCase` (2), `DeleteSupplierUseCase` (1), `CreateUserUseCase` (1), `UpdateUserUseCase` (1) | **à l'extérieur** du lambda `_transactionRunner.RunAsync(...)` | **conforme** — la transaction est déjà annulée quand le `catch` s'exécute |
| [OrderRepository.cs:250-257](../../src/MMV.Infrastructure/Repositories/OrderRepository.cs#L250-L257) | **dans** la transaction | **conforme** — traduit `DbUpdateException` en exception métier et **relance** ; n'écrit plus rien, aucune re-tentative |
| `EfTransactionRunner` (2), `UnitOfWork` (1) | frontière transactionnelle | **conforme** — annulent, ne poursuivent pas |

> **O6 apparaît déjà satisfaite par construction** — `STATIC_CODE_PROOF`. Une **re-vérification au
> runtime PostgreSQL** reste recommandée (`NEEDS_RUNTIME_PROOF`), notamment pour `CreateProductUseCase`,
> dont le `catch` **rouvre une requête** (`ExistsFreshAsync`) après annulation.

### 7. Isolation et re-tentative

[EfTransactionRunner.cs:63-64](../../src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs#L63-L64)
appelle `BeginTransactionAsync()` **sans niveau d'isolation explicite** ⇒ **READ COMMITTED** sur
PostgreSQL, contre un comportement mono-écrivain quasi sérialisable sur SQLite (`OFFICIAL_SEMANTICS`).
Les primitives étant **mono-instruction**, READ COMMITTED reste suffisant pour elles ; le point à
vérifier est l'absence de séquence *lire puis écrire sur la lecture* à l'intérieur d'une même
transaction. Aucune re-tentative n'existe, et **aucune ne doit être ajoutée sans clé d'idempotence** :
stock et numérotation sont **mesurés non idempotents** (ADR **O13**).

---

## Entity Mapping Analysis

21 entités, 21 tables. Nom de table = nom du `DbSet` (deux `ToTable` explicites : `Notifications`,
`DocumentSequences`). La colonne `Risque PostgreSQL` ne retient que les risques **propres au portage**,
pas les défauts génériques.

| Entity | Table | Risque PostgreSQL | Action nécessaire |
|---|---|---|---|
| `User` | `Users` | index unique `NormalizedUsername` créé par du SQL de migration SQLite (`GLOB`, `lower(trim())`) | baseline serveur (P4-5) ; normalisation reste applicative |
| `Supplier` | `Suppliers` | aucun spécifique | reprise en baseline |
| `ProductCategory` | `ProductCategories` | aucun spécifique | reprise en baseline |
| `Product` | `Products` | **3 colonnes monétaires en `REAL`** ; `TechnicalSpecs` `HasColumnType("TEXT")` ; index unique `NormalizedReference` créé par SQL SQLite | **O2** ; arbitrer `text` vs `jsonb` ; baseline |
| `GlassDetail` | `GlassDetails` | 3 `decimal?` **sans** `HasColumnType` → `TEXT` sur SQLite, `numeric` sur PostgreSQL | aucun code ; **changement de type physique** à acter en baseline |
| `LensDetail` | `LensDetails` | 2 `decimal?` idem | idem |
| `AccessoryDetail` | `AccessoryDetails` | aucun spécifique | reprise en baseline |
| `Supplement` | `Supplements` | `SupplementPrice` **monétaire** sans `HasColumnType` → `TEXT` / `numeric` | **O2** — cohérence monétaire |
| `GlassSupplement` | `GlassSupplements` | clé composite, 2 FK `Cascade` | reprise en baseline |
| `GlassPricingTier` | `GlassPricingTiers` | 2 colonnes **monétaires** + 2 de puissance, toutes sans `HasColumnType` | **O2** |
| `Customer` | `Customers` | recherche par `lower()` (§Index) ; `BirthDate` = **date civile** mappée `DateTime` | **O4** + arbitrage type `date` |
| `Prescription` | `Prescriptions` | **8 colonnes optiques `REAL`** ; `IssueDate` = date civile | **hors O2** (voir §Money) ; **O4** |
| `Order` | `Orders` | 3 colonnes `DateTime` ; FK `Cascade` depuis `Sale` | **O4** |
| `OrderItem` | `OrderItems` | **1 monétaire `REAL`** + 4 optiques `REAL` | **O2** (monétaire seul) |
| `WorkshopSheet` | `WorkshopSheets` | **index unique filtré `"IsCurrent" = 1` — invalide sur PostgreSQL** | **O3 — bloquant** |
| `WorkshopSheetItem` | `WorkshopSheetItems` | 6 optiques `REAL` | hors O2 |
| `Sale` | `Sales` | **5 colonnes monétaires `REAL`** ; `DiscountAmount` `ValueGeneratedOnAdd` + défaut `0` | **O2** |
| `SaleItem` | `SaleItems` | **2 monétaires `REAL`** + 4 optiques `REAL` **implicites** | **O2** |
| `StockMovement` | `StockMovements` | aucun spécifique | reprise en baseline |
| `Notification` | `Notifications` | index unique filtré (**valide** en PostgreSQL, voir §Index) ; `CreatedAt = DateTime.Now` par défaut d'entité | **O4 — le plus exposé** |
| `DocumentSequence` | `DocumentSequences` | **`HasData`** (2 lignes `SALE` / `ORDER`) présent des deux côtés d'un futur import | **O11** — ne jamais dupliquer au transfert |

**Clés et intégrité, transverses** : toutes les clés primaires sont `long` auto-générées
(`Sqlite:Autoincrement` sur SQLite ⇒ `bigint GENERATED BY DEFAULT AS IDENTITY` sur PostgreSQL,
`OFFICIAL_SEMANTICS`). Deux clés non entières : `DocumentSequence.SequenceName` (texte) et les clés
partagées `ProductId` des tables de détail. Comportements de suppression : **Cascade** strictement
limité aux agrégats (Order→OrderItems, Sale→SaleItems, WorkshopSheet→Items, Product→détails),
**Restrict** sur tout ce qui est historique, **SetNull** sur les liens optionnels. **Cette sémantique
est portable telle quelle.**

> **Conséquence d'import (O11)** : l'insertion explicite d'identifiants existants **ne resynchronise pas**
> les séquences d'identité PostgreSQL. Sans `setval` explicite après import, la première écriture
> applicative entrerait en collision de clé primaire. `NEEDS_RUNTIME_PROOF`, à traiter en **P4-7**.

---

## Money Type Analysis

### 8. Inventaire exhaustif des colonnes monétaires

Le value object [`Money`](../../src/MMV.Domain/ValueObjects/Money.cs) (`decimal`, arrondi
`decimal.Round(…, 2, MidpointRounding.ToEven)`) **n'est pas persisté** : il ne vit que dans les commandes
de la couche Application. **Les entités persistées utilisent `decimal` nu.** L'intention métier est donc
bien **2 décimales exactes** — mais elle n'est appliquée nulle part dans le mapping.

**14 colonnes monétaires**, réparties en **deux mappings incohérents** :

| # | Table | Colonne | Type CLR | `HasColumnType` | Physique SQLite | Physique PostgreSQL attendu |
|---|---|---|---|---|---|---|
| 1 | `Products` | `PurchasePrice` | `decimal` | **`"REAL"`** | `REAL` (float8) | **`real` (float4)** |
| 2 | `Products` | `SalePrice` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 3 | `Products` | `RecommendedPrice` | `decimal?` | **`"REAL"`** | `REAL` | **`real`** |
| 4 | `Sales` | `TotalAmount` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 5 | `Sales` | `DiscountAmount` | `decimal` | **`"REAL"`** + défaut `0` | `REAL` | **`real`** |
| 6 | `Sales` | `FinalAmount` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 7 | `Sales` | `DepositAmount` | `decimal?` | **`"REAL"`** | `REAL` | **`real`** |
| 8 | `Sales` | `RemainingAmount` | `decimal?` | **`"REAL"`** | `REAL` | **`real`** |
| 9 | `SaleItems` | `UnitPrice` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 10 | `SaleItems` | `TotalPrice` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 11 | `OrderItems` | `UnitPrice` | `decimal` | **`"REAL"`** | `REAL` | **`real`** |
| 12 | `Supplements` | `SupplementPrice` | `decimal` | *(aucun)* | `TEXT` | `numeric` |
| 13 | `GlassPricingTiers` | `PurchasePriceGrid` | `decimal` | *(aucun)* | `TEXT` | `numeric` |
| 14 | `GlassPricingTiers` | `SalePriceGrid` | `decimal` | *(aucun)* | `TEXT` | `numeric` |

### 9. Problèmes identifiés

**P1 — `REAL` sur PostgreSQL est strictement pire que `REAL` sur SQLite.** `OFFICIAL_SEMANTICS`.
`REAL` en SQLite est un flottant **IEEE 754 sur 8 octets** (double, ~15 chiffres significatifs).
`real` en PostgreSQL est un flottant **sur 4 octets** (`float4`, **~6 chiffres décimaux
significatifs**). Le même littéral `HasColumnType("REAL")`, transmis verbatim au provider actif, ne
désigne donc **pas le même type** : porter tel quel **diviserait par deux la précision déjà
insuffisante**. Un montant à cinq chiffres significatifs avant la virgule (`12345.67`) n'est **pas**
représentable fidèlement en `float4`. **C'est une régression, pas un statu quo.**
(`NEEDS_RUNTIME_PROOF` pour le comportement exact de conversion `decimal` ↔ `float4` sous Npgsql ; la
sémantique de type, elle, est documentée.)

**P2 — Deux mappings monétaires coexistent déjà.** 11 colonnes en `REAL`, 3 en `TEXT`. Deux prix
comparables (`Products.SalePrice` et `GlassPricingTiers.SalePriceGrid`) n'ont ni le même type
physique, ni la même fidélité, ni le même comportement de tri. **Ce n'est pas un risque de portage :
c'est un défaut présent aujourd'hui en SQLite**, que le portage rend visible.

**P3 — Aucune précision déclarée.** Aucune colonne monétaire ne porte `HasPrecision`. Sur PostgreSQL,
un `numeric` **sans** précision est de précision arbitraire — correct mais non contraint : rien
n'empêcherait d'écrire trois décimales là où le métier en veut deux.

**P4 — Le portage n'assainit pas le passé.** `EXECUTED_SPIKE` (P4-1, ADR §12.2) : la perte de valeur en
`REAL` a été mesurée **sur les deux candidats**. Les montants déjà écrits en flottant **sont déjà
altérés à la source**. **O2 est un prérequis strict de O11** (import) : migrer d'abord, importer
ensuite — l'inverse figerait l'erreur.

**P5 — Ne pas confondre monétaire et optique.** **22 colonnes `REAL` sont optiques**
(`Sphere`, `Cylinder`, `Addition`, `PrismValue`, `Transposed*`, `Od*`/`Og*`), typées `double` en CLR.
Elles sont **hors du périmètre de O2** (ADR §15, O2, explicitement). `double` → `double precision`
(`float8`) est le mapping Npgsql par défaut et **ne perd rien** : il suffit de **retirer le littéral
`"REAL"`** pour ces colonnes. La dioptrie se mesure par pas de 0,25 ; le flottant y est adéquat. Deux
colonnes de puissance sont en revanche typées `decimal` (`GlassPricingTiers.PowerMin`/`PowerMax`,
`HasPrecision(5,2)`) : **incohérence de typage CLR à signaler**, sans conséquence de portage.

### 10. Recommandation (proposition, non décision)

| Objet | Recommandation | Justification |
|---|---|---|
| 14 colonnes **monétaires** | `decimal` + `HasPrecision(12, 2)` ⇒ `numeric(12,2)` sur PostgreSQL, **sans** `HasColumnType` littéral | Exact en base 10, conforme à l'intention déjà exprimée par `Money` (2 décimales, `ToEven`) ; 12 chiffres couvrent 9 999 999 999,99 € — sans commune mesure avec le besoin d'un magasin d'optique. **Le mapping devient provider-neutre** : SQLite reçoit son type par défaut, PostgreSQL `numeric(12,2)`. |
| 22 colonnes **optiques `double`** | **retirer** `HasColumnType("REAL")`, laisser le mapping par défaut | `double precision` sur PostgreSQL, `REAL` sur SQLite : aucune perte, aucun littéral provider |
| 2 colonnes de puissance `decimal` | conserver `HasPrecision(5,2)`, retirer tout littéral | déjà correct |
| `Product.TechnicalSpecs` | **arbitrer** `text` (statu quo, sûr) vs `jsonb` | `jsonb` n'apporte qu'avec un besoin de requête JSON — **non constaté** dans le dépôt |

> **Décision à prendre** — voir `AD-1`. La précision `(12,2)` est une **proposition motivée**, pas une
> décision. Tout autre couple explicite et justifié est recevable ; ce qui ne l'est pas, c'est un
> flottant binaire sur un montant, ni le maintien de deux mappings concurrents.

---

## DateTime Analysis

### 11. Inventaire

**21 colonnes `DateTime` / `DateTime?`** (snapshot, famille (B) — toutes en `TEXT` sous SQLite,
**aucun `HasColumnType` écrit à la main** : le code est donc déjà neutre, seul le comportement du
provider change).

Génération des dates dans `src/**`, hors migrations (`STATIC_CODE_PROOF`) :

| Source | Occurrences | `Kind` produit | Statut face à PostgreSQL |
|---|---|---|---|
| `DateTime.UtcNow` | **61** | `Utc` | **compatible** |
| `DateTime.Now` | **17** | **`Local`** | **refusé par le chemin EF** |
| `DateTime.Today` | 0 | — | — |
| `DateTimeOffset` | 6 (UI uniquement) | — | conversion à auditer |

**Les 17 sites `DateTime.Now`, nommément :**

- Défaut d'entité : [Notification.cs:50](../../src/MMV.Domain/Entities/Notification.cs#L50) —
  `CreatedAt = DateTime.Now`. **Le plus grave : toute notification créée sans horodatage explicite
  porte un `Kind = Local`.** Les 9 autres entités datées utilisent `UtcNow`.
- Application (7 lignes) : `GenerateLowStockNotificationsUseCase` (2, dont l'argument de
  `ResolveActiveLowStockAsync`), `AdvanceOrderStatusUseCase` (1), `SettleOrderBalanceUseCase` (1),
  `RegisterSaleUseCase` (4 lignes : `SaleDate`, `EstimatedDelivery`, `OrderDate`, `CreatedAt`).
- Infrastructure (3) : [OrderRepository.cs:167](../../src/MMV.Infrastructure/Repositories/OrderRepository.cs#L167)
  (**comparaison `EstimatedDelivery < DateTime.Now` traduite en SQL** — comparer un `Local` à une colonne
  `timestamptz` est un piège silencieux), `DbSeeder` (2).
- UI (3) : `OrderDetailViewModel`, `OrderFormViewModel`, `CustomersView.axaml.cs`.

### 12. Risques

**R1 — PostgreSQL refuse purement et simplement l'écriture.** `EXECUTED_SPIKE` + `OFFICIAL_SEMANTICS`
(ADR §16.1, citant Npgsql : *« trying to send a non-UTC DateTime as timestamptz will throw an
exception »*). Npgsql ≥ 6 mappe `DateTime` → `timestamp with time zone` et **exige `Kind = Utc`**.
Par le chemin **SQL brut**, pire : l'heure locale est **stockée comme si elle était UTC** — **2 h
d'écart silencieuses** en heure d'été française. **En l'état, MMV ne peut pas écrire de date vers
PostgreSQL par le chemin EF.**

**R2 — Divergence de `Kind` à la relecture.** SQLite rend `Unspecified` (texte re-parsé) ; PostgreSQL
rend `Utc`. Tout code comparant, formatant ou soustrayant des dates change de comportement **sans que
rien n'échoue**. `NEEDS_RUNTIME_PROOF`.

**R3 — Risque multi-poste spécifique.** C'est le risque **structurellement nouveau** de P4 : avec
`DateTime.Now`, l'horodatage dépend du **fuseau et de l'horloge de chaque poste**. Deux postes
désynchronisés, ou réglés sur des fuseaux différents, produisent un **ordre chronologique faux dans une
base commune**. Les conséquences sont directement métier : ordre des mouvements de stock, chronologie
des alertes `LowStock`, séquence des transitions de commande, traçabilité d'ordonnance — donnée de santé
horodatée. **Le stockage UTC ne corrige pas une horloge fausse**, mais il supprime la **moitié fuseau**
du problème et rend la dérive résiduelle mesurable.

**R4 — Dates civiles mappées en instants.** `Customer.BirthDate` et `Prescription.IssueDate` sont des
**dates civiles**, pas des instants. Les convertir en UTC peut **décaler le jour** (une naissance
enregistrée à 00:00 locale devient la veille à 22:00 UTC). PostgreSQL offre `date` ; SQLite non. À
arbitrer explicitement.

**R5 — Conversions UI.** `new DateTimeOffset(customer.BirthDate.Value)` et
`new DateTimeOffset(prescription.IssueDate)` ([CustomerFormViewModel.cs:250](../../src/MMV.App/ViewModels/CustomerFormViewModel.cs#L250),
[PrescriptionFormViewModel.cs:414](../../src/MMV.App/ViewModels/PrescriptionFormViewModel.cs#L414))
interprètent un `DateTime` non marqué **comme heure locale**. Avec des valeurs `Utc` venues de
PostgreSQL, le décalage se propagerait jusqu'à l'affichage.

**R6 — L'obligation est déjà indépendamment justifiée.** [ADR-005](../architecture/adr-candidates.md#adr-005)
avait **déjà** rejeté `DateTime.Now` et orienté vers **horloge injectable + stockage UTC**. La
remédiation exigée par PostgreSQL est donc un **travail déjà prévu**, pas une dette nouvelle.

### 13. Stratégie recommandée (proposition)

1. **Invariant unique : tout instant persisté est UTC.** Remplacer les 17 `DateTime.Now` par
   `DateTime.UtcNow`, en commençant par le défaut d'entité `Notification.CreatedAt` et par la
   comparaison SQL de `OrderRepository`.
2. **Horloge injectable** (`IClock`) plutôt qu'appels statiques dispersés — testabilité, et seule voie
   pour **verrouiller le comportement par assertion**, ce qui manque aujourd'hui (le constat P4-1 est
   *observationnel*).
3. **Convertisseur de valeur de défense** (`HasConversion` UTC) au niveau du modèle : filet qui empêche
   qu'un futur `Local` oublié atteigne la base. **À arbitrer** : un convertisseur global masque aussi
   les erreurs au lieu de les faire remonter.
4. **Arbitrer séparément** les dates civiles (`BirthDate`, `IssueDate`) : `date` PostgreSQL, ou
   `DateOnly` côté CLR.
5. **Auditer la frontière UI** : formater en local à l'affichage, jamais au stockage.

### 14. Faut-il une ADR ?

> **Oui — recommandée, mais courte.** ADR-005 a déjà tranché *stockage UTC + horloge injectable*, donc
> **le principe n'est pas à rouvrir**. Une ADR est en revanche justifiée pour ce qu'ADR-005 ne couvre
> pas : (a) le traitement des **dates civiles** (`date`/`DateOnly` vs instant UTC) ; (b) le choix
> **convertisseur de valeur global** vs **discipline applicative vérifiée par test** ; (c) la
> **stratégie de reprise des données existantes**, dont le `Kind` est aujourd'hui inconnu et hétérogène
> — point à traiter **avec** l'import P4-7. Si l'architecte juge (a) et (b) tranchables sans ADR, une
> **note de décision dans le rapport P4-5B** suffit ; (c) doit être écrit quelque part, **sous peine de
> réinterpréter silencieusement des dates de santé**.

---

## PostgreSQL Index Analysis

### 15. Inventaire des index (16 déclarations)

| Index | Table | Unique | Filtre | Validité PostgreSQL |
|---|---|---|---|---|
| `idx_users_normalized_username_unique` | `Users` | ✔ | — | **OK** |
| `idx_product_categories_name_unique` | `ProductCategories` | ✔ | — | **OK** |
| `idx_products_normalized_reference_unique` | `Products` | ✔ | — | **OK** |
| `idx_products_category_id` | `Products` | — | — | **OK** |
| `idx_customers_lastname` / `idx_customers_phone` | `Customers` | — | — | **OK** |
| `idx_orders_order_number_unique` | `Orders` | ✔ | — | **OK** |
| `idx_orders_sale_id` / `idx_orders_status` | `Orders` | — | — | **OK** |
| `idx_sales_sale_number_unique` / `idx_sales_sale_date` | `Sales` | ✔ / — | — | **OK** |
| `idx_stock_movements_product_id` | `StockMovements` | — | — | **OK** |
| `idx_glass_pricing_tier_range` | `GlassPricingTiers` | — | — | **OK** |
| `idx_workshop_sheet_items_sheet_id` | `WorkshopSheetItems` | — | — | **OK** |
| *(3 index simples)* `IsRead`, `Type`, `CreatedAt` | `Notifications` | — | — | **OK** |
| `idx_workshop_sheets_order_version_unique` | `WorkshopSheets` | ✔ | — | **OK** |
| **`idx_workshop_sheets_current_unique`** | `WorkshopSheets` | ✔ | **`"IsCurrent" = 1`** | **❌ INVALIDE** |
| **`idx_notifications_active_low_stock_unique`** | `Notifications` | ✔ | `"Type" = 'LowStock' AND "EntityType" = 'Product' AND "EntityId" IS NOT NULL AND "ResolvedAt" IS NULL` | **✔ VALIDE tel quel** |

### 16. Le seul filtre réellement invalide — et pourquoi

`STATIC_CODE_PROOF` + `OFFICIAL_SEMANTICS`.

[WorkshopSheetConfiguration.cs:62](../../src/MMV.Infrastructure/Data/Configurations/WorkshopSheetConfiguration.cs#L62)
et [20260719003826_AddWorkshopSheets.cs:93](../../src/MMV.Infrastructure/Migrations/20260719003826_AddWorkshopSheets.cs#L93)
déclarent `filter: "\"IsCurrent\" = 1"`.

- SQLite n'a **pas** de type booléen : `IsCurrent` est physiquement un `INTEGER` (0/1), et `= 1`
  est une comparaison valide.
- PostgreSQL crée une colonne **`boolean`** : `"IsCurrent" = 1` compare un `boolean` à un `integer`,
  ce que PostgreSQL **refuse** (aucune conversion implicite booléen↔entier). La création de l'index
  **échoue**, donc la création du schéma échoue.
- Forme PostgreSQL correcte : `WHERE "IsCurrent"`.

C'est l'**obligation O3**. L'adaptation existe **côté spike uniquement** (`AdaptedOpticDbContext`,
ADR §16.2) : **les mappings et migrations de production ne l'ont pas.**

> **Ce que cet index garantit** : au plus **une** version courante par commande — le filet structurel
> anti-ambiguïté de la bascule « ancienne à false / nouvelle à true ». **Sa perte serait une perte de
> garantie métier, pas une perte d'index.** Il doit être recréé **prouvé**, pas seulement recréé.

### 17. Le filtre `Notifications` est portable tel quel

Contrairement à ce qu'une lecture rapide suggère, ce filtre **ne contient aucun littéral SQLite** :
comparaisons de texte et tests `IS NULL` / `IS NOT NULL`, tous valides et **immuables** en PostgreSQL —
condition requise pour un index partiel. Cette précision **réduit le périmètre O3 à un seul index**.
`NEEDS_RUNTIME_PROOF` pour la création effective. Le comportement des `NULL` dans un index unique
(distincts par défaut) est **identique** sur les deux moteurs.

### 18. Autres divergences d'index et de requêtes

| Sujet | SQLite | PostgreSQL | Portée |
|---|---|---|---|
| `lower()` | ne replie que **A–Z** (ASCII) | **sensible à la collation**, replie les accentués et l'Unicode | [CustomerRepository.cs:38-42](../../src/MMV.Infrastructure/Repositories/CustomerRepository.cs#L38-L42) et [ProductRepository.cs:124-127](../../src/MMV.Infrastructure/Repositories/ProductRepository.cs#L124-L127) : la recherche remontera **plus** de résultats sur PostgreSQL. Divergence **réelle mais probablement souhaitable** — à **acter**, pas à subir |
| `LIKE` | insensible à la casse en ASCII | **sensible à la casse** | neutralisé ici : les deux côtés sont déjà passés en minuscules |
| `ToLower()` .NET | culture courante | — | `searchTerm.ToLower()` est **culture-sensible** ; `ToLowerInvariant()` serait plus sûr. Hors périmètre P4-5 |
| Espace de noms des index | par base | par schéma | noms déjà globalement distincts ⇒ **aucun conflit** |
| Identifiants | non sensibles à la casse | **`"PascalCase"` exige les guillemets** | EF les cite toujours ; **impact opérationnel** : toute requête manuelle d'exploitation devra citer |

---

## Migration Analysis

### 19. État

**14 migrations**, toutes SQLite, plus **un** `OpticDbContextModelSnapshot.cs` **unique et partagé**.

Constructions **non portables** (`STATIC_CODE_PROOF`) :

| Construction | Où | Pourquoi bloquant |
|---|---|---|
| `.Annotation("Sqlite:Autoincrement", true)` | `InitialCreate`, 10+ tables | annotation de provider ; ignorée / inopérante hors SQLite |
| `GLOB '*[^ -~]*'` | `AddProductNormalizedReferenceAndProtectHistory:65` | `GLOB` **n'existe pas** en PostgreSQL |
| `GLOB '*[^a-zA-Z0-9._-]*'`, `lower(trim(…))` | `AddNormalizedUsernameAndSecureLocalUsers:58-88` | `GLOB` absent ; `lower()` **ne se comporte pas pareil** |
| Tables `CHECK (col = '')` pour **avorter** | 2 migrations | astuce d'arrêt SQLite ; PostgreSQL dispose de `RAISE EXCEPTION` |
| `filter: "\"IsCurrent\" = 1"` | `AddWorkshopSheets:93` | **invalide** (§16) |
| ~90 `AlterColumn` (rebuilds de table SQLite) | 8 migrations | SQLite reconstruit la table ; PostgreSQL fait un vrai `ALTER` — chemins **incomparables** |
| `HasData` + `InsertData` avec **clés explicites** | `InitialCreate:324`, `AddDocumentSequences:30`, `DocumentSequenceConfiguration:49` | identités explicites ⇒ **désynchronisation des séquences** PostgreSQL |

### 20. Réponses aux questions posées

**Faut-il une nouvelle baseline PostgreSQL ?** — **OUI, sans alternative raisonnable.** Trois raisons
indépendantes, chacune suffisante :

1. **Les migrations ne sont pas rejouables** : elles portent du SQL SQLite exclusif.
2. **L'historique est sans valeur sur une base neuve** : ces 14 migrations décrivent l'évolution d'une
   base *existante* ; une base PostgreSQL neuve n'a aucun passé à rattraper. Une baseline propre est
   **plus courte, plus lisible et plus vérifiable**.
3. **Obligation O7** de l'ADR, explicitement, avec sa contrainte jumelle : **les 14 migrations
   historiques doivent rester utilisables pour SQLite local** (dev/test/démo/mono-poste).

**Faut-il adapter les migrations existantes ?** — **NON, et c'est un interdit.** Les modifier romprait
le hachage d'historique des bases SQLite déjà déployées et déclencherait des re-migrations ou des échecs
sur des données réelles. Le critère de sortie P4 n° 7 et O7 les protègent explicitement.

**Risque pour les installations futures ?** — Le risque n'est pas dans la baseline elle-même, il est
dans les **trois pièges structurels** ci-dessous, qui sont la vraie difficulté du lot :

| Piège | Description | Traitement |
|---|---|---|
| **Snapshot unique** | EF n'entretient **qu'un** `ModelSnapshot` par `DbContext`. Deux chaînes de migrations pour un même contexte ⇒ **deux snapshots**, donc **deux assemblys de migrations** ou **deux `DbContext` de migrations**. **C'est LA décision structurante de P4-5.** | **AD-3** |
| **Factory design-time figée sur SQLite** | [OpticDbContextFactory.cs:24](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L24) ignore délibérément `MMV_DATABASE_PROVIDER`. Générer une migration PostgreSQL **exige** de lever ce gel, **sans** casser la CI. | **AD-3** |
| **CI `has-pending-model-changes`** | [ci.yml](../../.github/workflows/ci.yml) exécute la vérification **sans configuration serveur**, donc **contre SQLite**. Avec deux chaînes, une seule est vérifiée : la **dérive du modèle PostgreSQL deviendrait invisible**. | **AD-5** |

> **Piège supplémentaire, à ne pas manquer** : les corrections O2 (monétaire), O3 (filtre) et O4
> (`DateTime`) **modifient le modèle EF partagé**. Elles produiront donc **une 15ᵉ migration SQLite**
> — inévitable et légitime, mais qui **doit être planifiée** : elle change des types de colonnes sur des
> bases SQLite déjà déployées (rebuild de table). Elle ne peut **pas** être traitée comme un effet de
> bord du portage PostgreSQL.

---

## Test Coverage Analysis

### 21. Tests présents

143 fichiers de test dans `MMV.sln` (Domain · Application · App). Baseline enregistrée : **1569**
(citée, non rejouée — §0.2).

| Catégorie | État |
|---|---|
| **SQLite** | **78 fichiers** câblent `UseSqlite` ; **114 sites** `EnsureCreated()` ; quelques suites acceptance/migration utilisent `Migrate()` réel ([AcceptanceScenarioBase.cs:92](../../tests/MMV.Application.Tests/Acceptance/AcceptanceScenarioBase.cs#L92)) |
| **PostgreSQL** | **5 fichiers** mentionnent PostgreSQL/Npgsql : `DatabaseProviderResolverTests`, `PersistenceErrorMapperPostgreSqlTests`, `ProviderNeutralityArchitectureTests`, `LowStockCreationProviderPortabilityTests`, `UnitOfWorkTests`. **Aucun ne se connecte à un serveur** : ce sont des tests de résolution, de classification, d'architecture et de dialecte |
| **Intégration serveur** | **zéro dans `MMV.sln`**. Le harness `spikes/P4.ProviderComparison` (E1→E21) est **hors solution** : la CI ne l'exécute pas et ne l'exécutera pas sans modification explicite du workflow |
| **Transactions** | `EfTransactionRunnerTests`, `UnitOfWorkTests`, `SettleOrderBalanceAtomicityTests`, `LowStockTransactionalEnrollmentTests` — tous **SQLite** |
| **Concurrence** | `LowStockConcurrencyAndQueryCountTests` et suites P3 — **même processus, multi-connexion SQLite** |
| **Architecture** | 8 suites, dont `ProviderNeutralityArchitectureTests` qui **verrouille O14** |

### 22. Tests manquants — liste de travail pour P4-5 / P4-10

| # | Manque | Phase | Criticité |
|---|---|---|---|
| T1 | **Création d'une base PostgreSQL vide par migrations**, reproductible, **deux fois à l'identique** (critère de sortie n° 4) | P4-5 | **bloquant** |
| T2 | **Vérification physique** des contraintes créées : les deux index filtrés, les uniques, les FK et leurs comportements de suppression — lus dans `information_schema` / `pg_index`, **jamais** déduits du modèle | P4-5 | **bloquant** |
| T3 | **Fidélité monétaire** : écrire/relire des montants aux bornes (2 décimales, arrondi bancaire, valeurs limites) et **prouver l'égalité exacte** | P4-5 | **bloquant** (O2) |
| T4 | **Fidélité `DateTime`** : écriture UTC, relecture, `Kind`, ordre chronologique inter-postes — **avec assertion**, ce qui manque aujourd'hui (constat P4-1 *observationnel*) | P4-5 | **bloquant** (O4) |
| T5 | **CRUD complet** de chaque entité contre PostgreSQL via les repositories de production | P4-5 | **bloquant** |
| T6 | **Re-preuve des 14 primitives** sur PostgreSQL via leurs implémentations de production | P4-4 (O12) | **bloquant** |
| T7 | **Rollback et `25P02`** : erreur en transaction puis commande suivante ; re-vérification des `catch` §6 au runtime | P4-4 / P4-5 | élevée |
| T8 | **Concurrence multi-processus réelle** : deux processus, deux connexions, scénarios P4-10 | P4-10 | **bloquant** |
| T9 | **Perte réseau, reconnexion, timeout, deadlock** | P4-10 | **bloquant** |
| T10 | **Migration pendant qu'un poste travaille** ; client trop ancien bloqué proprement | P4-6 (O8) | **bloquant** |
| T11 | **Import SQLite → PostgreSQL** : lignes, FK, unicité, **montants exacts**, `HasData` non dupliqué, **séquences resynchronisées** | P4-7 (O11) | **bloquant** |
| T12 | **Divergence `lower()`** : verrouiller par test le comportement de recherche attendu sur chaque provider | P4-5 | moyenne |

### 23. Question d'infrastructure de test à trancher

Les tests d'intégration PostgreSQL ont besoin d'un serveur. Aujourd'hui, **la CI n'en a pas** et le
harness du spike vit hors solution, ce qui a permis d'avancer sans toucher au workflow. **Ce compromis
n'est plus tenable à partir de P4-5** : T1–T5 doivent tourner en CI, sinon le schéma serveur n'est
jamais vérifié à distance et toutes ses preuves restent locales — exactement la situation que la
roadmap signale comme à ne pas confondre. → **AD-6**.

---

## Required Architectural Decisions

Six décisions. **Aucune n'est prise ici.** Chacune donne le problème, les options réelles, une
recommandation motivée et l'impact.

### AD-1 — Stratégie monétaire *(obligation O2)*

- **Problème** : 14 colonnes monétaires, **deux mappings incohérents** (11 `REAL`, 3 `TEXT`).
  `HasColumnType("REAL")` désigne un flottant **8 octets** sur SQLite et **4 octets** sur PostgreSQL :
  porter tel quel **dégrade** la précision. Aucune précision n'est déclarée nulle part.
- **Options** :
  1. **`decimal` + `HasPrecision(12,2)`**, sans littéral provider → `numeric(12,2)` PostgreSQL,
     type par défaut SQLite.
  2. **Entier de centimes** (`long`) + conversion de valeur → exact et rapide, mais **réécriture large**
     du Domain, de l'Application et de l'UI, et **rupture de lisibilité** des données en base.
  3. **`HasColumnType` par provider** (littéral conditionnel) → réintroduit exactement le couplage que
     P4-3 a supprimé. **Non recommandée.**
- **Recommandation** : **option 1**. Elle est exacte en base 10, alignée sur l'intention déjà exprimée
  par `Money` (2 décimales, `MidpointRounding.ToEven`), **provider-neutre**, et corrige du même geste
  l'incohérence `REAL`/`TEXT`. La précision exacte reste à valider par l'architecte.
- **Impact** : 14 colonnes ; **migration SQLite supplémentaire** (rebuild de table) ; **prérequis
  strict de l'import O11** ; ne touche **pas** les 22 colonnes optiques ; tests T3.

### AD-2 — Stratégie `DateTime` *(obligation O4)*

- **Problème** : 17 `DateTime.Now` produisent `Kind = Local` ; PostgreSQL **refuse** l'écriture par EF
  et **décale silencieusement de 2 h** par SQL brut. Le multi-poste ajoute le risque **fuseau + horloge
  par poste** sur une chronologie commune.
- **Options** :
  1. **UTC partout + `IClock` injectable** (direction déjà prise par ADR-005), *plus* un convertisseur
     de valeur de défense.
  2. **UTC partout sans convertisseur**, verrouillé par tests d'architecture.
  3. **`timestamp without time zone`** : PostgreSQL accepterait `Local`/`Unspecified` — **traite le
     symptôme, conserve la faille multi-poste. Non recommandée.**
- **Recommandation** : **option 1**, avec une réserve explicite : un convertisseur global **masque**
  aussi les oublis au lieu de les révéler. Une variante défendable est l'option 2 **plus** un test
  d'architecture interdisant `DateTime.Now` dans `src/**`, qui échoue **au test** plutôt qu'au runtime
  en production. À trancher.
- **Sous-décisions à ne pas oublier** : dates **civiles** (`BirthDate`, `IssueDate`) → `date`/`DateOnly`
  ou instant UTC ? ; reprise des données existantes, dont le `Kind` est **inconnu et hétérogène**
  (à traiter avec P4-7).
- **Impact** : 21 colonnes ; 17 sites de génération ; 3 conversions UI ; **1 comparaison SQL**
  (`OrderRepository:167`) ; **ADR courte recommandée** (§14) ; tests T4.

### AD-3 — Stratégie de migrations PostgreSQL *(obligation O7)* — **la plus structurante**

- **Problème** : EF n'entretient **qu'un** `ModelSnapshot` par `DbContext`. Il faut **deux** chaînes —
  SQLite historique (intouchable) et PostgreSQL neuve — pour **un seul modèle** métier.
- **Options** :
  1. **Deux assemblys de migrations** pour le **même** `OpticDbContext`
     (`MigrationsAssembly` sélectionné par provider). Le modèle métier reste unique.
  2. **Deux `DbContext` de migrations** dérivés d'une configuration commune.
  3. **Baseline PostgreSQL par script SQL** géré à la main, hors migrations EF. **Non recommandée** :
     supprime `has-pending-model-changes`, donc la seule garde automatique contre la dérive.
- **Recommandation** : **option 1** — un seul modèle, un seul jeu de configurations, deux jeux de
  migrations. C'est la forme supportée par EF Core qui **préserve** la détection de dérive sur les deux
  chaînes. Le prix est la **factory design-time**, qui doit devenir sélective par provider sans casser
  la CI (→ AD-5).
- **Impact** : arborescence `MMV.Infrastructure` ; `OpticDbContextFactory` ; CI ; documentation
  opérateur. **Les 14 migrations SQLite ne sont pas touchées.**

### AD-4 — Stratégie d'index filtrés *(obligation O3)*

- **Problème** : **un seul** filtre est invalide (`"IsCurrent" = 1`). Le filtre `Notifications` est
  portable tel quel. Le filtre porte une **garantie métier** — une seule version de fiche atelier
  courante par commande.
- **Options** :
  1. **Filtre par provider** dans la configuration EF : `"IsCurrent" = 1` (SQLite) /
     `"IsCurrent"` (PostgreSQL), sélectionné sur `Database.ProviderName` — **exactement le motif déjà
     établi et prouvé** par `NotificationRepository.ActiveLowStockInsertSql`.
  2. **Filtre écrit uniquement dans les migrations**, retiré du modèle → crée une **dérive permanente**
     modèle ↔ base et casse `has-pending-model-changes`. **Non recommandée.**
  3. **Supprimer le filtre**, remplacer par une contrainte applicative → **perte de garantie
     structurelle**. **À rejeter** : contraire au principe P3 « la base décide ».
- **Recommandation** : **option 1** — cohérente avec un motif déjà en production, testé, et qui laisse
  SQLite **inchangé au caractère près**.
- **Impact** : 1 fichier de configuration ; les deux baselines ; **tests T2 obligatoires** — un index
  filtré doit être **vérifié physiquement**, jamais supposé.

### AD-5 — Contrôle de dérive du modèle en CI

- **Problème** : `dotnet ef migrations has-pending-model-changes` tourne **sans configuration serveur**,
  donc contre SQLite. Avec deux chaînes, **la dérive PostgreSQL serait invisible**.
- **Options** :
  1. **Deux exécutions** du contrôle, une par chaîne, avec une factory design-time sélective —
     **aucun serveur requis**, la génération de migrations n'en demande pas.
  2. Une seule exécution SQLite (statu quo) → **dérive PostgreSQL non détectée**. **Non recommandée.**
  3. Contrôle PostgreSQL déplacé dans un job d'intégration avec serveur → le lie inutilement à la
     disponibilité d'un serveur.
- **Recommandation** : **option 1**. C'est la modification de CI **minimale** qui restaure sur la chaîne
  PostgreSQL la garantie déjà acquise sur SQLite.
- **Impact** : [ci.yml](../../.github/workflows/ci.yml) ; `OpticDbContextFactory`. **Hors périmètre
  P4-5A** — aucune modification n'a été faite.

### AD-6 — Stratégie de tests d'intégration PostgreSQL

- **Problème** : zéro test serveur dans `MMV.sln` ; le harness du spike est hors solution et **non
  rejoué par la CI**. T1–T5 sont des **critères de sortie**, pas des tests de confort.
- **Options** :
  1. **Projet d'intégration dans `MMV.sln`**, ignoré proprement si aucun serveur n'est configuré, + un
     **service PostgreSQL en CI**.
  2. **Promouvoir le harness du spike** dans la solution → il est écrit pour **comparer des providers**,
     pas pour valider MMV ; il porte encore SQL Server et un `AdaptedOpticDbContext` divergent.
  3. **Rester en preuves locales** → **incompatible** avec les critères de sortie P4 : rien ne serait
     reproduit à distance.
- **Recommandation** : **option 1**, avec une règle stricte : les tests d'intégration **s'ignorent
  explicitement** sans serveur (jamais de faux vert), et la CI **fournit** le serveur pour qu'ils
  s'exécutent réellement. Le harness du spike reste où il est, comme trace.
- **Impact** : nouveau projet de test ; `MMV.sln` ; CI ; **premier changement de `.github/workflows/**`
  de toute la phase P4** — à décider consciemment.

---

## Recommended P4-5 Implementation Plan

Découpage **proposé**, ordonné par dépendances réelles. Chaque sous-lot se termine par **commit + CI
verte sur le SHA exact**, conformément à la discipline P4.

| Sous-lot | Contenu | Dépend de | Sortie |
|---|---|---|---|
| **P4-5A** | *ce rapport* — audit, aucune modification | P4-4 partiel | décisions AD-1…AD-6 posées |
| **P4-5B** | **Décisions actées** par l'architecte (+ ADR courte `DateTime` si retenue). Documentaire | P4-5A | AD-1…AD-6 tranchées |
| **P4-5C** | **Neutralisation du modèle** : mapping monétaire (AD-1), retrait des littéraux `"REAL"` optiques, filtre d'index par provider (AD-4). **SQLite reste vert, une 15ᵉ migration SQLite est produite** | P4-5B | modèle provider-neutre, 1569 tests conservés |
| **P4-5D** | **Stratégie `DateTime`** (AD-2) : `IClock`, UTC partout, garde par test, dates civiles | P4-5B | O4 close, assertions en place |
| **P4-5E** | **Chaîne de migrations PostgreSQL** (AD-3) + factory design-time sélective + double contrôle de dérive en CI (AD-5) | P4-5C, P4-5D | **O7** — baseline serveur générée |
| **P4-5F** | **Tests d'intégration PostgreSQL** (AD-6) : T1, T2, T3, T4, T5 | P4-5E | schéma serveur **prouvé**, non supposé |
| **P4-5G** | **Levée du garde-fou de démarrage** ([App.axaml.cs:198](../../src/MMV.App/App.axaml.cs#L198)) + chaîne de préparation serveur. **Uniquement après P4-5F vert** | P4-5F | MMV démarre réellement sur PostgreSQL |
| *(retour P4-4)* | **O12** — re-preuve runtime des 14 primitives, **débloquée** par P4-5E | P4-5E | P4-4 peut se clore |

> **Note de dépendance, à porter à l'arbitrage.** P4-4 est déclarée `IN PROGRESS` mais sa validation
> runtime est `BLOCKED_BY_P4_5_SCHEMA` (rapport P4-4B0 §15). **P4-5 ne peut donc pas attendre la clôture
> de P4-4** : c'est P4-5E qui **débloque** P4-4. Cette inversion de dépendance P4-4 ↔ P4-5 est **réelle,
> documentée, et appartient à l'architecte** — ce rapport la constate, il ne la tranche pas.

---

## Risks

| # | Risque | Probabilité | Impact | Atténuation |
|---|---|---|---|---|
| **R-1** | **Porter `HasColumnType("REAL")` tel quel** : la précision monétaire **régresse** (float8 → float4) sans erreur ni signal | élevée si non traité | **critique** — données financières | AD-1 avant tout import (**O2 précède O11**) |
| **R-2** | **Échec de création du schéma** sur `"IsCurrent" = 1` | **certaine** si non traité | bloquant | AD-4 ; T2 vérifie **physiquement** |
| **R-3** | **Refus d'écriture `DateTime`** par EF sur les 17 sites `Local` | **certaine** si non traité | bloquant | AD-2 ; T4 avec assertion |
| **R-4** | **Décalage horaire silencieux** (2 h) par un futur chemin SQL brut, ou chronologie faussée entre postes désynchronisés | moyenne | **élevé** — donnée de santé horodatée | AD-2 ; supervision d'horloge en P4-8/P4-9 |
| **R-5** | **Deux chaînes de migrations divergent** en silence : le modèle avance, une seule baseline suit | moyenne | élevé | AD-3 + AD-5 (double contrôle de dérive) |
| **R-6** | **Casser la chaîne SQLite** en corrigeant le modèle : les bases déjà déployées re-migrent ou échouent | moyenne | élevé | la 15ᵉ migration SQLite est **planifiée**, pas subie ; suite 1569 verte exigée |
| **R-7** | **Faux vert** : tests d'intégration ignorés faute de serveur, interprétés comme réussite | moyenne | **élevé** — fausse confiance | AD-6 : ignorés **explicitement**, et CI **fournit** le serveur |
| **R-8** | **Séquences d'identité non resynchronisées** après import ⇒ collisions de clés à la première écriture | élevée si non traité | élevé | O11 / P4-7 ; T11 |
| **R-9** | **`HasData` dupliqué** (`SALE`, `ORDER`) présent des deux côtés à l'import | moyenne | moyen | O11 ; réconciliation explicite |
| **R-10** | **Divergence `lower()`** modifiant silencieusement les résultats de recherche | **certaine** | faible à moyen | T12 : **acter** le comportement par test, sur chaque provider |
| **R-11** | **Confondre monétaire et optique** et « corriger » 22 colonnes qui n'ont pas à l'être | moyenne | moyen — travail inutile, risque introduit | ADR §15 O2 l'exclut explicitement ; §Money P5 |
| **R-12** | **Re-tentative ajoutée** sans clé d'idempotence sur stock ou numérotation | faible | **critique** — double décrément | O13 ; interdiction déjà écrite dans l'ADR |

---

## Conclusion

**1. PostgreSQL ne peut pas démarrer une base propre aujourd'hui** — `POSTGRESQL_CLEAN_START = BLOCKED`,
par **trois** blocages cumulatifs : un garde-fou **volontaire** au démarrage (P4-3), **l'absence de
chaîne de migrations serveur**, et **un modèle EF non portable**. Lever le garde-fou seul ne produirait
qu'un échec plus tardif et moins lisible.

**2. La fondation acquise est solide et ne doit pas être refaite.** Sélection du provider centralisée,
`Npgsql` confiné à l'Infrastructure, Domain et Application provider-neutres (vérifié par test),
classification d'erreurs PostgreSQL livrée (**O5 fait**), dialecte d'upsert provider prouvé, rollback
sûr à l'annulation. **P4-5 construit sur une base saine.**

**3. Le périmètre réel est plus étroit — et plus profond — qu'il n'y paraît.**

- **Étroit** : seuls **30 `HasColumnType` écrits à la main** et **un seul filtre d'index** sont des
  défauts de *code*. Les 209 types du snapshot sont des inférences du provider SQLite et disparaissent
  d'eux-mêmes sur une baseline PostgreSQL. Le filtre `Notifications`, souvent compté comme à risque,
  est **portable tel quel**. L'obligation **O6** apparaît **déjà satisfaite par construction** — les 12
  `catch` ont été relus un à un.
- **Profond** : la **précision monétaire** et la **stratégie `DateTime`** ne sont pas des ajustements de
  mapping. L'une conditionne la fidélité de tout import futur (**O2 précède O11**) ; l'autre conditionne
  la **chronologie d'une base partagée entre postes** — c'est-à-dire la cohérence des mouvements de
  stock, des alertes et de la traçabilité d'ordonnance.

**4. Trois faits nouveaux, non consignés jusqu'ici, méritent l'attention de l'architecte :**

- **`REAL` n'est pas `REAL`.** Le même littéral désigne 8 octets sur SQLite et **4** sur PostgreSQL.
  Le portage naïf **dégraderait** une précision déjà jugée insuffisante — ce n'est pas un statu quo.
- **Le mapping monétaire est déjà incohérent en SQLite** : 11 colonnes en `REAL`, 3 en `TEXT`. Deux
  prix comparables n'ont pas le même type physique. Le portage révèle un défaut existant.
- **P4-5 débloque P4-4, et non l'inverse.** L'ordre nominal de la roadmap est contredit par
  `BLOCKED_BY_P4_5_SCHEMA` (P4-4B0 §15). **Arbitrage requis.**

**5. Rien n'a été décidé ici.** Six décisions sont posées (**AD-1** monétaire, **AD-2** `DateTime`,
**AD-3** migrations, **AD-4** index, **AD-5** contrôle de dérive en CI, **AD-6** tests d'intégration),
chacune avec problème, options, recommandation motivée et impact. **AD-3 est la plus structurante** :
tout le reste du découpage P4-5 en dépend.

**6. Ce que ce rapport ne prouve pas.** Il est **strictement statique** : aucun test n'a été exécuté
(restore NuGet impossible sur ce poste, §0.2), aucune connexion PostgreSQL n'a été ouverte. Tout ce qui
est étiqueté `NEEDS_RUNTIME_PROOF` **reste à prouver au runtime**. **La compatibilité applicative
complète de MMV sur PostgreSQL demeure `NOT_PROVED`**, et **la V1 multi-poste n'est pas `GO`**.

```
P4-5A AUDIT                   = COMPLETE
P4-5A CODE CHANGED            = NONE
P4-5A TESTS EXECUTED          = NONE (restore NuGet indisponible — §0.2)
POSTGRESQL CLEAN START        = BLOCKED (B1 garde-fou · B2 migrations · B3 modèle)
MONEY COLUMNS                 = 14 (11 REAL · 3 TEXT) — INCOHÉRENT
OPTICAL REAL COLUMNS          = 22 — HORS PÉRIMÈTRE O2
DATETIME COLUMNS              = 21 · DateTime.Now SITES = 17
FILTERED INDEXES              = 2 (1 INVALIDE · 1 PORTABLE)
HANDWRITTEN HasColumnType     = 30 (29 REAL · 1 TEXT)
OBLIGATION O6                 = SATISFAITE PAR CONSTRUCTION (statique)
ARCHITECTURAL DECISIONS       = 6 REQUISES (AD-1 … AD-6)
P4-5 IMPLEMENTATION           = NOT STARTED
V1 MULTI-POSTE                = NOT GO
```
