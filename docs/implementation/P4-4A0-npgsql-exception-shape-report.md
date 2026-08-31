# P4-4A0 — Forme structurelle des exceptions Npgsql

> **PREUVE DE SPIKE LOCAL.** Toutes les mesures de ce rapport ont été exécutées **localement**,
> sur un PostgreSQL jetable en conteneur, sur le poste de l'opérateur. **Aucune de ces mesures
> n'a été exécutée par GitHub Actions.** Aucun résultat ci-dessous ne doit être attribué à la CI.

---

## 1. Base Git

| Élément | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p4-multi-poste` |
| `HEAD` | `669e48c9b90751e08ff9d507313bd541536766df` |
| `origin/p4-multi-poste` | `669e48c9b90751e08ff9d507313bd541536766df` |
| Fichiers suivis modifiés | aucun |
| Fichiers indexés (*staged*) | aucun |

Le lot P4-4A0 est une **passe de MESURE**. Aucun commit, aucun *staging*, aucun *push* n'a été
effectué. Aucun code de production n'a été touché.

## 2. Environnement (sans secret)

| Élément | Valeur |
|---|---|
| `MMV_P4_POSTGRES_CONNECTION` | **définie et non blanche** — valeur jamais affichée, jamais écrite, jamais journalisée |
| `MMV_P4_PG_CONTAINER` | `mmv-p4-pg` (préfixe `mmv-p4-` exigé par `DockerControl`) |
| Client Docker | serveur `29.4.1`, répond |
| État du conteneur | `running`, image `postgres:17` |
| Version serveur | `PostgreSQL 17.10 (Debian 17.10-1.pgdg13+1)` |
| Disponibilité | `pg_isready` → `accepting connections` ; puis ouverture Npgsql réelle + `SELECT 1` par le mécanisme du harness (`DockerControl.WaitUntilReachableAsync`), exercé au cours du scénario D |
| Bases utilisées | bases **jetables** `mmv_p4_e19_*` et `mmv_p4_e19d_*`, créées puis supprimées par `SpikeDatabase` |
| Journal de preuves | écrit **hors du dépôt** (`MMV_P4_SPIKE_LOG_DIR`, répertoire temporaire de session) |

La chaîne de connexion n'apparaît nulle part dans ce rapport, dans le dépôt, ni dans le journal :
`SpikeLog` ne reçoit que des faits structurés, et `ExceptionShape` ne sérialise **aucun message
d'exception**. Aucun secret n'a été créé ni renouvelé.

## 3. Fichiers de sonde

Les trois fichiers étaient **déjà écrits** lors de la première passe (arrêtée au portail
d'environnement) ; ils ont été relus intégralement et exécutés **sans être recréés**.

| Fichier | Rôle |
|---|---|
| `spikes/P4.ProviderComparison/Support/ExceptionShape.cs` | sonde de forme structurelle |
| `spikes/P4.ProviderComparison/ExceptionShapeProbeTests.cs` | tests de la sonde elle-même (10) |
| `spikes/P4.ProviderComparison/E19_NpgsqlExceptionShapeTests.cs` | scénarios serveur réels A–F |

### 3.1 Vérification de la sonde avant exécution

| Exigence | Constat (lecture du code) |
|---|---|
| Ne lit jamais `Exception.Message` | **CONFORME** — aucune lecture de `.Message` dans `ExceptionShape.cs` |
| Ne lit jamais `InnerException.Message` | **CONFORME** |
| Parcourt structurellement les `InnerException` | **CONFORME** — `Walk`, borné à `MaxDepth = 16`, garde `ReferenceEqualityComparer` contre les cycles, et parcourt **toutes** les branches d'une `AggregateException` |
| Détecte `SocketException` | **CONFORME** — `HasType<SocketException>()`, par TYPE |
| Détecte `TimeoutException` | **CONFORME** — par TYPE |
| Détecte `OperationCanceledException` / `TaskCanceledException` | **CONFORME** — les deux ; `TaskCanceledException` dérive de `OperationCanceledException` (vérifié : `BaseType = System.OperationCanceledException`) |
| Relève `SqlState` | **CONFORME** — via `DbException.SqlState` **et** `PostgresException.SqlState` |
| Relève `ConstraintName`, `TableName`, `ColumnName`, `SchemaName` | **CONFORME** — `PostgresException` testée **avant** `NpgsqlException` (dont elle dérive) |
| Relève `SocketErrorCode` | **CONFORME** |
| Relève `IsTransient` | **CONFORME** — `DbException.IsTransient` |
| Relève `DbException.ErrorCode` | **CONFORME** |
| Relève `CancellationToken.IsCancellationRequested` | **CONFORME** — sur toute trame `OperationCanceledException` |
| Relève l'état de connexion avant/après | **CONFORME** — quand le scénario le fournit ; **observation seule**, jamais un critère |

Aucune observation n'a été transformée en logique de production. `PersistenceErrorMapper` et
`PersistenceErrorCategory` sont **inchangés**.

## 4. Tests de la sonde

```
dotnet test --filter FullyQualifiedName~ExceptionShapeProbeTests
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

**10 / 10, 0 échec.** Aucun test supplémentaire n'a été ajouté. Ces tests utilisent des exceptions
**fabriquées** : ils valident l'OUTIL, et **ne prouvent rien** sur Npgsql. Tous les faits provider
ci-dessous viennent exclusivement des scénarios E19 exécutés contre le serveur réel.

## 5. Scénarios exécutés

Les deux tests E19 ont été exécutés contre le PostgreSQL 17.10 jetable : `Passed`, 0 échec, 0 *skip*.

| Scénario | Provocation réelle | Statut |
|---|---|---|
| A | `CommandTimeout = 2 s` sur `SELECT pg_sleep(30)` | `EXECUTED_SPIKE` |
| B1 | ligne verrouillée par une autre transaction, expiration **cliente** (`CommandTimeout = 3 s`) | `EXECUTED_SPIKE` |
| B2 | même verrou, expiration **serveur** (`SET lock_timeout = '1000ms'`, `CommandTimeout = 30 s`) | `EXECUTED_SPIKE` |
| C | ouverture vers un port fermé (`127.0.0.1:59999`) | `EXECUTED_SPIKE` |
| D | `docker stop mmv-p4-pg` **pendant** une transaction en écriture | `EXECUTED_SPIKE` |
| E1 | `CancellationTokenSource(2 s)`, `CommandTimeout = 60 s` | `EXECUTED_SPIKE` |
| E2 | `SET statement_timeout = '1000ms'`, `CommandTimeout = 60 s` | `EXECUTED_SPIKE` |
| F | `INSERT` violant `CHECK (qty >= 0)` sur une table de sonde jetable | `EXECUTED_SPIKE` |

Deux formes **supplémentaires** ont été relevées pendant D, non demandées mais mesurées :

| Scénario | Provocation réelle | Statut |
|---|---|---|
| D-bis | `ROLLBACK` client **pendant** la panne serveur | `EXECUTED_SPIKE` |
| D-ter | **1re** reconnexion après redémarrage, **sans** purge du pool | `EXECUTED_SPIKE` |

Aucune exception fabriquée n'a été substituée à une mesure. Le serveur a été rendu joignable en fin
de scénario D (`ClearAllPools` puis **0,5 s**), et le conteneur est de nouveau `running`.

## 6. Formes exactes relevées

Aucune de ces lignes ne provient d'un texte de message. Sur **toutes** les trames Npgsql,
`DbException.ErrorCode = -2147467259` (`0x80004005`, `E_FAIL`) : cette valeur est **constante** et
ne porte donc **aucune** information discriminante — elle est consignée, jamais utilisée.

### A — `CommandTimeout` client

```
Npgsql.NpgsqlException -> System.TimeoutException
profondeur=2 | SqlState=(aucun) | contrainte/table/colonne/schema=(aucun)
IsTransient=True | SocketException=NON | TimeoutException=OUI
OperationCanceled=NON | TaskCanceled=NON | conn avant=Open apres=Open
```

### B1 — timeout de verrou, expiration **cliente**

```
Npgsql.NpgsqlException -> System.TimeoutException
profondeur=2 | SqlState=(aucun) | contrainte/table/colonne/schema=(aucun)
IsTransient=True | SocketException=NON | TimeoutException=OUI
OperationCanceled=NON | TaskCanceled=NON | conn avant=Open apres=Open
```

**Forme rigoureusement identique à A**, jusqu'à `IsTransient` et `ErrorCode`.

### B2 — timeout de verrou, expiration **serveur** (`lock_timeout`)

```
Npgsql.PostgresException  (trame unique)
profondeur=1 | SqlState=55P03 | contrainte/table/colonne/schema=(aucun)
IsTransient=True | SocketException=NON | TimeoutException=NON
OperationCanceled=NON | TaskCanceled=NON | conn avant=Open apres=Open
```

### C — connexion refusée (port fermé)

```
Npgsql.NpgsqlException -> System.Net.Sockets.SocketException
profondeur=2 | SqlState=(aucun)
IsTransient=True | SocketErrorCode=ConnectionRefused (natif 10061)
TimeoutException=NON | OperationCanceled=NON | conn avant=Closed apres=Closed
```

### D — perte de connexion pendant une transaction

```
Npgsql.NpgsqlException -> System.IO.IOException -> System.Net.Sockets.SocketException
profondeur=3 | SqlState=(aucun)
IsTransient=True | SocketErrorCode=ConnectionAborted (natif 10053)
TimeoutException=NON | OperationCanceled=NON | conn avant=Open apres=Closed
```

### D-bis — `ROLLBACK` pendant la panne

```
System.ObjectDisposedException -> Npgsql.NpgsqlException -> System.IO.IOException
                               -> System.Net.Sockets.SocketException
profondeur=4 | SqlState=(aucun)
IsTransient=True (trame 1) | SocketErrorCode=ConnectionAborted (natif 10053)
conn avant=Open apres=Closed
```

**L'exception EXTERNE n'est pas une `DbException`.** Toute logique qui n'inspecterait que le type
externe verrait ici une `ObjectDisposedException` et manquerait entièrement la panne : le parcours
**complet** de la chaîne est donc obligatoire, pas facultatif.

### D-ter — 1re reconnexion **sans** purge du pool

```
Npgsql.NpgsqlException -> System.IO.EndOfStreamException
profondeur=2 | SqlState=(aucun)
IsTransient=True | SocketException=NON | SocketErrorCode=(n/a)
TimeoutException=NON | OperationCanceled=NON | conn avant=Closed apres=Closed
```

**Cas de panne SANS `SocketException` et SANS `TimeoutException`.** Vérifié contre le système de
types du BCL : `System.IO.EndOfStreamException.BaseType = System.IO.IOException`. La panne reste
donc détectable par `IOException`, pas par `SocketException`.

### E1 — annulation par `CancellationToken`

```
System.OperationCanceledException -> Npgsql.PostgresException
profondeur=2 | SqlState=57014 (porté par la trame INTERNE)
IsTransient=False | SocketException=NON | TimeoutException=NON
OperationCanceled=OUI | TaskCanceled=NON
CancellationToken.IsCancellationRequested=True
conn avant=Open apres=Open
```

Le type externe exact est `System.OperationCanceledException`, **et non** `TaskCanceledException`.

### E2 — `statement_timeout` serveur

```
Npgsql.PostgresException  (trame unique)
profondeur=1 | SqlState=57014
IsTransient=False | SocketException=NON | TimeoutException=NON
OperationCanceled=NON | TaskCanceled=NON | conn avant=Open apres=Open
```

### F — violation `CHECK`

```
Npgsql.PostgresException  (trame unique)
profondeur=1 | SqlState=23514
contrainte=ck_check_probe_qty | schema=public | table=check_probe | colonne=(aucune)
IsTransient=False | SocketException=NON | TimeoutException=NON
OperationCanceled=NON | conn avant=Open apres=Open
```

## 7. Réponses Q1 → Q9

| # | Question | Réponse mesurée | Statut |
|---|---|---|---|
| **Q1** | `CommandTimeout` possède-t-il une `TimeoutException` structurelle ? | **OUI.** `NpgsqlException` → `System.TimeoutException` en `InnerException` directe. | `EXECUTED_SPIKE` |
| **Q2** | B1 a-t-il la même forme que A ? | **OUI, identique** : même chaîne de types, même absence de `SqlState`, même `IsTransient=True`, mêmes états de connexion. **Aucun signal typé ne les sépare.** | `EXECUTED_SPIKE` |
| **Q3** | B2 produit-il une `PostgresException` avec un `SqlState` exploitable ? | **OUI.** `SqlState = 55P03` (`lock_not_available`), trame unique, `IsTransient=True`. | `EXECUTED_SPIKE` |
| **Q4** | *Connection refused* contient-elle une `SocketException` ? | **OUI.** `SocketErrorCode = ConnectionRefused`, code natif `10061`. | `EXECUTED_SPIKE` |
| **Q5** | *Connection loss* contient-elle une `SocketException` ? | **OUI** pour la perte en transaction (D) : `ConnectionAborted`, natif `10053`, imbriquée sous une `IOException`. **NON** pour la connexion morte rendue par le pool (D-ter) : seulement `EndOfStreamException` (une `IOException`). | `EXECUTED_SPIKE` |
| **Q6** | Refus / perte sont-ils distinguables des timeouts ? | **OUI, sur l'ensemble mesuré.** A et B1 portent `TimeoutException` et **jamais** `SocketException` ; C et D portent `SocketException` et **jamais** `TimeoutException` — les deux prédicats sont mutuellement exclusifs ici. D-ter ne porte **ni** l'un **ni** l'autre et n'est rattrapé que par `IOException`. | `EXECUTED_SPIKE` |
| **Q7** | Que produit le `CancellationToken` ? | `System.OperationCanceledException` **externe** (pas `TaskCanceledException`), avec une `PostgresException` **interne** `SqlState = 57014`, et `IsCancellationRequested = True`. | `EXECUTED_SPIKE` |
| **Q8** | `statement_timeout` produit-il `57014` **sans** annulation cliente ? | **OUI.** `PostgresException` `57014` seule ; **aucune** `OperationCanceledException` nulle part dans la chaîne. | `EXECUTED_SPIKE` |
| **Q9** | `23514` est-il réellement observé ? | **OUI**, et pour la **première fois** dans P4 (P4-1 ne le citait que dans un tableau d'intentions, sans mesure). `ConstraintName = ck_check_probe_qty`, `SchemaName = public`, `TableName = check_probe`, `ColumnName` absent, `IsTransient = False`. | `EXECUTED_SPIKE` |

**Conséquence majeure de Q7 + Q8 :** `57014` est **ambigu à lui seul**. Il apparaît dans l'annulation
utilisateur (E1) **et** dans l'expiration serveur (E2). Ce qui les sépare n'est pas le `SqlState`,
mais la **présence d'une `OperationCanceledException` dans la chaîne**. Classifier `57014`
globalement transformerait une annulation demandée par l'utilisateur en erreur de persistance.

## 8. Matrice structurelle

| Cas | Outer | Inner chain | SqlState | SocketException | SocketErrorCode | TimeoutException | OperationCanceled | IsTransient | Connection state | Structural distinction |
|---|---|---|---|---|---|---|---|---|---|---|
| **A** timeout client | `Npgsql.NpgsqlException` | `System.TimeoutException` | *(aucun)* | NON | *(n/a)* | **OUI** | NON | True | avant=Open apres=Open | **Non distinguable de B1** — `EXECUTED_SPIKE` |
| **B1** lock timeout client | `Npgsql.NpgsqlException` | `System.TimeoutException` | *(aucun)* | NON | *(n/a)* | **OUI** | NON | True | avant=Open apres=Open | **Non distinguable de A** — `EXECUTED_SPIKE` |
| **B2** `lock_timeout` serveur | `Npgsql.PostgresException` | *(aucune)* | **`55P03`** | NON | *(n/a)* | NON | NON | True | avant=Open apres=Open | **Distinct** par `SqlState` — `EXECUTED_SPIKE` |
| **C** connexion refusée | `Npgsql.NpgsqlException` | `System.Net.Sockets.SocketException` | *(aucun)* | **OUI** | **`ConnectionRefused`** (10061) | NON | NON | True | avant=Closed apres=Closed | **Distinct** par `SocketException` — `EXECUTED_SPIKE` |
| **D** perte de connexion | `Npgsql.NpgsqlException` | `System.IO.IOException` → `System.Net.Sockets.SocketException` | *(aucun)* | **OUI** | **`ConnectionAborted`** (10053) | NON | NON | True | avant=Open apres=**Closed** | **Distinct** par `SocketException` ; distinct de C par `SocketErrorCode` et par `conn.apres` — `EXECUTED_SPIKE` |
| **E1** `CancellationToken` | `System.OperationCanceledException` | `Npgsql.PostgresException` | **`57014`** | NON | *(n/a)* | NON | **OUI** (`IsCancellationRequested=True`) | False | avant=Open apres=Open | **Distinct de E2** par `OperationCanceledException` — `EXECUTED_SPIKE` |
| **E2** `statement_timeout` serveur | `Npgsql.PostgresException` | *(aucune)* | **`57014`** | NON | *(n/a)* | NON | NON | False | avant=Open apres=Open | **Distinct de E1** par l'ABSENCE d'annulation — `EXECUTED_SPIKE` |
| **F** violation `CHECK` | `Npgsql.PostgresException` | *(aucune)* | **`23514`** | NON | *(n/a)* | NON | NON | False | avant=Open apres=Open | **Distinct** par `SqlState` + `ConstraintName` — `EXECUTED_SPIKE` |
| *D-bis* rollback en panne | `System.ObjectDisposedException` | `NpgsqlException` → `IOException` → `SocketException` | *(aucun)* | **OUI** | `ConnectionAborted` (10053) | NON | NON | True | avant=Open apres=Closed | Outer **non-`DbException`** : impose le parcours complet — `EXECUTED_SPIKE` |
| *D-ter* pool non purgé | `Npgsql.NpgsqlException` | `System.IO.EndOfStreamException` | *(aucun)* | **NON** | *(n/a)* | NON | NON | True | avant=Closed apres=Closed | **Ni socket ni timeout** ; rattrapé par `IOException` seule — `EXECUTED_SPIKE` |

Toutes les cellules ci-dessus sont `EXECUTED_SPIKE`. Ce qui est `NOT_PROVED` est listé au §11.

### 8.1 Signaux inutilisables (mesuré)

| Signal | Constat | Verdict |
|---|---|---|
| `DbException.ErrorCode` | `-2147467259` sur **toutes** les trames Npgsql, sans exception | **Aucun pouvoir discriminant** |
| `IsTransient` | `True` pour A, B1, **B2**, C, D, D-ter ; `False` pour E1, E2, F | **Inutilisable seul** : regroupe un timeout de verrou serveur avec des pannes de connexion |
| `connection.State` | `Open` après A, B1, B2, E1, E2, F ; `Closed` après C, D et D-ter | **Observation seule** ; ne sépare pas D de D-ter, ni C d'une connexion jamais ouverte |
| Texte des `Message` | **jamais lu** | Interdit par construction |

## 9. Distinction des cas sans `SqlState`

Quatre cas mesurés n'ont **aucun** `SqlState` : A, B1, C, D (plus D-bis et D-ter). Ils se séparent
malgré tout par le **type**, ce qui satisfait la règle « aucun parsing de message » :

- **Panne de connexion** — `SocketException` présente (C : `ConnectionRefused` ; D :
  `ConnectionAborted`) **ou**, à défaut, `IOException` présente (D-ter : `EndOfStreamException`).
  → `ConnectionFailure`.
- **Timeout client** — `TimeoutException` présente, `SocketException` absente (A, B1).
  → `DatabaseBusy`, **classe commune**.

**Perte de finesse assumée et documentée :** A (`CommandTimeout` sur une requête simplement longue)
et B1 (attente d'un verrou détenu par un tiers) sont **structurellement identiques**. Aucun signal
typé ne les sépare. Les fusionner dans `DatabaseBusy` est donc le choix le plus honnête : « la base
n'a pas répondu à temps, réessayez » couvre correctement les deux, alors que les séparer exigerait
un parsing de message — interdit.

**Remontée de finesse par le serveur :** lorsque l'expiration est décidée **côté serveur**, la
finesse revient — B2 porte `55P03`, distinct de tout le reste. Configurer `lock_timeout` /
`statement_timeout` côté PostgreSQL n'est donc pas seulement un garde-fou d'exploitation : c'est
aussi ce qui rend l'erreur **classifiable**. Point à instruire en P4-4A1, hors périmètre ici.

## 10. Classification recommandée (proposition)

Proposition pour la future branche PostgreSQL de `PersistenceErrorMapper`. **Rien n'est implémenté
dans cette passe.** Contraintes respectées : aucun parsing de message, aucun code numérique inventé,
aucune modification de `PersistenceErrorCategory` (les 6 valeurs existantes suffisent), aucun type
provider dans `Domain` ou `Application` — la classification reste confinée à `MMV.Infrastructure`.

**L'ORDRE des étapes est imposé par la mesure : ce n'est pas une préférence de style.**

| Ordre | Prédicat (par TYPE / propriété structurée) | Résultat | Fondement |
|---|---|---|---|
| **1** | Chaîne contenant une `OperationCanceledException` (`TaskCanceledException` incluse) dont `CancellationToken.IsCancellationRequested == true` | **Pas de `PersistenceException`** — l'exception est **relayée inchangée** | E1 mesuré |
| **2** | Chaîne contenant une `PostgresException` → aiguillage sur `SqlState` (table ci-dessous) | selon `SqlState` | B2, E2, F mesurés |
| **3** | Chaîne contenant une `SocketException` **ou** une `IOException` | `ConnectionFailure` | C, D, D-ter mesurés |
| **4** | Chaîne contenant une `TimeoutException` | `DatabaseBusy` | A, B1 mesurés |
| **5** | *rien de ce qui précède* | `Unknown` | défaut prudent |

### 10.1 Aiguillage `SqlState` (étape 2)

| `SqlState` | Catégorie proposée | Provenance de la preuve |
|---|---|---|
| `23505` | `UniqueConstraint` | règle de départ opérateur ; mesuré en **P4-1** (`P4-1-provider-comparison-spike-report.md`), **non re-mesuré ici** |
| `23503` | `ConstraintViolation` | idem P4-1 |
| `23502` | `ConstraintViolation` | idem P4-1 |
| `23514` | `ConstraintViolation` | **mesuré dans cette passe** (F) — `ConstraintName` exploitable |
| `40P01` | `Concurrency` | règle de départ opérateur ; mesuré en P4-1 |
| `3D000` | `ConnectionFailure` | règle de départ opérateur ; mesuré en P4-1 |
| `55P03` | `DatabaseBusy` | **mesuré dans cette passe** (B2) — expiration d'attente de verrou décidée par le serveur : même sémantique utilisateur que A/B1, donc même catégorie |
| `57014` | `DatabaseBusy` | **mesuré dans cette passe** (E2). Atteignable **uniquement** après l'étape 1, E1 ayant déjà été relayée. Sans l'étape 1, ce mapping transformerait une annulation utilisateur en erreur de persistance. |
| `25P02` | **`Unknown`** | état **secondaire** d'une transaction déjà avortée : ce n'est pas la cause, seulement sa conséquence. Aucune preuve ne justifie mieux. **Explicitement PAS `Concurrency`.** |
| *autre* | `Unknown` | défaut prudent — aucun code inventé |

### 10.2 Justifications imposées par la mesure

1. **L'étape 1 doit précéder l'étape 2.** La chaîne de E1 **contient** une `PostgresException 57014`.
   Un aiguillage `SqlState` placé en premier convertirait une annulation demandée par l'utilisateur
   en `PersistenceException`, ce que le brief interdit. Le prédicat doit couvrir toute la famille
   `OperationCanceledException`, car `TaskCanceledException` en dérive (vérifié).
2. **Le prédicat de l'étape 3 doit inclure `IOException`, pas seulement `SocketException`.** D-ter
   est une panne réelle sans `SocketException` : sa cause est une `EndOfStreamException`, qui dérive
   de `IOException` (vérifié contre le BCL). `SocketException`, elle, dérive de `Win32Exception` et
   **non** de `IOException` : les deux prédicats sont nécessaires, aucun n'absorbe l'autre.
3. **`PostgresException` doit être testée avant `NpgsqlException`**, dont elle dérive — sans quoi
   tous les `SqlState` seraient perdus. La sonde applique déjà cette règle.
4. **Le parcours doit être complet, pas limité au type externe.** D-bis présente une
   `ObjectDisposedException` en position externe : une inspection du seul type externe manquerait la
   panne. Le `FindInChain<T>` existant du mapper convient sur ce point, mais devra aussi parcourir
   les branches d'une `AggregateException` — non mesuré ici (§11).
5. **Étapes 3 et 4 : l'ordre est libre sur l'ensemble mesuré** (les prédicats y sont mutuellement
   exclusifs), mais l'ordre proposé reste le plus sûr si un cas futur portait les deux.
6. **`PersistenceErrorCategory` n'a pas besoin d'être étendue.** Les 6 valeurs existantes couvrent
   tous les cas mesurés. Aucune valeur n'est ajoutée, renommée ni renumérotée.

## 11. Ambiguïtés restantes et `NOT_PROVED` explicites

| # | Point | Statut |
|---|---|---|
| 1 | **A vs B1** — aucun signal typé ne sépare un `CommandTimeout` d'une attente de verrou côté client. Fusionnés dans `DatabaseBusy`, finesse perdue. | `NOT_PROVED` qu'une distinction structurelle existe |
| 2 | **Chemin EF Core** — toutes les mesures passent par `DbCommand` ADO.NET **brut**. La production passe par EF Core, qui enveloppe dans `DbUpdateException`. La forme relevée devrait survivre (une trame de plus en profondeur 0), mais **cela n'a pas été mesuré**. | `NOT_PROVED` → **LEVÉ par E20** (section « Propagation EF Core — E20 ») ; la prédiction « une trame de plus en profondeur 0 » y est **partiellement infirmée** |
| 3 | **`AggregateException`** — la sonde en parcourt toutes les branches, mais **aucun** scénario réel n'en a produit une. Le comportement du mapper face à une chaîne agrégée reste non mesuré. | `NOT_PROVED` |
| 4 | **`23505`, `23503`, `23502`, `3D000`, `40P01`, `25P02`** — **non re-mesurés dans cette passe.** Leur mapping repose sur les règles de départ de l'opérateur et sur les mesures P4-1, pas sur P4-4A0. | `NOT_PROVED` *(dans cette passe)* → `23505`, `23503`, `23502` **mesurés via EF Core** par E20 ; `3D000`, `40P01`, `25P02` restent `NOT_PROVED` |
| 5 | **`28P01`** (échec d'authentification) — non provoqué. | `NOT_PROVED` |
| 6 | **SQL Server** — aucune mesure dans cette passe ; seul PostgreSQL a été exercé, ce qui est cohérent avec P4-2, qui a retenu PostgreSQL. La branche SQL Server du mapper reste sans fondement mesuré. | `NOT_PROVED` |
| 7 | **Messages utilisateur** — aucun libellé n'est proposé pour les nouvelles branches ; c'est du ressort de P4-4A1. | Hors périmètre |
| 8 | **Politique de réessai** — `IsTransient=True` sur B2, C, D et D-ter pourrait suggérer un réessai automatique. Aucun réessai n'est proposé ici : sa sûreté (idempotence) n'a pas été mesurée dans cette passe. | Hors périmètre |
| 9 | **Purge du pool après panne** — D-ter montre qu'une connexion morte est rendue par le pool ; `ClearAllPools` a rétabli l'accès en 0,5 s. Aucune recommandation de production n'en est tirée ici. | Hors périmètre |

## 12. Périmètre respecté

| Garantie | État |
|---|---|
| Aucun code de production modifié | **CONFORME** — `src/**` intact |
| Aucun test de `MMV.sln` modifié | **CONFORME** — `tests/**` intact |
| `PersistenceErrorMapper` inchangé | **CONFORME** |
| `PersistenceErrorCategory` inchangée | **CONFORME** |
| Aucun provider dans `Domain` / `Application` | **CONFORME** — aucun changement dans ces couches |
| Aucun package ajouté | **CONFORME** — aucun `.csproj` modifié |
| Aucune migration | **CONFORME** |
| Aucun workflow modifié | **CONFORME** |
| Aucun parsing de `Message` | **CONFORME** — vérifié par lecture ET par le test `Capture_never_derives_anything_from_the_message_text` |
| Aucun secret affiché, écrit, journalisé ou commité | **CONFORME** |
| Aucune preuve attribuée à la CI | **CONFORME** — ce rapport est une preuve de **spike local** |
| `design-handoff/`, `design/`, `docs/ui/` | **intacts**, hors périmètre |

---

## Propagation EF Core — E20

### Objet

E19 a mesuré les échecs tels que les rend **ADO.NET / Npgsql directement**. `PersistenceErrorMapper`
ne verra jamais ces exceptions-là : il verra celles que lui remet **EF Core**. Cette passe
(`P4-4A0B`) répond donc à **une seule** question :

> « Quelle chaîne d'exceptions le code EF Core réel remet-il au mapper ? »

Elle **ne re-prouve aucun** scénario d'E19. `40P01`, `25P02`, `3D000`, `28P01`, les 14 primitives, la
concurrence E18 et la perte réseau E14 ne sont **pas** re-provoqués : ces preuves ont leurs lots
dédiés. Aucun conteneur n'est arrêté.

Cette section **lève le `NOT_PROVED` du §11 ligne 2** — et corrige la prédiction qui y figurait
(cf. « Conclusion 1 » ci-dessous, qui l'infirme partiellement).

### Fichier ajouté

| Fichier | Rôle |
|---|---|
| `spikes/P4.ProviderComparison/E20_EfCoreExceptionPropagationTests.cs` | **Nouveau.** Seul fichier créé par cette passe. Contient les 10 scénarios EF, plus un modèle EF **minimal propre au harness** (`EfProbeContext`, `EfProbeParent`, `EfProbeChild`). |
| `spikes/P4.ProviderComparison/Support/ExceptionShape.cs` | **Réutilisé sans aucune modification.** Aucune seconde infrastructure de diagnostic n'a été créée : E20 interroge les trames déjà relevées (`ExceptionFrame.ClrType`) par un test de type. |

Le modèle de **production** n'est ni exercé ni modifié ici : `EfProbeContext` n'hérite pas
d'`OpticDbContext` et ne partage rien avec le schéma MMV. Le schéma de sonde est créé par
`EnsureCreated` dans une base jetable — **aucune migration** n'est générée. Contraintes nommées
explicitement (`ux_ef_probe_parent_code`, `fk_ef_probe_child_parent`) afin que `ConstraintName` soit
vérifiable dans la chaîne.

### Exécution

| Élément | Valeur |
|---|---|
| Branche | `p4-multi-poste` |
| `HEAD` | `669e48c9b90751e08ff9d507313bd541536766df` (inchangé — rien n'a été commité) |
| Serveur | PostgreSQL **17.10** (Debian), conteneur jetable `mmv-p4-pg`, `127.0.0.1:5433` |
| Tests exécutés | `ExceptionShapeProbeTests` (10) + `E20` (1) = **11 réussis, 0 échec, 0 ignoré** |
| `MMV.sln` | **non exécutée** — hors périmètre de cette passe |
| Reproductibilité | E20 exécutée **deux fois** ; les deux journaux de preuve sont **identiques octet pour octet** (7 221 o), horodatages distincts |
| Attribution | **preuve de spike local** — **aucune** preuve n'est attribuée à la CI |

Aucun secret n'a été affiché, journalisé ni écrit : la chaîne de connexion n'est lue que depuis
`MMV_P4_POSTGRES_CONNECTION`, et le journal ne contient que des faits structurés.

### Scénarios exécutés

| Cas | Provocation réelle, à travers EF Core | Statut |
|---|---|---|
| **EF1** | `SaveChangesAsync` — doublon sur index unique | `EXECUTED_SPIKE` |
| **EF2a** | `SaveChangesAsync` — clé étrangère vers un parent inexistant | `EXECUTED_SPIKE` |
| **EF2b** | `SaveChangesAsync` — colonne `NOT NULL` laissée nulle (EF ne valide pas côté client : c'est le serveur qui refuse) | `EXECUTED_SPIKE` |
| **EF3a** | Requête EF (`ExecuteSqlRawAsync`) + `CommandTimeout = 2 s` sur `pg_sleep(30)` | `EXECUTED_SPIKE` |
| **EF3b** | `SaveChangesAsync` **réellement bloqué** par un verrou détenu sur une autre connexion, + `CommandTimeout = 3 s` | `EXECUTED_SPIKE` |
| **EF3c** | Requête EF sous `SET LOCAL statement_timeout = '1000ms'` (expiration **serveur**) | `EXECUTED_SPIKE` |
| **EF4a** | Requête EF annulée par `CancellationToken` (2 s), `CommandTimeout = 60 s` hors de cause | `EXECUTED_SPIKE` |
| **EF4b** | `SaveChangesAsync` bloqué par un verrou, annulé par `CancellationToken` (3 s), `CommandTimeout = 60 s` hors de cause | `EXECUTED_SPIKE` |
| **EF5a** | Requête EF vers un **port local fermé** (`59999`) | `EXECUTED_SPIKE` |
| **EF5b** | `SaveChangesAsync` vers le même port fermé | `EXECUTED_SPIKE` |

Les 10 provocations ont **réellement levé** une exception : le test échoue explicitement si l'une
d'elles réussit, afin qu'aucune ligne non observée ne puisse entrer dans ce rapport.
`SET LOCAL` confine le réglage d'EF3c à sa transaction : rien n'est laissé dans la connexion rendue
au pool. EF5 n'arrête **aucun** conteneur — un port fermé suffit.

### Formes exactes relevées

Aucune de ces lignes ne provient d'un texte de message. `DbException.ErrorCode = -2147467259` reste
constant partout, comme au §6 : consigné, jamais utilisé.

#### EF1 — unique `23505` par `SaveChangesAsync`

```
Microsoft.EntityFrameworkCore.DbUpdateException -> Npgsql.PostgresException
profondeur=2 | SqlState=23505 (trame INTERNE)
contrainte=ux_ef_probe_parent_code | schema=public | table=ef_probe_parent
IsTransient=False | SocketException=NON | TimeoutException=NON | OperationCanceled=NON
conn avant=Closed apres=Closed
```

#### EF2a — clé étrangère `23503` par `SaveChangesAsync`

```
Microsoft.EntityFrameworkCore.DbUpdateException -> Npgsql.PostgresException
profondeur=2 | SqlState=23503 (trame INTERNE)
contrainte=fk_ef_probe_child_parent | schema=public | table=ef_probe_child
IsTransient=False | SocketException=NON | TimeoutException=NON | OperationCanceled=NON
conn avant=Closed apres=Closed
```

#### EF2b — `NOT NULL` `23502` par `SaveChangesAsync`

```
Microsoft.EntityFrameworkCore.DbUpdateException -> Npgsql.PostgresException
profondeur=2 | SqlState=23502 (trame INTERNE)
contrainte=(aucune) | schema=public | table=ef_probe_parent | colonne=Code
IsTransient=False | SocketException=NON | TimeoutException=NON | OperationCanceled=NON
conn avant=Closed apres=Closed
```

`23502` ne porte **pas** de `ConstraintName` — c'est normal, une contrainte `NOT NULL` n'est pas
nommée — mais il porte `ColumnName`, `TableName` et `SchemaName`, tous structurés.

#### EF3a — timeout client sur le chemin **requête**

```
Npgsql.NpgsqlException -> System.TimeoutException
profondeur=2 | SqlState=(aucun) | AUCUNE DbUpdateException
IsTransient=True (trame 0) | SocketException=NON | TimeoutException=OUI | OperationCanceled=NON
conn avant=Closed apres=Closed
```

**Forme identique au cas A d'E19** : le chemin requête d'EF n'ajoute aucune enveloppe.

#### EF3b — timeout client **pendant** `SaveChangesAsync` (verrou détenu)

```
System.InvalidOperationException
  -> Microsoft.EntityFrameworkCore.DbUpdateException
    -> Npgsql.NpgsqlException
      -> System.TimeoutException
profondeur=4 | SqlState=(aucun)
IsTransient=True (trame 2) | SocketException=NON | TimeoutException=OUI | OperationCanceled=NON
conn avant=Closed apres=Closed
```

**Cas le plus instructif de la passe.** L'exception externe n'est **ni** une `DbException`, **ni** une
`DbUpdateException` : c'est une `System.InvalidOperationException`. Et la `DbUpdateException`
présente en profondeur 1 enveloppe ici un échec **sans aucun `SqlState`**.

#### EF3c — `statement_timeout` **serveur** vu à travers EF

```
Npgsql.PostgresException  (trame unique)
profondeur=1 | SqlState=57014 | AUCUNE DbUpdateException
IsTransient=False | TimeoutException=NON | OperationCanceled=NON
conn avant=Closed apres=Closed
```

#### EF4a — annulation cliente sur le chemin **requête**

```
System.OperationCanceledException -> Npgsql.PostgresException
profondeur=2 | SqlState=57014 (trame INTERNE) | AUCUNE DbUpdateException
CancellationToken.IsCancellationRequested=True
IsTransient=False | TimeoutException=NON | TaskCanceled=NON
conn avant=Closed apres=Closed
```

#### EF4b — annulation cliente **pendant** `SaveChangesAsync`

```
System.OperationCanceledException -> Npgsql.PostgresException
profondeur=2 | SqlState=57014 (trame INTERNE) | AUCUNE DbUpdateException
CancellationToken.IsCancellationRequested=True
IsTransient=False | TimeoutException=NON | TaskCanceled=NON
conn avant=Closed apres=Closed
```

**Forme rigoureusement identique à EF4a**, alors même que l'échec survient au cœur d'une écriture
bloquée : EF Core **n'enveloppe pas** l'annulation dans une `DbUpdateException`. Le type externe
exact est `System.OperationCanceledException`, **et non** `TaskCanceledException` — comme en E1.

#### EF5a / EF5b — connexion refusée (port fermé), requête **et** écriture

```
System.InvalidOperationException
  -> Npgsql.NpgsqlException
    -> System.Net.Sockets.SocketException
profondeur=3 | SqlState=(aucun) | AUCUNE DbUpdateException
IsTransient=True (trame 1) | SocketErrorCode=ConnectionRefused (natif 10061)
TimeoutException=NON | OperationCanceled=NON
conn avant=Closed apres=Closed
```

Les deux chemins — requête (EF5a) et `SaveChangesAsync` (EF5b) — donnent la **même** forme : un échec
d'ouverture de connexion n'est **jamais** enveloppé dans une `DbUpdateException`.

### Matrice EF

| Cas | Outer | Inner chain | SqlState | Structured signal preserved? | Mapper traversal sufficient? |
|---|---|---|---|---|---|
| **EF1** `23505` | `Microsoft.EntityFrameworkCore.DbUpdateException` | `Npgsql.PostgresException` | **`23505`** | **OUI** — `SqlState` + `ConstraintName` (`ux_ef_probe_parent_code`) | **OUI** — `EXECUTED_SPIKE` |
| **EF2a** `23503` | `Microsoft.EntityFrameworkCore.DbUpdateException` | `Npgsql.PostgresException` | **`23503`** | **OUI** — `SqlState` + `ConstraintName` (`fk_ef_probe_child_parent`) | **OUI** — `EXECUTED_SPIKE` |
| **EF2b** `23502` | `Microsoft.EntityFrameworkCore.DbUpdateException` | `Npgsql.PostgresException` | **`23502`** | **OUI** — `SqlState` + `ColumnName` / `TableName` / `SchemaName` | **OUI** — `EXECUTED_SPIKE` |
| **EF3a** timeout requête | `Npgsql.NpgsqlException` | `System.TimeoutException` | *(aucun)* | **OUI** — `TimeoutException` par TYPE | **OUI** — `EXECUTED_SPIKE` |
| **EF3b** timeout écriture | `System.InvalidOperationException` | `DbUpdateException` → `NpgsqlException` → `System.TimeoutException` | *(aucun)* | **OUI** — `TimeoutException` par TYPE, en **profondeur 3** | **OUI**, mais **uniquement** par parcours complet — `EXECUTED_SPIKE` |
| **EF3c** `statement_timeout` serveur | `Npgsql.PostgresException` | *(aucune)* | **`57014`** | **OUI** — `SqlState` | **OUI** — `EXECUTED_SPIKE` |
| **EF4a** annulation requête | `System.OperationCanceledException` | `Npgsql.PostgresException` | **`57014`** | **OUI** — `OperationCanceledException` + `IsCancellationRequested=True` | **OUI** — `EXECUTED_SPIKE` |
| **EF4b** annulation écriture | `System.OperationCanceledException` | `Npgsql.PostgresException` | **`57014`** | **OUI** — idem EF4a, **sans** `DbUpdateException` | **OUI** — `EXECUTED_SPIKE` |
| **EF5a** connexion refusée (requête) | `System.InvalidOperationException` | `NpgsqlException` → `System.Net.Sockets.SocketException` | *(aucun)* | **OUI** — `SocketErrorCode = ConnectionRefused` | **OUI**, par parcours complet — `EXECUTED_SPIKE` |
| **EF5b** connexion refusée (écriture) | `System.InvalidOperationException` | `NpgsqlException` → `System.Net.Sockets.SocketException` | *(aucun)* | **OUI** — `SocketErrorCode = ConnectionRefused` | **OUI**, par parcours complet — `EXECUTED_SPIKE` |

Les 10 cellules sont `EXECUTED_SPIKE`. Ce qui reste `NOT_PROVED` est listé plus bas.

### Conclusions

**1. `DbUpdateException` n'est présente que dans 4 cas sur 10 — et la prédiction du §11 était
partiellement fausse.** Le §11 ligne 2 supposait « une trame de plus en profondeur 0 ». La mesure
l'infirme :

| Chemin | `DbUpdateException` présente ? | Profondeur |
|---|---|---|
| EF1, EF2a, EF2b (rejet serveur d'une écriture) | **OUI** | **0** (externe) |
| EF3b (timeout pendant l'écriture) | **OUI** | **1** — sous une `InvalidOperationException` |
| EF3a, EF3c (requête) | **NON** | — |
| EF4a, EF4b (annulation, requête **et** écriture) | **NON** | — |
| EF5a, EF5b (échec de connexion, requête **et** écriture) | **NON** | — |

`DbUpdateConcurrencyException` n'a été observée **dans aucun** cas.

**2. Le type EXTERNE est un critère inutilisable — le parcours complet est obligatoire.** Trois cas
mesurés (EF3b, EF5a, EF5b) présentent une `System.InvalidOperationException` externe, qui n'est ni
une `DbException` ni une `DbUpdateException`. Une logique qui n'inspecterait que le type externe
manquerait entièrement le timeout d'EF3b et les deux échecs de connexion. C'est la confirmation, sur
le chemin EF, de ce que D-bis avait montré sur le chemin ADO.NET.

**3. `PersistenceErrorMapper` ne peut pas assimiler `DbUpdateException` à « violation de
contrainte ».** EF3b le prouve : une `DbUpdateException` y enveloppe un échec **sans `SqlState`**,
dont le seul signal est une `TimeoutException` en profondeur 3.

**4. `SqlState` survit intégralement à l'enveloppe EF.** `23505`, `23503`, `23502` et `57014` sont
tous lisibles dans la chaîne, portés par la `PostgresException` interne. Les règles acquises en
P4-1 (`23505` → `UniqueConstraint`, `23503`/`23502` → `ConstraintViolation`) restent donc
applicables **telles quelles** sur le chemin EF Core.

**5. `ConstraintName` survit également.** `ux_ef_probe_parent_code` (EF1) et
`fk_ef_probe_child_parent` (EF2a) sont relevés intacts à travers `DbUpdateException`. Pour `23502`,
`ConstraintName` est absent — fait normal, compensé par `ColumnName` / `TableName` / `SchemaName`.

**6. L'annulation reste identifiable STRUCTURELLEMENT, avant toute classification `57014`.** C'est le
résultat le plus important de la passe. Dans EF4a **et** EF4b, `OperationCanceledException` est
l'exception **externe**, `IsCancellationRequested = True`, et **aucune** `DbUpdateException`
n'intervient. Le futur mapper peut donc relayer l'annulation **inchangée** avant d'examiner le
`SqlState`, y compris quand l'annulation survient au milieu d'un `SaveChangesAsync`.

**7. L'ambiguïté `57014` d'E19 se reproduit à l'identique sur le chemin EF.** EF3c produit `57014`
**sans** annulation ; EF4a/EF4b produisent `57014` **avec** annulation demandée. Le `SqlState` seul
ne les sépare toujours pas : seule la présence d'une `OperationCanceledException` le fait. La règle
E1/E2 s'applique donc sans changement à EF.

**8. L'état de connexion n'est PAS un signal exploitable sur le chemin EF.** Les 10 cas rapportent
`conn avant=Closed apres=Closed`, EF ouvrant et refermant la connexion autour de chaque opération.
La variété `Open`/`Closed` observée en E19 était un artefact du maintien manuel de la connexion en
ADO.NET brut. `ConnectionState` doit donc rester une **observation**, jamais un critère — ce que le
§8.1 disait déjà, et que cette passe renforce.

**9. Profondeur maximale observée : 4** (EF3b). Le garde-fou `MaxDepth = 16` d'`ExceptionShape` est
largement suffisant.

**10. `IsTransient` reste cohérent avec E19** : `True` sur les timeouts et les échecs de connexion
(EF3a, EF3b, EF5a, EF5b), `False` sur les violations de contrainte et sur `57014` (EF1, EF2a, EF2b,
EF3c, EF4a, EF4b). Aucune politique de réessai n'en est déduite ici.

**Conséquence pour l'ordre d'examen du futur mapper** (conception, **non implémentée** dans cette
passe) : parcourir la chaîne par TYPE et par `InnerException`, puis
① relayer l'annulation inchangée si `OperationCanceledException` est présente **et**
`IsCancellationRequested = True` ; ② sinon aiguiller sur le premier `SqlState` non vide de la
chaîne ; ③ sinon classer par type structurel (`SocketException` / `IOException` /
`TimeoutException`). Le type externe n'entre dans aucune de ces étapes.

### Limites et `NOT_PROVED` de cette passe

| # | Point | Statut |
|---|---|---|
| 1 | **Mécanisme** produisant `System.InvalidOperationException` en position externe (EF3b, EF5a, EF5b) — seule sa **présence** est mesurée, sa cause interne à EF Core n'a pas été instruite. | `NOT_PROVED` |
| 2 | **Modèle du harness, pas le modèle MMV** — la mécanique de propagation d'EF Core est mesurée, **pas** les points d'appel réels de MMV (dépôts, cas d'usage). | `NOT_PROVED` |
| 3 | **`DbUpdateConcurrencyException`** — jamais observée : le modèle de sonde ne porte aucun jeton de concurrence. | `NOT_PROVED` |
| 4 | **`AggregateException`** — toujours aucun scénario réel n'en a produit une. | `NOT_PROVED` *(inchangé depuis §11)* |
| 5 | **`40P01`, `25P02`, `3D000`, `28P01`**, 14 primitives, concurrence E18, perte réseau E14 — **délibérément non re-provoqués** ici. | Hors périmètre — lots dédiés |
| 6 | **Perte de connexion en cours de transaction** à travers EF (équivalent EF du cas D) — non mesurée : elle exigerait d'arrêter le conteneur, ce que cette passe s'interdit. | `NOT_PROVED` |
| 7 | **SQL Server** — aucune mesure. | `NOT_PROVED` *(inchangé depuis §11)* |
| 8 | **Stratégie d'exécution / `EnableRetryOnFailure`** — aucune n'est configurée ; la forme des exceptions sous stratégie de réessai n'est pas mesurée. | `NOT_PROVED` |

### Politique de timeout — décision documentaire

Décision enregistrée explicitement, **P4-4A ne configure AUCUN** :

| Élément | Décision P4-4A |
|---|---|
| `lock_timeout` PostgreSQL | **NON configuré** |
| `statement_timeout` PostgreSQL | **NON configuré** |
| `CommandTimeout` supplémentaire | **NON configuré** |
| Politique de réessai | **NON configurée** |

Les `CommandTimeout` et le `SET LOCAL statement_timeout` employés dans E19 et E20 sont des
**instruments de mesure confinés au harness**. Ils ne préjugent d'aucune politique de production et
ne doivent pas être présentés comme telle.

Le mapper doit seulement être capable de **traduire structurellement** ces erreurs **si** elles
surviennent. La décision d'introduire des timeouts PostgreSQL en production relève d'une **étape
ultérieure** de politique transactionnelle et de configuration. La roadmap n'est pas modifiée par
cette passe.

### Périmètre respecté (passe P4-4A0B)

| Garantie | État |
|---|---|
| Aucun code de production modifié | **CONFORME** — `src/**` intact |
| Aucun test de `MMV.sln` modifié ni exécuté | **CONFORME** — `tests/**` intact |
| `PersistenceErrorMapper` / `PersistenceErrorCategory` inchangés | **CONFORME** |
| `ExceptionShape` non modifiée | **CONFORME** — réutilisée telle quelle |
| Aucun paquet ajouté | **CONFORME** — aucun `.csproj` modifié |
| Aucune migration | **CONFORME** — schéma de sonde via `EnsureCreated` dans une base jetable |
| Aucun workflow modifié | **CONFORME** |
| Aucun parsing de `Message` | **CONFORME** — seuls des types CLR et des propriétés structurées |
| Aucun secret affiché, écrit, journalisé ou commité | **CONFORME** |
| Aucune preuve attribuée à la CI | **CONFORME** — preuve de **spike local** |
| Aucun conteneur arrêté | **CONFORME** — EF5 utilise un port fermé |
| `design-handoff/`, `design/`, `docs/ui/` | **intacts**, hors périmètre |

### Verdict de la passe P4-4A0B

```
P4_4A0B_EF_PROPAGATION = READY_FOR_MAPPER_IMPLEMENTATION
```

La propagation EF Core **préserve** les signaux structurels nécessaires. Chaque cas mesuré reste
atteignable en parcourant la chaîne par **type** et par **`InnerException`**, sans jamais lire un
message : `SqlState` et `ConstraintName` traversent `DbUpdateException` intacts, l'annulation reste
portée par une `OperationCanceledException` externe non enveloppée, et les échecs sans `SqlState`
restent identifiables par `TimeoutException` ou `SocketException`. La seule exigence supplémentaire
imposée par la mesure est que le mapper **ne se fie jamais au type externe** et parcoure la chaîne
en entier.

**P4-4A1 n'est pas commencée. P4-5 n'est pas commencée. V1 MULTI-POSTE : NOT GO.**
---

## Verdict

```
P4_4A0_EXCEPTION_MEASUREMENT = READY_FOR_MAPPER_IMPLEMENTATION
```

La structure mesurée permet de concevoir la branche PostgreSQL de `PersistenceErrorMapper`
**sans aucun parsing de message** : chaque cas mesuré est atteignable par un test de TYPE ou par une
propriété structurée (`SqlState`, `SocketErrorCode`, `IsCancellationRequested`). Les deux seules
zones d'ombre — l'indiscernabilité A / B1 et le chemin EF Core non mesuré — sont l'une **assumée par
une fusion documentée** dans `DatabaseBusy`, l'autre **explicitement `NOT_PROVED`** et à instruire
avant l'implémentation.

**Mise à jour P4-4A0B :** la seconde zone d'ombre — le chemin EF Core — n'est plus `NOT_PROVED` : elle a été mesurée par **E20** (section « Propagation EF Core — E20 », verdict `P4_4A0B_EF_PROPAGATION = READY_FOR_MAPPER_IMPLEMENTATION`). L'indiscernabilité A / B1, elle, reste assumée par la fusion documentée dans `DatabaseBusy`.

**P4-4A1 n'est pas commencée. P4-5 n'est pas commencée. V1 MULTI-POSTE : NOT GO.**
