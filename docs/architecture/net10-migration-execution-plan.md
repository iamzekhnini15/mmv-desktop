# Plan d'exécution — montée de MMV vers .NET 10 (LTS) — lot `P4-NET10`

> **Nature : plan d'exécution. Strictement documentaire.**
> **AUCUN CODE MODIFIÉ par ce document.** Aucun `.csproj`, aucun `global.json`, aucun paquet, aucune CI, aucune
> migration. Il décrit ce qu'un lot futur fera, dans quel ordre, et à quelles conditions il doit s'arrêter.
>
> Date : 22 septembre 2026. Branche : `p4-multi-poste`. HEAD de référence :
> `ccbeb3259444d26264e20323058764dd59318e1c` (= `origin/p4-multi-poste`).
> Origine : [ADR-APP-DISTRIBUTION-001](adr-app-distribution-001-installation-and-updates.md) **DI-8** et son
> obligation **OI-10** — .NET 10 (LTS) avant la Release V1, aucune Release V1 sur .NET 8.
> Lot inscrit dans la [roadmap P4](P4-multi-poste-roadmap.md), §P4-NET10.
> **Le dépôt réel prime toujours sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `CODE` | constaté par lecture du dépôt à HEAD |
| `LOCAL` | constaté sur le poste de développement le 22/09/2026 (commande exécutée) |
| `NUGET` | relevé sur `api.nuget.org` le 22/09/2026 — **à revérifier au moment de l'exécution** |
| `À VÉRIFIER` | **non établi.** À prouver par l'exécution, jamais à supposer |

---

## 1. Objet et périmètre

### 1.1 Ce que le lot fait

Porter le dépôt de `net8.0` à `net10.0`, avec les paquets **couplés au runtime** : EF Core, Npgsql, outil
`dotnet-ef`, SDK de test. **Rien d'autre.**

### 1.2 Ce que le lot ne fait pas — et ne doit pas faire

| Interdit | Pourquoi |
|---|---|
| tout **changement du modèle EF** | déclencherait la règle de double migration ([CONTRIBUTING.md](../../CONTRIBUTING.md)) et une migration sur **deux** chaînes. Ce n'est plus une montée de runtime |
| toute **migration nouvelle** | idem. Si EF 10 en exige une, **le lot s'arrête** (voir G-4) |
| la montée d'**Avalonia vers 12.x** | changement majeur d'interface graphique, avec ses propres ruptures. **Lot distinct** (§3.3) |
| la montée de **`LangVersion`** vers C# 14 | aucun bénéfice ici, et chaque nouvelle syntaxe est un risque gratuit. Reste à **12.0** (§3.4) |
| la **publication autonome** `win-x64` et l'empaquetage Velopack | relèvent de **P8** (DI-1, DI-2) |
| toute **refactorisation**, tout renommage, toute correction d'avertissement préexistant | **une montée de version n'est pas une occasion de refactoriser.** Un diff mêlé est un diff non revu |

### 1.3 Motif, et pourquoi la date compte

**.NET 8 sort de support le 10 novembre 2026** ; .NET 10 (LTS) est supporté jusqu'au 14 novembre 2028
(`EXTERNE S22` d'ADR-APP-DISTRIBUTION-001). En publication autonome (DI-1), **le runtime est embarqué dans le
paquet livré au client** : une Release V1 sur .NET 8 livrerait un runtime privé de correctifs de sécurité, à des
postes qui manipulent des **données de santé**.

### 1.4 Effet sur P4 — pourquoi ce lot est architectural malgré les apparences

**Il tranche Q-6 d'[ADR-PROD-DB-009](ADR-PROD-DB-009.md) par disponibilité.** DP-2 retient une **famille** de
mécanismes de verrouillage — « natif PostgreSQL » — et non un appel précis, **exactement** pour ne pas fermer la
voie du verrou de migration natif d'EF Core ≥ 9 (option LK-5). Exécuter ce lot **avant** P4-6B rend la question
décidable sur pièces : soit le verrou natif convient et `MMV.DatabaseManager` s'en sert, soit il ne convient pas
et l'outil implémente `pg_advisory_lock`. **Aujourd'hui, l'implémentation Npgsql de ce verrou est `UNKNOWN`**
(ADR-PROD-DB-009 §4.2.1) : c'est à vérifier, pas à supposer.

---

## 2. État constaté

### 2.1 Poste de développement

| Constat | Preuve |
|---|---|
| **SDK .NET 10 déjà installé : `10.0.103`**, à côté de `8.0.425` | `LOCAL` `dotnet --list-sdks` |
| Runtimes présents : `Microsoft.NETCore.App` **10.0.3** et 8.0.31 ; `Microsoft.WindowsDesktop.App` 10.0.3 et 8.0.31 | `LOCAL` `dotnet --list-runtimes` |
| `dotnet --version` renvoie **8.0.425** : c'est [global.json](../../global.json) qui l'impose, pas une absence de SDK | `LOCAL` |
| **La seule source NuGet enregistrée est un dossier hors ligne** : `Microsoft Visual Studio Offline Packages` | `LOCAL` `dotnet nuget list source` |
| **`nuget.org` est joignable** : `HTTP 200` en 0,38 s | `LOCAL` `curl api.nuget.org/v3/index.json` |
| Le cache local `~/.nuget/packages` ne contient **que du 8.x** pour EF Core, Npgsql et leurs satellites ; Avalonia y est en 11.1.0 et 11.2.8 | `LOCAL` |

> **Conclusion la plus importante de ce paragraphe.** La contrainte d'
> [ADR-PROD-DB-008 §2.4](adr-prod-db-008-postgresql-integration-testing.md) — « la seule source NuGet est un cache
> hors ligne, tout paquet nouveau y est non restaurable » — est une contrainte **de configuration**, pas de
> connectivité. Le réseau répond. **L'obstacle est une source non enregistrée, et le lever est une décision**
> (voir **G-1**), pas une manipulation anodine : cela change ce que le poste peut télécharger et exécuter.

### 2.2 Dépôt

| Constat | Preuve |
|---|---|
| **8 projets dans [MMV.sln](../../MMV.sln)** : 5 sous `src/`, 3 sous `tests/` | `CODE` |
| **Tous en `net8.0`**, tous en `LangVersion 12.0` sauf `MMV.App.Tests` qui n'en déclare aucune | `CODE` |
| **2 projets de spike hors solution**, en `net8.0` : `MMV.P4.ProviderComparison` et `MMV.P4.Worker` | `CODE` |
| `MMV.P4.ProviderComparison` référence `MMV.Domain` **et** `MMV.Infrastructure` par `ProjectReference` | `CODE` [MMV.P4.ProviderComparison.csproj:42-43](../../spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj#L42-L43) |
| [global.json](../../global.json) épingle le SDK **8.0.417**, `rollForward: latestFeature`, `allowPrerelease: false` | `CODE` |
| [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json) épingle **`dotnet-ef` 8.0.27**, `rollForward: false` | `CODE` |
| **Aucun `packages.lock.json`**, **aucun `NuGet.config`** dans le dépôt | `CODE` |
| [Directory.Build.props](../../Directory.Build.props) ne porte **que** l'audit NuGet. **Il est à la racine : il s'applique aussi aux spikes** | `CODE` |
| La CI utilise `actions/setup-dotnet@v4` avec **`global-json-file: global.json`** | `CODE` [ci.yml](../../.github/workflows/ci.yml) |
| La CI exécute **deux contrôles de dérive EF** : chaîne SQLite, puis chaîne PostgreSQL avec garde anti-faux-vert | `CODE` |
| Instantané de la chaîne PostgreSQL en `ProductVersion 8.0.27` ; **14 migrations SQLite**, **1 migration PostgreSQL** | `CODE` |
| **Trois épingles de sécurité** : `System.Text.Json` 8.0.6 (App + Infrastructure), `Tmds.DBus.Protocol` 0.21.3 (App), `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 (Infrastructure) | `CODE` |
| Baseline de tests **1569** (P4-4C) | `DOC` [roadmap P4 §6](P4-multi-poste-roadmap.md) |

### 2.3 Deux conséquences que l'état constaté impose

**(a) Les spikes casseront si on les oublie.** `MMV.P4.ProviderComparison` est en `net8.0` et référence deux
projets de `src/` par `ProjectReference`. Dès que `src/` passe en `net10.0`, **ce projet ne compile plus** : un
projet `net8.0` ne peut pas référencer un projet `net10.0`. Comme il est **hors de [MMV.sln](../../MMV.sln) et hors
CI**, **rien ne le signalera** — la rupture sera silencieuse jusqu'au jour où quelqu'un rejouera le spike P4-1.
Traité en **E-8**.

**(b) L'épingle `System.Text.Json` 8.0.6 devient nocive.** Sur `net8.0`, elle **remonte** un transitif au-delà de
deux avis High. Sur `net10.0`, `System.Text.Json` fait partie du framework partagé en version 10.x : conserver une
`PackageReference` explicite en **8.0.6** ferait **redescendre** l'assembly de deux versions majeures sous celle
du runtime. Une épingle de sécurité se transformerait en régression de sécurité. Traité en **E-5**.

---

## 3. Dépendances — état et cible

### 3.1 SDK, runtime, outillage

| Élément | Actuel | Cible | Contrainte |
|---|---|---|---|
| SDK (global.json) | 8.0.417 | **10.0.103** | version **installée localement** (`LOCAL`). `rollForward: latestFeature` et `allowPrerelease: false` **conservés**. La CI la suivra sans modification de workflow, via `global-json-file` |
| `TargetFramework` | `net8.0` | **`net10.0`** | 8 projets de la solution, **plus** les spikes (E-8) |
| `dotnet-ef` | 8.0.27 | **10.0.x**, aligné sur EF Core | `rollForward: false` **conservé**. Le majeur de l'outil doit suivre celui d'EF Core |

### 3.2 EF Core et Npgsql — le couple contraint

| Paquet | Actuel | Cible recommandée | Preuve / contrainte |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore.*` | 8.0.27 | **10.0.x, x ≥ 4** | `NUGET` — dernier relevé : **10.0.12**. EF Core 10 **ne cible que `net10.0`** : il **exige** le changement de TFM, les deux ne se séparent pas |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.27 | **même version qu'EF Core** | `NUGET` — tire `SQLitePCLRaw.bundle_e_sqlite3` **2.1.12** : voir §3.5 |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.27 | **même version qu'EF Core** | présent dans 3 projets, toujours en `PrivateAssets=all` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.11 | **10.0.x** | `NUGET` — dernier relevé : **10.0.3** |
| `Npgsql` (transitif) | 8.0.6 (cache) | **10.0.x** | arrive par le provider |

> **Contrainte dure, relevée sur le `.nuspec`** (`NUGET`) : `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**
> dépend de `Microsoft.EntityFrameworkCore` **`[10.0.4, 11.0.0)`**. Une cible EF Core **inférieure à 10.0.4** est
> donc **impossible**. Les deux paquets se choisissent ensemble, et leurs versions exactes se **revérifient au
> moment de l'exécution** : ce tableau a la fraîcheur du 22/09/2026.

### 3.3 Avalonia — **ne bouge pas**, et c'est la bonne nouvelle du plan

| Paquet | Actuel | Décision |
|---|---|---|
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` | 11.2.8 | **inchangés** |
| `FluentAvaloniaUI` | 2.1.0 | **inchangé** |
| `CommunityToolkit.Mvvm` | 8.2.2 | **inchangé** |
| `Projektanker.Icons.Avalonia` | 9.6.2 | **inchangé** — c'est déjà la dernière version publiée (`NUGET`) |

**Pourquoi rien ne bouge.** Les assemblages d'Avalonia 11.2.8 embarquent `lib/net8.0`, `lib/net6.0` et
`lib/netstandard2.0` (`LOCAL`) : un consommateur `net10.0` résout `net8.0` sans difficulté. Même constat pour
`FluentAvaloniaUI` 2.1.0, `CommunityToolkit.Mvvm` 8.2.2 (`net6.0` + `netstandard2.x`) et
`Projektanker.Icons.Avalonia` 9.6.2 (`netstandard2.0`).

**Ce que cela retire du lot :** la couche interface graphique — la plus volumineuse et la plus difficile à tester
automatiquement — **n'est pas touchée**. Le risque de régression visuelle tombe à zéro par construction.

**Ce qui reste à décider ailleurs.** Avalonia publie aujourd'hui une série **12.x** (`NUGET` : 12.1.3, et
`Avalonia.Controls.DataGrid` seulement en 12.1.2 — **les satellites ne sont pas tous au même niveau**). La durée
de support de la série 11.2.x est **`À VÉRIFIER`**. Monter en 12.x est un **lot distinct**, à instruire pour
lui-même ; le mêler à la montée de runtime rendrait tout diagnostic impossible.

### 3.4 Langage

`LangVersion 12.0` est **conservé**. C# 12 reste valide sur `net10.0`, et rien dans ce lot n'exige de syntaxe
nouvelle. Monter vers C# 14 se décidera séparément, quand un besoin le justifiera.

### 3.5 Épingles de sécurité — les trois cas sont différents

| Épingle | Actuel | Décision | Motif |
|---|---|---|---|
| `System.Text.Json` (App, Infrastructure) | 8.0.6 | **RETIRER la `PackageReference`** | sur `net10.0`, le framework partagé fournit la 10.x. Garder 8.0.6 **rétrograderait** de deux majeures (§2.3 b). L'avis d'origine est corrigé depuis 8.0.5 : l'épingle n'a plus d'objet |
| `SQLitePCLRaw.bundle_e_sqlite3` (Infrastructure) | 3.0.0 | **CONSERVER**, et revérifier | `Microsoft.EntityFrameworkCore.Sqlite` **10.0.12** tire encore le bundle **2.1.12** (`NUGET`). L'avis **GHSA-2m69-gcr7-jv3q / CVE-2025-6965** exige SQLite **≥ 3.50.2** ; la version native livrée par le bundle 2.1.12 est **`À VÉRIFIER`**. **Retirer l'épingle sans cette vérification serait une régression.** Dernière version publiée du bundle : **3.0.5** (`NUGET`) |
| `Tmds.DBus.Protocol` (App) | 0.21.3 | **réexaminer**, sans urgence | remontée pour un transitif d'`Avalonia.Desktop` → X11 → FreeDesktop, **code Linux/D-Bus jamais exécuté sur la cible Windows**. Avalonia ne bougeant pas (§3.3), le transitif ne bouge pas : l'épingle peut rester telle quelle. La retirer exige de vérifier la chaîne, ce qui n'apporte rien ici |

> **Le garde-fou reste l'audit NuGet.** [Directory.Build.props](../../Directory.Build.props) rend NU1903/NU1904
> **bloquants en CI**, et le job d'audit contrôle le JSON officiel. Toute épingle mal retirée **fera échouer la
> CI**, pas passer silencieusement. C'est le filet de sécurité de E-5.

### 3.6 Pile de test

| Paquet | Actuel | Cible | Remarque |
|---|---|---|---|
| `Microsoft.NET.Test.Sdk` | 17.12.0 | **`À VÉRIFIER`** — 17.12.0 si elle exécute sur `net10.0`, sinon **18.x** (`NUGET` : 18.10.1) | **point de risque réel** : le passage de 17.x à 18.x touche la plateforme de test elle-même. Isolé en **E-3** |
| `xunit` | 2.9.3 | **inchangé** | cible `netstandard` |
| `xunit.runner.visualstudio` | 2.8.2 | **inchangé, sauf si E-3 l'exige** | lié au SDK de test |
| `FluentAssertions` | 6.12.0 | **inchangé — ne pas monter** | **v7+ est sous licence commerciale.** L'épingle 6.x est volontaire |
| `Moq` | 4.20.72 | **inchangé** | `À VÉRIFIER` sur `net10.0` |
| `coverlet.collector` | 6.0.0 | **inchangé, sauf si E-3 l'exige** | lié au SDK de test |

---

## 4. Ordre exact d'exécution

**Principe directeur : un risque par étape.** Chaque étape est **committable et révocable seule**, et porte un
**critère de sortie** et une **condition d'arrêt**. On ne passe pas à la suivante sans le critère de sortie.

> **Pourquoi cet ordre, et pas « tout changer puis débugger ».** Si le SDK, le TFM, EF Core et la pile de test
> bougent ensemble et que la CI rougit, **rien ne dit lequel des quatre est responsable**. L'ordre ci-dessous
> garantit qu'un échec désigne sa cause.

### E-0 — Ouvrir la source NuGet *(décision, pas manipulation)*

| | |
|---|---|
| **Fichiers** | `NuGet.config` **créé à la racine** (ou configuration utilisateur, voir ci-dessous) |
| **Action** | enregistrer `nuget.org` comme source de restauration |
| **Sortie** | `dotnet restore MMV.sln` réussit **à l'identique** en `net8.0`, sans aucune autre modification |
| **Arrêt** | si la décision **G-1** n'est pas prise, **le lot ne commence pas** |

**Deux formes possibles, et elles ne se valent pas :**

| Forme | Effet | Avis |
|---|---|---|
| `NuGet.config` **dans le dépôt** | la source devient une **décision de projet**, revue, versionnée, identique pour tous les postes et pour la CI | **recommandée.** Elle rend explicite ce qui est aujourd'hui implicite : la CI restaure déjà depuis `nuget.org` par la configuration par défaut du runner, alors que le poste de développement ne le peut pas. **Cette asymétrie est aujourd'hui non écrite** |
| configuration **utilisateur** du poste | invisible du dépôt, non reproductible, divergente d'un poste à l'autre | à éviter : reconduit le problème sous une autre forme |

**Effet de bord à accepter.** Ouvrir `nuget.org` lève la contrainte d'ADR-PROD-DB-008 §2.4, qui servait de garde
implicite contre l'ajout de dépendances. Ce garde **doit être remplacé par une revue explicite** des
`PackageReference` — l'audit NuGet bloquant est déjà en place, il traite la vulnérabilité, pas l'opportunité.

### E-1 — Figer l'état de référence

| | |
|---|---|
| **Fichiers** | **aucun** |
| **Action** | sur `net8.0` inchangé : `restore`, `build -c Debug`, `test`, puis `dotnet ef migrations list` sur **les deux chaînes** et les **deux** contrôles `has-pending-model-changes` |
| **Sortie** | **nombre de tests relevé et écrit** (baseline attendue : **1569**) ; liste des migrations des deux chaînes **archivée** ; **les deux contrôles de dérive verts** |
| **Arrêt** | si la baseline n'est **pas** verte avant la montée, **elle ne sera pas imputable à la montée**. Corriger d'abord, ou renoncer |

**Cette étape n'est pas une formalité : c'est la seule chose qui rendra un échec ultérieur interprétable.** Sans
la liste de migrations d'avant, impossible de prouver qu'EF 10 n'a rien changé.

### E-2 — SDK seul : `global.json` → 10.0.103

| | |
|---|---|
| **Fichiers** | [global.json](../../global.json) |
| **Action** | `sdk.version` → `10.0.103`. **`rollForward` et `allowPrerelease` inchangés.** **Aucun `TargetFramework` touché** |
| **Sortie** | `dotnet --version` renvoie une **10.0.1xx** ; `restore` / `build` / `test` **verts, toujours en `net8.0`** ; **compte de tests identique à E-1** ; les deux contrôles de dérive verts |
| **Arrêt** | si le build casse ici, la cause est **le SDK** — analyseurs, MSBuild, avertissements nouveaux — et non le TFM. Revenir à 8.0.417 et instruire |

**Le SDK 10 compile parfaitement du `net8.0`.** Cette étape isole donc, à elle seule, tout le risque « chaîne de
compilation » — et elle est révocable par une ligne.

### E-3 — Pile de test, encore en `net8.0`

| | |
|---|---|
| **Fichiers** | les 3 `tests/**/*.csproj` |
| **Action** | **uniquement si nécessaire** : `Microsoft.NET.Test.Sdk` → 18.x, et ses satellites (`xunit.runner.visualstudio`, `coverlet.collector`) si la 18.x les exige |
| **Sortie** | compte de tests **identique à E-1** ; aucun test ignoré ni retiré |
| **Arrêt** | si la 18.x change le comportement de découverte ou d'exécution des tests, **s'arrêter** : la pile de test devient un sujet à elle seule, et elle doit être traitée avant la montée de TFM, pas pendant |

**Pourquoi avant le TFM.** Si l'hôte de test 17.12.0 n'exécute pas sur `net10.0`, on l'apprendra à l'étape
suivante **sans pouvoir distinguer** un échec d'hôte de test d'une régression réelle. On le tranche donc ici, sur
un terrain connu. **Si la 17.12.0 s'avère suffisante sur `net10.0`, cette étape est vide** — et c'est le meilleur
résultat possible.

### E-4 — `TargetFramework` : `net8.0` → `net10.0`

| | |
|---|---|
| **Fichiers** | les **8** `.csproj` de [MMV.sln](../../MMV.sln) : `MMV.Domain`, `MMV.Application`, `MMV.Infrastructure`, `MMV.Infrastructure.PostgreSQL.Migrations`, `MMV.App`, `MMV.Domain.Tests`, `MMV.Application.Tests`, `MMV.App.Tests` |
| **Action** | `<TargetFramework>net10.0</TargetFramework>`. **`LangVersion` reste 12.0.** Aucune version de paquet touchée |
| **Sortie** | `restore` / `build` / `test` verts ; compte de tests **identique** ; l'application **démarre** et ouvre l'écran de connexion ; les deux contrôles de dérive verts |
| **Arrêt** | avertissement de **rétrogradation de paquet** (NU1605) ou d'incompatibilité de framework : **ne pas le contourner**. C'est le symptôme d'une épingle à traiter — passer à E-5 avant de conclure |

**À quoi s'attendre ici.** EF Core 8 et Avalonia 11.2.8 fonctionnent sur `net10.0` (§3.2, §3.3) : le build doit
passer. Le point sensible est l'épingle `System.Text.Json` 8.0.6, qui devient une rétrogradation (§2.3 b).
**Ne pas centraliser le TFM dans [Directory.Build.props](../../Directory.Build.props)** : ce fichier est à la
racine et s'appliquerait **aussi aux spikes**, qui doivent rester maîtrisés séparément (E-8).

### E-5 — Épingles de sécurité

| | |
|---|---|
| **Fichiers** | [MMV.App.csproj](../../src/MMV.App/MMV.App.csproj), [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj) |
| **Action** | **retirer** `System.Text.Json` (les deux projets) · **conserver** `SQLitePCLRaw.bundle_e_sqlite3` · **laisser** `Tmds.DBus.Protocol` · **mettre à jour les commentaires** : ils expliquent un raisonnement daté qui ne vaut plus |
| **Sortie** | `dotnet list MMV.sln package --vulnerable --include-transitive` ne rapporte **aucune** vulnérabilité **High/Critical** ; version résolue de `System.Text.Json` **≥ 10.x** ; version native SQLite **≥ 3.50.2**, vérifiée et écrite |
| **Arrêt** | si la version native SQLite **ne peut pas être établie**, **conserver l'épingle 3.0.x** et documenter le doute. Ne jamais retirer une épingle de sécurité sur une supposition |

**Les commentaires comptent autant que les lignes.** Chaque épingle porte aujourd'hui un commentaire qui cite son
avis et sa version de correction. Laisser ces commentaires décrire un état révolu ferait de la prochaine revue une
enquête.

### E-6 — EF Core, Npgsql et l'outil `dotnet-ef` *(étape décisive)*

| | |
|---|---|
| **Fichiers** | [MMV.Infrastructure.csproj](../../src/MMV.Infrastructure/MMV.Infrastructure.csproj), [MMV.App.csproj](../../src/MMV.App/MMV.App.csproj), [MMV.Infrastructure.PostgreSQL.Migrations.csproj](../../src/MMV.Infrastructure.PostgreSQL.Migrations/MMV.Infrastructure.PostgreSQL.Migrations.csproj), les 3 `tests/**/*.csproj`, [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json) |
| **Action** | EF Core + Design + Sqlite → **10.0.x (x ≥ 4)** · `Npgsql.EntityFrameworkCore.PostgreSQL` → **10.0.x** · `dotnet-ef` → **même majeure**. Versions **revérifiées le jour de l'exécution** |
| **Sortie** | **les deux `has-pending-model-changes` verts** · `migrations list` renvoie **exactement** les mêmes migrations qu'en E-1 — **14** en SQLite, **1** en PostgreSQL · compte de tests identique · la garde anti-faux-vert de la CI reste opérante (`InitialPostgreSqlBaseline` présente, `InitialCreate` absente) |
| **Arrêt** | **G-4 ci-dessous. C'est le point d'arrêt le plus important du plan.** |

> ### G-4 — Si EF Core 10 signale une dérive de modèle, **le lot s'arrête**
>
> `has-pending-model-changes` rouge signifie qu'EF 10 **traduit le modèle autrement** qu'EF 8. La seule
> « correction » disponible serait **une migration nouvelle sur les deux chaînes** — ce que le §1.2 interdit, et
> ce qui transformerait une montée de runtime en **changement de schéma de production**.
>
> **Conduite à tenir :** ne pas générer la migration. Ne pas « rafraîchir » l'instantané. **Consigner la
> différence exacte**, revenir à EF 8, et porter le sujet devant l'architecte : un changement de traduction du
> modèle relève d'[ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md) et
> d'[ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md), pas d'un lot de montée de version.
>
> **Ce risque est réel et non mesuré.** Deux majeures d'EF séparent 8.0.27 de 10.0.x ; l'instantané PostgreSQL est
> en `ProductVersion 8.0.27`. Que la traduction soit strictement identique est **`À VÉRIFIER`** — c'est même la
> raison d'être de l'étape E-1.

### E-7 — CI et vérification de bout en bout

| | |
|---|---|
| **Fichiers** | [.github/workflows/ci.yml](../../.github/workflows/ci.yml) — **probablement aucune modification** |
| **Action** | vérifier que `setup-dotnet` installe bien le SDK 10 via `global-json-file`, puis lire **intégralement** le journal du job |
| **Sortie** | CI **verte sur le SHA exact**, tous les steps : restore, build, test, audit JSON, `tool restore`, **les deux** contrôles de dérive |
| **Arrêt** | si `setup-dotnet` ne trouve pas `10.0.103`, épingler une version de SDK **publiée et disponible sur le runner** — et la revérifier localement |

**Point de vigilance.** La CI ne construit qu'en **Debug** et ne publie rien : elle **ne prouve pas** qu'une
publication autonome `win-x64` fonctionne sur .NET 10. Cette preuve appartient à **P8** (DI-1). Ne pas conclure
de la CI verte que la Release V1 est servie.

### E-8 — Spikes, hors solution

| | |
|---|---|
| **Fichiers** | [MMV.P4.ProviderComparison.csproj](../../spikes/P4.ProviderComparison/MMV.P4.ProviderComparison.csproj), [MMV.P4.Worker.csproj](../../spikes/P4.ProviderComparison/Worker/MMV.P4.Worker.csproj) |
| **Action** | `MMV.P4.ProviderComparison` → `net10.0`, avec ses paquets EF alignés (`Microsoft.EntityFrameworkCore.SqlServer` et `.Relational` en 10.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` en 10.0.x). `MMV.P4.Worker` **peut rester en `net8.0`** : il n'a aucune `ProjectReference` vers `src/` |
| **Sortie** | `dotnet build` **explicite** sur le projet de spike réussit |
| **Arrêt** | si les paquets SQL Server 10.x posent difficulté : **le spike n'est pas sur le chemin de la V1**. Consigner l'état et laisser le projet non compilable **en l'écrivant dans son en-tête** — jamais en silence |

**Pourquoi cette étape existe.** Sans elle, la rupture décrite en §2.3 (a) reste invisible jusqu'au jour où l'on
voudra rejouer le spike P4-1 — c'est-à-dire, précisément, le jour où l'on voudra re-prouver les 14 primitives
(obligation **O12**). **Le moment où l'on découvre la casse est le plus mauvais possible.**

### E-9 — Documentation et clôture

| | |
|---|---|
| **Fichiers** | [roadmap P4](P4-multi-poste-roadmap.md) · [ADR-PROD-DB-009](ADR-PROD-DB-009.md) **Q-6** · [ARCHITECTURE.md](../../ARCHITECTURE.md) si une version de runtime y est écrite · rapport de lot sous `docs/implementation/` |
| **Action** | passer `P4-NET10` à `COMPLETED` avec **commit et numéro de CI** · **répondre à Q-6** au vu du verrou natif d'EF 10 · consigner les versions **réellement retenues**, avec les mesures |
| **Sortie** | la roadmap dit ce que le dépôt fait ; **Q-6 est tranchée sur pièces** |

**C'est ici que le lot rend son bénéfice architectural** : Q-6 cesse d'être une question ouverte d'ADR-PROD-DB-009
et devient une décision appuyée sur une vérification.

---

## 5. Risques techniques

| # | Risque | Probabilité | Impact | Détection | Atténuation |
|---|---|---|---|---|---|
| **R-1** | **EF Core 10 traduit le modèle autrement** : dérive signalée, migration exigée | **moyenne** — deux majeures d'écart, `À VÉRIFIER` | **ÉLEVÉ** — sort du périmètre du lot et touche au schéma de production | les **deux** contrôles `has-pending-model-changes`, déjà en CI | **G-4** : arrêt, retour à EF 8, escalade. Ne jamais générer la migration « pour faire passer » |
| **R-2** | **L'épingle `System.Text.Json` 8.0.6 rétrograde** l'assembly sous celle du runtime | **élevée si oubliée** | **élevé** — régression de sécurité déguisée en épingle de sécurité | NU1605 au build ; audit NuGet en CI | **E-5**, avec vérification de la version résolue |
| **R-3** | **Épingle SQLite retirée à tort** : retour sous le correctif de CVE-2025-6965 | moyenne | **élevé** — corruption mémoire dans une bibliothèque qui porte les données patients | audit NuGet bloquant (NU1903/NU1904) | **conserver** l'épingle par défaut ; ne la retirer que sur version native **établie** |
| **R-4** | **L'hôte de test n'exécute pas sur `net10.0`** | moyenne — `À VÉRIFIER` | moyen — bloque la preuve, pas le produit | E-3, sur terrain `net8.0` connu | isoler avant la montée de TFM ; 18.x en repli |
| **R-5** | **Les spikes cassent en silence** | **élevée si E-8 est omise** | moyen — se révèle au pire moment (re-preuve O12) | **aucune détection automatique** : hors solution, hors CI | **E-8** ; à défaut, écrire la casse dans l'en-tête du projet |
| **R-6** | **Ouvrir `nuget.org` lève un garde implicite** contre l'ajout de dépendances | certaine — c'est l'effet voulu | moyen | revue de `PackageReference` | remplacer le garde par une **revue explicite** ; l'audit bloquant reste en place |
| **R-7** | **`setup-dotnet` ne trouve pas le SDK épinglé** | faible | faible — CI rouge immédiate et lisible | journal du job | épingler une version publiée ; vérifier localement d'abord |
| **R-8** | **Avertissements nouveaux** du SDK 10 ou des analyseurs | élevée | faible — la CI ne construit pas avec `-warnaserror` | journal de build | **ne pas corriger dans ce lot** (§1.2). Consigner, traiter ailleurs |
| **R-9** | **Npgsql 10 change un comportement d'exécution** (types, fuseaux, délais) | moyenne | **`UNKNOWN` — non mesurable aujourd'hui** | **aucune** : le démarrage PostgreSQL est **volontairement bloqué** par le garde-fou P4-3, et **P4-5F n'existe pas** | **à porter explicitement** comme risque résiduel du lot. La preuve serveur appartient à P4-5F. **C'est un argument fort pour exécuter la montée avant P4-5F** : le corpus N1 … N8 s'écrira alors directement sur Npgsql 10 |
| **R-10** | Le lot **s'élargit** en cours de route — Avalonia 12, C# 14, corrections d'avertissements | élevée, humaine | élevé — diff non revu, diagnostic impossible | revue de diff | §1.2 est une **limite de périmètre**, pas une préférence |

> **R-9 mérite d'être lu deux fois.** Le dépôt **ne peut pas prouver aujourd'hui** que Npgsql 10 se comporte comme
> Npgsql 8 à l'exécution, puisque **aucun code ne s'exécute contre PostgreSQL** : le garde-fou de démarrage bloque
> volontairement cette voie ([App.axaml.cs:200-214](../../src/MMV.App/App.axaml.cs#L200-L214)) et les tests
> d'intégration serveur sont le contenu de P4-5F, non commencé. **La montée de version est donc sûre pour ce que
> le dépôt sait faire, et non prouvée pour ce qu'il ne sait pas encore faire.** Le dire est plus utile que de
> l'espérer.

---

## 6. Stratégie de retour arrière

**Le lot ne touche que des fichiers de construction et de dépendances.** Aucun `.cs`, aucune migration, aucune
donnée. Le retour arrière est donc **complet et sans reste** — c'est la propriété la plus précieuse du plan.

| Niveau | Moyen | Portée | Coût |
|---|---|---|---|
| **N-1 — une étape** | révoquer le commit de l'étape | l'étape seule | minutes. C'est la raison du découpage E-0 … E-9 |
| **N-2 — tout le lot** | révoquer la plage de commits, ou abandonner la branche de lot | dépôt entier | minutes |
| **N-3 — poste de développement** | le SDK **8.0.425 reste installé** (`LOCAL`) ; revenir à `global.json` 8.0.417 suffit | poste | immédiat |
| **N-4 — CI** | la CI suit `global.json` : la révocation de ce seul fichier ramène le runner en .NET 8 | CI | un *push* |

**Conditions pour que ce retour arrière reste vrai — à tenir pendant tout le lot :**

1. **travailler sur une branche de lot dédiée**, jamais directement sur `p4-multi-poste` ;
2. **un commit par étape**, avec son critère de sortie vérifié dans le message ;
3. **ne pas désinstaller le SDK .NET 8** du poste ni des runners avant la clôture ;
4. **ne générer aucune migration**, sous aucun prétexte (G-4). Une migration commitée **n'est pas révocable sans
   trace** : elle entre dans l'historique du schéma, et [ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md)
   interdit de retoucher une migration existante. **C'est le seul geste de ce lot qui ne se défait pas.**

> **Ce qui n'est pas un retour arrière.** Si le lot va jusqu'à une **release livrée** à un client, le retour
> arrière cesse d'être un `git revert` : il devient le couple (binaires, base) d'ADR-APP-DISTRIBUTION-001 **DI-7**.
> Ce lot s'arrête **avant** toute publication : il n'y a pas de release, donc pas de couple à ramener.

---

## 7. Critères GO / NO-GO

### 7.1 Avant de commencer — conditions de GO

| # | Condition | État au 22/09/2026 | Qui |
|---|---|---|---|
| **G-1** | **La source NuGet est ouverte**, par un `NuGet.config` versionné, et cette ouverture est **assumée comme une décision** (§E-0, R-6) | **NON SATISFAITE** — seule source : dossier hors ligne. **C'est le seul obstacle matériel** | **architecte** |
| **G-2** | **Baseline verte et écrite** : build, tests (**1569** attendus), les deux contrôles de dérive, les listes de migrations des deux chaînes archivées | à produire par **E-1** | exécutant |
| **G-3** | **SDK 10 disponible** localement **et** sur le runner, en version publiée | **SATISFAITE localement** — 10.0.103 (`LOCAL`). Runner : `À VÉRIFIER` | exécutant |
| **G-5** | **Versions cibles revérifiées le jour même** sur `api.nuget.org`, contrainte `Npgsql EF → EF Core [10.0.4, 11.0.0)` incluse | relevé du 22/09/2026 à rafraîchir | exécutant |
| **G-6** | **Branche de lot dédiée** créée ; SDK .NET 8 conservé | à faire | exécutant |
| **G-7** | **Séquence arbitrée** : avant P4-5F et P4-6B *(recommandé)*, ou après | **NON ARBITRÉE** | **architecte** |

> **G-4 n'est pas une condition de départ : c'est la condition d'arrêt de E-6**, énoncée au §4. Elle est numérotée
> dans la même série parce qu'elle a le même poids qu'un NO-GO.

### 7.2 NO-GO — ne pas commencer si

- **G-1 n'est pas tranchée.** Sans source NuGet, aucun paquet 10.x ne se restaure : le lot s'arrêterait à E-2.
- **La baseline n'est pas verte** avant la montée (G-2) : un échec ultérieur ne serait pas imputable.
- **Une release est en préparation**, ou une tournée de mise à jour de postes est en cours : ce lot change le
  runtime **embarqué**, ce qui interdit de le glisser entre deux postes d'une même tournée
  (ADR-APP-DISTRIBUTION-001 **OI-13**).
- **P4-6B est déjà commencée** : la montée y ajouterait un second changement de fond simultané, et Q-6 aurait été
  tranchée par défaut au lieu d'être tranchée sur pièces.

### 7.3 Arrêt en cours de route

| Déclencheur | Conduite |
|---|---|
| **Dérive de modèle EF** (G-4) | **arrêt immédiat**, retour à EF 8, escalade à l'architecte. Ne générer aucune migration |
| Le compte de tests **change**, dans un sens ou dans l'autre | arrêt : un test disparu est une régression de preuve, un test apparu n'appartient pas à ce lot |
| Un **contrôle de dérive** passe au rouge | arrêt : traiter avant d'avancer |
| Une **vulnérabilité High/Critical** apparaît à l'audit | arrêt : c'est le motif même des épingles de sécurité |
| Le périmètre **s'élargit** (Avalonia 12, C# 14, correction d'avertissements) | arrêt et retour au §1.2 |

### 7.4 Critères de sortie — le lot est terminé quand

1. les **8 projets** de la solution sont en `net10.0`, `LangVersion 12.0` conservée ;
2. EF Core, Npgsql et `dotnet-ef` sont en **10.0.x**, versions exactes **écrites** dans le rapport de lot ;
3. **aucun fichier `.cs` modifié, aucune migration ajoutée ou retouchée, aucun changement de modèle EF** ;
4. **1569 tests verts**, sans test ajouté, retiré ni ignoré ;
5. **les deux contrôles de dérive verts**, avec la garde anti-faux-vert opérante ;
6. **aucune vulnérabilité High/Critical** à l'audit, épingles de sécurité justifiées **ligne par ligne** ;
7. **CI verte sur le SHA exact**, tous les steps ;
8. les spikes sont **traités** (E-8) : migrés, ou explicitement consignés comme non compilables ;
9. **Q-6 d'ADR-PROD-DB-009 est tranchée** au vu du verrou de migration natif d'EF 10 ;
10. la roadmap porte `P4-NET10 = COMPLETED`, avec **commit et numéro de CI**.

---

## 8. Ce que le lot laisse à d'autres

| Sujet | Lot |
|---|---|
| **Publication autonome `win-x64`** sur .NET 10, taille du paquet, ReadyToRun | **P8** (DI-1, ND-4, SD-9) |
| **Empaquetage Velopack, signature, flux de mise à jour** | **P8** (DI-2, DI-6, ND-3) |
| **Preuve d'exécution de Npgsql 10 contre un vrai serveur** (R-9) | **P4-5F** — corpus N1 … N8 |
| **Montée d'Avalonia vers 12.x** | lot d'interface graphique à instruire pour lui-même (§3.3) |
| **Montée de `LangVersion` vers C# 14** | à décider quand un besoin le justifiera (§3.4) |
| **Version applicative unique en SemVer**, remplaçant le `1.0.0` écrit en dur | **P4-6B** (ADR-PROD-DB-009 **DP-7**) — *ne pas la glisser dans ce lot* |
| **Correction des avertissements préexistants** (dont un `CS1998` connu) | hors périmètre depuis l'Étape 0 |

---

## 9. Références

- [ADR-APP-DISTRIBUTION-001](adr-app-distribution-001-installation-and-updates.md) — **DI-1** publication
  autonome · **DI-8** .NET 10 avant la Release V1 · **OI-10** · **OI-13** une release porteuse de schéma par
  tournée · §2.1 fin de support de .NET 8
- [ADR-PROD-DB-009](ADR-PROD-DB-009.md) — **Q-6** (verrou natif d'EF ≥ 9) · **DP-2** (famille « natif
  PostgreSQL ») · **DP-7** (version SemVer) · §4.2.1 (LK-5 `UNKNOWN`)
- [ADR-PROD-DB-005](adr-prod-db-005-migration-architecture.md) — migrations générées par l'outillage EF, jamais
  retouchées
- [ADR-PROD-DB-007](adr-prod-db-007-schema-drift-prevention.md) — prévention de la dérive, contrôles en CI
- [ADR-PROD-DB-008 §2.4](adr-prod-db-008-postgresql-integration-testing.md) — contrainte NuGet hors ligne, levée
  par E-0
- [Roadmap P4 — §P4-NET10, §P4-6, §6](P4-multi-poste-roadmap.md) ·
  [Roadmap P5 — §15](P5-product-completion-roadmap.md) ·
  [Rapport d'acceptation P4-6A](../implementation/P4-6A-adr-acceptance-report.md)
- Dépôt : [global.json](../../global.json) · [Directory.Build.props](../../Directory.Build.props) ·
  [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json) ·
  [.github/workflows/ci.yml](../../.github/workflows/ci.yml) · [CONTRIBUTING.md](../../CONTRIBUTING.md)

---

**Plan prêt à exécuter, sous réserve de G-1 et G-7 — deux décisions de l'architecte.** Le SDK .NET 10 est déjà
en place ; Avalonia et le modèle EF ne bougent pas ; le retour arrière est complet à toutes les étapes. **Le seul
risque non maîtrisé est R-1 / G-4**, une dérive de traduction du modèle par EF Core 10 : il est détecté
automatiquement par les deux contrôles déjà en CI, et sa conduite à tenir est écrite.
