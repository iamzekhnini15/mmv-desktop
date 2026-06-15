# Rapport P2C-2 — Clients : Create/Update via Application + réduction allowlist

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-2 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

Aucun commit créé, aucun push effectué (conformément à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 2. État Git initial

Branche : `p2c-ui-cleanup`. Derniers commits :

```
e3d2e9d docs(P2C-1): record UI guardrails CI validation
b292aff test(P2C-1): add UI persistence guardrails
6300250 docs(P2C-0): update merge report with CI distante validation
1f8e05c docs(P2C-0): record P2B merge validation
9a40772 merge(P2C-0): integrate P2B architecture into main
```

`git status --short` initial : **une seule entrée pré-existante**, non liée à P2C-2 :

```
MM docs/implementation/P2C-1-report.md
```

Cette modification du rapport P2C-1 était **déjà présente** dans l'arbre de travail au démarrage (édition antérieure hors périmètre P2C-2). Elle n'a **pas** été touchée : `docs/implementation/P2C-1-report.md` n'appartient pas aux fichiers autorisés de cette phase, et `ALLOW_COMMIT = false`. Seul écart à « working tree propre », sans impact sur build/tests/architecture.

## 3. Baseline (sur `p2c-ui-cleanup`, avant modification)

| Contrôle | Attendu | Obtenu |
|---|---|---|
| `dotnet restore MMV.sln` | OK | **OK** |
| `dotnet build MMV.sln --no-restore -c Debug` | vert | **vert** (0 erreur) |
| `dotnet test MMV.sln --no-build -c Debug` | 403 | **403** (App 131 + Application 49 + Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | 0 | **0 vulnérabilité** |
| `dotnet tool restore` | OK | **OK** (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | false | **false** |
| `dotnet list src/MMV.Application reference` | Domain seul | **MMV.Domain seul** |
| `dotnet list src/MMV.Application package` | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |

Baseline **GO**.

## 4. Inventaire du flux client avant extraction (lecture du code réel)

### Où se faisait la création / modification client

Trois emplacements portaient de la persistance directe du client :

1. **`CustomerFormViewModel.ExecuteSave`** (chemin réellement actif) — branche création (`new Customer { … }` + `_customerRepository.CreateAsync` + `_unitOfWork.SaveChangesAsync`) et branche édition (mutation de `_originalCustomer` + `_customerRepository.UpdateAsync` + `SaveChangesAsync`).
2. **`CustomerFormView.axaml.cs` → `ButtonEnregistrer_Click`** — *code mort* : branche « legacy » sur `CustomersViewModel` appelant `CustomersListViewModel.Repository.CreateAsync/UpdateAsync` + `UnitOfWork.SaveChangesAsync`. La vue est bindée en XAML via `DataContext="{Binding CustomerFormViewModel}"` et le bouton via `Command="{Binding SaveCommand}"` : ce handler `Click` n'est **pas câblé** dans le `.axaml`.
3. **`CustomersView.axaml.cs` → `ButtonEnregistrer_Click`** — *code mort* identique, jamais câblé dans `CustomersView.axaml` (qui n'utilise que des `Command` et l'événement `Border_PointerPressed`).

### Champs Customer alimentés

`FirstName`, `LastName`, `Email`, `Phone`, `BirthDate`, `Address`, `City`, `PostalCode`, `SocialSecurityNumber`, `InsuranceName`, `Notes`, `CreatedAt`, `UpdatedAt`.

### Appels Repository / SaveChanges

- Création : `ICustomerRepository.CreateAsync(customer)` puis `IUnitOfWork.SaveChangesAsync()`.
- Édition : `ICustomerRepository.UpdateAsync(customer)` puis `IUnitOfWork.SaveChangesAsync()`.
- `SaveChangesAsync` : un seul appel par enregistrement (mono-écriture, pas de `ITransactionRunner`).

### Normalisation / horodatage

- Champs optionnels : `string.IsNullOrWhiteSpace(x) ? null : x`.
- Création : `CreatedAt = UpdatedAt = DateTime.UtcNow`.
- Édition : `UpdatedAt = DateTime.UtcNow` (CreatedAt préservé).

### Événements UI après succès / erreurs / rafraîchissements

- Succès → `CustomerSaved?.Invoke(this, customer)`. Consommé par `CustomersViewModel.OnCustomerFormSaved` qui **recharge la liste** (`LoadCustomersAsync`) puis ferme le formulaire (`CloseForm`).
- Annulation → `Cancelled?.Invoke` → `CloseForm`.
- Erreur → `ErrorMessage = "Erreur lors de l'enregistrement : …"`. Validation de surface : `ErrorMessage = "Veuillez corriger les erreurs…"`.
- Aucun rafraîchissement de détail dans ce flux (la fiche détail est un flux séparé, hors périmètre).

## 5. Décisions de périmètre

- **Code-behind = code mort** : les deux `ButtonEnregistrer_Click` ne sont pas câblés en XAML ; leur suppression est iso-fonctionnelle (le flux réel passe par `SaveCommand` → `CustomerFormViewModel`).
- **Suppression client** (`CustomersListViewModel.DeleteCommand`, `CustomersView.axaml.cs → ButtonSupprimer_Click`) : **laissée intacte**, hors périmètre create/update. `ButtonSupprimer_Click` ne contient aucun jeton de persistance (appelle `DeleteCommand.Execute`), donc ne bloque pas le retrait du code-behind de l'allowlist.
- **Propriétés publiques `CustomersListViewModel.Repository` / `.UnitOfWork`** : toujours présentes (la liste s'en sert encore pour son propre chargement/suppression). Non retirées — la garde « stale allowlist » indique qu'elles existent encore ; conformément à la consigne, on ne retire pas une entrée encore réelle. Reportées à une phase ultérieure (cf. roadmap §6.5).
- **`CustomersViewModel`** adapté **uniquement** pour transmettre les use cases au `CustomerFormViewModel` (il construit la VM manuellement, hors DI). Ses dépendances `ICustomerRepository`/`IUnitOfWork` restent (utilisées par la liste et la fiche détail) → entrées d'allowlist conservées.
- **`CustomerSaved`** : signature `EventHandler<Customer>` **conservée** (le consommateur ne lit que pour recharger la liste). La VM construit l'entité de notification à partir de l'état du formulaire + l'identifiant renvoyé par le use case ; cette copie ne déclenche aucune écriture.

## 6. Use cases créés

`src/MMV.Application/UseCases/Customers/CreateCustomer/`
- `CreateCustomerCommand.cs` — DTO d'entrée (champs requis `FirstName`/`LastName`, optionnels `string?`).
- `CreateCustomerResult.cs` — DTO de sortie (`CustomerId`, `DisplayName`).
- `ICreateCustomerUseCase.cs` — contrat.
- `CreateCustomerUseCase.cs` — construit le `Customer`, normalise (blanc→null), `CreatedAt`/`UpdatedAt = UtcNow`, `CreateAsync` + `SaveChangesAsync`, renvoie le résultat.

`src/MMV.Application/UseCases/Customers/UpdateCustomer/`
- `UpdateCustomerCommand.cs` — DTO d'entrée (`CustomerId` + champs éditables).
- `UpdateCustomerResult.cs` — DTO de sortie (`CustomerFound`, `CustomerId`, `DisplayName`).
- `IUpdateCustomerUseCase.cs` — contrat.
- `UpdateCustomerUseCase.cs` — charge par `GetByIdAsync` ; si absent → `CustomerFound = false` sans écrire ; sinon met à jour les champs (normalisés), `UpdatedAt = UtcNow` (CreatedAt préservé), `UpdateAsync` + `SaveChangesAsync`.

Frontière transactionnelle : mono-écriture, pas de `ITransactionRunner` (cohérent avec `CreateOrderUseCase`/`UpdateOrderUseCase`). Aucune nouvelle règle métier, aucune entité EF retournée, aucun `DbContext` exposé.

## 7. Modifications ViewModel

- **`CustomerFormViewModel`** : constructeur `(ICreateCustomerUseCase, IUpdateCustomerUseCase)` au lieu de `(ICustomerRepository, IUnitOfWork)` (avec garde `ArgumentNullException`). `ExecuteSave` construit `CreateCustomerCommand`/`UpdateCustomerCommand` et appelle le use case ; gère `CustomerFound = false` (message d'erreur), conserve `IsSaving`, `ErrorMessage`, la validation de surface et l'événement `CustomerSaved`. Nouveau helper privé `ApplyFormTo(Customer)` (copie d'affichage uniquement). Suppression de tous les appels `CreateAsync`/`UpdateAsync`/`SaveChangesAsync` et du bruit de logs.
- **`CustomersViewModel`** : injection de `ICreateCustomerUseCase` + `IUpdateCustomerUseCase` (gardes null) ; les deux `new CustomerFormViewModel(_customerRepository, _unitOfWork)` deviennent `new CustomerFormViewModel(_createCustomerUseCase, _updateCustomerUseCase)`. Le reste (liste, détail, ventes) inchangé.

## 8. Modifications code-behind

- **`CustomerFormView.axaml.cs`** : suppression de `ButtonEnregistrer_Click` (branche legacy `Repository.CreateAsync/UpdateAsync` + `UnitOfWork.SaveChangesAsync`) et du `using MMV.Domain.Entities` devenu inutile. Conservé : constructeur, `OnDataContextChanged` (diagnostic), `ButtonRetour_Click` (état VM / `CancelCommand`). **Plus aucun jeton de persistance.**
- **`CustomersView.axaml.cs`** : suppression de `ButtonEnregistrer_Click` (même branche legacy). Conservé : `Border_PointerPressed` (câblé en XAML → `ViewDetailCommand`), `ButtonNouveau/Retour/Supprimer/NouvelleOrdonnance_Click` (état VM / commandes, sans persistance). **Plus aucun jeton de persistance.**

## 9. Tests créés / modifiés

**Application (créés, SQLite réel — jamais InMemory) :**
- `tests/MMV.Application.Tests/UseCases/Customers/CreateCustomerUseCaseTests.cs` — 4 tests : création persistée + identifiant/nom ; champs blancs → null ; commande nulle → `ArgumentNullException` ; constructeur sans repository → throw.
- `tests/MMV.Application.Tests/UseCases/Customers/UpdateCustomerUseCaseTests.cs` — 5 tests : édition + `UpdatedAt` réaffecté / `CreatedAt` préservé ; champs blancs → null ; client introuvable → `CustomerFound = false` sans écriture ; commande nulle → throw ; constructeur sans `IUnitOfWork` → throw.

**App (réécrit) :**
- `tests/MMV.App.Tests/ViewModels/CustomerFormViewModelTests.cs` — adapté aux use cases (mocks Moq). Couvre : délégation à `CreateCustomerUseCase` (mapping état→commande, événement, id renvoyé) ; délégation à `UpdateCustomerUseCase` ; `CustomerFound = false` → message d'erreur, pas d'événement ; exception use case → `ErrorMessage` + `IsSaving` remis à `false` ; **anti double-submit** (`CanExecute` faux pendant `IsSaving`) ; gardes constructeur null ; validation de surface et `InitializeForEdit/Create` inchangés.

## 10. Réduction d'allowlist

Fichier : `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`.

| Allowlist | Avant (P2C-1) | Après (P2C-2) | Entrées retirées |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 51 | **50** | `CustomerFormViewModel -> ICustomerRepository` |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 21 | **20** | `CustomerFormViewModel -> IUnitOfWork` |
| `AllowedPublicPersistenceProperties` | 2 | **2** | — (encore présentes) |
| `AllowedCodeBehindPersistenceFiles` | 4 | **2** | `Views/Clients/CustomerFormView.axaml.cs`, `Views/Clients/CustomersView.axaml.cs` |
| **Total** | **78** | **74** | **−4** |

La garde « stale allowlist » (une entrée retirée doit avoir réellement disparu, sinon échec) **valide automatiquement** la réduction : les 4 tests de `AppUiPersistenceGuardrailTests` sont verts.

## 11. Fichiers modifiés / créés

**Créés :**
- `src/MMV.Application/UseCases/Customers/CreateCustomer/{CreateCustomerCommand,CreateCustomerResult,ICreateCustomerUseCase,CreateCustomerUseCase}.cs`
- `src/MMV.Application/UseCases/Customers/UpdateCustomer/{UpdateCustomerCommand,UpdateCustomerResult,IUpdateCustomerUseCase,UpdateCustomerUseCase}.cs`
- `tests/MMV.Application.Tests/UseCases/Customers/{CreateCustomerUseCaseTests,UpdateCustomerUseCaseTests}.cs`
- `docs/implementation/P2C-2-report.md` (ce rapport)

**Modifiés :**
- `src/MMV.Application/DependencyInjection.cs`
- `src/MMV.App/ViewModels/CustomerFormViewModel.cs`
- `src/MMV.App/ViewModels/CustomersViewModel.cs`
- `src/MMV.App/Views/Clients/CustomerFormView.axaml.cs`
- `src/MMV.App/Views/Clients/CustomersView.axaml.cs`
- `tests/MMV.App.Tests/ViewModels/CustomerFormViewModelTests.cs`
- `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`

Tous dans le périmètre des fichiers autorisés. `docs/implementation/P2C-1-report.md` (`MM`) = modification **pré-existante**, non touchée.

## 12. Contrôles exécutés

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

## 13. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| build | vert | **vert** (0 erreur, 0 warning) |
| tests | > 403 | **417** (App 136 + Application 58 + Domain 223) |
| vulnérabilités | 0 | **0** |
| `has-pending-model-changes` | false | **false** |
| `MMV.Application` reference | Domain seul | **Domain seul** |
| `MMV.Application` package | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |
| allowlist | diminuée | **78 → 74 (−4)** |
| `git diff --check` | propre | **propre** |
| nouveau code-behind persistant | aucun | **aucun** |

## 14. Migrations créées ou non

**Aucune migration créée.** Aucun modèle EF modifié (`has-pending-model-changes = false`). Aucune entité Domain modifiée.

## 15. Risques résiduels

- **`CustomersListViewModel.Repository` / `.UnitOfWork`** restent exposés publiquement (2 entrées d'allowlist) : encore utilisés par la liste pour son propre chargement/suppression. À traiter dans une phase ultérieure (suppression client + lectures), pas dans le périmètre create/update.
- **`CustomersViewModel` / `CustomersListViewModel`** dépendent encore de `ICustomerRepository`/`IUnitOfWork` (chargement de liste, fiche détail, suppression) : entrées d'allowlist conservées (réelles).
- **`async void ExecuteSave`** conservé (contrainte `RelayCommand`/MVVM existante) : comportement inchangé, exceptions captées et remontées en `ErrorMessage`.
- **Copie d'affichage `ApplyFormTo`** : duplique la normalisation blanc→null côté VM pour l'événement `CustomerSaved` ; sans effet de persistance (la liste est rechargée depuis la base juste après). Acceptable et iso-fonctionnel.

## 16. Validation CI distante

| Champ | Valeur |
|---|---|
| Run ID | 27546180258 |
| CI # | 48 |
| Commit testé | `e15c614` |
| Branche | `p2c-ui-cleanup` |
| Statut | **completed / success** |
| Durée | 4m03s |
| Workflow | Restore / Build / Test / Scan |

CI distante **verte** sur le commit de référence P2C-2.

## 17. Verdict

**P2C-2 = GO DÉFINITIF COMPLET.**

Tous les critères d'acceptation satisfaits : `CreateCustomerUseCase` et `UpdateCustomerUseCase` créés et enregistrés en DI Application ; `CustomerFormViewModel` ne dépend plus de `ICustomerRepository` ni de `IUnitOfWork` ; `CustomerFormView.axaml.cs` et `CustomersView.axaml.cs` ne font plus de persistance directe ; tests Application ajoutés (SQLite réel) ; tests App adaptés (délégation + garde-fous) ; `AppUiPersistenceGuardrailTests` verts ; allowlist réduite (78 → 74) ; build vert ; **417 tests verts** ; 0 vulnérabilité ; `has-pending-model-changes = false` ; aucune migration ; aucun modèle EF modifié. CI distante #48 (run 27546180258) verte sur `e15c614`.

## 18. Prochaine étape candidate

**P2C-3 — Clients (reliquat) :** retirer la fuite `CustomersListViewModel.Repository` / `.UnitOfWork` (propriétés publiques) et migrer la **suppression** client (`CustomersListViewModel.ExecuteDelete`) vers un `DeleteCustomerUseCase`, puis les **lectures d'affichage** (`LoadCustomersAsync`, fiche détail) vers des query use cases / ports de lecture — réduisant alors `AllowedPublicPersistenceProperties` (2 → 0) et les dépendances repository/UoW restantes des ViewModels clients.
