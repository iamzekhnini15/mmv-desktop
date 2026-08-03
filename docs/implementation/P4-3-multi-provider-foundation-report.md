# P4-3 — Fondation Infrastructure multi-provider — rapport d'implémentation

> **Statut : `COMPLETE AND RECORDED — COMMIT + CI SUCCESS`.**
> **P4-3 est close.** L'implémentation a d'abord été **validée localement** (passe locale, sans staging ni
> commit ni push), puis **enregistrée par commit et CI** : commit
> **`b312a6c901b1e620a3694a32ce0cd14e2f0fc707`**, CI
> [**`30823399148`**](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30823399148) — `event=push`,
> `headSha` **exact**, **`conclusion=success`**, **1529 tests** (Domain 673 · Application 617 · App 239).
> **La V1 multi-poste n'est toujours pas déclarée `GO`.**

- **Base Git** : `724ffecc61a891b150193d838315edc61e238a1a` (branche `p4-multi-poste`,
  identique à `origin/p4-multi-poste` au démarrage de la passe).
- **Décision de référence** : [ADR-PROD-DB-002](../architecture/adr-prod-db-002-server-database-provider-selection.md)
  — **acceptée**, **PostgreSQL retenu pour la V1 multi-poste**, **SQL Server Express rejeté pour V1 mais non éliminé**,
  **SQLite conservé** en dev / test / démo / mono-poste local. **L'ADR n'est pas modifiée par cette passe.**
- **Baseline d'entrée mesurée localement** : **1505** tests verts
  (Domain 649 · Application 617 · App 239), 0 échec, 0 ignoré, 0 avertissement de build.

---

## 1. Périmètre exact

### Fichiers créés

| Fichier | Rôle |
|---|---|
| `src/MMV.Infrastructure/Configuration/DatabaseProvider.cs` | Énumération fermée : `Sqlite`, `PostgreSql`. |
| `src/MMV.Infrastructure/Configuration/DatabaseProviderOptions.cs` | Options résolues (`Provider`, `ConnectionString`). |
| `src/MMV.Infrastructure/Configuration/DatabaseProviderResolver.cs` | Résolution depuis l'environnement **+** configuration EF centralisée. |
| `src/MMV.Infrastructure/Configuration/DatabaseConfigurationException.cs` | Blocage explicite d'une configuration invalide. |
| `tests/MMV.Domain.Tests/Configuration/DatabaseProviderResolverTests.cs` | 24 tests unitaires (aucune connexion réelle). |
| `docs/implementation/P4-3-multi-provider-foundation-report.md` | Ce rapport. |

### Fichiers modifiés

| Fichier | Nature de la modification |
|---|---|
| `src/MMV.App/App.axaml.cs` | Résolution unique du provider ; DI via le configurateur central ; garde-fou de démarrage serveur ; chemin SQLite résolu **uniquement** si SQLite. |
| `src/MMV.Infrastructure/Data/OpticDbContext.cs` | `OnConfiguring` passe par le configurateur central (garde `IsConfigured` **inchangée**). |
| `src/MMV.Infrastructure/Data/OpticDbContextFactory.cs` | Passe par le configurateur central, **explicitement figée sur SQLite**. |
| `src/MMV.Infrastructure/MMV.Infrastructure.csproj` | Ajout du seul package `Npgsql.EntityFrameworkCore.PostgreSQL`. |
| `docs/architecture/P4-multi-poste-roadmap.md` | Mise à jour d'état minimale — **telle qu'écrite lors de la passe locale**, où P4-3 était alors implémentée localement et non close. Cet état a depuis été remplacé par la clôture enregistrée (§14). |

**Aucun fichier hors de ce périmètre n'a été modifié.** Aucun fichier `MMV.Domain` ou `MMV.Application`
n'est touché. Les dossiers non suivis `design-handoff/`, `design/` et `docs/ui/` sont restés **intacts**.

---

## 2. Les cinq occurrences SQLite ne jouent pas le même rôle

L'audit P4-0 §8 recensait « 4 sites `UseSqlite` ». L'inspection de cette passe en distingue **cinq**,
de **trois natures différentes** — distinction structurante pour P4-3 :

### 2.1 Sites réellement multi-provider — **centralisés** (3)

1. **`src/MMV.App/App.axaml.cs`** — enregistrement DI principal de `OpticDbContext` (composition root).
2. **`src/MMV.Infrastructure/Data/OpticDbContext.cs`** — `OnConfiguring`, **uniquement** lorsque les options
   ne sont pas déjà configurées par le conteneur ou par un test.
3. **`src/MMV.Infrastructure/Data/OpticDbContextFactory.cs`** — factory design-time. Centralisée sur le même
   configurateur, mais **volontairement figée sur SQLite** (cf. §7).

### 2.2 Site actif conservé **SQLite-only** (1)

4. **`src/MMV.App/App.axaml.cs`** — `DbContext` **ad hoc** construit pour `LegacyDatabaseRecoveryService`,
   qui récupère l'ancien **fichier local** `mmv-optic.db` (P2A-1B).

   Ce site **n'est pas devenu multi-provider** et ne doit pas le devenir : il ouvre un **fichier SQLite
   historique**, pas la base applicative courante. Il n'est atteint **que** lorsque le provider sélectionné
   est SQLite (le garde-fou du §5 s'exécute avant). `LegacyDatabaseRecoveryService` n'a **pas** été
   transformé en service multi-provider.

### 2.3 Site inerte — **non modifié** (1)

5. **`src/MMV.Infrastructure/DependencyInjection.cs`** — `AddInfrastructure` n'a **aucun appelant**, ni en
   production ni dans les tests (constat P2B-2J, toujours vrai). **Ce fichier n'a pas été touché.** P4-3
   n'a pas servi de prétexte à le nettoyer, le réactiver ou le rendre multi-provider ; il conserve donc
   son `UseSqlite` local, sans effet d'exécution.

---

## 3. Modèle de configuration

### 3.1 Variables d'environnement

| Variable | Rôle | Obligatoire |
|---|---|---|
| `MMV_DATABASE_PROVIDER` | Sélection du provider. | Non — **absente ou vide ⇒ SQLite**. |
| `MMV_DATABASE_CONNECTION_STRING` | Chaîne de connexion **serveur** (secret). | **Oui pour PostgreSQL**, ignorée pour SQLite. |
| `MMV_DATABASE_PATH` | **Inchangée** — chemin du fichier SQLite (P2A-1A). | Non. |

`MMV_DATABASE_PATH` reste la propriété exclusive de `SqliteDatabasePathResolver` : **P4-3 ne la remplace pas
et ne la duplique pas.**

### 3.2 Valeurs reconnues

- **SQLite** : `sqlite`.
- **PostgreSQL** : `postgresql`, `postgres`, `npgsql`.
- Comparaison **insensible à la casse**, valeurs **`Trim`ées** — conformément à la convention des resolvers
  existants (`SqliteDatabasePathResolver`, `SeedOptionsResolver`).
- **Toute autre valeur lève `DatabaseConfigurationException`.** Une **faute de frappe ne retombe jamais
  silencieusement sur SQLite** : le cas contraire ferait démarrer un poste sur une base locale isolée en
  croyant être connecté au serveur.

### 3.3 Secrets

La chaîne de connexion provient **exclusivement** d'une variable d'environnement. **Aucune chaîne de
connexion réelle, aucun identifiant, aucun exemple complet exploitable n'est écrit dans le dépôt.** Les
messages d'erreur **nomment** la variable fautive mais **n'en restituent jamais la valeur**. La seule chaîne
présente dans les sources est le littéral de test `Host=localhost;Database=mmv-p4-3-fake`, **sans aucun
identifiant**, utilisé uniquement pour prouver la sélection du provider EF.

---

## 4. Traitement SQLite — comportement **inchangé**

- Provider par défaut, y compris en l'absence totale de configuration.
- **Format de connexion identique** : `Data Source=<chemin résolu>`, produit par
  `SqliteDatabasePathResolver.GetConnectionString`.
- **Chemin par défaut, mode de cache et options : inchangés.** Aucune option n'a été ajoutée ni retirée.
- `EnsureDirectoryExists` reste appelée **exactement aux mêmes endroits qu'avant** (`OnConfiguring`,
  factory design-time) : le configurateur central ne l'appelle pas, afin de ne pas introduire un effet de
  bord là où il n'existait pas (composition root).
- **Sémantique des tests SQLite inchangée** : les tests qui fournissent `UseSqlite(connection)` continuent
  de faire autorité (cf. §6).

## 5. Traitement PostgreSQL — sélectionnable, démarrage **volontairement bloqué**

- `UseNpgsql(<chaîne résolue>)` — **sélection du provider EF, rien d'autre.**
- **Aucun** retry, **aucun** mapping `DateTime`, **aucun** mapping monétaire, **aucune** migration serveur,
  **aucune** option non mesurée : tout cela appartient à P4-4/P4-5 et reste **ouvert**.
- **Garde-fou de démarrage (intentionnel).** Lorsque le provider n'est pas SQLite, `App.ConfigureServices`
  lève une `DatabaseConfigurationException` **avant toute opération dépendante de la base**. En conséquence :
  - `LegacyDatabaseRecoveryService` n'est **pas** exécuté et son `DbContext` SQLite ad hoc n'est **pas** créé ;
  - `SqliteDatabaseManager.PrepareDatabase` n'est **pas** appelé ;
  - **`Migrate()` n'est jamais exécuté avec la chaîne SQLite contre PostgreSQL.**
- Le garde-fou est placé **avant** le bloc `try` de préparation de base, précisément pour qu'il **ne puisse
  pas être avalé** par le `catch (Exception)` générique existant : un repli silencieux sur SQLite est ainsi
  structurellement impossible.
- **Ce blocage ne signifie pas que la sélection PostgreSQL a échoué.** Le provider est correctement
  sélectionné côté EF ; ce qui manque est la **chaîne de préparation et de migrations serveur**
  (**P4-5/P4-6**). Le message d'erreur l'indique explicitement.

## 6. `OpticDbContext.OnConfiguring`

- La garde **`if (!optionsBuilder.IsConfigured)` est préservée telle quelle**.
- **Options absentes** : le provider est résolu, le chemin SQLite n'est résolu **que si** SQLite est
  sélectionné, puis le configurateur central est appelé.
- **Options déjà configurées** (DI ou tests) : **rien n'est remplacé, aucun provider n'est reconfiguré**.
  Un test fournissant `UseSqlite(connection)` reste maître de sa connexion — vérifié par un test dédié qui
  contrôle que la chaîne de connexion fournie survit et n'est pas remplacée par le fichier par défaut.

## 7. Factory design-time — **figée sur SQLite**

`OpticDbContextFactory` utilise le configurateur central mais passe **explicitement**
`DatabaseProviderOptions.Sqlite`. Elle **ne lit ni `MMV_DATABASE_PROVIDER` ni
`MMV_DATABASE_CONNECTION_STRING`**.

Raison, documentée dans le code : la chaîne de migrations existante est **SQLite**, et la CI exécute
`dotnet ef migrations has-pending-model-changes` **sans configuration serveur**. Le passage design-time à
PostgreSQL appartient à la **chaîne de migrations serveur P4-5**. Le contrôle EF actuel reste donc
fonctionnel et vert.

## 8. Package ajouté

| Package | Version demandée | Version résolue | Projet |
|---|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `8.0.11` | **`8.0.11`** | **`src/MMV.Infrastructure` uniquement** |

- Dépendance transitive pertinente introduite : **`Npgsql 8.0.6`**.
- **Versions EF Core inchangées** : `Microsoft.EntityFrameworkCore*` restent en **`8.0.27`**
  (aucune montée ni descente de version ; `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11` exige EF Core ≥ 8.0.11,
  contrainte déjà satisfaite).
- **Aucun pin de sécurité existant n'a été modifié** (`System.Text.Json 8.0.6`, `SQLitePCLRaw.bundle_e_sqlite3 3.0.0`).

**Précision d'honnêteté technique** : `Npgsql` n'est une **référence directe** que dans `MMV.Infrastructure`.
Il apparaît **transitivement** dans `MMV.App` et dans les projets de test qui référencent `MMV.Infrastructure`
— c'est le mécanisme normal de propagation NuGet, sans lequel l'assembly ne serait pas disponible à
l'exécution. **`MMV.Domain` et `MMV.Application` n'ont, eux, aucun package EF / SQLite / Npgsql**, ni direct
ni transitif (vérifié par `dotnet list package --include-transitive` sur les 7 projets).

## 9. Garde-fous d'architecture

| Contrôle | Résultat |
|---|---|
| `MMV.Domain` référence Infrastructure / EF / SQLite / Npgsql | **Non** — aucun package, aucune référence projet. |
| `MMV.Application` référence Infrastructure / EF / SQLite / Npgsql | **Non** — seule référence projet : `MMV.Domain`. |
| `Npgsql` référencé directement ailleurs qu'en Infrastructure | **Non**. |
| `PersistenceException` / `PersistenceErrorCategory` modifiés | **Non** — non touchés. |
| Tests d'architecture existants | **Tous verts** (`ApplicationArchitectureTests`, `NotificationsArchitectureTests`, `UsersApplicationArchitectureTests`, `AppUiPersistenceGuardrailTests`, …). |

---

## 10. Tests ajoutés — un par comportement

`tests/MMV.Domain.Tests/Configuration/DatabaseProviderResolverTests.cs` — **24 cas découverts**, tous verts.
Environnement **injecté par dictionnaire** ; l'environnement du processus **n'est jamais modifié**
(déterminisme). **Aucun test n'ouvre de connexion PostgreSQL réelle** ; aucun serveur ni conteneur requis.

| # | Comportement | Cas |
|---|---|---|
| 1 | Variable provider **absente** ⇒ `Sqlite` (et `ConnectionString` nulle) | 1 |
| 2 | Variable provider **vide / blanche** ⇒ `Sqlite` | 2 |
| 3 | `sqlite` ⇒ `Sqlite`, **casse et espaces tolérés** | 3 |
| 4 | `postgresql` + alias `postgres` / `npgsql` ⇒ `PostgreSql`, **casse et espaces tolérés** | 5 |
| 5 | Provider **inconnu** ⇒ `DatabaseConfigurationException`, **jamais de repli SQLite** | 3 |
| 6 | PostgreSQL **sans** `MMV_DATABASE_CONNECTION_STRING` ⇒ exception | 1 |
| 7 | PostgreSQL avec chaîne **blanche** ⇒ exception | 2 |
| 8 | PostgreSQL avec chaîne **factice** ⇒ options résolues **sans connexion** | 1 |
| 9 | SQLite **ignore** la chaîne serveur (chemin laissé à `SqliteDatabasePathResolver`) | 1 |
| 10 | Configuration SQLite ⇒ provider EF `Microsoft.EntityFrameworkCore.Sqlite` | 1 |
| 11 | Configuration SQLite ⇒ **chemin fourni respecté** (`Data Source=…`) | 1 |
| 12 | Configuration PostgreSQL ⇒ provider EF `Npgsql.EntityFrameworkCore.PostgreSQL` | 1 |
| 13 | `Configure` PostgreSQL **sans** chaîne ⇒ exception | 1 |
| 14 | Options **déjà configurées** ⇒ **non écrasées** par `OnConfiguring` | 1 |
| | **Total** | **24** |

**Aucun test existant n'a été supprimé, modifié, désactivé ou ignoré** pour faire passer la suite.

### Comptage

| | Domain | Application | App | **Total** |
|---|---|---|---|---|
| **Avant P4-3** (mesuré localement) | 649 | 617 | 239 | **1505** |
| **Après P4-3** | **673** | 617 | 239 | **1529** |
| Delta | **+24** | 0 | 0 | **+24** |

L'augmentation correspond **exactement** aux 24 nouveaux cas découverts par le runner ; les **1505** tests
préexistants sont **tous présents et verts**. **0 échec, 0 ignoré.**

---

## 11. Résultats de validation

| Commande | Résultat |
|---|---|
| `dotnet restore MMV.sln` | **Succès.** |
| `dotnet build MMV.sln -c Debug --no-restore` | **0 erreur, 0 avertissement.** |
| `dotnet test MMV.sln -c Debug --no-build` | **1529 réussis, 0 échec, 0 ignoré** (Domain 673 · Application 617 · App 239). |
| `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | **`No changes have been made to the model since the last migration.`** — **aucune migration créée**. |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **Aucune vulnérabilité** sur les 7 projets — ni Critical, ni High, ni Moderate, ni Low. |
| `git diff --check` | **Aucune anomalie** (pas d'espace en fin de ligne, pas de conflit résiduel). |

---

## 12. Limites — ce que P4-3 ne prouve **pas**

1. **PostgreSQL est sélectionnable dans la composition EF** — et **rien de plus**.
2. **Le démarrage de production sur PostgreSQL reste volontairement bloqué** jusqu'à la chaîne de
   préparation et de migrations serveur (**P4-5/P4-6**).
3. **Aucune compatibilité applicative complète n'est revendiquée.** Restent **ouverts et non traités** :
   - la conversion des valeurs **`DateTime`** (`Kind=Local` refusé par PostgreSQL via le chemin EF normal —
     constat du Lot D, **non résolu**) ;
   - le **mapping monétaire** (`REAL` perd de la valeur sur les deux candidats — constat Lot A) ;
   - les **index filtrés booléens** PostgreSQL ;
   - la **classification d'erreurs** (`PersistenceErrorMapper` reste **entièrement couplé** à `SqliteException`) ;
   - le **niveau d'isolation** et le **retry** transactionnels.
4. **Aucune migration serveur** n'existe : la chaîne de migrations reste **SQLite**, et le design-time y est
   volontairement figé.
5. **Aucun travail P4-4 ni P4-5 n'a été engagé** par cette passe.
6. **Aucune primitive applicative n'a été exercée contre un serveur PostgreSQL réel** dans cette passe.
7. **La clôture de P4-3 est administrative, pas une preuve de compatibilité.** Sa condition — revue humaine
   **+** commit **+** CI GitHub Actions verte sur le SHA exact — est **satisfaite** (§14) : **P4-3 est close.**
   Cette clôture atteste **l'enregistrement de la fondation**, et **rien de plus** : **la V1 multi-poste n'est
   toujours pas `GO`**, et **`PersistenceErrorMapper` reste entièrement à traiter en P4-4**.

---

## 13. Vérifications finales du diff *(passe locale)*

> Les points ci-dessous ont été vérifiés **sur le diff de la passe locale d'implémentation**, avant tout
> enregistrement. Ils restent vrais du contenu committé — l'enregistrement lui-même est décrit au §14.

- ✅ **Aucun secret**, aucune chaîne de connexion réelle, aucun identifiant.
- ✅ **Aucun repli PostgreSQL → SQLite**, en aucun point du code.
- ✅ **Aucun appel aux migrations SQLite** lorsque PostgreSQL est sélectionné.
- ✅ **Aucun changement de mapping**, **aucun changement de migration**.
- ✅ **Aucun changement de `PersistenceErrorMapper`**.
- ✅ **Aucun changement `MMV.Domain` / `MMV.Application`.**
- ✅ **Aucune implémentation P4-4 / P4-5.**
- ✅ **Aucun fichier UI / design touché** (`design-handoff/`, `design/`, `docs/ui/` restent non suivis et intacts).
- ✅ **`src/MMV.Infrastructure/DependencyInjection.cs` non modifié** (site inerte).
- ✅ *(historique)* Lors de la passe locale initiale, **aucun staging, aucun commit ni push n'avait été
  effectué** — l'énoncé était exact **à cette date** et ne décrit **plus** l'état du dépôt : le contenu a
  depuis été enregistré par le commit `b312a6c` (§14).

---

## 14. Enregistrement officiel — commit et CI

La condition de clôture de P4-3 — **revue humaine + commit + CI GitHub Actions verte sur le SHA exact** — est
**satisfaite**.

| Élément | Valeur |
|---|---|
| **Commit** | **`b312a6c901b1e620a3694a32ce0cd14e2f0fc707`** |
| **Parent** | `724ffecc61a891b150193d838315edc61e238a1a` |
| **Message** | `feat(P4-3): add multi-provider infrastructure foundation` |
| **Fichiers** | **11** — **5 modifiés** + **6 ajoutés** |
| **CI** | run **`30823399148`** — <https://github.com/iamzekhnini15/mmv-desktop/actions/runs/30823399148> |
| `event` | `push` |
| `headBranch` | `p4-multi-poste` |
| `headSha` | **`b312a6c901b1e620a3694a32ce0cd14e2f0fc707`** (SHA **exact** du commit) |
| `status` | `completed` |
| **`conclusion`** | **`success`** |
| **Tests** | **1529** — Domain **673** · Application **617** · App **239** |
| Échecs | **0** |
| Ignorés | **0** |
| **Build** | **0 erreur, 0 avertissement** |
| **Audit** | **aucune vulnérabilité** |
| **Contrôle EF** | **aucun changement de modèle** |
| **Migrations** | **aucune créée** |

**Portée exacte de cette CI — à ne pas surinterpréter.** La CI a rejoué les **1529 tests de `MMV.sln`**. Elle
**n'a exécuté ni les tests E18**, **ni le harness `spikes/P4.ProviderComparison`**, qui reste **hors
`MMV.sln`** : les preuves serveur PostgreSQL / SQL Server du spike demeurent des **preuves locales** et **ne
doivent pas** être présentées comme reproduites par la CI.

---

## 15. Verdict

```
P4_3_IMPLEMENTATION_LOCAL          = PASS
P4_3_IMPLEMENTATION_COMMIT_AND_CI  = PASS
P4_3                               = CLOSE
P4_4                               = READY — NOT STARTED
V1 MULTI-POSTE                     = NOT GO
```

La **clôture documentaire** de P4-3 devient **définitive** après la **CI verte du présent commit de
documentation sur son SHA exact**.
