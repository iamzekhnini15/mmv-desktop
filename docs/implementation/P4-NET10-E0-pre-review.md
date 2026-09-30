# P4-NET10 — E-0 : ouverture de la source NuGet — PRÉ-REVUE

> **Statut : E-0 FAIT LOCALEMENT. PRÉ-REVUE, AUCUN COMMIT, AUCUN PUSH.**
> Périmètre : **E-0 uniquement**, conformément à la décision **G-1** du
> [plan d'exécution .NET 10](../architecture/net10-migration-execution-plan.md) (§E-0, lignes 233-277).
> Un fichier **nouveau** est créé à la racine : [NuGet.config](../../NuGet.config). **Aucun `.csproj`, aucun
> fichier de CI, aucun paquet ajouté, aucune ligne de code touchée.**
>
> Date : 22 septembre 2026 · Branche : `p4-net10` · Destinataire : Lead Software Architect.
> **Le dépôt réel prime sur ce document.**

**Légende des preuves**

| Étiquette | Sens |
|---|---|
| `EXÉCUTÉ` | commande exécutée sur le dépôt le 22/09/2026, sortie observée |
| `ISOLÉ` | exécuté avec un dossier de paquets **jetable** hors du dépôt (`--packages`), supprimé ensuite. Le cache global `~/.nuget/packages` **n'a pas servi** |
| `CODE` | constaté par lecture du dépôt |
| `NON ÉTABLI` | **non prouvé.** Ne pas supposer |

---

## 1. Résumé exécutif

- **E-0 est fait et il fonctionne.** `restore`, `build -c Debug` et `test` sont **verts** sur les 8 projets, en
  `net8.0`, avec le SDK **10.0.103**. **1953 tests / 1953 verts, 0 ignoré. 0 avertissement, 0 erreur** au build.
- **Le `<clear />` produit exactement l'effet attendu** : `dotnet nuget list source` ne montre plus qu'**une**
  source, `nuget.org`. Le dossier `Microsoft Visual Studio Offline Packages` a **disparu du jeu effectif**.
- **Preuve décisive, et elle explique le blocage initial** (`ISOLÉ`) : le SDK 10 **ne contient pas** les packs de
  référence `net8`. Il les **télécharge depuis `nuget.org`** — `microsoft.netcore.app.ref`,
  `microsoft.windowsdesktop.app.ref`, `microsoft.aspnetcore.app.ref` et `microsoft.netcore.app.host.win-x64`, tous
  en **8.0.24**. Sans E-0, compiler du `net8.0` avec le SDK 10 sur ce poste était **impossible**, et aucune
  modification de code n'y aurait rien changé. Le diagnostic qui a motivé l'arrêt du build est **confirmé**.
- **Restauration reproductible démontrée, pas supposée** (`ISOLÉ`) : restauration **à froid** dans un dossier de
  paquets vide → **125 paquets**, code de sortie **0**. Le graphe complet est obtenable depuis `nuget.org` seul,
  sans cache chaud et sans dossier hors ligne. C'est ce que le runner de CI fait déjà ; le poste le fait
  désormais **à l'identique**.
- **Trois écarts au plan sont à trancher par l'architecte** (§6) : **G-0 n'est pas levé durablement**,
  **E-2 a été appliqué avant E-1**, et la **baseline de tests du plan (1569) est périmée** — elle est de **1953**.
- **Aucun commit n'a été créé.**

---

## 2. Fichiers modifiés

| Fichier | État | Diff | Imputable à E-0 |
|---|---|---|---|
| [NuGet.config](../../NuGet.config) | **créé**, versionné, racine du dépôt | **+11 / −0** | **oui** |
| [global.json](../../global.json) | modifié, **non commité**, `8.0.417` → `10.0.103` | +1 / −1 | **non — préexistant** (§6.2) |

`git status` après E-0 (`EXÉCUTÉ`) :

```
 M global.json
?? NuGet.config
```

**Rien d'autre n'apparaît.** Les 8 `.csproj` sont **intacts** et tous encore en `net8.0` (`CODE`).
[.github/workflows/ci.yml](../../.github/workflows/ci.yml), [Directory.Build.props](../../Directory.Build.props)
et [.config/dotnet-tools.json](../../.config/dotnet-tools.json) sont **intacts**. **Aucun `PackageReference`
ajouté, retiré ou modifié.**

### 2.1 Contenu du fichier créé

Le contenu est celui du plan (§E-0, lignes 254-266), **repris sans modification** :

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <!-- P4-NET10 / E-0 (décision G-1) : le dépôt impose son jeu de sources.
         <clear /> neutralise les sources héritées des configurations machine et utilisateur
         (dont le dossier hors ligne de Visual Studio), afin que le poste de développement et le
         runner de CI restaurent depuis EXACTEMENT la même source. -->
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

---

## 3. Justification

**Contraintes respectées.** `<clear />` présent · `nuget.org` déclaré explicitement · aucune dépendance aux
sources machine · **la configuration utilisateur n'a pas été employée** (`%APPDATA%\NuGet\NuGet.Config` n'a été ni
lu ni écrit) · aucun paquet système installé · CI non modifiée · `.csproj` non modifiés.

**Pourquoi un fichier versionné et non la configuration utilisateur.** C'est la décision **G-1**. La configuration
utilisateur est invisible du dépôt, non reproductible et divergente d'un poste à l'autre. Le fichier versionné
rend explicite ce qui était implicite : la CI restaurait déjà depuis `nuget.org` par la configuration par défaut du
runner, alors que le poste ne le pouvait pas. **Cette asymétrie n'était écrite nulle part.** E-0 la supprime.

**Pourquoi `<clear />` n'est pas un ornement — et la mesure le prouve.** Avant E-0, la seule source du poste était
le dossier hors ligne de Visual Studio, que le runner GitHub n'a pas. Après E-0 (`EXÉCUTÉ`) :

```
Sources inscrites :
  1.  nuget.org [Activé]
      https://api.nuget.org/v3/index.json
```

**Une seule source, des deux côtés.** Sans `<clear />`, le poste aurait conservé le dossier hors ligne en plus, et
deux restaurations auraient pu produire deux graphes — le « ça marche chez moi » structurel que le plan désigne.

**Effet de bord assumé, et il change une responsabilité.** Ouvrir `nuget.org` lève la contrainte
d'[ADR-PROD-DB-008 §2.4](../architecture/adr-prod-db-008-postgresql-integration-testing.md), qui servait de garde
**implicite** contre l'ajout de dépendances : un paquet nouveau était simplement non restaurable. **Ce garde
n'existe plus.** Il doit être remplacé par la revue explicite des `PackageReference` (**R-6**, point de validation
**V-1**), désormais **à la charge du relecteur**. L'audit NuGet traite la vulnérabilité, **pas l'opportunité**.

**Bénéfice non prévu au plan : l'audit NuGet devient réellement opérant en local.**
[Directory.Build.props](../../Directory.Build.props) impose `NuGetAudit` / `NuGetAuditMode=all`. Avec un dossier
hors ligne pour seule source, la base d'avis n'était pas joignable depuis le poste. `nuget.org` la sert. Le
contrôle ci-dessous est donc, pour la première fois, **significatif sur le poste** (`EXÉCUTÉ`) :

```
Le projet spécifié 'MMV.Domain' n'a aucun package vulnérable compte tenu des sources actuelles.
... (8 projets sur 8)
```

**0 avertissement `NU19xx`** à la restauration et au build.

---

## 4. Résultats restore / build / test

Toutes les commandes ci-dessous ont été exécutées **après correction de l'ordre du `PATH`** — voir **§6.1**, qui
est une réserve, pas un détail.

| # | Commande | Résultat | Preuve |
|---|---|---|---|
| 1 | `dotnet --version` | **`10.0.103`** | `EXÉCUTÉ` |
| 2 | `dotnet nuget list source` | **`nuget.org` seule, activée** ; dossier hors ligne absent | `EXÉCUTÉ` |
| 3 | `dotnet restore MMV.sln` | **8 / 8 projets restaurés**, 0 avertissement | `EXÉCUTÉ` |
| 4 | `dotnet restore MMV.sln --packages <jetable>` | **125 paquets**, sortie **0** | `ISOLÉ` |
| 5 | `dotnet build MMV.sln -c Debug` | **`La génération a réussi.` — 0 avertissement, 0 erreur**, 37,8 s | `EXÉCUTÉ` |
| 6 | `dotnet test MMV.sln -c Debug --no-build` | **1953 / 1953 verts, 0 ignoré** | `EXÉCUTÉ` |
| 7 | `dotnet tool restore` | **`dotnet-ef` 8.0.27 restauré** | `EXÉCUTÉ` |
| 8 | `dotnet list MMV.sln package --vulnerable --include-transitive` | **aucune vulnérabilité**, 8 / 8 projets | `EXÉCUTÉ` |

### 4.1 Détail des tests (`EXÉCUTÉ`)

| Assembly | Réussite | Échec | Ignoré | Durée |
|---|---:|---:|---:|---|
| `MMV.Domain.Tests` (net8.0) | **1065** | 0 | 0 | 59 s |
| `MMV.Application.Tests` (net8.0) | **628** | 0 | 0 | 42 s |
| `MMV.App.Tests` (net8.0) | **260** | 0 | 0 | 3 s |
| **Total** | **1953** | **0** | **0** | — |

Ce total **concorde** avec celui relevé dans [la pré-revue P4-5E-C7](P4-5E-C7-pre-review.md) (« 1953 / 1953 verts,
0 ignoré »). **La baseline `1569` inscrite au plan (§E-1) est donc périmée** — voir §6.3.

### 4.2 Les packs `net8` sont-ils restaurables avec le SDK 10 ? — **oui, et depuis `nuget.org`**

Objectif n° 2 de la mission. La restauration ordinaire (#3) ne prouve rien à elle seule : le cache global contenait
déjà du 8.x. La restauration **isolée** (#4) tranche. Contenu du dossier jetable, après coup (`ISOLÉ`) :

| Paquet téléchargé | Version | Rôle |
|---|---|---|
| `microsoft.netcore.app.ref` | **8.0.24** | pack de **référence** `net8.0` — assemblages de compilation |
| `microsoft.windowsdesktop.app.ref` | **8.0.24** | pack de référence Desktop |
| `microsoft.aspnetcore.app.ref` | **8.0.24** | pack de référence ASP.NET Core |
| `microsoft.netcore.app.host.win-x64` | **8.0.24** | hôte exécutable `win-x64` |
| `npgsql` | 8.0.6 | — |
| `npgsql.entityframeworkcore.postgresql` | 8.0.11 | — |

**Lecture.** Le SDK 10.0.103 embarque les packs de référence `net10`, **pas ceux de `net8`**. Pour compiler du
`net8.0`, il les acquiert **en tant que paquets NuGet**. Un poste dont la seule source est un dossier hors ligne
qui ne les contient pas **ne peut pas compiler** — quel que soit l'état du code. **C'est la cause racine du blocage
signalé, et E-0 la traite à la racine.**

Le runtime `Microsoft.NETCore.App` installé sur le poste est en **8.0.31**, les packs de référence en **8.0.24** :
c'est le fonctionnement **normal** du SDK (référence figée, exécution en roll-forward). **Ce n'est pas un écart.**

### 4.3 Le graphe restauré a-t-il bougé ? — **invérifiable, et c'est une limite honnête**

Le plan demande de vérifier que « E-0 n'a rien changé d'autre », par un `restore` **identique** à l'avant. **Ce
contrôle ne peut pas être exécuté ici** : avant E-0, la restauration **échouait**. Il n'existe **aucun graphe
antérieur** auquel comparer. `NON ÉTABLI`, et non « vert ».

Ce qui est établi à la place, et qui borne le risque :

- **Aucune version flottante** dans le dépôt (`CODE`) : `grep -rn 'Version="[^"]*\*'` sur les `.csproj` et
  `Directory.Build.props` ne renvoie **rien**. Toutes les versions directes sont épinglées.
- **Les 41 déclarations `PackageReference` des 8 projets sont toutes résolues à la version demandée** (`EXÉCUTÉ`,
  colonnes « demandée » et « résolue » de `dotnet list package` **identiques ligne à ligne**) : Avalonia 11.2.8,
  EF Core 8.0.27,
  Npgsql.EFCore.PostgreSQL 8.0.11, `System.Text.Json` 8.0.6, `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0,
  `Microsoft.NET.Test.Sdk` 17.12.0, xunit 2.9.3.

La résolution NuGet étant déterministe à sources et versions égales, le graphe est reproductible **en avant**. Ce
qui reste non prouvé est la comparaison **avec l'avant**, faute d'avant exploitable.

---

## 5. Risques

| # | Risque | Portée | Conduite proposée |
|---|---|---|---|
| **R-A** | **`G-0` n'est pas levé durablement.** `dotnet` sur le `PATH` désigne toujours `C:\Program Files (x86)\dotnet`, **sans SDK**. Toutes les preuves de ce rapport ont exigé une correction du `PATH` **limitée à la session** | **bloquant pour E-1** | §6.1 — à lever **avant** E-1, de façon **persistante** |
| **R-B** | **Dépendance dure à `nuget.org`.** `<clear />` retire le repli hors ligne. Une indisponibilité de `nuget.org` rend la restauration impossible pour tout paquet non caché | poste **et** CI | **accepté** : c'est le prix de la symétrie voulue par G-1. `nuget.org` est joignable (#3, #4) |
| **R-C** | **La CI devient rouge sur incident `nuget.org`.** `Directory.Build.props` promeut `NU1900` / `NU1905` en **erreurs** quand `CI=true`. Avec une source unique, une panne de la base d'avis **arrête le pipeline** | CI | **accepté et voulu** : « échec de récupération » ne doit jamais être lu comme « aucune vulnérabilité ». Comportement **inchangé** par E-0 |
| **R-D** | **Le garde implicite contre l'ajout de dépendances a disparu** (ADR-PROD-DB-008 §2.4) | gouvernance | **R-6 / V-1** : revue explicite des `PackageReference` à chaque revue. Contrepartie assumée de G-1 |
| **R-E** | **Pas de `packages.lock.json`.** La reproductibilité est garantie au niveau **source** et **versions directes**, pas par un verrou de graphe transitif | faible | **hors périmètre E-0.** À proposer comme lot distinct si l'architecte le souhaite ; **ne pas l'introduire pendant une montée de runtime** |
| **R-F** | **Casse du nom de fichier.** `NuGet.config` est résolu sans égard à la casse sous Windows ; un runner **Linux** serait plus exigeant | nul aujourd'hui | la CI tourne sur `windows-latest` (`CODE`). À revoir **si et seulement si** un runner Linux est introduit |
| **R-G** | **E-2 appliqué avant E-1** : la baseline de référence n'a jamais été figée sous SDK 8 | **méthodologique** | §6.2 — arbitrage architecte |

---

## 6. Écarts au plan — arbitrages demandés

### 6.1 G-0 : levé pour la session seulement — **réserve la plus importante de ce rapport**

**Constat** (`EXÉCUTÉ`). Dans le shell de travail, `dotnet --version` **échoue** :
`No .NET SDKs were found`. Le `PATH` place `C:\Program Files (x86)\dotnet\` **avant** `C:\Program Files\dotnet\`,
et l'installation x86 ne porte **aucun SDK**. L'installation x64 porte bien **8.0.425 et 10.0.103**.

**Ce que j'ai fait.** J'ai appliqué l'un des deux remèdes prévus par le plan — **corriger l'ordre du `PATH`** — mais
**seulement dans la session d'exécution** (`$env:PATH = "C:\Program Files\dotnet;" + $env:PATH`). Je n'ai **pas**
invoqué le chemin complet dans les commandes du lot, ce que le plan interdit explicitement (§2.1).

**Ce que je n'ai pas fait, et pourquoi.** Je n'ai **pas** modifié le `PATH` persistant de l'utilisateur et n'ai
**pas** désinstallé le `dotnet` x86 : ce sont des modifications de l'environnement **de la machine**, hors du
périmètre d'un lot qui doit « ne modifier aucun fichier code », et elles engagent le poste au-delà de cette session.

**Conséquence à assumer.** Une session correctrice est, dans son esprit, le contournement que le plan dénonce :
elle masque le problème. **Les résultats de ce rapport sont valides ; ils ne sont pas reproductibles dans un shell
neuf.** `G-0` reste donc **NON LEVÉ** au sens du §9.1.

**Décision demandée.** Autoriser l'une des deux mesures persistantes : (a) placer `C:\Program Files\dotnet` avant
`C:\Program Files (x86)\dotnet` dans le `PATH` **utilisateur**, ou (b) désinstaller le `dotnet` x86 s'il n'a pas
d'usage. **(a) est réversible et suffit.**

### 6.2 `global.json` : E-2 a été appliqué avant E-1

**Constat.** `global.json` est **déjà** à `10.0.103` dans le plan de travail, **non commité**. Or c'est
**exactement** l'action de **E-2** (§E-2 du plan). E-2 a donc été appliqué **avant** E-0 et **avant** E-1.

**Je ne l'ai pas touché** : la consigne était de ne pas modifier `global.json` sans nécessité démontrée, et la
mission demandait explicitement de vérifier les packs `net8` **avec le SDK 10**. Le revenir en arrière aurait été
une modification non demandée.

**Ce que cela casse.** E-1 doit figer l'état de référence **sur `net8.0` inchangé**, et le critère de sortie de E-2
est « **compte de tests identique à E-1** ». Comme E-1 n'a jamais été exécuté sous SDK 8, **ce critère est
aujourd'hui invérifiable** : on ne peut pas distinguer ce que le SDK 10 a changé de ce qui était déjà là.

**Ce que cela ne casse pas.** E-0 est **indépendant** de cette question : il ouvre une source, il ne choisit pas de
SDK. Les preuves du §4 restent valides telles qu'énoncées — « verts sous SDK 10, en `net8.0` ».

### 6.3 La baseline de tests du plan est périmée

Le plan annonce « baseline attendue : **1569** » (§E-1). Le dépôt en est à **1953** (§4.1), chiffre **concordant**
avec la pré-revue P4-5E-C7 du même jour. L'écart s'explique par les lots P4-5D / P4-5E livrés depuis la rédaction
de cette ligne. **Corriger le plan**, faute de quoi E-1 se conclura sur une fausse alerte.

---

## 7. Décision pour E-1

**Avis d'exécutant : E-0 est complet, conforme à G-1, et prêt à être commité. E-1 ne doit pas démarrer en l'état.**

### 7.1 Préalables bloquants

| # | Préalable | Pourquoi |
|---|---|---|
| **P1** | **Lever `G-0` durablement** (§6.1) | sans cela, aucune preuve de E-1 n'est reproductible dans un shell neuf, et un échec ultérieur sera ininterprétable — exactement ce que E-1 doit empêcher |
| **P2** | **Trancher `global.json`** (§6.2) | E-1 est la baseline. Sa valeur dépend entièrement du SDK sous lequel elle est prise |
| **P3** | **Corriger `1569` → `1953`** dans le plan (§6.3) | sinon E-1 échoue sur un chiffre faux |

### 7.2 Recommandation sur P2

**Revenir `global.json` à `8.0.417`, exécuter E-1, puis réappliquer E-2.** Motifs :

- c'est **une ligne**, et le SDK **8.0.425 est présent** sur le poste : le coût est de deux minutes ;
- cela **rend son sens à E-2**, dont le critère de sortie est une comparaison avec E-1. Sans baseline SDK 8, E-2
  est une étape **sans critère**, et le plan perd précisément l'isolation du risque « chaîne de compilation » pour
  laquelle E-2 existe ;
- **E-1 exige en plus** `dotnet ef migrations list` sur les **deux** chaînes et les **deux** contrôles de dérive.
  Les prendre sous SDK 10 mêlerait, dans un même résultat, l'état de référence et l'effet du SDK.

**Variante acceptable si l'architecte préfère ne pas revenir en arrière** : acter formellement que la baseline du
lot est **« SDK 10.0.103 + `net8.0` »**, requalifier E-2 en **déjà fait**, et **amender le plan** en conséquence.
Ce qu'il faut éviter est l'état actuel : E-2 appliqué, mais toujours présenté comme à faire, avec un critère de
sortie qui ne peut pas être satisfait.

### 7.3 Ce que E-0 a déjà débloqué pour E-1

- `dotnet tool restore` fonctionne : **`dotnet-ef` 8.0.27 est disponible** (#7), ce dont E-1 a besoin pour lister
  les migrations des deux chaînes. Avant E-0, cet outil n'était pas restaurable.
- L'audit de vulnérabilités est opérant en local (#8) : **aucune vulnérabilité** sur 8 projets, à consigner comme
  point de départ pour **E-5**.

### 7.4 Commit proposé — **non exécuté, en attente de validation**

Un seul fichier, un seul motif :

```
chore(P4-NET10): add versioned NuGet.config pinning nuget.org as sole source (E-0)
```

**Aucun commit n'a été créé. Aucun push n'a été fait.** La branche `p4-net10` est inchangée.

---

## 8. Reproduire ce rapport

```powershell
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH   # §6.1 — remède temporaire, à rendre persistant
dotnet --version                                      # 10.0.103
dotnet nuget list source                              # nuget.org seule
dotnet restore MMV.sln                                # 8/8
dotnet build MMV.sln -c Debug                         # 0 avertissement, 0 erreur
dotnet test MMV.sln -c Debug --no-build               # 1953/1953
dotnet tool restore                                   # dotnet-ef 8.0.27
dotnet list MMV.sln package --vulnerable --include-transitive
```

Preuve de restauration à froid (`ISOLÉ`), dossier jetable **hors du dépôt**, supprimé après relevé :

```powershell
dotnet restore MMV.sln --packages "$env:TEMP\coldpkgs"
```

> Après cette commande, les `obj/project.assets.json` désignent le dossier jetable. **Relancer
> `dotnet restore MMV.sln`** pour rétablir l'état normal — ce qui a été fait (#3 exécuté une seconde fois, vert).
