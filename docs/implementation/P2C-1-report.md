# Rapport P2C-1 — Branche P2C + tests garde-fous UI sans persistance

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-1 |
| `EXECUTION_MODE` | CREATE_BRANCH_AND_IMPLEMENT_GUARDRAILS |
| `SOURCE_BRANCH` | main |
| `TARGET_BRANCH` | p2c-ui-cleanup |
| `ALLOW_BRANCH_CREATE` | true |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. Branche créée

`p2c-ui-cleanup`, créée depuis `main` (`git checkout -b p2c-ui-cleanup`).

Aucun commit, aucun push (conformément à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 3. État Git initial

`main` propre et à jour avant création de la branche :

```
git status --short      # (vide)
git fetch origin        # ok
git checkout main       # Already on 'main'
git pull --ff-only      # Already up to date.
```

Derniers commits de `main` (conformes au contexte attendu) :

```
6300250 docs(P2C-0): update merge report with CI distante validation
1f8e05c docs(P2C-0): record P2B merge validation
9a40772 merge(P2C-0): integrate P2B architecture into main
f273c14 docs(P2B-2K): record exit audit CI validation
f29ab88 docs(P2B-2K): record P2B exit audit and P2C roadmap
```

Après `git checkout -b p2c-ui-cleanup` :

```
git branch --show-current   # p2c-ui-cleanup
git status --short          # (vide)
```

## 4. Baseline

Exécutée sur `p2c-ui-cleanup` **avant** toute modification :

| Contrôle | Résultat |
|---|---|
| `dotnet restore MMV.sln` | OK (projets à jour) |
| `dotnet build MMV.sln --no-restore -c Debug` | **Vert** (0 erreur, 1 warning préexistant CS1998 dans `OrderFormViewModel.cs`) |
| `dotnet test MMV.sln --no-build -c Debug` | **398 tests verts** (App 126 + Application 49 + Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** |
| `dotnet tool restore` | OK (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | **false** (No changes…) |
| `dotnet list src/MMV.Application reference` | **uniquement `MMV.Domain`** |
| `dotnet list src/MMV.Application package` | **uniquement `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1** |

Baseline GO.

## 5. Stratégie de garde-fous

L'objectif P2C est : **UI = affichage + état écran + envoi de données** ; **Application = orchestration métier + persistance**.

Comme la couche UI (`MMV.App`) dépend aujourd'hui encore largement des `*Repository` du Domain et d'`IUnitOfWork`, les tests **ne peuvent pas** encore imposer « zéro violation ». Ils fonctionnent donc en **baseline contrôlée** :

- les violations existantes sont listées explicitement dans des **allowlists** ;
- toute violation **détectée mais absente** de l'allowlist fait **échouer** le test (empêche toute nouvelle violation) ;
- toute entrée d'allowlist **qui ne correspond plus** à une violation réelle fait **aussi échouer** le test (« the allowlist must shrink, not lie ») — l'allowlist ne peut donc que **diminuer** au fil des phases P2C ;
- la détection est faite par **réflexion** (constructeurs / propriétés de l'assembly `MMV.App`) et par **scan de fichiers** (`*.axaml.cs`), donc indépendante d'une recherche textuelle approximative.

Cette double garde (nouvelles violations **et** entrées périmées) a déjà servi pendant l'implémentation : elle a révélé des dépendances non repérées au grep (`MainWindowViewModel`, `CustomerInfoViewModel`, `SaleFormViewModel`, `OrderKanbanViewModel -> IUnitOfWork`, `ProductFormViewModel -> INotificationRepository`) et corrigé une entrée erronée que j'avais ajoutée à tort.

## 6. Allowlists créées

Fichier : `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`.

| Allowlist | Entrées | Objectif |
|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | **51** | VM ⇒ `*Repository` (paramètre de constructeur) |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | **21** | VM ⇒ `IUnitOfWork` (paramètre de constructeur) |
| `AllowedPublicPersistenceProperties` | **2** | propriété publique de VM exposant un port de persistance |
| `AllowedCodeBehindPersistenceFiles` | **4** | code-behind `*.axaml.cs` réalisant de la persistance |
| **Total** | **78** | baseline P2C-1 à réduire phase par phase |

Format : chaînes lisibles et triées, chemins normalisés avec `/` (stable Windows/CI). Chaque allowlist porte un `// TODO P2C` rappelant qu'elle doit décroître.

## 7. Tests créés

Classe `AppUiPersistenceGuardrailTests` (5 `[Fact]`) :

1. `ViewModels_DoNotDependOnRepositories_OutsideAllowlist` — paramètres de constructeur de type `*Repository` (nom se terminant par `Repository` ou type du namespace `MMV.Domain.Interfaces.Repositories`).
2. `ViewModels_DoNotDependOnUnitOfWork_OutsideAllowlist` — paramètres de constructeur de type `IUnitOfWork`.
3. `ViewModels_DoNotExposePersistenceProperties_OutsideAllowlist` — propriétés publiques exposant `IUnitOfWork` / `I…Repository` (capture `CustomersListViewModel.Repository` et `.UnitOfWork`).
4. `CodeBehind_DoesNotPerformPersistence_OutsideAllowlist` — scan `src/MMV.App/**/*.axaml.cs` pour les jetons `SaveChangesAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `IUnitOfWork`, `Repository`, `DbContext`.
5. `App_References_Application` — contrôle positif : `MMV.App` référence bien `MMV.Application` (sans affaiblir la pureté de `MMV.Application`, verrouillée côté `MMV.Application.Tests`).

Message d'échec explicite et copiable, du type :

```
New UI persistence dependency detected (ViewModel constructor depends on a repository):
    "CustomerFormViewModel -> ICustomerRepository",
Either migrate this dependency to an Application use case, or add it explicitly to the allowlist with justification.
```

Les tests existants `ApplicationArchitectureTests` (pureté de `MMV.Application`) sont **conservés intacts**.

## 8. Violations actuelles connues (baseline P2C-1)

**Code-behind avec persistance (4)** :

- `src/MMV.App/App.axaml.cs` *(composition root / DI — autorisé temporairement)*
- `src/MMV.App/Views/Clients/CustomerFormView.axaml.cs`
- `src/MMV.App/Views/Clients/CustomersView.axaml.cs`
- `src/MMV.App/Views/MainWindow.axaml.cs`

**Propriétés publiques exposant la persistance (2)** :

- `CustomersListViewModel.Repository`
- `CustomersListViewModel.UnitOfWork`

**VM ⇒ `IUnitOfWork` (21)** : CustomerDetail, CustomerForm, CustomerPrescriptions, CustomersList, Customers, Inventory, MainWindow, NotificationsList, Notifications, OrderKanban, Orders, PrescriptionForm, ProductForm, ProductsList, Products, SupplierForm, SuppliersList, Suppliers, UserForm, UsersList, Users.

**VM ⇒ `*Repository` (51)** : réparties sur 26 ViewModels (Customer/Customers, Inventory, MainWindow, Notifications, Order/Orders, Prescription, Product/Products, Sale, StockMovement, Supplier/Suppliers, User/Users…). Liste exhaustive dans `AllowedViewModelRepositoryConstructorDependencies`.

Total : **78 violations allowlistées**.

## 9. Fichiers modifiés

| Fichier | Nature |
|---|---|
| `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs` | **créé** (tests + allowlists) |
| `docs/implementation/P2C-1-report.md` | **créé** (ce rapport) |

Aucune modification de `MMV.App.Tests.csproj` (FluentAssertions n'était pas référencé dans ce projet → tests écrits en `xUnit.Assert`, comme les autres tests App, sans ajout de dépendance).

Aucun code métier, ViewModel, View, code-behind, UseCase, Domain, Infrastructure, DbContext, migration ou workflow CI touché.

## 10. Contrôles exécutés (finaux)

```
git status --short
git diff --stat
git diff --check
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list src/MMV.Application/MMV.Application.csproj reference
dotnet list src/MMV.Application/MMV.Application.csproj package
```

## 11. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| build | vert | **vert** (1 warning préexistant CS1998) |
| tests | > 398 | **403** (App 131 + Application 49 + Domain 223) |
| vulnérabilités | 0 | **0** |
| `has-pending-model-changes` | false | **false** |
| `MMV.Application` reference | Domain seul | **Domain seul** |
| `MMV.Application` package | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |
| fichiers modifiés | tests + rapport | **tests + rapport uniquement** |
| `git diff --check` | propre | **propre** |

## 12. Migrations créées ou non

**Aucune migration créée.** Aucun modèle EF modifié (`has-pending-model-changes = false`).

## 13. Risques

- **Faux négatif token `Repository`** : le scan code-behind est volontairement large (substring) ; un futur fichier mentionnant « Repository » même sans persistance déclencherait le test. C'est conservateur (préfère bloquer) et acceptable pour une baseline ; arbitrable au cas par cas.
- **Réduction d'allowlist obligatoire** : la garde « stale allowlist » impose de retirer une entrée dès que la dépendance disparaît — bénéfique, mais exige de mettre à jour le test à chaque phase P2C (comportement voulu).
- **Périmètre figé** : ces tests ne corrigent **aucune** violation ; ils ne font que geler la baseline. La dette UI⇄persistance reste entière jusqu'aux phases P2C suivantes.

## 14. Validation CI distante

| Paramètre | Valeur |
|---|---|
| Lien du run | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27496279411 |
| Identifiant du run | 27496279411 |
| Commit testé | `b292affa941bf24ce3bf04777bac4ee031a2ee7e` |
| Branche testée | `p2c-ui-cleanup` |
| Workflow | CI — Restore / Build / Test / Scan |
| Événement déclencheur | push |

| Étape CI | Résultat |
|---|---|
| Restore | **success** (10:42:01 → 10:42:58) |
| Build | **success** (10:42:58 → 10:43:25) |
| Test | **success** (10:43:25 → 10:44:09) |
| Audit NuGet (JSON + sévérité) | **success** (10:44:09 → 10:44:27) |
| Restore .NET tools | **success** (10:44:27 → 10:44:28) |
| Check EF Core pending model changes | **success** (10:44:28 → 10:44:31) |
| **Statut final du workflow** | **completed / success** |

Nombre de tests CI : **403** (App 131 + Application 49 + Domain 223).
Durée totale : **3 min 18 s**.

## 15. Verdict

**P2C-1 = GO DÉFINITIF.**

Critères d'acceptation tous satisfaits localement et validés par CI : branche `p2c-ui-cleanup` créée depuis `main`, aucun code métier/VM/use case/migration touché, tests garde-fous créés avec allowlists explicites, violations documentées (78), tests verts (403), 0 vulnérabilité, `has-pending-model-changes = false`. CI GitHub Actions complétée avec succès (run 27496279411).

## 17. Prochaine étape candidate

**P2C-2** — première réduction d'allowlist : migrer la persistance d'un code-behind « feuille » vers la couche Application, en commençant par le cas le plus isolé :

- `Views/Clients/CustomerFormView.axaml.cs` + `CustomerFormViewModel` (`ICustomerRepository` + `IUnitOfWork`) → use case `CreateCustomer` / `UpdateCustomer` dans `MMV.Application`,
- puis retrait des entrées correspondantes des allowlists (la garde « stale allowlist » validera la réduction).

`App.axaml.cs` (composition root) reste autorisé jusqu'à la fin de P2C.
