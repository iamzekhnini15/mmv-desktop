# P4-5E-A PostgreSQL Migration Architecture Audit

> **Statut : AUDIT — NON-CODE.** Aucun fichier `.cs`, test, migration, ADR, configuration ou workflow n'a été
> modifié. Aucun build, aucune restauration, aucun test n'a été exécuté. Seules des commandes **en lecture**
> (`git log`, `git ls-files`, `grep`, `wc`, `ls`) ont été lancées.
>
> Date : 21 septembre 2026 · Branche : `p4-multi-poste` · HEAD : `440ddcb352b12e0c6900cfd5d6df550e234856c5`
> (P4-5D-R CLOSED).
> Entrées : [ADR-PROD-DB-005](../architecture/adr-prod-db-005-migration-architecture.md) ·
> [ADR-PROD-DB-007](../architecture/adr-prod-db-007-schema-drift-prevention.md) ·
> [ADR-PROD-DB-008](../architecture/adr-prod-db-008-postgresql-integration-testing.md) ·
> [ADR-PROD-DB-002 §15](../architecture/adr-prod-db-002-server-database-provider-selection.md) ·
> [roadmap P4 §P4-5 … P4-8](../architecture/P4-multi-poste-roadmap.md) · rapport de réconciliation P4-5
> (présent dans l'arbre de travail, **non suivi**) · code à HEAD.
> **Le dépôt réel prime toujours sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `STATIC_CODE_PROOF` | constaté par lecture du code ou des fichiers du dépôt à HEAD |
| `REPO_COMMAND` | mesuré par une commande en lecture sur le dépôt (comptage, historique Git) |
| `EF_BEHAVIOR — À CONFIRMER` | comportement documenté d'EF Core / Npgsql / PostgreSQL, **non exécuté ici** ; à confirmer par essai en P4-5E |

---

## Objective

**Question posée :** *« Que manque-t-il pour qu'une nouvelle installation MMV puisse démarrer proprement sur
PostgreSQL ? »*

**Réponse courte.** Aujourd'hui, une installation PostgreSQL est **bloquée volontairement au démarrage**
([App.axaml.cs:205-214](../../src/MMV.App/App.axaml.cs#L205-L214)). Si l'on retirait ce garde-fou, la chaîne
de démarrage échouerait successivement sur sept manques :

| # | Manque | Lot propriétaire |
|---|---|---|
| 1 | **Aucune chaîne de migrations PostgreSQL** (ni projet, ni baseline, ni instantané) | **P4-5E** |
| 2 | **Aucune sélection d'assembly de migrations**, factory design-time **figée sur SQLite** | **P4-5E** |
| 3 | **Aucun gestionnaire de préparation serveur** : `SqliteDatabaseManager` est inutilisable sur PostgreSQL | P4-5G |
| 4 | **Aucune procédure de provisioning** : création de la base, rôles, droits, schéma | P4-8 |
| 5 | **Aucune sérialisation** de l'application des migrations, **aucune garde de version** app ↔ schéma | P4-6 |
| 6 | **Configuration de connexion non sécurisée** : secret en clair dans une variable d'environnement | P4-8 |
| 7 | **Aucun administrateur** sur une base neuve sans secret bootstrap fourni au premier démarrage | P4-8 / décision produit |

**P4-5E ne couvre que les points 1 et 2**, plus le double contrôle de dérive en CI. **Après P4-5E, une nouvelle
installation ne démarrera toujours pas sur PostgreSQL.** C'est conforme à la roadmap : la levée du garde-fou
appartient à P4-5G, **uniquement après P4-5F vert**.

Ce rapport établit l'état réel, confronte les trois ADR concernés au code, et liste les décisions à prendre
**avant** d'écrire le code de P4-5E.

---

## Current EF Architecture

### Synthèse

```
DbContext:              1 — OpticDbContext (21 DbSet), aucun second contexte
SQLite migrations:      14 + 1 instantané — src/MMV.Infrastructure/Migrations/ (29 fichiers)
PostgreSQL migrations:  AUCUNE — ni projet, ni dossier, ni baseline
Migration assemblies:   1 — MMV.Infrastructure (implicite : assembly du contexte ;
                        aucun appel MigrationsAssembly(...) dans src/)
Design-time factory:    OpticDbContextFactory — figée sur SQLite, ignore toute variable d'environnement
```

`STATIC_CODE_PROOF` + `REPO_COMMAND`.

### DbContext

- **Un seul contexte**, [OpticDbContext](../../src/MMV.Infrastructure/Data/OpticDbContext.cs), construit par
  options (`DbContextOptions<OpticDbContext>`). `UnitOfWork`, `EfTransactionRunner`, tous les repositories et la
  composition DI en dépendent directement.
- **`OnConfiguring`** ([OpticDbContext.cs:136-154](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L136-L154))
  ne reconfigure rien si les options sont fournies ; sinon, il résout le provider par
  `DatabaseProviderResolver.Resolve()` (l. 143) et délègue à `DatabaseProviderResolver.Configure`.
- **`OnModelCreating`** ([l. 159-205](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L159)) lit le provider
  **une seule fois** (`ModelPortability.For(Database.ProviderName)`, l. 169), le passe aux 9 configurations qui
  en dépendent, puis applique `UtcDateTimeConverter.ApplyTo` **en dernier** (l. 202). Un provider inconnu lève
  `NotSupportedException`.

### Configuration des providers

Paquets, tous dans [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) :
`Microsoft.EntityFrameworkCore.Sqlite 8.0.27`, `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11`,
`Microsoft.EntityFrameworkCore.Design 8.0.27` (`PrivateAssets=all`). Outil : `dotnet-ef 8.0.27`, verrouillé
par [.config/dotnet-tools.json](../../.config/dotnet-tools.json).

**Point de sélection** : [DatabaseProviderResolver.Configure](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L93)
appelle `UseSqlite(...)` (l. 105) ou `UseNpgsql(connectionString)` (l. 117). **Aucune option n'est passée à
`UseNpgsql`** : pas de `MigrationsAssembly`, pas de `MigrationsHistoryTable`, pas de retry, pas de timeout.

| Site de configuration EF | Passe par `DatabaseProviderResolver` ? | Remarque |
|---|---|---|
| [App.axaml.cs:129-130](../../src/MMV.App/App.axaml.cs#L129-L130) — composition runtime | **oui** | provider résolu une fois (l. 120) |
| [OpticDbContext.cs:143](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L143) — repli `OnConfiguring` | **oui** | — |
| [OpticDbContextFactory.cs:24](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L24) — design-time | **oui**, mais avec `DatabaseProviderOptions.Sqlite` codé en dur | — |
| [App.axaml.cs:240-241](../../src/MMV.App/App.axaml.cs#L240-L241) — reprise de l'ancien `mmv-optic.db` | **non**, `UseSqlite` direct | **voulu** : reprise d'un fichier SQLite local |
| [DependencyInjection.cs:38](../../src/MMV.Infrastructure/DependencyInjection.cs#L38) — `AddInfrastructure` | **non**, `UseSqlite` codé en dur | code **inerte** (documenté P2B-2J), jamais appelé |

**Conséquence pour G2** : placer `MigrationsAssembly` dans `DatabaseProviderResolver.Configure` couvre
**automatiquement** les trois sites gouvernés, dont la factory design-time. Les deux sites hors gouvernance sont
SQLite-only, donc **non concernés**. `AddInfrastructure` reste toutefois un piège latent : un appelant futur
obtiendrait SQLite quelle que soit la configuration (voir R-E17).

### Dépendance du modèle au provider — explicite et implicite

P4-5C a rendu **explicites** deux constructions dépendantes du provider. Le modèle en porte d'autres,
**implicites**, issues des conventions de chaque provider. Toutes se retrouvent dans l'instantané.

| Construction | SQLite (instantané actuel) | PostgreSQL (attendu) | Origine |
|---|---|---|---|
| 11 colonnes monétaires historiques `REAL` | `REAL` | `numeric(12,2)` | explicite — [ModelPortability.cs](../../src/MMV.Infrastructure/Data/Portability/ModelPortability.cs) |
| 3 colonnes monétaires sans type déclaré | `TEXT` | `numeric(12,2)` | explicite — même point |
| Filtre `idx_workshop_sheets_current_unique` | `"IsCurrent" = 1` | `"IsCurrent"` | explicite — même point |
| Clés entières générées | `INTEGER` + `Sqlite:Autoincrement` | colonnes identity (convention Npgsql) | implicite |
| `DateTime` | `TEXT` | `timestamp with time zone` | implicite (ADR-004 S1) |
| `DateOnly` | `TEXT` | `date` | implicite (ADR-004 C1) |
| `bool` | `INTEGER` | `boolean` | implicite |
| `string` avec `HasMaxLength(n)` | `TEXT` | `character varying(n)` | implicite |

**Constat** : l'instantané ne peut être partagé entre providers, même en l'absence de toute décision MMV. Cela
**confirme** la prémisse d'[ADR-PROD-DB-005 §2.3](../architecture/adr-prod-db-005-migration-architecture.md) :
deux chaînes exigent deux instantanés, donc deux assemblys.

### Design-time

[OpticDbContextFactory](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs) :

- résout le chemin SQLite et **crée son dossier** (l. 17-18), y compris en CI — effet de bord sans conséquence
  aujourd'hui, mais à ne pas reproduire dans une branche PostgreSQL ;
- passe `DatabaseProviderOptions.Sqlite` en dur (l. 24) ; le commentaire l. 20-23 renvoie explicitement le
  déverrouillage à « la chaîne de migrations serveur (P4-5) ».

Le contrôle CI ([ci.yml:118](../../.github/workflows/ci.yml#L118)) lance
`dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build`, **sans
`--startup-project`** : le projet cible sert aussi de projet de démarrage, et c'est la factory qui construit le
contexte.

### Préparation runtime

[SqliteDatabaseManager](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs) porte tout le cycle de vie
au démarrage : sauvegarde de fichier, détection d'état, `Migrate()`, adoption de bases historiques, vérifications,
reprise des dates civiles. **Il est SQLite de bout en bout** — voir *New Installation Flow Analysis*, étape 5.

### Cache de modèle (question P-1, ouverte)

P4-5C a mesuré qu'EF Core 8 isole déjà les modèles SQLite et PostgreSQL d'un même processus : chaque provider a
son propre fournisseur de services interne, donc son propre cache. Ce comportement est un **détail
d'implémentation d'EF**, verrouillé par un test. P4-5C a posé la question d'un `IModelCacheKeyFactory` explicite
et l'a rattachée à P4-5E
([rapport P4-5C, P-1](P4-5C-ef-model-portability-report.md)). **Non tranchée à ce jour.**

---

## Migration Analysis

### Inventaire des 14 migrations

`REPO_COMMAND` sur `src/MMV.Infrastructure/Migrations/*.cs` (hors `.Designer.cs` et instantané).

| # | Migration | Nature | Constructions SQLite spécifiques |
|---|---|---|---|
| 1 | `20260127184542_InitialCreate` | création des tables ; `InsertData Users` (admin, `UserId = 1`) | 11 × `Sqlite:Autoincrement` |
| 2 | `20260129192001_AddProductEntryDate` | colonne + `UpdateData` | 14 × `AlterColumn` (reconstructions de table) |
| 3 | `20260201181136_ProductSchemaRefactoring` | refonte produit | 2 × `Autoincrement`, 16 × `AlterColumn` |
| 4 | `20260202005050_AddNotifications` | table `Notifications` | 1 × `Autoincrement`, 14 × `AlterColumn` |
| 5 | `20260212164646_AddCounterSaleFieldsToOrder` | **`DeleteData Users` (`UserId = 1`)** | 14 × `AlterColumn` |
| 6 | `20260212173313_AddDepositAndRemainingAmountToOrder` | acompte / reste dû | 14 × `AlterColumn` |
| 7 | `20260212220902_RestoreSaleOrderSeparation` | `DropColumn` / `RenameColumn` | 14 × `AlterColumn` |
| 8 | `20260611114307_FixDateTimeDefaultValues` | retrait des `DEFAULT` figés (R-19) | 14 × `AlterColumn` |
| 9 | `20260612071633_AddDocumentSequences` | table + `InsertData` `SALE` / `ORDER` | — |
| 10 | `20260713215132_AddCustomerArchivingAndProtectHistory` | colonne + FK `Restrict` | — |
| 11 | `20260717183027_AddProductNormalizedReferenceAndProtectHistory` | 2 × `Sql` : **`GLOB`**, table d'arrêt `CHECK`, `UPPER(TRIM(…))` | **`GLOB`** |
| 12 | `20260719003826_AddWorkshopSheets` | 2 tables, index unique filtré | 2 × `Autoincrement`, **`filter: "\"IsCurrent\" = 1"`** (l. 93) |
| 13 | `20260720204330_AddNotificationResolution` | 1 × `Sql` (`ROW_NUMBER() OVER`), index filtré | aucune (SQL portable) |
| 14 | `20260721134634_AddNormalizedUsernameAndSecureLocalUsers` | 4 × `Sql` : **`GLOB`**, 3 tables d'arrêt, `lower(trim(…))` ; `AlterColumn` pour retirer un `DEFAULT` | **`GLOB`**, reconstruction |

**Agrégats mesurés**

| Mesure | Valeur |
|---|---|
| Littéraux de type dans les migrations | `TEXT` × 220 · `INTEGER` × 67 · `REAL` × 35 · **aucun type PostgreSQL** |
| `Sqlite:Autoincrement` | 16, dans 4 migrations |
| `migrationBuilder.Sql(...)` | 7, dans 3 migrations ; `GLOB` dans 2 |
| `AlterColumn` (Up + Down) | 102, dans 8 migrations — ordre de grandeur cohérent avec « ~90 » d'ADR-005 §2.1 |
| Opérations de données | `InsertData` × 3 (dont 1 dans un `Down`), `UpdateData` × 6, `DeleteData` × 1 |
| Instantané | 1 : `OpticDbContextModelSnapshot` — `INTEGER` × 61, `TEXT` × 115, `REAL` × 33 ; filtres en forme SQLite ; `HasData` `DocumentSequences` ; `ProductVersion 8.0.27` |
| Version EF des fichiers `.Designer.cs` | 7 × `8.0.0`, 7 × `8.0.27` (informatif, sans effet) |
| Dernier commit touchant le dossier | `8d4bf49` (P3-10) — **aucun commit P4 ne l'a modifié** |

### Couplages qui fixent la chaîne SQLite à sa place

La chaîne SQLite ne peut pas être déplacée ni renommée :

- `SqliteDatabaseManager` reconnaît des migrations **par suffixe d'identifiant**
  ([l. 671-737](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L671-L737)) pour décider de ce qu'il
  baseline ou exécute lors de l'adoption d'une base historique ;
- il écrit `__EFMigrationsHistory` **directement**
  ([l. 744-762](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L744-L762)) ;
- plusieurs suites de tests énumèrent `GetMigrations()` ou `IMigrationsAssembly.Migrations`
  (`SqliteDatabaseManagerTests`, `NormalizedUsernameMigrationTests`, `NotificationResolutionMigrationTests`…).

Cela **confirme** ADR-005 §5.2 : la chaîne SQLite reste **exactement** dans `MMV.Infrastructure`.

### Table de synthèse

| Element | Current State | Risk |
|---|---|---|
| Nombre de migrations | 14, toutes SQLite | **Nul** tant qu'elles ne visent que SQLite ; **bloquant** si rejouées sur PostgreSQL |
| Provider cible | SQLite exclusivement (littéraux `INTEGER`/`REAL`/`TEXT`, annotations `Sqlite:*`) | aucune n'est rejouable sur PostgreSQL |
| `GLOB` + tables d'arrêt `CHECK` | 2 migrations (11, 14) | `GLOB` **n'existe pas** en PostgreSQL : échec immédiat |
| Filtre `"IsCurrent" = 1` | migration 12, l. 93, et instantané l. 948 | **invalide** en PostgreSQL (booléen ≠ entier) : la création d'index, donc du schéma, échoue |
| Reconstructions de table (`AlterColumn`) | 102 occurrences, 8 migrations | chemins SQLite et PostgreSQL **incomparables** ; sans objet pour une base neuve |
| Admin inséré puis supprimé | `InsertData` (1) puis `DeleteData` (5) | une base migrée jusqu'à la tête contient **0 utilisateur** ; vrai aussi pour la future baseline PostgreSQL (voir *Installation*) |
| `HasData` `DocumentSequences` | clé `string`, 2 lignes | **faible** : aucune séquence d'identité à resynchroniser ; sera reprise telle quelle par la baseline PostgreSQL |
| Instantané | 1, typé SQLite | **ne peut pas** servir la chaîne PostgreSQL (§ *Current EF Architecture*) |
| Nouvelle chaîne dans la **même** assembly | — | EF découvre toutes les migrations `[DbContext(typeof(OpticDbContext))]` de l'assembly de migrations : deux chaînes s'y **mélangeraient**. `EF_BEHAVIOR — À CONFIRMER`. Raison structurelle de l'assembly séparée |
| Déplacement de la chaîne SQLite | — | casserait l'adoption par suffixe et les tests : **interdit** (ADR-005 §5.2, G4) |
| `__EFMigrationsHistory` | nom et schéma par défaut ; écrit à la main pour l'adoption SQLite | côté PostgreSQL, **nom et schéma à décider avant la baseline** (D-08) |

**Conclusion.** À HEAD `440ddcb`, la chaîne est **100 % SQLite**, intacte depuis P3-10, et **aucun élément** de
la chaîne PostgreSQL n'existe. Le constat d'ADR-005 §2.1 reste exact et chiffré.

---

## ADR Compliance Review

### ADR-PROD-DB-005 — architecture des migrations

| Décision | Implémentation à HEAD | Statut |
|---|---|---|
| §5.1 Un seul modèle, un seul `DbContext` | seul `OpticDbContext` existe ; P4-5C a rendu le modèle provider-sélectif **sans second contexte** | **APPLIQUÉE** |
| §5.2 Deux assemblys de migrations | côté SQLite : conforme, en place et intact. Côté PostgreSQL : **absent** | **PARTIELLE** — G1 ouverte |
| §5.3 Sélection de l'assembly dans `DatabaseProviderResolver` | `Configure` ne passe **aucune** option à `UseNpgsql` | **NON APPLIQUÉE** — G2 |
| §5.4 Baseline générée par l'outillage | rien n'est généré | **SANS OBJET** à ce jour |
| §5.5 Factory design-time sélective, SQLite par défaut | factory figée sur SQLite | **NON APPLIQUÉE** — G3 |
| §5.6 Génération sans serveur ; chaîne design-time issue d'une variable d'environnement | aucune variable design-time définie ; **le nom n'est pas fixé** par l'ADR | **NON APPLIQUÉE** — voir D-05 |
| §5.7 Règle de double migration | non documentée ; **aucun `CONTRIBUTING.md`** dans le dépôt | **NON APPLIQUÉE** — G6 |
| §5.8 `__EFMigrationsHistory` PostgreSQL neuf | rien ne le contredit | **SANS OBJET** jusqu'à P4-7 |
| §5.9 Assembly PostgreSQL dans l'anneau Infrastructure | sans objet ; [ApplicationArchitectureTests.cs:35](../../tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs#L35) interdit toute référence préfixée `MMV.Infrastructure` depuis Application | **SANS OBJET** — couverture automatique si le nom commence par `MMV.Infrastructure` |
| §5.10 `Migrate()` par poste inadapté en multi-poste | `Migrate()` reste appelé par poste, **légitimement pour SQLite mono-poste** | **RECONNUE** — P4-6 / O8 |
| G4 Migrations SQLite inchangées | aucun commit sur le dossier depuis `8d4bf49` | **VRAIE à HEAD**, à re-prouver à la clôture de P4-5E |

### ADR-PROD-DB-007 — prévention de la dérive

| Décision | Implémentation à HEAD | Statut |
|---|---|---|
| §5.1 Un contrôle de dérive par chaîne, tous deux bloquants | **un seul**, SQLite ([ci.yml:118](../../.github/workflows/ci.yml#L118)) ; `ci.yml` inchangé depuis `8cf0919` (P4-0) | **PARTIELLE** — S1 ouverte |
| §5.2 Un seul chemin de génération | vrai aujourd'hui (factory SQLite) ; à préserver après G3 | **APPLIQUÉE** pour la seule chaîne existante |
| §5.3 Vérification physique du schéma PostgreSQL | — | **NON APPLIQUÉE** — P4-5F (S3) |
| §5.4 Reproductibilité (base neuve migrée deux fois) | — | **NON APPLIQUÉE** — P4-5F (S4) |
| §5.5 `EnsureCreated()` interdit en production et en intégration PostgreSQL | **une occurrence dans un chemin de démarrage** : [DbInitializer.cs:19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19), atteint par `DatabaseSeeder.Seed` lorsque le seed de démonstration est activé | **À QUALIFIER** — contradiction C-1 |
| §5.6 Règle de double migration vérifiable | — | **NON APPLIQUÉE** — S6 |
| §5.7 Dérive de la base de production bloquée au démarrage | **aucune** détection de migrations appliquées inconnues de l'application (`GetAppliedMigrations` n'apparaît qu'à [SqliteDatabaseManager.cs:317](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L317), en installation neuve) | **NON APPLIQUÉE** — P4-6 (S7) |
| §5.8 Aucune modification manuelle du schéma de production | documentation d'exploitation | **SANS OBJET** — P4-8/P4-9 |

### ADR-PROD-DB-008 — tests d'intégration PostgreSQL

| Décision | Implémentation à HEAD | Statut |
|---|---|---|
| §5.1 Projet d'intégration dédié dans `MMV.sln` | `MMV.sln` : 4 projets `src`, 3 projets `tests`, **aucun** projet d'intégration | **NON APPLIQUÉE** — Q1 (P4-5F) |
| §5.2 Variable de test distincte de la production | aucune. Les deux tests unitaires qui construisent un modèle Npgsql utilisent une chaîne factice codée en dur, **sans connexion** ([EfModelPortabilityTests.cs:107](../../tests/MMV.Domain.Tests/Data/Portability/EfModelPortabilityTests.cs#L107)) — conforme : aucun secret, aucun serveur | **NON APPLIQUÉE** — Q2 |
| §5.3 Serveur fourni par la CI | — | **NON APPLIQUÉE** — Q3 |
| §5.4 Garde anti-faux-vert | — | **NON APPLIQUÉE** — Q4 |
| §5.5 Schéma créé par migrations | **impossible** tant que la chaîne PostgreSQL n'existe pas | **BLOQUÉE PAR P4-5E** |
| §5.6 à §5.11 | — | P4-5F / P4-8 / P4-10 |

**P4-5E est le prérequis dur de tout ADR-008** : le corpus N1 … N8 exige une base créée par migrations.

### Contradictions et tensions relevées

| # | Nature | Constat | Gravité |
|---|---|---|---|
| **C-1** | ADR-007 §5.5 ↔ code | `DbInitializer.Initialize` appelle `EnsureCreated()` ([l. 19](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L19)) dans le **binaire de production**, sur le chemin du seed de démonstration (Development/Demonstration + `MMV_ENABLE_DEMO_SEED`). **Aujourd'hui sans effet** : `PrepareDatabase` a déjà migré la base, donc `EnsureCreated()` ne crée rien. L'ADR ne dit pas si ce chemin compte comme « chemin de production ». Sur PostgreSQL, un ordre d'appel inversé créerait un schéma **sans historique de migrations** | faible aujourd'hui ; à trancher avant P4-5G |
| **C-2** | ADR-005 §5.6 ↔ ADR-007 §5.2 | L'ADR-005 impose une chaîne design-time issue d'une variable d'environnement, **sans la nommer**. Réutiliser `MMV_DATABASE_PROVIDER` / `MMV_DATABASE_CONNECTION_STRING` couplerait la génération à l'environnement runtime du développeur : une variable positionnée pour lancer l'application sur PostgreSQL ferait générer la migration sur la mauvaise chaîne | moyenne — décision D-05 |
| **C-3** | ADR-005 §5.3 ↔ déploiement | En EF Core 8, `MigrationsAssembly` reçoit un **nom** d'assembly, chargé à l'exécution (`EF_BEHAVIOR — À CONFIRMER`). L'assembly PostgreSQL doit donc être **déployée avec `MMV.App`**, et donc référencée par elle. Cette obligation n'apparaît pas dans G1 … G7 | faible — à inscrire dans le périmètre |
| **C-4** | ADR-005 §5.3 ↔ P4-3 | `DependencyInjection.AddInfrastructure` configure `UseSqlite` en dur, hors du point de sélection unique. Code inerte, mais il contredit le principe « un seul point de décision » | faible |
| **C-5** | chiffres des ADR | ADR-007 et ADR-008 citent une baseline de **1569** tests ; le rapport de réconciliation mesure **1915** à HEAD. Chiffres **historiques**, pas une contradiction de décision | documentaire |

Aucune contradiction **entre** les trois ADR n'a été trouvée : leurs obligations s'enchaînent sans recouvrement
(005 → 007 → 008).

---

## New Installation Flow Analysis

### Scénario : un nouvel opticien

```
[0] Installation de MMV sur les postes      → AUCUN installeur dans le dépôt
        ↓
[1] Installation du serveur PostgreSQL      → procédure de LABORATOIRE uniquement (P4-1 Lot C)
        ↓
[2] Création base + rôles + droits          → RIEN
        ↓
[3] Configuration de chaque poste           → 2 variables d'environnement, à poser à la main
        ↓
[4] Démarrage de l'application              → BLOQUÉ volontairement (App.axaml.cs:205)
        ↓
[5] Application des migrations              → RIEN (ni chaîne, ni gestionnaire serveur)
        ↓
[6] Seed initial                            → EXISTE, provider-neutre en apparence, NON PROUVÉ sur PostgreSQL
        ↓
[7] Application prête (connexion)           → IMPOSSIBLE sans secret bootstrap fourni au 1er démarrage
```

### Détail par étape

| Étape | Existe | Manque | Lot |
|---|---|---|---|
| **0. Installation MMV** | rien : `git ls-files` ne montre aucun fichier d'installeur, profil de publication, script ou `appsettings` | packaging, procédure, prérequis | non attribué explicitement (P4-8 « procédure d'installation ») |
| **1. Serveur PostgreSQL** | PostgreSQL 17.4 natif observé en laboratoire ([P4-1 Lot C](P4-1-windows-network-lot-c-report.md)) ; `pg_hba.conf` en `scram-sha-256` | version cible, procédure de production | P4-8 (Q8) |
| **2. Base, rôles, droits** | rôles de laboratoire : `mmv_spike` (`CREATEDB`), `mmv_app` (`db_owner`, « raccourci jetable ») | **tout** : qui exécute `CREATE DATABASE`, quel propriétaire, quel schéma, quels droits runtime | P4-8 (O10) |
| **3. Configuration des postes** | `MMV_DATABASE_PROVIDER=postgresql` + `MMV_DATABASE_CONNECTION_STRING` ; échec explicite si la chaîne manque, sans repli SQLite | stockage protégé, distribution, TLS | P4-8 |
| **4. Démarrage** | garde-fou : `DatabaseConfigurationException` avant toute préparation ([App.axaml.cs:205-214](../../src/MMV.App/App.axaml.cs#L205-L214)) | levée conditionnelle du garde-fou | P4-5G |
| **5. Migrations** | chaîne SQLite + `SqliteDatabaseManager` | chaîne PostgreSQL (**P4-5E**) ; **gestionnaire serveur** (P4-5G) ; sérialisation (P4-6) | P4-5E → P4-5G → P4-6 |
| **6. Seed** | [DatabaseSeeder](../../src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs) (LINQ EF, sans SQL brut) ; `DocumentSequences` `SALE`/`ORDER` via `HasData`, donc **inclus d'office dans la baseline** | preuve sur PostgreSQL ; comportement concurrent | P4-5F / P4-6 |
| **7. Application prête** | écran de connexion | **premier administrateur** (voir ci-dessous) | P4-8 / décision produit |

### Pourquoi `SqliteDatabaseManager` ne peut pas servir PostgreSQL

`STATIC_CODE_PROOF` :

- `GetDatabaseFilePath` parse la chaîne de connexion avec `SqliteConnectionStringBuilder`
  ([l. 1177-1182](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L1177-L1182)). Sur une chaîne
  Npgsql (`Host=…`), il lève dès la première ligne de `PrepareDatabase` (`EF_BEHAVIOR — À CONFIRMER` :
  mot-clé non pris en charge) ;
- la détection d'état lit `sqlite_master` (l. 1158), les contrôles d'index lisent `PRAGMA index_list` et
  `index_info` ;
- la sauvegarde est un `File.Copy` du fichier et de ses annexes `-wal`/`-shm` (l. 189-218) ;
- la reprise des dates civiles est SQLite-only — **sans objet** pour une base PostgreSQL neuve.

Un **équivalent serveur** est donc nécessaire. Il n'appartient pas à P4-5E. Ses invariants utiles sont
réutilisables sur le principe : état détecté, aucune migration en attente après préparation, agrégats lisibles,
échec explicite.

### Trois constats sur le premier démarrage

**(a) Une base neuve ne contient aucun utilisateur.** `InitialCreate` insère `admin`
([l. 324](../../src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs#L324)) puis
`AddCounterSaleFieldsToOrder` le supprime
([l. 14](../../src/MMV.Infrastructure/Migrations/20260212164646_AddCounterSaleFieldsToOrder.cs#L14)). Le modèle
ne porte aucun `HasData` utilisateur : **la baseline PostgreSQL naîtra elle aussi sans utilisateur**. Un
administrateur n'est créé que si `MMV_BOOTSTRAP_ADMIN_PASSWORD` est positionné au démarrage
([DatabaseSeeder.cs:137-140](../../src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs#L137-L140)). Sinon,
personne ne peut se connecter, et **aucun parcours applicatif** ne permet de créer le premier compte. Ce
comportement est **voulu et documenté** ([adr-environments-seeding §2.3](../architecture/adr-environments-seeding.md)).
En multi-poste, il pose deux questions nouvelles : sur quel poste le secret doit-il être posé, et combien de
temps doit-il y rester ?

**(b) Le seed tourne sur chaque poste, à chaque démarrage.** Deux postes démarrant ensemble sur une base neuve,
avec le secret, exécutent chacun `!Users.Any(Role == Admin)` puis `Users.Add(...)`. L'index unique sur
`NormalizedUsername` ([UserConfiguration.cs:33](../../src/MMV.Infrastructure/Data/Configurations/UserConfiguration.cs#L33))
en rejette un. L'intégrité est préservée, mais l'erreur n'est **pas** une `DatabaseMigrationException` : voir (c).

**(c) Le démarrage avale les erreurs non migratoires.** Le `catch (Exception)` de
[App.axaml.cs:274-278](../../src/MMV.App/App.axaml.cs#L274-L278) journalise en `Debug` et **laisse démarrer**
l'application. Sur SQLite, `PrepareDatabase` enveloppe toutes ses erreurs en `DatabaseMigrationException`, et ce
`catch` ne voit que les erreurs du seed ou de la reprise de l'ancien fichier. Sur PostgreSQL, un serveur
injoignable ou un seed en échec afficherait l'écran de connexion devant une base inutilisable. Cela contredit
l'exigence O10 : « indisponibilité serveur gérée ».

### Ensemble minimal manquant pour « nouvelle installation PostgreSQL = OK »

1. chaîne de migrations PostgreSQL et sélection de l'assembly — **P4-5E** ;
2. preuve serveur de cette chaîne (N1, N2) — **P4-5F** ;
3. gestionnaire de préparation serveur et levée du garde-fou — **P4-5G** ;
4. désignation de l'acteur qui applique les migrations, et garde de version — **P4-6** ;
5. procédure de provisioning (base, rôles, droits, schéma) et configuration protégée des postes — **P4-8** ;
6. mécanisme du premier administrateur adapté au multi-poste — **P4-8**, avec une décision produit.

---

## Connection Configuration Review

*Constats uniquement ; aucune implémentation n'est proposée dans cette partie.*

### Où la configuration est stockée

**100 % variables d'environnement.** Aucun `appsettings.json` : `git ls-files` n'en montre aucun.
`Microsoft.Extensions.Configuration.Abstractions` est référencé, mais seul le code inerte `AddInfrastructure`
consomme une `IConfiguration` (chaîne nommée `OpticDatabase`).

| Variable | Rôle | Défaut | Lue par |
|---|---|---|---|
| `MMV_DATABASE_PROVIDER` | `sqlite` / `postgresql` (alias `postgres`, `npgsql`) | SQLite si absente ou vide ; **exception** si valeur inconnue | `DatabaseProviderResolver` |
| `MMV_DATABASE_CONNECTION_STRING` | chaîne serveur — **secret** | obligatoire pour PostgreSQL, sinon exception ; ignorée pour SQLite | `DatabaseProviderResolver` |
| `MMV_DATABASE_PATH` | chemin du fichier SQLite | `%LOCALAPPDATA%\ManageMyVision\mmv.db` | `SqliteDatabasePathResolver` |
| `MMV_ENVIRONMENT` | Production / Development / Demonstration / Test | **Production** (défaut sûr) | `SeedOptionsResolver` |
| `MMV_ENABLE_DEMO_SEED` | seed de démonstration | `false` | `SeedOptionsResolver` |
| `MMV_BOOTSTRAP_ADMIN_USERNAME` / `_PASSWORD` | premier administrateur — le mot de passe est un **secret** | `admin` / aucun | `SeedOptionsResolver` |

### Comment SQLite est choisi

Par **absence** de configuration : sans `MMV_DATABASE_PROVIDER`, SQLite est retenu et le fichier est résolu par
`SqliteDatabasePathResolver` (explicite → `MMV_DATABASE_PATH` → configuration → `%LOCALAPPDATA%`). La
résolution a lieu **une fois** par composition ([App.axaml.cs:120](../../src/MMV.App/App.axaml.cs#L120)). La
factory design-time ne lit aucune variable.

### Comment PostgreSQL serait configuré aujourd'hui

`MMV_DATABASE_PROVIDER=postgresql` et `MMV_DATABASE_CONNECTION_STRING=<chaîne Npgsql>` sur chaque poste. La
sélection EF fonctionne (P4-3), puis le démarrage est bloqué par le garde-fou. La chaîne est passée **telle
quelle** à `UseNpgsql` : TLS, délais, pool et nom d'application ne dépendent que de ce que l'opérateur y écrit.
MMV n'impose rien.

### Constats de sécurité

| # | Constat | Preuve |
|---|---|---|
| S-1 | **Secret en clair** dans une variable d'environnement. Portée utilisateur : lisible par tout processus de l'utilisateur. Portée machine : lisible par tous les comptes du poste. Aucun chiffrement au repos (ni DPAPI, ni gestionnaire d'identifiants, ni `pgpass`, ni authentification intégrée) | `STATIC_CODE_PROOF` |
| S-2 | Le secret **n'est jamais restitué** dans les messages d'erreur (provider inconnu, chaîne absente) — conforme à O10 | [DatabaseProviderResolver.cs](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs), `DatabaseConfigurationException` |
| S-3 | Aucune journalisation sensible : ni `EnableSensitiveDataLogging` ni `LogTo` dans `src/` ; `Debug.WriteLine` ne trace que le chemin SQLite | `grep` |
| S-4 | **Aucune exigence TLS** imposée par MMV : le chiffrement du transport dépend entièrement de la chaîne fournie et du serveur | `STATIC_CODE_PROOF` |
| S-5 | **Aucun modèle de droits de production** : seuls des rôles de laboratoire existent (`CREATEDB`, `db_owner`). ADR-002 les qualifie de « raccourci jetable » | ADR-002 l. 254, 300, 963 |
| S-6 | Le **mot de passe bootstrap** suit le même canal (S-1). En multi-poste, rien n'indique sur quel poste le poser ni quand le retirer | `STATIC_CODE_PROOF` |
| S-7 | Le garde-fou de démarrage n'est couvert par **aucun test automatisé**. `DatabaseConfigurationException` n'est testée que dans `DatabaseProviderResolverTests` | `grep` |
| S-8 | P4-5E devra introduire une **chaîne design-time** : si elle réutilise la variable de production, un vrai secret pourrait finir dans un shell de développement ou dans la CI | ADR-005 §5.6 |

---

## Database Upgrade Analysis

### Scénario

```
Poste(s) : Application 1.0 — Schéma 1.0
    ↓ mise à jour
Poste(s) : Application 1.1 — Schéma 1.1 attendu
```

### Mécanisme existant (SQLite, mono-poste)

`STATIC_CODE_PROOF` — [SqliteDatabaseManager.PrepareDatabase](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L74),
à **chaque démarrage** :

1. copie de sauvegarde horodatée du fichier et de ses annexes (l. 89-95) ;
2. détection de l'état : `Empty` / `HistoricalWithoutMigrationsHistory` / `MigrationsManaged` (l. 154-183) ;
3. `MigrationsManaged` → `GetPendingMigrations()` puis `Migrate()` (l. 329-349) ;
4. vérification : **zéro migration en attente**, agrégats lisibles, deux invariants structurels (l. 768-830) ;
5. reprise du format des dates civiles (l. 124) ;
6. en cas d'échec : `DatabaseMigrationException`, base et sauvegarde conservées, démarrage arrêté.

**La montée 1.0 → 1.1 est donc automatique** au premier lancement de la version 1.1, sauvegarde comprise. C'est
adapté au mono-poste SQLite.

### Comment MMV « connaît » la version de schéma

- **Implicitement** : les identifiants de migrations compilés dans l'assembly, comparés aux lignes de
  `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`). `ProductVersion` est la version **d'EF**, pas celle
  de MMV.
- **Aucune version applicative** : ni `<Version>` ni `AssemblyVersion` dans les `.csproj` ou dans
  [Directory.Build.props](../../Directory.Build.props). Tous les binaires portent la version par défaut.
- **Aucune table de compatibilité** application ↔ schéma.

### Manques et risques pour PostgreSQL multi-poste

| # | Manque | Conséquence | Lot |
|---|---|---|---|
| **U-1** | **Schéma plus récent que l'application : non détecté.** Seul `GetPendingMigrations()` est consulté ; les migrations appliquées mais inconnues de l'application ne le sont jamais | un poste resté en 1.0 face à un schéma 1.1 trouve « 0 en attente » et **démarre normalement**, puis échoue à l'usage (par exemple sur une nouvelle colonne `NOT NULL`). Rare en mono-poste ; **cas normal** pendant un déploiement multi-poste | P4-6 (O8, ADR-007 §5.7) |
| **U-2** | **Aucune sérialisation** : chaque poste appellerait `Migrate()` au démarrage. EF Core 8 n'a pas de verrou de migration natif (EF Core 9 en introduit un) — `EF_BEHAVIOR — À CONFIRMER` | deux postes mis à jour en même temps exécutent le même DDL en concurrence ; l'un échoue au démarrage | P4-6 (O8) |
| **U-3** | **Aucune sauvegarde serveur avant migration** : l'équivalent de `File.Copy` est `pg_dump`, qui exige outillage et droits | montée de version d'une base de production sans point de retour | P4-9 (O9) |
| **U-4** | **Droits DDL au runtime** : `Migrate()` depuis l'application exige que le rôle applicatif puisse modifier le schéma | conflit avec le moindre privilège (O10) | P4-6 / P4-8 |
| **U-5** | **Fenêtre de versions mixtes** : un `ALTER TABLE` prend un verrou exclusif sur la table (`EF_BEHAVIOR — À CONFIRMER`) pendant que d'autres postes travaillent | postes bloqués, délais dépassés, erreurs en cours de vente | P4-6, P4-10 |
| **U-6** | **Échec partiel** : Npgsql applique chaque migration dans sa propre transaction, et le DDL PostgreSQL est transactionnel (`EF_BEHAVIOR — À CONFIRMER`) | après un échec, la base reste à une version **intermédiaire mais cohérente**. L'application doit alors refuser de démarrer, ce que ferait un portage de `VerifyAfterPreparation` | P4-5G |
| **U-7** | **Aucune stratégie de retour arrière** : les `Down()` existent mais aucun outil ne les appelle ; le `Down` d'une baseline supprime tout | le seul retour arrière réaliste est la **restauration** d'une sauvegarde | P4-9 |
| **U-8** | **Double écriture des migrations de données** : une migration de données future (du type P4-5D-R) doit être écrite pour chaque provider | effort doublé, risque de divergence ; couvert en partie par la règle de double migration (G6/S6) | P4-5E (règle) |

---

## Risks Identified

| # | Risque | Probabilité | Impact | Traitement | Lot |
|---|---|---|---|---|---|
| **R-E1** | Baseline PostgreSQL placée dans `MMV.Infrastructure` : **mélange des deux chaînes** à la découverte EF | faible (ADR-005 l'interdit) | **critique** | assembly séparée (G1) ; test « la chaîne SQLite compte 14 migrations, la chaîne PostgreSQL uniquement la sienne » | P4-5E |
| **R-E2** | Génération sur la **mauvaise chaîne**, parce que la sélection design-time réutilise les variables runtime (C-2) | moyenne | élevé | variable design-time dédiée (D-05) | P4-5E |
| **R-E3** | Assembly PostgreSQL **non déployée** avec `MMV.App` : chargement impossible au runtime (C-3) | moyenne | élevé à P4-5G | référence depuis `MMV.App` et depuis les tests | P4-5E / P4-5G |
| **R-E4** | Contrôle de dérive PostgreSQL rouge en CI parce que la factory exige une chaîne absente | moyenne | faible | chaîne factice non secrète dans le workflow ; factory tolérante en design-time | P4-5E |
| **R-E5** | Une chaîne prend du retard **en silence** (R-5 de P4-5A) | élevée sans S1 | élevé | double contrôle bloquant (S1) | P4-5E |
| **R-E6** | Baseline générée sous une **mauvaise décision de schéma** (schéma, table d'historique) : la corriger coûte une migration | moyenne | moyen | trancher D-08 **avant** génération | P4-5E |
| **R-E7** | `EnsureCreated()` sur un chemin de démarrage (C-1) | faible | moyen sur PostgreSQL | qualification par l'architecte | avant P4-5G |
| **R-E8** | `catch (Exception)` générique au démarrage : application lancée sur une base inutilisable | moyenne sur PostgreSQL | élevé | politique d'échec (D-15) | P4-5G / P4-8 |
| **R-E9** | `SqliteDatabaseManager` inutilisable sur PostgreSQL | **certaine** | bloquant | gestionnaire serveur dédié | P4-5G |
| **R-E10** | Schéma plus récent que l'application : non détecté (U-1) | élevée en multi-poste | élevé | garde de version | P4-6 |
| **R-E11** | Migrations concurrentes depuis plusieurs postes (U-2) | élevée en multi-poste | élevé | sérialisation | P4-6 |
| **R-E12** | Base neuve **sans administrateur** ; secret bootstrap en clair sur les postes | **certaine** sans secret | élevé (installation inutilisable) | décision D-12 | P4-8 |
| **R-E13** | Modèle de droits : création automatique de la base par Npgsql (`CREATEDB`), droits DDL au runtime, schéma `public` restreint depuis PostgreSQL 15 (`EF_BEHAVIOR — À CONFIRMER`) | élevée | bloquant pour l'installation | D-08, D-09, D-10 | P4-8 |
| **R-E14** | Secret de connexion en clair (S-1), aucune exigence TLS (S-4) | **certaine** | moyen à élevé | D-13, D-14 | P4-8 |
| **R-E15** | Isolation du cache de modèle reposant sur un détail d'EF (P-1) | faible | élevé si réalisé | décision D-07 | P4-5E |
| **R-E16** | Garde-fou de démarrage non testé (S-7) : P4-5E modifie `DatabaseProviderResolver.Configure`, et une régression passerait inaperçue | faible | élevé | test de non-régression du garde-fou, ou preuve équivalente | P4-5E |
| **R-E17** | `AddInfrastructure` inerte avec `UseSqlite` codé en dur (C-4) | faible | moyen | à trancher : supprimer ou aligner (hors P4-5E) | ultérieur |
| **R-E18** | Chiffres de baseline obsolètes dans ADR-007 et ADR-008 (C-5) | — | documentaire | note de réconciliation | P4-5E (rapport) |

---

## Architecture Decisions Required

Légende : **B** = bloquante **avant le code de P4-5E** · **L** = bloquante pour un lot ultérieur, indiqué.
Les décisions déjà tranchées par un ADR sont rappelées pour confirmation uniquement.

### Migration architecture

| # | Question | Options | Recommandation de l'audit | Portée |
|---|---|---|---|---|
| **D-01** | **Deux assemblys de migrations ou autre approche ?** | *Tranché* par ADR-005 §5 (Option 1). Restent le **nom**, l'**emplacement** et le **contenu** du projet | projet `src/MMV.Infrastructure.<…>` ne contenant **que** des migrations. Le préfixe `MMV.Infrastructure` entre automatiquement dans la garde [ApplicationArchitectureTests.cs:35](../../tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs#L35) | **B** |
| **D-02** | **Un `DbContext` ou plusieurs ?** | *Tranché* : un seul (ADR-005 §5.1, Option 2 rejetée) | confirmer, aucun changement | confirmation |
| **D-03** | **Stratégie d'instantané ?** | *Tranché* : un instantané par assembly. Restent le nom de la baseline et la règle « baseline générée, non retouchée » (§5.4) | baseline unique, nom explicite ; aucune retouche manuelle ; `migrations remove` proscrit une fois fusionnée | **B** (nom) |
| **D-04** | **Projet de démarrage pour `dotnet ef`** sur la chaîne PostgreSQL. EF exige que le projet de démarrage référence l'assembly de migrations (`EF_BEHAVIOR — À CONFIRMER`) ; `MMV.Infrastructure` ne le peut pas (référence circulaire) | (a) le projet de migrations lui-même, avec `Microsoft.EntityFrameworkCore.Design 8.0.27` ; (b) `MMV.App` | (a) : autonome, aucune dépendance à l'UI, version de paquet déjà utilisée par le dépôt | **B** |
| **D-05** | **Variable de sélection design-time** | (a) réutiliser `MMV_DATABASE_PROVIDER` / `MMV_DATABASE_CONNECTION_STRING` ; (b) variables **dédiées au design-time** | (b) : supprime C-2 et S-8, et rend la CI explicite | **B** |
| **D-06** | **`MigrationsAssembly` explicite côté SQLite ?** | (a) défaut implicite inchangé ; (b) nom explicite `MMV.Infrastructure` | (a) : zéro différence runtime SQLite, G4 trivialement vraie | **B** |
| **D-07** | **P-1 : `IModelCacheKeyFactory` explicite ?** | A statu quo (test existant) ; B garde explicite | à l'architecte ; P4-5C l'a rattachée à P4-5E | **B** |
| **D-08** | **Schéma PostgreSQL et table d'historique** : `public` ou schéma dédié ? Où vit `__EFMigrationsHistory` ? | (a) `public` ; (b) schéma dédié (par exemple `mmv`) | à trancher **avec** D-09 et D-10. Un schéma de modèle doit passer par `ModelPortability`, sinon l'instantané SQLite dérive ; la table d'historique relève des options Npgsql, pas du modèle. **La baseline en dépend : à fixer avant génération** | **B** |
| **D-19** | **Où documenter la règle de double migration** (G6/S6) ? Aucun `CONTRIBUTING.md` n'existe | (a) nouveau `CONTRIBUTING.md` ; (b) section dans `docs/architecture/` | (a) : c'est le lieu attendu par un contributeur | **B** (mineure) |

> **Note de nommage.** Le modèle et le SQL brut des repositories (par exemple
> `NotificationRepository.ActiveLowStockInsertSql`) emploient des identifiants PascalCase entre guillemets.
> Adopter une convention `snake_case` côté PostgreSQL casserait ce SQL. Ce n'est pas une option réelle pour la
> V1 ; le point est consigné pour qu'il ne soit pas rouvert pendant P4-5E.

### Installation

| # | Question | Options | Portée |
|---|---|---|---|
| **D-09** | **Qui crée la base ?** | (a) un script ou une procédure de provisioning exécutée par un administrateur ; (b) l'application, via `Migrate()` : Npgsql crée la base si elle manque, ce qui exige `CREATEDB` et un accès à la base de maintenance (`EF_BEHAVIOR — À CONFIRMER`) | **L — P4-8**, avant P4-5G |
| **D-10** | **Qui possède les droits PostgreSQL ?** | (a) un seul rôle propriétaire, utilisé par l'application ; (b) deux rôles : **migrateur/propriétaire** (DDL) et **applicatif** (DML seulement) | **L — P4-6/P4-8** ; liée à O10 et D-11 |
| **D-11** | **Premier démarrage : qui applique la baseline ?** | (a) le premier poste, sous verrou ; (b) un poste désigné ; (c) un outil d'administration distinct de l'application | **L — P4-6** (O8) |
| **D-12** | **Premier administrateur sur une base neuve** | (a) secret bootstrap par variable (actuel) ; (b) assistant de premier démarrage — une UI minimale n'est autorisée par la roadmap (§3) **que** pour la configuration de connexion et les opérations de migration ; (c) création par l'outil de provisioning | **L — P4-8** + décision produit |

### Configuration

| # | Question | Options | Portée |
|---|---|---|---|
| **D-13** | **Où stocker la chaîne de connexion ?** | variable d'environnement (actuel) · fichier de configuration machine · magasin d'identifiants Windows / DPAPI · fichier `pgpass` · authentification Windows intégrée (sans mot de passe stocké) | **L — P4-8** |
| **D-14** | **Comment protéger les secrets ?** | chiffrement au repos · **TLS exigé** · rotation · droits NTFS · interdiction de journalisation (déjà respectée) | **L — P4-8** |
| **D-15** | **Politique d'échec au démarrage** : serveur injoignable, préparation ou seed en échec | arrêt explicite (cohérent avec l'échec de migration SQLite) · mode dégradé · nouvelle tentative bornée | **L — P4-5G/P4-8** ; traite R-E8 |

### Versioning

| # | Question | Options | Portée |
|---|---|---|---|
| **D-16** | **Comment connaître la version de la base ?** Et que faire d'un schéma plus récent que l'application ? | (a) `__EFMigrationsHistory` seul : migrations appliquées ⊆ migrations connues, sinon blocage ; (b) table de compatibilité explicite (version minimale d'application) ; (c) (a) + discipline de migrations rétrocompatibles (étendre puis contracter) | **L — P4-6** (O8, ADR-007 §5.7) |
| **D-17** | **Numérotation de version de MMV** : aucune aujourd'hui | version sémantique dans `Directory.Build.props`, affichée et journalisée | **L — P4-6/P4-8** |

### Déploiement

| # | Question | Options | Portée |
|---|---|---|---|
| **D-18** | **Comment migrer une base PostgreSQL existante ?** | (a) `Migrate()` au démarrage du premier poste, sous verrou ; (b) outil explicite (bundle EF, ou script idempotent généré) exécuté pendant une fenêtre de maintenance ; (c) (b) + **sauvegarde obligatoire** avant exécution | **L — P4-6 + P4-9** |
| — | Migration d'une base **SQLite** existante vers PostgreSQL | déjà affectée : outil ou procédure d'échec sûre, historique jamais importé, séquences d'identité à resynchroniser (O11) | **P4-7**, hors P4-5E |

### Préalables de gouvernance

| # | Point | Source |
|---|---|---|
| **G-1** | Ouvrir P4-5E en `NEXT` et confirmer que RR4 et les tests T-B1, T-B4 … T-B8 ne le précèdent pas | réconciliation P4-5, D-4 |
| **G-2** | Trancher l'inversion de dépendance P4-4 ↔ P4-5 (R-5B-6), toujours ouverte | rapport P4-5B |

---

## Recommended P4-5E Implementation Plan

### Périmètre

**Inclus** — ADR-005 **G1, G2, G3, G4, G6** · ADR-007 **S1, S2, S6** · décision D-07 (P-1), si l'option B est
retenue.

**Exclu, explicitement** — gestionnaire de préparation serveur et levée du garde-fou (P4-5G) · projet et tests
d'intégration, job CI avec serveur (P4-5F) · sérialisation et garde de version (P4-6) · provisioning, droits,
secrets (P4-8) · import SQLite (P4-7). **Le garde-fou de [App.axaml.cs:205](../../src/MMV.App/App.axaml.cs#L205)
reste en place.**

### Séquence proposée

| Étape | Contenu | Vérification de sortie |
|---|---|---|
| **P4-5E-B** — pré-revue | Consigner D-01, D-03 … D-08, D-19, G-1, G-2 dans une pré-revue soumise à l'architecte | décisions tracées, **aucun code** |
| **1. Projet** (G1a) | Créer le projet de migrations PostgreSQL et l'inscrire dans `MMV.sln`. Il référence `MMV.Infrastructure`, **jamais l'inverse**. Il est lui-même référencé par `MMV.App` (déploiement, C-3) et par le projet de tests qui en a besoin. **Aucun paquet nouveau** : versions déjà utilisées par le dépôt | `dotnet build MMV.sln` vert |
| **2. Sélection** (G2) | `DatabaseProviderResolver.Configure` : `MigrationsAssembly(...)` sur la **branche PostgreSQL uniquement** (plus la table d'historique si D-08 l'exige) ; branche SQLite **inchangée au caractère près** | tests du résolveur étendus : nom d'assembly côté PostgreSQL, rien côté SQLite |
| **3. Factory** (G3) | `OpticDbContextFactory` sélective par la variable design-time (D-05), **SQLite par défaut**. La branche PostgreSQL ne touche pas au chemin SQLite et n'exige aucun serveur | sans variable : comportement identique ; contrôle SQLite existant vert |
| **4. Baseline** (G1b) | Générer par l'outillage : `dotnet ef migrations add <Nom> --project <projet PG> --startup-project <D-04>` ; **aucune retouche**. Produire le script SQL de la baseline, sans connexion (`EF_BEHAVIOR — À CONFIRMER`), comme pièce du rapport | revue statique du DDL : 14 × `numeric(12,2)` · 2 index filtrés en forme PostgreSQL (`"IsCurrent"` et le filtre LowStock) · `timestamp with time zone` · `date` · `boolean` · identity · `InsertData` `SALE`/`ORDER` · FK `Restrict` · **aucun** `REAL` monétaire, `GLOB` ou annotation `Sqlite:` |
| **5. Intégrité SQLite** (G4) | — | `git diff` **vide** sur `src/MMV.Infrastructure/Migrations/` ; contrôle SQLite vert ; suite complète verte (**1915** à HEAD, à re-mesurer) |
| **6. Tests unitaires, sans serveur** | (a) contexte SQLite : `GetMigrations()` = les 14 identifiants actuels ; (b) contexte Npgsql (chaîne factice) : `GetMigrations()` = la baseline seule ; (c) **aucun changement de modèle en attente sur chacune des deux chaînes**, via l'API EF 8 équivalente à la commande CLI (`EF_BEHAVIOR — À CONFIRMER`) ; (d) Domain et Application ne référencent pas l'assembly PostgreSQL ; (e) non-régression du garde-fou de démarrage (R-E16), ou preuve équivalente | tests verts, comptés dans la suite unitaire |
| **7. CI** (S1, S2) | **Un seul** ajout à `ci.yml` : un second `has-pending-model-changes` visant la chaîne PostgreSQL, variables design-time **non secrètes** posées dans le step, `--no-build`. **Rien d'autre** ne change dans le fichier | CI verte **sur le SHA exact** ; preuve que le step échoue si le modèle change sans migration PostgreSQL (démonstration locale consignée) |
| **8. Règle de contribution** (G6, S6) | Documenter la règle de double migration et la question « quelle chaîne ? » (ADR-005 §7.2) à l'emplacement choisi (D-19) | document relu |
| **9. Rapport de clôture** | Rapport P4-5E, réconciliation des chiffres (C-5), statut roadmap | CI verte référencée par numéro d'exécution |

### Critères de clôture de P4-5E

1. Assembly PostgreSQL présente, baseline **générée**, instantané propre, **aucune** migration SQLite modifiée.
2. `has-pending-model-changes` **vert et bloquant sur les deux chaînes**, en CI, sans serveur.
3. Suite unitaire verte, sans test ignoré, compte mesuré et publié.
4. Garde-fou de démarrage **toujours actif** et prouvé.
5. Règle de double migration documentée.
6. Aucune variable ni chaîne de connexion de production dans le dépôt ou dans le workflow.

**Ce que P4-5E ne prouvera pas, et ne doit pas prétendre prouver** : que la baseline **s'applique** sur un vrai
serveur, qu'elle est reproductible, que le schéma créé est le bon (N1, N2 → P4-5F). La revue statique de
l'étape 4 est une lecture du DDL généré, **pas** une preuve d'exécution. Elle doit être présentée comme telle.

---

## Conclusion

**Ce qui existe.** Un modèle EF unique, déjà rendu provider-sélectif par P4-5C et P4-5D ; un point de sélection
du provider testé (P4-3) ; une chaîne SQLite de 14 migrations, intacte depuis P3-10 et solidement gardée ; un
cycle de vie SQLite complet et sûr ; un garde-fou qui empêche honnêtement tout démarrage serveur.

**Ce qui manque pour qu'une nouvelle installation démarre sur PostgreSQL.** La chaîne de migrations serveur et
sa sélection (**P4-5E**), la preuve serveur (**P4-5F**), un gestionnaire de préparation serveur et la levée du
garde-fou (**P4-5G**), la désignation de l'acteur qui migre et une garde de version (**P4-6**), le provisioning,
les droits, la protection des secrets et le premier administrateur (**P4-8**).

**Conformité aux ADR.** ADR-005 : §5.1 appliqué, G1 … G3 et G6 ouverts. ADR-007 : un seul contrôle de dérive
sur deux. ADR-008 : entièrement ouvert, et **bloqué par P4-5E**. Aucune contradiction entre ADR ; cinq tensions
entre ADR et code, dont deux à trancher avant le code (C-2, C-3) et une avant P4-5G (C-1).

**Décisions à prendre avant tout code de P4-5E** : D-01, D-03, D-04, D-05, D-06, D-07, D-08, D-19, ainsi que les
préalables G-1 et G-2. **D-08** (schéma et table d'historique) est la plus structurante : elle est gravée dans la
baseline, et la changer ensuite coûterait une migration.

```
P4-5E-A                      = AUDIT COMPLETE — ARCHITECT REVIEW REQUIRED
P4-5E                        = NOT STARTED — 8 DECISIONS + 2 GOVERNANCE PREREQUISITES BEFORE CODE
PG MIGRATION CHAIN           = ABSENT
SQLITE MIGRATION CHAIN       = 14 MIGRATIONS INTACT SINCE 8d4bf49 (P3-10)
DESIGN-TIME FACTORY          = SQLITE-FROZEN
CI DRIFT CONTROL             = 1 OF 2 (SQLITE ONLY)
PG STARTUP                   = BLOCKED BY DESIGN (App.axaml.cs:205) — LIFT IN P4-5G, AFTER P4-5F GREEN
NEW PG INSTALLATION READY    = NO — REQUIRES P4-5E, P4-5F, P4-5G, P4-6, P4-8
```
