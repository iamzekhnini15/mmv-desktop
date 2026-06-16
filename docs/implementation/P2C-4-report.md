# Rapport P2C-4 — Ordonnances : Create / Update / Delete Prescription via Application

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2C-4 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2c-ui-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

Aucun commit, aucun push effectués (conforme à `ALLOW_COMMIT = false` / `ALLOW_PUSH = false`).

## 2. État Git initial

Branche : `p2c-ui-cleanup`. Working tree **propre** au démarrage. Derniers commits :

```
da45c3d docs(P2C-3): record customer deletion CI validation
146ceb5 feat(P2C-3): move customer deletion to application use case
21fc1c2 docs(P2C-2): record documentary CI validation (CI #49 success)
ba4f30d docs(P2C-2): record customer use cases CI validation
e15c614 feat(P2C-2): move customer create update to application use cases
```

Précondition P2C-3 **vérifiée** : présence de `feat(P2C-3)` (`146ceb5`) **et** du commit documentaire final P2C-3 (`da45c3d`). Branche propre. → P2C-4 **GO précondition**.

## 3. Baseline (sur `p2c-ui-cleanup`, avant modification)

| Contrôle | Attendu | Obtenu |
|---|---|---|
| `git branch --show-current` | p2c-ui-cleanup | **p2c-ui-cleanup** |
| working tree | propre | **propre** |
| `dotnet restore MMV.sln` | OK | **OK** |
| `dotnet build MMV.sln --no-restore -c Debug` | vert | **vert** (0 erreur, 1 warning pré-existant `OrderFormViewModel` CS1998, hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | 429 | **429** (App 143 + Application 63 + Domain 223) |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | 0 | **0 vulnérabilité** |
| `dotnet tool restore` | OK | **OK** (`dotnet-ef` 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | false | **false** |
| `dotnet list src/MMV.Application reference` | Domain seul | **MMV.Domain seul** |
| `dotnet list src/MMV.Application package` | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |

Baseline **GO**.

## 4. Inventaire exact du flux ordonnance avant extraction (lecture du code réel)

Le dépôt réel prime sur les rapports : la lecture des ViewModels a révélé **deux** écritures réelles, et **non trois**.

| Écriture | Emplacement d'origine | Appels persistance |
|---|---|---|
| **Création** | `PrescriptionFormViewModel.SavePrescriptionAsync` | `_prescriptionRepository.CreateAsync(prescription)` + `_unitOfWork.CommitAsync()` |
| **Suppression** | `CustomerPrescriptionsViewModel.ExecuteDeleteAsync` | `_prescriptionRepository.DeleteAsync(prescription.PrescriptionId)` + `_unitOfWork.CommitAsync()` |
| **Modification** | *(n'existait pas réellement)* | — |

Détails relevés :

- **Création.** `SavePrescriptionAsync` validait le formulaire, construisait une `Prescription` (tous les champs OD/OG, `IssueDate.UtcDateTime`, `DoctorName`, `Notes`), appelait `CreateAsync` + `CommitAsync`, déclenchait l'événement `PrescriptionSaved`, puis `ClearForm()`. **Aucune normalisation** des champs optionnels (pas de blanc → null). `CreatedAt` laissé à la valeur par défaut de l'entité.
- **« Modification ».** Le formulaire servait aussi à l'édition (`ExecuteEdit` → `LoadPrescription` → `Title = "Modifier Ordonnance"`), **mais** `LoadPrescription` **ne capturait jamais `PrescriptionId`** et `SavePrescriptionAsync` appelait **toujours** `CreateAsync`. Conséquence : **éditer une ordonnance créait un doublon** — il n'existait donc aucune écriture `UpdateAsync` à extraire.
- **Suppression.** `ExecuteDeleteAsync(prescription)` appelait `DeleteAsync(id)` + `CommitAsync` puis `LoadPrescriptionsAsync()`. Pas de dialogue de confirmation (TODO laissé en place). Message d'erreur : `"Erreur lors de la suppression : {ex.Message}"`.
- **Lecture.** La liste est chargée par `CustomerPrescriptionsViewModel.LoadPrescriptionsAsync` via `_prescriptionRepository.GetByCustomerIdAsync` (tri décroissant par `IssueDate`).
- **`PrescriptionDetailViewModel`.** Recevait `IPrescriptionRepository` mais **ne l'utilisait jamais** (dépendance morte) : VM purement présentationnelle (n'émet que des événements Edit/Delete/Back).
- **Chaîne de construction.** `CustomersViewModel` → `CustomerDetailViewModel` → `new CustomerPrescriptionsViewModel(_prescriptionRepository, _unitOfWork)` → `new PrescriptionFormViewModel(_prescriptionRepository, _unitOfWork)` / `new PrescriptionDetailViewModel(_prescriptionRepository)`.
- **Code-behind.** `PrescriptionFormView.axaml.cs`, `PrescriptionDetailView.axaml.cs`, `CustomerPrescriptionsView.axaml.cs`, `PrescriptionsView.axaml.cs` : **aucun jeton de persistance** (seulement `InitializeComponent` et un relais `ViewDetailsCommand`). `PrescriptionsViewModel` (module haut niveau, DI) est un **placeholder vide** (aucune écriture).

## 5. Décisions de périmètre

- **Décision Update (validée avec le demandeur).** Le flux d'édition n'effectuant aucun `UPDATE` réel (doublon), une question de périmètre a été posée. Choix retenu : **« Implémenter + câbler un vrai update »**. P2C-4 introduit donc `UpdatePrescriptionUseCase` **et** câble réellement la modification : `LoadPrescription` capture désormais `PrescriptionId`, et `SavePrescriptionAsync` aiguille `create` vs `update` selon `_prescriptionId > 0`. Effet observable assumé : l'édition **ne duplique plus** (correction du bug latent), conforme à l'intention manifeste de l'UI (« Modifier Ordonnance »).
- **Create / Delete : extraction iso-fonctionnelle** vers `ICreatePrescriptionUseCase` / `IDeletePrescriptionUseCase`. Messages d'erreur, événements, `ClearForm`, refresh de liste, gardes `IsSaving`/`IsLoading` conservés.
- **Pas de normalisation introduite** (le flux d'origine n'en faisait pas). Pas de cascade métier nouvelle. Pas de dialogue de confirmation introduit (TODO d'origine conservé).
- **`CommitAsync` → `SaveChangesAsync`.** Les use cases utilisent `SaveChangesAsync` (convention établie en P2C-2/P2C-3 : `Create/Update/DeleteCustomerUseCase`). Sur une mono-écriture, `CommitAsync` et `SaveChangesAsync` sont équivalents (un seul flush atomique) ; aucun `ITransactionRunner` requis.
- **`IPrescriptionRepository` conservé dans `CustomerPrescriptionsViewModel`** : encore requis pour la lecture d'affichage (`LoadPrescriptionsAsync`, `GetByCustomerIdAsync`). Entrée d'allowlist conservée (réelle), dette reportée vers des query use cases.
- **`PrescriptionDetailViewModel` : dépendance morte retirée.** `IPrescriptionRepository` (jamais utilisé) supprimé → constructeur sans dépendance de persistance. Candidat explicitement listé au §16 du brief.
- **`CustomerDetailViewModel` (hors périmètre, fiche client).** Injecte et transmet les 3 use cases ordonnance à `CustomerPrescriptionsViewModel`. Son `IUnitOfWork` n'est désormais plus utilisé en interne, mais le **paramètre de constructeur est conservé** (entrée d'allowlist maintenue, comme prévu par le brief : extraction « fiche client » = phase ultérieure) ; le champ mort a été retiré pour éviter un warning CS0414 (garde null conservée via `_ = unitOfWork ?? throw …`).
- **`CustomersViewModel` (hors périmètre).** Injecte les 3 use cases ordonnance (résolus par DI) et les transmet à `CustomerDetailViewModel`. Aucune autre modification de navigation.

## 6. Use cases créés

`src/MMV.Application/UseCases/Prescriptions/`

| Dossier | Fichiers |
|---|---|
| `CreatePrescription/` | `CreatePrescriptionCommand.cs`, `CreatePrescriptionResult.cs`, `ICreatePrescriptionUseCase.cs`, `CreatePrescriptionUseCase.cs` |
| `UpdatePrescription/` | `UpdatePrescriptionCommand.cs`, `UpdatePrescriptionResult.cs`, `IUpdatePrescriptionUseCase.cs`, `UpdatePrescriptionUseCase.cs` |
| `DeletePrescription/` | `DeletePrescriptionCommand.cs`, `DeletePrescriptionResult.cs`, `IDeletePrescriptionUseCase.cs`, `DeletePrescriptionUseCase.cs` |

Comportements :

- **Create** : construit la `Prescription` depuis la commande (sans normalisation, `CreatedAt` par défaut), `CreateAsync` + `SaveChangesAsync`, renvoie `PrescriptionId`.
- **Update** : `GetByIdAsync` ; si absent → `PrescriptionFound = false` sans écriture ; sinon met à jour les champs éditables (date, médecin, OD/OG, notes), **préserve `CustomerId` et `CreatedAt`**, `UpdateAsync` + `SaveChangesAsync`, renvoie `PrescriptionFound = true`.
- **Delete** : `GetByIdAsync` ; si absent → `PrescriptionFound = false` sans écriture ; sinon `DeleteAsync(entité)` + `SaveChangesAsync`, renvoie `PrescriptionFound = true`.

DTO neutres (aucune dépendance EF/Avalonia/MVVM ; `PrismBase` = enum Domain). Aucun `DbContext` exposé, aucune entité EF retournée.

## 7. Modifications ViewModel

- **`PrescriptionFormViewModel`** : constructeur `(ICreatePrescriptionUseCase, IUpdatePrescriptionUseCase)` au lieu de `(IPrescriptionRepository, IUnitOfWork)` (gardes `ArgumentNullException`). Champ `_prescriptionId` ajouté (0 = création, > 0 = édition), renseigné par `LoadPrescription`, remis à 0 par `ClearForm`. `SavePrescriptionAsync` réécrit : valide, construit la commande, **aiguille create/update**, gère `update introuvable` (message d'erreur, pas d'événement), conserve `PrescriptionSaved` + `ClearForm` + `IsSaving` + messages. Plus aucun jeton de persistance.
- **`CustomerPrescriptionsViewModel`** : constructeur `(IPrescriptionRepository, ICreatePrescriptionUseCase, IUpdatePrescriptionUseCase, IDeletePrescriptionUseCase)` au lieu de `(IPrescriptionRepository, IUnitOfWork)`. `IUnitOfWork` retiré. `ExecuteDeleteAsync` délègue à `IDeletePrescriptionUseCase` (gère `PrescriptionFound = false`), conserve le refresh et le format de message. `ExecuteCreate`/`ExecuteEdit` construisent le formulaire avec les use cases ; `ExecuteViewDetails` construit `PrescriptionDetailViewModel()` (sans dépendance).
- **`PrescriptionDetailViewModel`** : constructeur **paramétré → sans paramètre** (dépendance `IPrescriptionRepository` morte retirée). Comportement (événements Edit/Delete/Back) inchangé.

## 8. Modifications constructeurs parents

- **`CustomerDetailViewModel`** : + paramètres `ICreatePrescriptionUseCase`, `IUpdatePrescriptionUseCase`, `IDeletePrescriptionUseCase` (champs + gardes null) ; transmis à `CustomerPrescriptionsViewModel`. `IUnitOfWork` : paramètre conservé (allowlist), champ retiré (garde null préservée via `_ = unitOfWork ?? throw`).
- **`CustomersViewModel`** : + paramètres des 3 use cases ordonnance (champs + gardes null) ; transmis à `CustomerDetailViewModel`. Résolution DI automatique (`AddTransient<CustomersViewModel>`).

## 9. Modifications code-behind

**Aucune.** Les code-behind d'ordonnances ne contenaient aucun jeton de persistance (cf. §4). L'allowlist code-behind reste inchangée (2 entrées : `App.axaml.cs`, `MainWindow.axaml.cs`).

## 10. Tests créés / modifiés

**Application (créés, SQLite réel — jamais InMemory) — 16 tests :**
- `CreatePrescriptionUseCaseTests` (5) : création persistée + round-trip des champs ; pas de normalisation des champs optionnels ; commande nulle → `ArgumentNullException` ; constructeur sans repository / sans `IUnitOfWork` → throw.
- `UpdatePrescriptionUseCaseTests` (6) : mise à jour persistée ; introuvable → `PrescriptionFound = false` sans écriture ; préservation `CustomerId` + `CreatedAt` ; commande nulle → throw ; 2 gardes constructeur.
- `DeletePrescriptionUseCaseTests` (5) : suppression persistée ; introuvable → `PrescriptionFound = false` sans écriture (survivant conservé) ; commande nulle → throw ; 2 gardes constructeur.

**App (créés, spies/mocks) — 18 tests :**
- `PrescriptionFormViewModelTests` (7) : délégation create + événement ; délégation update (édition) ; update introuvable → message, pas d'événement ; exception use case → `ErrorMessage` (format existant) ; formulaire invalide → aucune délégation ; 2 gardes constructeur.
- `CustomerPrescriptionsViewModelTests` (7) : délégation delete + mapping `PrescriptionId` ; introuvable → message ; exception → `ErrorMessage` ; 4 gardes constructeur.
- `PrescriptionDetailViewModelTests` (4) : construction sans dépendance de persistance ; `DeleteCommand`/`EditCommand` émettent l'événement avec l'ordonnance courante ; pas d'émission sans ordonnance.

**App (adapté, nécessité technique) :**
- `SaleFormViewModelTransactionTests.cs` : l'appel `new CustomerDetailViewModel(...)` (chaîne de production) reçoit les 3 nouveaux use cases (`Mock.Of<…>()`) pour rester compilable. Aucune assertion modifiée.

## 11. Réduction d'allowlist

Fichier : `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`.

| Allowlist | Avant (P2C-3) | Après (P2C-4) | Entrées retirées |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 50 | **48** | `PrescriptionFormViewModel -> IPrescriptionRepository`, `PrescriptionDetailViewModel -> IPrescriptionRepository` |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 19 | **17** | `PrescriptionFormViewModel -> IUnitOfWork`, `CustomerPrescriptionsViewModel -> IUnitOfWork` |
| `AllowedPublicPersistenceProperties` | 0 | **0** | — |
| `AllowedCodeBehindPersistenceFiles` | 2 | **2** | — |
| **Total** | **71** | **67** | **−4** |

`CustomerPrescriptionsViewModel -> IPrescriptionRepository` **conservé** (lecture réelle). La garde « stale allowlist » valide la réduction : les 5 tests de `AppUiPersistenceGuardrailTests` sont verts (aucune entrée retirée n'est encore une violation réelle, aucune nouvelle violation).

## 12. Fichiers modifiés / créés

**Créés (src) :**
- `src/MMV.Application/UseCases/Prescriptions/CreatePrescription/{CreatePrescriptionCommand,CreatePrescriptionResult,ICreatePrescriptionUseCase,CreatePrescriptionUseCase}.cs`
- `src/MMV.Application/UseCases/Prescriptions/UpdatePrescription/{UpdatePrescriptionCommand,UpdatePrescriptionResult,IUpdatePrescriptionUseCase,UpdatePrescriptionUseCase}.cs`
- `src/MMV.Application/UseCases/Prescriptions/DeletePrescription/{DeletePrescriptionCommand,DeletePrescriptionResult,IDeletePrescriptionUseCase,DeletePrescriptionUseCase}.cs`

**Créés (tests) :**
- `tests/MMV.Application.Tests/UseCases/Prescriptions/{Create,Update,Delete}PrescriptionUseCaseTests.cs`
- `tests/MMV.App.Tests/ViewModels/{PrescriptionFormViewModel,CustomerPrescriptionsViewModel,PrescriptionDetailViewModel}Tests.cs`

**Créés (docs) :**
- `docs/implementation/P2C-4-report.md` (ce rapport)

**Modifiés :**
- `src/MMV.Application/DependencyInjection.cs`
- `src/MMV.App/ViewModels/PrescriptionFormViewModel.cs`
- `src/MMV.App/ViewModels/PrescriptionDetailViewModel.cs`
- `src/MMV.App/ViewModels/CustomerPrescriptionsViewModel.cs`
- `src/MMV.App/ViewModels/CustomerDetailViewModel.cs`
- `src/MMV.App/ViewModels/CustomersViewModel.cs`
- `tests/MMV.App.Tests/Architecture/AppUiPersistenceGuardrailTests.cs`
- `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` (adaptation de constructeur, nécessité technique)

Tous dans le périmètre des fichiers autorisés (§18 du brief, dont `MainWindowViewModel.cs`/`CustomersViewModelTests.cs` autorisés mais non requis ; `SaleFormViewModelTransactionTests.cs` adapté par nécessité technique de compilation).

## 13. Contrôles exécutés

```
git branch --show-current
git status --short
git log -8 --oneline
git diff --check
git diff --stat
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list src/MMV.Application/MMV.Application.csproj reference
dotnet list src/MMV.Application/MMV.Application.csproj package
```

## 14. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| build | vert | **vert** (0 erreur, 1 warning pré-existant `OrderFormViewModel` CS1998, hors périmètre ; aucun nouveau warning) |
| tests | > 429 | **463** (App 161 + Application 79 + Domain 223) |
| vulnérabilités | 0 | **0** |
| `has-pending-model-changes` | false | **false** |
| `MMV.Application` reference | Domain seul | **Domain seul** |
| `MMV.Application` package | DI.Abstractions seul | **DI.Abstractions 8.0.1 seul** |
| allowlist | diminuée | **71 → 67 (−4)** |
| `git diff --check` | propre | **propre** (seul un avis LF→CRLF, pas une erreur d'espaces) |
| jetons de persistance dans les VM ordonnance d'écriture | aucun | **aucun** (`CreateAsync`/`UpdateAsync`/`DeleteAsync`/`CommitAsync`/`SaveChangesAsync`/`IUnitOfWork` retirés) |

Détail des tests ajoutés : **+34** (App +18, Application +16, Domain +0).

## 15. Migrations créées ou non

**Aucune migration créée.** Aucun modèle EF modifié (`has-pending-model-changes = false`). Aucune entité Domain modifiée. Aucun repository Infrastructure modifié.

## 16. Validation CI distante

Commit applicatif `f52f4cd` poussé sur `p2c-ui-cleanup` (`da45c3d..f52f4cd`).

| Champ | Valeur |
|---|---|
| Lien du run | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/27584443523 |
| Identifiant du run | 27584443523 (CI #53) |
| Commit testé | `f52f4cdf018740f2debd0280c9f5cacb53237058` |
| Branche testée | `p2c-ui-cleanup` |
| Event | push |
| Workflow | CI (Restore / Build / Test / Scan) |
| Créé le | 2026-06-15T23:59:05Z |
| Complété le | 2026-06-16T00:02:16Z |

| Étape CI | Résultat |
|---|---|
| Restore | **success** |
| Build | **success** |
| Test | **success** (463 tests) |
| Audit NuGet (`Audit des packages vulnérables`) | **success** (0 vulnérabilité) |
| Restore .NET tools | **success** (`dotnet-ef` 8.0.27) |
| Check EF Core pending model changes | **success** (false) |
| **Statut final du workflow** | **completed / success** |

## 17. Risques résiduels

- **Changement de comportement assumé (validé) :** l'édition d'ordonnance effectue désormais un vrai `UPDATE` (plus de doublon). C'est une correction de bug latent, conforme à l'intention de l'UI et au choix explicite du demandeur ; à signaler en revue.
- **`CustomerPrescriptionsViewModel` dépend encore de `IPrescriptionRepository`** (lecture `LoadPrescriptionsAsync`) : 1 entrée d'allowlist conservée (réelle). Dette → query use cases / port de lecture.
- **`CustomerDetailViewModel -> IUnitOfWork`** conservé en allowlist : paramètre de constructeur maintenu (hors périmètre « fiche client »), désormais inutilisé en interne. À extraire dans une phase ultérieure.
- **`async void` UI** (`SavePrescriptionAsync` via `RelayCommand`, `OnPrescriptionSaved`, `OnDeleteRequested`) conservés (contrainte MVVM) : comportement inchangé, exceptions captées → `ErrorMessage`.
- **Absence de confirmation utilisateur** sur la suppression : comportement d'origine inchangé (TODO conservé).
- **Bug latent connexe non corrigé (hors périmètre) :** `LoadPrescription` fait `new DateTimeOffset(prescription.IssueDate)` ; sur une `IssueDate` à `DateTime.MinValue` et un fuseau UTC+, cela lève `ArgumentOutOfRangeException`. Sans effet sur les données réelles (les ordonnances ont une date réelle) ; non traité ici pour rester iso-fonctionnel.

## 18. Verdict

**P2C-4 = GO DÉFINITIF COMPLET.**

Critères d'acceptation satisfaits : `CreatePrescriptionUseCase`, `UpdatePrescriptionUseCase`, `DeletePrescriptionUseCase` créés et enregistrés en DI Application ; les ViewModels d'ordonnance ne font plus de `Create/Update/Delete/Commit/SaveChanges` direct pour les écritures traitées ; tests Application ajoutés (SQLite réel, 16) ; tests App ajoutés (délégation + garde-fous, 18) ; `AppUiPersistenceGuardrailTests` verts ; allowlist réduite (71 → 67, −4) ; build vert ; **463 tests verts** ; 0 vulnérabilité ; `has-pending-model-changes = false` ; aucune migration ; aucun modèle EF modifié ; rapport P2C-4 créé. Commit `f52f4cd` poussé sur `p2c-ui-cleanup`. CI GitHub Actions run #53 (`27584443523`) : **completed / success**.

## 19. Prochaine étape candidate

- **Reliquat lecture ordonnance / client** : migrer `LoadPrescriptionsAsync` (et lectures client) vers des **query use cases**, ce qui retirerait `CustomerPrescriptionsViewModel -> IPrescriptionRepository` et les dernières dépendances `ICustomerRepository`/`IUnitOfWork` des VM clients (dont `CustomerDetailViewModel -> IUnitOfWork`).
- **P2C-5 — module suivant** selon la roadmap (ex. Produits / Stock, ou Notifications), en continuant à ne traiter que les écritures existantes et à réduire l'allowlist.
