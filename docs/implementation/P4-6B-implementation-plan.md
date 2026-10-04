# P4-6B — Plan d'implémentation : cycle de vie de la base en multi-poste

> **Statut : PLAN. AUCUN CODE ÉCRIT, AUCUN FICHIER DE PRODUCTION MODIFIÉ, AUCUNE MIGRATION.**
> Ce document est le plan d'exécution du sous-lot **P4-6B** de la [roadmap P4](../architecture/P4-multi-poste-roadmap.md).
> Il applique [ADR-PROD-DB-009](../architecture/ADR-PROD-DB-009.md) (**ACCEPTED**, dix points de décision
> arrêtés) et ne rouvre aucune de ses décisions.
>
> Date de la révision : 4 octobre 2026 (première version : 30 septembre 2026, sur `p4-net10` / `68a3b81` —
> **historique**). Branche : `p4-5f-integration-tests`. HEAD de référence : **`692b6da`**
> (`feat(P4-5F): add PostgreSQL integration tests`), CI **`37202557042` SUCCESS** sur le SHA exact,
> **1953** tests unitaires + **74** tests d'intégration PostgreSQL 17.10 (comptés séparément).
> **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `CODE` | constaté par lecture du code du dépôt à `692b6da` |
| `DOC` | énoncé d'un document du dépôt (ADR, roadmap, rapport) |
| `MESURÉ` | constaté par inspection des assemblys réellement restaurés dans le cache NuGet |
| `CI` | prouvé par la CI `37202557042` sur `692b6da` (job Linux `PostgreSQL integration (P4-5F)`) |
| `À MESURER` | comportement serveur ou EF non vérifié ; mesurable **dès maintenant** dans le projet d'intégration livré par P4-5F |
| `UNKNOWN` | absent du code et de la documentation ; rien n'est inventé |

---

## 0. La porte P4-5F est levée

**Le volet décisionnel et le volet technique de P4-6B sont tous deux satisfaits.**

| Fait | Preuve |
|---|---|
| ADR-PROD-DB-009 est **ACCEPTED**, dix points de décision arrêtés | `DOC` [ADR-PROD-DB-009 §1, §5.2](../architecture/ADR-PROD-DB-009.md) |
| **P4-5F = `COMPLETE — CLOSED`** : `692b6da`, CI `37202557042` SUCCESS ; PostgreSQL **17.10**, **74 / 74** (0 ignoré), **1953 / 1953** unitaires, dérive EF verte sur les deux chaînes, migrations inchangées | `DOC` [roadmap P4 §P4-5](../architecture/P4-multi-poste-roadmap.md), [rapport P4-5F](P4-5F-postgresql-integration-tests-report.md) ; `CI` |
| Le projet `tests/MMV.Infrastructure.PostgreSQL.IntegrationTests` existe, est inscrit dans `MMV.sln` (9 projets), et tourne dans un **job Linux distinct** avec service `postgres:17.10` et garde anti-faux-vert machine (TRX : 0 résultat ou 1 non-`Passed` ⇒ échec) | `CODE` [.github/workflows/ci.yml](../../.github/workflows/ci.yml) (`postgresql-integration`) |
| Le job Windows exécute `dotnet test MMV.sln` en **excluant** seulement `MMV.Infrastructure.PostgreSQL.IntegrationTests` : tout nouveau projet de tests unitaires inscrit dans la solution y est exécuté **sans modifier la CI** | `CODE` ci.yml, step `Test` |
| P4-6B a besoin d'un serveur pour prouver **H2** (sérialisation), **H5** (rôle applicatif sans DDL), **H14** (fenêtre N-1) — critères S-1 à S-4 : ce serveur **existe désormais en CI** | `DOC` [ADR-009 §8.3](../architecture/ADR-PROD-DB-009.md) ; `CI` |

**Conséquence sur le plan.** Les tranches **C** (l'outil) et **E** (preuves serveur en CI) **ne sont plus
`BLOCKED BY P4-5F`**. Le découpage `P4-6B-1` / `P4-6B-2` de la version du 30/09 devient **sans objet** :
P4-6B s'exécute d'un seul tenant, tranche par tranche (§1). Reste **interdit** : déclarer P4-6B `COMPLETE` sur
des preuves locales — la discipline P4 exige la CI verte sur le SHA exact.

**Réserve O12, inchangée et hors P4-6B** : la preuve sur PostgreSQL **natif Windows** (`MMV-SRV`) reste séparée ;
elle conditionne O12 / P4-4, pas P4-6B.

---

## 1. Ordre exact des fichiers à modifier

Ordre de dépendance réelle : chaque tranche compile et passe la suite complète avant que la suivante ne
commence. Un commit par tranche, CI verte sur le SHA exact.

**Ordre d'implémentation (révision du 04/10/2026 — P4-5F clos, plus de scission P4-6B-1 / P4-6B-2)** :

1. **A** — version applicative unique (A1 … A6) ;
2. **B** — garde de compatibilité en lecture seule (B1 … B6) ;
3. **C, partie sans serveur** — projet `MMV.DatabaseManager` inscrit dans la solution **avant** tout code
   (C1, C2), puis C3 … C12, puis le projet `MMV.DatabaseManager.Tests` et ses tests (C13, C14), puis H11
   (C15) ;
4. **C, partie serveur** — référence ajoutée au projet d'intégration (C16), preuves I-1 … I-14 (C17), mesures
   M-1 … M-9 ;
5. **E** — constat que la CI existante exécute les deux familles **sans modification** de `ci.yml` (E1) ;
6. **D** — CONTRIBUTING.md (D1), ARCHITECTURE.md (D2), roadmap (D3), addendum ADR-009 **sur autorisation**
   (D4), rapport de lot (D5).

D1 peut avancer avec A si l'architecte le souhaite : il ne dépend d'aucun code, mais U-E1 le rend vérifiable.

### Tranche A — Version applicative unique (DP-7, H7) — **sans serveur**

| # | Fichier | Nature | Existe ? |
|---|---|---|---|
| A1 | [Directory.Build.props](../../Directory.Build.props) | ajout d'un `PropertyGroup` de version : source unique du SemVer | **existe** — aucune propriété de version aujourd'hui (`CODE`, `grep` : 0 occurrence de `<Version>`, `<VersionPrefix>`, `<AssemblyVersion>` dans tout le dépôt) |
| A2 | `src/MMV.Infrastructure/Configuration/ApplicationVersion.cs` | **nouveau** : lecture à l'exécution de la version, source unique côté runtime | nouveau |
| A3 | `tests/MMV.Domain.Tests/Configuration/ApplicationVersionTests.cs` | **nouveau** | nouveau ; le dossier existe |
| A4 | [src/MMV.App/ViewModels/PageViewModels.cs](../../src/MMV.App/ViewModels/PageViewModels.cs) (`SettingsViewModel`, l. 31) | ajout d'une propriété `Version` | **existe** |
| A5 | [src/MMV.App/Views/SettingsView.axaml](../../src/MMV.App/Views/SettingsView.axaml) l. 43 | `Text="1.0.0"` → `{Binding Version}` (bindings compilés, `x:DataType="vm:SettingsViewModel"` déjà posé l. 5) | **existe** |
| A6 | `tests/MMV.App.Tests/Architecture/ApplicationVersionDisplayTests.cs` | **nouveau** : interdit le retour d'un littéral de version dans l'axaml | nouveau ; le dossier existe |

**Pourquoi A est en premier.** DP-3 (fenêtre N-1), DP-8 (journal) et DI-3.4 sont **inexprimables** sans
version applicative lisible à l'exécution : la garde compare une version à un minimum, le journal enregistre
une version. Rien d'autre ne peut être écrit avant.

**Trois pièges concrets de A1/A2.**

1. `Directory.Build.props` est à la **racine** : il s'applique aussi à `spikes/**`, dont
   [MMV.P4.Worker](../../spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj), délibérément resté en
   `net8.0` (`CODE`). Poser la version à la racine la propage aux spikes. **Effet observable aujourd'hui :
   nul** — le SDK applique déjà `1.0.0` par défaut, donc tant que la valeur vaut `1.0.0` rien ne change ; le
   premier incrément, lui, versionnera les spikes. C'est acceptable et **préférable** à un second fichier :
   DP-7.1 exige « une seule source ». À défaut, un `src/Directory.Build.props` important le parent isole les
   spikes — au prix d'un fichier de plus et d'une source qui n'est plus unique. **Recommandation : racine**,
   avec un commentaire disant pourquoi.
2. `AssemblyInformationalVersion` reçoit, sur .NET 8+, un suffixe `+<sha>` par défaut. `ApplicationVersion`
   doit **tronquer au premier `+`**, ou le build doit poser
   `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`. Un test
   (A3) doit verrouiller le format retourné : `Major.Minor.Patch`, sans suffixe.
3. `ApplicationVersion` lit la version de **l'assembly qui porte le type** (`MMV.Infrastructure`), **jamais**
   celle de l'assembly d'entrée : l'entrée diffère entre `MMV.App` et `MMV.DatabaseManager`, et **DI-5.2**
   exige que les deux exécutables annoncent la même version. Un test doit interdire
   `Assembly.GetEntryAssembly()` dans ce chemin.

### Tranche B — Garde de compatibilité, en lecture seule (DP-3, DP-4 ; H1, H3, H4, H14) — **sans serveur pour les tests unitaires**

| # | Fichier | Nature |
|---|---|---|
| B1 | `src/MMV.Infrastructure/Data/ServerSchemaCompatibility.cs` | **nouveau** : états `E1`, `E2`, `E3a`, `E3b`, `E3c`, `E4`, `E5`, `E7` et cas `C-1` … `C-5` de [DP-3 §5.2.3.2](../architecture/ADR-PROD-DB-009.md) |
| B2 | `src/MMV.Infrastructure/Data/ServerCompatibilityMetadataNames.cs` | **nouveau** : noms du schéma et des objets MMV hors modèle EF, en **un seul point** (même discipline que `DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName`) |
| B3 | `src/MMV.Infrastructure/Data/ServerCompatibilityMetadataReader.cs` | **nouveau** : lecture **seule** de la métadonnée, SQL brut via `DbConnection`/`DbCommand` (aucune entité EF) |
| B4 | `src/MMV.Infrastructure/Data/ServerSchemaCompatibilityGuard.cs` | **nouveau** : verdict à partir de *Appliquées* (`GetAppliedMigrations`), *Connues* (`GetMigrations`), du minimum supporté et de la version applicative |
| B5 | `tests/MMV.Domain.Tests/Data/ServerSchemaCompatibilityGuardTests.cs` | **nouveau** : les cinq cas et les huit états, sur ensembles **injectés** — aucun serveur |
| B6 | `tests/MMV.Domain.Tests/Data/ServerCompatibilityMetadataReaderTests.cs` | **nouveau** : absence de la table, table vide, valeurs illisibles |

**Localisation de B5/B6 — vérifiée, aucune dépendance nouvelle.** `MMV.Domain.Tests` référence **déjà**
`MMV.Infrastructure` et `MMV.Infrastructure.PostgreSQL.Migrations` (`CODE`
[MMV.Domain.Tests.csproj](../../tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj) l. 26, 28) et héberge déjà les
tests unitaires d'Infrastructure (`Data/SqliteDatabaseManagerTests.cs`, `Data/Migrations/MigrationChainsTests.cs`,
`Configuration/OpticDbContextFactoryTests.cs`). B1 … B4 vivent dans `MMV.Infrastructure` : y placer B5/B6
**n'ajoute aucune référence** et suit le précédent. Le nom du projet est historique ; il n'est pas renommé ici.
**Ce qui n'y va pas** : tout test de composant de `MMV.DatabaseManager` (tranche C, voir C13).

**Règle de conception non négociable de la tranche B :** elle ne contient **aucune écriture**. `MMV.App`
référence `MMV.Infrastructure` ; tout code d'écriture placé ici deviendrait **atteignable depuis
l'application**, ce que **H11** interdit de prouver autrement que par test. Verrou, journal serveur, écriture
de la métadonnée : **tranche C uniquement**.

**B n'appelle pas la garde.** Aucun site d'appel dans `MMV.App`. Le garde-fou de démarrage
([App.axaml.cs:200-214](../../src/MMV.App/App.axaml.cs#L200-L214)) **reste en place** : sa levée est
**P4-6C**, et [ServerStartupGuardTests](../../tests/MMV.App.Tests/Architecture/ServerStartupGuardTests.cs) doit
rester vert **sans modification**. Si ce test doit changer, P4-6B a dépassé son périmètre.

### Tranche C — `MMV.DatabaseManager` (DP-1, DP-2, DP-8, DP-9, DP-10 ; H2, H8, H11, H12, H13, H16, H17)

| # | Fichier | Nature |
|---|---|---|
| C1 | `src/MMV.DatabaseManager/MMV.DatabaseManager.csproj` | **nouveau** : `net10.0`, `OutputType=Exe` |
| C2 | [MMV.sln](../../MMV.sln) | inscription du projet (dossier `src`, nouveau GUID) |
| C3 | `src/MMV.DatabaseManager/Program.cs` | **nouveau** : analyse des arguments, codes de sortie, aucune logique |
| C4 | `src/MMV.DatabaseManager/CommandLine/MigrationToolOptions.cs` | **nouveau** : `--operator`, `--backup-ref`, `--app-role`, `--wait`, verbes `status` / `migrate` / `adopt-compatibility` |
| C5 | `src/MMV.DatabaseManager/Locking/IMigrationLock.cs` | **nouveau** : port |
| C6 | `src/MMV.DatabaseManager/Locking/PostgreSqlAdvisoryMigrationLock.cs` | **nouveau** : verrou consultatif de **session** (§3) |
| C7 | `src/MMV.DatabaseManager/Journal/ServerMigrationJournal.cs` | **nouveau** : table dédiée, écritures **hors** transaction de migration, horodatage `now()` serveur |
| C8 | `src/MMV.DatabaseManager/Journal/CompatibilityMetadataWriter.cs` | **nouveau** : minimum supporté **calculé** (H17) |
| C9 | `src/MMV.DatabaseManager/Backup/IBackupVerification.cs` + `RefusingBackupVerification.cs` | **nouveau** : port + **seule** implémentation de production, qui **refuse toujours** (H12) ; le vérificateur réel est **P4-9** — seam détaillé au §6.2 |
| C10 | `src/MMV.DatabaseManager/MigrationRunner.cs` | **nouveau** : orchestration de la séquence d'outil du §2.3 (couvre les étapes 1 à 6 de [§4.6.2](../architecture/ADR-PROD-DB-009.md)) |
| C11 | `src/MMV.DatabaseManager/ServerSchemaVerification.cs` | **nouveau** : vérification post-migration, périmètre restreint (§6), **y compris les droits du rôle applicatif (Q-23)** |
| C12 | `src/MMV.DatabaseManager/Permissions/ApplicationRoleGrants.cs` | **nouveau** : `GRANT` idempotents au rôle `--app-role` (**Q-23**, §4.5) |
| C13 | `tests/MMV.DatabaseManager.Tests/MMV.DatabaseManager.Tests.csproj` + inscription dans [MMV.sln](../../MMV.sln) | **nouveau projet de tests unitaires** — voir ci-dessous |
| C14 | `tests/MMV.DatabaseManager.Tests/**Tests.cs` | **nouveaux** : arguments (dont `--app-role`), refus de sauvegarde, ordre de la séquence, calcul du minimum, format du journal, clé de verrou — sur doubles, **sans serveur** |
| C15 | `tests/MMV.App.Tests/Architecture/NoServerDdlInApplicationTests.cs` | **nouveau** : **H11** par réflexion sur les assemblys |
| C16 | [MMV.Infrastructure.PostgreSQL.IntegrationTests.csproj](../../tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/MMV.Infrastructure.PostgreSQL.IntegrationTests.csproj) | **+1 `ProjectReference`** vers `src/MMV.DatabaseManager` — seule modification d'un projet de test existant |
| C17 | `tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/Lifecycle/*.cs` | **nouveaux** : preuves serveur I-1 … I-14 (§7.3) |

**C2 avant C3.** `dotnet build MMV.sln` doit voir le projet dès le premier commit de la tranche, sinon la CI
compile un dépôt où le projet n'existe pas et le vert ne veut rien dire.

**Pourquoi un projet de tests dédié `MMV.DatabaseManager.Tests` (C13).** Constat sur les références réelles
(`CODE`) :

| Projet de tests | Référence aujourd'hui |
|---|---|
| `MMV.Domain.Tests` | `MMV.Domain`, `MMV.Infrastructure`, `MMV.Infrastructure.PostgreSQL.Migrations` |
| `MMV.Application.Tests` | `MMV.Application`, `MMV.Domain`, `MMV.Infrastructure` |
| `MMV.App.Tests` | `MMV.App` |
| `MMV.Infrastructure.PostgreSQL.IntegrationTests` | `MMV.Domain`, `MMV.Application`, `MMV.Infrastructure`, `MMV.Infrastructure.PostgreSQL.Migrations` |

Aucun ne référence `MMV.DatabaseManager`, et l'y ajouter serait artificiel : `MMV.Domain.Tests` recevrait une
référence vers un **exécutable** sans lien avec son périmètre, et `MMV.App.Tests` **ne doit pas** la recevoir
(U-D1 / H11 prouvent justement que rien côté application ne voit l'outil). Le projet dédié référence **un seul**
projet, `src/MMV.DatabaseManager` (Infrastructure et migrations arrivent par transitivité), et uniquement les
paquets de test **déjà présents** dans la solution, **aux mêmes versions** (xunit, `Microsoft.NET.Test.Sdk`,
FluentAssertions **6.x**) : **aucun paquet nouveau**. Inscrit dans `MMV.sln`, il est exécuté par le step `Test`
du job Windows **sans modification de la CI** (son nom ne correspond pas au filtre d'exclusion). Les preuves
**avec serveur** restent dans le projet d'intégration (C16, C17), jamais dans ce projet.

### Tranche D — Documentation et discipline (DP-3 §5.2.3.4, H15) — **sans serveur**

| # | Fichier | Nature |
|---|---|---|
| D1 | [CONTRIBUTING.md](../../CONTRIBUTING.md) | nouvelle section **« Étendre → migrer → contracter »**, insérée **après** « Règle de double migration » (l. 153-201) et **avant** « Commandes EF de référence » (l. 203) ; deux entrées ajoutées au tableau **Interdits** (l. 304) ; deux contrôles ajoutés à la **Procédure de revue** (l. 320) |
| D2 | [ARCHITECTURE.md](../../ARCHITECTURE.md) | `MMV.DatabaseManager` dans la carte des projets ; la garde de compatibilité dans Infrastructure |
| D3 | [docs/architecture/P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) | état de P4-6B ; bloc de verdicts (§8) ; dépendance **P4-6C → P4-9** (décision Q-24) |
| D4 | `docs/architecture/ADR-PROD-DB-009.md` | **addendum** fermant Q-6, Q-9, Q-11, Q-12, Q-13, Q-15, Q-21, Q-22 et enregistrant la décision **Q-23** (§8) — **sur autorisation explicite** : l'ADR est ACCEPTED, l'addendum n'en rouvre aucune décision |
| D5 | `docs/implementation/P4-6B-*-report.md` | rapport de lot |

### Tranche E — Preuves CI (job existant de P4-5F — plus bloquée)

| # | Fichier | Nature |
|---|---|---|
| E1 | [.github/workflows/ci.yml](../../.github/workflows/ci.yml) | **modification attendue : aucune.** Le job Windows compile et exécute `MMV.DatabaseManager.Tests` via `MMV.sln` ; le job `postgresql-integration` compile le projet d'intégration, qui référencera `MMV.DatabaseManager` (C16), et exécute I-1 … I-14 sous sa garde anti-faux-vert existante. Les rôles de test (applicatif sans DDL, migrateur) sont créés **par les tests** dans la base jetable, avec l'utilisateur éphémère du service. **Aucun** contrôle de dérive touché. Toute modification de `ci.yml` qui s'avérerait nécessaire devient une **exception à justifier en revue** |

**Ce que E ne fait pas :** aucun troisième contrôle de dérive. P4-6B **n'ajoute aucune migration** — la
métadonnée et le journal vivent **hors du modèle EF** (DP-3.5, DP-8.1), donc `has-pending-model-changes` reste
vert sur les deux chaînes **sans nouvelle migration**, et
[MigrationChainsTests](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs) **n'est pas
modifié**. Si l'un des deux bouge, une table a été mise dans le modèle : **H16 est violée**.

---

## 2. Architecture de `MMV.DatabaseManager`

### 2.1 Forme du projet

```
src/MMV.DatabaseManager/
├── MMV.DatabaseManager.csproj      net10.0 · OutputType=Exe · LangVersion 12.0 · Nullable enable
├── Program.cs                      point d'entrée : arguments → runner → code de sortie
├── CommandLine/
│   └── MigrationToolOptions.cs     analyse et validation des arguments ; aucun défaut permissif
├── Locking/
│   ├── IMigrationLock.cs
│   └── PostgreSqlAdvisoryMigrationLock.cs
├── Journal/
│   ├── ServerMigrationJournal.cs   table dédiée, hors transaction, horloge serveur
│   └── CompatibilityMetadataWriter.cs
├── Backup/
│   ├── IBackupVerification.cs
│   └── RefusingBackupVerification.cs
├── Permissions/
│   └── ApplicationRoleGrants.cs    GRANT idempotents au rôle --app-role (Q-23)
├── ServerSchemaVerification.cs
└── MigrationRunner.cs              orchestration
```

**Références de projet** — exactement deux, et pour un motif chacune :

| Référence | Motif |
|---|---|
| `src/MMV.Infrastructure` | `OpticDbContext`, `DatabaseProviderResolver`, `ApplicationVersion`, `IMigrationJournal`, la garde en lecture |
| `src/MMV.Infrastructure.PostgreSQL.Migrations` | EF charge l'assembly de migrations **par son nom** à l'exécution ; la référence place la DLL à côté de l'exécutable — **même motif, mot pour mot, que [MMV.App.csproj](../../src/MMV.App/MMV.App.csproj)** (`CODE`) |

**Références interdites** : `MMV.App` (UI), `MMV.Application` (aucun cas d'usage métier n'intervient —
DP-9.2 : « aucune autorisation métier MMV »), Avalonia, et toute référence **entrante** : rien ne référence
`MMV.DatabaseManager`. C'est ce qui rend **H11** prouvable par réflexion.

**Paquet NuGet nouveau : aucun.** `Npgsql` arrive par transitivité de `MMV.Infrastructure`, et le verrou
s'écrit sur `DbConnection` / `DbCommand` — le **précédent est P4-1 Lot D**, où `NotificationRepository` a été
corrigé en créant ses paramètres par `Database.GetDbConnection().CreateCommand().CreateParameter()` plutôt
qu'en instanciant un type de provider. La contrainte de terrain d'[ADR-008 §2.4](../architecture/adr-prod-db-008-postgresql-integration-testing.md)
(restauration NuGet difficile) est ainsi respectée.

### 2.2 Surface de commande

| Verbe | Écrit ? | Verrou ? | Sauvegarde exigée ? | Rôle |
|---|---|---|---|---|
| `status` | **non** | non | non | diagnostic : état, Appliquées/Connues, minimum supporté, maintenance. Utilisable par le support sans risque. **`--app-role` non requis** |
| `migrate` | oui | **oui** | **oui** (H12) | applique la baseline **et** les migrations ultérieures (DP-1.1). **`--app-role` obligatoire** (Q-23) |
| `adopt-compatibility` | oui | **oui** | **oui** | **réponse à Q-22** : initialise la métadonnée sur une base migrée **avant** l'introduction du journal (§4.4). **`--app-role` obligatoire** (Q-23) |

**Aucun verbe `rollback`, aucun `drop`, aucun `down`.** Les `Down()` ne sont pas outillés et celui de la
baseline supprime tout (U-7) ; la seule voie de retour arrière est la restauration, **P4-9** (§6).

**Entrées obligatoires de `migrate`**

| Entrée | Source | Obligation |
|---|---|---|
| chaîne de connexion du **rôle migrateur** | variable d'environnement, **jamais** un argument de ligne de commande | DP-5, DP-9.1 ; un argument apparaîtrait dans l'historique du shell et dans la liste des processus |
| `--operator <référence>` | argument | **H13** : « une référence d'opérateur est exigée au lancement et journalisée ». Absente ⇒ l'outil refuse de démarrer |
| `--backup-ref <identifiant>` | argument | **H12** : aucune migration sans sauvegarde vérifiée. Absente ⇒ refus |
| `--app-role <nom>` | argument | **Q-23** : rôle applicatif qui reçoit la lecture de la métadonnée. Le **nom** appartient à P4-8 ; l'outil n'en code **aucun**. Absent ⇒ code 10. Rôle inexistant sur le serveur (`pg_roles`) ⇒ code 10, **avant toute écriture** |

**La variable d'environnement de connexion doit être distincte de celle des postes.** Les postes utilisent
`MMV_DATABASE_CONNECTION_STRING` (`CODE`
[DatabaseProviderResolver](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs)), qui porte
le rôle **applicatif**. L'outil doit lire une variable propre — `MMV_MIGRATOR_CONNECTION_STRING` — sans quoi un
outil lancé sur un poste configuré prendrait **le rôle applicatif**, qui n'a aucun droit DDL (DP-5) : l'échec
serait tardif et confus. C'est aussi ce qui matérialise DP-5 : « l'identifiant migrateur n'est stocké sur aucun
poste ».

**Codes de sortie** (un échec doit être diagnosticable sans lire un journal)

| Code | Sens |
|---|---|
| `0` | succès ; ou `status` exécuté |
| `10` | arguments invalides : référence d'opérateur absente, `--app-role` absent ou rôle inexistant |
| `11` | sauvegarde absente ou non vérifiée (**H12**) |
| `12` | verrou non obtenu : une autre exécution est en cours (**H2**) |
| `13` | échec de migration ; base à la dernière migration réussie |
| `14` | vérification post-migration en échec — **y compris droits du rôle applicatif non assurés (Q-23)** |
| `15` | métadonnée absente ou incohérente ; `adopt-compatibility` requis (**Q-22**) |
| `20` | serveur injoignable ou authentification refusée |

### 2.3 Enchaînement de `migrate` — réconciliation avec les huit étapes de §4.6.2

**Correction de la version du 30/09.** Elle annonçait « les huit étapes de §4.6.2 » puis en énumérait **dix** :
les deux listes ne décrivent pas la même chose. Réconciliation, **sans rien ajouter à l'ADR** :

- [ADR-009 §4.6.2](../architecture/ADR-PROD-DB-009.md) décrit **huit étapes de procédure d'exploitation**.
  Les étapes **7** (mettre à jour les binaires des postes) et **8** (redémarrer les postes) sont **hors de
  l'outil**. L'étape **1** a un volet humain (annoncer, recenser ou fermer les postes : CX-5) et un volet
  technique (l'indicateur `maintenance_started_at` : CX-3). L'étape **2** (`pg_dump`) appartient à
  l'opérateur et à **P4-9** ; l'outil n'en fait que la **vérification** (H12).
- L'outil exécute donc la partie technique des étapes **1 à 6**, décomposée ci-dessous en **une étape locale
  (0)**, **douze étapes ordonnées sous contrôle de l'outil (1-12)** et **une étape locale de clôture (13)**.
  Parmi elles, **trois sont du nettoyage** : nettoyage d'un marqueur périmé (5), remise à `NULL` de la
  maintenance (11), libération du verrou (12). Les étapes 3, 4 et 7 sont des **préalables techniques**
  (cohérence, DDL hors modèle, journal) qu'exigent DP-3.5.2, DP-8.1 et DP-8.3.

**Ordre imposé, normatif** (décision d'architecte du 04/10/2026) :

```
 #   Étape d'outil                                        ADR §4.6.2   Échec ⇒
 0   options validées (--operator, --backup-ref, --app-role) + trace locale OPEN
                                                           —            code 10 ; aucun contact serveur
 1   IBackupVerification.Verify(backupRef)                 2 (vérif.)   code 11 ; AUCUNE écriture en base
 2   IMigrationLock.Acquire()  — verrou consultatif        3            code 12 ; AUCUNE écriture en base
 3   contrôles de cohérence, lecture seule :               —            code 15 (Q-22) / code 10 (rôle) ;
       Q-22 selon la table du §4.4 ;                                     aucune écriture
       existence de --app-role dans pg_roles
 4   DDL de métadonnée idempotent (CREATE … IF NOT EXISTS)  —            code 13 ; → étape 12
       + GRANT idempotents au rôle --app-role (Q-23)
       — aucune ligne insérée ici
 5   NETTOYAGE : marqueur maintenance_started_at périmé    1 (techn.)   → étape 12
       d'une exécution antérieure → NULL, tracé
 6   maintenance_started_at ← now() serveur                1 (techn.)   → étape 12
       base sans ligne : INSERT de la ligne unique en ÉTAT
       D'INITIALISATION (schema_version = NULL,
       minimum_supported_version = NULL, maintenance ≠ NULL) — §4.3.1
 7   ServerMigrationJournal.Open(...) — hors transaction   (DP-8)       → étapes 11, 12
 8   context.Database.Migrate()                            4            code 13 ; → étapes 10, 11, 12
 9   ServerSchemaVerification.Verify()                     5            code 14 ; → étapes 10, 11, 12
       aucune en attente ; Appliquées ⊆ Connues ;
       droits du rôle --app-role assurés (Q-23)
10a  CompatibilityMetadataWriter.Advance()                 5            code 14 ; → étapes 10b, 11, 12
       version de schéma + minimum CALCULÉ (H17)            (SEULEMENT si 9 a réussi)
10b  ServerMigrationJournal.Close(...) — résultat          5            → étapes 11, 12
11   NETTOYAGE : maintenance_started_at ← NULL             6            finally, si 6 a été atteinte,
       SAUF si schema_version IS NULL (initialisation                    et SEULEMENT pour une ligne
       inachevée : le marqueur reste posé, §4.3.1)                       initialisée
12   NETTOYAGE : pg_advisory_unlock + fermeture            6            finally, si 2 a réussi
13   trace locale de clôture                               —            finally, toujours
```

Écrit avec les intitulés de la décision d'architecte : **vérification de sauvegarde → verrou consultatif →
nettoyage éventuel d'un marqueur périmé → `maintenance_started_at` → migration → vérification → métadonnée →
clôture du journal → maintenance `NULL` → libération du verrou**. Les étapes 3, 4 et 7 s'intercalent sans en
changer l'ordre relatif.

**Six propriétés à ne pas perdre en écrivant ce code.**

1. **L'étape 1 précède l'étape 2.** Refuser la sauvegarde **avant** de prendre le verrou évite qu'un refus
   n'immobilise la base.
2. **Rien n'écrit en base avant l'acquisition réussie du verrou.** En particulier, il est **INTERDIT** de
   nettoyer `maintenance_started_at` avant l'étape 2 : un outil qui nettoierait sans verrou effacerait
   l'indicateur d'une exécution **vivante** et rouvrirait les postes en pleine migration. Le nettoyage (5) ne
   s'appuie que sur la possession du verrou : si l'outil le détient, toute exécution antérieure est morte.
3. **L'étape 10a suit l'étape 9, et ne s'exécute qu'en cas de succès.** La métadonnée ne s'avance **qu'après**
   vérification : c'est ce qui garantit le point 3 de [§4.6.3](../architecture/ADR-PROD-DB-009.md) — après un
   échec, la fenêtre d'un poste N-1 est celle de l'**état réel** de la base, pas celle qu'on visait.
4. **Les étapes 7 et 10b sont hors transaction.** Sinon l'annulation d'une migration en échec effacerait la
   trace de son propre échec — le manque exact que Q-14 devait combler (DP-8.3).
5. **L'ordre de sortie est l'inverse de l'ordre d'entrée** : journal clos (10b) → maintenance `NULL` (11) →
   verrou libéré (12). Libérer le verrou avant de remettre la maintenance à `NULL` laisserait une fenêtre où une
   autre exécution prendrait le verrou et verrait un marqueur « vivant » qu'elle traiterait comme périmé.
6. **Une exécution n'est `success` que si 8, 9 et 10a ont réussi** — l'étape 9 incluant la preuve que le
   `GRANT` au rôle applicatif est assuré (Q-23). Toute autre issue est `failure`, avec sa cause.

**`adopt-compatibility`** suit la même séquence, avec l'étape 8 **sans `Migrate()`** et l'étape 10a posant
`minimum = version courante` (§4.4). `status` n'exécute que 0 (sans `--app-role`) et des lectures : ni
verrou, ni écriture.

**La trace locale réutilise l'existant.** [`IMigrationJournal` /
`MigrationJournal`](../../src/MMV.Infrastructure/Data/MigrationJournal.cs) (`CODE`) écrit en mémoire et en
**append** dans un fichier, et **n'échoue jamais la préparation** en cas d'erreur d'écriture. C'est exactement
le comportement voulu par DP-8.2 pour ce qui « n'a jamais atteint la base ». **Deux réserves** :

- son horodatage est `DateTime.UtcNow`, donc l'**horloge du poste**. Acceptable pour la trace locale, et
  **interdit** pour la table en base, qui exige `now()` serveur (DP-8,
  [ADR-004](../architecture/adr-prod-db-004-datetime-strategy.md), T9/NTP) ;
- son chemin : l'outil n'est pas l'application. Le fichier doit vivre dans un dossier de **données** de
  l'outil — **jamais** sous le dossier d'installation si celui-ci est remplacé à chaque mise à jour (**DI-9**,
  Velopack remplace `current`). À arbitrer avec P8 ; par défaut, dossier de données de l'utilisateur, à l'image
  de [`SqliteDatabasePathResolver`](../../src/MMV.Infrastructure/Data/SqliteDatabasePathResolver.cs).

---

## 3. Stratégie de verrou : verrou natif EF Core 10 ou verrou consultatif PostgreSQL

### 3.1 Ce que le code réellement restauré fait — mesuré, pas supposé

| Constat | Preuve |
|---|---|
| Le dépôt est sur **EF Core 10.0.12** et **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3** | `CODE` [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) |
| `Microsoft.EntityFrameworkCore.Relational` 10.0.12 expose `IHistoryRepository.AcquireDatabaseLock` / `…Async`, l'interface `IMigrationsDatabaseLock`, la propriété `LockReleaseBehavior` et l'évènement `RelationalEventId.AcquiringMigrationLock` | `MESURÉ` — symboles présents dans l'assembly du cache NuGet |
| Le provider **surcharge** ce verrou dans `NpgsqlHistoryRepository`, et le mécanisme émis est **`LOCK TABLE <historique> IN ACCESS EXCLUSIVE MODE`** | `MESURÉ` — littéraux `'LOCK TABLE '` et `' IN ACCESS EXCLUSIVE MODE'` présents dans `Npgsql.EntityFrameworkCore.PostgreSQL.dll` 10.0.3 |
| **Aucune** chaîne `pg_advisory*` n'existe dans l'assembly du provider | `MESURÉ` |
| Le dépôt ne contient **aucun** verrou : 0 occurrence de `advisory`, `AcquireDatabaseLock`, `MigrationLock`, `pg_locks`, `lock_timeout` dans `src/` et `tests/` | `CODE` (`grep`) |

**Ce que cette mesure change, et c'est le point important du §3.** Le verrou natif d'EF Core ≥ 9 sur Npgsql
**n'est pas LK-1**. C'est **LK-4** — « `LOCK TABLE "__EFMigrationsHistory"` », que
[ADR-009 §4.2.1](../architecture/ADR-PROD-DB-009.md) cite « pour mémoire », avec les limites de LK-3 : *la
table n'existe pas sur une base vide, et il faut une transaction englobante*. **Q-6 était posée comme un choix
entre « rester sur EF Core 8 avec LK-1 » et « bénéficier du verrou natif LK-5 ».** La mesure montre que les
deux branches ne sont pas équivalentes : LK-5, chez Npgsql, retombe dans la famille que DP-2.2 a écartée en
substance.

#### 3.1.1 EF Core 10 — trois niveaux à ne pas confondre

| Niveau | Contenu | Preuve |
|---|---|---|
| **(a) Comportement par défaut d'EF Core 10 / Npgsql 10** | `Migrate()` prend le verrou natif via `IHistoryRepository.AcquireDatabaseLock` ; chez Npgsql, `LOCK TABLE <historique> IN ACCESS EXCLUSIVE MODE`, aucun `pg_advisory*` | `MESURÉ` (§3.1) |
| | une transaction par migration ; un `Migrate()` lancé **dans une transaction utilisateur** est signalé par un avertissement que EF ≥ 9 traite par défaut comme une erreur ; un modèle avec changements en attente fait échouer `Migrate()` par défaut depuis EF 9 | notes de rupture EF Core 9 (**hors dépôt**) — `À MESURER` (M-5, M-9) |
| **(b) Comportement effectivement configuré dans MMV** | la branche PostgreSQL se limite à `UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgreSqlMigrationsAssemblyName))`. **Aucun** `ConfigureWarnings`, **aucune** surcharge de `IHistoryRepository` / `LockReleaseBehavior`, **aucune** table d'historique renommée, **aucune** stratégie de réessai, **aucun** `CommandTimeout`. MMV hérite donc **intégralement** des défauts (a) | `CODE` [DatabaseProviderResolver.cs:133-135](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs#L133-L135) ; `grep` de ces symboles dans `src/` : 0 occurrence |
| **(c) Preuve d'intégration à conserver** | `MigrateAsync()` applique la baseline sur une base **vide** `mmv_it_<guid>`, avec le verrou natif par défaut, sur PostgreSQL **17.10** / EF **10.0.12** / Npgsql **10.0.3** | `CI` `37202557042` ; `CODE` [PostgreSqlDatabase.cs:45](../../tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/Infrastructure/PostgreSqlDatabase.cs#L45), [SchemaTests.cs:37](../../tests/MMV.Infrastructure.PostgreSQL.IntegrationTests/Schema/SchemaTests.cs#L37) |

**Ce que (c) prouve et ne prouve pas.** Il prouve qu'une exécution **unique** de `Migrate()` réussit sur base
vide avec les défauts EF 10 ; il **ne prouve pas** la sérialisation de deux exécutions, ni la portée du verrou
natif au travers de plusieurs migrations (M-1 … M-3). Ces tests restent **verts et inchangés** pendant P4-6B :
ils sont la référence de non-régression du chemin EF par défaut.

**Conséquences pour l'outil.** (1) P4-6B **ne modifie pas** la configuration EF de `OpticDbContext` : le verrou
consultatif (§3.3) et `lock_timeout` (CX-1) se posent en SQL de session, pas par option EF. (2) Le verrou
consultatif vit sur une **connexion distincte**, et `Migrate()` n'est **jamais** enveloppé dans une transaction
de l'outil — sinon le défaut (a) le refuserait.

### 3.2 Comparaison, adossée au code du dépôt

| Critère | **LK-5** — verrou natif EF Core 10 (`LOCK TABLE … ACCESS EXCLUSIVE`) | **LK-1** — verrou consultatif de session (`pg_advisory_lock`) |
|---|---|---|
| **Base vide** (installation, étape 3 de §4.6.1) | la table d'historique **n'existe pas** avant la baseline. EF la crée puis la verrouille (`CREATE TABLE IF NOT EXISTS`, capture de `42P07` / `42710` — `MESURÉ`, littéraux présents) : la fenêtre *créer → verrouiller* n'est pas elle-même protégée par le verrou | **fonctionne** : la clé n'est liée à aucun objet. C'est l'argument décisif, et c'est celui du dépôt : la chaîne PostgreSQL ne contient **qu'une** migration, `20260922001219_InitialPostgreSqlBaseline` (`CODE`) — la première exécution réelle de l'outil se fera **sur une base vide** |
| **Portée** | liée à une **transaction** : `LOCK TABLE` n'existe qu'en transaction. EF ouvre **une transaction par migration** (`DOC` P4-5E-A U-6, `À MESURER`). Si la libération suit la transaction, le verrou **ne couvre pas** une montée à plusieurs migrations — c'est **l'objection que DP-2 oppose explicitement à LK-2** | **session** : tenue par une connexion ouverte pendant tout le `Migrate()`, donc **couvre toute la montée**, quel que soit le nombre de migrations |
| **Ce que le verrou protège** | l'intérieur de `Migrate()` | **toute l'opération** : sauvegarde vérifiée, DDL de métadonnée, `Migrate()`, vérification, avancement de la métadonnée, journal. C'est ce qu'exige DP-3.5.2 — la métadonnée est écrite « dans la **même opération verrouillée** que la migration » |
| **Maîtrise du comportement** | interne au provider ; change avec une montée de version d'EF ou de Npgsql, sans que le dépôt en décide | constante de clé et appels SQL **dans le dépôt**, lisibles en revue |
| **Libération après plantage** | fin de transaction, donc fin de session au pire | fin de session ; délai de détection d'une session morte `À MESURER` (keepalive TCP) |
| **Attente ou échec immédiat** | comportement d'attente du provider, non paramétré par le dépôt | **au choix** : `pg_advisory_lock` (attente) ou `pg_try_advisory_lock` (échec immédiat, code 12) |
| **Observabilité** | `RelationalEventId.AcquiringMigrationLock` (`MESURÉ`) : utile en **test** pour prouver que le verrou a bien été pris | `pg_locks` ; droits de lecture `À MESURER` |
| **Conformité à DP-2** | DP-2.1 « natif PostgreSQL » : **oui**. DP-2.2 « aucune table de verrou applicative » : **oui au sens littéral** (aucune table créée), mais le verrou **porte sur une table**, et §4.2.1 range ce mécanisme avec LK-3 / LK-4 | DP-2.1 : **oui**. DP-2.2 : **oui**. DP-2.4 : la forme reste à arrêter, ce que fait ce plan |

### 3.3 Décision recommandée

> **Retenir LK-1 — verrou consultatif de session — comme verrou de l'outil, et laisser le verrou natif d'EF
> opérer dessous, sans le désactiver ni s'y fier.**

Forme exacte, à inscrire dans `PostgreSqlAdvisoryMigrationLock` :

1. **Clé** : une constante `long` unique, propre à MMV, déclarée **en un seul point** avec un commentaire
   disant qu'elle ne change **jamais** — même discipline que
   `DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName` (`CODE`). Un test compare la constante à sa
   valeur attendue, pour qu'une modification soit un acte de revue.
2. **Acquisition** : `pg_try_advisory_lock(clé)` dans une boucle d'attente **bornée** (`--wait`, défaut court).
   Échec ⇒ code 12 et message nommant l'autre exécution. **Jamais d'attente infinie** : un outil lancé deux
   fois doit le **dire**, pas se figer.
3. **Connexion** — *amendé par décision d'architecte du 04/10/2026 (ADR-009 §11.1)* : **une seule session**
   PostgreSQL porte le verrou **et** `Migrate()`. Elle est ouverte par l'acquisition du verrou et tenue ouverte
   pour toute la séquence ; si elle meurt, la migration meurt avec elle, et EF ne peut pas la rouvrir
   (intercepteur de connexion). *(Version initiale, remplacée : un contexte dédié au verrou, distinct de celui
   qui migre.)* Le SQL passe par `DbConnection.CreateCommand()` — aucune référence directe à Npgsql, précédent
   P4-1 Lot D.
4. **Libération** : `pg_advisory_unlock(clé)` dans un `finally`, **puis** fermeture. La libération implicite en
   fin de session est un filet, **pas** la stratégie : une connexion **rendue au pool** sans réinitialisation
   garderait le verrou (§4.2.1). C'est la mesure **M-3** ci-dessous.
5. **`lock_timeout`** posé sur la session **qui migre** (pas celle du verrou), pour qu'un `ALTER TABLE` bloqué
   derrière une session de poste échoue proprement au lieu d'attendre indéfiniment — option **CX-1** de
   §4.2.4.

*Pourquoi ne pas désactiver le verrou natif d'EF.* Il ne gêne pas : `LOCK TABLE` sur l'historique, à
l'intérieur d'une opération déjà protégée par le verrou consultatif, est un doublon inerte. Le désactiver
exigerait de remplacer `IHistoryRepository`, c'est-à-dire d'entrer dans les services internes d'EF pour un gain
nul. **DR-10** (coût V1) et **DR-5** (prouvabilité) commandent de le laisser tranquille.

### 3.4 Mesures exigées avant de déclarer le verrou prouvé — exécutables dès maintenant (projet d'intégration P4-5F)

| # | Mesure | Pourquoi elle décide de quelque chose |
|---|---|---|
| **M-1** | valeur de `LockReleaseBehavior` chez `NpgsqlHistoryRepository`, et durée de vie réelle du verrou natif au travers d'un `Migrate()` à **plusieurs** migrations | confirme ou infirme §3.2, ligne « Portée » ; si le verrou natif tenait toute la montée, LK-5 redeviendrait discutable — mais pas sur base vide |
| **M-2** | ordre exact sur base vide : création de l'historique, puis acquisition | mesure la fenêtre non protégée de LK-5 |
| **M-3** | une connexion rendue au pool Npgsql libère-t-elle les verrous consultatifs de session ? | si non, un verrou oublié survit dans le pool : la libération explicite en `finally` devient **obligatoire**, pas prudentielle |
| **M-4** | délai de libération après mort brutale de la session (keepalive) | borne la durée d'immobilisation après plantage du migrateur (§4.6.3, point 7) |
| **M-5** | une transaction par migration ; état de la base après échec au milieu d'une chaîne | fonde le §6 (reprise) ; aujourd'hui `EF_BEHAVIOR — À CONFIRMER` |
| **M-6** | effet de `lock_timeout` sur un `ALTER TABLE` en attente derrière une session de poste | tranche la forme de **CX-1** |
| **M-7** | le rôle applicatif **peut** lire `__EFMigrationsHistory` et la métadonnée, et **ne peut pas** faire de DDL | **H5**, obligation bloquante |
| **M-8** | `pg_locks` lisible par le rôle migrateur | observabilité du verrou en test |
| **M-9** | les deux défauts EF ≥ 9 de §3.1.1 (a), ligne 2 — `Migrate()` en transaction utilisateur, changements de modèle en attente — se vérifient sur EF 10.0.12 | confirme la conséquence (2) de §3.1.1 : aucune transaction de l'outil autour de `Migrate()` |

---

## 4. Modèle de journal de migration

### 4.1 Emplacement — arrêté ici, comme DP-8 et DP-3.5.5 le demandent

Un **schéma PostgreSQL dédié**, propriété du rôle migrateur, hors du modèle EF, portant **deux** tables. Nom
proposé : **`mmv_meta`**. Les noms sont déclarés en un point unique (`ServerCompatibilityMetadataNames`,
fichier B2) et un test les verrouille.

**Pourquoi un schéma dédié plutôt que `public`** : il rend la propriété et les droits exprimables en un seul
`GRANT` / `REVOKE`, il sépare ce qui appartient à l'outil de ce qui appartient aux migrations, et il évite toute
collision avec une table applicative future. **Pourquoi deux tables** : DP-8 laisse le choix ; les deux objets
ont des **droits différents** — le rôle applicatif lit la compatibilité et **n'a aucun accès** au journal
(DP-5, DP-8). Une table unique rendrait ce partage impossible sans vue.

### 4.2 `mmv_meta.migration_run` — le journal autoritatif

| Colonne | Type | Champ minimal DP-8 | Note |
|---|---|---|---|
| `run_id` | `uuid` | — | identifiant d'exécution ; cité par la trace locale, ce qui relie les deux journaux |
| `app_version` | `text` | **version applicative** | SemVer lu par `ApplicationVersion` (DP-7) ; **source du calcul du minimum** (§4.3) |
| `applied_before` | `jsonb` | **migrations appliquées, avant** | liste ordonnée des identifiants EF |
| `applied_after` | `jsonb` | **migrations appliquées, après** | `NULL` tant que l'exécution n'est pas close |
| `started_at` | `timestamptz` | **date et heure, ouverture** | **`now()` serveur**, jamais l'horloge du poste |
| `finished_at` | `timestamptz` | **date et heure, clôture** | `now()` serveur ; `NULL` ⇒ exécution en vol |
| `operator_role` | `text` | **opérateur (1/2)** | `current_user` |
| `operator_reference` | `text` | **opérateur (2/2)** | `--operator`, exigée au lancement (H13, DP-9) ; **seule** trace distinguant magasin et support (modèle mixte) |
| `run_kind` | `text` | — | `migrate` \| `adopt` — distingue l'adoption d'une migration (§4.4) ; **nécessaire au calcul du §4.3**, où une adoption sert d'ancre sans avoir modifié le schéma |
| `outcome` | `text` | **résultat** | `open` \| `success` \| `failure` |
| `failure_cause` | `text` | **résultat, cause** | `NULL` si succès |
| `backup_reference` | `text` | **référence de sauvegarde** | identifiant de la sauvegarde **vérifiée** (DP-10, DI-7) |

**Deux écritures, hors transaction de migration** (DP-8.3) : `INSERT` à l'ouverture (`outcome='open'`),
`UPDATE` de résultat à la clôture. Une exécution interrompue laisse donc une ligne `open` avec
`finished_at IS NULL` — c'est la **preuve de l'échec**, et l'étape 4 de la reprise (§4.6.3) s'appuie dessus.

### 4.3 `mmv_meta.schema_compatibility` — la métadonnée de compatibilité (DP-3.5)

Une **seule ligne**, garantie par une contrainte (`CHECK (id = 1)` sur une clé primaire constante), et
`CHECK ((schema_version IS NULL) = (minimum_supported_version IS NULL))` : les deux versions sont nulles
ensemble (initialisation, §4.3.1) ou renseignées ensemble.

| Colonne | Type | Rôle |
|---|---|---|
| `schema_version` | `text` null | version de la release qui a **produit le schéma présent** = **N** ; `NULL` ⇒ initialisation inachevée |
| `minimum_supported_version` | `text` null | version de la release qui a produit le schéma **immédiatement précédent** = **N-1** au sens de §5.2.3.3, **calculé** (H17) ; `NULL` avec `schema_version` |
| `maintenance_started_at` | `timestamptz` null | signal de maintenance, lisible par le rôle applicatif ⇒ **état E5** (§5.3) — **ni verrou, ni autorité de compatibilité** |
| `updated_at` | `timestamptz` | `now()` serveur |
| `updated_by_run` | `uuid` | renvoi vers `migration_run.run_id` — sans donner au rôle applicatif le droit de lire cette table |

**Calcul — jamais une saisie (H17, DP-3.5.3).** À l'étape **10a** du §2.3, après succès des étapes 8 et 9.

*Correction de la version précédente.* Le pseudo-code prenait « la dernière release ayant modifié le schéma, en
excluant l'exécution en cours » comme minimum, et posait toujours `schema_version ← version courante`. Pour une
release **sans** migration, cela donnait `minimum` = la release qui a produit le schéma présent, soit **N au
lieu de N-1**, et avançait `schema_version` à tort — en contradiction avec sa propre règle « une release sans
migration ne consomme aucun cran ».

> **Amendement d'architecte du 04/10/2026 (ADR-009 §11.2) — remplace la règle ci-dessous.** Une ancre est un
> **état physique vérifié**, pas une exécution qui a elle-même appliqué une migration. L'état vérifié (Appliquées
> après) est comparé à celui de la **dernière ancre** : identiques ⇒ aucune évolution ; différents ⇒ nouvelle
> ancre. Chaque migration est attribuée à la release qui l'a **physiquement** appliquée (journal, échecs et
> plantages compris) : `schema_version` = productrice du schéma présent, `minimum` = productrice du schéma
> immédiatement précédent ; jamais la version de l'outil qui relance. Motif : avec la règle ci-dessous, une
> exécution dont les migrations s'appliquent puis dont la vérification échoue n'était l'ancre de personne — base
> bloquée après une première installation, et fenêtre accordée à un poste N-2 après la release suivante.
> L'exemple de référence reste valable à l'identique.

Règle corrigée (version du 04/10, **remplacée par l'amendement ci-dessus**), dérivée **uniquement** du journal :

```
ancres  ← lignes de migration_run, exécution en cours COMPRISE, telles que
            outcome = 'success'
            ET ( (run_kind = 'migrate' ET applied_after ≠ applied_before)   -- a produit un schéma
                 OU run_kind = 'adopt' )                                    -- point de départ adopté
          triées par started_at (strictement ordonnées : le verrou sérialise les exécutions)
versions ← app_version des ancres, en fusionnant les valeurs consécutives identiques

si versions est vide              → ne rien écrire (aucun schéma produit : la ligne reste en l'état)
schema_version                    ← dernier élément de versions                    (N)
minimum_supported_version         ← avant-dernier élément de versions,
                                     ou le dernier s'il est seul                    (N-1, ou N pour le 1er schéma)
```

**Exemple de référence — normatif, repris tel quel par I-9 :**

| Exécution | Migrations appliquées ? | Schéma présent | `schema_version` | `minimum_supported_version` |
|---|---|---|---|---|
| release **1.0.0** | oui | **S1** | `1.0.0` | `1.0.0` |
| release **1.1.0** | oui | **S2** | `1.1.0` | `1.0.0` |
| release **1.2.0** | **non** | **S2** | `1.1.0` | `1.0.0` |
| release **1.3.0** | oui | **S3** | `1.3.0` | `1.1.0` |

Lecture par la garde (§5.2) : sur **S3**, un poste **1.2.0** (qui connaît S2) est en **E3a** car `1.2.0 ≥ 1.1.0` ;
un poste **1.0.0** est en **E3b**. Sur **S2**, un poste 1.2.0 et un poste 1.1.0 sont en **E1** (Appliquées =
Connues).

Conséquences que le code doit porter explicitement :

- **une release sans migration ne consomme aucun cran** (§5.2.3.3) : elle n'est pas une ancre ; ni
  `schema_version` ni `minimum_supported_version` ne bougent ;
- **un échec ne crée pas d'ancre** (`outcome = 'failure'`) : la métadonnée décrit le dernier schéma **réussi**,
  ce qui préserve le point 3 de §4.6.3 ;
- **aucune valeur codée en dur** : ni version littérale, ni décalage fixe ; seul le journal fait foi ;
- **la valeur est un constat, pas une déclaration** : aucun développeur ne l'écrit, aucun `Up()` ne la touche.
  C'est ce qui retire à VS-2 son objection principale.

#### 4.3.1 Base vide — état d'initialisation explicite

Le scénario *base vide → création de `mmv_meta` → migration initiale → échec → relance de `migrate`* ne doit
**jamais** laisser une ligne `schema_version = minimum = version courante` sur une base dont la baseline n'a pas
réussi. D'où trois états de la ligne, et trois seulement :

| Moment | `schema_version` | `minimum_supported_version` | `maintenance_started_at` | Verdict de la garde |
|---|---|---|---|---|
| **avant réussite** de la première migration (posé à l'étape 6) | `NULL` | `NULL` | **≠ `NULL`** | **bloqué — initialisation inachevée** (§5.3), jamais E1 |
| **après réussite** (étapes 10a puis 11) | version courante | version courante | `NULL` | verdict normal (§5.2) |
| **après échec** de la première migration | `NULL` | `NULL` | **reste ≠ `NULL`** — l'étape 11 ne nettoie pas une ligne non initialisée | **bloqué — initialisation inachevée** |

**Reprise sans `adopt-compatibility`.** Une ligne en état d'initialisation n'est posée **que par l'outil**, à
l'étape 6 : elle prouve que l'outil a commencé l'installation. La relance de `migrate` la reconnaît (§4.4),
prend le verrou, nettoie le marqueur (étape 5 — il est périmé par définition, puisque l'outil détient le
verrou), le repose (étape 6), migre, et à l'étape 10a la première ancre réussie donne
`schema_version = minimum = version courante`. L'exécution échouée reste dans le journal (`failure`) et n'est
pas une ancre.

### 4.4 Réponse à **Q-22** — journal absent ou incomplet

| Situation | Comportement recommandé |
|---|---|
| historique EF **vide** (`mmv_meta` absent, présent sans ligne, ou ligne en état d'initialisation) | installation : l'outil crée ce qui manque, pose l'**état d'initialisation** (§4.3.1) et n'écrit `schema_version = minimum = version courante` **qu'après réussite** (§4.6.1, étape 4) |
| historique EF **non vide**, ligne **en état d'initialisation** (`schema_version IS NULL`) | **reprise d'une installation inachevée** (première montée partiellement appliquée) : `migrate` continue, **sans** `adopt-compatibility` |
| `mmv_meta` absent **ou sans ligne**, historique EF **non vide** (base migrée avant l'introduction du journal) | **`migrate` refuse — code 15.** Le minimum n'est pas calculable, et le déduire serait inventer une fenêtre. Seul `adopt-compatibility` peut initialiser, en posant `schema_version = minimum = version courante` : **la valeur la plus stricte possible**, qui n'admet aucun poste plus ancien. Journalisé avec `run_kind = 'adopt'` : cette ligne est l'**ancre** du calcul suivant (§4.3) |
| ligne présente mais illisible (version non SemVer, contrainte violée) | code 15 |
| Côté **poste**, métadonnée absente | **pas de fenêtre** : la garde retombe sur l'égalité stricte — démarrage seulement si Appliquées = Connues. Sûr par défaut, et E3a n'est pas accordé sur une supposition |

### 4.5 Droits, et la limite de périmètre à ne pas franchir

`GRANT USAGE ON SCHEMA mmv_meta` + `GRANT SELECT ON mmv_meta.schema_compatibility` au rôle applicatif ;
**aucun droit** sur `mmv_meta.migration_run` (DP-5, DP-8 : « le rôle applicatif ne lit pas le journal »).

**Q-23 — `CLOSED` par décision d'architecte du 04/10/2026.** (La proposition du 30/09 — `--app-role`
facultatif, journalisation bruyante en son absence — est **remplacée**.)

1. **P4-8** possède les rôles PostgreSQL **et leurs noms** (DP-5, Q-4). **P4-6B ne code aucun nom de rôle** —
   ni constante, ni défaut, ni configuration embarquée.
2. `MMV.DatabaseManager` accepte `--app-role <nom>`, **obligatoire** pour `migrate` et
   `adopt-compatibility` ; **non requis** pour `status`.
3. L'outil applique, **idempotemment**, à l'étape 4 du §2.3 :
   `GRANT USAGE ON SCHEMA mmv_meta` et `GRANT SELECT ON mmv_meta.schema_compatibility` au rôle `--app-role` —
   et **rien d'autre** : aucun droit sur `mmv_meta.migration_run`.
4. **Une migration ne peut pas être `success` si ce `GRANT` n'est pas assuré.** L'étape 9 vérifie, côté
   serveur, `has_schema_privilege(<rôle>, 'mmv_meta', 'USAGE')` et
   `has_table_privilege(<rôle>, 'mmv_meta.schema_compatibility', 'SELECT')` ; l'un des deux faux ⇒ code 14,
   `outcome = failure`, métadonnée **non avancée**.

**Contrôle adverse sur `--app-role` (entrée hostile).** La valeur vient de la ligne de commande et finit dans
un `GRANT`, où un identifiant **ne peut pas** être un paramètre lié. Règles : (a) existence vérifiée dans
`pg_roles` par requête **paramétrée** avant toute écriture, inexistant ⇒ code 10 ; (b) l'identifiant n'est
**jamais concaténé** côté client : le texte du `GRANT` est produit **par le serveur**,
`SELECT format('GRANT … TO %I', @role)` avec `@role` **paramètre lié**, puis exécuté tel quel ; (c) un test unitaire (U-C7) passe des noms hostiles (`x; DROP …`, guillemets, vide, espaces)
et attend un refus ou une citation exacte. Le droit de poser ces `GRANT` découle de la propriété du schéma
`mmv_meta` par le rôle migrateur (§4.1).

*Sur la légitimité de ce DDL hors chaîne EF* : **K-7** vise les **migrations**, qui doivent rester générées par
l'outillage EF. Ces deux tables ne sont pas des migrations : DP-3.5.1 et DP-8.1 les placent **délibérément hors
du modèle EF**, et citent `__EFMigrationsHistory` comme précédent — EF le crée et l'entretient hors du modèle.
Le DDL idempotent, exécuté par l'outil sous verrou, est donc la voie sanctionnée, pas un contournement.

---

## 5. Mécanisme de vérification de version d'un poste

### 5.1 Ce que le poste lit, et ce qu'il n'écrit pas

| Lecture | Source | Droit |
|---|---|---|
| **Appliquées** | `context.Database.GetAppliedMigrations()` → `__EFMigrationsHistory` | `SELECT` (DP-3.1.3, DP-5) |
| **Connues** | `context.Database.GetMigrations()` → assembly `MMV.Infrastructure.PostgreSQL.Migrations`, désignée par `MigrationsAssembly` (`CODE` [DatabaseProviderResolver](../../src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs)) | local, aucun droit |
| **minimum supporté**, **schema_version**, **maintenance** | `mmv_meta.schema_compatibility`, SQL brut | `SELECT` seulement |
| **version applicative** | `ApplicationVersion` (tranche A) | local |

**Zéro écriture, zéro DDL, zéro seed** (DP-4.1, SB-3). La garde est une fonction pure des quatre lectures
ci-dessus : elle se teste donc **entièrement sans serveur** sur des ensembles injectés, ce qui est le motif du
découpage de la tranche B.

### 5.2 Verdict — les cinq cas de DP-3

Notation : `A` = Appliquées, `K` = Connues, `v` = version du poste, `m` = minimum supporté.

| Cas | Condition | État | Comportement |
|---|---|---|---|
| **C-1** | `A = K` | **E1** | démarrage normal |
| **C-2** | `K ⊄ A` — le poste connaît des migrations **non appliquées** | **E2** | **blocage** : « la base doit être mise à jour par l'opérateur ». **Aucune fenêtre dans ce sens** |
| **C-3** | `A ⊋ K` et `v ≥ m` | **E3a** | **démarrage normal, en fenêtre.** Le poste travaille normalement, ni dégradé ni en lecture seule |
| **C-4** | `A ⊋ K` et `v < m` | **E3b** | **blocage** : « ce poste doit être mis à jour » |
| **C-5** | `A \ K ≠ ∅` **et** `K \ A ≠ ∅` — chaînes divergentes | **E3c** | **blocage inconditionnel** |

L'ordre d'évaluation importe : **les blocages du §5.3 lisibles dans la métadonnée d'abord** (initialisation
inachevée, maintenance), puis **C-5** (divergence), puis C-2, puis C-1, puis C-3 / C-4. Écrit dans
l'autre ordre, une divergence serait lue comme un simple retard.

**C-3 corrige U-1.** La vérification actuelle ne regarde que les migrations **en attente**
(`CODE` [SqliteDatabaseManager.cs:770](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L770) :
`GetPendingMigrations()`) : une migration **appliquée mais inconnue** du binaire passe inaperçue. La garde
ajoute cette détection — c'est la raison d'être de U-1, et le test doit l'exprimer comme tel.

### 5.3 États hors des cinq cas

| État | Détection | Comportement recommandé |
|---|---|---|
| **E4** — base vide | ni tables applicatives, ni historique | **blocage** : la baseline appartient à l'outil (DP-1) |
| **E5** — maintenance | `schema_compatibility.maintenance_started_at IS NOT NULL` | **blocage du démarrage**, message « maintenance en cours », **quel que soit l'âge** du marqueur. **Voir la réserve ci-dessous** |
| **E5, initialisation inachevée** | ligne présente avec `schema_version IS NULL` (§4.3.1) | **blocage**, message « installation de la base inachevée — relancer l'outil ». Évalué **avant** les cas C-1 … C-5 : une telle base n'est **jamais** saine, même si Appliquées = Connues |
| **E6** — serveur injoignable | exception de connexion | **D-15, P4-8** ; **jamais avalé** (K-13) |
| **E7** — tables **sans** historique EF | tables applicatives présentes, `__EFMigrationsHistory` absent | **blocage — réponse à Q-12.** C'est, côté serveur, l'équivalent de `HistoricalWithoutMigrationsHistory` côté SQLite (`CODE` [DatabaseState](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs#L15)) — mais l'**adoption** y est un chemin d'écriture, que DP-1 retire aux postes. Le poste bloque et renvoie à l'outil |

**`maintenance_started_at` — rôle exact, à lire avant de coder.** *(Remplace la réserve de la version
précédente, dont la contre-mesure n° 3 — « fenêtre bornée » au-delà de laquelle le marqueur serait ignoré —
est **retirée** : elle exigeait une durée arbitraire et faisait du timestamp un faux mécanisme d'expiration de
verrou.)*

| Question | Réponse |
|---|---|
| **Pourquoi le champ existe** | DP-6 impose un état de maintenance (CX-3). C'est le **seul** moyen pour un poste de connaître la maintenance **sans lire le journal**, que DP-5 lui interdit |
| **Informatif ou autoritaire ?** | **Signal d'admission à sens unique** : il peut **empêcher** un poste de démarrer (E5), il ne peut **jamais** l'autoriser. Il **n'entre pas** dans le verdict de compatibilité (Appliquées, Connues, version, minimum) et **ne sérialise rien** |
| **Autorité de sérialisation** | **le verrou consultatif, exclusivement** (DP-2, LK-1). L'outil **ne lit jamais** le marqueur pour décider s'il peut s'exécuter : il le **remplace** à l'étape 6, sous verrou |
| **Pendant la migration** | posé à l'étape 6, sous verrou ; un poste qui démarre voit E5 et attend. Un poste **déjà démarré** n'est pas revérifié (Q-11, P4-6C) : la protection des sessions ouvertes reste `lock_timeout` (CX-1) et la procédure « postes fermés » (CX-5) |
| **Après succès** | remis à `NULL` à l'étape 11, **avant** la libération du verrou (étape 12) |
| **Après échec** | remis à `NULL` à l'étape 11 : la base est à la dernière migration réussie, la métadonnée n'a pas avancé, les postes sont jugés sur l'**état réel** (§4.6.3, point 3). **Exception** : une ligne en état d'initialisation garde son marqueur (§4.3.1) |
| **Après plantage** de l'outil (étape 11 jamais atteinte) | le marqueur reste posé ; le serveur libère le verrou à la fin de la session. Les postes restent en E5 **jusqu'à la relance de l'outil**, qui le nettoie à l'étape 5 **après** avoir pris le verrou. C'est voulu : un plantage du migrateur exige de toute façon un diagnostic par le journal (§4.6.3, point 4) avant toute réouverture |
| **Timestamp ancien** | **aucune durée n'est appliquée** : un marqueur ancien bloque comme un récent. L'âge n'est qu'une **information de diagnostic**, affichée par `status`. Seul l'outil, **sous verrou**, le nettoie. **Nettoyer avant le verrou est INTERDIT** : seule la possession du verrou prouve que l'exécution qui l'a posé est morte |

Pourquoi aucune durée : un poste ne peut pas distinguer une migration **longue** d'un outil **mort** sans
interroger le verrou (`pg_locks`, droits `À MESURER`, CX-4 écarté ci-dessous). Toute durée serait donc soit
trop courte (un poste rouvert en pleine migration), soit sans fondement. Le coût de l'absence de durée — une
relance de l'outil après plantage — est borné et déjà exigé par la procédure de reprise.

Cette forme est la réponse minimale à **DP-6** (état de maintenance imposé) : elle combine **CX-3** (indicateur
en base, sans nouvelle table) et **CX-5** (procédure « tous les postes fermés »), avec **CX-1**
(`lock_timeout`) côté migrateur. **CX-2** (verrou partagé tenu par chaque poste) et **CX-4**
(`pg_stat_activity` + `Application Name`) sont **écartés pour la V1** : CX-2 impose une connexion dédiée hors
pool à chaque poste, CX-4 dépend de droits `À MESURER` et exigerait de modifier la chaîne de connexion (Q-19,
propriété partagée avec P4-8). **Réponse à Q-9.**

### 5.4 Comparaison de versions — et pourquoi aucun paquet nouveau

DP-7 impose SemVer. La comparaison porte sur le triplet `Major.Minor.Patch` uniquement : `System.Version`
suffit, aucune dépendance NuGet n'est ajoutée (contrainte de terrain, ADR-008 §2.4). Une étiquette de
pré-version (`-rc.1`) est **refusée** par la garde comme par l'outil, avec un message explicite : aucune
release de production n'en porte, et l'ordre SemVer des pré-versions ne se lit pas avec `System.Version`. À
inscrire dans CONTRIBUTING.md avec la règle d'incrément.

### 5.5 Où la garde **n'est pas** appelée

`MMV.App` n'appelle pas la garde en P4-6B. Le garde-fou de démarrage reste actif ; sa levée, l'écran de blocage
à trois messages (E2, E3b, E5) et le branchement de la garde sont **P4-6C**
([ADR-009 §8.1](../architecture/ADR-PROD-DB-009.md)). P4-6B livre un composant **testé et non branché** —
c'est délibéré : le branchement change le comportement du poste et appartient à P4-6C.

---

## 6. Stratégie de retour arrière

### 6.1 Ce qui existe réellement comme retour arrière : la restauration, et rien d'autre

| Fait | Preuve |
|---|---|
| Les `Down()` ne sont pas outillés, et celui de la baseline **supprime tout** | `DOC` [ADR-009 §4.6.3, U-7](../architecture/ADR-PROD-DB-009.md) |
| `dotnet ef database update` et `database drop` sont **interdits** sur toute chaîne | `DOC` [CONTRIBUTING.md, Interdits n° 2](../../CONTRIBUTING.md) |
| La seule voie réaliste est la restauration d'une sauvegarde ; l'outillage est **P4-9** | `DOC` ADR-009 DP-10, U-7, DR-11 ; DI-7 |

**Donc : `MMV.DatabaseManager` n'implémente aucun retour arrière**, et n'expose aucun verbe qui y ressemble. Ce
que P4-6B livre, ce sont les **conditions** qui rendent une restauration possible et un échec diagnosticable.

### 6.2 Les six garanties que P4-6B doit porter

| # | Garantie | Mise en œuvre |
|---|---|---|
| **R-1** | **aucune migration sans sauvegarde vérifiée** (H12, DP-10) | `IBackupVerification` interrogé **avant** le verrou ; `RefusingBackupVerification` refuse ⇒ code 11. La **vérification réelle** est P4-9 : P4-6B livre le **port** et le refus, pas l'outillage. **Seam de test ci-dessous** |
| **R-2** | la référence de sauvegarde est **enregistrée** | `migration_run.backup_reference` — c'est l'unique point de retour arrière (DI-7) |
| **R-3** | un échec laisse la base à la **dernière migration réussie** | transaction par migration (EF) — `À MESURER` (**M-5**). L'outil ne l'affirme pas : il **relit** l'historique et journalise `applied_after` réel |
| **R-4** | la **trace de l'échec survit** à l'annulation | écritures de journal **hors** transaction (DP-8.3) : ligne `open` + `UPDATE` de résultat |
| **R-5** | la métadonnée **n'a pas avancé** | étape 8 **après** vérification. Un poste N-1 est donc jugé sur l'**état réel** (§4.6.3, point 3) |
| **R-6** | le verrou est **libéré** | `pg_advisory_unlock` en `finally` ; fin de session en filet ; délai de détection `À MESURER` (**M-4**) |

**Seam de vérification de sauvegarde — R-1 ne doit avoir aucune porte dérobée.**

| Contexte | Implémentation de `IBackupVerification` | Comment elle arrive |
|---|---|---|
| **Production** (P4-6B) | `RefusingBackupVerification` — **refuse toujours** | **seule** implémentation construite par `Program.cs` (racine de composition de l'outil) |
| **Production** (après P4-9) | le vérificateur réel de **P4-9** | remplace `RefusingBackupVerification` **dans `Program.cs`**, par un commit revu — pas par configuration |
| **Tests** (unitaires et intégration) | double `IBackupVerification` (stub qui accepte, stub qui refuse) | **injecté** dans le constructeur de `MigrationRunner` par le test ; jamais via `Program.cs` |

**Interdits** : aucune variable d'environnement, aucune option de ligne de commande (`--skip-backup` ou
équivalent), aucune clé de configuration, aucun `#if DEBUG` ne permet de contourner la vérification en
production. Preuves : **U-C9** (la surface d'options ne contient aucun contournement ; `Program.cs` ne construit
que `RefusingBackupVerification`) et **U-C2** (refus ⇒ code 11, aucune écriture en base).

**Conséquence assumée** : tant que P4-9 n'a pas livré son vérificateur, `migrate` **refuse sur toute base
réelle**. P4-6B prouve la mécanique en tests ; la mise en service réelle de l'outil dépend de P4-9.
**Décision Q-24 (architecte, 04/10/2026)** : P4-6B est implémentable **maintenant** avec
`RefusingBackupVerification` ; **P4-9 est obligatoire avant P4-6C**, c'est-à-dire avant la levée effective du
garde-fou de démarrage (§8.2).

### 6.3 Reprise — deux voies, documentées, aucune automatisée

1. **En avant** : corriger la cause, relancer `migrate`. EF ne rejoue pas les migrations déjà inscrites
   (`À MESURER`). Une **nouvelle** ligne `migration_run` est créée : l'historique des tentatives est conservé.
2. **Par restauration** de la sauvegarde référencée en `backup_reference`. **P4-9.** La procédure est
   documentée en P4-6C / P4-9 (H10), pas outillée ici.

### 6.4 Réponse à **Q-15** — les migrations non transactionnelles

Une opération qui **ne peut pas** s'exécuter en transaction laisserait un état partiel, hors de portée de R-3.
**Recommandation : l'interdire en V1**, par une entrée au tableau **Interdits** de CONTRIBUTING.md — pas de
`CREATE INDEX CONCURRENTLY`, ni de migration marquée non transactionnelle, ni de DDL hors transaction dans un
`Up()`. Motif : R-3 et la fenêtre N-1 reposent tous deux sur l'atomicité par migration ; une exception les
prive de fondement. Le jour où un index volumineux l'exigera, ce sera une décision d'architecture, pas un choix
d'implémentation.

---

## 7. Tests nécessaires

### 7.1 Deux familles, jamais fusionnées

La suite unitaire vaut **1953** tests à `692b6da`, et la suite d'intégration **74** (`CI` `37202557042` ;
`DOC` [rapport P4-5F](P4-5F-postgresql-integration-tests-report.md)).
Les tests d'intégration se comptent **séparément** et ne rejoignent **jamais** ce nombre (ADR-008 §5.7) : une
baseline « en hausse » masquerait un serveur absent.

### 7.2 Sans serveur — suite unitaire (job Windows)

Emplacements : `MMV.Domain.Tests` pour ce qui teste `MMV.Infrastructure` (référence existante, §1 B) ;
**`MMV.DatabaseManager.Tests`** (nouveau, C13) pour tout ce qui teste `MMV.DatabaseManager` ; `MMV.App.Tests`
pour les tests d'architecture de l'application.

| # | Test | Emplacement | Obligation |
|---|---|---|---|
| **U-A1** | `ApplicationVersion` retourne `Major.Minor.Patch`, **sans suffixe `+sha`** | `tests/MMV.Domain.Tests/Configuration/` | H7 |
| **U-A2** | la version vient de l'assembly **Infrastructure**, jamais de `GetEntryAssembly()` | idem | H7, DI-5.2 |
| **U-A3** | `SettingsView.axaml` ne contient **aucun** littéral de version ; `SettingsViewModel.Version` est la source | `tests/MMV.App.Tests/Architecture/` | DP-7.3 |
| **U-B1** | les cinq cas C-1 … C-5 sur ensembles injectés, **y compris l'ordre d'évaluation** (une divergence n'est pas lue comme un retard) | `tests/MMV.Domain.Tests/Data/` | **H14**, partiel |
| **U-B2** | E3a est accordé **seulement** si `v ≥ m` ; N-2 bloqué ; poste plus récent bloqué **sans fenêtre** | idem | **H14** |
| **U-B3** | métadonnée **absente** ⇒ égalité stricte, pas de fenêtre (Q-22) | idem | H14 |
| **U-B4** | `maintenance_started_at` non `NULL` ⇒ E5 **quel que soit son âge** (aucune durée) ; le marqueur n'autorise jamais un démarrage | idem | DP-6 |
| **U-B5** | E7 (tables sans historique) ⇒ blocage (Q-12) | idem | DP-4 |
| **U-B6** | la garde n'émet **aucun** SQL d'écriture — `DbCommand` interceptés | idem | **DP-4.1**, SB-3 |
| **U-B7** | ligne en état d'initialisation (`schema_version IS NULL`) ⇒ **bloqué**, même si Appliquées = Connues ; évalué avant C-1 … C-5 | idem | §4.3.1 |
| **U-C1** | `migrate` sans `--operator` ⇒ code 10 | `tests/MMV.DatabaseManager.Tests/` | **H13** |
| **U-C2** | `migrate` sans sauvegarde vérifiée ⇒ code 11, **aucune entrée en base** | idem | **H12** |
| **U-C3** | calcul du §4.3 sur journaux injectés : **l'exemple de référence** (1.0.0 / 1.1.0 / 1.2.0 sans migration / 1.3.0 ⇒ valeurs exactes du tableau) ; une release sans migration ne consomme aucun cran ; un `failure` n'est pas une ancre ; une ancre `adopt` ; versions consécutives identiques fusionnées ; journal sans ancre ⇒ aucune écriture | idem | **H17** |
| **U-C4** | le minimum n'est **jamais** un paramètre : aucune surface publique ne permet de le fixer | idem | **H17** |
| **U-C5** | les six champs minimaux de DP-8 sont tous présents et non nuls à la clôture | idem | **H8** |
| **U-C6** | la clé du verrou est la constante attendue, déclarée en un seul point | idem | DP-2 |
| **U-C7** | `--app-role` : absent pour `migrate` / `adopt-compatibility` ⇒ code 10 ; non requis pour `status` ; noms hostiles refusés ou cités exactement, jamais concaténés | idem | **Q-23** |
| **U-C8** | **ordre de la séquence** du §2.3 sur doubles enregistreurs : aucune écriture avant le verrou ; nettoyage du marqueur **après** le verrou ; sortie 10b → 11 → 12 ; métadonnée non avancée si 9 échoue ; `success` impossible si le `GRANT` n'est pas assuré | idem | DP-2, DP-3, DP-6, **Q-23** |
| **U-C9** | aucun contournement de la vérification de sauvegarde : ni option, ni variable d'environnement ; `Program.cs` ne construit que `RefusingBackupVerification` | idem | **H12**, R-1 |
| **U-D1** | `MMV.App` ne référence **pas** `MMV.DatabaseManager`, et aucun type de l'outil n'est atteignable depuis les assemblys de l'application | `tests/MMV.App.Tests/Architecture/` | **H11** |
| **U-D2** | [ServerStartupGuardTests](../../tests/MMV.App.Tests/Architecture/ServerStartupGuardTests.cs) **reste vert sans modification** | existant | K-13, périmètre |
| **U-D3** | [MigrationChainsTests](../../tests/MMV.Domain.Tests/Data/Migrations/MigrationChainsTests.cs) **reste vert sans modification** ⇒ aucune migration ajoutée, aucune table dans le modèle | existant | **H16**, H9 |
| **U-D4** | les 14 migrations SQLite et le cycle SQLite sont **inchangés** : la suite existante passe sans retouche | existant | **H9**, DR-8, K-12 |
| **U-E1** | **contrôle d'« étendre → migrer → contracter »** : énumère les opérations de contraction (`DropColumn`, `DropTable`, `RenameColumn`, resserrement de nullabilité) des `UpOperations` de chaque migration et les compare à une **liste d'autorisation explicite**. Toute contraction nouvelle **casse le test** jusqu'à ce que l'auteur la déclare — même idiome que `MigrationChainsTests` | `tests/MMV.Domain.Tests/Data/Migrations/` | **Q-21**, H15 |

**U-E1 est la réponse à Q-21**, la seule question dont l'ADR dit qu'elle touche « la qualité de P4-6B ». Une
revue humaine était le seul garde-fou ; ce test rend chaque contraction **visible en revue** sans interdire une
contraction légitime à la release suivante.

### 7.3 Avec serveur — projet d'intégration P4-5F (job Linux, **plus bloqués**)

Ces tests vivent dans le **projet d'intégration livré par P4-5F** (`Lifecycle/`, C17), jamais dans la suite
unitaire (ADR-008 §5.9, E3 rejetée). Ils injectent un double `IBackupVerification` qui accepte (§6.2) et
créent leurs rôles (applicatif sans DDL, migrateur) dans la base jetable.

| # | Test | Critère | Obligation |
|---|---|---|---|
| **I-1** | **deux exécutions simultanées** de l'outil : **une seule** applique le DDL, l'autre échoue en code 12 | S-1 | **H2** |
| **I-2** | verrou pris sur une base **vide**, avant toute baseline | S-1 | **H2**, DP-2 |
| **I-3** | verrou tenu sur **toute** une montée à plusieurs migrations (M-1) | S-1 | **H2** |
| **I-4** | plantage du migrateur : verrou libéré, base à la dernière migration réussie, ligne `open` **présente** | S-2 | R-3, R-4, R-6 |
| **I-5** | le **rôle applicatif ne peut pas** faire de DDL ; il **peut** lire l'historique et la métadonnée | S-3 | **H5**, M-7 |
| **I-6** | le rôle applicatif **ne peut pas** lire `mmv_meta.migration_run` | S-3 | DP-5, DP-8 |
| **I-7** | **fenêtre N-1 de bout en bout** : un poste N-1 démarre **et écrit** sur un schéma N (E3a) ; un N-2 est bloqué **sans écriture** (E3b) ; un poste plus récent est bloqué (E2) | S-4 | **H14** |
| **I-8** | journal : six champs présents, horodatages issus de `now()` **serveur**, entrées survivant à l'annulation | S-2 | **H8** |
| **I-9** | **exemple de référence du §4.3, de bout en bout** sur un serveur réel : quatre exécutions successives sous les versions 1.0.0, 1.1.0, 1.2.0 (**sans** migration), 1.3.0 — versions injectées au `MigrationRunner` par le test, avec une chaîne de migrations de test ; après chacune, la ligne vaut exactement (`1.0.0`,`1.0.0`), (`1.1.0`,`1.0.0`), (`1.1.0`,`1.0.0`), (`1.3.0`,`1.1.0`). Variante : un échec entre 1.1.0 et 1.3.0 ne change pas la ligne | S-4 | **H17**, DP-3 |
| **I-10** | `adopt-compatibility` sur une base migrée sans journal : `schema_version = minimum = version courante`, ligne de journal `run_kind = 'adopt'` ; un `migrate` ultérieur qui applique une migration donne `minimum` = version adoptée | — | **Q-22**, H17 |
| **I-11** | migration pendant qu'un poste **est connecté** : `lock_timeout` produit un échec **propre** (M-6) | roadmap P4-6 | DP-6, **H6** (partagé P4-6C) |
| **I-12** | après `migrate`, le rôle `--app-role` **lit** `schema_compatibility` et **ne lit pas** `migration_run` ; rejouer `migrate` laisse les droits identiques (idempotence) ; un rôle privé du `GRANT` entre-temps ⇒ code 14, `failure` | — | **Q-23**, H5 |
| **I-13** | marqueur `maintenance_started_at` laissé par une exécution tuée : la garde rend E5 quel que soit son âge ; une seconde exécution **sans verrou** n'y touche pas ; **avec** verrou, elle le nettoie et le trace | — | DP-6, §2.3 propriété 2 |
| **I-14** | **base vide, échec de la migration initiale, relance** : échec provoqué de façon déterministe par un objet homonyme d'une table de la baseline, créé avant `migrate` ; après l'échec : `schema_version` et `minimum` `NULL`, `maintenance_started_at` non `NULL`, journal `failure`, garde ⇒ **bloqué (initialisation inachevée)** ; objet retiré puis **relance de `migrate` sans `adopt-compatibility`** : succès, `schema_version = minimum = version courante`, `maintenance_started_at` `NULL` | — | §4.3.1, Q-22, DP-8 |

**I-1 et la question Q-13.** DP-1 retirant le DDL aux postes, S-1 n'est plus « deux postes démarrent en même
temps » mais « **deux exécutions de l'outil** ». **Recommandation : deux connexions dans un seul processus
suffisent à P4-6B** ; la variante **multi-processus** reste **P4-10** (ADR-008 §5.11), où le
[worker du spike](../../spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj) a déjà servi de précédent
(E9, 20/20 rondes sans anomalie). **Réponse à Q-13.**

### 7.4 Garde anti-faux-vert

Les deux verrous d'ADR-008 §5.4 s'appliquent **sans exception** aux tests I-n : ignorés en local avec un
message actionnable ; **un `skip` en CI est un échec**. Un plan qui laisserait H2, H5 ou H14 « vertes » sur des
tests ignorés livrerait une sérialisation non prouvée.

---

## 8. Impacts sur la roadmap et sur les ADR

### 8.1 Roadmap P4 — deux modifications

*(La clôture de P4-5F — état, dépendance de P4-6B, note « levé le 04/10/2026 » — est déjà inscrite dans la
roadmap ; le découpage `P4-6B-1` / `P4-6B-2` de la version du 30/09 est **abandonné**, §0.)*

1. **§P4-6, tableau de découpage** : état de P4-6B, mis à jour à chaque tranche commitée et verte ; colonne
   « Dépend de » de P4-6C : **P4-6B + P4-9** (décision Q-24).
2. **Bloc de verdicts** : lignes proposées, à poser **seulement** quand elles sont vraies.

```
P4-6B APPLICATION VERSION     = ...   (DP-7, H7)
P4-6B COMPATIBILITY GUARD     = ...   (DP-3, DP-4, H14 — volet sans serveur)
P4-6B DATABASE MANAGER        = ...   (DP-1, H11)
P4-6B MIGRATION LOCK          = ...   (DP-2 — preuve serveur I-1 … I-4)
P4-6B SERVER JOURNAL          = ...   (DP-8, H8)
P4-6B APP ROLE GRANT          = ...   (Q-23, H5 — I-12)
P4-6B EXPAND MIGRATE CONTRACT = ...   (DP-3, H15)
P4-6B SERVER PROOFS           = ...   (S-1 … S-4 : H2, H5, H14 — job postgresql-integration)
P4-6B DATABASE LIFECYCLE      = ...
V1 MULTI-POSTE                = NOT GO
```

### 8.2 ADR-PROD-DB-009 — un **addendum**, aucune décision rouverte

Les huit questions du §9.2 dont P4-6B est propriétaire reçoivent une réponse. Aucune ne touche au §5.2.

| # | Question | Réponse proposée | Où elle est instruite |
|---|---|---|---|
| **Q-6** | EF Core 8 + LK-1, ou verrou natif EF ≥ 9 (LK-5) ? | **Tranchée par disponibilité et par mesure.** EF Core 10 est en place ; le verrou natif de Npgsql est `LOCK TABLE … ACCESS EXCLUSIVE`, soit **LK-4** en substance, inopérant sur base vide et de portée transactionnelle. **LK-1 est retenu** comme verrou de l'outil ; le verrou natif reste actif dessous, sans être désactivé | §3 |
| **Q-7** | des scripts SQL générés peuvent-ils exécuter les migrations post-baseline ? | **Non en V1** : l'outil appelle `Migrate()`. Un script ajouterait un artefact à produire, signer et vérifier, pour un gain nul (DR-10). **K-7 et l'interdit n° 2 de CONTRIBUTING.md restent la règle** | §2.3 |
| **Q-9** | état de maintenance : garde technique ou procédure ? | **CX-3 minimal + CX-5 + CX-1** : indicateur dans `schema_compatibility`, procédure « tous les postes fermés », `lock_timeout` côté migrateur. **CX-2 et CX-4 écartés pour la V1** | §5.3 |
| **Q-11** | revérification en cours de session ? | **Non en V1.** L'indicateur de maintenance réduit le risque ; une revérification périodique change le comportement du poste en cours de session, ce qui appartient à **P4-6C** avec l'écran de blocage | §5.3 |
| **Q-12** | E7, tables sans historique EF ? | **Blocage.** L'adoption est un chemin d'écriture, que DP-1 retire aux postes | §5.3 |
| **Q-13** | S-1 en P4-6 ou P4-10 ? | **P4-6B** : deux connexions, un processus. **P4-10** : multi-processus | §7.3 |
| **Q-15** | migrations non transactionnelles ? | **Interdites en V1**, par une entrée au tableau Interdits | §6.4 |
| **Q-21** | contrôle automatique d'« étendre → migrer → contracter » ? | **Test d'énumération + liste d'autorisation explicite** sur les `UpOperations` | §7.2, U-E1 |
| **Q-22** | journal absent ou incomplet ? | **`migrate` refuse (code 15)** sauf base vide ou ligne en état d'initialisation (reprise sans adoption) ; `adopt-compatibility` pose `schema_version = minimum = version courante`, journalisé `run_kind = 'adopt'` ; côté poste, métadonnée absente ⇒ **pas de fenêtre**, ligne en initialisation ⇒ **bloqué** | §4.3.1, §4.4 |

**`Q-23` — question née de ce plan, `CLOSED` par décision d'architecte du 04/10/2026**, à enregistrer dans
l'addendum : P4-8 possède les rôles et leurs noms ; P4-6B n'en code aucun ; `--app-role` est **obligatoire**
pour `migrate` et `adopt-compatibility`, non requis pour `status` ; l'outil applique idempotemment
`GRANT USAGE ON SCHEMA mmv_meta` et `GRANT SELECT ON mmv_meta.schema_compatibility` ; une migration n'est
**jamais** `success` si ce `GRANT` n'est pas assuré (§4.5). L'alternative `ALTER DEFAULT PRIVILEGES` par P4-8
seule est **écartée** comme mécanisme dont dépend P4-6B.

**`Q-24` — question née de la décision R-1, `CLOSED` par décision d'architecte du 04/10/2026**, à enregistrer
dans l'addendum et dans la roadmap (D3). `RefusingBackupVerification` refuse toujours (§6.2) : jusqu'à P4-9,
`migrate` refuse sur toute base réelle. Décision :

```
P4-6B = implémentable maintenant (RefusingBackupVerification tant que P4-9 n'est pas disponible)
P4-6C = dépend de P4-9
P4-9  = obligatoire avant la levée effective du garde-fou de démarrage
```

Ordre résultant : **P4-6B → P4-9 → P4-6C**. P4-6B ne livre rien de P4-9 et ne lève pas le garde-fou.

**Q-20** (renommage du fichier `ADR-PROD-DB-009.md` vers `adr-prod-db-009-*.md`) reste ouverte et **ne bloque
rien** ; P4-6B ne la traite pas — le renommage casserait les renvois de plusieurs documents.

### 8.3 ADR-APP-DISTRIBUTION-001 — statut **inchangé**

Reste **PROPOSED** : SD-1, SD-2, SD-3, SD-5 non exécutés, QD-1 et QD-10 ouverts. P4-6B ne fait pas
d'empaquetage, ne signe rien, ne touche pas à l'updater — tout cela est **P8**. Deux interactions, à
enregistrer sans changer de statut :

- **DI-5.2** (« les deux exécutables portent la même version applicative ») devient **réalisable** : la
  tranche A pose la source unique, et `ApplicationVersion` lit l'assembly Infrastructure, partagée. P4-6B
  satisfait le **préalable**, pas l'obligation ;
- **DI-9** (séparation installation / données) contraint l'emplacement de la **trace locale** de l'outil
  (§2.3). À arbitrer avec P8 ; le défaut proposé est un dossier de données utilisateur.

### 8.4 CONTRIBUTING.md — ce qui s'ajoute exactement

Nouvelle section, insérée **entre** « Règle de double migration » (l. 153-201) et « Commandes EF de référence »
(l. 203), portant :

1. **« Étendre → migrer → contracter »** — les trois temps de DP-3 §5.2.3.4 ;
2. **l'interdiction de contracter dans la release qui étend** ;
3. **l'interdiction de déployer deux releases porteuses de schéma dans une même tournée de postes** (H15,
   OI-13) — avec sa conséquence dure : la fenêtre vaut **un** cran, donc la tournée est bornée dans le temps ;
4. **la règle d'incrément du SemVer** et l'emplacement de sa source unique (DP-7.4) ; pré-versions refusées ;
5. **la règle de transaction** : une migration doit s'exécuter en transaction (Q-15).

Tableau **Interdits** (l. 304) — deux entrées : *migration non transactionnelle* ; *contraction dans la même
release que l'expand*. Tableau de **revue avant commit** (l. 320) — deux contrôles : *les opérations de
contraction sont déclarées dans la liste d'autorisation* ; *la tranche de la tournée en cours est connue avant
de publier une release porteuse de schéma*.

---

## 9. Ce que ce lot ne fait pas

| Hors périmètre | Propriétaire |
|---|---|
| Levée du garde-fou de démarrage ; écran de blocage à trois messages ; branchement de la garde | **P4-6C** |
| Portage complet de `VerifyAfterPreparation` (invariants **physiques** : colonnes, index filtrés, prédicats) vers PostgreSQL | **P4-6C** — §4.6.1 étape 4 le rattache explicitement à P4-5G / P4-6C. P4-6B se limite à : aucune migration en attente, `Appliquées ⊆ Connues`, historique lisible |
| Création de la base, provisionnement, **noms** des trois rôles, secrets, TLS, premier administrateur | **P4-8** (D-09, D-12, D-13, D-14, D-15) |
| Outillage de **sauvegarde** et de sa **vérification** ; restauration | **P4-9** (O9). P4-6B livre le **port** et le refus par défaut |
| Empaquetage, signature, updater, `packId` | **P8** / ADR-APP-DISTRIBUTION-001 |
| Concurrence **multi-processus**, perte réseau, reconnexion, interblocage | **P4-10** (O13) |
| `EnsureCreated()` du seed de démonstration sous un rôle sans DDL (C-1, Q-16) | **P4-6C** |
| Projet d'intégration PostgreSQL et **job CI avec serveur** | **P4-5F — livré** (`692b6da`, CI `37202557042`) ; P4-6B l'**utilise**, n'en modifie que le `.csproj` (+1 référence, C16) |
| Preuve sur PostgreSQL **natif Windows** (`MMV-SRV`) | **O12 / P4-4** — séparée |

---

## 10. Critères de sortie de P4-6B

| # | Critère | Prouvé par | Serveur requis |
|---|---|---|---|
| 1 | Version applicative unique en SemVer, affichée et journalisée (**H7**) | U-A1 … U-A3 | non |
| 2 | Garde de compatibilité **en lecture seule**, cinq cas et huit états (**H1** partiel, **H3**, **H4**) | U-B1 … U-B7 | non |
| 3 | `MMV.App` ne contient **aucun** chemin d'exécution de DDL PostgreSQL, prouvé par test (**H11**) | U-D1, U-D2 | non |
| 4 | Aucun changement de modèle EF, aucune migration, **deux** contrôles de dérive verts (**H16**) | U-D3, CI | non |
| 5 | Chemin SQLite **sans régression**, 14 migrations inchangées, 1953 tests verts (**H9**) | U-D4, CI | non |
| 6 | Discipline « étendre → migrer → contracter » inscrite **et outillée** (**H15**, Q-21) | D1, U-E1 | non |
| 7 | Journal serveur conforme à DP-8 : table dédiée **et** trace locale, écritures hors transaction, six champs (**H8**) | U-C5, **I-8** | **oui** |
| 8 | Minimum supporté **calculé**, jamais saisi (**H17**) | U-C3, U-C4, **I-9**, I-10, I-14 | **oui** |
| 9 | Migration sur **action explicite**, référence d'opérateur exigée et journalisée (**H13**) | U-C1, I-8 | **oui** |
| 10 | **Aucune** migration sans sauvegarde vérifiée (**H12**) | U-C2 | non pour le refus |
| 11 | **Sérialisation prouvée** : deux acteurs simultanés, **une seule** application du DDL (**H2**) | **I-1 … I-4** | **oui** |
| 12 | Rôles conformes à DP-5 : le rôle applicatif **ne peut pas** faire de DDL (**H5**) ; il lit la métadonnée grâce au `GRANT` de l'outil, jamais le journal (**Q-23**) | **I-5, I-6, I-12**, U-C7 | **oui** |
| 13 | **Fenêtre N-1 prouvée** : N-1 écrit sur un schéma N ; N-2 bloqué ; plus récent bloqué (**H14**) | U-B2, **I-7** | **oui** |
| 14 | Commit + **CI verte sur le SHA exact**, par tranche | Actions | — |

**Les critères 7, 8, 9, 11, 12, 13 exigent un serveur PostgreSQL en CI** : le job `postgresql-integration`
(P4-5F, vert sur `692b6da`) le fournit. Ils ne sont **plus bloqués** ; ils restent **à prouver**. Aucune
preuve locale ne remplace cette CI, et un `skip` y est un échec.

---

## 11. Verdicts de ce plan

```
P4-6B PLAN                      = READY FOR P4-6B IMPLEMENTATION — REVISED 2026-10-04 (2nd pass)
P4-6B DECISION INPUTS           = COMPLETE — ADR-PROD-DB-009 ACCEPTED, 10 DP ARRÊTÉS
P4-6B TECHNICAL PREREQUISITE    = SATISFIED — P4-5F COMPLETE — CLOSED — 692b6da — CI 37202557042 SUCCESS
P4-6B Q-6 LOCK                  = ANSWERED BY MEASUREMENT — LK-1 RETENU ; LK-5 = LK-4 CHEZ NPGSQL
P4-6B Q-9 / Q-11 / Q-12         = ANSWERED — À RATIFIER PAR ADDENDUM
P4-6B Q-13 / Q-15 / Q-21 / Q-22 = ANSWERED — À RATIFIER PAR ADDENDUM
P4-6B Q-23 APP ROLE GRANT       = CLOSED — ARCHITECT DECISION 2026-10-04 — --app-role MANDATORY
                                  (migrate, adopt-compatibility), NO ROLE NAME IN CODE, IDEMPOTENT GRANT,
                                  NO SUCCESS WITHOUT GRANT
P4-6B MIGRATE SEQUENCE          = FIXED — §2.3 — ADR §4.6.2 STEPS 1-6 ; 7-8 OUT OF TOOL ; 3 CLEANUP STEPS ;
                                  NO DB WRITE BEFORE LOCK ; STALE MARKER CLEANED ONLY UNDER LOCK
P4-6B BACKUP SEAM               = FIXED — PROD RefusingBackupVerification (→ P4-9) ; TESTS INJECT
                                  IBackupVerification ; NO ENV / OPTION BYPASS
P4-6B TEST LOCATION             = Domain.Tests (Infrastructure, existing ref) ; NEW MMV.DatabaseManager.Tests ;
                                  IntegrationTests (+1 ProjectReference)
P4-6B Q-24 SEQUENCE            = CLOSED — ARCHITECT DECISION 2026-10-04 — P4-6B NOW ; P4-9 BEFORE P4-6C
P4-6B N-1 COMPUTATION          = CORRECTED — §4.3 — ANCHORS FROM JOURNAL ; REFERENCE EXAMPLE 1.0.0 … 1.3.0 ; I-9
P4-6B EMPTY-BASE INIT           = EXPLICIT INTERMEDIATE STATE — §4.3.1 — NULL / NULL / MAINTENANCE ; I-14
P4-6B MAINTENANCE MARKER        = ONE-WAY ADMISSION SIGNAL — NO DURATION — NOT A LOCK — CLEANED ONLY UNDER LOCK
P4-6B TRANCHES A / B / C / D / E = EXECUTABLE — NONE BLOCKED BY P4-5F OR P4-9
P4-6C                           = FUTURE — REQUIRES P4-6B VALIDATED AND P4-9
P4-6B CI CHANGE                 = NONE EXPECTED
P4-6B                           = NOT STARTED
V1 MULTI-POSTE                  = NOT GO
```

**Aucun code n'a été écrit. Aucun fichier de `src/`, de `tests/`, de `.csproj`, de migration ou de CI n'a été
modifié. Ce document n'est pas commité.**
