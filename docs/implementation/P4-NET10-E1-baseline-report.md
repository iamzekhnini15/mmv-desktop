# Rapport E-1 — baseline `.NET 8` de référence — lot `P4-NET10`

> **Nature : rapport d'exécution. Étape E-1 du**
> [plan d'exécution .NET 10](../architecture/net10-migration-execution-plan.md) **§4.**
> **AUCUN code, AUCUN `.csproj`, AUCUNE dépendance, AUCUNE migration modifiés.**
>
> Date d'exécution : **30 septembre 2026**. Branche : **`p4-net10`**.
> HEAD : **`32465f6f19ac469e826097a6676fc56ef3a94739`**.
> Poste : Windows 11 Home 10.0.26200, `C:\Program Files\dotnet` (x64).

## Verdict

```
P4-NET10 E-1 BASELINE = PASS
G-0 (résolution dotnet)  = LEVÉE
G-2 (baseline verte)     = SATISFAITE
G-3 (SDK 10 local)       = SATISFAITE — runner À VÉRIFIER (E-7)
BASELINE TESTS           = 1953   (et NON 1569 — voir §6)
MIGRATIONS SQLite        = 14
MIGRATIONS PostgreSQL    = 1
AVERTISSEMENTS DE BUILD  = 0
VULNÉRABILITÉS H/C       = 0
ARBRE DE TRAVAIL         = INCHANGÉ (fichiers suivis)
COMMIT                   = AUCUN
```

---

## 1. Écart de séquence constaté, et conduite tenue

**Le plan place E-1 avant E-2.** Or le commit E-0 `32465f6` — *« update global.json to SDK version 10.0.103 and
add NuGet.config »* — a déjà porté `global.json` à **`10.0.103`**, ce qui relève de **E-2** et non de E-0
(le plan, §4 E-0, ne prévoit pour E-0 que l'ajout de `NuGet.config`).

**Conséquence :** à HEAD, le SDK résolu n'est plus un SDK 8. Exécuter E-1 tel quel aurait produit une baseline
sur SDK 10 — c'est-à-dire **pas une baseline**, puisque son objet est précisément de figer l'état *avant* la
montée.

**Conduite tenue,** conformément à l'autorisation explicite de l'architecte (« modifier uniquement `global.json`
temporairement si nécessaire ») :

1. `global.json` rétabli à son contenu d'avant `32465f6` — `sdk.version: 8.0.417`, `rollForward` et
   `allowPrerelease` **inchangés** — par `git show 32465f6~1:global.json` ;
2. batterie **VP-1 … VP-7** exécutée intégralement sur SDK 8 ;
3. `global.json` remis à l'état de HEAD par `git checkout -- global.json`.

**Contrôle d'intégrité :** l'empreinte du blob `global.json` est **identique avant et après** —
`53b4c27791345461babd7477ea7759718b6eddc8`. Voir §10.

> **Point à trancher par l'architecte (non bloquant pour E-1) :** `32465f6` mêle E-0 (NuGet.config) et E-2
> (`global.json`), alors que le §6.2 du plan impose **un commit par étape, dans l'ordre du §4**. E-1 est
> néanmoins produite ci-dessous, complète et verte. **Voir aussi §6.2 : cette épingle n'épingle rien.**

---

## 2. VP-1 — Résolution de l'outillage

```
$ which dotnet
/c/Program Files/dotnet/dotnet

$ dotnet --list-sdks
8.0.417 [C:\Program Files\dotnet\sdk]
10.0.102 [C:\Program Files\dotnet\sdk]
10.0.103 [C:\Program Files\dotnet\sdk]
10.0.401 [C:\Program Files\dotnet\sdk]

$ dotnet --version
8.0.417

$ dotnet nuget list source
Sources inscrites :
  1.  nuget.org [Activé]
      https://api.nuget.org/v3/index.json
```

**VP-1 = PASS.**

**G-0 est levée.** `dotnet` résout vers l'installation **x64 porteuse des SDK**, et non vers le `dotnet` x86
sans SDK décrit au §2.1 du plan. Aucun chemin complet n'a été employé : la résolution du `PATH` est correcte
dans le shell du lot.

**G-1 est vérifiée dans les faits.** Le `NuGet.config` versionné avec son `<clear />` est **opérant** : la seule
source restante est `nuget.org`. Le « dossier hors ligne de Visual Studio » du §2.1 du plan **a disparu de la
résolution**. C'est la preuve d'exécution que E-0 demandait.

### 2.1 Corrections à apporter à l'inventaire §2.1 du plan

| Le plan (22/09/2026) affirme | Constat du 30/09/2026 |
|---|---|
| SDK 8 installé : **8.0.425** | **8.0.417** — c'est aussi la version qu'épinglait `global.json` |
| SDK 10 installé : **10.0.103** | **10.0.102, 10.0.103 et 10.0.401** — trois bandes |
| Runtimes `Microsoft.NETCore.App` **10.0.3** et **8.0.31** | **10.0.2, 10.0.3, 10.0.12** et **8.0.23** |
| `dotnet` résout vers l'installation x86 sans SDK (**G-0 NON SATISFAITE**) | **résolution correcte** — G-0 levée |
| La seule source NuGet est un dossier hors ligne | **`nuget.org` seule** — corrigé par E-0 |

---

## 3. VP-2 — Restauration

```
$ dotnet restore MMV.sln
→ exit 0
```

| Contrôle | Résultat |
|---|---|
| Code de sortie | **0** |
| Projets restaurés | **8 / 8** |
| Avertissements `NU1605` (rétrogradation) | **0** |
| Avertissements, tous codes confondus | **0** |

**VP-2 = PASS.**

---

## 4. VP-3 — Construction

```
$ dotnet build MMV.sln --no-restore -c Debug
La génération a réussi.
    0 Avertissement(s)
    0 Erreur(s)
Temps écoulé 00:00:11.46
```

**VP-3 = PASS.**

### 4.1 Archivage §5.2-3 — avertissements de build

**La liste des avertissements de build est VIDE.** `0 Avertissement(s)`, `0 Erreur(s)`, sur les 8 projets.

> **C'est la donnée la plus exploitable de ce rapport.** La baseline d'avertissements étant **nulle**, tout
> avertissement apparaissant en E-2 (SDK 10), E-4 (TFM `net10.0`) ou E-6 (EF 10) est **nécessairement nouveau**
> et imputable à l'étape qui l'a fait apparaître. Aucune discussion « préexistant ou non » n'est possible.
> Le §1.2 du plan interdit de les corriger : ils sont **relevés et comparés**, jamais traités.

### 4.2 Inventaire des projets construits

Les 8 projets de `MMV.sln` produisent en `bin/Debug/**/net8.0/**`.

| Projet | TFM | `LangVersion` | Dans `MMV.sln` |
|---|---|---|---|
| `src/MMV.Domain` | `net8.0` | 12.0 | oui |
| `src/MMV.Application` | `net8.0` | 12.0 | oui |
| `src/MMV.Infrastructure` | `net8.0` | 12.0 | oui |
| `src/MMV.Infrastructure.PostgreSQL.Migrations` | `net8.0` | 12.0 | oui |
| `src/MMV.App` | `net8.0` | 12.0 | oui |
| `tests/MMV.Domain.Tests` | `net8.0` | 12.0 | oui |
| `tests/MMV.Application.Tests` | `net8.0` | 12.0 | oui |
| `tests/MMV.App.Tests` | `net8.0` | **aucune** | oui |
| `spikes/P4.ProviderComparison/MMV.P4.ProviderComparison` | `net8.0` | 12.0 | **non** |
| `spikes/P4.ProviderComparison/Worker/MMV.P4.Worker` | `net8.0` | 12.0 | **non** |

**Conforme au §2.2 du plan** : 8 projets en solution, 2 spikes hors solution, tous en `net8.0`,
`LangVersion 12.0` partout sauf `MMV.App.Tests`. **Les 10 projets sont à porter** (E-4 pour les 8, E-8 pour
les 2 spikes).

---

## 5. VP-4 — Tests

```
$ dotnet test MMV.sln --no-build -c Debug
Réussi!  - échec : 0, réussite :  260, ignorée(s) : 0, total :  260, durée :  1 s - MMV.App.Tests.dll (net8.0)
Réussi!  - échec : 0, réussite :  628, ignorée(s) : 0, total :  628, durée : 19 s - MMV.Application.Tests.dll (net8.0)
Réussi!  - échec : 0, réussite : 1065, ignorée(s) : 0, total : 1065, durée : 24 s - MMV.Domain.Tests.dll (net8.0)
→ exit 0
```

### 5.1 Archivage §5.2-1 — le compte exact

| Assemblage | Réussis | Échecs | Ignorés | Total |
|---|---|---|---|---|
| `MMV.Domain.Tests` | 1065 | 0 | 0 | **1065** |
| `MMV.Application.Tests` | 628 | 0 | 0 | **628** |
| `MMV.App.Tests` | 260 | 0 | 0 | **260** |
| **TOTAL** | **1953** | **0** | **0** | **1953** |

```
BASELINE E-1 = 1953 tests   (Domain 1065 · Application 628 · App 260)
                0 échec · 0 ignoré
```

**VP-4 = PASS.** **C'est ce nombre — 1953 — qui fait foi pour toutes les étapes E-2 à E-9**, et non celui
qu'annonçait le plan. Voir §6.

---

## 6. L'écart 1569 / 1953 — instruit et clos

**Le plan attendait 1569 tests ; la mesure en donne 1953.** Cet écart a été instruit avant toute conclusion,
car le §9.3 du plan fait d'un changement de compte de tests un motif d'arrêt.

**Ce n'est pas une régression, ni un test fantôme : c'est une donnée périmée dans le plan.**

| Élément | Constat |
|---|---|
| Origine du « 1569 » | Baseline **P4-4C**, citée par le plan §2.2 sous l'étiquette **`DOC`** — *jamais mesurée par le plan*, seulement reprise de la [roadmap P4 §6](../architecture/P4-multi-poste-roadmap.md) |
| Ce qui s'est passé depuis P4-4C | **P4-5D** (stratégie temporelle / dates civiles) et **P4-5E** (architecture de migration PostgreSQL) ont été livrés, ajoutant **+384** tests |
| Preuve indépendante | Le [rapport de vérification finale P4-5](P4-5-final-verification-report.md) enregistre déjà **1953 / 1953 verts, 0 échec, 0 ignoré** |
| Répartition qu'il enregistre | **260 + 628 + 1065** — **strictement identique** à la mesure de E-1 |
| Stabilité déjà établie | Ce rapport note le compte « **identique à C7, C8 et C9** » |

**La mesure de E-1 reproduit donc à l'unité près une baseline déjà constatée et archivée à HEAD, par un autre
lot, à une autre date.** Le plan .NET 10 a été rédigé le 22/09/2026 en reprenant un chiffre P4-4C sans le
rafraîchir contre P4-5.

> **Conséquence pour la suite du lot — à acter par l'architecte.** Les occurrences de **1569** dans le plan
> d'exécution sont **périmées** et doivent être lues **1953** : §2.2 (tableau d'état), §4 E-1 (*« baseline
> attendue »*), §5.1 **VP-4** (*« 1569 réussis »*), §5.2-1 (*« attendu »*), §9.1 **G-2**, §9.4-4 (critère de
> sortie). **Le critère de sortie du lot devient : 1953 tests verts, aucun ajouté, aucun retiré, aucun ignoré.**
> La règle d'arrêt du §9.3 reste **inchangée dans son esprit** : c'est tout écart **par rapport à 1953**, à
> partir de maintenant, qui déclenche l'arrêt.

### 6.1 Ce que cet écart ne remet pas en cause

La règle du §9.3 vise les variations **à l'intérieur du lot**, entre E-1 et les étapes suivantes. Ici l'écart
est **antérieur au lot** et entièrement expliqué par des livraisons P4-5 déjà commitées et vérifiées.
**G-2 est donc satisfaite** : la baseline est verte, mesurée, écrite, et corroborée par une source indépendante.

### 6.2 Deuxième constat sur `global.json` — l'épingle n'épingle pas

Après remise en état, **`global.json` demande `10.0.103` mais le SDK réellement résolu est `10.0.401`** :

```
$ cat global.json          →  "version": "10.0.103", "rollForward": "latestFeature"
$ dotnet --version         →  10.0.401
```

`rollForward: latestFeature` sélectionne la **bande de fonctionnalités la plus élevée ≥ celle demandée** :
`10.0.103` (bande `1xx`) autorise donc `10.0.401` (bande `4xx`), qui est installée.

**Portée.** Le plan, §4 **E-2**, attend que `dotnet --version` renvoie une **`10.0.1xx`**. Ce ne sera pas le cas
sur ce poste. Trois conséquences :

1. **le lot ne sera pas exécuté sur le SDK qu'il croit épingler** — ni localement, ni forcément sur le runner ;
2. la CI utilise `actions/setup-dotnet@v4` avec **`global-json-file: global.json`** : le runner installera une
   `10.0.1xx` et n'aura probablement **pas** `10.0.401`. **Poste et CI ne compileront donc pas avec le même
   SDK** — exactement le genre d'écart qui rend un échec de CI inexplicable, et que G-0 visait par ailleurs ;
3. le §8 du plan (retour arrière) suppose qu'une ligne de `global.json` suffit à revenir en arrière ; avec
   `latestFeature`, elle ne contraint que la borne **basse**.

**Ce point relève de E-2, pas de E-1.** Il est consigné ici parce que E-1 est le seul moment où il pouvait être
observé. **Aucune action n'a été prise.** Options pour l'architecte : passer `rollForward` à `patch` ou
`disable`, viser explicitement `10.0.401`, ou assumer l'écart par écrit.

---

## 7. VP-5 et VP-6 — dérive du modèle EF, et archivage §5.2-2

Outil : `dotnet-ef` **8.0.27**, restauré par `dotnet tool restore` depuis
[`.config/dotnet-tools.json`](../../.config/dotnet-tools.json) (`rollForward: false`).
**Aucune commande ne contacte de base de données.**

### 7.1 VP-5 — chaîne SQLite

```
$ dotnet ef migrations has-pending-model-changes \
    --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build
No changes have been made to the model since the last migration.
→ exit 0
```

**VP-5 = PASS.**

### 7.2 Archivage §5.2-2 (a) — chaîne SQLite, **14** migrations

Obtenues par `dotnet ef migrations list --no-connect --project src/MMV.Infrastructure
--startup-project src/MMV.Infrastructure --no-build`, **collées telles quelles, dans l'ordre** :

```
20260127184542_InitialCreate
20260129192001_AddProductEntryDate
20260201181136_ProductSchemaRefactoring
20260202005050_AddNotifications
20260212164646_AddCounterSaleFieldsToOrder
20260212173313_AddDepositAndRemainingAmountToOrder
20260212220902_RestoreSaleOrderSeparation
20260611114307_FixDateTimeDefaultValues
20260612071633_AddDocumentSequences
20260713215132_AddCustomerArchivingAndProtectHistory
20260717183027_AddProductNormalizedReferenceAndProtectHistory
20260719003826_AddWorkshopSheets
20260720204330_AddNotificationResolution
20260721134634_AddNormalizedUsernameAndSecureLocalUsers
```

**14 migrations** — conforme au §2.2 du plan.

> `Pending status not shown. Unable to determine which migrations have been applied.` est **attendu et normal** :
> `--no-connect` interdit l'accès à la base. Ce n'est pas une erreur ; le code de sortie est **0**.

### 7.3 VP-6 — chaîne PostgreSQL, avec sa garde anti-faux-vert

Variable `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` posée **sur ces seules commandes**, comme en CI.

**(1) Chaîne réellement contrôlée** — `migrations list --no-connect --json --prefix-output`, exit **0** :

```json
[
  {
    "id": "20260922001219_InitialPostgreSqlBaseline",
    "name": "InitialPostgreSqlBaseline",
    "safeName": "InitialPostgreSqlBaseline",
    "applied": null
  }
]
```

| Contrôle de la garde | Attendu | Obtenu |
|---|---|---|
| `InitialPostgreSqlBaseline` **présente** | oui | **oui** ✔ |
| `InitialCreate` (SQLite) **absente** | oui | **absente** ✔ |
| Nombre de migrations | 1 | **1** ✔ |

**La garde est opérante : la chaîne contrôlée est bien la chaîne PostgreSQL**, et non un repli silencieux sur
SQLite.

**(2) Dérive du modèle** :

```
$ dotnet ef migrations has-pending-model-changes \
    --project src/MMV.Infrastructure.PostgreSQL.Migrations \
    --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations --no-build
No changes have been made to the model since the last migration.
→ exit 0
```

**VP-6 = PASS.**

### 7.4 Archivage §5.2-2 (b) — chaîne PostgreSQL, **1** migration

```
20260922001219_InitialPostgreSqlBaseline
```

> **C'est l'archive qui rendra E-6 décidable.** Le §5.3 du plan exige, après la montée d'EF Core, la
> **comparaison littérale** de `migrations list` avec ces deux listes : **mêmes identifiants, même ordre, même
> nombre**. Un `has-pending-model-changes` vert ne suffit pas. **Toute différence est G-4 : arrêt immédiat,
> retour à EF 8, escalade, et aucune migration générée.**

---

## 8. VP-7 — Vulnérabilités

```
$ dotnet list MMV.sln package --vulnerable --include-transitive
Le projet spécifié 'MMV.Domain' n'a aucun package vulnérable compte tenu des sources actuelles.
Le projet spécifié 'MMV.Infrastructure' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.App' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.Application' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.Domain.Tests' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.App.Tests' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.Application.Tests' n'a aucun package vulnérable ...
Le projet spécifié 'MMV.Infrastructure.PostgreSQL.Migrations' n'a aucun package vulnérable ...
→ exit 0
```

**VP-7 = PASS.** **Aucune** vulnérabilité, tous niveaux confondus — donc a fortiori aucune High/Critical, sur
les **8** projets, **transitifs inclus**.

---

## 9. Archivage §5.2-4 — versions résolues des paquets sensibles

Relevé par `dotnet list MMV.sln package --include-transitive`.

| Paquet | Version résolue | Statut |
|---|---|---|
| **`System.Text.Json`** | **8.0.6** | épingle de sécurité (App + Infrastructure). **Devient nocive en `net10.0`** — traitée en **E-5** (§3.5 du plan) |
| **`SQLitePCLRaw.bundle_e_sqlite3`** | **3.0.0** | épingle de sécurité (Infrastructure) |
| **`SQLitePCLRaw.lib.e_sqlite3`** | **3.50.3** | **binaire SQLite natif. Déjà ≥ 3.50.2**, le seuil visé par E-5 |
| `SQLitePCLRaw.core` / `.config` / `.provider` | 3.0.0 | transitifs |
| **`Npgsql`** | **8.0.6** | couplé au runtime — **E-6** |
| **`Npgsql.EntityFrameworkCore.PostgreSQL`** | **8.0.11** | couplé à EF Core — **E-6** |
| **`Microsoft.EntityFrameworkCore`** (+ `.Abstractions`, `.Analyzers`, `.Relational`, `.Design`, `.Sqlite`, `.Sqlite.Core`) | **8.0.27** | couplé au runtime — **E-6** |
| **`dotnet-ef`** (outil) | **8.0.27** | `rollForward: false` — **E-6** |
| **`Avalonia`** | **11.2.8** | **NE BOUGE PAS** (§1.2, §3.3 du plan) |

> **Donnée utile pour E-5.** `SQLitePCLRaw.lib.e_sqlite3` est **déjà en 3.50.3**, au-dessus du seuil `3.50.2`
> visé par E-5. Le §3.5 du plan pourra le constater plutôt que le provoquer. L'épingle `bundle_e_sqlite3 3.0.0`
> reste, elle, à justifier ligne par ligne au titre du §9.4-6.

> **Les trois épingles du §2.2 du plan sont confirmées**, à une réserve près : **`Tmds.DBus.Protocol` 0.21.3**
> (annoncée sur `MMV.App`) **n'apparaît pas** dans le relevé. À vérifier en E-5 — l'épingle a pu être retirée
> depuis le 22/09, ou n'être conditionnée qu'à un RID non-Windows.

---

## 10. Intégrité de l'arbre de travail — `git diff` avant / après

### 10.1 Avant E-1

```
$ git rev-parse HEAD
32465f6f19ac469e826097a6676fc56ef3a94739

$ git diff
(vide)

$ git status --porcelain
?? design-handoff/
?? design/
?? docs/ui/

$ git hash-object global.json
53b4c27791345461babd7477ea7759718b6eddc8
```

### 10.2 Pendant E-1 — la seule modification, et elle est revenue

```
$ git diff --stat
 global.json | 2 +-
 1 file changed, 1 insertion(+), 1 deletion(-)
```

Diff porté **uniquement** par `sdk.version` : `10.0.103` → `8.0.417`. `rollForward` et `allowPrerelease`
**inchangés**. **Aucun autre fichier touché à aucun moment.**

### 10.3 Après E-1

```
$ git checkout -- global.json

$ git hash-object global.json
53b4c27791345461babd7477ea7759718b6eddc8      ← IDENTIQUE au §10.1

$ git rev-parse HEAD
32465f6f19ac469e826097a6676fc56ef3a94739      ← IDENTIQUE

$ git diff --stat
(vide)                                        ← aucun fichier suivi modifié

$ git status --porcelain
?? design-handoff/
?? design/
?? docs/ui/                                   ← IDENTIQUE au §10.1

$ dotnet --version
10.0.401
```

### 10.4 Conclusion d'intégrité

| Contrôle | Avant | Après | Verdict |
|---|---|---|---|
| HEAD | `32465f6` | `32465f6` | **identique** |
| `git diff` (suivis) | vide | vide | **identique** |
| Blob `global.json` | `53b4c27…` | `53b4c27…` | **identique** |
| Non suivis | 3 répertoires | 3 répertoires | **identique** |
| Commits créés | — | **0** | **aucun** |

**Le dépôt est rigoureusement dans l'état où E-1 l'a trouvé.** Les seuls trois répertoires non suivis
(`design/`, `design-handoff/`, `docs/ui/`) préexistaient à E-1 : ils sont **hors périmètre du lot** et n'ont
pas été touchés.

> **Seule exception, et elle est postérieure aux relevés ci-dessus : le présent rapport**, créé en dernier sous
> `docs/implementation/`. Il apparaît donc comme un **quatrième** élément non suivi. Aucun fichier **suivi**
> n'est modifié.

> **Note d'environnement, sans effet sur le dépôt.** `bin/` et `obj/` contiennent désormais des artefacts
> produits par le **SDK 8** et sont ignorés par Git. La prochaine commande du lot sous SDK 10 les régénérera.
> **Aucune action requise.**

---

## 11. Synthèse de la batterie

| # | Contrôle | Commande | Attendu | Obtenu | Verdict |
|---|---|---|---|---|---|
| **VP-1** | Outillage | `dotnet --list-sdks` / `--version` | SDK 8 résolu | **8.0.417**, x64 | **PASS** |
| **VP-2** | Restauration | `dotnet restore MMV.sln` | succès, 0 `NU1605` | exit 0, **0** `NU1605`, 0 avert. | **PASS** |
| **VP-3** | Construction | `dotnet build MMV.sln --no-restore -c Debug` | succès, avert. relevés | **0 avert., 0 err.** | **PASS** |
| **VP-4** | Tests | `dotnet test MMV.sln --no-build -c Debug` | compte exact, 0 échec, 0 ignoré | **1953 / 0 / 0** | **PASS** |
| **VP-5** | Dérive SQLite | `ef … has-pending-model-changes` | exit 0 | exit **0** | **PASS** |
| **VP-6** | Dérive PostgreSQL + garde | `ef … list --no-connect` puis `has-pending…` | exit 0, garde opérante | exit **0**, garde **opérante** | **PASS** |
| **VP-7** | Vulnérabilités | `dotnet list … --vulnerable --include-transitive` | aucune High/Critical | **aucune, tous niveaux** | **PASS** |

**Batterie VP-1 … VP-7 : 7 / 7 PASS.**

---

## 12. Conditions de GO — état après E-1

| # | Condition | État |
|---|---|---|
| **G-0** | `dotnet` résout vers une installation portant un SDK | **LEVÉE** — §2 |
| **G-1** | Source NuGet ouverte par `NuGet.config` versionné | **SATISFAITE et vérifiée à l'exécution** — §2 |
| **G-2** | Baseline verte et écrite, migrations archivées | **SATISFAITE** — §4 à §9. **Compte de référence : 1953**, non 1569 |
| **G-3** | SDK 10 disponible localement **et** sur le runner | **local : SATISFAITE** (10.0.102 / 10.0.103 / 10.0.401). **Runner : `À VÉRIFIER`** en E-7 — voir la réserve du §6.2 |
| **G-5** | Versions cibles revérifiées **le jour même** sur `api.nuget.org` | **NON FAITE** — hors périmètre E-1, **à faire avant E-6** |
| **G-6** | Branche `p4-net10` depuis `p4-multi-poste`, base poussée | **branche confirmée** (`p4-net10`, HEAD `32465f6`). **Filiation et push de la base non vérifiés par E-1** |
| **G-7** | Séquence avant P4-5F et P4-6B | **hors périmètre E-1** |
| **G-4** | *(condition d'arrêt de E-6, pas de départ)* | **non applicable** à ce stade |

---

## 13. Points remis à l'architecte

**Aucun n'est bloquant pour E-1, qui est verte. Tous appellent une décision avant d'avancer.**

1. **Le compte de référence est 1953, pas 1569.** Le plan doit être corrigé en 6 endroits (§6). **Le critère de
   sortie §9.4-4 devient : 1953 tests verts.** *— décision documentaire.*
2. **`rollForward: latestFeature` vide l'épingle `10.0.103` de son effet** : le poste résout `10.0.401`, la CI
   installera une `10.0.1xx`. **Poste et runner ne compileraient pas avec le même SDK** (§6.2).
   *— décision technique, à prendre **avant E-2**.*
3. **`32465f6` mêle E-0 et E-2** au regard du §6.2 du plan (§1). *— à acter ou à corriger.*
4. **`Tmds.DBus.Protocol` 0.21.3 est introuvable** dans le relevé, alors que le plan la liste parmi les trois
   épingles de sécurité (§9). *— à instruire en E-5.*
5. **`SQLitePCLRaw.lib.e_sqlite3` est déjà en 3.50.3**, au-dessus du seuil de E-5 (§9). *— E-5 constate au lieu
   de provoquer.*
6. **G-5 et G-6 ne sont pas couvertes par E-1** : relevé `api.nuget.org` du jour, filiation de branche et push
   de la base restent à faire (§12).

---

## 14. Conclusion

**La baseline .NET 8 est établie, verte, reproductible et archivée.** Les sept contrôles passent, le dépôt est
intact, aucun commit n'a été créé.

Les deux archives qui rendront la suite interprétable sont en place : **une liste d'avertissements de build
vide** — donc tout avertissement futur est nouveau — et **les deux chaînes de migrations, 14 et 1**, qui seules
permettront de prononcer G-4 en E-6.

**Le seul écart de fond est documentaire** : le plan attendait 1569 tests, le dépôt en porte **1953** depuis
P4-5, ce qu'un rapport antérieur et indépendant confirme à l'unité près.

```
P4-NET10 E-1 = PASS
G-2 = SATISFAITE
BASELINE DE RÉFÉRENCE = 1953 tests · 14 migrations SQLite · 1 migration PostgreSQL · 0 avertissement
PROCHAINE ÉTAPE = E-2 — sous réserve des points 1 et 2 du §13
ARRÊT DEMANDÉ PAR LA CONSIGNE : RESPECTÉ
```
