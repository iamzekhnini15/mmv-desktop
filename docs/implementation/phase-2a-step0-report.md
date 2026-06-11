  # PHASE 2A — ÉTAPE 0 — RAPPORT D'IMPLÉMENTATION

> **Date : 9 juin 2026.** Stabilisation qualité, dépendances et CI.
> Périmètre **strictement limité à l'Étape 0** de la feuille de route ([migration-roadmap.md](../architecture/migration-roadmap.md) §3). Aucune étape 1+ n'a été commencée. Aucune modification métier ni réglementaire n'a été réalisée.
>
> Référentiels : [phase-1-technical-audit.md](../architecture/phase-1-technical-audit.md), [risk-register.md](../architecture/risk-register.md) (R-10, R-22, R-24, R-21), [phase-1b-validation-report.md](../architecture/phase-1b-validation-report.md), et le référentiel métier `docs/domain/`.

---

## 1. État Git avant intervention

Branche courante : **`phase2a-stabilization`** (l'audit Phase 1 portait sur `refactor/redesignLightAndDarkMode`).

`git status --short` (avant toute écriture) :

```
?? docs/PHASE0-CARTOGRAPHIE.md
?? docs/PHASE0.5-DOMAINE-OPTIQUE.md
?? docs/architecture.zip
?? docs/architecture/
?? docs/domain.zip
?? docs/domain/
```

`git diff --stat` et `git diff --stat -- src tests` : **vides** (aucun fichier `src/` ou `tests/` modifié dans l'arbre de travail).

### 1.1 Écart constaté avec la Phase 1B (documenté avant de continuer)

La Phase 1B (§8) décrivait **16 fichiers `src/MMV.App/**` modifiés** (refonte thème clair/sombre) **non commités**, plus `IThemeService.cs`/`ThemeService.cs` non suivis. **Ce n'est plus l'état actuel.** Vérification :

```
git show --stat 81ee386   # "chore: checkpoint before phase 2"
 18 files changed, 227 insertions(+), 60 deletions(-)
 (App.axaml(.cs), Controls/*, Styles/AppStyles.axaml, ViewModels/PageViewModels.cs,
  ViewModels/UserProfileViewModel.cs, Services/IThemeService.cs, Services/ThemeService.cs,
  Views/** dont Views/UserProfileView.axaml, SettingsView.axaml, …)
git ls-files src/MMV.App/Services/IThemeService.cs src/MMV.App/Services/ThemeService.cs  → suivis
```

**Conclusion de l'écart :** le travail de thème préexistant a été **commité** dans `81ee386 chore: checkpoint before phase 2`. Il est donc désormais protégé par l'historique Git (et non plus présent comme modifications de l'arbre de travail). **Aucun de ces fichiers n'a été touché, reformaté ou supprimé durant l'Étape 0** (cf. §12-13 : le seul changement sous `src/MMV.App/` est le `.csproj` et la suppression de 2 fichiers `.old` sans rapport avec le thème).

---

## 2. Baseline initial (état réel du dépôt, revérifié)

Environnement (`dotnet --info`, `dotnet --list-sdks`) :

| Élément | Valeur |
|---|---|
| SDK par défaut (sans `global.json`) | **10.0.103** |
| SDK .NET installés | `8.0.417`, `10.0.102`, `10.0.103` |
| `global.json` | **absent** au baseline |
| TargetFramework (5 `.csproj`) | `net8.0` |

Commandes (SDK 10.0.103, sans `global.json`) :

| Commande | Résultat baseline |
|---|---|
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 avertissement / 0 erreur** |
| `dotnet test MMV.sln --no-build -c Debug` | **187 ✅ / 5 ❌** (App.Tests 79/79 ; Domain.Tests 108/113) |
| `dotnet list ... --vulnerable --include-transitive` | **5 packages transitifs High** (cf. §8) |
| `dotnet list ... --outdated --include-transitive` | nombreux packages en retard (non vulnérables : Avalonia 11.2.8, SkiaSharp 2.88.9, EF 8.0.0 vs 10.x, etc.) |

Baseline **identique** à celui de la Phase 1B (suite rouge sur checkout propre, mêmes 5 vulnérabilités). Aucune divergence.

---

## 3. Analyse des cinq tests en échec

| # | Test | Cause racine vérifiée |
|---|---|---|
| 1 | `Data.DbContextTests.ShouldCreateDatabaseAndSeedAdmin` | Le test attendait qu'`EnsureCreatedAsync()` seede l'admin (`UserId == 1`). Or `EnsureCreated` ne crée que le **schéma** ; le seed admin vit dans `DbInitializer.Initialize()` (impératif, non appelé). → `admin` est `null`. |
| 2 | `RepositoryTests.UserRepositoryTests.GetActiveUsersAsync_ShouldReturnOnlyActive` | Même cause : le commentaire « admin seed + active user » attendait **2**, le contexte (EnsureCreated, sans seed) ne contient que l'utilisateur actif créé par le test → **1**. |
| 3 | `ServiceTests.ProductServiceTests.CreateProductAsync_ShouldSucceed` | `Product.SupplierId` est une FK **obligatoire** (`OnDelete.Restrict`, `IsRequired`). Le test ne créait aucun `Supplier` → `SQLite Error 19: FOREIGN KEY constraint failed`. |
| 4 | `ServiceTests.ProductServiceTests.GetProductAsync_ShouldReturnProduct` | Idem (échoue dès `CreateProductAsync`). |
| 5 | `ServiceTests.ProductServiceTests.DeleteProductAsync_ShouldSucceed` | Idem (échoue dès `CreateProductAsync`). |

Deux dérives structurelles confirmées : (a) **stratégie de seed incohérente** (tests présumant un seed implicite que `EnsureCreated` ne produit pas) ; (b) **fixtures fragiles** ignorant la FK obligatoire Product↔Supplier.

**Principes appliqués (conformes aux consignes) :** le seed **n'a pas** été déplacé dans `HasData` ; la FK **n'a pas** été rendue optionnelle ; la stratégie de production `EnsureCreated` **n'a pas** été modifiée (réservée à l'Étape 1). Les tests ont été rendus **explicites** : chaque test crée ses propres fixtures et ne dépend plus d'aucune donnée de démonstration implicite.

---

## 4. Corrections appliquées, test par test

### 4.1 `DbContextTests` (test #1) — séparation schéma / DbInitializer
Le test conflateur a été **scindé en deux** ([DbContextTests.cs](../../tests/MMV.Domain.Tests/Data/DbContextTests.cs)) :
- `EnsureCreated_ShouldCreateSchema_WithoutSeedingUsers` — exerce la **création de schéma** seule et vérifie qu'**aucun** utilisateur n'est seedé (`Users.Count == 0`).
- `DbInitializer_Initialize_ShouldSeedAdminUser` — exerce **explicitement** `DbInitializer.Initialize(context)` puis vérifie l'admin (`UserId == 1`, `Username == "admin"`, `Role == Admin`, `IsActive`).

→ Net : **+1 test** (la suite Domain passe de 113 à 114).

### 4.2 `UserRepositoryTests.GetActiveUsersAsync_ShouldReturnOnlyActive` (test #2)
Attente corrigée de `Assert.Equal(2, …)` (qui supposait un admin seedé inexistant) vers `Assert.Single(result)` — seul l'utilisateur actif **créé explicitement** par le test est attendu. Commentaire trompeur « admin seed » retiré.

### 4.3 `ProductServiceTests` (tests #3, #4, #5)
Ajout d'un helper `SeedSupplierAsync(context)` créant **explicitement un `Supplier` valide** ; chaque test qui persiste un produit fixe désormais `product.SupplierId = supplierId` **avant** la création. La FK obligatoire est respectée, **sans** être affaiblie. Le test `CalculateMarginPercentage_ShouldCalculateCorrectly` (pur calcul, sans persistance) reste inchangé.

**Résultat après corrections :** `dotnet test MMV.sln -c Debug` → **193 ✅ / 0 ❌** (App.Tests 79/79 ; Domain.Tests 114/114).

---

## 5. Version du SDK verrouillée

[`global.json`](../../global.json) créé à la racine :

```json
{
  "sdk": {
    "version": "8.0.417",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

**Justification :**
- `version: 8.0.417` — **seul SDK .NET 8 réellement installé** sur le poste (vérifié par `dotnet --list-sdks`), cohérent avec le `TargetFramework net8.0` des 5 projets.
- `rollForward: latestFeature` — politique **prudente** : reste **dans la branche majeure .NET 8** (ne bascule **jamais** vers .NET 9/10) tout en tolérant un autre patch/feature-band 8.0.x sur un poste/CI différent ; si **aucun** SDK 8.0.x n'est présent, le build **échoue volontairement** (au lieu d'utiliser silencieusement .NET 10).
- `allowPrerelease: false` — exclut les SDK préliminaires.
- Aucune migration vers .NET 9/10 n'a été faite.

**Vérification :** depuis la racine, `dotnet --version` → **`8.0.417`** (avant : 10.0.103). Restore/build/test restent verts sous le SDK 8.

> **Observation de reproductibilité :** sous le SDK .NET 8, le build fait apparaître **1 avertissement préexistant** `CS1998` ([OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453)) que le SDK .NET 10 par défaut n'émettait pas (0 avertissement au baseline). C'est précisément le type de divergence inter-SDK que `global.json` corrige. Cet avertissement concerne la logique d'un ViewModel (**hors périmètre Étape 0**) ; il est laissé tel quel et reste **visible** (non bloquant) en CI. Voir §14.

---

## 6. Fichiers de CI ajoutés

Plateforme confirmée : **GitHub** (`origin = https://github.com/iamzekhnini15/mmv-desktop.git`). Aucune CI préexistante (`.github/`, `.gitlab-ci.yml`, etc. absents). → **GitHub Actions**.

Fichier créé : [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml). Job unique `build-test-scan` sur **`windows-latest`** (runner représentatif de la cible : `MMV.App` est `OutputType=WinExe` + `app.manifest`). Étapes :

1. **checkout** (`actions/checkout@v4`) ;
2. **installation du SDK** via `actions/setup-dotnet@v4` avec `global-json-file: global.json` (donc 8.0.417) ;
3. `dotnet --info` (diagnostic) ;
4. **restore** (`dotnet restore MMV.sln`) ;
5. **build** (`dotnet build … -c Debug`) **sans `-warnaserror`** → avertissements **visibles** mais non bloquants ;
6. **test** (`dotnet test … --no-build`, logger `trx`) ;
7. **analyse des packages vulnérables** : `dotnet list … --vulnerable --include-transitive`, avec **échec explicite** du pipeline si un avis GHSA est listé (la commande renvoie sinon toujours 0).

`permissions: contents: read` (moindre privilège). **Aucun déploiement, aucun artefact commercial, aucun secret de production.** Le pipeline échoue si le build ou les tests échouent (comportement par défaut des steps `run`).

> La CI a été **créée** cette session ; son **premier run** sur GitHub reste à confirmer vert (cf. §14).

---

## 7. Packages mis à jour

| Projet | Package | Avant | Après | Raison |
|---|---|---|---|---|
| Infrastructure | `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.0 | **8.0.27** | chaînes Caching.Memory + DependencyModel/STJ (R-24) |
| Infrastructure | `Microsoft.EntityFrameworkCore.Design` | 8.0.0 | **8.0.27** | idem |
| Infrastructure | `System.Text.Json` | *(transitif)* | **8.0.6 (pin direct)** | avis STJ (≥ 8.0.5) |
| App | `Microsoft.EntityFrameworkCore.Design` | 8.0.0 | **8.0.27** | idem |
| App | `Microsoft.Extensions.DependencyInjection` | 8.0.0 | **8.0.1** | cohérence cadre Extensions 8.0.x |
| App | `Microsoft.Extensions.Hosting` | 8.0.0 | **8.0.1** | chaîne STJ (Configuration.Json / Logging) |
| App | `System.Text.Json` | *(transitif)* | **8.0.6 (pin direct)** | avis STJ (≥ 8.0.5) |
| App | `Tmds.DBus.Protocol` | 0.20.0 *(transitif)* | **0.21.3 (pin direct)** | avis Tmds (≥ 0.21.2) |
| Domain.Tests | `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.0 | **8.0.27** | chaînes Caching.Memory + STJ |
| Domain.Tests | `xunit` | 2.6.6 | **2.9.3** | harmonisation |
| Domain.Tests | `xunit.runner.visualstudio` | 2.5.6 | **2.8.2** | harmonisation |
| Domain.Tests | `Microsoft.NET.Test.Sdk` | 17.9.0 | **17.12.0** | harmonisation |
| Domain.Tests | `Moq` | 4.20.70 | **4.20.72** | harmonisation |
| App.Tests | `xunit` | 2.5.3 | **2.9.3** | supprime la chaîne .NET Standard 1.x (System.Net.Http / RegularExpressions) |
| App.Tests | `xunit.runner.visualstudio` | 2.5.3 | **2.8.2** | idem + harmonisation |
| App.Tests | `Microsoft.NET.Test.Sdk` | 17.8.0 | **17.12.0** | harmonisation |

**Volontairement NON modifiés** (mise à niveau majeure hors périmètre, ou risque licence) :
- **Avalonia** (11.2.8) et **EF Core** **maintenus en 8.0.x** (pas de saut majeur ; R-24 traité sans 11.3+/9.x/10.x).
- **FluentAssertions maintenu en 6.12.0** : la v7+ a basculé en **licence commerciale** → ne pas l'introduire en Étape 0.
- `coverlet.collector` 6.0.0, `BCrypt.Net-Next` 4.0.3, `CommunityToolkit.Mvvm` 8.2.2, `FluentValidation` 11.9.0, `Microsoft.Extensions.Configuration.Abstractions` 8.0.0, `Projektanker.Icons.Avalonia` 9.6.2, `FluentAvaloniaUI` 2.1.0 — non vulnérables, laissés inchangés.

Principe respecté : pour chaque vulnérabilité, la **dépendance directe** introductrice a été identifiée et mise à jour ; les **pins directs** (System.Text.Json, Tmds.DBus.Protocol) sont des remontées de transitifs **comprises et documentées** (approche recommandée par Microsoft quand le parent ne tire pas encore une version corrigée), **non** des forçages arbitraires.

---

## 8. Chaîne de dépendance de chaque vulnérabilité (revérifiée par `dotnet nuget why`)

| Package vulnérable | Gravité / avis | Projets | Chaîne (dépendance directe → … → package) | Remédiation appliquée |
|---|---|---|---|---|
| `Microsoft.Extensions.Caching.Memory` 8.0.0 | High — GHSA-qj66-m88j-hmgj | Infra, App, Domain.Tests, App.Tests | **EF Core Sqlite/Design 8.0.0** → EF Core Relational 8.0.0 → EF Core 8.0.0 → Caching.Memory 8.0.0 | EF Core → **8.0.27** (tire Caching.Memory 8.0.x ≥ 8.0.1) |
| `System.Text.Json` 8.0.0 | High — GHSA-hh2w-p6rv-4g7w, GHSA-8g4q-xg66-9fp4 | tous | **EF Core (Design/Sqlite.Core)** → Microsoft.Extensions.DependencyModel 8.0.0 → STJ 8.0.0 ; (App aussi) **Microsoft.Extensions.Hosting 8.0.0** → Configuration.Json / Logging.Console / Logging.EventSource → STJ 8.0.0 | EF Core → 8.0.27, Hosting/DI → 8.0.1, **+ pin direct STJ 8.0.6** (≥ 8.0.5) en Infra et App |
| `Tmds.DBus.Protocol` 0.20.0 | High — GHSA-xrw6-gwf8-vvr9 | App, App.Tests | **Avalonia.Desktop 11.2.8** → Avalonia.X11 11.2.8 → Avalonia.FreeDesktop 11.2.8 → Tmds.DBus.Protocol 0.20.0 *(code Linux/D-Bus, jamais exécuté sur la cible Windows)* | **pin direct 0.21.3** (≥ 0.21.2) en App ; propagé à App.Tests via la référence projet |
| `System.Net.Http` 4.3.0 | High — GHSA-7jgj-8wvc-jh57 | App.Tests | **xunit 2.5.3** → xunit.assert/core → NETStandard.Library 1.6.1 → System.Net.Http 4.3.0 | xunit → **2.9.3** (≥ 2.6 ne tire plus la chaîne .NET Standard 1.x) |
| `System.Text.RegularExpressions` 4.3.0 | High — GHSA-cmhx-cq75-c4mj | App.Tests | **xunit 2.5.3** → NETStandard.Library 1.6.1 → System.Text.RegularExpressions 4.3.0 (et via System.Xml.ReaderWriter/XDocument) | xunit → **2.9.3** |

**Preuve directe que le bump xunit corrige les deux derniers :** `MMV.Domain.Tests` utilisait déjà **xunit 2.6.6** et **n'exhibait ni** `System.Net.Http` **ni** `System.Text.RegularExpressions` au baseline (seules les chaînes EF Core l'affectaient). Aligner `App.Tests` sur une version ≥ 2.6 supprime donc ces deux avis.

---

## 9. Vulnérabilités restantes et justification

**Aucune.** Après remédiation, `dotnet list MMV.sln package --vulnerable --include-transitive` retourne :

```
'MMV.Domain'        → aucun package vulnérable
'MMV.Infrastructure'→ aucun package vulnérable
'MMV.App'           → aucun package vulnérable
'MMV.Domain.Tests'  → aucun package vulnérable
'MMV.App.Tests'     → aucun package vulnérable
```

Les **5 vulnérabilités High** identifiées en Phase 1B sont **toutes corrigées**. **Aucune vulnérabilité High non traitée**, donc **aucune exception à documenter** (pas de migration majeure requise). Le critère « 0 vulnérabilité High non traitée » est satisfait, et le scan est intégré au CI (échec si réapparition).

---

## 10. Fichiers obsolètes supprimés

| Fichier supprimé | Preuve d'inutilisation |
|---|---|
| `src/MMV.App/Views/ProductDetailView.old.axaml.cs` | Contenu **intégralement commenté** (`/* … */`) → ne définit aucun type, ne compile en rien ; aucune référence (le seul `ProductDetailViewOld` était dans ce fichier). Vue réelle : [Views/Products/ProductDetailView.axaml](../../src/MMV.App/Views/Products/ProductDetailView.axaml). |
| `src/MMV.App/Views/CustomerFormView.axaml.old` | Extension `.axaml.old` → **hors glob de build** Avalonia (`**/*.axaml`). Vue réelle : [Views/Clients/CustomerFormView.axaml](../../src/MMV.App/Views/Clients/CustomerFormView.axaml). |
| `tests/MMV.App.Tests/Integration/CustomerRepositoryTests.cs.old` | Extension `.cs.old` → **hors glob de compilation** (`**/*.cs`). |
| `tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs.old` | Idem. |

Procédure suivie : recherche des références, vérification des globs/inclusions projet, contrôle ViewLocator/ressources XAML. Le build et les **193 tests** restent verts après suppression.

### 10.1 Vues dupliquées — **NON supprimées (différé, justifié)**

Doublons identifiés (audit §4.9) :
- `Views/UsersView.axaml(.cs)` **vs** `Views/Users/UsersView.axaml(.cs)`
- `Views/UserProfileView.axaml(.cs)` **vs** `Views/Users/UserProfileView.axaml(.cs)`

**Conservées** car : (1) la consigne interdit de supprimer une vue dupliquée tant que la vue réellement utilisée n'est pas **démontrée** (ViewLocator/navigation) ; (2) **`Views/UserProfileView.axaml` a été modifiée par le commit thème protégé `81ee386`** — y toucher risquerait d'altérer le travail préexistant. Cette démonstration et le dédoublonnage sortent du remit « risque minimal » de l'Étape 0 → **ticket séparé** (R-21, reliquat).

---

## 11. Commandes finales et résultats

Sous `global.json` (SDK **8.0.417**) :

| Commande | Résultat final |
|---|---|
| `dotnet restore MMV.sln` | **Succès** |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 erreur**, 1 avertissement préexistant `CS1998` (visible, hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **193 ✅ / 0 ❌** (App.Tests 79/79 ; Domain.Tests 114/114) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 package vulnérable** (5 projets) |
| `dotnet --version` (racine) | **8.0.417** |
| CI `.github/workflows/ci.yml` | YAML valide ; restore/build/test/scan ; échoue si build/tests KO ou avis GHSA détecté |

---

## 12. État Git après intervention

`git status --short` :

```
 M src/MMV.App/MMV.App.csproj
 D src/MMV.App/Views/CustomerFormView.axaml.old
 D src/MMV.App/Views/ProductDetailView.old.axaml.cs
 M src/MMV.Infrastructure/MMV.Infrastructure.csproj
 D tests/MMV.App.Tests/Integration/CustomerRepositoryTests.cs.old
 M tests/MMV.App.Tests/MMV.App.Tests.csproj
 D tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs.old
 M tests/MMV.Domain.Tests/Data/DbContextTests.cs
 M tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj
 M tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs
 M tests/MMV.Domain.Tests/ServiceTests/ProductServiceTests.cs
?? .github/
?? global.json
?? docs/ (PHASE0*.md, *.zip, architecture/, domain/, implementation/)   ← inchangé + ce rapport
```

`git diff --stat -- src tests` : **11 fichiers, +91 / −719** (les −719 viennent surtout des 4 fichiers `.old` supprimés). **Aucun fichier de thème `src/MMV.App/**` modifié** (le seul diff sous `src/MMV.App/` hors `.old` est `MMV.App.csproj`).

---

## 13. Liste exacte des fichiers modifiés / ajoutés / supprimés

**Modifiés (7) :**
- `src/MMV.App/MMV.App.csproj` (EF Design 8.0.27, DI/Hosting 8.0.1, pins STJ 8.0.6 + Tmds 0.21.3)
- `src/MMV.Infrastructure/MMV.Infrastructure.csproj` (EF Sqlite/Design 8.0.27, pin STJ 8.0.6)
- `tests/MMV.App.Tests/MMV.App.Tests.csproj` (xunit 2.9.3 / runner 2.8.2 / Test.Sdk 17.12.0)
- `tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj` (EF Sqlite 8.0.27, xunit 2.9.3 / runner 2.8.2 / Test.Sdk 17.12.0 / Moq 4.20.72)
- `tests/MMV.Domain.Tests/Data/DbContextTests.cs` (scission schéma / DbInitializer)
- `tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs` (attente 2→1)
- `tests/MMV.Domain.Tests/ServiceTests/ProductServiceTests.cs` (helper Supplier + `SupplierId`)

**Ajoutés (3) :**
- `global.json`
- `.github/workflows/ci.yml`
- `docs/implementation/phase-2a-step0-report.md` (ce rapport)

**Supprimés (4) :**
- `src/MMV.App/Views/ProductDetailView.old.axaml.cs`
- `src/MMV.App/Views/CustomerFormView.axaml.old`
- `tests/MMV.App.Tests/Integration/CustomerRepositoryTests.cs.old`
- `tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs.old`

> Note : `docs/architecture/`, `docs/domain/`, `docs/PHASE0*.md`, `docs/*.zip` restent **non suivis** et **inchangés** (artefacts préexistants des phases 0.5/1/1B, hors périmètre).

---

## 14. Risques résiduels

1. **Pin `Tmds.DBus.Protocol` 0.21.3** surchargeant un transitif d'`Avalonia.FreeDesktop 11.2.8` (compilé contre 0.20.0). Sur la **cible Windows**, le chemin X11/D-Bus n'est **jamais** exécuté → risque résiduel quasi nul ; il ne concernerait qu'un hypothétique build/exécution **Linux** (hors périmètre produit). À réévaluer lors d'une future montée Avalonia (ticket séparé).
2. **Avertissement `CS1998` préexistant** ([OrderFormViewModel.cs:453](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L453)) — logique de ViewModel **hors périmètre Étape 0**, surfacé par le SDK .NET 8. Laissé tel quel (visible, non bloquant) ; ticket de nettoyage dédié recommandé.
3. **Vues dupliquées** (`Users`/`UserProfile`) non dédoublonnées (cf. §10.1) — différé (R-21) ; une des vues racine appartient au commit thème protégé.
4. **Packages encore « outdated » mais non vulnérables** (Avalonia 11.2.8, SkiaSharp 2.88.9, EF/Extensions 8.0.x vs 10.x, `Newtonsoft.Json` transitif de test, etc.) — mises à niveau **majeures hors périmètre Étape 0**.
5. **`global.json` `rollForward: latestFeature`** — reste sur .NET 8 ; sur un poste sans aucun SDK 8.0.x, le build **échoue volontairement** (comportement prudent à connaître ; la CI installe 8.0.417 via `setup-dotnet`).
6. **Premier run CI non encore observé** — le workflow a été créé cette session ; son exécution verte sur le runner GitHub reste à confirmer (dépend de l'accès `actions/setup-dotnet` au SDK 8.0.417 et à `api.nuget.org`).
7. **Écart Phase 1B** (thème commité dans `81ee386`) — documenté (§1.1) ; aucun comportement ni donnée modifiés, travail de thème préservé.

Aucun de ces risques ne touche au métier, au réglementaire, ni aux interdictions de l'Étape 0.

---

## 15. Verdict — GO / NO-GO pour commencer l'Étape 1

### **GO** pour démarrer l'Étape 1 (socle de persistance fiable), sous réserve de commiter les changements ci-dessus et de confirmer le premier run CI vert.

Critères d'acceptation de l'Étape 0 — **tous satisfaits** :

| Critère | État |
|---|---|
| Tous les tests passent | ✅ 193/193 |
| Le build réussit | ✅ 0 erreur (1 avert. préexistant visible) |
| SDK verrouillé sur une version .NET 8 | ✅ `global.json` → 8.0.417 (`rollForward: latestFeature`) |
| CI : restore + build + test + scan vulnérabilités | ✅ `.github/workflows/ci.yml` |
| Chaque vulnérabilité High corrigée **ou** documentée | ✅ 5/5 corrigées, 0 restante |
| Aucune modification métier ou réglementaire | ✅ aucune valeur réglementaire ni entité métier touchée |
| Aucune étape 1+ commencée | ✅ `EnsureCreated`, migrations, chemin DB, Money, Organisation, couche Application, numérotation, concurrency token, i18n/RTL… **non touchés** |
| Modifications préexistantes du thème préservées | ✅ commit `81ee386` intact, non reformaté |
| Rapport d'implémentation complet | ✅ présent document |

**Arrêt à la fin de l'Étape 0.** L'Étape 1 (ADR-003 : suppression d'`EnsureCreated`, chemin DB unique, déclencheur de migration, concurrency token ADR-010, transactions) **n'est pas commencée**, conformément aux consignes.
