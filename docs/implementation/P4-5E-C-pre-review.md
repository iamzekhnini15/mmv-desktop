# P4-5E-C — PostgreSQL Migration Chain — PRE-REVIEW

> **Statut : ARRÊT SUR CONDITION D'ARRÊT N°1 (D-04). PRÉ-REVUE, AUCUN COMMIT.**
> Étapes C1 à C3 implémentées et prouvées. C4 (baseline) est **bloquée** : EF ne découvre pas `OpticDbContext`
> quand le projet de migrations PostgreSQL, encore vide, est son propre projet de démarrage. Conformément à
> [P4-5E-B §Conditions d'arrêt](P4-5E-B-architecture-decisions.md), **aucun contournement n'a été appliqué
> dans le dépôt**. Les options ont été évaluées dans un spike **hors dépôt** (annexe B).
>
> Date : 22 septembre 2026 · Branche : `p4-multi-poste` · Destinataire : Lead Software Architect.
> Décisions de référence : [P4-5E-B](P4-5E-B-architecture-decisions.md). **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `EXÉCUTÉ` | commande ou test exécuté sur le dépôt, résultat observé |
| `SPIKE — HORS DÉPÔT` | exécuté sur une copie jetable des sources, hors du dépôt ; **rien n'en a été reporté dans le dépôt** |
| `STATIC_CODE_PROOF` | constaté par lecture du code |

---

## 1. Résumé exécutif

**Ce qui est fait et prouvé (C1, C2, C3, C5, une partie de C6)**

- Projet `MMV.Infrastructure.PostgreSQL.Migrations` créé, inscrit dans `MMV.sln`, référencé par `MMV.App` (R-3)
  et par `MMV.Domain.Tests`. Il ne contient que son `.csproj`.
- `DatabaseProviderResolver.Configure` : la branche PostgreSQL désigne l'assembly de migrations dédiée, via une
  constante unique. La branche SQLite est **inchangée au caractère près**.
- `OpticDbContextFactory` : sélective par `MMV_DESIGNTIME_DATABASE_PROVIDER`, SQLite par défaut, chaîne
  PostgreSQL factice. Elle ne lit jamais les variables runtime.
- **32 tests nouveaux**. Suite complète **1947 / 1947 verte**, aucun test ignoré (1915 à HEAD).
- **0 migration SQLite touchée**. Contrôle de dérive SQLite vert.

**Ce qui est bloqué (C4, et par conséquent C6-b/c PostgreSQL, C7, C8, C9)**

`dotnet ef migrations add InitialPostgreSqlBaseline` avec la commande exacte de D-04 échoue :
`No DbContext was found in assembly 'MMV.Infrastructure.PostgreSQL.Migrations'` (`EXÉCUTÉ`).

EF ne cherche le type de contexte que parmi les types **définis** dans l'assembly cible et dans l'assembly de
démarrage, ou via l'attribut `[DbContext]` porté par une migration de ces assemblys. Avant la baseline, le projet
PostgreSQL n'a ni l'un ni l'autre. L'hypothèse de D-04 (« la factory est cherchée dans l'assembly du contexte »)
est **vraie, mais seulement une fois le contexte découvert**. C'est la condition d'arrêt n°1. Détail en
annexe A.

**Ce que le spike établit (annexe B)**

- Le blocage ne concerne **que l'amorçage** : une fois la baseline présente, la commande D-04 fonctionne telle
  quelle et utilise **l'unique factory** de `MMV.Infrastructure`, sans connexion (S3a, S4).
- La baseline générée dans le spike **respecte la table de D-03** sur tous les points mesurés. Les conditions
  d'arrêt n°2 et n°3 ne se déclencheraient pas.
- Le contrôle de dérive PostgreSQL détecte un changement que le contrôle SQLite **ne voit pas** (S5). Le step CI
  prévu par C7 est donc utile.
- **Nouveau risque** : sans la variable design-time, `has-pending-model-changes` sur le projet PostgreSQL
  répond **vert** en contrôlant en réalité la chaîne SQLite (S3b). La garde croisée d'EF ne couvre que
  `migrations add`.

**Décision attendue** : Q1 (voie de déblocage), puis Q2 et Q3 (section 8).

**Incident pendant le spike** (annexe C) : un essai avec `MMV.App` comme projet de démarrage a exécuté le point
d'entrée de l'application. Celle-ci a créé une base SQLite neuve dans le profil local du poste de développement.
Aucune donnée préexistante n'a été touchée : la base n'existait pas.

---

## 2. SHA avant / après

| | SHA |
|---|---|
| Avant | `440ddcb352b12e0c6900cfd5d6df550e234856c5` (P4-5D-R CLOSED) |
| Après | `440ddcb352b12e0c6900cfd5d6df550e234856c5`. **Aucun commit** ; toutes les modifications sont dans l'arbre de travail |

---

## 3. Liste complète des fichiers modifiés

| Fichier | Nature | Étape | Lignes |
|---|---|---|---|
| [`src/MMV.Infrastructure.PostgreSQL.Migrations/MMV.Infrastructure.PostgreSQL.Migrations.csproj`](../../src/MMV.Infrastructure.PostgreSQL.Migrations/MMV.Infrastructure.PostgreSQL.Migrations.csproj) | **nouveau** | C1 | 40 |
| [`MMV.sln`](../../MMV.sln) | ajout du projet (dossier `src`) | C1 | +7 |
| [`src/MMV.App/MMV.App.csproj`](../../src/MMV.App/MMV.App.csproj) | une `ProjectReference` (R-3) | C1 | +4 |
| [`src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs`](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs) | constante, branche PostgreSQL, `ParseProvider` partagé | C2 | +30 / −6 |
| [`src/MMV.Infrastructure/Data/OpticDbContextFactory.cs`](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs) | sélection design-time | C3 | +68 / −7 |
| [`tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj`](../../tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj) | une `ProjectReference` | C6 | +2 |
| [`tests/MMV.Domain.Tests/Configuration/DatabaseProviderResolverTests.cs`](../../tests/MMV.Domain.Tests/Configuration/DatabaseProviderResolverTests.cs) | 4 tests ajoutés | C6 | +58 |
| [`tests/MMV.Domain.Tests/Configuration/OpticDbContextFactoryTests.cs`](../../tests/MMV.Domain.Tests/Configuration/OpticDbContextFactoryTests.cs) | **nouveau**, 16 cas | C6 | 178 |
| [`tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs`](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs) | **nouveau**, 10 tests | C6 | 210 |
| [`tests/MMV.App.Tests/Architecture/ServerStartupGuardTests.cs`](../../tests/MMV.App.Tests/Architecture/ServerStartupGuardTests.cs) | **nouveau**, 2 tests (R-E16) | C6 | 78 |
| `docs/implementation/P4-5E-C-pre-review.md` | ce rapport | — | — |

**Non modifiés, vérifié par `git status` (`EXÉCUTÉ`)** : `src/MMV.Infrastructure/Migrations/**`,
`SqliteDatabaseManager.cs`, `App.axaml.cs`, `OpticDbContext.cs`, `Data/Configurations/**`,
`Data/Portability/**`, `Data/Time/**`, tous les ADR, `.github/workflows/ci.yml`. Aucun paquet NuGet nouveau :
`Microsoft.EntityFrameworkCore.Design 8.0.27` est déjà utilisé par le dépôt.

**Détail des changements de code**

- `DatabaseProviderResolver` ([l. 47](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L47),
  [l. 131-135](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L131-L135)) :
  - constante `PostgreSqlMigrationsAssemblyName` ;
  - `UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(...))` ;
  - `ParseProvider` passe de `private` à `internal` et reçoit le nom de la variable à citer dans le message.
    Le message runtime reste identique au caractère près.
- `OpticDbContextFactory` ([l. 31-86](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L31-L86)) :
  - surcharge `CreateDbContext(IReadOnlyDictionary<string,string?>?)` pour l'injection en test ;
    `CreateDbContext(string[])` lui délègue avec l'environnement réel ;
  - la branche SQLite garde le même enchaînement : `ResolveDatabasePath`, puis `EnsureDirectoryExists`, puis
    `Configure(Sqlite)`.

---

## 4. Migrations SQLite impactées

**0.** `git diff --stat 440ddcb -- src/MMV.Infrastructure/Migrations/` renvoie une sortie vide (`EXÉCUTÉ`).
`dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` affiche
« No changes have been made to the model since the last migration. », code 0 (`EXÉCUTÉ`).

---

## 5. Tests exécutés et résultats

**Commandes**

| Commande | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | **0 avertissement, 0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **1947 réussis, 0 échec, 0 ignoré**. Détail : App 260, Application 628, Domain 1059 |
| Mesure de référence à HEAD, avant toute modification | 1915 réussis (258 / 628 / 1029). Confirme le chiffre du rapport de réconciliation (C-5) |
| `has-pending-model-changes`, chaîne SQLite | **vert**, code 0 |
| `has-pending-model-changes`, chaîne PostgreSQL (commande C7) | **rouge**, code 1, `No DbContext was found`. Conséquence directe du blocage |
| `migrations add InitialPostgreSqlBaseline` (commande D-04), puis avec `--context` nommé et avec `--context` qualifié par l'assembly | **échec ×3**, aucun fichier écrit |

**Tests exigés par la demande, et tests qui les couvrent**

| Exigence | Tests (tous verts) |
|---|---|
| 1. Migrations SQLite inchangées | `MigrationChainsTests.SqliteChain_IsExactlyThe14HistoricalMigrations_InOrder` · `SqliteChain_IsReadFromTheContextAssembly` · `SqliteChain_HasNoPendingModelChanges` (API EF 8 équivalente à la CLI) · `DatabaseProviderResolverTests.Configure_Sqlite_DeclaresNoExplicitMigrationsAssembly` |
| 2. Assembly de migrations PostgreSQL chargée | `MigrationChainsTests.PostgreSqlChain_IsReadFromItsDedicatedAssembly` (EF charge l'assembly par son nom) · `PostgreSqlMigrationsAssemblyName_IsTheRealAssemblyName` (D-01.6, comparaison ordinale) · `DatabaseProviderResolverTests.Configure_PostgreSql_DeclaresTheDedicatedMigrationsAssembly` · **« baseline unique » : bloqué** |
| 3. `MMV_DESIGNTIME_DATABASE_PROVIDER` absente → SQLite | `OpticDbContextFactoryTests.WithoutDesignTimeVariable_TargetsSqlite_WithTheHistoricalConnectionString` · `WithBlankOrSqliteValue_TargetsSqlite` (×4) |
| 3. `postgresql` → PostgreSQL | `WithPostgreSqlValueOrAlias_TargetsNpgsql_AndThePostgreSqlMigrationsAssembly` (×4) · `PostgreSqlBranch_ResolvesNoSqliteFile_AndCreatesNoDirectory` (D-05.5) · `DesignTimePostgreSqlConnectionString_IsFake_AndCarriesNoCredential` (D-05.4) |
| 3. valeur inconnue → exception | `WithUnknownValue_Throws_NamingTheDesignTimeVariable_WithoutEchoingTheValue` (×3, dont une valeur qui ressemble à une chaîne de connexion) |
| 4. Étanchéité runtime | `DatabaseProviderResolverTests.Resolve_IgnoresTheDesignTimeProviderVariable` · `Resolve_WithInvalidDesignTimeVariable_DoesNotThrow` · `OpticDbContextFactoryTests.RuntimeProviderVariables_NeverSelectPostgreSql` · `RuntimeConnectionString_NeverReachesThePostgreSqlDesignTimeContext` |
| 5. Coexistence des modèles | `EfModelPortabilityTests.BothProviderModels_CoexistInTheSameProcess_WithoutContaminatingEachOther` (**inchangé**, vert) · `MigrationChainsTests.BothChains_CoexistInTheSameProcess_EachWithItsOwnAssembly` |

**Autres gardes ajoutées**

- `MigrationChainsTests.PostgreSqlChain_NeverSeesASqliteMigration` : aucun des 14 identifiants SQLite n'apparaît
  dans la chaîne PostgreSQL (R-E1).
- `PostgreSqlMigrationsAssembly_ContainsOnlyMigrationsAndSnapshots` (D-01.2). **Il est trivialement vrai tant que
  l'assembly est vide.**
- `PostgreSqlMigrationsAssembly_DependsOnNeitherTheUiNorTheApplicationLayer` (D-04).
- `DomainAndInfrastructure_DoNotReferenceThePostgreSqlMigrationsAssembly` (O14, sens unique). Application est
  déjà couverte par `ApplicationArchitectureTests` (préfixe `MMV.Infrastructure`).
- `ServerStartupGuardTests` (×2, C6-e / R-E16) : **preuve statique** que le garde-fou existe, lit le
  résolveur runtime et précède le premier `try` ainsi que tout le cycle de vie SQLite. Voir Q5.

**Non écrits, car ils dépendent de la baseline** : C6-b (« Npgsql : la baseline seule ») et C6-c côté PostgreSQL
(aucune dérive sur la chaîne PostgreSQL). Le helper `PendingModelChanges` de `MigrationChainsTests` est prêt à
les porter.

---

## 6. Validation de chaque décision

| Décision | Statut | Preuve |
|---|---|---|
| **D-01** — Deux assemblys | **PARTIEL** | Voir le détail ci-dessous. **D-01.4** (un instantané par assembly) et la partie « baseline seule » de D-01.5 attendent C4 |
| **D-03** — Baseline unique | **BLOQUÉ** | Non générée dans le dépôt. La baseline du spike respecte la table D-03 (annexe B.2), mais **elle n'est pas livrable** |
| **D-04** — Projet de démarrage autonome | **BLOQUÉ à l'amorçage** | Condition d'arrêt n°1 (annexe A). Après amorçage, la commande D-04 fonctionne sans modification et utilise `OpticDbContextFactory` (S3a, S4, `SPIKE — HORS DÉPÔT`). Références conformes : `ProjectReference` vers `MMV.Infrastructure` ; paquet Design 8.0.27 en `PrivateAssets=all` ; `net8.0` ; aucune référence vers App, Application ou Avalonia (test) |
| **D-05** — Séparation runtime / design-time | **CONFORME** | Les cinq preuves de D-05 sont couvertes par des tests (§5). D-05.4 : chaîne factice sans identifiant (test ; précision en Q4). D-05.5 : aucun dossier créé sur la branche PostgreSQL (test). D-05.6 : sans objet tant que C7 n'est pas ajouté. D-05.7 : aucune commande connectée n'a été lancée |
| **D-06** — SQLite inchangé | **CONFORME** | Voir le détail ci-dessous |
| **D-07** — Cache de modèle, option A | **CONFORME** | Aucun `IModelCacheKeyFactory` (grep sur `src/`). Test de coexistence **inchangé** et vert, complété côté assemblys de migrations |
| **D-08** — Schéma `public` | **CONFORME dans le code, preuve finale en attente de C4** | Aucun `HasDefaultSchema`, `ToTable(…, schema)`, `MigrationsHistoryTable` ni convention `snake_case` dans `src/` (grep). DDL du spike : 0 `CREATE SCHEMA`, 0 qualification `public.`, table d'historique au nom par défaut |
| **D-19** — Double migration | **NON COMMENCÉ** | C8 suit C4 ; les commandes à documenter dépendent de Q1 et Q3. Emplacement proposé : `CONTRIBUTING.md` à la racine (Q6) |

**Détail D-01**

- **D-01.1** : projet `src/MMV.Infrastructure.PostgreSQL.Migrations`, nom d'assembly identique.
- **D-01.2** : `.csproj` seul ; garde par test.
- **D-01.3** : aucune migration déplacée ; `git diff` vide.
- **D-01.5** : `MigrationsAssembly(constante)` sur la seule branche PostgreSQL ; SQLite garde l'assembly implicite
  (tests).
- **D-01.6** : constante unique ; test de comparaison ordinale.
- Sens des dépendances : `MMV.App` vers le projet PostgreSQL (R-3) ; `MMV.Infrastructure` et Domain ne le
  référencent pas (test).

**Détail D-06**

- **D-06.1 / D-06.2** : aucun `MigrationsAssembly` ni `MigrationsHistoryTable` sur la branche SQLite (test).
- **D-06.3** : le diff de `DatabaseProviderResolver` ne touche aucune ligne de la branche SQLite de `Configure`
  (`git diff -U0`, `EXÉCUTÉ`).
- **D-06.4** : `git diff` vide sur `src/MMV.Infrastructure/Migrations/`.
- **D-06.5** : `SqliteDatabaseManager` non modifié.
- **D-06.6** : step CI SQLite non modifié.
- Contrôle de dérive SQLite vert ; suites historiques vertes sans modification.

**Comportements EF marqués « À CONFIRMER » dans P4-5E-B : résultat**

| Hypothèse de P4-5E-B | Résultat | Preuve |
|---|---|---|
| EF trouve la factory de `MMV.Infrastructure` quand le projet de démarrage est le projet de migrations | **Infirmée avant la baseline** (le contexte n'est pas découvert). **Confirmée après** | `EXÉCUTÉ` + S3a |
| Génération, `has-pending-model-changes` et `migrations script` fonctionnent sans connexion | **Confirmé** (hôte `.invalid`, jamais résolu) | S2, S3a, S6 |
| EF refuse une génération dont la cible ne correspond pas à l'assembly configurée | **Confirmé pour `migrations add`** (S3d, S3e). **Non applicable à `has-pending-model-changes`** (S3b), d'où Q2 | spike |
| `MigrationsAssembly` est chargée par son nom à l'exécution | **Confirmé** (DLL à côté de l'hôte nécessaire ; R-3 justifié) | test + S1 |
| Une API EF 8 équivaut à `has-pending-model-changes` | **Confirmé côté SQLite** (test vert, même algorithme que la CLI : instantané finalisé puis `IMigrationsModelDiffer`). Côté PostgreSQL : en attente de C4 | test |

---

## 7. Limitations restantes

1. **Aucun serveur PostgreSQL réel n'a été contacté**, par construction. Que la baseline s'applique, soit
   reproductible et crée un schéma physiquement conforme relève de **P4-5F** (N1, N2, S3, S4, G5). La revue du DDL
   (annexe B.2) est une **lecture** d'un script de spike, pas une preuve d'exécution.
2. **Pas de baseline dans le dépôt.** L'assembly PostgreSQL est vide. `GetMigrations()` sur Npgsql renvoie une
   liste vide. Le contrôle de dérive PostgreSQL est rouge.
3. **Step CI (C7) non ajouté** : il serait rouge tant que C4 n'est pas fait. `ci.yml` est intact.
4. **Garde-fou de démarrage prouvé statiquement seulement** (lecture du source). Une preuve comportementale
   exigerait d'extraire le garde-fou de `App.axaml.cs`, ce qui est interdit en P4-5E-C.
5. **État intermédiaire inerte** : `MMV.App` référence une assembly de migrations vide. Le garde-fou bloque
   toujours PostgreSQL au démarrage, et le runtime SQLite est inchangé (suite verte).
6. **`ARCHITECTURE.md` §« Migrations EF Core »** documente `--startup-project src/MMV.App`. Cette commande
   **exécute l'application** (annexe C) et ne permet pas de générer la chaîne PostgreSQL (S1b). Hors périmètre
   de P4-5E-C, non modifié (Q6).
7. **C8 (D-19), C9 (rapport de clôture, roadmap) non commencés.**

---

## 8. Questions ouvertes pour l'architecte

**Q1 (bloquante) — Voie de déblocage de la condition d'arrêt n°1.** Toutes les voies ont été évaluées dans le
spike.

| Option | Description | Écart aux décisions | Résultat du spike |
|---|---|---|---|
| **A** (recommandée) | Classe permanente de 5 lignes dans le projet PostgreSQL : `IDesignTimeDbContextFactory<OpticDbContext>` qui **délègue** à `new OpticDbContextFactory().CreateDbContext(args)` | exception explicite à **D-01.2** ; D-04 reformulé : un seul chemin **logique** de génération, deux points d'entrée. D-05 intact | génère sans connexion, instantané propre (S2) |
| **A'** | Même classe, mais qui **force** PostgreSQL au lieu de lire la variable | ré-ouvre **D-05** : la variable design-time devient inutile pour la chaîne PostgreSQL. Ferme structurellement le faux vert de Q2 | même mécanique que A |
| **B** | Amorçage **temporaire** : classe A créée hors commit, baseline générée, classe supprimée | aucun écart dans l'état final | après suppression, la commande D-04 fonctionne seule (S3a, S4). Mais l'amorçage n'est pas reproductible depuis le dépôt, et toute régénération autorisée avant fusion (D-03.5) exige de le refaire |
| C | `--startup-project src/MMV.App` | contraire à D-04 | **échec** : même erreur, et le point d'entrée de l'application est exécuté (S1b, annexe C) |
| D | `--startup-project src/MMV.Infrastructure` | — | **impossible** : EF exige que le projet de démarrage référence la cible, ce qui serait une référence circulaire (S1) |
| `--context <nom>` | option CLI | — | **échec**, nom simple comme nom qualifié par l'assembly |

**Recommandation : A.** C'est le plus petit écart qui rend **toutes** les commandes PostgreSQL reproductibles
depuis un état propre du dépôt, y compris une régénération complète avant fusion. D-05 reste intact. Le test
`PostgreSqlMigrationsAssembly_ContainsOnlyMigrationsAndSnapshots` recevrait une exception nommée pour cette seule
classe.

**Q2 — Faux vert du contrôle de dérive PostgreSQL (S3b).** Si la variable manque au step CI (clé mal
orthographiée, step copié sans son `env:`), le contrôle « PostgreSQL » vérifie la chaîne SQLite et passe. Deux
réponses possibles :
- **A'** (Q1) ;
- **ou** une assertion dans le step C7 : `dotnet ef migrations list --no-connect` doit lister
  `_InitialPostgreSqlBaseline` et **aucun** `_InitialCreate`. Cela ajoute une commande au step unique autorisé
  par D-05.6.

**Q3 — Espace de noms et dossier de la chaîne PostgreSQL (D-03.6).** Constats du spike :
- avec `--namespace`, **l'instantané** est écrit dans un dossier dérivé de l'espace de noms
  (`MMV/Infrastructure/PostgreSQL/Migrations/`), et non dans `--output-dir` ;
- la commande D-04 **sans option** place les trois fichiers dans `Migrations/`, sous l'espace de noms
  `MMV.Infrastructure.PostgreSQL.Migrations.Migrations`. Cela reproduit la convention SQLite (`<RootNamespace>.Migrations`),
  et les migrations suivantes suivent sans option (S4).

**Proposition : commande D-04 sans option.** Alternative : `<RootNamespace>MMV.Infrastructure.PostgreSQL</RootNamespace>`
dans le `.csproj`, qui donne l'espace de noms `MMV.Infrastructure.PostgreSQL.Migrations`.

**Q4 — Précision de D-05.4.** La chaîne design-time retenue est
`Host=mmv-design-time.invalid;Database=mmv_design_time`. Elle ne porte ni utilisateur ni mot de passe. Le
domaine `.invalid` est réservé (RFC 2606) : une commande qui se connecterait ne peut jamais atteindre un serveur
local de développeur. Le document citait une chaîne en `127.0.0.1` comme modèle. À confirmer.

**Q5 — Preuve du garde-fou (C6-e, R-E16).** Le test statique `ServerStartupGuardTests` est-il accepté comme
« preuve équivalente » ? Le lot qui lèvera le garde-fou (P4-5G) devra le mettre à jour délibérément.

**Q6 — Emplacement de D-19 et documentation existante.** `CONTRIBUTING.md` à la racine est proposé : c'est le
point d'entrée que GitHub présente à tout contributeur, et aucun n'existe. Faut-il aussi autoriser la correction
de `ARCHITECTURE.md` §« Migrations EF Core » ? Elle est hors périmètre, mais ses commandes lancent l'application
et visent un projet de démarrage désormais inadapté.

**Q7 — Sort de l'arbre de travail.** Les étapes C1 à C3 ne dépendent pas de l'option retenue en Q1 (A, A' ou B).
**Proposition** : les conserver et reprendre à C4 après décision, plutôt que de les annuler.

```
P4-5E-C                      = STOPPED — STOP CONDITION 1 (D-04) — PRE-REVIEW
C1 PROJECT                   = DONE
C2 SELECTION                 = DONE
C3 FACTORY                   = DONE
C4 BASELINE                  = BLOCKED — NO DbContext DISCOVERABLE IN EMPTY MIGRATIONS ASSEMBLY
C5 SQLITE INTEGRITY          = VERIFIED (git diff EMPTY, DRIFT GREEN)
C6 UNIT TESTS                = PARTIAL — 32 NEW, GREEN; BASELINE-DEPENDENT TESTS PENDING
C7 CI                        = NOT ADDED (WOULD BE RED UNTIL C4)
C8 CONTRIBUTING (D-19)       = NOT STARTED
C9 CLOSURE                   = NOT STARTED
TESTS                        = 1947 / 1947 GREEN, 0 SKIPPED (HEAD: 1915)
SQLITE MIGRATION CHAIN       = 14 MIGRATIONS INTACT
PG MIGRATION CHAIN           = EMPTY ASSEMBLY — NO BASELINE
PG STARTUP                   = BLOCKED BY DESIGN (App.axaml.cs) — UNCHANGED
COMMIT                       = NONE
```

---

## Annexe A — Condition d'arrêt n°1 : preuve

Commande exécutée sur le dépôt (`EXÉCUTÉ`) :

```bash
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql \
  dotnet ef migrations add InitialPostgreSqlBaseline \
    --project         src/MMV.Infrastructure.PostgreSQL.Migrations \
    --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations \
    --no-build --verbose
```

Journal `--verbose`, extraits :

```
Finding DbContext classes...
Finding IDesignTimeDbContextFactory implementations...
Finding application service provider in assembly 'MMV.Infrastructure.PostgreSQL.Migrations'...
No application service provider was found.
Finding DbContext classes in the project...
No DbContext was found in assembly 'MMV.Infrastructure.PostgreSQL.Migrations'.
```

**Lecture.**
1. EF 8 cherche d'abord une factory dans l'assembly **de démarrage** : il n'y en a aucune.
2. Il cherche ensuite un fournisseur de services applicatif : aucun, car c'est une bibliothèque.
3. Il cherche enfin les contextes parmi les types définis dans les assemblys cible et de démarrage, ou désignés
   par `[DbContext]` sur leurs migrations : aucun.

La recherche d'une factory **dans l'assembly du contexte**, sur laquelle repose D-04, n'intervient qu'une fois le
type de contexte connu. Après la baseline, l'attribut `[DbContext(typeof(OpticDbContext))]` de la migration
fournit ce type. C'est pourquoi le journal du spike S3a affiche alors
`Found DbContext 'OpticDbContext'. Using DbContext factory 'OpticDbContextFactory'.`

`--context MMV.Infrastructure.Data.OpticDbContext` et `--context "MMV.Infrastructure.Data.OpticDbContext, MMV.Infrastructure"`
filtrent parmi les contextes **déjà découverts** et échouent de la même façon (`No DbContext named … was found`).

---

## Annexe B — Spike hors dépôt

Copie jetable de `src/`, `global.json`, `Directory.Build.props` et `.config/`, placée hors du dépôt.
**Aucun fichier du spike n'a été reporté dans le dépôt**, et la baseline qu'il contient n'est **pas** livrable.

### B.1 Essais

| # | Essai | Résultat |
|---|---|---|
| S1 | cible PostgreSQL, démarrage `MMV.Infrastructure` | `Could not load assembly 'MMV.Infrastructure.PostgreSQL.Migrations'. Ensure it is referenced by the startup project` |
| S1b | cible PostgreSQL, démarrage `MMV.App` | `No DbContext was found…`, **et exécution du point d'entrée de l'application** (annexe C) |
| S2 | option A (factory déléguée temporaire), commande D-04 | `Using DbContext factory 'SpikeBootstrapFactory'` ; baseline écrite, **sans connexion** |
| S2' | variante avec `--output-dir Migrations --namespace MMV.Infrastructure.PostgreSQL.Migrations` | migration dans `Migrations/`, **instantané dans `MMV/Infrastructure/PostgreSQL/Migrations/`** (Q3) |
| S3a | factory temporaire **supprimée** ; `has-pending-model-changes`, commande C7 | `Found DbContext 'OpticDbContext'. Using DbContext factory 'OpticDbContextFactory'. No changes…`, code 0 |
| S3b | même commande **sans** la variable design-time | « No changes… », code 0 : **faux vert**, la chaîne SQLite est contrôlée (Q2) |
| S3c | `migrations list --no-connect` | la baseline seule |
| S3d | `migrations add` sur la cible PostgreSQL **sans** la variable | refus : l'assembly de migrations ne correspond pas à la cible ; rien n'est écrit |
| S3e | `migrations add` sur la cible SQLite **avec** la variable | refus : `Could not load … PostgreSQL.Migrations` ; rien n'est écrit dans la chaîne SQLite |
| S4 | migration suivante, commande D-04 **sans option** | écrite dans `Migrations/`, même espace de noms que la baseline ; `Up` vide (modèle inchangé) |
| S5 | `Customer.Phone` : `HasMaxLength(20)` → `25` | PostgreSQL **rouge** (code 1) ; SQLite **vert** (`TEXT` des deux côtés) |
| S5d | `Customer.FirstName` : `IsRequired()` → `IsRequired(false)` | PostgreSQL **rouge** et SQLite **rouge** |
| S6 | `migrations script` | script produit **sans connexion** |

S5 montre que le step C7 attrape une dérive que le step SQLite actuel laisse passer. C'est la démonstration
locale exigée par C7, faite ici sur la copie jetable.

### B.2 Revue statique du DDL de la baseline du spike, selon la table D-03

| Doit contenir | Mesure | | Ne doit pas contenir | Mesure |
|---|---|---|---|---|
| `numeric(12,2)` | **14** | | `REAL` | **0** |
| filtre `WHERE "IsCurrent"` sur `idx_workshop_sheets_current_unique` | **présent** | | `"IsCurrent" = 1` | **0** |
| filtre LowStock portable | **identique au filtre SQLite** | | `GLOB`, `CHECK` | **0**, **0** |
| `timestamp with time zone` | 19 | | annotation ou mention SQLite | **0** |
| `date` (`Customers.BirthDate`, `Prescriptions.IssueDate`) | 2 | | `CREATE SCHEMA`, qualification `public.` | **0**, **0** |
| `boolean` · identity | 7 · 16 | | `InsertData` utilisateur | **0** |
| `InsertData DocumentSequences` | `ORDER` / `CMD`, `SALE` / `VTE`, `UpdatedAt` = `2026-01-01T00:00:00Z` | | | |
| clés étrangères | `RESTRICT` 7 · `CASCADE` 10 · `SET NULL` 3, telles que déclarées par le modèle | | | |

Autres mesures : 22 tables, 30 index, `double precision` ×22 pour les colonnes optiques, historique
`__EFMigrationsHistory` au nom par défaut et sans schéma.

**Conclusion.** Les conditions d'arrêt n°2 (baseline contraire à D-03) et n°3 (connexion exigée) ne se
déclencheraient pas. La condition n°5 (modification du modèle nécessaire) non plus : aucun changement de modèle
n'a été requis.

---

## Annexe C — Incident pendant le spike

**Fait.** Pour chercher un `IHost`, l'essai S1b (`--startup-project src/MMV.App`) a amené `dotnet ef` à exécuter le
`Main` de `MMV.App` depuis la copie du spike. L'application Avalonia a démarré et fait une installation neuve
SQLite dans `%LOCALAPPDATA%\ManageMyVision\` :

```
PREPARE start path='…\ManageMyVision\mmv.db' fileExists=False
DETECT state=Empty
FRESH install: applied 14 migration(s).
```

**Portée.**
- `mmv.db` et `migration-journal.log` ont été **créés** ; ils n'existaient pas auparavant.
- Aucune donnée préexistante n'a été affectée.
- Aucune connexion PostgreSQL n'a eu lieu : le runtime a ignoré `MMV_DESIGNTIME_DATABASE_PROVIDER` et choisi
  SQLite. C'est cohérent avec D-05.2, mais anecdotique et sans valeur de preuve.
- Les deux fichiers ont été laissés en place. Leur suppression est laissée à l'utilisateur du poste.

**Enseignements.**
1. Un projet de démarrage `MMV.App` pour `dotnet ef` **exécute l'application** et son cycle de vie SQLite sur la
   base réelle du développeur. Cela conforte la justification de D-04.
2. Les commandes de `ARCHITECTURE.md` exposent tout contributeur à ce risque (Q6).
