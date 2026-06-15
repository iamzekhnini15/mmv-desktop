# Rapport P2C-3 — Clients (reliquat) : DeleteCustomer + suppression des fuites Repository / UnitOfWork

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-3 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | true |
| `ALLOW_PUSH` | true |

## 2. État Git initial

Branche : `p2c-ui-cleanup`. Working tree **propre** au démarrage (`git status --short` vide). Derniers commits :

```
21fc1c2 docs(P2C-2): record documentary CI validation (CI #49 success)
ba4f30d docs(P2C-2): record customer use cases CI validation
e15c614 feat(P2C-2): move customer create update to application use cases
e3d2e9d docs(P2C-1): record UI guardrails CI validation
b292aff test(P2C-1): add UI persistence guardrails
```

Note : la branche porte un commit de plus (`21fc1c2`, documentaire) que le « dernier commit attendu » `ba4f30d` du brief — sans incidence (commit de rapport P2C-2, déjà validé CI #49).

## 3. Baseline (sur `p2c-ui-cleanup`, avant modification)

| Contrôle | Attendu | Obtenu |
|---|---|---|
| `git branch --show-current` | p2c-ui-cleanup | **p2c-ui-cleanup** |
| working tree | propre | **propre** |
| `dotnet restore MMV.sln` | OK | **OK** |
| `dotnet build MMV.sln --no-restore -c Debug` | vert | **vert** (0 erreur, 1 warning pré-existant `OrderFormViewModel` CS1998, hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | 417 | **417** (App 136 + Application 58 + Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | 0 | **0 vulnérabilité** |
| `dotnet tool restore` | OK | **OK** (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | false | **false** |
| `dotnet list src/MMV.Application reference` | Domain seul | **MMV.Domain seul** |
| `dotnet list src/MMV.Application package` | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |

Baseline **GO**.

## 4. Inventaire exact du flux suppression client avant extraction (lecture du code réel)

- **Commande de suppression** : `CustomersListViewModel.DeleteCommand` (= `RelayCommand(ExecuteDelete, CanEditOrDelete)`), exposée via XAML et déclenchée aussi par le code-behind.
- **Méthode exécutant la suppression** : `CustomersListViewModel.ExecuteDelete` (`async void`).
- **Appels Repository/Delete** : `await _customerRepository.DeleteAsync(SelectedCustomer.CustomerId)` (surcharge par identifiant de `IGenericRepository`).
- **`SaveChangesAsync`** : `await _unitOfWork.SaveChangesAsync()` — un seul appel (mono-écriture, pas de `ITransactionRunner`).
- **Message de confirmation** : **aucun**. Le flux ne comporte **pas** de dialogue de confirmation. Le code-behind `CustomersView.axaml.cs → ButtonSupprimer_Click` appelle directement `vm.CustomersListViewModel.DeleteCommand.Execute(null)`. (Il n'y a donc pas de chemin « annulation » à migrer ni à tester côté VM.)
- **Message d'erreur** : `ErrorMessage = "Erreur lors de la suppression du client : {ex.Message}"`.
- **Rafraîchissement de la liste après suppression** : pas de rechargement base ; la VM retire l'élément en mémoire (`Customers.Remove(SelectedCustomer)` + `ApplyFilter()`) puis `SelectedCustomer = null`.
- **Accès code-behind à Repository / UnitOfWork** : **aucun**. Recherche exhaustive (`.Repository` / `.UnitOfWork`) dans `src/` : aucune consommation des propriétés publiques `CustomersListViewModel.Repository` / `.UnitOfWork` (les usages legacy avaient été retirés en P2C-2). Le code-behind n'utilise que `SelectedCustomer` et `DeleteCommand`. Les deux propriétés publiques étaient donc devenues du **code mort exposé** (pure fuite de port).

## 5. Décisions de périmètre

- **Suppression directe → use case** : `ExecuteDelete` ne fait plus de `DeleteAsync` + `SaveChangesAsync` ; elle construit `DeleteCustomerCommand` et délègue à `IDeleteCustomerUseCase`. La VM conserve la mise à jour d'écran (retrait liste, `ErrorMessage`, `SelectedCustomer = null`).
- **Pas de dialogue de confirmation introduit** : le flux d'origine n'en avait pas ; on reste iso-fonctionnel (aucune nouvelle règle UX).
- **Cas introuvable** : `DeleteCustomerResult.CustomerFound = false` → message d'erreur « Le client à supprimer est introuvable. », client conservé. En pratique la suppression ne se déclenche que sur un client sélectionné réel ; ce chemin couvre le cas théorique sans écriture dangereuse (cohérent avec `DeleteOrderUseCase`).
- **`ICustomerRepository` conservé dans `CustomersListViewModel`** : encore requis pour `LoadCustomersAsync` (lecture d'affichage). Entrée d'allowlist `CustomersListViewModel -> ICustomerRepository` **conservée** (réelle) ; dette reportée vers des query use cases (roadmap §6).
- **`IUnitOfWork` retiré de `CustomersListViewModel`** : plus aucun usage après extraction de la suppression → retiré du constructeur et de l'allowlist.
- **Propriétés publiques `Repository` / `UnitOfWork` supprimées** : plus aucun consommateur (cf. §4) → suppression iso-fonctionnelle ; allowlist `AllowedPublicPersistenceProperties` vidée (2 → 0).
- **`CustomersViewModel`** : conserve `IUnitOfWork` (encore requis pour construire `CustomerDetailViewModel`, fiche détail, hors périmètre) ; injecte désormais `IDeleteCustomerUseCase` et le transmet à `CustomersListViewModel` (la liste n'est plus construite avec `unitOfWork`).
- **`CustomersView.axaml.cs`** : **non modifié** (aucun jeton de persistance ; déjà retiré de l'allowlist code-behind en P2C-2).
- **Composition root (`App.axaml.cs`)** : **non touché**. `CustomersViewModel` est enregistré via `AddTransient<CustomersViewModel>()` (résolution automatique du constructeur) ; le nouveau paramètre `IDeleteCustomerUseCase` est résolu automatiquement par DI.

## 6. Use case créé

`src/MMV.Application/UseCases/Customers/DeleteCustomer/`
- `DeleteCustomerCommand.cs` — DTO d'entrée (`CustomerId`).
- `DeleteCustomerResult.cs` — DTO de sortie (`CustomerFound`, `CustomerId`).
- `IDeleteCustomerUseCase.cs` — contrat.
- `DeleteCustomerUseCase.cs` — charge par `GetByIdAsync` ; si absent → `CustomerFound = false` sans écrire ; sinon `DeleteAsync(entité)` + `SaveChangesAsync`, renvoie `CustomerFound = true`.

Frontière transactionnelle : mono-écriture, pas de `ITransactionRunner` (cohérent avec `DeleteOrderUseCase`). Aucune nouvelle règle métier, aucune cascade inventée, aucune entité EF retournée, aucun `DbContext` exposé.

## 7. Modifications ViewModel

- **`CustomersListViewModel`** : constructeur `(ICustomerRepository, IDeleteCustomerUseCase)` au lieu de `(ICustomerRepository, IUnitOfWork)` (gardes `ArgumentNullException`). Champ `_unitOfWork` supprimé ; champ `_deleteCustomerUseCase` ajouté. Propriétés publiques `Repository` et `UnitOfWork` **supprimées**. `ExecuteDelete` réécrit : construit `DeleteCustomerCommand`, délègue, gère `CustomerFound = false`, conserve `IsLoading`, `ErrorMessage`, le retrait de liste et `ApplyFilter`. Plus aucun jeton de persistance (`DeleteAsync`/`SaveChangesAsync`/`IUnitOfWork`).
- **`CustomersViewModel`** : injection de `IDeleteCustomerUseCase` (garde null) ; `new CustomersListViewModel(customerRepository, unitOfWork)` devient `new CustomersListViewModel(customerRepository, _deleteCustomerUseCase)`. `IUnitOfWork` conservé pour `CustomerDetailViewModel`. Reste inchangé.

## 8. Modifications code-behind

**Aucune.** `CustomersView.axaml.cs` ne contenait aucun jeton de persistance ni accès à `Repository`/`UnitOfWork` (déjà nettoyé en P2C-2) ; il n'a pas eu besoin d'être modifié.

## 9. Tests créés / modifiés

**Application (créé, SQLite réel — jamais InMemory) :**
- `tests/MMV.Application.Tests/UseCases/Customers/DeleteCustomerUseCaseTests.cs` — 5 tests : suppression persistée (`CustomerFound = true`, base vidée) ; client introuvable → `CustomerFound = false` sans écriture (le survivant reste) ; commande nulle → `ArgumentNullException` ; constructeur sans repository → throw ; constructeur sans `IUnitOfWork` → throw.

**App (créé, mocks/spy) :**
- `tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs` — 7 tests : délégation à `IDeleteCustomerUseCase` + retrait de liste ; mapping `SelectedCustomer.CustomerId` → commande ; `CustomerFound = false` → message d'erreur, client conservé, pas de crash ; exception use case → `ErrorMessage` (format existant) ; `DeleteCommand.CanExecute` faux sans sélection ; gardes constructeur null (repository, use case).

Aucun test existant n'a dû être adapté (`CustomersViewModelTests` n'existe pas ; le constructeur `CustomersViewModel` n'est testé par aucun test existant).

## 10. Réduction d'allowlist

Fichier : `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`.

| Allowlist | Avant (P2C-2) | Après (P2C-3) | Entrées retirées |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 50 | **50** | — (`CustomersListViewModel -> ICustomerRepository` conservé, réel) |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 20 | **19** | `CustomersListViewModel -> IUnitOfWork` |
| `AllowedPublicPersistenceProperties` | 2 | **0** | `CustomersListViewModel.Repository`, `CustomersListViewModel.UnitOfWork` |
| `AllowedCodeBehindPersistenceFiles` | 2 | **2** | — |
| **Total** | **74** | **71** | **−3** |

La garde « stale allowlist » (une entrée retirée doit avoir réellement disparu) valide automatiquement la réduction : les 5 tests de `AppUiPersistenceGuardrailTests` sont verts. `AllowedPublicPersistenceProperties` est désormais **vide** : aucun ViewModel ne ré-expose ses ports de persistance.

## 11. Fichiers modifiés / créés

**Créés :**
- `src/MMV.Application/UseCases/Customers/DeleteCustomer/{DeleteCustomerCommand,DeleteCustomerResult,IDeleteCustomerUseCase,DeleteCustomerUseCase}.cs`
- `tests/MMV.Application.Tests/UseCases/Customers/DeleteCustomerUseCaseTests.cs`
- `tests/MMV.App.Tests/ViewModels/CustomersListViewModelTests.cs`
- `docs/implementation/P2C-3-report.md` (ce rapport)

**Modifiés :**
- `src/MMV.Application/DependencyInjection.cs`
- `src/MMV.App/ViewModels/CustomersListViewModel.cs`
- `src/MMV.App/ViewModels/CustomersViewModel.cs`
- `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`

Tous dans le périmètre des fichiers autorisés. `CustomersView.axaml.cs` autorisé mais non modifié.

## 12. Contrôles exécutés

```
git branch --show-current
git status --short
git log -5 --oneline
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

## 13. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| build | vert | **vert** (0 erreur, 1 warning pré-existant hors périmètre) |
| tests | > 417 | **429** (App 143 + Application 63 + Domain 223) |
| vulnérabilités | 0 | **0** |
| `has-pending-model-changes` | false | **false** |
| `MMV.Application` reference | Domain seul | **Domain seul** |
| `MMV.Application` package | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |
| allowlist | diminuée | **74 → 71 (−3)** |
| `git diff --check` | propre | **propre** (seul un avis LF→CRLF, pas une erreur d'espaces) |
| propriétés publiques Repository/UnitOfWork dans `CustomersListViewModel` | aucune | **aucune** |

## 14. Migrations créées ou non

**Aucune migration créée.** Aucun modèle EF modifié (`has-pending-model-changes = false`). Aucune entité Domain modifiée.

## 15. Risques résiduels

- **`CustomersListViewModel` dépend encore de `ICustomerRepository`** (lecture `LoadCustomersAsync` + filtre/recherche en mémoire) : 1 entrée d'allowlist conservée (réelle). Dette → query use cases / port de lecture (roadmap §6).
- **`CustomersViewModel` dépend encore de `ICustomerRepository` + `IUnitOfWork`** : transmis à `CustomerDetailViewModel` (fiche détail, hors périmètre P2C-3). Entrées d'allowlist conservées.
- **`async void ExecuteDelete`** conservé (contrainte `RelayCommand`/MVVM) : comportement inchangé, exceptions captées → `ErrorMessage`.
- **Absence de confirmation utilisateur** sur la suppression : comportement d'origine inchangé (non introduit ici pour rester iso-fonctionnel) ; pourrait être ajouté ultérieurement via `IDialogService` si souhaité (évolution UX, hors nettoyage architectural).

## 16. Validation CI distante

| Champ | Valeur |
|---|---|
| Lien du run | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27580886305 |
| Identifiant du run | 27580886305 (CI #51) |
| Commit testé | `146ceb59724bb38d6afd002159fa0070ca5382b4` |
| Branche testée | `p2c-ui-cleanup` |
| Créé le | 2026-06-15T22:34:59Z |
| Complété le | 2026-06-15T22:37:57Z |

| Étape CI | Résultat |
|---|---|
| Restore | **success** |
| Build | **success** |
| Test | **success** (429 tests) |
| Audit NuGet (`Audit des packages vulnérables`) | **success** (0 vulnérabilité) |
| Restore .NET tools | **success** (`dotnet-ef` 8.0.27) |
| Check EF Core pending model changes | **success** (false) |
| **Statut final du workflow** | **completed / success** |

## 17. Verdict

**P2C-3 = GO DÉFINITIF COMPLET.**

Critères d'acceptation satisfaits : `DeleteCustomerUseCase` créé et enregistré en DI Application ; `CustomersListViewModel` n'expose plus `Repository` / `UnitOfWork` et ne dépend plus de `IUnitOfWork` ; suppression client déléguée à `IDeleteCustomerUseCase` ; tests Application ajoutés (SQLite réel, 5) ; tests App ajoutés (délégation + garde-fous, 7) ; `AppUiPersistenceGuardrailTests` verts ; allowlist réduite (74 → 71, dont `AllowedPublicPersistenceProperties` 2 → 0) ; build vert ; **429 tests verts** ; 0 vulnérabilité ; `has-pending-model-changes = false` ; aucune migration ; aucun modèle EF modifié ; rapport P2C-3 créé. Commit `146ceb5` poussé sur `p2c-ui-cleanup`. CI GitHub Actions run #51 : **completed / success**.

## 18. Prochaine étape candidate

**P2C-4 — Ordonnances** (selon roadmap §4) : extraire la persistance directe de `PrescriptionFormViewModel`, `PrescriptionDetailViewModel` et `CustomerPrescriptionsViewModel` vers des use cases Application (create/update/delete d'ordonnance), réduisant les entrées d'allowlist correspondantes (`*Prescription* -> IPrescriptionRepository` / `-> IUnitOfWork`).

Reliquat lecture client (hors P2C-3, à planifier) : migrer `LoadCustomersAsync` / recherche / fiche détail vers des query use cases, ce qui retirerait les dernières dépendances `ICustomerRepository` des ViewModels clients.
