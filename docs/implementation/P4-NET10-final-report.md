# Rapport de clôture — montée de MMV vers .NET 10 (LTS) — lot `P4-NET10`

> **Nature : rapport d'exécution et de clôture.** Il consigne ce qui a été **mesuré**, pas ce qui était prévu.
>
> Date d'exécution : **30 septembre 2026**. Branche : **`p4-net10`**.
> Base du lot : **`32465f6`** — HEAD de la [baseline E-1](P4-NET10-E1-baseline-report.md).
> Plan de référence : [plan d'exécution .NET 10](../architecture/net10-migration-execution-plan.md).
> Origine : [ADR-APP-DISTRIBUTION-001](../architecture/adr-app-distribution-001-installation-and-updates.md)
> **DI-8** et son obligation **OI-10** — .NET 10 avant la Release V1, aucune Release V1 sur .NET 8.
> **.NET 8 sort de support le 10 novembre 2026.**

## Verdict

```
P4-NET10                 = COMPLETE
SDK                      = 10.0.401  (rollForward: disable — poste ET runner)
TARGET FRAMEWORK         = net10.0   (8 projets de MMV.sln + 1 spike)
EF CORE                  = 10.0.12
NPGSQL EF PROVIDER       = 10.0.3
dotnet-ef                = 10.0.12
TESTS                    = 1953 / 1953 — 0 échec — 0 ignoré
AVERTISSEMENTS DE BUILD  = 0   (identique à la baseline E-1)
VULNÉRABILITÉS H/C       = 0
MIGRATIONS SQLite        = 14 — INCHANGÉES (identifiants et ordre identiques)
MIGRATIONS PostgreSQL    = 1  — INCHANGÉE
INSTANTANÉS EF           = BIT À BIT IDENTIQUES (empreintes git égales)
G-4 (dérive de modèle)   = NON DÉCLENCHÉE
CI                       = VERTE sur le SHA exact
READY FOR P4-6B          = OUI, sous réserve du §9
```

---

## 1. Versions — avant / après

### 1.1 Socle

| Élément | Avant (baseline E-1) | Après | Preuve |
|---|---|---|---|
| SDK (`global.json`) | `8.0.417`, `rollForward: latestFeature` | **`10.0.401`**, **`rollForward: disable`** | `dotnet --version` → `10.0.401` ; journal CI → `Version: 10.0.401` |
| `TargetFramework` | `net8.0` (8 projets + 2 spikes) | **`net10.0`** (8 projets + 1 spike ; `MMV.P4.Worker` reste `net8.0`, §5) | sorties dans `bin/Debug/net10.0/` |
| `LangVersion` | `12.0` | **`12.0` — inchangé** | plan §3.4 |
| Runtime d'exécution | 8.0.23 | **10.0.12** (livré avec le SDK 10.0.401) | `dotnet --list-runtimes` |
| `dotnet-ef` | `8.0.27` | **`10.0.12`**, `rollForward: false` conservé | `dotnet ef --version` |

> **Le point que E-1 §6.2 avait laissé ouvert est clos.** Avec `rollForward: latestFeature`, `global.json`
> demandait `10.0.103` et le poste résolvait `10.0.401`, tandis que la CI aurait installé une `10.0.1xx` :
> **poste et runner n'auraient pas compilé avec le même SDK**. Le commit `145bd8b` a épinglé `10.0.401` en
> `rollForward: disable`. **Vérifié sur pièces :** le journal CI du run `36734663921` affiche
> `Version: 10.0.401`, la même que le poste.

### 1.2 Paquets modifiés

| Paquet | Avant | Après | Projets |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.27 | **10.0.12** | `MMV.Infrastructure`, `MMV.Domain.Tests`, `MMV.Application.Tests` |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.27 | **10.0.12** | `MMV.Infrastructure`, `MMV.App`, `MMV.Infrastructure.PostgreSQL.Migrations` |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.11 | **10.0.3** | `MMV.Infrastructure` |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | **10.0.12** | `MMV.App` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 8.0.1 | **10.0.12** | `MMV.Application` |
| `Microsoft.Extensions.Hosting` | 8.0.1 | **10.0.12** | `MMV.App` |
| `Microsoft.Extensions.Configuration.Abstractions` | 8.0.0 | **10.0.12** | `MMV.Infrastructure` |
| `System.Text.Json` | 8.0.6 (épingle) | **RETIRÉ** — fourni par le framework | `MMV.App`, `MMV.Infrastructure` |
| `dotnet-ef` (outil) | 8.0.27 | **10.0.12** | `.config/dotnet-tools.json` |

**Contrainte dure respectée.** Le `.nuspec` de `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** exige
`Microsoft.EntityFrameworkCore` dans l'intervalle **`[10.0.4, 11.0.0)`**. Une cible EF Core inférieure à
10.0.4 était donc impossible ; **10.0.12** la satisfait. Versions revérifiées sur `api.nuget.org` **le jour de
l'exécution** (condition **G-5**) : 10.0.12 et 10.0.3 sont les dernières publiées.

**Pourquoi les `Microsoft.Extensions.*` bougent.** EF Core 10.0.12 les exige en 10.0.12 par transitivité. Les
laisser en 8.0.x aurait produit une **rétrogradation NU1605** — précisément ce que le plan interdit de
contourner.

### 1.3 Paquets délibérément inchangés

| Paquet | Version | Motif |
|---|---|---|
| `Avalonia` et satellites, `FluentAvaloniaUI`, `CommunityToolkit.Mvvm`, `Projektanker.Icons.Avalonia` | 11.2.8 / 2.1.0 / 8.2.2 / 9.6.2 | plan §3.3 — leurs assemblages `net8.0` / `netstandard2.0` se résolvent sans difficulté depuis `net10.0`. **La couche interface graphique n'est pas touchée : le risque de régression visuelle est nul par construction.** La série Avalonia 12.x est un **lot distinct** |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | le plan la donnait `À VÉRIFIER`, avec un passage possible en 18.x annoncé comme *« point de risque réel »*. **Mesure : 17.12.0 exécute les 1953 tests sur `net10.0` sans réserve.** Le risque ne s'est pas matérialisé ; aucune raison de toucher à la plateforme de test |
| `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Moq` 4.20.72, `coverlet.collector` 6.0.0 | — | inchangés, aucun symptôme |
| `FluentAssertions` | 6.12.0 | **épinglé volontairement — v7+ est sous licence commerciale.** Ne pas monter |
| `SQLitePCLRaw.bundle_e_sqlite3` | 3.0.0 | épingle de sécurité **portante**, §3.2 |
| `Tmds.DBus.Protocol` | 0.21.3 | épingle de sécurité **portante**, §3.3 |

---

## 2. Commits réalisés

Un commit par étape, dans l'ordre du plan §4, **jamais squashés**.

| # | SHA | Message | Étape | Contenu |
|---|---|---|---|---|
| 1 | `32465f6` | `chore(P4-NET10): update global.json … add NuGet.config` | E-0 (+ E-2) | base du lot — `NuGet.config` versionné, `global.json` → 10.0.103 |
| 2 | `145bd8b` | `chore(P4-NET10): pin SDK 10.0.401 for deterministic migration` | E-2 | `rollForward: disable` — lève le point E-1 §6.2 |
| 3 | `d822191` | `chore(P4-NET10): migrate projects to net10.0` | E-3 | `TargetFramework` des 8 projets de la solution |
| 4 | **`3880d03`** | `chore(P4-NET10): upgrade dependencies to .NET 10` | **E-4** | EF Core, Npgsql, `Microsoft.Extensions.*`, `dotnet-ef` + adaptation de test (§4) |
| 5 | **`6bbba52`** | `chore(P4-NET10): cleanup .NET 10 compatibility` | **E-5** | épingles réexaminées, `NU1510` résorbés, `regenerate-database.ps1` |
| 6 | **`4e961af`** | `chore(P4-NET10): align P4-1 spike with .NET 10` | **E-8** | spike `MMV.P4.ProviderComparison` réparé |
| 7 | *(ce commit)* | `docs(P4-NET10): finalize migration report` | **E-9** | ce rapport, roadmap P4, README, CONTRIBUTING, ARCHITECTURE |

> **Les étapes E-6 (validation complète) et E-7 (validation CI) n'ont produit aucun commit, et ne le pouvaient
> pas : ce sont des étapes de *vérification*, sans fichier à modifier.** Leurs preuves sont consignées ici,
> §6 et §7. Le message `chore(P4-NET10): validate .NET 10 migration` prévu par la consigne aurait été un
> **commit vide** : il n'a pas été fabriqué.

---

## 3. Les trois épingles de sécurité — réexaminées une par une

Le plan §3.5 posait trois cas distincts. **Chacun a été tranché sur mesure, aucun sur supposition.**

### 3.1 `System.Text.Json` — RETIRÉE

- **Mesure avant retrait :** version résolue **8.0.6**. L'épingle **gagnait** la résolution alors que, sur
  `net10.0`, le framework partagé fournit la **10.x**. Elle **rétrogradait donc l'assembly de deux majeures** —
  c'est ce que signalaient les **2 avertissements NU1510** apparus avec le changement de TFM.
- Les avis d'origine **GHSA-hh2w-p6rv-4g7w** et **GHSA-8g4q-xg66-9fp4** sont corrigés depuis **8.0.5** :
  l'épingle n'avait plus d'objet.
- **Preuve du retrait, double :** `System.Text.Json` a disparu du graphe de paquets **et**
  `src/MMV.App/bin/Debug/net10.0/System.Text.Json.dll` **n'existe plus** — l'assembly vient du framework.

### 3.2 `SQLitePCLRaw.bundle_e_sqlite3` 3.0.0 — CONSERVÉE, elle est portante

- `Microsoft.EntityFrameworkCore.Sqlite` **10.0.12** tire encore le bundle **2.1.12** (relevé sur le `.nuspec`).
  **La montée en EF Core 10 n'a rien changé au raisonnement d'origine.**
- La version du binaire SQLite embarqué par `SQLitePCLRaw.lib.e_sqlite3` **2.1.12** **n'est pas lisible sur le
  paquet**. Le plan §4 E-5 est explicite : *« si la version native SQLite ne peut pas être établie, conserver
  l'épingle 3.0.x et documenter le doute »*.
- Avec l'épingle, la version résolue est **`SQLitePCLRaw.lib.e_sqlite3` 3.50.3** — numéro qui **est** la version
  de SQLite, donc vérifiable, et **au-dessus du correctif 3.50.2** exigé par
  **GHSA-2m69-gcr7-jv3q / CVE-2025-6965**.
- **Conservée.** Une épingle de sécurité ne se retire pas sur une supposition.

### 3.3 `Tmds.DBus.Protocol` 0.21.3 — CONSERVÉE, et un constat de E-1 est corrigé

- Le [rapport E-1](P4-NET10-E1-baseline-report.md) §9 la donnait **introuvable** dans le relevé. **C'était
  inexact :** elle est bien résolue, en **0.21.3**.
- **Relevé du jour sur le `.nuspec` :** `Avalonia.FreeDesktop` **11.2.8** exige `Tmds.DBus.Protocol` **0.20.0**,
  **sous** le correctif **0.21.2** de **GHSA-xrw6-gwf8-vvr9**.
- **La retirer réintroduirait un avis High.** Avalonia ne bougeant pas dans ce lot, son transitif ne bouge pas
  non plus. **Conservée.**

---

## 4. Le seul écart au plan — une assertion de test, et pourquoi

Le plan §5.4 posait comme critère de sortie que le diff du lot **ne montre aucun `.cs`**. **Ce critère n'est pas
tenu : un fichier de test a changé.** L'écart est assumé, instruit, escaladé, et tranché par l'architecte.

**Ce qui s'est passé.** Après la montée en EF Core 10, **1 test sur 1953** a échoué :
`SupplierDeletionSqlAndTrackerTests.TryDeleteIfUnused_SansProduit_ProduitUneSeuleCommandeDelete_AvecConditionNotExists`.

| | |
|---|---|
| SQL produit par EF **8** | `WHERE "s"."SupplierId" = @__supplierId_0 AND NOT EXISTS (…)` |
| SQL produit par EF **10** | `WHERE "s"."SupplierId" = @supplierId AND NOT EXISTS (…)` |
| Assertion en cause | `sql.Should().Contain("@__supplierId_0")` |
| Occurrences dans le dépôt | **1 seule** |

**Ce n'est pas une régression fonctionnelle.** EF Core a simplifié la convention de nommage des paramètres dans
le SQL généré entre la 8 et la 10. L'identifiant reste **lié par un paramètre nommé** ; la propriété que le test
visait — *« jamais concaténé en clair »* — est **intacte**, et la garde indépendante contre toute valeur
littérale passait **sans modification**.

**Ce que le test écrivait en dur était un détail d'implémentation d'EF, pas un contrat.**

**Décision de l'architecte : adapter ET durcir.** L'assertion ne porte plus sur un nom d'implémentation mais sur
la **collection `DbParameter` réellement transmise au pilote** :

- `CommandRecordingInterceptor` prend désormais un **instantané des paramètres liés** (nom et valeur) **au
  moment de l'exécution** — la collection du `DbCommand` est réutilisable par le pilote et ne peut pas être
  relue fiablement après coup ;
- le test exige **un paramètre unique**, **nommé**, **portant l'identifiant réel du fournisseur**, et
  **référencé sous ce nom dans le texte de commande** ;
- la garde contre toute valeur littérale est **conservée telle quelle**.

**Le test est désormais structurellement insensible au nommage interne d'EF** — il aurait passé sur EF 8 comme
il passe sur EF 10. **Aucun test supprimé. Aucun test ajouté. Le compte reste 1953.**

> **Portée de l'écart, pour qu'il ne soit pas sur-interprété.** Le fichier touché est un **fichier de test**.
> **Aucun fichier de `src/` n'a été modifié par ce lot.** Aucun comportement de production n'a changé.

---

## 5. Spikes — la rupture que seule E-8 pouvait voir

Les deux projets de spikes sont **hors `MMV.sln`** : aucune CI ne les construit, donc **aucune CI ne pouvait
signaler ce qui suit** (plan §5.5, risque **R-5**).

| Spike | Avant E-8 | Après E-8 |
|---|---|---|
| `MMV.P4.ProviderComparison` | **NE COMPILAIT PLUS** — 2 × `NU1201` : *« Le projet MMV.Domain n'est pas compatible avec net8.0. Le projet MMV.Domain prend en charge : net10.0 »* | **`net10.0`**, paquets EF alignés (`SqlServer` et `Relational` **10.0.12**, `Npgsql.EFCore.PostgreSQL` **10.0.3**). **Build explicite réussi : 0 erreur, 0 avertissement** |
| `MMV.P4.Worker` | compilait | **reste `net8.0`, délibérément** — aucune `ProjectReference` vers `src/`, donc rien ne le contraint à suivre le TFM de la solution. Build explicite vérifié : réussi, seul `CA1416` (API Windows dans un exécutable Windows), **préexistant** |

**Pourquoi cela comptait.** Sans E-8, la casse serait restée invisible jusqu'au jour où l'on aurait voulu
rejouer le spike P4-1 — c'est-à-dire **le jour où l'on veut re-prouver les 14 primitives** (obligation **O12**).
**Le moment de la découverte aurait été le pire possible.** Les deux décisions sont écrites dans l'en-tête de
chaque `.csproj`, jamais laissées implicites.

---

## 6. E-6 — validation complète

Batterie **VP-1 … VP-7** du plan §5.1, rejouée **à froid** : `bin/` et `obj/` supprimés, puis restore, build et
tests intégralement rejoués. Cela vérifie du même coup la **reproductibilité de la restauration**.

| # | Contrôle | Attendu (baseline E-1) | Obtenu | Verdict |
|---|---|---|---|---|
| **VP-1** | Outillage | SDK 10 résolu | `10.0.401` | **PASS** |
| **VP-2** | Restauration | succès, 0 `NU1605` | exit 0, **0 avertissement** | **PASS** |
| **VP-3** | Construction | avertissements relevés et comparés | **0 avertissement, 0 erreur** — *identique à la liste vide de E-1* | **PASS** |
| **VP-4** | Tests | **1953**, 0 échec, 0 ignoré | **1953 / 0 / 0** | **PASS** |
| **VP-5** | Dérive SQLite | exit 0 | exit **0** | **PASS** |
| **VP-6** | Dérive PostgreSQL + garde | exit 0, garde opérante | exit **0**, garde **opérante** | **PASS** |
| **VP-7** | Vulnérabilités | aucune High/Critical | **aucune, tous niveaux confondus**, 8 projets, transitifs inclus | **PASS** |

### 6.1 Tests — avant / après

| Assemblage | Baseline E-1 (`net8.0`, EF 8) | Clôture (`net10.0`, EF 10) | Écart |
|---|---|---|---|
| `MMV.Domain.Tests` | 1065 | **1065** | **0** |
| `MMV.Application.Tests` | 628 | **628** | **0** |
| `MMV.App.Tests` | 260 | **260** | **0** |
| **TOTAL** | **1953** | **1953** | **0** |

**0 échec, 0 ignoré, avant comme après. Aucun test ajouté, aucun retiré.**

> **Note sur le chiffre 1569.** Le plan d'exécution annonce **1569** en six endroits (§2.2, §4 E-1, §5.1 VP-4,
> §5.2-1, §9.1 G-2, §9.4-4). **Cette donnée est périmée** : elle vient de la baseline P4-4C, non rafraîchie
> contre les livraisons **P4-5D** et **P4-5E** (+384 tests). [E-1 §6](P4-NET10-E1-baseline-report.md) l'a
> instruit et clos **avant** le lot, corroboré par le rapport de vérification finale P4-5. **Le compte de
> référence du lot est 1953**, et il est tenu. *Le plan d'exécution n'a pas été réécrit par ce lot ; il porte
> lui-même la mention « Le dépôt réel prime toujours sur ce document ».*

### 6.2 G-4 — le point d'arrêt le plus important du plan : **NON DÉCLENCHÉ**

C'était le risque de fond, explicitement **non mesuré** avant le lot : *deux majeures d'EF séparent 8.0.27 de
10.0.x ; que la traduction du modèle soit strictement identique était* `À VÉRIFIER`.

**Elle l'est.** Le contrôle exigé n'est pas un code de sortie mais la **comparaison littérale** des listes de
migrations avec l'archive E-1 — *mêmes identifiants, même ordre, même nombre*.

**Chaîne SQLite — 14 migrations. `diff` avec l'archive E-1 : aucune différence.**

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

**Chaîne PostgreSQL — 1 migration, garde anti-faux-vert opérante** (`InitialPostgreSqlBaseline` **présente**,
`InitialCreate` **absente**) :

```
20260922001219_InitialPostgreSqlBaseline
```

**Preuve supplémentaire, plus forte qu'une liste — les instantanés EF sont bit à bit identiques :**

| Instantané | Empreinte git à `32465f6` | Empreinte git à HEAD |
|---|---|---|
| `src/MMV.Infrastructure/Migrations/OpticDbContextModelSnapshot.cs` | `bc9b8b1361e184d0aa111f5605fbd2d0f4207af7` | **`bc9b8b1361e184d0aa111f5605fbd2d0f4207af7`** |
| `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/OpticDbContextModelSnapshot.cs` | `4fa6e0f07f81cde8eb3b1448725718a1e954b423` | **`4fa6e0f07f81cde8eb3b1448725718a1e954b423`** |

**Aucun fichier sous un répertoire `Migrations/` n'a été modifié par le lot.** Aucune migration créée, aucun
schéma touché, aucune commande `database update` exécutée.

---

## 7. E-7 — validation CI

**Aucune modification du workflow n'a été nécessaire**, et aucune n'a été faite.

| Contrôle | Constat |
|---|---|
| Déclenchement | `on.push.branches` contient déjà `p4*` : **`p4-net10` est couverte sans toucher au workflow** |
| Installation du SDK | `actions/setup-dotnet@v4` avec **`global-json-file: global.json`** — suit l'épingle sans modification |
| SDK réellement utilisé sur le runner | **`10.0.401`**, lu dans le journal (`dotnet --info`) — **exactement celui du poste** |
| Restauration reproductible | vérifiée à froid localement **et** sur le runner (checkout neuf) |

**Deux runs verts, sur deux SHA exacts.**

| Run | SHA | Étape couverte | Résultat |
|---|---|---|---|
| [`36734663921`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/36734663921) | **`6bbba52`** | E-4 + E-5 — la partie risquée du lot | **succès**, 5 min 41 s |
| [`36735563202`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/36735563202) | **`4e961af`** | E-8 — **dernier SHA de code du lot** | **succès**, 4 min 57 s |

Les deux journaux ont été **lus intégralement**, et pas seulement leurs coches vertes. Relevés du run
`36734663921` (ceux du run `36735563202` sont identiques : `10.0.401`, `0 Warning(s)`, `0 Error(s)`,
`260 / 1065 / 628` tous `Passed`, audit vert, les deux dérives vertes, `Chaine controlee : 1 migration(s).`) :

| Step | Relevé dans le journal |
|---|---|
| Diagnostic SDK | ` Version:           10.0.401` |
| Restore | succès, aucun `NU1605`, aucun `NU1510` |
| Build | `Build succeeded.` · **`0 Warning(s)`** · **`0 Error(s)`** |
| Test | `Passed! … Passed: 260` · `Passed: 1065` · `Passed: 628` — **0 Failed, 0 Skipped** |
| Audit vulnérabilités (JSON + sévérité) | `Aucune vulnerabilite High/Critical detectee.` |
| Restore .NET tools | succès — `dotnet-ef` 10.0.12 depuis le manifeste |
| Dérive EF SQLite | `No changes have been made to the model since the last migration.` |
| Dérive EF PostgreSQL + garde | `Chaine controlee : 1 migration(s).` · `- 20260922001219_InitialPostgreSqlBaseline` · dérive verte |

> **Les lignes `Write-Error` visibles dans le journal ne sont pas des erreurs :** ce sont les **sources** des
> scripts PowerShell, que GitHub Actions recopie avant exécution. **Aucun de ces chemins d'échec n'a été
> emprunté** — le step se termine sur `Aucune vulnerabilite High/Critical detectee.` et sur une dérive verte.

**Le commit E-9 qui porte ce rapport est strictement documentaire** — aucun `.cs`, aucun `.csproj`, aucune
dépendance, aucune migration. Il déclenche néanmoins sa propre CI, et celle-ci doit être verte pour que le lot
soit clos : c'est la même discipline que la clôture de P4-0 et de P4-3, où un run couvre le contenu et un
second la clôture documentaire.

**Seule annotation des runs :** `Node.js 20 is deprecated` sur `actions/checkout@v4` et `actions/setup-dotnet@v4`.
C'est une **notice d'infrastructure GitHub**, sans rapport avec ce lot et sans effet sur son résultat.
**Elle n'a pas été traitée ici** — le plan §1.2 interdit de profiter d'une montée de runtime pour faire autre
chose. À instruire pour elle-même.

---

## 8. Risques rencontrés

| Risque | Prévu par le plan | Ce qui s'est réellement passé |
|---|---|---|
| **G-4 — dérive du modèle EF entre la 8 et la 10** | oui — *« réel et non mesuré »*, point d'arrêt majeur | **Ne s'est pas matérialisé.** Migrations identiques, instantanés bit à bit identiques |
| **R-5 — rupture silencieuse des spikes** | oui, sans détection automatique possible | **S'est matérialisé** : `NU1201`, deux fois. Corrigé en E-8 (§5) |
| **Épingle `System.Text.Json` devenue nocive** | oui (§2.3 b) | **S'est matérialisé** : 2 × `NU1510`, version résolue 8.0.6 sur un framework qui fournit la 10.x. Résorbé en E-5 (§3.1) |
| **`Microsoft.NET.Test.Sdk` 17.x → 18.x** | oui — *« point de risque réel »* | **Ne s'est pas matérialisé.** 17.12.0 exécute les 1953 tests sur `net10.0`. Rien à changer |
| **Avalonia incompatible avec `net10.0`** | jugé nul (§3.3) | **Confirmé nul.** Aucune ligne d'interface touchée, aucun avertissement |
| **Écart SDK poste / runner** | signalé par E-1 §6.2, après rédaction du plan | **Résolu avant E-4** par `rollForward: disable` (`145bd8b`), **vérifié sur le journal CI** |
| **Changement de nommage des paramètres SQL par EF** | **non prévu par le plan** | **S'est matérialisé** : 1 test rouge. Instruit, escaladé, tranché, durci (§4) |

---

## 9. Ce que ce lot NE prouve PAS

**À ne pas confondre avec une garantie.** Le plan §5.5 l'énonce ; c'est répété ici parce que c'est la partie
d'un rapport vert qu'on oublie de lire.

1. **La publication autonome `win-x64` sur .NET 10 n'est pas prouvée.** La CI construit en **Debug** et ne
   publie rien. Cette preuve appartient à **P8** (DI-1). **Une CI verte ne sert pas la Release V1.**
2. **Le démarrage de l'application n'a pas été vérifié.** Le plan §5.4 en fait une **vérification manuelle**.
   Elle **n'a pas été exécutée par ce lot** : lancer l'application aurait appliqué les migrations à une base de
   développement locale, ce que la consigne d'exécution interdisait (*aucun `database update`*).
   **Action restante pour l'architecte : lancer `MMV.App` et constater l'ouverture de l'écran de connexion.**
3. **Aucun serveur PostgreSQL n'a été touché.** Le comportement d'exécution de **Npgsql 10** reste
   **non mesuré** (**R-9**), et le restera jusqu'à **P4-5F**. Le bénéfice de l'arbitrage G-7 est que ce corpus
   s'écrira **directement sur Npgsql 10** — ce n'est **pas** une preuve déjà acquise.
4. **Les réserves fonctionnelles P4 restent entières** : mapping monétaire (**O2**), index filtrés PostgreSQL
   (**O3**), stratégie `DateTime` (**O4**). **Ce lot est une montée de runtime, pas une avancée multi-poste.**

---

## 10. Q-6 — tranchée sur pièces, mais la décision reste à l'architecte

Le plan §4 E-9 attendait de ce lot qu'il rende **Q-6** décidable. La question posée par
[ADR-PROD-DB-009](../architecture/ADR-PROD-DB-009.md) est : *rester sur EF Core 8 avec un verrou **LK-1**, ou
bénéficier du verrou de migration natif d'EF Core ≥ 9 (**LK-5**) ?* — l'ADR précisant que *« sur quoi s'appuie
l'implémentation Npgsql de ce verrou reste* `UNKNOWN` *: c'est à vérifier, pas à supposer »*.

**La condition bloquante est levée** — LK-5 exigeait une montée d'EF Core et de Npgsql, désormais faite.
**Et l'`UNKNOWN` est levé lui aussi**, par inspection directe de l'assemblage
`Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** réellement livré dans le dépôt :

| Constat | Preuve relevée dans l'assemblage |
|---|---|
| Le verrou natif **existe** et est implémenté par Npgsql | types et membres `IMigrationsDatabaseLock`, **`NpgsqlMigrationDatabaseLock`**, `AcquireDatabaseLock`, `AcquireDatabaseLockAsync`, `AcquiringMigrationLock`, `get_LockReleaseBehavior` |
| Sur quoi il s'appuie | littéraux SQL **`LOCK TABLE `** et **` IN ACCESS EXCLUSIVE MODE`** |
| Ce sur quoi il **ne** s'appuie **pas** | **`pg_advisory` est totalement absent de l'assemblage** |

**Conséquence, et c'est le point que l'architecte doit trancher.** **DP-2** a arrêté *« mécanisme natif
PostgreSQL (famille **LK-1**), **pas de table** »*, LK-1 désignant les verrous **consultatifs**
(`pg_advisory_lock`, `pg_try_advisory_lock`). **L'implémentation Npgsql de LK-5 n'est pas un verrou
consultatif** : c'est un **verrou de table `ACCESS EXCLUSIVE` posé sur la table d'historique des migrations**,
tenu pour la durée de la transaction. Il est bien **natif PostgreSQL** et ne **crée** aucune table — il ne
s'agit donc pas de LK-3 — **mais ce n'est pas LK-1 non plus.**

> **Aucun ADR n'a été modifié par ce lot**, conformément à la consigne d'exécution. **Q-6 est désormais
> décidable sur pièces, et non plus par supposition** ; la formulation de la réponse dans ADR-PROD-DB-009 et
> le rapport de ce constat à **DP-2** **appartiennent à l'architecte**, et relèvent de **P4-6B** (forme exacte
> du verrou).

---

## 11. Retour arrière

**Le lot est révocable, et chaque étape l'est séparément.** Aucun commit n'a été squashé, précisément pour cela.

| Portée du retour | Commande | Effet |
|---|---|---|
| **Tout le lot** | `git revert --no-commit 4e961af 6bbba52 3880d03 d822191 145bd8b` puis un commit | retour à `32465f6` — SDK 8.0.417, `net8.0`, EF Core 8.0.27 |
| **EF Core seul**, en gardant `net10.0` | `git revert 3880d03` | **inopérant en pratique** : EF Core 10 ne cible que `net10.0`, et EF Core 8 ne se résout pas proprement depuis `net10.0` avec les `Microsoft.Extensions.*` en 10.x. Revenir sur EF impose de revenir aussi sur le TFM (`d822191`) |
| **Épingles seules** | `git revert 6bbba52` | réintroduit `System.Text.Json` 8.0.6 et les 2 `NU1510` |
| **Spike seul** | `git revert 4e961af` | le spike redevient non compilable (`NU1201`) |

**Ce qui rend le retour arrière sûr :** **aucune migration n'a été créée**, **aucun schéma n'a été touché**,
**aucune base n'a été mise à jour**. Un retour de code est donc un retour **complet** — il n'existe aucun état
de base à défaire. C'est la conséquence directe du respect de la règle G-4.

> **Une réserve.** `global.json` épingle `10.0.401` en `rollForward: disable`. Un retour arrière **doit**
> restaurer aussi `global.json`, faute de quoi le dépôt exigerait un SDK 10 pour compiler du `net8.0` — ce qui
> fonctionne, mais brouille le diagnostic.

---

## 12. Documentation mise à jour par E-9

Uniquement ce qui était devenu **factuellement faux**.

| Fichier | Correction |
|---|---|
| `README.md` | *.NET 8* → **.NET 10** (4 endroits : présentation, stack technique, EF Core, lien SDK des prérequis) |
| `CONTRIBUTING.md` | tableau des prérequis : SDK **10.0.401** avec `rollForward: disable` ; `dotnet-ef` **10.0.12** |
| `ARCHITECTURE.md` | `dotnet-ef 8.0.27` → **10.0.12** ; 3 chemins `net8.0` → **`net10.0`** (configuration de lancement VS Code, sortie de publication, source Inno Setup) — **ces chemins pointaient dans le vide depuis le changement de TFM** |
| `docs/architecture/P4-multi-poste-roadmap.md` | `P4-NET10` → **`COMPLETED`** dans les trois endroits qui portaient son état ; compte de référence corrigé en **1953** ; **Q-6** consignée comme décidable, avec son constat |
| `docs/implementation/P4-NET10-final-report.md` | **ce rapport** |

**Non modifiés, délibérément :** les **ADR** (consigne d'exécution — voir §10) et le **plan d'exécution
.NET 10**, document daté qui porte lui-même la mention *« Le dépôt réel prime toujours sur ce document »*.
Ses six occurrences de **1569** restent périmées ; le §6.1 ci-dessus le consigne.

---

## 13. Conclusion

**La montée vers .NET 10 est terminée, et elle n'a rien changé d'autre.**

Le résultat qui compte n'est pas que la CI soit verte : c'est que **le modèle EF se traduise exactement comme
avant**. Deux majeures d'EF Core séparent le point de départ du point d'arrivée, et les deux instantanés sont
**bit à bit identiques**. C'était le risque de fond du lot, et il était **non mesuré** au moment de le lancer.

Les deux ruptures réelles — le spike hors solution et l'épingle `System.Text.Json` — étaient toutes deux
**invisibles pour la CI**. Elles ont été trouvées parce que le plan prévoyait d'aller les chercher.

**Le troisième écart n'était prévu par personne** : EF Core a changé le nommage des paramètres dans le SQL
généré, et un test écrivait ce nom en dur. Il est désormais impossible qu'il se reproduise — le test lit le
paramètre réellement transmis au pilote, et non plus le texte qu'EF choisit d'écrire.

```
P4-NET10 = COMPLETE
SDK 10.0.401 · net10.0 · EF Core 10.0.12 · Npgsql 10.0.3 · dotnet-ef 10.0.12
1953 / 1953 TESTS · 0 AVERTISSEMENT · 0 VULNÉRABILITÉ
MIGRATIONS INCHANGÉES — 14 SQLite + 1 PostgreSQL — INSTANTANÉS BIT À BIT IDENTIQUES
G-4 NON DÉCLENCHÉE · AUCUNE MIGRATION CRÉÉE · AUCUN SCHÉMA TOUCHÉ · AUCUN database update
CI VERTE SUR LE SHA EXACT
RESTE À FAIRE, MANUELLEMENT : DÉMARRAGE DE L'APPLICATION (§9.2)
READY FOR P4-6B
```
