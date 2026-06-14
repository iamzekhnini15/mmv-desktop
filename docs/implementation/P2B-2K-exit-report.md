# P2B-2K — Bilan de sortie P2B + cadrage P2C

> Phase **d'audit et de documentation uniquement**. Aucun code métier, ViewModel, use case,
> migration, DbContext, repository, test existant ou workflow CI n'a été modifié. Seuls deux
> documents ont été créés (ce rapport + la feuille de route P2C). Le dépôt réel a primé sur
> les rapports antérieurs.

## 1. Paramètres reçus

| Paramètre | Valeur |
|-----------|--------|
| TARGET_PHASE_ID | P2B-2K |
| EXECUTION_MODE | ANALYZE_AND_DOCUMENT |
| ALLOW_COMMIT | false |
| ALLOW_PUSH | false |

## 2. Prérequis

P2B-2J devait être verrouillée définitivement avant tout audit de sortie. **Vérifié :**

| Contrôle prérequis | Résultat |
|--------------------|----------|
| Dernier commit P2B-2J présent | ✅ `a3ef996 docs(P2B-2J)` + `a42bb1b refactor(P2B-2J)` |
| Rapport P2B-2J présent | ✅ `docs/implementation/P2B-2J-report.md` |
| Run CI distant P2B-2J vert | ✅ run `27472724654` — completed/success, 398 tests (cf. rapport P2B-2J §20) |
| Branche `p2b-architecture` propre | ✅ working tree clean |
| Local = remote | ✅ `git rev-list --left-right --count origin/p2b-architecture...HEAD` → `0  0` |

→ **P2B-2J = GO DÉFINITIF confirmé.** Audit de sortie autorisé.

## 3. État Git initial

- Branche : `p2b-architecture`
- `git status --short` : propre
- Local et distant synchronisés (0 ahead / 0 behind)
- Dernier commit : `a3ef996 docs(P2B-2J): record dependency cleanup CI validation`

## 4. Baseline (audit non destructif)

| Contrôle | Résultat |
|----------|----------|
| `dotnet --version` | **8.0.417** |
| `dotnet restore MMV.sln` | OK (up-to-date) |
| `dotnet build MMV.sln --no-restore -c Debug` | **Build succeeded** — 0 error, 1 warning (CS1998 préexistant, hors périmètre) |
| `dotnet test MMV.sln --no-build -c Debug` | **398 tests OK** (App 126 / Application 49 / Domain 223), 0 échec |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | **0 vulnérabilité** (7 projets) |
| `dotnet tool restore` | OK (dotnet-ef 8.0.27) |
| `dotnet ef migrations has-pending-model-changes` | **false** (« No changes… ») |
| `dotnet list src/MMV.Application reference` | **uniquement** `..\MMV.Domain\MMV.Domain.csproj` |
| `dotnet list src/MMV.Application package` | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` uniquement |

→ **Baseline verte.** Aucune divergence avec le rapport P2B-2J.

## 5. État des phases P2B

| Phase | Objectif | Commit principal | Commit doc | Tests | Statut |
|-------|----------|------------------|-----------|-------|--------|
| P2A (0A→1F) | Étape 0 : cycle SQLite, transactions, stock atomique, numérotation, seeding gouverné | (série P2A) | série `docs(P2A-*)` | série | GO définitif |
| P2B-2A | Définition des frontières Application (ADR) | `19ace13` | `0fca9ae` | — | GO |
| P2B-2A-CI / CI-R2 | Déclenchement CI sur branches de phase + check EF pending | `3042e52`, `32bed23` | `0815bcf`, `0fca9ae` | — | GO |
| P2B-2B | Squelette couche Application (`AddApplication` à vide) | `7c3324c` | `d6050d4` | tests d'archi différés | GO |
| P2B-2C | Extraction « Enregistrer une vente » → `RegisterSaleUseCase` | `889e663` | `fd3e33b` | +archi | GO |
| P2B-2D | Extraction « Créer commande fournisseur » → `CreateOrderUseCase` | `59a782b` | `975067d` | +Application | GO |
| P2B-2E | Extraction « Avancement statut commande » → `AdvanceOrderStatusUseCase` | `0a76bb9` | `bfa1cff` | +Application | GO |
| P2B-2F | Extraction « Mouvement de stock manuel » → `CreateStockMovementUseCase` | `fcaad91` | `a67aa4d` | +Application | GO |
| P2B-2G | Extraction « Encaisser solde commande » → `SettleOrderBalanceUseCase` | `e683bc3` | `fa8c4e4` | +Application | GO |
| P2B-2H | Extraction « Supprimer commande » → `DeleteOrderUseCase` | `871fa1a` | `a471bab` | +Application | GO |
| P2B-2I | Extraction « Modifier commande » → `UpdateOrderUseCase` | `3d784e4` | `4c69d54` | +Application | GO |
| P2B-2J | Nettoyage dépendances mortes ViewModels + composition root | `a42bb1b` | `a3ef996` | **398** (App 126 / Appli 49 / Domain 223) | GO définitif (CI run 27472724654) |

Toutes les phases attendues sont en GO. Test count actuel confirmé localement : **398**.

## 6. Flux extraits de l'UI (use cases)

Les 7 use cases attendus existent dans `src/MMV.Application/UseCases/` avec la structure
complète (Command / Result / Interface / UseCase) **et** sont enregistrés en `Scoped` dans
`AddApplication` (`src/MMV.Application/DependencyInjection.cs`) :

| Use case | Command | Result | Interface | Impl. | Enregistré DI |
|----------|---------|--------|-----------|-------|---------------|
| `RegisterSaleUseCase` | ✅ (+LineCommand) | ✅ | ✅ | ✅ | ✅ ligne 42 |
| `CreateOrderUseCase` | ✅ (+LineCommand) | ✅ | ✅ | ✅ | ✅ ligne 46 |
| `AdvanceOrderStatusUseCase` | ✅ | ✅ | ✅ | ✅ | ✅ ligne 51 |
| `CreateStockMovementUseCase` | ✅ | ✅ | ✅ | ✅ | ✅ ligne 57 |
| `SettleOrderBalanceUseCase` | ✅ | ✅ | ✅ | ✅ | ✅ ligne 63 |
| `DeleteOrderUseCase` | ✅ | ✅ | ✅ | ✅ | ✅ ligne 68 |
| `UpdateOrderUseCase` | ✅ (+LineCommand) | ✅ | ✅ | ✅ | ✅ ligne 74 |

Couverture de tests : 49 tests `MMV.Application.Tests` + tests de délégation côté ViewModel
(App.Tests). `AddApplication` est **appelée** par le composition root (`App.axaml.cs` ligne 147).

## 7. Pureté de MMV.Application

| Invariant | Constat |
|-----------|---------|
| Référence projet | **uniquement** `MMV.Domain` |
| Package | **uniquement** `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` |
| Référence `MMV.App` | ❌ absente |
| Référence `MMV.Infrastructure` | ❌ absente |
| Référence Avalonia | ❌ absente |
| Référence EF Core | ❌ absente |
| Référence SQLite | ❌ absente |
| `DbContext` / code UI dans Application | ❌ absent |

Verrouillé par `ApplicationArchitectureTests` (6 faits par réflexion sur les assemblies
référencés). **MMV.Application est pure.**

## 8. Persistance restante dans l'UI

Recherche `IUnitOfWork|DbContext|SaveChangesAsync|CreateAsync|UpdateAsync|DeleteAsync|GetByIdAsync|GetAllAsync|Repository`
dans `src/MMV.App` → **338 occurrences / 35 fichiers**. Recherche ciblée des opérations
d'écriture/lecture persistante → **61 occurrences / 20 ViewModels**.

### A. Persistance directe bloquante pour P2C

| Fichier | Constat |
|---------|---------|
| `Views/Clients/CustomerFormView.axaml.cs` | `Repository.CreateAsync` / `UpdateAsync` + `UnitOfWork.SaveChangesAsync` **dans le code-behind** |
| `Views/Clients/CustomersView.axaml.cs` | idem (création / mise à jour / save dans le code-behind) |
| `CustomersListViewModel.cs` | expose **publiquement** `Repository` et `UnitOfWork` ; `DeleteAsync` + `SaveChangesAsync` |
| `CustomerFormViewModel`, `CustomerPrescriptionsViewModel`, `PrescriptionFormViewModel` | écritures clients/ordonnances directes |
| `ProductFormViewModel` (7), `ProductsListViewModel` (3) | écritures produits directes |
| `InventoryViewModel` (5), `StockMovementsListViewModel` (2) | lectures/écritures stock |
| `SupplierFormViewModel` (4), `SuppliersListViewModel`, `SuppliersViewModel` | écritures fournisseurs |
| `UserFormViewModel` (4), `UsersListViewModel` (3) | écritures utilisateurs |
| `NotificationsListViewModel` (6) | persistance notifications |

### B. Dépendance temporaire encore acceptée (lectures d'écran déjà documentées en P2B)

| Fichier | Constat |
|---------|---------|
| `SaleFormViewModel` | lectures produit/ordonnance pour initialiser l'écran (volontairement conservées, P2B-2C) |
| `OrderFormViewModel` (2), `OrdersViewModel` (2) | lectures (listes déroulantes, rechargement détail) ; flux d'écriture déjà délégués aux use cases |
| `StockMovementFormViewModel` (1) | lecture produits ; écriture déléguée à `CreateStockMovementUseCase` |
| `OrderKanbanViewModel` (3) | lecture/déplacement de cartes ; à migrer en P2C-2 |
| `MainWindowViewModel` (4) | alertes stock (lecture pour affichage) |

### C. Faux positifs / composition root / affichage

| Fichier | Constat |
|---------|---------|
| `App.axaml.cs` (22) | **composition root** : enregistrement DbContext + repositories + résolution `INotificationRepository`/`IProductRepository`/`IUnitOfWork` pour `MainWindow` (acceptable comme amorçage) |
| `MainWindow.axaml.cs` (4) | passe les ports d'alerte stock au `MainWindowViewModel` (affichage) |

`rg "MMV.Infrastructure" src/MMV.App` → uniquement `App.axaml.cs` (usings de la composition
root) + la `ProjectReference` du `.csproj`. **Aucune fuite Infrastructure ailleurs dans l'UI.**

## 9. Code-behind restant à nettoyer

`rg "SaveChangesAsync|CreateAsync|UpdateAsync|DeleteAsync|IUnitOfWork|Repository|DbContext" -g "*.axaml.cs"` :

- **`CustomerFormView.axaml.cs`** et **`CustomersView.axaml.cs`** : persistance directe
  (`CreateAsync` / `UpdateAsync` / `SaveChangesAsync`) — **à nettoyer en priorité (P2C-3)**.
- `MainWindow.axaml.cs` : transmet des ports au ViewModel (pas de persistance directe ;
  construction d'objet) — acceptable, à revoir en P2C-7/8.
- `App.axaml.cs` : composition root (hors « code-behind métier »).

Cible P2C : code-behind = affichage / événements UI uniquement.

## 10. Domain services à reclasser

`rg "IUnitOfWork|Repository|SaveChangesAsync" src/MMV.Domain` → les services suivants injectent
`IUnitOfWork` et font `CreateAsync`/`UpdateAsync`/`DeleteAsync` + `SaveChangesAsync` :

| Service Domain | Nature réelle | Consommé ? | Classement |
|----------------|---------------|-----------|------------|
| `CustomerService` | orchestration CRUD + transaction | enregistré **uniquement** dans `AddInfrastructure` (inerte), consommé **nulle part** | **Applicatif** → migrer vers Application **ou** supprimer (P2C-8) |
| `ProductService` | idem | idem | **Applicatif** → P2C-8 |
| `PrescriptionService` | idem | idem | **Applicatif** → P2C-8 |
| `OrderService` | idem | idem | **Applicatif** → P2C-8 |
| `SaleService` | idem | idem | **Applicatif** → P2C-8 |

À **conserver dans Domain** (logique pure, pas de persistance applicative) : interfaces de
ports (`Interfaces/Repositories/*`, `Interfaces/Persistence/IStockMutationService`) — ce sont
des contrats, pas des services applicatifs. Les vrais services métier purs (calculs, règles)
restent en Domain.

> Constat clé : ces 5 services « ressemblent à des services Application » et sont **morts**
> (registrés seulement par le `AddInfrastructure` inerte). Décision reportée à P2C-8, **non
> traitée ici** (interdiction de modifier le code en P2B-2K).

## 11. Composition root

| Élément | Constat |
|---------|---------|
| Point unique | `MMV.App/App.axaml.cs` → `ConfigureServices()` |
| `AddApplication` | **appelée** (ligne 147) — 7 use cases en Scoped |
| `AddInfrastructure` | **NON appelée** — inerte, doublon documenté ; `rg AddInfrastructure` → seulement sa définition + docs |
| Enregistrements directs dans App | DbContext, 10 repositories + `IUnitOfWork`, `ITransactionRunner`, `IStockMutationService`, `INumberSequenceService`, services Auth/Session/Permission/Theme/Navigation/Dialog, ViewModels |
| Services enregistrés mais non consommés | les 5 Domain services (uniquement via `AddInfrastructure` inerte) ; `AddInfrastructure` lui-même |
| `MMV.App → MMV.Infrastructure` | référence projet nécessaire pour l'amorçage (confiné à `App.axaml.cs`) |

Choix P2B-2K : **documenter, ne pas nettoyer** (conforme aux interdictions). Assainissement
(unifier la DI, décider d'`AddInfrastructure`, confiner le couplage Infrastructure) → **P2C-8**.

## 12. Tests d'architecture : existants et manquants

### Existants

| Test | Couverture |
|------|------------|
| `ApplicationArchitectureTests` (Application.Tests) | pureté de `MMV.Application` (pas d'Infra/App/Avalonia/EF/SQLite ; dépend de Domain) |
| `OrderViewModelDependencyHygieneTests` (App.Tests) | absence de ports de persistance dans les constructeurs de `OrderDetailViewModel` / `OrdersViewModel` |

### Manquants — à **proposer** pour P2C-1 (non créés ici)

- Aucun ViewModel ne doit dépendre de `IUnitOfWork` (réflexion sur tous les constructeurs de VM).
- Aucun ViewModel ne doit dépendre d'un `I…Repository`.
- Aucun ViewModel ne doit exposer publiquement un repository / `IUnitOfWork`
  (cas actuel : `CustomersListViewModel.Repository` / `.UnitOfWork`).
- Le code-behind (`*.axaml.cs`) ne doit pas référencer repository / `IUnitOfWork` / `DbContext`.
- À terme : `MMV.App` ne référence `MMV.Infrastructure` que via un point d'amorçage isolé.

## 13. Dettes résiduelles (reportées vers P2C)

1. Persistance directe dans 20 ViewModels + 2 code-behind (clients) — **flux CRUD non transactionnels**.
2. `CustomersListViewModel` expose `Repository`/`UnitOfWork` publiquement.
3. 5 Domain services orphelins de nature applicative (morts, registrés via `AddInfrastructure` inerte).
4. `AddInfrastructure` inerte (doublon DI jamais appelé).
5. Couplage `MMV.App → MMV.Infrastructure` à confiner.
6. Lectures d'affichage encore servies par repositories (cible : query use cases).
7. Warning préexistant **CS1998** (`OrderFormViewModel.LoadExistingOrderAsync`, ligne 464) — hors périmètre.

Toutes ces dettes sont **identifiées et reportées** dans
[`docs/architecture/P2C-ui-application-cleanup-roadmap.md`](../architecture/P2C-ui-application-cleanup-roadmap.md).

## 14. Décision de sortie P2B

Critères de **GO sortie** :

| Critère | État |
|---------|------|
| Couche Application créée | ✅ |
| Principaux flux Commandes / Ventes / Stock extraits | ✅ (7 use cases) |
| Tests verts | ✅ 398 |
| CI verte | ✅ (P2B-2J run 27472724654) |
| Pas de migration pending | ✅ `has-pending-model-changes = false` |
| Architecture Application propre | ✅ (pureté vérifiée + test d'archi) |
| Dettes restantes identifiées et reportées vers P2C | ✅ (roadmap créée) |

Aucun critère de NO-GO présent (CI rouge / tests rouges / Application impure / migrations
pending / P2B-2J non validée / roadmap impossible). → **P2B = GO SORTIE.**

## 15. Roadmap P2C

Créée : [`docs/architecture/P2C-ui-application-cleanup-roadmap.md`](../architecture/P2C-ui-application-cleanup-roadmap.md).
Séquence : P2C-0 (merge P2B) → P2C-1 (garde-fous) → P2C-2 (Kanban) → P2C-3 (Clients) →
P2C-4 (Ordonnances) → P2C-5 (Produits) → P2C-6 (Inventaire) → P2C-7 (Fournisseurs/Users/
Notifications) → P2C-8 (composition root + Domain services + `AddInfrastructure`) →
P2C-9 (bilan UI sans persistance).

## 16. Contrôles exécutés

```
git status --short
git branch --show-current
git log -10 --oneline
git rev-list --left-right --count origin/p2b-architecture...HEAD
dotnet --version
dotnet restore MMV.sln
dotnet build MMV.sln --no-restore -c Debug
dotnet test MMV.sln --no-build -c Debug
dotnet list MMV.sln package --vulnerable --include-transitive
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
dotnet list src/MMV.Application/MMV.Application.csproj reference
dotnet list src/MMV.Application/MMV.Application.csproj package
rg "IUnitOfWork|DbContext|SaveChangesAsync|CreateAsync|UpdateAsync|DeleteAsync|GetByIdAsync|GetAllAsync|Repository" src/MMV.App
rg "MMV.Infrastructure" src/MMV.App
rg "...|Repository|SaveChangesAsync|DbContext" src/MMV.Domain
rg "SaveChangesAsync|CreateAsync|UpdateAsync|DeleteAsync|IUnitOfWork|Repository|DbContext" src/MMV.App -g "*.axaml.cs"
rg "AddInfrastructure" (dépôt)
```

## 17. Résultats

- Build vert ; **398 tests verts** ; 0 vulnérabilité ; `has-pending-model-changes = false` ;
  aucune migration pending.
- `MMV.Application` pure (référence uniquement `MMV.Domain`).
- 7 use cases extraits, structurés et enregistrés.
- Persistance UI restante inventoriée (20 ViewModels + 2 code-behind clients).
- Domain services orphelins identifiés (5).
- Composition root documentée ; `AddInfrastructure` inerte confirmé.
- Tests d'architecture existants recensés ; tests garde-fous P2C proposés.

## 18. Risques

- **Faible.** Aucun code modifié : aucun risque de régression introduit par P2B-2K.
- Dette principale restante = persistance CRUD dans l'UI (clients/produits/ordonnances/
  fournisseurs/utilisateurs) — non transactionnelle, donc migration P2C à risque modéré.
- `OrderKanbanViewModel` et les lectures d'affichage nécessiteront des query use cases
  (décision de design à prendre en P2C-2).
- Suppression vs rebranchement d'`AddInfrastructure` et des Domain services orphelins :
  décision structurante à arbitrer en P2C-8 (impact composition root).
- Hors périmètre et non abordés : Payment / Invoice / Quote / Money / TVA / Belgique / Maroc /
  SaaS / multi-Store / Organization.

## 19. Verdict GO/NO-GO

**P2B-2K = GO.**

- Aucun code métier / ViewModel / use case / migration / test / CI modifié.
- Baseline verte ; audit P2B complet ; persistance UI inventoriée ; code-behind audité ;
  Domain services audités ; composition root documentée ; tests d'architecture audités.
- Roadmap P2C créée ; rapport P2B-2K créé ; décision de sortie claire.

**Décision de sortie : P2B = GO SORTIE** (fermable, prête pour préparation de merge vers `main`).

## 20. Prochaine étape candidate

**P2C-0** — finaliser / préparer le merge de `p2b-architecture` vers `main` (P2B validée),
puis **P2C-1** — créer les tests garde-fous « UI sans persistance » avant toute migration de
flux. (Arbitrage hors de cette phase.)

> Fin de P2B-2K. Aucun commit, aucun push. Ne pas commencer P2C.
