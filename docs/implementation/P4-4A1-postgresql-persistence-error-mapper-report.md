# P4-4A1 — Mapper d'erreurs de persistance PostgreSQL

> **État : IMPLEMENTED LOCALLY — PENDING HUMAN REVIEW / COMMIT / CI**
> Aucun `git add`, aucun commit, aucun push n'a été effectué par ce lot.

---

## 1. Base Git

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| HEAD de base | `b84c7e3cedf21e7ab36a09d19ad6235c17685a69` |
| `origin/p4-multi-poste` | `b84c7e3cedf21e7ab36a09d19ad6235c17685a69` (identique) |
| Parent | `669e48c9b90751e08ff9d507313bd541536766df` |
| Commit de preuve P4-4A0 | `b84c7e3` — `test(P4-4A0): record PostgreSQL exception shape evidence` |
| CI P4-4A0 | `33411724068` — SUCCESS sur le SHA exact |
| Baseline de tests avant ce lot | **1529** (Domain 673 · Application 617 · App 239), 0 échec, 0 ignoré |

Périmètre de départ vérifié : aucun fichier suivi modifié, aucun fichier *staged*, non suivis
limités à `design-handoff/`, `design/`, `docs/ui/`. Les cinq fichiers P4-4A0 sont bien suivis et
présents dans HEAD.

---

## 2. Objectif du lot

Rendre `PersistenceErrorMapper` capable de classer les erreurs **PostgreSQL** :

- sans **aucune** régression SQLite ;
- sans exposer `Npgsql` à `MMV.Domain` ni à `MMV.Application` ;
- de façon **intégralement testable sans serveur PostgreSQL**.

Ce lot ne modifie **aucune primitive métier** et n'introduit **aucune** politique de production :
ni timeout, ni `lock_timeout`, ni `statement_timeout`, ni `CommandTimeout`, ni retry, ni migration,
ni mapping `DateTime`, ni mapping monétaire, ni filtre d'index, ni politique transactionnelle
P4-4B.

---

## 3. Fichiers modifiés / créés

Exactement **quatre** fichiers.

| Statut | Fichier |
|---|---|
| `M` | `src/MMV.Infrastructure/Persistence/PersistenceErrorMapper.cs` (+216 / −21) |
| `A` | `tests/MMV.Domain.Tests/Persistence/PersistenceErrorMapperPostgreSqlTests.cs` (524 lignes, 33 tests) |
| `A` | `tests/MMV.Application.Tests/Architecture/ProviderNeutralityArchitectureTests.cs` (69 lignes, 3 tests) |
| `A` | `docs/implementation/P4-4A1-postgresql-persistence-error-mapper-report.md` (ce rapport) |

Aucun `.csproj`, aucun `.sln`, aucun workflow, aucune migration, aucun snapshot EF, aucun fichier
sous `spikes/` ni `docs/architecture/` n'a été touché. **Aucun paquet NuGet n'a été ajouté** :
`Npgsql 8.0.6` était déjà disponible transitivement via
`Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11`, référencé par `MMV.Infrastructure` depuis P4-3.

---

## 4. Algorithme final du mapper

L'API publique est **inchangée** : `public static Exception Map(Exception exception)`, classe
statique, mêmes types `PersistenceException` / `PersistenceErrorCategory` du Domain, helper
`FindInChain<T>` conservé tel quel (son comportement — parcours de la chaîne `InnerException` — était
déjà suffisant).

Aucune abstraction DI n'a été créée. Aucune `IPersistenceErrorStrategy`. **Le provider n'est jamais
détecté via `ProviderName`** : la classification repose *uniquement* sur la structure de
l'exception.

Le constat central de P4-4A0 gouverne l'algorithme :

> **Le type externe ne doit jamais être utilisé comme classification.**

Une même panne apparaît, selon le chemin, sous `DbUpdateException`, sous
`InvalidOperationException`, ou nue. Le mapper parcourt donc **toute** la chaîne `InnerException`
pour chaque signal recherché.

### Règle absolue — aucune décision par le texte

Ni `Message`, ni `Detail`, ni `Hint`, ni `Where`, ni `Routine`, ni `File`, ni `ConstraintName` ne
sont lus pour choisir une catégorie. Seuls des signaux **structurés** sont utilisés : type
d'exception, `SqlState`, `CancellationToken.IsCancellationRequested`.

---

## 5. Ordre exact des branches (contrat)

| # | Branche | Résultat |
|---|---|---|
| A | `exception is PersistenceException` | **la même instance**, inchangée |
| B | annulation cliente **réellement demandée** | **l'exception d'origine**, inchangée |
| C | `PostgresException` trouvée dans la chaîne | `PersistenceException` classée par `SqlState` |
| D | `SqliteException` trouvée dans la chaîne | `PersistenceException` — classification historique inchangée |
| E | `NpgsqlException` présente, sans `SqlState` exploitable | `PersistenceException` classée par forme structurelle |
| F | `DbUpdateException` présente, aucun signal provider | `PersistenceException` / `Unknown`, message historique |
| G | tout le reste | **l'exception d'origine**, inchangée |

Cet ordre est un **contrat**. Deux points en dépendent directement :

- **B avant C** : la forme réelle d'une annulation utilisateur est
  `OperationCanceledException -> PostgresException 57014`. Sans cette priorité, une annulation
  demandée par l'utilisateur deviendrait une erreur de persistance.
- **C et E avant F** : `DbUpdateException` est une enveloppe, pas un diagnostic. E20 a mesuré
  qu'un **timeout d'écriture** est lui aussi enveloppé par une `DbUpdateException` ; la traiter en
  premier écraserait le signal provider situé plus bas dans la chaîne.

`PostgresException` étant **scellée** et dérivant de `NpgsqlException`, la branche C capte
nécessairement tous les cas à `SqlState` avant que la branche E ne soit atteinte.

---

## 6. Matrice PostgreSQL (branche C — `SqlState` structuré uniquement)

| `SqlState` | Nom PostgreSQL | Catégorie | Origine |
|---|---|---|---|
| `23505` | `unique_violation` | `UniqueConstraint` | P4-1 E6 ; re-mesuré via EF Core en P4-4A0/E20 |
| `23503` | `foreign_key_violation` | `ConstraintViolation` | P4-1 E6/E17 ; re-mesuré via EF Core en P4-4A0/E20 |
| `23502` | `not_null_violation` | `ConstraintViolation` | P4-1 E6 ; re-mesuré via EF Core en P4-4A0/E20 |
| `23514` | `check_violation` | `ConstraintViolation` | P4-4A0/E19 |
| `3D000` | `invalid_catalog_name` | `ConnectionFailure` | P4-1 E6 — **NON re-provoqué en P4-4A0/E20** |
| `40P01` | `deadlock_detected` | `Concurrency` | P4-1 E12 — **NON re-provoqué en P4-4A0/E20** |
| `55P03` | `lock_not_available` | `DatabaseBusy` | P4-4A0/E19 |
| `57014` | `query_canceled` | `DatabaseBusy` — **uniquement après le garde d'annulation (B)** | P4-1 E6/E13 ; re-mesuré P4-4A0/E19/E20 |
| `25P02` | `in_failed_sql_transaction` | `Unknown` — **jamais `Concurrency`** | P4-1 E12/E13 — **NON re-provoqué en P4-4A0/E20** |
| *tout autre* | — | `Unknown` | aucune catégorie attribuée sur hypothèse |

**Codes délibérément NON ajoutés** : `40001` (`serialization_failure`), `28P01`
(`invalid_password`), et tout autre code non prouvé ou non décidé. Les classer aurait été une
hypothèse, pas une mesure.

`25P02` reste `Unknown` par décision explicite : c'est la **conséquence** d'une erreur antérieure
(PostgreSQL avorte la transaction après toute erreur), jamais une cause. La classer en `Concurrency`
serait faux.

`ConstraintName`, `TableName`, `ColumnName` et `SchemaName` restent des propriétés structurées
accessibles dans la chaîne d'origine — mais **la catégorie n'est jamais choisie d'après le texte de
`ConstraintName`**.

---

## 7. Comportement des erreurs sans `SqlState` (branche E)

Cette branche n'est atteinte **que si une `NpgsqlException` est présente quelque part dans la
chaîne**. C'est le point critique de sûreté : une `IOException`, une `SocketException` ou une
`TimeoutException` **arbitraire** — sans `NpgsqlException` — ne doit jamais devenir une erreur de
persistance, sous peine de transformer des pannes applicatives sans rapport.

Une fois la présence de `NpgsqlException` établie, dans cet ordre :

| Test sur la chaîne | Catégorie | Forme mesurée correspondante |
|---|---|---|
| contient `SocketException` | `ConnectionFailure` | `NpgsqlException -> SocketException` (connexion refusée) |
| sinon contient `IOException` | `ConnectionFailure` | `NpgsqlException -> IOException -> SocketException` et `NpgsqlException -> EndOfStreamException` (perte de connexion) |
| sinon contient `TimeoutException` | `DatabaseBusy` | `NpgsqlException -> TimeoutException` (command timeout client ; lock timeout client — même forme) |
| sinon | `Unknown` | `NpgsqlException` nue |

`EndOfStreamException` **dérive** d'`IOException` : les deux formes de perte de connexion mesurées
sont donc couvertes par un seul test de type.

**Discriminants explicitement ignorés** : `ErrorCode`, `IsTransient`, `Connection.State`. Aucun
`Message` n'est parsé.

`Connection.State` **dépend du chemin d'exécution** et n'est donc pas un discriminant stable :

- **E19** (chemins ADO.NET) a observé des états **différents selon le scénario** ;
- **E20** (chemins EF Core) a observé `Closed` / `Closed` sur les chemins mesurés.

`ErrorCode` et `IsTransient` ne sont pas retenus non plus. La classification reste fondée
exclusivement sur les **types structurés** présents dans la chaîne et sur le `SqlState`.

Ces états observés ne sont **pas** transposés en logique de production : le mapper ne lit jamais
`Connection.State`.

Dans tous les cas, la `PersistenceException` produite conserve l'exception **originale** en
`InnerException`.

---

## 8. Garde d'annulation (branche B)

```
pour chaque exception de la chaîne InnerException :
    si c'est une OperationCanceledException
       ET que son CancellationToken.IsCancellationRequested == true
    alors -> retourner l'exception ORIGINALE, inchangée
```

- Aucune `PersistenceException` n'est créée.
- Le contrôle précède **toute** recherche de `PostgresException`.
- `TaskCanceledException` dérivant d'`OperationCanceledException`, la famille entière est couverte.
- Le texte du message n'est jamais examiné.

Motif prouvé en P4-4A0 : `OperationCanceledException -> PostgresException 57014` avec
`CancellationToken.IsCancellationRequested = true` **est** la forme réelle d'une annulation
utilisateur. Un `57014` direct, **sans** annulation cliente demandée, est un cas différent
(annulation subie côté serveur) et reste classé `DatabaseBusy`.

Conséquence en aval : `EfTransactionRunner` compare par référence (`ReferenceEquals(mapped, ex)`) et
relance donc l'annulation via `throw;`, pile d'appels préservée.

---

## 9. Préservation SQLite

La classification SQLite est **fonctionnellement identique**. Inchangés : les codes (primaires 19,
5, 6, 10, 14 ; étendus 2067, 1555), les catégories, les messages, la priorité du code étendu sur le
code primaire, le comportement BUSY/LOCKED, le comportement IOERR/CANTOPEN, le fallback.

Le seul changement de forme est l'extraction des libellés en constantes privées, afin de les
partager entre providers. **Preuve de non-altération** : la comparaison automatisée de l'ensemble
des littéraux de chaîne entre `HEAD:PersistenceErrorMapper.cs` et la version finale donne un
ensemble vide de littéraux disparus — chaque message historique survit **au caractère près**, y
compris son découpage en fragments concaténés.

Les tests SQLite préexistants (`EfTransactionRunnerTests`) passent **sans aucune modification** ;
aucun ancien test du mapper n'a été supprimé ni modifié.

---

## 10. Préservation des exceptions génériques

L'invariant historique est intact : une exception non liée à la persistance est renvoyée
**inchangée** (même instance). Aucune exception ne devient `PersistenceException` du fait de son
**type** ou de son **message**. Cas verrouillés par test :

- `TimeoutException` seule, sans `NpgsqlException` → inchangée ;
- `IOException` seule, sans `NpgsqlException` → inchangée ;
- exception générique dont le message contient
  `"23505 duplicate timeout socket connection refused"` → inchangée (interdiction explicite du
  parsing de `Message`) ;
- `NpgsqlException` dont le message annonce `"23505 duplicate"` mais dont la cause réelle est un
  `TimeoutException` → `DatabaseBusy`, **jamais** `UniqueConstraint`.

---

## 11. Garde d'architecture Npgsql (O14)

`tests/MMV.Application.Tests/Architecture/ProviderNeutralityArchitectureTests.cs` verrouille O14 par
réflexion sur les assemblies **réellement référencés** (`GetReferencedAssemblies()`) :

| Test | Objet |
|---|---|
| `ReferenceListsAreNotEmpty` | contrôle positif — évite qu'une liste vide rende les gardes vraies pour une mauvaise raison |
| `Domain_DoesNotReference_NpgsqlProvider` | `MMV.Domain` ne référence aucun assembly provider Npgsql |
| `Application_DoesNotReference_NpgsqlProvider` | `MMV.Application` ne référence aucun assembly provider Npgsql |

Prédicat de rejet : nom d'assembly **égal à** `"Npgsql"` **ou commençant par** `"Npgsql."` — ce qui
couvre le pilote ADO.NET comme `Npgsql.EntityFrameworkCore.PostgreSQL`. La garde couvre bien **les
deux** assemblies. Les gardes SQLite/EF/Avalonia existantes de `ApplicationArchitectureTests` ne sont
pas dupliquées : elles ne couvraient ni Npgsql, ni l'assembly Domain.

Ni `MMV.Domain` ni `MMV.Application` n'ont été modifiés. Aucun paquet supplémentaire.

**Contrôle de surface (étape 8)** : recherche de `Npgsql`, `PostgresException`, `NpgsqlException`
dans `src/MMV.Domain/**` et `src/MMV.Application/**` → **0 occurrence**.

**Contrôle des politiques interdites** sur l'ensemble du diff P4-4A1 —
`lock_timeout`, `statement_timeout`, `CommandTimeout`, `EnableRetryOnFailure`, `retry`, `Migrate`,
`EnsureCreated`, `DateTime`, `HasColumnType`, `HasFilter` → **0 occurrence** pour chacun. Aucune
nouvelle politique de ces catégories n'est introduite.

---

## 12. Nombre exact de nouveaux tests

**36 nouveaux tests**, tous exécutés sans serveur.

| Fichier | Tests |
|---|---|
| `PersistenceErrorMapperPostgreSqlTests.cs` | **33** |
| `ProviderNeutralityArchitectureTests.cs` | **3** |

Répartition des 33 tests du mapper :

- **P1–P25 (25 tests)** — la totalité des tests PostgreSQL minimaux exigés : matrice `SqlState`
  (P1–P10), garde d'annulation (P11), formes sans `SqlState` (P12–P15, P18), parcours profonds
  mesurés en E20 (P16, P17), indépendance au type externe (P19, P20), récupérabilité du
  `ConstraintName` (P21), préservation des exceptions non-provider (P22–P24) et interdiction du
  parsing de `Message` côté provider (P25).
- **S1–S8 (8 tests)** — non-régression SQLite : **exactement les branches sans test direct
  préexistant**, à savoir extended `1555` (clé primaire), primaire `19` générique, `LOCKED 6`,
  `IOERR 10`, `CANTOPEN 14`, autre `SqliteException` → `Unknown`, `DbUpdateException` sans exception
  provider → `Unknown`, et passthrough de `PersistenceException`.

Les branches SQLite **déjà** couvertes directement par `EfTransactionRunnerTests` n'ont pas été
dupliquées : extended `2067` (unique), `BUSY 5`, `DbUpdateException -> SqliteException`, et
« exception générique inchangée ».

Toutes les exceptions Npgsql/Postgres sont **fabriquées en mémoire** avec la véritable API
Npgsql 8.0.6, inspectée par réflexion avant écriture :

- `PostgresException(messageText, severity, invariantSeverity, sqlState, …, constraintName, …)`
  — type **scellé**, dérivant de `NpgsqlException` ; il n'accepte pas d'`InnerException`, l'imbrication
  se fait donc par les enveloppes ;
- `NpgsqlException(message, innerException)`.

Aucun serveur, aucun Docker, aucune variable d'environnement, aucun test ignoré, aucun paquet ajouté.
Plusieurs messages synthétiques sont **délibérément trompeurs** : si le mapper venait à lire
`Message`, ces tests échoueraient.

---

## 13. Baseline avant / après

| Projet | Avant (`b84c7e3`) | Après | Δ |
|---|---:|---:|---:|
| `MMV.Domain.Tests` | 673 | **706** | +33 |
| `MMV.Application.Tests` | 617 | **620** | +3 |
| `MMV.App.Tests` | 239 | **239** | 0 |
| **Total** | **1529** | **1565** | **+36** |

`1529 + 36 = 1565` — **0 échec, 0 ignoré**.

---

## 14. Validations

| Contrôle | Commande | Résultat |
|---|---|---|
| Restore | `dotnet restore MMV.sln` | succès — `All projects are up-to-date for restore.` |
| Build | `dotnet build MMV.sln -c Debug --no-restore` | **0 erreur, 0 avertissement** |
| Tests | `dotnet test MMV.sln -c Debug --no-build` | **1565 réussis, 0 échec, 0 ignoré** |
| Outils | `dotnet tool restore` | `dotnet-ef 8.0.27` restauré |
| Modèle EF | `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | **`No changes have been made to the model since the last migration.`** |
| Migrations | `git status --short src/MMV.Infrastructure/Migrations/` | aucune migration créée, aucun snapshot modifié |
| Audit | `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité** sur les 7 projets |
| Espaces | `git diff --check` | propre (aucune sortie) |

Les trois fichiers sources produits ont été normalisés en **CRLF**, conformément au reste du dépôt
(`.gitattributes` : `* text=auto`).

---

## 15. Limites

### Ce que P4-4A1 **NE PROUVE PAS**

P4-4A1 est un lot de **classification d'erreurs**, testé **hors serveur**. Il ne prouve **aucun** des
points suivants :

- ❌ compatibilité PostgreSQL complète de MMV ;
- ❌ comportement transactionnel `25P02` de bout en bout ;
- ❌ les 14 primitives métier ;
- ❌ l'installation Windows native ;
- ❌ la migration serveur ;
- ❌ le mapping monétaire (`REAL` perd de la valeur sur les deux moteurs — toujours ouvert) ;
- ❌ le mapping `DateTime` (`DateTime.Now`, `Kind=Local` : PostgreSQL le REFUSE par le chemin EF
  normal et le décale silencieusement de 2 h en SQL brut — toujours ouvert) ;
- ❌ les filtres d'index PostgreSQL ;
- ❌ toute politique de timeout ;
- ❌ tout retry ;
- ❌ la résilience réseau multi-poste.

### Précisions

- **E19 / E20 restent `EXECUTED_SPIKE LOCAL`**, hors `MMV.sln`. Ils ne sont pas exécutés en CI et
  ne le deviennent pas avec ce lot.
- **Les nouveaux tests P4-4A1 sont des tests sans serveur, inclus dans `MMV.sln`** et donc exécutés
  en CI. Ils verrouillent la logique de classification, pas le comportement d'un serveur réel.
- **P4-4A1 ne configure aucun timeout de production.** Les catégories `DatabaseBusy` produites pour
  `55P03` / `57014` / timeout client décrivent une panne observée ; elles ne déclenchent ni attente,
  ni nouvelle tentative.
- La correspondance entre les formes fabriquées en mémoire et les formes réelles repose sur les
  mesures E19 / E20, qui sont des **`EXECUTED_SPIKE LOCAL`** : elles ont été exécutées
  **localement**, hors CI, et leurs preuves ont été **enregistrées** dans le commit `b84c7e3`. La
  CI `33411724068` a seulement prouvé la **non-régression de `MMV.sln`** sur ce commit ; elle
  **n'a pas exécuté** le harness PostgreSQL. Cette correspondance sera reconfirmée sur serveur
  réel lors d'un lot d'intégration ultérieur.

---

## 16. État documentaire

| Élément | État |
|---|---|
| P4-4A0 | **RECORDED** |
| P4-4A1 | **IMPLEMENTED LOCALLY — PENDING HUMAN REVIEW / COMMIT / CI** |
| P4-4 | **NOT CLOSE** |
| P4-5 | **NOT STARTED** |
| V1 MULTI-POSTE | **NOT GO** |

La roadmap n'a pas été modifiée par ce lot.

**Verdict : `P4_4A1_IMPLEMENTATION_LOCAL = READY_FOR_HUMAN_REVIEW`**
