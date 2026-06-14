# P2B-2B — Création contrôlée de MMV.Application + DI unifiée — RAPPORT

> **Programme 2 — Corriger l'architecture applicative.** Phase **P2B-2B**. Branche `p2b-architecture`.
> Date : 13 juin 2026. **Mode : IMPLEMENT.** `ALLOW_COMMIT=false`, `ALLOW_PUSH=false`.
> **Objectif strict** : créer la couche `MMV.Application` de manière **minimale, propre et contrôlée**
> (projet + arborescence + `AddApplication` + branchement `MMV.App → MMV.Application`), **sans** migrer
> de logique métier. **Aucun use case réel, aucun `SaleFormViewModel` migré, aucune migration, aucune
> règle métier modifiée.**

---

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | `P2B-2B` |
| `EXECUTION_MODE` | `IMPLEMENT` |
| `ALLOW_COMMIT` | `false` |
| `ALLOW_PUSH` | `false` |

---

## 2. Prérequis

Phases précédentes **validées (GO définitif)** : P2A, P2B-2A, P2B-2A-CI, P2B-2A-CI-R2.

| Élément | Valeur |
|---|---|
| Branche | `p2b-architecture` |
| Dernier run CI connu | **#21** — id `27447845006` |
| Commit testé | `32bed23` |
| Statut CI | ✅ completed / success (VERT) |
| Pipeline CI | restore → build → test → audit NuGet → restore .NET tools → `has-pending-model-changes` |

Documents de cadrage lus avant modification (le dépôt prime) :
[adr-application-boundaries](../architecture/adr-application-boundaries.md),
[application-layer-migration-plan](../architecture/application-layer-migration-plan.md),
[P2B-2A-report](P2B-2A-report.md), [P2B-2A-CI-report](P2B-2A-CI-report.md),
[P2B-2A-CI-R2-report](P2B-2A-CI-R2-report.md), ADR P2A (transaction-idempotency, stock-concurrency,
numbering, environments-seeding).

---

## 3. État Git initial

```
git status --short        → (propre)
git branch --show-current → p2b-architecture
git log -5 --oneline      → 0fca9ae docs(P2B-2A): record EF CI validation
                            32bed23 ci(P2B-2A): check EF pending model changes
                            0815bcf docs(P2B-2A): record CI trigger validation
                            3042e52 ci(P2B-2A): run workflow on phase branches
                            ea382c5 docs(P2B-2A): record remote CI status (validation distante requise)
```

---

## 4. Baseline (avant toute modification)

| Commande | Résultat |
|---|---|
| `dotnet --version` | **8.0.417** (`global.json` actif) |
| `dotnet restore MMV.sln` | ✅ tous les projets à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant, [OrderFormViewModel.cs:458](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458)) |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (5 projets) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes …` | ✅ **false** |

Dépôt propre, suite verte, 0 vulnérabilité, `has-pending=false` ⇒ conditions de démarrage réunies.

---

## 5. Fichiers créés / modifiés

### 5.1 Créés (couche Application — 5 fichiers)

| Fichier | Rôle |
|---|---|
| [`src/MMV.Application/MMV.Application.csproj`](../../src/MMV.Application/MMV.Application.csproj) | projet `classlib net8.0` ; réf. projet **→ `MMV.Domain` seul** ; package `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (neutre) |
| [`src/MMV.Application/DependencyInjection.cs`](../../src/MMV.Application/DependencyInjection.cs) | `AddApplication(this IServiceCollection)` — **squelette neutre** (aucun use case réel) |
| [`src/MMV.Application/Abstractions/IApplicationMarker.cs`](../../src/MMV.Application/Abstractions/IApplicationMarker.cs) | marqueur d'assembly neutre (ancre de type ; aucune sémantique métier) |
| [`src/MMV.Application/UseCases/README.md`](../../src/MMV.Application/UseCases/README.md) | dossier des use cases (vide en P2B-2B) |
| [`src/MMV.Application/Common/README.md`](../../src/MMV.Application/Common/README.md) | types transverses (vide en P2B-2B) |

### 5.2 Créés (documentation — 2 fichiers)

| Fichier | Rôle |
|---|---|
| [`docs/architecture/application-layer-structure.md`](../architecture/application-layer-structure.md) | doc courte : structure, invariants, DI, note de nommage |
| [`docs/implementation/P2B-2B-report.md`](P2B-2B-report.md) | ce rapport |

### 5.3 Modifiés (4 fichiers, périmètre autorisé + 1 forcé par compilation)

| Fichier | Changement | Justification |
|---|---|---|
| [`MMV.sln`](../../MMV.sln) | ajout du projet `MMV.Application` (GUID `…0005`, dossier solution `src`, configs Debug/Release) | item 2 |
| [`src/MMV.App/MMV.App.csproj`](../../src/MMV.App/MMV.App.csproj) | `ProjectReference` → `MMV.Application` | item 3 (composition root) |
| [`src/MMV.App/App.axaml.cs`](../../src/MMV.App/App.axaml.cs) | `using MMV.Application;` + `services.AddApplication();` + base `: Avalonia.Application` qualifiée | items 3/5 + collision de nommage |
| [`src/MMV.App/Services/ThemeService.cs`](../../src/MMV.App/Services/ThemeService.cs) | `Application.Current` → `Avalonia.Application.Current` (2 occurrences) | **forcé par la compilation** (cf. §14.2), neutre |

> **ThemeService.cs hors du périmètre nominal** mais modifié au titre d'une **nécessité strictement liée
> à la compilation** : le nouveau namespace `MMV.Application` masque `Avalonia.Application` par résolution
> de nom simple. Changement **2 jetons**, **sans logique**, **comportement identique** (cf. §14.2).

---

## 6. Structure de `MMV.Application`

```
src/MMV.Application/
  MMV.Application.csproj
  DependencyInjection.cs          # AddApplication(this IServiceCollection) — squelette neutre (P2B-2B)
  Abstractions/
    IApplicationMarker.cs         # marqueur d'assembly neutre (ancre de type)
  UseCases/                       # (1 dossier par use case en P2B-2C+) — vide en P2B-2B
    README.md
  Common/                         # types transverses (Result, gardes) — vide en P2B-2B
    README.md
```

Les dossiers `UseCases/` et `Common/`, vides de code en P2B-2B, sont matérialisés par un `README.md` court
(Git ne versionne pas les dossiers vides). `Abstractions/` contient le marqueur neutre. Détails :
[application-layer-structure.md](../architecture/application-layer-structure.md).

`AddApplication` (extrait) :

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    // Aucun enregistrement métier en P2B-2B (couche Application créée à vide).
    // Les use cases seront ajoutés ici en P2B-2C, sans modifier cette signature.
    return services;
}
```

---

## 7. Références projet confirmées

```
MMV.Application → MMV.Domain                                  (UNIQUE réf. projet)   ✅
MMV.App         → MMV.Domain, MMV.Application, MMV.Infrastructure                    ✅
MMV.Infrastructure → MMV.Domain                              (inchangé)              ✅
MMV.Domain      → (aucune réf. projet)                       (pur, préservé)         ✅
```

Vérifié par `dotnet list <proj> reference` :

| Projet | Références projet |
|---|---|
| `MMV.Application` | `..\MMV.Domain\MMV.Domain.csproj` **(seule)** |
| `MMV.App` | `MMV.Domain`, **`MMV.Application`**, `MMV.Infrastructure` |

**Interdits respectés** dans `MMV.Application.csproj` : aucun `MMV.Infrastructure`, `MMV.App`,
`Microsoft.EntityFrameworkCore*`, `Microsoft.Data.Sqlite`, `Avalonia*`, `CommunityToolkit.Mvvm`.
Seul package : `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (neutre, nécessaire pour
exposer l'extension `AddApplication`, version alignée sur le `Microsoft.Extensions.DependencyInjection`
8.0.1 déjà tiré par `MMV.App`).

---

## 8. Décision DI

**Décision : nettoyage du résidu `AddInfrastructure` DIFFÉRÉ (documenté) ; unification additive en P2B-2B.**

### 8.1 Constat (vérifié dans le dépôt)

- Le composition root **réel et unique** est [`App.ConfigureServices()`](../../src/MMV.App/App.axaml.cs#L108).
  Il enregistre `DbContext`, repos, `IUnitOfWork`, les 3 primitives P2A, `IAuthenticationService`, les
  services UI et les ViewModels — **mais PAS** les services de domaine.
- [`Infrastructure.DependencyInjection.AddInfrastructure()`](../../src/MMV.Infrastructure/DependencyInjection.cs#L19)
  enregistre ces services de domaine (`CustomerService`, `ProductService`, `PrescriptionService`,
  `OrderService`, `SaleService`) **mais n'est JAMAIS appelé** : `Grep "AddInfrastructure"` sur tout le
  dépôt ne renvoie que **sa définition** (ligne 19) + de la documentation — **0 appel** dans `src/` ou
  `tests/`. Résidu **mort au runtime** confirmé.

### 8.2 Décision retenue et justification

En P2B-2B, l'unification se fait de manière **additive et non comportementale** : le root unique
`App.ConfigureServices` compose désormais aussi la couche Application via `services.AddApplication()`
(squelette neutre). Le **nettoyage** du résidu `AddInfrastructure` (suppression ou rebranchement) est
**différé à P2B-2C**, pour les raisons suivantes :

1. **Rebrancher `AddInfrastructure` serait comportemental** — il enregistrerait les services de domaine
   (`SaleService` & co.) actuellement **absents** du conteneur vivant, modifiant le contenu de la DI. Or la
   phase impose « **éviter tout changement comportemental** » et « **ne pas commencer une refonte DI
   massive** ».
2. **Risque de divergence de résolution `DbContext`** — `App.ConfigureServices` résout le chemin via
   `SqliteDatabasePathResolver.ResolveDatabasePath()` et **réutilise** la variable locale `databasePath`
   pour le journal de migration et la reprise de l'ancien fichier ; `AddInfrastructure.ResolveConnectionString`
   suit un chemin différent (avec `IConfiguration` + `EnsureDirectoryExists`). Fusionner les deux
   risquerait une double inscription du `DbContext` et une divergence de source unique.
3. **Suppression hors périmètre** — retirer `AddInfrastructure` toucherait `MMV.Infrastructure` (hors du
   périmètre autorisé) et supprimerait l'API que l'ADR earmarke comme **futur module unifié**.

Le bon moment pour assainir ce résidu est **P2B-2C**, lorsque le use case `EnregistrerVente` consommera
réellement ces enregistrements (les services de domaine pertinents seront alors câblés dans le root unique,
et `AddInfrastructure` supprimé ou rebranché sans dette). Cette décision est conforme à l'option « documenter
pourquoi le nettoyage complet est différé » du cahier de phase et à l'anti-pattern #15
([plan de migration §7](../architecture/application-layer-migration-plan.md)).

> **Aucune divergence introduite** : `AddInfrastructure` reste inchangé et toujours mort ; le seul module
> vivant demeure `App.ConfigureServices`, désormais étendu de `AddApplication()`.

---

## 9. Tests ajoutés ou non

**Aucun test ajouté en P2B-2B.** Le test d'architecture (vérifier par réflexion que `MMV.Application` ne
référence ni `MMV.Infrastructure`, ni `MMV.App`, ni Avalonia, ni EF Core) est un **contrôle différé**,
pour les raisons suivantes :

- **Garantie structurelle déjà en place** : l'invariant est assuré au niveau `.csproj`
  (`MMV.Application.csproj` n'a qu'**une** `ProjectReference` vers `MMV.Domain` et **zéro** package interdit)
  et vérifié à chaque build + `dotnet list package`. Une référence interdite **ne compilerait pas**.
- **Coût hors périmètre** : un tel test exige soit d'ajouter une `ProjectReference` `MMV.Application` à un
  projet de tests existant (`MMV.Domain.Tests` / `MMV.App.Tests`, **hors périmètre**, et sémantiquement
  inadapté), soit de créer un nouveau projet `MMV.Application.Tests` (**hors périmètre**, « trop de
  changements » au sens du cahier). La phase autorise alors de « documenter comme contrôle différé ».
- **Échéance naturelle** : ce test sera ajouté en **P2B-2C**, lorsque le projet de tests de la couche
  Application sera introduit pour couvrir le premier use case (vrai SQLite + chemins d'erreur), conformément
  à la [stratégie de tests](../architecture/application-layer-migration-plan.md) (§5, garde-fou « optionnel,
  non bloquant P2B »).

La suite existante (**320**) reste **inchangée et verte**.

---

## 10. Migrations créées ou non

**Aucune migration créée.** Aucune entité, configuration EF ou `DbContext` modifiés. `MMV.Application`
ne contient **aucun** type EF. Les **9** migrations existantes sont intactes.
`has-pending-model-changes` reste **false**.

---

## 11. Contrôles exécutés

**Avant modification** : `git status/branch/log`, `dotnet --version`, `restore`, `build -c Debug`,
`test --no-build`, `list package --vulnerable`, `tool restore`, `ef … has-pending-model-changes` (cf. §4).

**Après modification** :

| Commande | Résultat |
|---|---|
| `git status --short` | ` M MMV.sln`, ` M src/MMV.App/App.axaml.cs`, ` M src/MMV.App/MMV.App.csproj`, ` M src/MMV.App/Services/ThemeService.cs`, `?? src/MMV.Application/` |
| `git diff --stat` | 4 fichiers suivis, **+20 / −3** (hors `src/MMV.Application/` non suivi) |
| `git diff --check` | ✅ aucune anomalie (espaces / marqueurs de conflit) |
| `dotnet restore MMV.sln` | ✅ `MMV.Application` restauré, autres à jour |
| `dotnet build MMV.sln --no-restore -c Debug` | ✅ Succès — **1 avert.** (`CS1998` préexistant), 0 erreur |
| `dotnet test MMV.sln --no-build -c Debug` | ✅ **320** (Domain **223** + App **97**), 0 échec, 0 ignoré |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | ✅ **0 vulnérabilité** (**6 projets**, `MMV.Application` inclus) |
| `dotnet tool restore` | ✅ `dotnet-ef` 8.0.27 |
| `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` | ✅ **false** |

---

## 12. Résultats

| Critère | Valeur observée | Statut |
|---|---|---|
| Build | Succès, 1 avert. `CS1998` préexistant, 0 erreur | ✅ |
| Tests | **320** (223 + 97), 0 échec | ✅ |
| Audit NuGet | **0 vulnérabilité** (6 projets) | ✅ |
| `has-pending-model-changes` | **false** | ✅ |
| `MMV.Application` → `MMV.Domain` seul | confirmé (`dotnet list reference`) | ✅ |
| `MMV.App` → `MMV.Application` | confirmé | ✅ |
| `AddApplication` existe + appelé par `App.axaml.cs` | confirmé | ✅ |
| Aucun use case métier réel | confirmé (squelette neutre) | ✅ |
| `SaleFormViewModel` non migré | confirmé (intact) | ✅ |
| Aucune migration | confirmé | ✅ |

---

## 13. Vulnérabilités

**Aucune** (High/Critical ou autre) sur les **6** projets, transitifs inclus. Le seul package ajouté
(`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1) est neutre et non vulnérable. Pins de
sécurité intacts (`System.Text.Json` 8.0.6, `Tmds.DBus.Protocol` 0.21.3). Pin `FluentAssertions` maintenu
en 6.x. Aucune dépendance EF/Avalonia ajoutée à `MMV.Application`.

---

## 14. Warnings résiduels

### 14.1 `CS1998` (préexistant, hors périmètre)

| Warning | Emplacement | Statut |
|---|---|---|
| `CS1998` (async sans `await`) | [`OrderFormViewModel.cs:458`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs#L458) | **Préexistant** (baseline), non modifié, hors périmètre. Candidate d'hygiène future. |

Aucun **nouveau** warning introduit par P2B-2B.

### 14.2 Note technique — collision de nommage `Application` (résolue, neutre)

Le projet `MMV.Application` introduit le namespace `MMV.Application`. Étant un **membre du namespace parent
`MMV`**, il **masque par résolution de nom simple** le type `Avalonia.Application` partout dans l'assembly
`MMV.App` (qui vit sous `MMV.App` / `MMV.App.*`). Conséquence à la compilation : `Application` (base de
`App`, et `Application.Current` dans `ThemeService`) se liait au **namespace** au lieu du **type**.

**Résolution** (neutre, 2 points uniquement) : qualification explicite `Avalonia.Application` —
(1) base de classe dans [`App.axaml.cs`](../../src/MMV.App/App.axaml.cs#L27) ;
(2) `Avalonia.Application.Current` dans [`ThemeService.cs`](../../src/MMV.App/Services/ThemeService.cs#L26).
`Application` y désignait **déjà** `Avalonia.Application` : **comportement identique**, aucune logique
touchée. Un alias global (`<Using Alias>`) a été **évalué puis écarté** : un alias **ne l'emporte pas** sur
un membre de namespace d'un scope englobant (vérifié empiriquement), il ne corrigeait donc pas `ThemeService`.

---

## 15. Risques résiduels

| # | Risque | Évaluation / mitigation |
|---|---|---|
| 1 | **R-05 non clos** | Inchangé : la fermeture est l'objet de P2B-2C (use case `EnregistrerVente`). P2B-2B ne fait que créer le réceptacle. |
| 2 | **Double DI (V1/V2)** — `AddInfrastructure` mort | **Différé documenté** (§8). Aucune divergence introduite : `AddInfrastructure` inchangé et toujours non appelé ; seul `App.ConfigureServices` est vivant. Assainissement en P2B-2C. |
| 3 | **Contrôle d'architecture par test** | **Différé** (§9). Invariant garanti structurellement par les `.csproj` (build + `dotnet list package`). Test ajouté en P2B-2C avec le projet de tests Application. |
| 4 | **Collision de nommage `Application`** | **Résolue** (§14.2), neutre. Documentée dans [application-layer-structure.md §5](../architecture/application-layer-structure.md). |
| 5 | **R-23** (`UnitOfWork.RollbackAsync` trompeur) | Inchangé, contourné ; assainissement après migration des flux. |
| 6 | **VO morts / entités anémiques (V7)** | Différés (modèle monétaire Étape 3, enrichissement par lots). |

Aucun de ces risques ne touche au réglementaire. **Aucune valeur de gate (TVA, devise, barème, magasin,
pays) n'a été introduite.** Périmètre fiscalité / facture / devis / Belgique / Maroc / organisation /
magasin / `Money` / SaaS **non touché**. Flux stock / vente / commande / client / ordonnance **non touchés**.

---

## 16. État Git final

> Le travail P2B-2B a d'abord été produit **sans commit** (`ALLOW_COMMIT=false`). Sur **autorisation
> explicite ultérieure** (`ALLOW_COMMIT=true`, `ALLOW_PUSH=true`), le changeset **strictement limité au
> périmètre P2B-2B** a été committé puis poussé (sans force-push).

```
git add MMV.sln src/MMV.Application src/MMV.App/MMV.App.csproj src/MMV.App/App.axaml.cs \
        src/MMV.App/Services/ThemeService.cs \
        docs/architecture/application-layer-structure.md docs/implementation/P2B-2B-report.md
git commit -m "feat(P2B-2B): add application layer shell"
  → 7c3324c  11 files changed, 572 insertions(+), 3 deletions(-)
git push origin p2b-architecture
  → 0fca9ae..7c3324c  p2b-architecture -> p2b-architecture (sans force)
```

**Commit `7c3324c`** — 11 fichiers exactement (les 5 fichiers `src/MMV.Application/`, `MMV.sln`,
3 fichiers `src/MMV.App/` [`MMV.App.csproj`, `App.axaml.cs`, `Services/ThemeService.cs`], les 2 docs).
`git diff --check` : **0 anomalie**. Working tree **propre** après commit. **Aucun** `bin/`/`obj/`,
`*.db`/`*.trx`/`*.zip`, secret ou temporaire. **Aucun** `SaleFormViewModel`, `OrderFormViewModel`,
`StockMovementFormViewModel`, repository, `DbContext`, migration, entité Domain, règle métier ou test
métier touché. **Aucun** `RegisterSaleUseCase`/`EnregistrerVente`/`RegisterSaleCommand`/`RegisterSaleResult`.

---

## 16 bis. Validation CI distante (run réel)

Le push du commit `7c3324c` a **déclenché** le pipeline GitHub Actions via le motif `p2*`.

| Élément | Valeur réelle |
|---|---|
| **Run** | **#23** — id **`27449157508`** |
| **Lien** | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27449157508 |
| **Commit testé** | **`7c3324c`** (`head_sha = 7c3324c46d3b7875e0dc0656eec8e81d44a2a9ee`) |
| **Branche testée** | **`p2b-architecture`** (déclenchée par le motif `p2*`) |
| Événement / workflow | `push` / `CI` — `Restore / Build / Test / Scan` |
| Runner | `windows-latest` (GitHub-hosted) |
| Durée | ≈ 2 min 21 s (23:34:26 → 23:36:47 UTC) |
| Setup .NET (SDK `global.json`) | ✅ success |
| **Restore** (step #5) | ✅ **success** |
| **Build** (step #6) | ✅ **success** |
| **Test** (step #7) | ✅ **success** (suite **320** confirmée localement) |
| **Audit NuGet** (step #8, High/Critical, JSON + sévérité) | ✅ **success** — **0 vulnérabilité** |
| **Restore .NET tools** (step #9) | ✅ **success** (`dotnet-ef` 8.0.27 restauré) |
| **Check EF Core pending model changes** (step #10) | ✅ **success** — `has-pending-model-changes` = **false** |
| **Statut final du workflow** | ✅ **completed / success (VERT)** |

Tous les steps en conclusion `success` : *Set up job, Checkout, Setup .NET, Diagnostic SDK, Restore, Build,
Test, Audit des packages vulnérables, Restore .NET tools, Check EF Core pending model changes, Post-steps,
Complete job*. **VALIDATION DISTANTE OBTENUE** : la gate roadmap « pipeline distant vert » est **levée**
pour P2B-2B.

---

## 17. Verdict — GO / NO-GO

| Critère d'acceptation P2B-2B | Statut |
|---|---|
| `MMV.Application` existe | ✅ |
| `MMV.Application` référence **uniquement** `MMV.Domain` | ✅ |
| `MMV.App` référence `MMV.Application` | ✅ |
| `AddApplication` existe | ✅ |
| `App.axaml.cs` appelle `AddApplication` | ✅ |
| Aucun use case métier réel implémenté | ✅ (squelette neutre) |
| `SaleFormViewModel` non migré | ✅ |
| Aucune migration créée | ✅ |
| `has-pending-model-changes = false` | ✅ |
| Build vert | ✅ (1 avert. `CS1998` préexistant) |
| Tests verts (**320**) | ✅ |
| Audit NuGet = 0 vulnérabilité | ✅ |
| Rapport complet (18 sections) | ✅ |
| Aucune règle métier modifiée | ✅ |
| Commit & push (sur autorisation explicite) | ✅ (`7c3324c`, branche `p2b-architecture`, sans force) |
| CI distante verte (restore/build/test/audit/tools/EF) | ✅ run **#23** (`27449157508`, commit `7c3324c`) |

### ✅ **P2B-2B = GO DÉFINITIF**

La couche `MMV.Application` est créée **minimale, propre et contrôlée** : projet `net8.0` → `MMV.Domain`
seul, arborescence (`UseCases/`, `Common/`, `Abstractions/`), `AddApplication` (squelette neutre) appelée
par le composition root, `MMV.App → MMV.Application` branché. **Aucun** use case réel, **aucune** migration
de `SaleFormViewModel`, **aucune** migration EF, **aucune** règle métier modifiée. DI : unification
additive ; nettoyage `AddInfrastructure` **différé documenté** (non comportemental). Build/tests/audit/EF
**verts** en local **et en CI distante** (run #23 `27449157508`, commit `7c3324c`, branche
`p2b-architecture`) ⇒ la gate « pipeline distant vert » est **levée** (cf. §16 bis).

---

## 18. Prochaine étape candidate : **P2B-2C**

**P2B-2C — premier vertical slice `EnregistrerVente`** (Programme 2) : extraire le use case
`RegisterSaleUseCase` de [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L909-L1057)
dans `MMV.Application/UseCases/Sales/RegisterSale/` (réutilisant les **mêmes** primitives P2A :
`ITransactionRunner`, `INumberSequenceService`, `IStockMutationService`), l'enregistrer dans `AddApplication`
(portée `Scoped`), faire **déléguer** `SaleFormViewModel`, retirer l'accès repo de ce flux, ajouter le
**test d'architecture** différé et les tests de use case (vrai SQLite). **Sans** changement de comportement
observable.

> **Ne pas démarrer** sans revue humaine du présent rapport et `TARGET_PHASE_ID = P2B-2C` fourni
> explicitement. **Claude ne lance jamais seul l'étape suivante.**

---

## Arrêt obligatoire

Fin de `P2B-2B`. **Aucun commit, aucun push, aucun use case réel, aucun `SaleFormViewModel` migré, aucune
migration. P2B-2C non commencé.**
