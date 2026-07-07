# Rapport P2D-5 — Clients / Ordonnances : lectures UI → Query Use Cases — VERDICT : **GO PARTIEL (local)**

> Extraction des **lectures** UI du module Clients / Ordonnances vers des *query use cases* Application renvoyant des
> **DTO applicatifs plats** (jamais d'entité EF suivie). Aucun commit, aucun push (conforme à `ALLOW_COMMIT = false`
> / `ALLOW_PUSH = false`).

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P2D-5 |
| `EXECUTION_MODE` | IMPLEMENT |
| `SOURCE_BRANCH` | p2d-query-cleanup |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : **`p2d-query-cleanup`** ✓
- Working tree : **propre** ✓
- Présence de `e81f99b feat(P2D): migrate simple read modules to query use cases` ✓
- Présence de `37067cc docs(P2D): record partial query cleanup CI validation` ✓
- Présence de `6592d46 feat(P2D-4): migrate stock reads to query use cases` ✓
- Présence de `d11b45c docs(P2D-4): record product stock query cleanup CI validation` ✓
- Précondition = **GO** (aucun déclencheur STOP).

## 3. Baseline (avant modification)

| Contrôle | Résultat |
|---|---|
| `dotnet build MMV.sln -c Debug` | ✅ vert (0 erreur ; 1 warning préexistant CS1998 `OrderFormViewModel`) |
| `dotnet test MMV.sln` | ✅ **529** (App 177 · Application 129 · Domain 223) |
| `dotnet list … --vulnerable --include-transitive` | ✅ 0 vulnérabilité |
| `ef migrations has-pending-model-changes` | ✅ `false` |
| `MMV.Application` references | ✅ `MMV.Domain` seul |
| `MMV.Application` packages | ✅ `Microsoft.Extensions.DependencyInjection.Abstractions` seul |

Baseline = **GO**.

## 4. Inventaire Clients / Ordonnances avant extraction

Périmètre scanné : `src/MMV.App/ViewModels` (Clients / Ordonnances) + Views associées. Dépendances repository en
constructeur, **exclusivement des lectures d'affichage** :

| ViewModel | Repo injecté | Méthode | Données consommées par l'écran | Entité Domain exposée | Query use case cible | DTO cible | Risque | Action |
|---|---|---|---|---|---|---|---|---|
| `CustomerPurchaseHistoryViewModel` | `ISaleRepository` | `GetByCustomerIdAsync` | historique ventes (date, n°, statut, livraison est., final, acompte, restant) | `ObservableCollection<Sale>` | `IGetCustomerPurchaseHistoryUseCase` | `CustomerSaleItemDto` | faible | **migré** |
| `CustomerInfoViewModel` | `ISaleRepository` | `GetByCustomerIdAsync` | onglet Infos : ventes (date, n°, statut, final) | `ObservableCollection<Sale>` | `IGetCustomerPurchaseHistoryUseCase` (réutilisé) | `CustomerSaleItemDto` | faible | **migré** |
| `CustomerPrescriptionsViewModel` | `IPrescriptionRepository` | `GetByCustomerIdAsync` | liste ordonnances → fiche détail + formulaire d'édition (tous champs OD/OG) | `ObservableCollection<Prescription>` | `IListPrescriptionsByCustomerUseCase` | `PrescriptionListItemDto` | moyen (threadé vers détail + form, mais **vérifié à la compilation**) | **migré** |
| `CustomerDetailViewModel` | `ICustomerRepository` | — | *(dépendance morte — jamais lue)* | non | — | — | faible | **retiré (mort)** |
| `CustomerDetailViewModel` | `ISaleRepository` | — (pass-through) | transmis à Info + Historique | non | (transmet le query use case) | — | faible | **migré** |
| `CustomerDetailViewModel` | `IPrescriptionRepository` | — (pass-through) | transmis à `SaleFormViewModel` **et** aux ordonnances | non | (transmet le query use case ordonnances) | — | élevé (SaleForm = P2D-6) | **conservé (SaleForm)** |
| `CustomerDetailViewModel` | `IProductRepository` | — (pass-through) | transmis à `SaleFormViewModel` | non | *(P2D-6 / Ventes)* | — | élevé | **conservé (SaleForm)** |
| `CustomersViewModel` | `ISaleRepository` | — (pass-through) | transmis à la fiche détail | non | (transmet le query use case) | — | faible | **migré** |
| `CustomersViewModel` | `ICustomerRepository` | — (pass-through) | transmis à la **liste** clients | non | *(reliquat)* | — | élevé | **reporté** |
| `CustomersViewModel` | `IPrescriptionRepository` / `IProductRepository` | — (pass-through) | transmis à la fiche → `SaleFormViewModel` | non | *(P2D-6 / Ventes)* | — | élevé | **conservé (SaleForm)** |
| `CustomersListViewModel` | `ICustomerRepository` | `GetAllAsync` | liste clients (nom, contact, ville, email, date) → **formulaire d'ÉDITION** + fiche détail via événements typés `Customer` | `ObservableCollection<Customer>` | *(reliquat)* | *(DTO à threader dans le form d'édition)* | **élevé** | **reporté** |

`PrescriptionDetailViewModel` et `PrescriptionFormViewModel` **n'ont aucune dépendance repository** (hors garde-fou) :
ils portaient l'entité `Prescription` pour l'affichage / le pré-remplissage. Ils ont été alignés sur le DTO
(`PrescriptionListItemDto`) dans le cadre du flux de lecture migré (aucune écriture modifiée).

## 5. Stratégie appliquée

**Déplacement iso-fonctionnel**, une lecture à la fois, compilation après chaque groupe. Pour chaque écran migré :
la lecture directe `I…Repository` est remplacée par un *query use case* Application qui **projette** l'entité vers un
**DTO plat** (`init`-only, sans navigation EF) ; recherche / filtre / tri **restent en présentation** (iso-fonctionnel,
le tri décroissant par date est de toute façon déjà porté par le repository). **Parité de noms** DTO ⇄ liaisons XAML.

**Décision de périmètre (GO PARTIEL).** Deux familles de lectures ont été migrées :

1. **Historique d'achats (ventes)** — projections **plates, uniquement d'affichage** (`DataGrid` en liaisons par
   réflexion sur colonnes). Migrées intégralement : `CustomerPurchaseHistoryViewModel` **et** l'onglet Infos de
   `CustomerInfoViewModel` réutilisent un seul query use case / DTO (`CustomerSaleItemDto`).
2. **Liste des ordonnances d'un client** — la liste alimente la fiche détail (`PrescriptionDetailView`, liaisons
   **compilées** `CurrentPrescription.*`) et le formulaire d'édition (`PrescriptionFormViewModel.LoadPrescription`).
   Migration possible en iso-fonctionnel car `Prescription` est une entité **plate** (aucune navigation consommée) : le
   DTO `PrescriptionListItemDto` porte tous les champs optométriques. Le threading DTO → détail + formulaire est
   **vérifié par le compilateur** (liaisons compilées + casts code-behind typés), ce qui borne le risque de régression
   d'affichage. Le flux de **sauvegarde** du formulaire est resté strictement inchangé (reconstruction d'un snapshot
   depuis les propriétés du formulaire, indépendante de la charge chargée).

**Reportés (reliquat P2D-5 justifié, clause §17/§19).**
- **Liste clients** (`CustomersListViewModel` / `CustomersViewModel → ICustomerRepository`) : l'entité `Customer` circule
  via des événements typés vers le **formulaire d'ÉDITION** (`CustomerFormViewModel.InitializeForEdit(Customer)`, qui
  stocke `_originalCustomer` et le réutilise dans son chemin de **sauvegarde**) et vers la fiche détail. Migrer imposerait
  de threader un DTO à travers un formulaire d'écriture — **non validable en iso-fonctionnel sans exécution UI de recette**
  (Avalonia non exécutable ici) — même clause d'échappement que le reliquat liste produit P2D-4.
- **Lectures de référence du formulaire de vente** (`SaleFormViewModel` via la fiche : `IProductRepository`,
  `IPrescriptionRepository`) : hors périmètre P2D-5 (« Ventes hors historique client »), planifiées en **P2D-6**
  (`GetSaleFormReferenceDataQuery`). `CustomerDetailViewModel` / `CustomersViewModel` conservent donc ces deux ports
  uniquement pour **construire** `SaleFormViewModel`.

**Réutilisation.** L'onglet Infos et l'historique d'achats partagent `IGetCustomerPurchaseHistoryUseCase` +
`CustomerSaleItemDto` : aucun quasi-doublon (DRY, cohérent avec la réutilisation de `SupplierListItemDto` en P2D-4).

## 6. Query use cases créés (2)

| Module (dossier) | Query use case | Sortie | Repository consommé |
|---|---|---|---|
| `Sales/GetCustomerPurchaseHistory` | `IGetCustomerPurchaseHistoryUseCase` | `IReadOnlyList<CustomerSaleItemDto>` | `ISaleRepository` |
| `Prescriptions/ListPrescriptionsByCustomer` | `IListPrescriptionsByCustomerUseCase` | `IReadOnlyList<PrescriptionListItemDto>` | `IPrescriptionRepository` |

Chacun : `…Query.cs` (objet d'entrée portant `CustomerId`, garde « query nulle »), `…Dto.cs`, `I…UseCase.cs`,
`…UseCase.cs`. Enregistrés en **`Scoped`** dans `MMV.Application/DependencyInjection.cs` (même portée qu'`OpticDbContext`
/ repositories). `MMV.Application` reste **pure** (référence `MMV.Domain` seul).

## 7. DTO applicatifs créés (2)

- **`CustomerSaleItemDto`** : `SaleId`, `SaleNumber`, `SaleDate`, `EstimatedDelivery`, `FinalAmount`, `DepositAmount`,
  `RemainingAmount`, `Status` (enum `SaleStatus`).
- **`PrescriptionListItemDto`** : `PrescriptionId`, `CustomerId`, `IssueDate`, `DoctorName`, OD (`OdSphere`, `OdCylinder`,
  `OdAxis`, `OdAddition`, `OdPrismValue`, `OdPrismBase`, `OdVisualAcuity`), OG (idem), `Notes`.

Tous : `sealed`, propriétés `init`-only, aucun type Avalonia / CommunityToolkit, aucune navigation EF, uniquement les
champs consommés par l'écran (le DTO ordonnance porte l'ensemble des champs scalaires car la même instance alimente la
liste, la fiche détail et le pré-remplissage du formulaire). Les enums Domain (`SaleStatus`, `PrismBase`) sont conservées
comme valeurs métier stables (ce ne sont pas des entités).

## 8. ViewModels modifiés (6)

- `CustomerPurchaseHistoryViewModel` → `IGetCustomerPurchaseHistoryUseCase` (retrait `ISaleRepository` ; `Sales` retypé
  `ObservableCollection<CustomerSaleItemDto>`).
- `CustomerInfoViewModel` → `IGetCustomerPurchaseHistoryUseCase?` (retrait `ISaleRepository` ; `Sales` retypé DTO ;
  paramètre entité `Customer` **conservé** pour l'affichage — non repository, hors garde-fou).
- `CustomerPrescriptionsViewModel` → `IListPrescriptionsByCustomerUseCase` (retrait `IPrescriptionRepository` ;
  `Prescriptions` / `SelectedPrescription` et les `RelayCommand<…>` retypés `PrescriptionListItemDto`).
- `PrescriptionDetailViewModel` → `CurrentPrescription` et événements `EditRequested` / `DeleteRequested` retypés
  `PrescriptionListItemDto` (aucune dépendance repository ; alignement sur le DTO).
- `PrescriptionFormViewModel` → `LoadPrescription(PrescriptionListItemDto)` (recopie de champs identiques ; **chemin de
  sauvegarde inchangé**).
- `CustomerDetailViewModel` → retrait `ICustomerRepository` (morte) + `ISaleRepository` ; injection de
  `IGetCustomerPurchaseHistoryUseCase` + `IListPrescriptionsByCustomerUseCase` (transmis aux sous-VM). **Conserve**
  `IPrescriptionRepository` + `IProductRepository` pour `SaleFormViewModel` (P2D-6).
- `CustomersViewModel` → retrait `ISaleRepository` ; injection des 2 query use cases (transmis à la fiche). **Conserve**
  `ICustomerRepository` (liste, reliquat) + `IProductRepository` / `IPrescriptionRepository` (fiche → SaleForm, P2D-6).

## 9. XAML / code-behind

**Aucun `.axaml` modifié.** **1 code-behind modifié** : `CustomerPrescriptionsView.axaml.cs` — le cast
`is Prescription` → `is PrescriptionListItemDto` (adaptation de type de l'élément de liste, exception §10 explicitement
autorisée). Toutes les autres liaisons migrées se résolvent sans changement :
- Historique / Infos : colonnes `DataGrid` liées par **réflexion** sur des DTO à **noms identiques** (`SaleDate`,
  `SaleNumber`, `Status`, `FinalAmount`, `DepositAmount`, `RemainingAmount`, `EstimatedDelivery`).
- Liste ordonnances : `DataTemplate` sans `x:DataType` (réflexion, noms identiques).
- Fiche détail ordonnance : liaisons **compilées** `CurrentPrescription.Od*/Og*/IssueDate/DoctorName/Notes` — recompilées
  et **vérifiées** contre le DTO (mêmes noms/types que l'entité). La compilation verte de `MMV.App` valide ce threading.

## 10. Tests créés / modifiés

- **Créés — Application (vrai SQLite temporaire, jamais InMemory)** :
  `UseCases/Sales/GetCustomerPurchaseHistoryUseCaseTests` (projection DTO, filtre par client, tri décroissant par date,
  cas vide, query nulle, constructeur null) **+4** ; `UseCases/Prescriptions/ListPrescriptionsByCustomerUseCaseTests`
  (projection DTO, portage de tous les champs OD/OG, filtre par client, tri décroissant par date d'émission, cas vide,
  query nulle, constructeur null) **+4**. Total **+8** (129 → 137).
- **Créé — App** : `ViewModels/P2D5CustomerReadViewModelTests` (délégation aux query use cases, remplissage d'état,
  propagation d'erreur, conservation du client, cas « sans use case », tri conservé, constructeur null) **+6** (177 → 183).
- **Adaptés — App** : `CustomerPrescriptionsViewModelTests` (`IPrescriptionRepository` mock → `IListPrescriptionsByCustomerUseCase`,
  `Prescription` → `PrescriptionListItemDto`) ; `PrescriptionDetailViewModelTests` + `PrescriptionFormViewModelTests`
  (entité `Prescription` → DTO pour `CurrentPrescription` / `LoadPrescription`) ; `SaleFormViewModelTransactionTests`
  (nouvelle signature de `CustomerDetailViewModel`).

Total : **543** tests (App 183 · Application 137 · Domain 223) — tous verts.

## 11. Réduction exacte d'allowlist (`AppUiPersistenceGuardrailTests`)

| Allowlist | Avant (P2D-4) | Après (P2D-5) | Δ |
|---|---|---|---|
| `AllowedViewModelRepositoryConstructorDependencies` | 25 | **19** | **−6** |
| `AllowedViewModelUnitOfWorkConstructorDependencies` | 0 | 0 | inchangé |
| `AllowedPublicPersistenceProperties` | 0 | 0 | inchangé |
| `AllowedCodeBehindPersistenceFiles` | 1 (`App.axaml.cs`) | 1 (`App.axaml.cs`) | inchangé |

**6 entrées retirées** : `CustomerDetailViewModel -> ICustomerRepository` (morte), `CustomerDetailViewModel -> ISaleRepository`,
`CustomerInfoViewModel -> ISaleRepository`, `CustomerPrescriptionsViewModel -> IPrescriptionRepository`,
`CustomerPurchaseHistoryViewModel -> ISaleRepository`, `CustomersViewModel -> ISaleRepository`. Aucune entrée ajoutée ;
la règle « l'allowlist ne fait que diminuer » est respectée (assertion anti-allowlist-obsolète verte).

## 12. Fichiers modifiés / créés

- **Créés (source, 8)** : `UseCases/Sales/GetCustomerPurchaseHistory/**` (4), `UseCases/Prescriptions/ListPrescriptionsByCustomer/**` (4).
- **Modifiés (source, 9)** : `MMV.Application/DependencyInjection.cs`, `CustomerPurchaseHistoryViewModel`,
  `CustomerInfoViewModel`, `CustomerPrescriptionsViewModel`, `PrescriptionDetailViewModel`, `PrescriptionFormViewModel`,
  `CustomerDetailViewModel`, `CustomersViewModel`, `Views/Clients/CustomerPrescriptionsView.axaml.cs`.
- **Créés (tests, 3)** : `GetCustomerPurchaseHistoryUseCaseTests`, `ListPrescriptionsByCustomerUseCaseTests`,
  `P2D5CustomerReadViewModelTests`.
- **Modifiés (tests, 5)** : `AppUiPersistenceGuardrailTests.cs`, `CustomerPrescriptionsViewModelTests.cs`,
  `PrescriptionDetailViewModelTests.cs`, `PrescriptionFormViewModelTests.cs`, `SaleFormViewModelTransactionTests.cs`.
- **Docs** : ce rapport ; `docs/architecture/P2D-read-application-roadmap.md` (note d'état P2D-5).

## 13. Contrôles exécutés

`git status --short`, `git diff --stat`, `git diff --check` (propre), `dotnet restore`,
`dotnet build --no-restore -c Debug`, `dotnet test --no-build -c Debug`, `dotnet list … --vulnerable --include-transitive`,
`dotnet tool restore`, `ef migrations has-pending-model-changes`, `dotnet list …Application… reference`,
`dotnet list …Application… package`.

## 14. Résultats

| Contrôle | Attendu | Obtenu |
|---|---|---|
| Build | vert | ✅ 0 erreur |
| Tests | > 529 | ✅ **543** |
| Vulnérabilités | 0 | ✅ 0 |
| `has-pending-model-changes` | false | ✅ false |
| Migration créée | aucune | ✅ aucune |
| Modèle EF modifié | aucun | ✅ aucun |
| `.axaml` modifié | aucun | ✅ aucun |
| Code-behind modifié | justifié | ✅ 1 (cast type liste, §10) |
| `MMV.Application` référence | `MMV.Domain` seul | ✅ |
| `MMV.Application` package | `DI.Abstractions` seul | ✅ |
| Allowlist repository | ↓ | ✅ **19** (−6) |
| Allowlist UoW / propriétés publiques persistance | 0 | ✅ 0 |
| Code-behind persistant | `App.axaml.cs` seul | ✅ |

## 15. Migrations créées ou non

**Aucune migration créée.** Aucune entité Domain / `DbContext` / migration / configuration EF touchée.
`has-pending-model-changes = false`. Écritures (Create/Update/Delete client, Create/Update/Delete ordonnance,
`RegisterSale`) **inchangées**. Pin SQLite (P2C-SEC-1) intact.

## 16. Risques résiduels

1. **Threading DTO ordonnance** vers la fiche détail (liaisons compilées) et le formulaire d'édition : vérifié par le
   compilateur (build vert). Risque résiduel limité aux `StringFormat` d'affichage — identiques car les types de
   propriétés du DTO sont identiques à ceux de l'entité (double? / int? / enum `PrismBase?` / `DateTime`). Non validé par
   recette UI (Avalonia non exécutable ici).
2. **`CustomerInfoViewModel.Customer` reste une entité `Customer`** (affichage de la fiche Infos) : ce n'est pas un
   repository (hors garde-fou) et aucune écriture n'y transite ; alignement sur un DTO client reporté avec la liste
   clients (voir §17).
3. **Reliquats conservés** (liste clients + lectures SaleForm) : la fiche client et la liste continuent de manipuler des
   entités EF via des repositories injectés — invariant P2C préservé (aucune écriture directe, aucun `IUnitOfWork`,
   aucune propriété publique de persistance).

## 17. Repositories restants sur Clients / Ordonnances et justification

**6 dépendances** subsistent (reliquat P2D-5 justifié, verrouillées par l'allowlist qui ne peut que diminuer) :

- `CustomersListViewModel → ICustomerRepository` et `CustomersViewModel → ICustomerRepository` : la **liste clients**
  charge des entités `Customer` qui circulent, via des événements typés, vers le **formulaire d'ÉDITION**
  (`CustomerFormViewModel.InitializeForEdit(Customer)` → `_originalCustomer` réutilisé au **SaveChangesAsync** applicatif)
  et la fiche détail. Migration iso-fonctionnelle impossible sans threader un DTO à travers un formulaire d'écriture,
  **non validable sans exécution UI de recette**. Cible : `SearchCustomersQuery` + `CustomerListItemDto` (+ éventuel DTO
  d'édition), à solder en clôture P2D avec surface d'exécution.
- `CustomerDetailViewModel → IPrescriptionRepository` / `→ IProductRepository` et `CustomersViewModel →
  IPrescriptionRepository` / `→ IProductRepository` : servent **uniquement** à construire `SaleFormViewModel` (lectures de
  référence du formulaire de vente). **Hors périmètre P2D-5** (« Ventes hors historique client ») ; planifiées en
  **P2D-6** (`GetSaleFormReferenceDataQuery`).

## 18. Verdict

**GO PARTIEL (local).** Les lectures **historique d'achats** (ventes du client, deux écrans) et **liste des ordonnances
d'un client** sont sorties vers des *query use cases* Application renvoyant des **DTO plats** ; l'allowlist repository
passe de **25 à 19** (−6), sans jamais grossir. Build vert, **543** tests verts (> 529), 0 vulnérabilité,
`has-pending-model-changes = false`, aucune migration, aucun modèle EF / `.axaml` / Domain / Infrastructure touché
(1 code-behind adapté au cast de type, §10), `MMV.Application` toujours pure, `IUnitOfWork` UI = 0, aucune écriture
directe UI. La **liste clients à threading vers formulaire d'édition** et les **lectures de référence du formulaire de
vente** sont **documentées en reliquat justifié** (6 entrées d'allowlist), conformément aux clauses §17/§19.

## 19. Prochaine étape candidate

**P2D-6 — Commandes / Ventes** : `ListOrdersQuery`, `GetOrdersKanbanQuery`, `GetOrderFormReferenceDataQuery`,
`GetSaleFormReferenceDataQuery` (cette dernière soldera aussi les lectures `SaleFormViewModel` conservées en reliquat
P2D-5). La **liste clients** (reliquat P2D-5) et la **liste produit / fiche** (reliquat P2D-4) restent à solder en
clôture P2D (P2D-7) avec recette UI. Ne pas ouvrir P3 / SaaS / Organization / Store / Subscription.
