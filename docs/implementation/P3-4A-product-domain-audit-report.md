# P3-4A — Audit métier du domaine Produits

> **MODE** : AUDIT_AND_REPORT_ONLY — aucun code applicatif modifié, aucune migration, aucune règle P3-4B implémentée.
> Ce rapport constate l'état actuel du domaine Produits. Les correctifs décrits ne sont **pas** implémentés : ce sont des constats et un découpage candidat.

## 1. Paramètres et périmètre

- **Repo** : iamzekhnini15/mmv-desktop · **Branche** : `p3-business-rules` · **Phase** : P3-4A · **Domaine** : Produits.
- Audit uniquement — aucune écriture sur des fichiers de code, d'entité, de use case, de test, de ViewModel, de vue, de configuration EF ou de migration.

## 2. État Git et CI

- `git branch --show-current` → **p3-business-rules**.
- HEAD → **d8af7048d0d7db95e6812ebf64b9dd77d966b3cf** (`feat(ui): redesign login screen`).
- `git status --short` → seulement `design-handoff/`, `design/`, `docs/ui/` (non suivis, hors périmètre, non touchés).
- `git diff --check` → propre ; aucun fichier suivi modifié.
- **CI (HEAD)** : run push `29541730063`, SHA = HEAD exact, status `completed`, conclusion **success**, workflow `CI`.
  URL : https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29541730063 — **CI verte confirmée par l'API GitHub.**

## 3. Baseline locale

- **Build** : `Build succeeded. 0 Warning(s), 0 Error(s)`.
- **Tests** : **751 réussis**, 0 échec, 0 ignoré → **294 Domain / 218 Application / 239 App** (conforme à la baseline attendue).
- **Vulnérabilités** : aucune (tous projets), transitives incluses.
- **Migrations** : `No changes have been made to the model since the last migration` (aucune en attente).
- **dotnet-ef** : 8.0.27 restauré.
- **MMV.Application — références** : uniquement `MMV.Domain` (pas d'Infrastructure). **Pure.**
- **MMV.Application — packages directs** : uniquement `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1`. **Pure.**

## 4. Cartographie du domaine

| Élément | Chemin | Couche | Responsabilité |
|---|---|---|---|
| `Product` (entité) | `src/MMV.Domain/Entities/Product.cs` | Domain | Table mère catalogue |
| `ProductCategoryEnum` | `src/MMV.Domain/Enums/ProductCategory.cs` | Domain | 6 catégories (CLIPS, PLASTIC, MONTURE, SOLAIRE, LENTILLE, VERRE) |
| `ProductCategory` (entité legacy) | `src/MMV.Domain/Entities/ProductCategory.cs` | Domain | Ancienne catégorie via `CategoryId` (conservée) |
| `GlassDetail` / `LensDetail` / `AccessoryDetail` | `src/MMV.Domain/Entities/*.cs` | Domain | Détails 1-1 (PK = ProductId) |
| `ProductValidator` | `src/MMV.Domain/Validators/ProductValidator.cs` | Domain | Règles FluentValidation — **non invoqué** |
| `IProductService` / `ProductService` | `src/MMV.Domain/Services/ProductService.cs` | Domain | Service legacy — **non consommé par l'UI** |
| `CreateProductUseCase` | `src/MMV.Application/UseCases/Products/CreateProduct/CreateProductUseCase.cs` | Application | Création + détails catégorie |
| `UpdateProductUseCase` | `src/MMV.Application/UseCases/Products/UpdateProduct/UpdateProductUseCase.cs` | Application | Mise à jour + détails |
| `DeleteProductUseCase` | `src/MMV.Application/UseCases/Products/DeleteProduct/DeleteProductUseCase.cs` | Application | **Suppression physique inconditionnelle** |
| `ListProducts` / `…ForPicker` / `…ForOrderPicker` | `src/MMV.Application/UseCases/Products/List*` | Application | Lectures / projections |
| `ProductConfiguration` | `src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs` | Infrastructure | Index unique Reference, FK, cascades |
| `ProductRepository` | `src/MMV.Infrastructure/Repositories/ProductRepository.cs` | Infrastructure | Requêtes (filtres `IsActive`) |
| `ProductFormViewModel` | `src/MMV.App/ViewModels/ProductFormViewModel.cs` | App | Formulaire création / édition |
| `ProductsListViewModel` | `src/MMV.App/ViewModels/ProductsListViewModel.cs` | App | Liste + suppression |
| `ProductUseCasesTests` | `tests/MMV.Application.Tests/UseCases/Products/ProductUseCasesTests.cs` | Tests | Happy-path + gardes |

**Aucun** `SetProductActive` / `ArchiveProduct` use case n'existe (contrairement à `SetUserActive` et `SetCustomerArchived`).

## 5. Entité Product

`decimal` pour les prix, `int` pour le stock, `bool IsActive = true`, `long SupplierId` (non nullable), `string Reference`, `ProductCategoryEnum Category` (+ `CategoryId` legacy nullable). Détails 1-1 optionnels. Collections `OrderItems`, `SaleItems`, `StockMovements`. **Aucune méthode de domaine ni invariant** : entité anémique, toute la logique est (ou devrait être) dans les use cases.

## 6. Référence et unicité

- **Obligatoire** : oui (validator `NotEmpty` + EF `IsRequired`).
- **Index unique EF** : **oui** — `idx_products_reference_unique` sur `Reference`, présent en base (`ProductConfiguration.cs`). L'unicité stricte multi-poste est donc **garantie par la base**.
- **Normalisation** : **aucune** — pas de `Trim`, pas de casse imposée. SQLite compare en collation `BINARY` par défaut → **`"ABC"` et `"abc"` sont considérés distincts** : deux références qui ne diffèrent que par la casse ou les espaces passent l'index unique.
- **Vérification applicative** : **aucune**. Ni `CreateProductUseCase` ni `UpdateProductUseCase` n'appellent `GetByReferenceAsync` (qui existe pourtant dans le repo). La seule barrière est l'index DB.
- **Collision** : remonte comme `DbUpdateException` (violation UNIQUE) non capturée → l'UI affiche `Erreur lors de… : {ex.Message}` (message SQLite brut). **Pas de message métier clair.** Pas de gestion « ignorer le produit lui-même » en update, ni de message dédié.
- **Tests** : **aucun** test d'unicité, de doublon, de casse ni de concurrence.

## 7. Catégories et détails spécialisés

| Catégorie | GlassDetail | LensDetail | AccessoryDetail | Règle actuelle |
|---|---|---|---|---|
| VERRE | créé si `GlassMaterial`/`GlassType` renseigné | — | — | aucune obligation, aucune exclusion |
| LENTILLE | — | créé si `LensBrand`/`LensModel` | — | idem |
| MONTURE / CLIPS / PLASTIC / SOLAIRE | — | — | créé si couleur/taille/matière | idem |

- La cohérence n'est **garantie nulle part** : ni Domain (entité anémique), ni use case (le `switch` ne fait qu'*ajouter* le détail de la catégorie courante), ni EF (les 3 FK détail sont indépendantes et optionnelles).
- **Incohérence prouvée** : dans `UpdateProductUseCase.ApplyCategorySpecificDetails`, un changement de catégorie (ex. VERRE → MONTURE) crée/actualise `AccessoryDetail` **mais ne supprime jamais** le `GlassDetail` existant → un produit peut porter **plusieurs détails incompatibles simultanément**.
- L'UI se contente de masquer les champs non pertinents (présentation) ; elle n'empêche pas la persistance de détails obsolètes.
- **Tests** : aucun sur les combinaisons catégorie/détail valides ou invalides.

## 8. Prix et stock

- **Prix** : `PurchasePrice`, `SalePrice` (obligatoires), `RecommendedPrice` (optionnel). `decimal` en domaine **mais mappés `REAL` (flottant) en SQLite** (`ProductConfiguration.cs`) → **risque de précision monétaire**.
- Négatifs : bloqués côté validator (`≥ 0`, non invoqué) et côté UI (`< 0` → erreur). **Aucune garde use case/domaine** → un appel direct au use case accepte un prix négatif.
- `SalePrice < PurchasePrice` : bloqué **uniquement dans l'UI** (`ProductFormViewModel`) ; règle absente du use case et du domaine.
- Marge : calculée par `ProductService.CalculateMarginPercentage` (service **inutilisé**), non persistée. Pas de TVA / fiscalité.
- **Stock porté par Product** : `StockQuantity` est **écrit directement** par Create **et** Update → **contournement total de `IStockMutationService`**, aucun `StockMovement` généré, aucune traçabilité, stock modifiable sans mouvement. Deuxième chemin d'écriture confirmé. **→ à traiter en P3-5.**

## 9. Suppression et historique

`DeleteProductUseCase` : charge le produit (sans includes) et appelle `DeleteAsync` **sans aucune vérification d'usage** → **suppression physique inconditionnelle**, en s'appuyant sur les cascades EF.

| Relation | FK | Nullable | DeleteBehavior | Effet d'une suppression Product |
|---|---|---|---|---|
| `SaleItem.ProductId` | → Products | **oui** (`long?`) | **SetNull** | ligne de vente **orpheline** : montants conservés, produit perdu 🔴 |
| `OrderItem.ProductId` | → Products | **oui** (`long?`) | **SetNull** | ligne de commande **orpheline** 🔴 |
| `StockMovement.ProductId` | → Products | non (`long`) | **Cascade** | **tous les mouvements supprimés → historique de stock effacé** 🔴 |
| `GlassDetail.ProductId` | PK+FK | non | Cascade | détail supprimé (correct) |
| `LensDetail.ProductId` | PK+FK | non | Cascade | détail supprimé (correct) |
| `AccessoryDetail.ProductId` | PK+FK | non | Cascade | détail supprimé (correct) |
| `Product.SupplierId` | → Suppliers | non | Restrict | (côté fournisseur) bloque la suppression du fournisseur |
| `Product.CategoryId` | → ProductCategories | oui | SetNull | lien catégorie legacy annulé |

Comportement DB-level confirmé par la migration `InitialCreate` et le snapshot courant. **La base ne refuse aucune suppression de produit** : elle exécute silencieusement la perte/l'orphelinage d'historique commercial. L'UI demande une confirmation générique (événement `DeleteProductRequested` → dialogue), **non consciente de l'usage**. **Aucune alternative de désactivation** n'existe.

## 10. Activation / désactivation

- `IsActive` existe, défaut `true`, filtré dans plusieurs requêtes repo (`GetActiveProductsAsync`, `SearchByNameAsync`, `GetLowStockProductsAsync`…).
- **Aucun chemin d'écriture ne met `IsActive` à `false`** : `UpdateProductUseCase` **ne mappe même pas** `IsActive`, et aucun use case dédié n'existe. `IsActive` est donc **figé à `true`** pour tout produit. La « désactivation plutôt que suppression » de la roadmap **n'est pas implémentée**.
- Filtrage incohérent des sélecteurs :
  - Vente : `SaleFormViewModel` filtre `.Where(p => p.IsActive)` **en mémoire**.
  - Commande : `ListProductsForOrderPickerUseCase` **n'applique aucun filtre** `IsActive`.
  - Stock : `ListProductsForPickerUseCase` **aucun filtre**.
  - Liste principale (`ListProductsUseCase`) : renvoie tous les produits (filtrage catégorie/recherche en mémoire seulement).
- Réactivation, visibilité dans les historiques : sans objet tant qu'aucun produit ne peut devenir inactif.

## 11. Fournisseur

- `SupplierId` **obligatoire** (non nullable, EF `Restrict + IsRequired`). Suppression d'un fournisseur ayant des produits → **refusée par la base** (Restrict).
- Le use case ne **valide pas** l'existence du fournisseur à la création : `SupplierId = command.SupplierId ?? 0` → un `0`/inexistant provoquerait une violation FK remontée en `DbUpdateException` brute. Changement de fournisseur possible (simple réaffectation). **→ intégrité fine et validateur fournisseur à traiter en P3-9.**

## 12. Flux UI et Application

- Écritures produit routées via les use cases (`ProductFormViewModel`, `ProductsListViewModel`). Pas d'injection directe de `DbContext`/`IUnitOfWork`/repository dans les ViewModels.
- **Duplication de règles UI-only** (prix ≥ 0, `SalePrice ≥ PurchasePrice`, stock ≥ 0) sans équivalent use case/domaine.
- **Chemins parallèles latents** : `IProductService` (legacy) reste enregistré dans la DI Infrastructure — deuxième chemin d'écriture disponible qui ne valide que `SalePrice > 0`, sans unicité. Non consommé aujourd'hui, mais résoluble. `ProductValidator` défini et testé mais **jamais appelé**.

## 13. Multi-poste et concurrence

- **Unicité Reference** : la seule garantie multi-poste correcte du domaine (index unique DB), mais collision → exception brute, pas de message métier.
- **Suppression** : aucun token de concurrence, aucun rechargement, last-write-wins ; pire, la suppression détruit l'historique sans que la base ne l'empêche (contrairement à Customer où les FK sont en `Restrict`).
- **Update** : `GetByIdWithDetailsAsync` puis `SaveChanges` sans jeton de concurrence → écrasement silencieux entre postes.
- **Stock direct** : `StockQuantity` écrasé par Create/Update sans mouvement → courses inter-postes non tracées (**P3-5**).
- Distinction :
  - *Problème réel observé* = perte/orphelinage d'historique à la suppression ; absence de désactivation ; incohérence catégorie/détail à l'update.
  - *Risque théorique fondé sur le code* = collision de référence à message obscur ; contournement stock.
  - *Solution candidate* = découpage candidat (voir §17).

## 14. Tests existants

| Règle / risque | Test existant | Niveau | Suffisant ? |
|---|---|---|---|
| Création + détail accessoire | `Create_PersistsProductWithAccessoryDetail` | Application / SQLite | Oui (happy) |
| Update applique changements | `Update_ExistingProduct_AppliesChanges` | Application / SQLite | Oui (happy) |
| Update produit absent | `Update_MissingProduct_ReturnsNotFound` | Application | Oui |
| Suppression produit **sans usage** | `Delete_ExistingProduct_RemovesIt` | Application / SQLite | Partiel — ne teste **pas** un produit utilisé |
| Suppression produit absent | `Delete_MissingProduct_ReturnsNotFound` | Application | Oui |
| Gardes null / DI | `NullCommand_Throws`, `Constructors_RejectNullDependencies` | Application | Oui |
| Validator (règles) | `ProductValidatorTests` | Domain | Teste un validateur **non branché** |

## 15. Trous de couverture

Aucun test pour : référence dupliquée · doublon casse/espaces · doublon concurrent · cohérence catégorie/détail (valide **et** incompatible) · détails multiples simultanés après changement de catégorie · prix négatif au niveau use case · `SalePrice < PurchasePrice` hors UI · stock négatif use case · **suppression d'un produit utilisé en vente / commande / avec mouvements** · désactivation / réactivation · produit inactif dans sélecteur vente/commande · intégrité fournisseur (SupplierId inexistant).

## 16. Risques classés

### 🔴 Critique

1. **Suppression physique efface l'historique de stock** — `StockMovement → Product = Cascade`. Preuve : `ProductConfiguration.cs` (`HasMany(p => p.StockMovements) … OnDelete(Cascade)`), snapshot courant. Couche : Application (garde) + Infrastructure (FK). Cible : découpage candidat B.
2. **Suppression physique orpheline les lignes de vente/commande** — `SaleItem`/`OrderItem → Product = SetNull` sur FK nullable. Preuve : `InitialCreate` + snapshot courant. L'historique commercial perd le produit vendu. Couche : Application + Infrastructure. Cible : découpage candidat B.
3. **`DeleteProductUseCase` ne vérifie aucun usage** — suppression inconditionnelle. Preuve : `DeleteProductUseCase.ExecuteAsync`. Couche : Application. Cible : découpage candidat B.

### 🟠 Important

4. **Unicité Reference non normalisée** — doublons casse/espaces acceptés (collation BINARY), aucune vérif applicative, message brut. Couche : Application (+ éventuellement collation DB). Cible : découpage candidat B.
5. **Incohérence catégorie/détail** — détails obsolètes conservés après changement de catégorie ; aucune garantie de cohérence. Preuve : `UpdateProductUseCase.ApplyCategorySpecificDetails`. Couche : Application/Domain. Cible : découpage candidat B.
6. **Désactivation impossible** — `IsActive` jamais écrit ; pas de use case ; Update ne le mappe pas. La « suppression douce » n'existe pas. Couche : Application + App. Cible : découpage candidat B.
7. **`ProductValidator` non branché & prix/marge non gardés hors UI** — règles métier contournables par appel use case direct. Couche : Application/Domain. Cible : découpage candidat B.

### 🟡 Dette

8. Prix mappés `REAL` (précision monétaire).
9. `IProductService` / `ProductService` legacy encore enregistré = chemin d'écriture parallèle latent.
10. Filtrage `IsActive` incohérent entre sélecteurs (mémoire vs requête, absent en commande/stock).
11. Traces `Console.WriteLine`/`Debug.WriteLine` dans les ViewModels.

### 🔵 Report explicite

12. Écriture directe de `StockQuantity` contournant `IStockMutationService` → **P3-5 (Stock)**.
13. Double décrément / traçabilité → **P3-5 / P3-6 / P3-7**.
14. Validateur fournisseur + intégrité `SupplierId` à la création → **P3-9 (Fournisseurs)**.
15. Jeton de concurrence générique multi-poste → **chantier transverse concurrence**.

## 17. Découpage candidat pour la suite

> Noms non officiels — à valider par l'utilisateur avant toute implémentation.

- **Candidat B — Règles métier Produits (Application + Domain)**
  - *Objectif* : unicité robuste, cohérence catégorie/détail, prix gardés, désactivation.
  - *Périmètre* : vérif unicité (normalisée) dans Create/Update en ignorant le produit lui-même + message métier stable ; nettoyage des détails hors-catégorie ; garde prix (`≥ 0`, `SalePrice ≥ PurchasePrice`) ; mapping `IsActive` dans Update + **use case de désactivation/archivage** ; **garde d'usage dans Delete** (bloquer si vente/commande/mouvement, message stable « … archivez-le à la place », sur le modèle `DeleteCustomerUseCase`).
  - *Fichiers probables* : `Create/Update/DeleteProductUseCase`, nouveau use case de désactivation, `IProductRepository`/`ProductRepository` (comptage d'usage), `ProductFormViewModel`/`ProductsListViewModel`.
  - *Migration* : **oui** — passer `SaleItem`/`OrderItem → Product` de `SetNull` à **`Restrict`** et `StockMovement → Product` de `Cascade` à **`Restrict`**, pour que la base soit le filet multi-poste (comme `Sale → Customer` en P3-2B).
  - *Risques* : réécriture de table SQLite (rebuild FK) ; données existantes déjà orphelines à contrôler.
  - *Dépendances* : aucune bloquante ; cohérent avec P3-2.
- **Candidat C — UI Produits** : messages de conflit clairs, action « Désactiver » à la place de « Supprimer », badge inactif, filtrage `IsActive` unifié côté requête.
- **Candidat D — Doc/CI** : rapport d'implémentation + preuve CI, sur le modèle P3-3B/3C.

*Ordre recommandé* : B (domaine + migration) → C (UI) → D (doc/CI).

## 18. Fichiers probablement concernés

`Create/Update/DeleteProductUseCase(.cs)`, nouveau use case de désactivation, `IProductRepository`/`ProductRepository` (comptage d'usage + normalisation), `ProductConfiguration` (FK Restrict), une **nouvelle migration**, `ProductFormViewModel`/`ProductsListViewModel`/pickers, tests Application + App.
**Hors périmètre P3-4** : login/shell redesignés, `ProductService` legacy (à supprimer séparément), tout le stock (P3-5).

## 19. Migrations éventuelles

**Oui, une migration sera nécessaire lors de l'implémentation** (pas en P3-4A) : basculer `SaleItem`/`OrderItem`/`StockMovement → Product` vers `Restrict`. Aucune migration en attente aujourd'hui ; le modèle est en phase avec la base.

## 20. Recommandations

1. Traiter en priorité les 🔴 (garde d'usage + FK Restrict + archivage) selon le patron Customer P3-2.
2. Ajouter la vérification d'unicité normalisée avec message métier.
3. Nettoyer les détails hors-catégorie à l'update.
4. Déplacer les règles prix de l'UI vers le use case ; brancher ou retirer `ProductValidator`.
5. Reporter explicitement le stock direct à **P3-5** et l'intégrité fournisseur à **P3-9**.

## 21. Verdict

**P3-4A = GO AUDIT** — L'arbre Git est propre, la CI du HEAD est verte (API confirmée), le build/tests/migrations sont sains, et le domaine Produits est cartographié avec preuves suffisantes pour décider la suite. L'audit établit que les règles P3-4 candidates **ne sont pas encore implémentées** (unicité non gardée applicativement, cohérence catégorie/détail absente, suppression destructrice d'historique, désactivation inexistante).
