# MMV — P4-1 · Lot D — Portabilité applicative de la création d'alerte LowStock active

> **MODE** : `IMPLEMENT_P4_1_LOT_D_NO_COMMIT`
> **Aucun commit, aucun push.** **Aucun provider n'est choisi ni éliminé.** **Aucune ADR P4-2 n'est créée.**
> **Aucune modification** de `src/MMV.App/**`, de l'UI, des contrats Domain/Application, des migrations, du
> snapshot EF, de `MMV.sln` ou de `.github/**`.
> **Le dépôt réel et les sorties de commandes priment toujours sur ce document.**

---

## 1. Paramètres et portes d'entrée

| Élément | Attendu | Constaté | Verdict |
|---|---|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` | idem | ✅ |
| Branche | `p4-multi-poste` | `p4-multi-poste` | ✅ |
| `HEAD` | `6f634601d4e042c1c5cc15db6642df58c926e321` | `6f634601d4e042c1c5cc15db6642df58c926e321` | ✅ |
| CI de référence | run `30211361647` succès | `event=push`, `status=completed`, **`conclusion=success`**, `headSha=6f63460…` (exact), `headBranch=p4-multi-poste` | ✅ |
| Baseline tests avant ajout | 1499 — Domain 649 · Application 611 · App 239 | reproduite (§8.2) | ✅ |
| Arbre de travail au démarrage | propre (suivis) | propre ; seuls `design-handoff/`, `design/`, `docs/ui/` non suivis | ✅ |

> **Note — état courant du dépôt (2026-07-26, reconciliation documentaire).** La ligne « Arbre de travail »
> ci-dessus décrit l'état **au démarrage du Lot D**, avant toute modification. Depuis, le Lot D a modifié
> `src/MMV.Infrastructure/Repositories/NotificationRepository.cs` (fichier **suivi**, `M`), puis la
> reconciliation documentaire a modifié `docs/architecture/P4-multi-poste-roadmap.md` (fichier **suivi**, `M`)
> et créé **ce rapport lui-même** ainsi que les deux fichiers de tests en tant que fichiers **non suivis**
> (`??`). **L'arbre de travail suivi n'est donc plus propre au moment de la lecture de ce document** — cet état
> est attendu, il est décrit exactement en §9, et **aucun commit n'a été effectué**.

Documents lus avant toute modification : `docs/architecture/P4-multi-poste-roadmap.md`,
[P4-1-windows-network-lot-c-report.md](P4-1-windows-network-lot-c-report.md),
[P4-1-provider-comparison-spike-report.md](P4-1-provider-comparison-spike-report.md),
[P4-1-technical-completion-lot-b-report.md](P4-1-technical-completion-lot-b-report.md).

---

## 2. Cause racine — **mesurée**, pas supposée

Le lot B avait **appelé** la méthode de production sur les deux serveurs et enregistré l'échec
(P4-1-technical-completion-lot-b-report §9) :

| Provider | Exception | Message |
|---|---|---|
| PostgreSQL | `InvalidCastException` | *The value "Microsoft.Data.Sqlite.SqliteParameter" is not of type "NpgsqlParameter" and cannot be used in this parameter collection.* |
| SQL Server | `InvalidCastException` | *The SqlParameterCollection only accepts non-null Microsoft.Data.SqlClient.SqlParameter type objects, not Microsoft.Data.Sqlite.SqliteParameter objects.* |

La dette était **double**, et les deux volets sont traités par le Lot D :

1. **Objets paramètres** — la méthode construisait sept `Microsoft.Data.Sqlite.SqliteParameter` **en dur**. Le
   rejet survenait **à l'ajout dans la collection de commande** du provider monté, donc **avant** toute
   soumission au moteur : le SQL n'était jamais évalué. C'est le point d'échec **effectif**.
2. **Dialecte** — `ON CONFLICT DO NOTHING` existe sur SQLite (≥ 3.24) **et** sur PostgreSQL, mais **pas** sur
   SQL Server, qui n'a aucun équivalent de cet opérateur. Ce second volet n'avait jamais pu être atteint, donc
   jamais mesuré, tant que le premier bloquait.

**L'échec était identique sur les deux providers : il ne départageait ni PostgreSQL ni SQL Server.** La
correction non plus — voir §6.

---

## 3. Approche retenue

### 3.1 Paramètres — fabrication déléguée au provider courant

Les paramètres sont désormais demandés à une commande **créée par la connexion elle-même** :

```csharp
var connection = _context.Database.GetDbConnection();
using var parameterFactory = connection.CreateCommand();
// parameterFactory.CreateParameter() → SqliteParameter | NpgsqlParameter | SqlParameter
```

Conséquences retenues :

- **Aucune référence de paquet provider n'est ajoutée** à `MMV.Infrastructure` (ni Npgsql, ni
  `Microsoft.EntityFrameworkCore.SqlServer`) — le `.csproj` est **inchangé** ; le seul type manipulé est
  `System.Data.Common.DbParameter`.
- **Aucune référence provider n'atteint Domain ni Application** : ces deux projets ne sont pas touchés.
- **La valeur est affectée telle quelle, `null` compris.** C'est délibéré et c'est ce qui **préserve à
  l'identique** le comportement SQLite existant : `Microsoft.Data.Sqlite` refuse un paramètre requis dont la
  valeur n'a pas été posée (`InvalidOperationException`), au lieu d'y substituer un `NULL` silencieux qui
  deviendrait indiscernable d'un refus métier. Le test P3-8
  `LaPrimitiveAtomique_PropageUneViolationNotNull_SansLaConfondreAvecUnDoublon` continue donc de passer
  **sans être modifié** (§8.1).

> **Alternative écartée, et pourquoi.** Passer des valeurs CLR nues à `ExecuteSqlRawAsync` avec des
> substituants positionnels `{0}` aurait aussi supprimé le couplage, mais EF pose alors `DBNull.Value` à la
> place d'une valeur absente : la violation NOT NULL aurait changé de nature et le test P3-8 ci-dessus aurait
> dû être réécrit. La fabrication par le provider est **plus petite en effet de bord** — c'est le critère
> « plus petit changement justifié ».

### 3.2 Dialecte — un seul dialecte par provider, **toujours une seule instruction**

`ActiveLowStockInsertSql(providerName)` sélectionne le texte SQL sur `_context.Database.ProviderName` (chaîne,
donc sans dépendance de compilation vers un provider) :

| Provider (`ProviderName`) | Forme SQL réellement employée |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | `INSERT INTO "Notifications" (…) VALUES (…) ON CONFLICT DO NOTHING;` — **texte inchangé** par rapport à l'existant |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **le même texte**, mot pour mot |
| `Microsoft.EntityFrameworkCore.SqlServer` | `INSERT INTO [Notifications] (…) SELECT @… WHERE NOT EXISTS (SELECT 1 FROM [Notifications] WITH (UPDLOCK, HOLDLOCK) WHERE [Type]=@type AND [EntityType]=@entityType AND [EntityId]=@entityId AND [ResolvedAt] IS NULL);` |
| tout autre | **`NotSupportedException` explicite** — jamais un dialecte deviné |

**SQL Server — pourquoi ce n'est pas un « check-then-act ».** Le test d'existence et l'insertion appartiennent
à **une seule instruction**, évaluée dans **une seule transaction implicite**. `UPDLOCK, HOLDLOCK` pose un
verrou de plage sur la clé **absente**, ce qui sérialise deux tentatives simultanées côté moteur. L'index
unique filtré `idx_notifications_active_low_stock_unique` reste le dernier rempart. À aucun moment
l'application ne lit, ne décide, puis n'écrit : **la base tranche**. Aucune commande supplémentaire n'est
émise — vérifié sur SQLite par interception (§8.1, test « une seule instruction »).

Cette forme n'est pas inventée : c'est **exactement** celle mesurée par le spike P4-1 (E5, §13 du rapport de
spike), 10 tours concurrents, 0 anomalie, 0 interblocage. Le Lot D la rejoue à travers la **méthode de
production**, sur 20 tours (§6).

### 3.3 Ce qui n'a **pas** changé

- Signature, contrat, sémantique de retour (`true` = alerte ouverte · `false` = refus métier silencieux).
- Garde de contrat `ArgumentException` (type/entityType/EntityId).
- Propagation du `CancellationToken` (désormais **testée**, §8.1).
- Aucun `catch` n'a été ajouté : **aucune erreur base n'est absorbée**, ni pertinente ni non pertinente.
- Aucun `SELECT` applicatif préalable.

---

## 4. Fichiers modifiés et ajoutés

| Fichier | Nature | Portée |
|---|---|---|
| [src/MMV.Infrastructure/Repositories/NotificationRepository.cs](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs) | **modifié** (`M`) | +87 / −24. Import `Microsoft.Data.Sqlite` **retiré**, remplacé par `System.Data.Common`. Corps de `TryCreateActiveLowStockAsync` ([L105-135](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L105-L135)) ; ajout de `CreateProviderParameter` ([L148-154](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L148-L154)) et `ActiveLowStockInsertSql` ([L177-201](../../src/MMV.Infrastructure/Repositories/NotificationRepository.cs#L177-L201)). **Aucune autre méthode touchée.** |
| [tests/MMV.Application.Tests/UseCases/Notifications/LowStockCreationProviderPortabilityTests.cs](../../tests/MMV.Application.Tests/UseCases/Notifications/LowStockCreationProviderPortabilityTests.cs) | **ajouté** (`??`) | 6 tests SQLite sur vraie base jetable |
| [spikes/P4.ProviderComparison/E18_ActiveLowStockPortabilityTests.cs](../../spikes/P4.ProviderComparison/E18_ActiveLowStockPortabilityTests.cs) | **ajouté** (`??`) | 4 tests (2 × 2 providers) appelant la **méthode de production** |
| [docs/implementation/P4-1-lot-d-active-low-stock-portability-report.md](P4-1-lot-d-active-low-stock-portability-report.md) | **ajouté** (`??`) | Ce rapport : exécution, preuves, limites et verdict du Lot D |
| [docs/architecture/P4-multi-poste-roadmap.md](../architecture/P4-multi-poste-roadmap.md) | **modifié** (`M`) | Réconciliation de l'état officiel après réussite du Lot D — **documentaire uniquement** |

Les deux dernières lignes relèvent de la **reconciliation documentaire** : elles ne touchent ni code ni test.

**Aucun autre fichier suivi n'est modifié.** En particulier : `src/MMV.App/**`, `src/MMV.Domain/**`,
`src/MMV.Application/**`, `src/MMV.Infrastructure/Migrations/**`, `MMV.sln`, `.github/**`,
`MMV.Infrastructure.csproj` — **tous intacts**.

---

## 5. Tests ajoutés

### 5.1 Piste SQLite — `MMV.Application.Tests` (6 tests, base SQLite jetable)

| Test | Ce qu'il prouve |
|---|---|
| `PremiereCreation_OuvreLAlerteEtEcritUneSeuleLigne` | 1re création → `true` ; **1** ligne, **1** active |
| `CreationEnDoublon_NAjouteAucuneLigne_EtRetourneFalse` | 2 connexions distinctes → `true` puis `false` ; **toujours 1** ligne au total, **1** active |
| `LaCreation_EstUneSeuleInstruction_SansLecturePrealable` | par interception : **exactement 1** commande, commençant par `INSERT`, contenant `ON CONFLICT DO NOTHING`, **sans aucun `SELECT`**, sans lot multi-instructions |
| `LesParametres_SontFabriquesParLeProviderCourant` | les **7** paramètres réellement transmis sont du type que la connexion courante fabrique elle-même — plus aucun type concret étranger |
| `UnJetonDejaAnnule_InterrompLaCreation_SansRienEcrire` | `OperationCanceledException` levée, **0** ligne écrite |
| `ApresResolution_UnNouvelEpisodeEstAccepte` | l'index ne contraint que l'**actif** : 2 lignes au total, **1** active |

### 5.2 Pistes serveur — harness de spike (E18, 4 tests)

Le harness `spikes/P4.ProviderComparison` est **délibérément hors de `MMV.sln`** : c'est le seul projet qui
référence les providers serveur. Il exerce le **vrai** `OpticDbContext` et la **vraie**
`NotificationRepository`. Les tests appellent `TryCreateActiveLowStockAsync` **elle-même** — aucun SQL recopié,
aucun substitut.

| Test | Providers | Ce qu'il vérifie (par **assertions**, pas par traces) |
|---|---|---|
| `Production_primitive_opens_then_refuses_then_reopens_after_resolution` | Postgres, SqlServer | 1re = `true` · doublon = `false` · **1** ligne / **1** active · après résolution, nouvel épisode = `true` · **2** lignes / **1** active |
| `Concurrent_production_calls_yield_exactly_one_active_alert` | Postgres, SqlServer | **20 tours**, 2 connexions réelles synchronisées par `Barrier`, chacune sur son propre `DbContext` : par tour exactement **1** `true`, **1** `false`, **0** erreur, **1** ligne, **1** active |

Aucune exception n'est absorbée dans ces tests : toute erreur rend le tour non conforme et **fait échouer**
l'assertion finale.

---

## 6. Résultats mesurés

### 6.1 Environnement d'exécution des pistes serveur

Conteneurs **jetables**, préfixe obligatoire `mmv-p4-`, publiés **sur la boucle locale uniquement**,
**supprimés avec leurs volumes** à la fin (§9). Aucune donnée de production ni utilisateur. Aucun mot de passe
n'est consigné ici ni dans le dépôt ; les chaînes proviennent exclusivement de
`MMV_P4_POSTGRES_CONNECTION` / `MMV_P4_SQLSERVER_CONNECTION`.

| Provider | Version relevée **par requête serveur** | Exposition |
|---|---|---|
| PostgreSQL | **17.10** (Debian 17.10-1.pgdg13+1, x86_64-pc-linux-gnu) | `127.0.0.1:5433` |
| SQL Server | **16.0.4265.3**, SQL Server 2022 (RTM-CU26), **Express Edition (64-bit)**, `EngineEdition = 4` | `127.0.0.1:14333` |

> Le laboratoire Windows à deux VM du Lot C **n'a pas été requis** pour cette itération de code : le Lot D
> mesure la **portabilité applicative** de la primitive, pas le socle Windows/réseau — lequel est déjà `PASS`
> (Lot C §20). Le dépôt n'apporte aucun élément rendant les VM nécessaires ici.

### 6.2 Piste SQLite

```
dotnet test tests\MMV.Application.Tests --filter LowStockCreationProviderPortabilityTests
  → Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

Tests P3-8 préexistants sur la même primitive, **non modifiés** :

```
dotnet test tests\MMV.Application.Tests --filter FullyQualifiedName~Notifications
  → Failed: 0, Passed: 43, Skipped: 0, Total: 43
```

**Compatibilité SQLite préservée** : le texte SQL SQLite est **identique au caractère près** à l'existant, et
les cinq tests P3-8 de `LowStockConcurrencyAndQueryCountTests` — y compris celui qui verrouille la propagation
de la violation NOT NULL — passent **sans aucune retouche**.

### 6.3 Pistes serveur — E18

```
dotnet test spikes\P4.ProviderComparison --filter E18_ActiveLowStockPortabilityTests
  → Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

Journal de preuves (`E18-lotD-active-lowstock-portability.log`), extrait **littéral** :

```
[Postgres]  PRIMITIVE creation-LowStock (PRODUCTION) | 1re=True (attendu True), doublon=False (attendu False)
            | lignes=1 (attendu 1), actives=1 (attendu 1)
[Postgres]  PRIMITIVE creation-LowStock (PRODUCTION) | nouvel episode apres resolution=True (attendu True)
            | lignes=2 (attendu 2), actives=1 (attendu 1) | GARANTIE TENUE
[Postgres]  RESULTAT concurrence (METHODE DE PRODUCTION) : 20/20 tours conformes
            (1 creation, 1 refus, 1 seule alerte active) ; anomalies=0

[SqlServer] PRIMITIVE creation-LowStock (PRODUCTION) | 1re=True (attendu True), doublon=False (attendu False)
            | lignes=1 (attendu 1), actives=1 (attendu 1)
[SqlServer] PRIMITIVE creation-LowStock (PRODUCTION) | nouvel episode apres resolution=True (attendu True)
            | lignes=2 (attendu 2), actives=1 (attendu 1) | GARANTIE TENUE
[SqlServer] RESULTAT concurrence (METHODE DE PRODUCTION) : 20/20 tours conformes
            (1 creation, 1 refus, 1 seule alerte active) ; anomalies=0
```

### 6.4 Concurrence — synthèse

| Provider | Tours | Conformes | Anomalies | Erreurs non gérées | Interblocages | Lignes actives max observées |
|---|---|---|---|---|---|---|
| PostgreSQL 17.10 | 20 | **20 / 20** | 0 | 0 | 0 | **1** |
| SQL Server 2022 Express | 20 | **20 / 20** | 0 | 0 | 0 | **1** |

« Conforme » = **exactement** 1 création (`true`), 1 refus (`false`), 0 erreur, **1 seule ligne** en base, dont
**1 active**. Les comptes en base sont relus par requête après chaque tour, sur une connexion distincte.

### 6.5 Re-confirmation de E11 (lot B)

E11 exerce les primitives de production, dont celle-ci. Rejoué après correction, sur les deux providers :

```
dotnet test spikes\P4.ProviderComparison --filter "E18_… |E11_RemainingPrimitivesTests"
  → Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

La primitive qui échouait en `InvalidCastException` au lot B **s'exécute désormais** dans le même harness, sans
autre changement que celui du §3.

---

## 7. ⚠️ Constat annexe **mesuré** — fidélité de `CreatedAt` (hors périmètre Lot D, **non résolu**)

Le Lot D fournit à la primitive **exactement** ce que fournit la production
([GenerateLowStockNotificationsUseCase.cs:104](../../src/MMV.Application/UseCases/Notifications/GenerateLowStockNotifications/GenerateLowStockNotificationsUseCase.cs#L104) :
`CreatedAt = DateTime.Now`, donc `Kind = Local`). Une **mesure sans assertion** a été consignée, comparant le
chemin **SQL brut** de la primitive et le chemin **EF normal** avec la même valeur :

| Provider | Chemin **SQL brut** (la primitive) | Chemin **EF** (`SaveChanges`) |
|---|---|---|
| SQL Server | envoyé `19:27:45.3451291+02:00` → relu `19:27:45.3466667` (`Unspecified`) — **arrondi**, précision perdue ; la valeur relue est compatible avec la granularité d'un `datetime` (1/300 s) | envoyé `…4029416+02:00` → relu `…4029416` — **identique** : le chemin EF mesuré a conservé une **précision supérieure** |
| PostgreSQL | envoyé `19:27:45.6647286+02:00` → relu `19:27:45.6647280Z` — **l'heure locale est stockée comme si elle était UTC (décalage de 2 h)** | **REFUS** — `DbUpdateException` |

**Lecture honnête de ce constat :**

- Il **ne remet pas en cause** les critères du Lot D : la garantie mesurée porte sur *qui décide* de la
  création et sur *combien de lignes actives* existent — l'un et l'autre sont tenus, 20/20, sur les deux
  providers.
- Il **n'est pas une régression introduite par le Lot D** : sur SQLite le chemin est inchangé au caractère
  près, et sur les serveurs la primitive **ne s'exécutait pas du tout** auparavant.
- Il **ne lui appartient pas** : le refus PostgreSQL du chemin **EF** montre que le sujet est le **mapping
  `DateTime` du modèle**, commun à **toutes** les colonnes de date, pas cette primitive. Le mandat Lot D
  interdit de toucher au modèle, et ce constat devient une **entrée obligatoire de l'ADR P4-2**.
- **Aucune correction n'a donc été tentée ici, et aucune portabilité de valeur de date n'est revendiquée.**
- Il est **mesuré sans assertion** : le harness l'a **consigné**, il ne le **verrouille pas**. Aucun test ne
  garantit aujourd'hui cette fidélité, et le constat est rapporté comme une observation, pas comme un contrat.
- Il **reste non résolu à la clôture du Lot D.**

**Conséquence à porter telle quelle à P4-2** : en l'état, `MMV` ne peut pas écrire de `DateTime.Now`
(`Kind = Local`) vers PostgreSQL par le chemin EF ; et sur les deux serveurs, le chemin SQL brut de cette
primitive n'a pas la même fidélité de date que le chemin EF. Ce point est **nouveau** — il n'apparaît dans
aucun rapport P4-1 antérieur.

**Ce que P4-2 en fait, exactement.** P4-2 est une **ADR documentaire** : elle **ne corrige rien**, n'ajoute
aucun paquet, ne touche ni schéma ni migration. Son mandat sur ce constat est donc de :

1. **l'évaluer** comme entrée de la décision de provider — il pèse différemment sur les deux candidats, le
   refus EF n'ayant été mesuré que sur PostgreSQL ;
2. **affecter son implémentation** à la phase P4 ultérieure appropriée, une fois le provider retenu.

Cette affectation ne peut être **décidée** ici : elle dépend de la solution retenue, que les preuves actuelles
ne déterminent pas. À titre **indicatif et non décisionnel**, elle relèverait vraisemblablement de **P4-4**
(comportement des primitives à l'exécution, conversions de valeurs) si la réponse est une conversion, et/ou de
**P4-5** (types de colonnes, schéma, migrations) si la réponse est un changement de type de colonne.

---

## 8. Contrôles de non-régression

### 8.1 Build

```
dotnet build MMV.sln -c Debug   → Build succeeded. 0 Warning(s) · 0 Error(s)
dotnet build spikes\P4.ProviderComparison\MMV.P4.ProviderComparison.csproj -c Debug
                                → Build succeeded. 0 Warning(s) · 0 Error(s)
```

### 8.2 Suite complète de la solution

| Projet | Baseline attendue | Constaté | Écart |
|---|---|---|---|
| `MMV.Domain.Tests` | 649 | **649** — 0 échec, 0 ignoré | 0 |
| `MMV.Application.Tests` | 611 | **617** — 0 échec, 0 ignoré | **+6** (§5.1) |
| `MMV.App.Tests` | 239 | **239** — 0 échec, 0 ignoré | 0 |
| **Total** | **1499** | **1505** | **+6** |

`1499 (baseline) + 6 (tests SQLite Lot D) = 1505`. **Aucun test préexistant n'a été modifié, supprimé ni
ignoré.** Les 4 tests E18 ne sont **pas** comptés ici : le harness de spike est hors `MMV.sln`, donc hors CI,
conformément à sa conception d'origine.

### 8.3 Audit de vulnérabilités

```
dotnet list MMV.sln package --vulnerable --include-transitive
  → « has no vulnerable packages » pour les 7 projets
```

Aucun paquet n'a été ajouté ni mis à jour : `MMV.Infrastructure.csproj` est **inchangé**.

### 8.4 Modèle EF — aucune dérive

Forme employée par la CI (`.github/workflows`, ligne 118), rejouée à l'identique :

```
dotnet ef migrations has-pending-model-changes --project src\MMV.Infrastructure --no-build
  → « No changes have been made to the model since the last migration. »   (exit 0)
```

**Aucune migration, aucun snapshot EF, aucune configuration d'entité n'a été modifié.** L'index unique filtré
`idx_notifications_active_low_stock_unique` est utilisé **tel qu'il existait déjà** — le Lot D n'a eu besoin
d'aucun changement de schéma, et n'en a donc proposé aucun.

### 8.5 Hygiène Git

```
git diff --check   → propre (aucune erreur d'espaces)
```

### 8.6 Portée respective des preuves **locales** et des preuves **CI**

Les preuves de ce lot n'ont pas toutes la même portée, et la distinction est **structurelle** : le harness
`spikes/P4.ProviderComparison` est **hors de `MMV.sln`** par conception (c'est le seul projet référençant les
providers serveur). Il faut donc les lire séparément.

**Preuves locales du Lot D:**

| Preuve | Résultat | Où |
|---|---|---|
| Tests SQLite ciblés (`MMV.Application.Tests`) | **6 passés** | §6.2 |
| Tests E18 PostgreSQL | **passés** | §6.3 |
| Tests E18 SQL Server | **passés** | §6.3 |
| Concurrence, méthode de production | **20 / 20 tours conformes par provider** | §6.4 |
| Rejeu combiné E11 + E18 | **6 passés** | §6.5 |

Les tests SQLite ciblés sont **dans** `MMV.sln` et seront donc rejoués en CI ; les **4 tests E18** et la mesure
de concurrence dépendent de conteneurs PostgreSQL/SQL Server locaux et **ne le seront pas**.

**Preuves de la solution `MMV.sln` — reproductibles par la CI normale :**

| Contrôle | Résultat |
|---|---|
| Build | **0 avertissement · 0 erreur** |
| `MMV.Domain.Tests` | **649** |
| `MMV.Application.Tests` | **617** |
| `MMV.App.Tests` | **239** |
| **Total `MMV.sln`** | **1505** |
| Vulnérabilités connues | **aucune** |
| Changement de modèle EF en attente | **aucun** |

> **Conséquence à ne pas surinterpréter.** La CI GitHub Actions normale reproduira le build, les **1505 tests
> de la solution**, l'audit de vulnérabilités et le contrôle EF — **elle ne rejouera pas E18** ni la mesure de
> concurrence serveur, tant que `.github/workflows/**` n'est pas **explicitement** modifié pour les exécuter.
> Le Lot D **ne modifie pas** le workflow. **Il ne devra donc jamais être affirmé qu'une CI verte sur ce lot a
> rejoué les tests serveur E18.**

---

## 9. État Git exact (au moment de la rédaction)

```
branche                  : p4-multi-poste
git rev-parse HEAD       : 6f634601d4e042c1c5cc15db6642df58c926e321   (inchangé — AUCUN commit)
commit Lot D             : aucun
push Lot D               : aucun

git status --porcelain (fichiers pertinents du Lot D) :
 M src/MMV.Infrastructure/Repositories/NotificationRepository.cs
 M docs/architecture/P4-multi-poste-roadmap.md
?? docs/implementation/P4-1-lot-d-active-low-stock-portability-report.md
?? spikes/P4.ProviderComparison/E18_ActiveLowStockPortabilityTests.cs
?? tests/MMV.Application.Tests/UseCases/Notifications/LowStockCreationProviderPortabilityTests.cs
```

Les **cinq fichiers du Lot D** sont donc :

1. `src/MMV.Infrastructure/Repositories/NotificationRepository.cs` — **seul** fichier de production
   (+87 / −24) ;
2. `tests/MMV.Application.Tests/UseCases/Notifications/LowStockCreationProviderPortabilityTests.cs` ;
3. `spikes/P4.ProviderComparison/E18_ActiveLowStockPortabilityTests.cs` ;
4. `docs/implementation/P4-1-lot-d-active-low-stock-portability-report.md` — ce rapport ;
5. `docs/architecture/P4-multi-poste-roadmap.md` — reconciliation documentaire.

Les répertoires `design-handoff/`, `design/` et `docs/ui/` apparaissent également en `??` : ils sont
**préexistants**, intentionnellement non suivis, **hors du Lot D**, et n'ont **pas été touchés**.

**Aucun `git add`, aucun `git add .`, aucun `git add -A`, aucun `git commit`, aucun `git push`.**

**Nettoyage du laboratoire jetable** : `docker rm -f -v mmv-p4-pg mmv-p4-mssql` exécuté ;
`docker ps -a --filter name=mmv-p4` → **aucun conteneur restant**. Aucun autre conteneur, volume ou image de la
station n'a été touché (le garde-fou de préfixe `mmv-p4-` du harness reste en vigueur). Le fichier local
contenant les mots de passe jetables a été supprimé ; **aucun secret n'est écrit dans le dépôt ni dans ce
rapport**.

---

## 10. Affirmations **non** prouvées — à ne pas surinterpréter

1. **La compatibilité applicative complète de MMV n'est pas prouvée.** Le Lot D prouve **une** primitive. Il
   n'exécute ni `MMV.App`, ni les cas d'usage complets, ni l'ensemble du modèle contre un serveur.
2. **La fidélité des valeurs `DateTime` n'est pas assurée** — elle est au contraire **mesurée comme divergente**
   (§7) et **laissée non résolue**, par mandat.
3. **La portabilité du reste du modèle reste conditionnée** aux adaptations déjà mesurées par le spike et
   toujours **non appliquées à la production** : réécriture des filtres d'index booléens pour PostgreSQL, et
   mapping monétaire `REAL` (perte de valeur mesurée sur **les deux** providers, spike §11-12). Le harness E18
   utilise `AdaptedOpticDbContext`, qui applique ces adaptations **dans le spike uniquement** : la création du
   schéma serveur repose donc encore sur elles.
4. **Aucun jugement de performance** n'est porté : `UPDLOCK, HOLDLOCK` a été mesuré **correct** (0 anomalie,
   0 interblocage sur 20 tours), **pas** « au même coût » que `ON CONFLICT DO NOTHING`.
5. **20 tours ne sont pas une preuve d'absence d'interblocage** en toute charge : c'est une mesure bornée, et
   elle est rapportée comme telle.
6. **Aucun provider n'est choisi ni éliminé.** Deux `PASS` symétriques ne constituent pas une décision
   d'architecture. La correction elle-même est **neutre** : elle ne favorise aucun des deux candidats.

---

## 11. Verdict

Tous les tests exigés — SQLite, PostgreSQL, SQL Server, et concurrence sur les deux serveurs — **passent**.

```
P4-1 LOT D SQLITE TRACK      = PASS
P4-1 LOT D POSTGRESQL TRACK  = PASS
P4-1 LOT D SQL SERVER TRACK  = PASS
P4-1 LOT D CONCURRENCY       = PASS
P4-1 PRIMITIVES              = 14 / 14
P4-1 LOT D                   = PASS
P4-1 SPIKE                   = COMPLETE
P4-2 ADR                     = READY
```

**Lecture du verdict — portée exacte.** `P4-1 PRIMITIVES = 14 / 14` signifie : les **13** primitives déjà
acquises aux lots A et B restent acquises **sans être rejouées** (elles n'avaient pas à l'être), et la **14ᵉ**,
`TryCreateActiveLowStockAsync`, est désormais exercée via son **implémentation de production** sur
**PostgreSQL** et **SQL Server**, avec preuve de concurrence propre. `P4-1 SPIKE = COMPLETE` et
`P4-2 ADR = READY` sont émis parce que **le critère d'entrée énoncé pour le Lot D est atteint**.

> **Réserve explicite, à lire avec le verdict.** `P4-2 ADR = READY` signifie que la **porte du Lot D** est
> franchie, **pas** que MMV tourne aujourd'hui sur l'un des deux serveurs. Trois chantiers mesurés restent
> **ouverts et non résolus** : le **mapping `DateTime`** (§7 — le chemin EF normal est **refusé** par
> PostgreSQL avec `DateTime.Now`), les **filtres d'index booléens** PostgreSQL, et le **mapping monétaire
> `REAL`**.
>
> Ils constituent des **entrées obligatoires de l'ADR P4-2**, et **non** des travaux exécutés par elle : P4-2
> est une **décision documentaire**, elle n'implémente ni code, ni paquet, ni schéma, ni migration. Son rôle
> sur ces trois points est d'**évaluer leur impact sur le choix du provider**, puis d'**affecter leur
> implémentation aux phases P4 ultérieures**. Selon la décision retenue, cette affectation relèverait
> vraisemblablement de **P4-3** (composition et configuration du provider), **P4-4** (comportement des
> primitives à l'exécution et conversions de valeurs) et/ou **P4-5** (types de colonnes, index, schéma et
> migrations) — répartition **indicative**, que les preuves actuelles ne permettent pas de figer.
>
> **Aucun de ces trois sujets n'est corrigé par P4-2 elle-même**, aucun ne relevait du mandat Lot D, et aucun
> n'a été touché ici.

**Bloqueur restant au sens du Lot D : aucun.** `TryCreateActiveLowStockAsync` n'est plus une primitive en
échec.

**Contraintes respectées** : aucun commit ; aucun push ; aucun `git add .` / `git add -A` ; `src/MMV.App/**`
intact ; UI intacte ; contrats Domain et Application intacts ; aucune migration ni snapshot EF modifié ; aucune
référence provider ajoutée à Domain, Application ou Infrastructure ; aucun provider choisi ni éliminé ; aucune
ADR P4-2 créée ; garantie d'unicité de l'alerte LowStock active **ni déplacée vers l'application ni
affaiblie** ; aucun `check-then-act` applicatif ; aucune erreur base absorbée ; aucun mot de passe consigné ;
aucune donnée de production ou utilisateur employée.

---

*Rapport P4-1 Lot D — établi le 2026-07-26 sur la base `6f63460` / CI `30211361647` (verte). Aucun commit
effectué.*
