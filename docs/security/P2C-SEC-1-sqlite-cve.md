# P2C-SEC-1 — CVE SQLite (GHSA-2m69-gcr7-jv3q / CVE-2025-6965)

> Note de sécurité en français simple. Objectif : expliquer la vulnérabilité, pourquoi
> elle est apparue sans qu'on ait modifié le code, ce qui a été corrigé, et comment vérifier.

## 1. La CVE en une phrase

Le moteur natif **SQLite** livré avec l'application (via EF Core) contenait un défaut de
**corruption mémoire** (CVE-2025-6965, gravité **High**, CVSS 7.2). Il touche toutes les
versions de SQLite **antérieures à 3.50.2**. L'avis GitHub associé est
`GHSA-2m69-gcr7-jv3q`, sur le paquet NuGet `SQLitePCLRaw.lib.e_sqlite3` (le binaire natif
de SQLite).

Détail technique de l'avis : le nombre de termes d'agrégat pouvait dépasser le nombre de
colonnes disponibles, ce qui ouvre une possibilité de corruption mémoire exploitable.

## 2. Pourquoi elle est apparue sans modification du code

C'est un **événement externe**, pas une régression de MMV :

- La baseline de la phase **P2C-4** (2026-06-16) mesurait **0 vulnérabilité**.
- La baseline **P2C-GLOBAL** (2026-07-05) a détecté **1 vulnérabilité High**.
- Entre ces deux dates, l'avis de sécurité amont a été **publié**. Le paquet natif
  `SQLitePCLRaw.lib.e_sqlite3 2.1.6` n'a pas changé dans le dépôt : c'est la
  **connaissance publique du risque** qui est apparue.

La chaîne exacte qui amenait ce binaire (jamais référencé directement) :

```
MMV.Infrastructure
  └─ Microsoft.EntityFrameworkCore.Sqlite 8.0.27
       └─ Microsoft.Data.Sqlite.Core 8.0.27
            └─ SQLitePCLRaw.bundle_e_sqlite3 2.1.6
                 └─ SQLitePCLRaw.lib.e_sqlite3 2.1.6   ← vulnérable (SQLite < 3.50.2)
```

Comme c'est un paquet **transitif** (tiré automatiquement), il se propageait à tous les
projets qui dépendent de l'Infrastructure : App, App.Tests, Domain.Tests, Application.Tests.

## 3. Ce qui a été corrigé

Une **remédiation minimale par les dépendances uniquement** (aucune ligne de code métier,
aucun changement de modèle EF, aucune migration).

Ajout d'une **référence de paquet de niveau supérieur** dans
`src/MMV.Infrastructure/MMV.Infrastructure.csproj` :

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.0" />
```

Le bundle `3.0.0` dépend de `SQLitePCLRaw.lib.e_sqlite3 >= 3.50.3`. En épinglant le bundle
en niveau supérieur, NuGet **remonte** le binaire natif de `2.1.6` à `3.50.3`
(SQLite 3.50.3, au-dessus du correctif 3.50.2). C'est la même technique de « pin de
sécurité » déjà utilisée dans ce fichier à l'Étape 0 pour `System.Text.Json`.

Versions résolues après correction :

| Paquet | Avant | Après |
|---|---|---|
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.6 (transitif) | **3.0.0** (top-level) |
| `SQLitePCLRaw.lib.e_sqlite3` | **2.1.6 (vulnérable)** | **3.50.3 (corrigé)** |
| `SQLitePCLRaw.core` | 2.1.6 | 3.0.0 |
| `SQLitePCLRaw.provider.e_sqlite3` | 2.1.6 | 3.0.0 |
| `SQLitePCLRaw.config.e_sqlite3` | — | 3.0.0 |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.27 | 8.0.27 (inchangé) |
| `Microsoft.Data.Sqlite.Core` | 8.0.27 | 8.0.27 (inchangé) |

EF Core reste en **8.0.27** (aucun saut de version majeur). Microsoft.Data.Sqlite.Core
8.0.27 fonctionne avec SQLitePCLRaw 3.x : les 463 tests (dont les tests d'intégration EF
SQLite sur base réelle) passent sans modification.

## 4. Comment vérifier que le risque est traité

```bash
dotnet restore MMV.sln
dotnet list MMV.sln package --vulnerable --include-transitive   # attendu : 0 vulnérabilité
dotnet list src/MMV.Infrastructure/MMV.Infrastructure.csproj package --include-transitive
#   → SQLitePCLRaw.lib.e_sqlite3 doit être résolu en 3.50.3 (et non 2.1.6)
dotnet build MMV.sln --no-restore -c Debug                      # vert, sans NU1903
dotnet test  MMV.sln --no-build   -c Debug                      # 463 tests verts
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
#   → "No changes have been made to the model since the last migration."
```

## 5. Quand revoir le sujet

Ce n'est **pas** une exception : la vulnérabilité est réellement corrigée (0 vulnérabilité).
Il n'y a donc **pas** de dette de sécurité ni d'échéance de revue obligatoire.

Point de vigilance pour l'avenir : lors d'une future montée de
`Microsoft.EntityFrameworkCore.Sqlite` (par ex. EF Core 9/10), vérifier que le pin
`SQLitePCLRaw.bundle_e_sqlite3` reste **cohérent** (≥ version tirée par le nouveau
Microsoft.Data.Sqlite) et supprimer le pin s'il devient redondant (c.-à-d. si le nouveau
EF tire déjà un `lib.e_sqlite3 >= 3.50.2`). L'audit NuGet du dépôt (`Directory.Build.props`,
`NuGetAudit` + `NuGetAuditMode=all`, NU1903 bloquant en CI) reste le garde-fou.
