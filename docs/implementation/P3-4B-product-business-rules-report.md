# P3-4B — Règles métier et intégrité des Produits (rapport canonique)

> **MODE** : FINALIZE_IMPLEMENTATION_AND_REPORT_NO_COMMIT — dernière passe locale de P3-4B (finalisation du
> backfill de migration), validations complètes, **aucun commit, aucun push, aucune UI**.
>
> Ce rapport **consolide toute l'étape P3-4B** et reflète l'**état final après correction du backfill**. Les
> affirmations antérieures rendues fausses par cette correction (backfill par liste de `REPLACE` accentués ;
> « approximation ASCII acceptée ») **ne sont pas reprises**. La stratégie de backfill retenue est désormais
> **« exact ou échec sûr »** (cf. §7).

## 1. Paramètres

- **Repo** : iamzekhnini15/mmv-desktop · **Branche** : `p3-business-rules` · **Phase** : P3-4B · **Domaine** : Produits.
- **BASE_COMMIT / HEAD** : `2709bfe89ef83534b062c02708da22b8e8b37c6a` — **inchangé** (aucun commit créé).

## 2. État Git et CI de départ

- `git branch --show-current` → **p3-business-rules**.
- `git rev-parse HEAD` → **`2709bfe89ef83534b062c02708da22b8e8b37c6a`** (P3-4A, `docs(P3-4A): audit product business rules`).
- **CI de la baseline** : run push `29601622654` sur le SHA du HEAD → `completed` / **success** (workflow `CI`).
- Au démarrage de cette passe : changements locaux P3-4B présents ; hors périmètre autorisé uniquement
  `design-handoff/`, `design/`, `docs/ui/`. Aucun fichier `src/MMV.App/**`.

## 3. Baseline

- Build baseline (HEAD) : `0 Warning(s), 0 Error(s)`, **751 tests** (Domain 294 / Application 218 / App 239),
  0 échec, 0 ignoré, aucune vulnérabilité, aucune migration en attente.
- P3-4B ajoute la logique métier Produits sur cette base sans toucher à l'UI ni introduire de dépendance de couche.

## 4. Périmètre

**Inclus** : normalisation et unicité des références ; validation Create/Update ; cohérence catégorie/détails ;
suppression protégée + FK `RESTRICT` ; activation/désactivation ; filtrage des sélecteurs backend ; migration
schéma + backfill sûr des données existantes ; tests métier et tests de migration sur bases jetables.

**Exclu (frontières P3-4B)** : toute UI Produits (`src/MMV.App/**`), reportée à l'alignement UI ; écriture directe
de `StockQuantity` (dette **P3-5**) ; validation/intégrité fournisseur détaillée (**P3-9**). Aucune migration
existante modifiée ; aucun package NuGet ajouté.

## 5. Normalisation et unicité des références

- **Source unique de vérité** : `Product.NormalizeReference(string?) = (r ?? "").Trim().ToUpperInvariant()` —
  jamais la culture courante de la machine (deux postes produisent la même clé).
- **Séparation affichage / clé** : `Reference` (valeur d'affichage, `Trim` sur le setter) reste distincte de
  `NormalizedReference` (colonne persistée, setter **privé** accessible à EF). Le setter de `Reference`
  **recalcule** `NormalizedReference` à chaque écriture. Portée exacte de cette garantie :
  - le setter de `Reference` garantit la cohérence lors des écritures **applicatives normales** ;
  - la migration garantit la cohérence des lignes historiques **qu'elle accepte** (cf. §7.1) ;
  - l'index garantit l'**unicité** de `NormalizedReference` (pas l'équivalence avec `Reference`) ;
  - **aucune contrainte SQL ni aucun trigger** ne garantit que `Reference` et `NormalizedReference` restent
    équivalentes après une **écriture SQL directe** ;
  - les écritures SQL directes hors migrations **ne font pas partie du flux applicatif supporté**.
- **Garantie base** : index unique `idx_products_normalized_reference_unique` sur `NormalizedReference` ; l'ancien
  index unique sur `Reference` brute est retiré.
- **Garde applicative** : `IProductRepository.ExistsByNormalizedReferenceAsync(normalizedReference,
  excludingProductId)` (`AnyAsync`, aucune collection matérialisée), appelée par Create (exclusion `0`) et Update
  (exclusion du produit courant) **avant** écriture. Message stable : « Un produit portant cette référence existe
  déjà. ».
- **Conflit concurrent (deux postes quasi simultanés)** : l'écriture de Create/Update est enveloppée dans
  `ITransactionRunner` ; une violation d'unicité qui échappe à la garde est rejetée par la base et neutralisée par
  `PersistenceErrorMapper` en `PersistenceException{Category=UniqueConstraint}` (jamais un texte SQLite/EF brut).
  Create et Update **interceptent** cette exception (`catch … when (Category == UniqueConstraint)`, filtre depuis
  `MMV.Domain.Exceptions` — aucune dépendance EF/SQLite) et renvoient **exactement le même résultat métier stable**
  que la garde pré-écriture (doublon référence). Chemin normal et chemin concurrent convergent donc vers le même
  contrat public ; toute autre erreur de persistance reste propagée telle quelle.

## 6. Validation Create/Update

`ProductValidator` est **réellement invoqué** par Create/Update via `CommandValidation` (convention
`CreateCustomerUseCase`). Règles avec messages : prix d'achat/vente ≥ 0 ; **prix de vente ≥ prix d'achat** (portée
de l'UI vers le Domain) ; prix conseillé ≥ 0 **s'il est renseigné** ; stock ≥ 0. Aucune règle hors périmètre
(pas de TVA, marge, ni règle fournisseur).

## 7. Migration — schéma et stratégie finale de backfill

**Migration** : `AddProductNormalizedReferenceAndProtectHistory` (`20260717183027`). Opérations `Up`, dans
l'ordre : `DropForeignKey` (OrderItems, SaleItems, StockMovements) → `DropIndex idx_products_reference_unique` →
`AddColumn NormalizedReference` (TEXT, NOT NULL, défaut `''`) → **garde + backfill** (ci-dessous) →
`CreateIndex idx_products_normalized_reference_unique` (unique) → `AddForeignKey` ×3 en `ReferentialAction.Restrict`.

### 7.1 Stratégie « exact ou échec sûr » (correction finale)

Le backfill doit produire pour **chaque** référence héritée **exactement** `Trim().ToUpperInvariant()`, ou bien
**échouer sans rien modifier**. Aucune valeur « approximativement normalisée mais acceptée » n'est tolérée : elle
affaiblirait silencieusement l'unicité (deux références que l'application tient pour égales pourraient coexister).

- **Cas exact — références purement ASCII imprimables** : `UPPER(TRIM("Reference"))` de SQLite égale **exactement**
  `Trim().ToUpperInvariant()`. Le `UPPER` natif de SQLite ne replie que `a–z` (aucune extension ICU, aucune
  dépendance culturelle), ce qui coïncide avec l'invariant sur l'ASCII ; aucun autre caractère n'est modifié. Cette
  identité vaut que la migration soit jouée par l'application **ou** par `dotnet ef`.
- **Échec sûr — toute référence hors ASCII imprimable** (accents latins, alphabets grec/cyrillique, caractères de
  contrôle…) : SQLite ne peut pas garantir l'égalité avec `ToUpperInvariant`. Plutôt qu'un backfill approximatif,
  une **garde** insère la référence fautive dans une table à contrainte `CHECK` impossible
  (`__abort_non_ascii_reference_needs_manual_normalization`, colonne `CHECK(... = '')`), ce qui lève une erreur et
  **annule la transaction de migration**.

**Garanties de l'échec sûr** : aucune ligne supprimée, fusionnée ni modifiée ; l'ancien schéma et les données
restent intacts ; la migration **n'est pas enregistrée** dans `__EFMigrationsHistory`. **La migration ayant échoué
au démarrage, le schéma reste l'ancien** : rien ne garantit que la nouvelle application peut ouvrir l'écran
Produits dans cet état. **Action opérateur** requise, dans l'ordre : (1) **sauvegarder la base** ; (2) **identifier**
les références historiques non ASCII fautives ; (3) les **normaliser ou les remplacer** dans un environnement
contrôlé — soit avec la **version précédente de l'application**, encore compatible avec l'ancien schéma, soit avec
un **outil d'administration SQLite maîtrisé** (édition SQL directe hors flux applicatif) ; (4) **vérifier l'absence
de collision** après normalisation ; (5) **rejouer la migration**. Les références enregistrées **après** migration
réussie passent toujours par `Product.NormalizeReference`.

> La stratégie antérieure — liste manuelle de `REPLACE` d'accents latins encadrée d'`UPPER` — a été **retirée** :
> incomplète (elle laissait les alphabets non latins seulement ASCII-repliés, donc approximés-mais-acceptés) et
> plus complexe que nécessaire. La règle « exact (ASCII) ou échec sûr (tout le reste) » est plus simple et sans
> chemin approximatif.

### 7.2 Collision de références

L'index unique étant créé **après** le backfill, deux références ASCII convergeant après normalisation
(`"dup-1"`/`"DUP-1"`) font **échouer** la migration à la création de l'index (aucune fusion, aucune perte). Une
collision impliquant une référence accentuée est interceptée **encore plus tôt** par la garde §7.1.

### 7.3 FK et détails

`SaleItem.ProductId` SetNull→**Restrict** ; `OrderItem.ProductId` SetNull→**Restrict** ; `StockMovement.ProductId`
Cascade→**Restrict**. Nullabilité des FK inchangée. Détails 1-1 (`GlassDetail`/`LensDetail`/`AccessoryDetail`) :
cascade inchangée (ils appartiennent au produit, pas à l'historique).

## 8. Cohérence catégorie / détails

- **Création** : un seul détail est créé, celui de la catégorie choisie (comportement inchangé).
- **Modification** : `UpdateProductUseCase.ApplyCategorySpecificDetails` met **explicitement à `null`** les deux
  détails incompatibles avec la catégorie cible avant de créer/actualiser le détail compatible ; EF traduit
  l'affectation `null` d'une navigation 1-1 chargée en suppression de la ligne dépendante (cascade configurée). Le
  détail compatible n'est pas rendu obligatoire (comportement legacy préservé, documenté).

## 9. Suppression protégée et FK RESTRICT

- **Garde applicative** : `IProductRepository.IsReferencedByHistoryAsync(productId)` = trois `AnyAsync`
  (SaleItems, OrderItems, StockMovements), court-circuit, aucune collection matérialisée. `DeleteProductUseCase`
  refuse par `BusinessRuleException` (« Ce produit est utilisé dans l'historique et ne peut pas être supprimé.
  Désactivez-le à la place. ») dès qu'une relation existe ; aucune ligne d'historique touchée.
- **Produit inutilisé** : suppression physique inchangée ; détails 1-1 en cascade.
- **Filet base (multi-poste)** : les FK `RESTRICT` (§7.3) refusent au niveau SQL la suppression d'un produit
  référencé par un historique créé concurremment — plutôt qu'orpheliner (`SetNull`) ou effacer (`Cascade`).

## 10. Activation / désactivation

Nouveau `SetProductActiveUseCase` (modèle `SetUserActiveUseCase` / `SetCustomerArchivedUseCase`) : commande à état
absolu (`ProductId`, `IsActive`), `NotFound` si absent, **idempotence stricte** (aucune écriture si l'état est déjà
celui demandé), ne modifie **que** `IsActive` (aucun impact stock/prix/détails), réactivation permise. Enregistré
dans `DependencyInjection.cs`.

## 11. Filtres des sélecteurs

Les lectures servant à une **nouvelle** opération excluent les produits inactifs : `ListProductsForPickerUseCase`
(mouvement de stock), `ListProductsForOrderPickerUseCase` (commande), `GetSaleFormReferenceDataUseCase` (vente —
filtre désormais côté lecture backend au lieu de la mémoire du `SaleFormViewModel`). `ListProductsUseCase` (liste
d'administration) reste **inchangé** (renvoie les inactifs pour consultation/réactivation). Aucune ligne historique
n'est masquée.

## 12. Tests de migration (bases jetables)

`ProductNormalizedReferenceMigrationTests` — base SQLite **jetable** positionnée à la migration précédente
`20260713215132_AddCustomerArchivingAndProtectHistory`, jamais la base réelle. 9 tests :

| Test | Cas couvert | Attendu observé |
|---|---|---|
| `FreshCreate_…NormalizationEnforced` | création depuis zéro | normalisation appliquée à l'insertion |
| `Upgrade_BackfillsAsciiReference_ExactlyLikeToUpperInvariant_AndKeepsAllRows` | **ASCII valide** | backfill = `Product.NormalizeReference`, 0 perte |
| `Upgrade_WithAsciiCollidingReferences_Fails_WithoutDataLoss` | **collision ASCII** | échec index, 2 lignes intactes, migration non enregistrée |
| `Upgrade_WithAccentedReference_FailsSafely_WithoutDataLoss` | **référence française accentuée** | échec sûr, référence brute intacte, migration non enregistrée |
| `Upgrade_WithGreekOrCyrillicReference_FailsSafely_WithoutDataLoss` | **référence grecque/cyrillique** | échec sûr, 0 perte, migration non enregistrée |
| `Upgrade_WithAccentedCollidingReferences_FailsSafely_WithoutDataLoss` | **collision accentuée** | garde non-ASCII avorte avant l'index, 0 perte |
| `Upgrade_PreservesSalesOrdersAndStockMovements_ThroughMigration` | **conservation ventes/commandes/mouvements** | 1 vente + ligne, 1 commande + ligne, 1 mouvement conservés ; `NormalizedReference` rempli |
| `Upgrade_CreatesUniqueNormalizedIndex_AndDropsOldReferenceIndex` | **index unique normalisé** | `unique=1` sur `NormalizedReference` ; ancien index disparu |
| `Upgrade_EnforcesRestrictOnProductDeletion_WhenReferencedBySaleItem` | **FK RESTRICT** | `DELETE` produit référencé refusé (`SqliteException`), lignes intactes |

Le test d'**échec transactionnel sans perte** est prouvé transversalement : chaque test d'échec vérifie le
`COUNT(*)` des lignes d'origine **et** l'absence d'enregistrement de la migration dans `__EFMigrationsHistory`.

## 13. Tests métier

- **Référence** : vide refusée ; trim des espaces externes ; doublon exact/casse/espaces refusé (Create) ; doublon
  vers un autre produit refusé (Update) ; conservation de sa propre référence acceptée (Update) ; unicité DB sur la
  représentation normalisée (insertion directe rejetée) ; **collision concurrente** au niveau use case (Create et
  Update) → **même résultat doublon stable, aucune exception** ; neutralisation provider-neutre au niveau runner.
- **Validation** : achat négatif, vente négative, prix conseillé négatif si renseigné, vente < achat, stock négatif
  → refusés sans écriture ; valeurs valides acceptées.
- **Catégorie/détails** : création VERRE/LENTILLE/accessoire ne persiste que le détail correspondant ; les 3
  changements de catégorie suppriment le détail devenu incompatible.
- **Suppression** : produit inutilisé supprimé (détails en cascade) ; produit utilisé par SaleItem/OrderItem/
  StockMovement → `BusinessRuleException`, historique intact ; suppression directe rejetée par les FK `Restrict`.
- **Activation** : désactivation, idempotence, réactivation, `NotFound`, seul `IsActive` change.
- **Sélecteurs** : actif présent / inactif absent des sélecteurs vente-commande-stock ; inactif toujours présent
  dans la liste d'administration.

## 14. Résultat complet (validation finale)

Commandes exécutées sur `MMV.sln` après la correction finale :

- `dotnet restore` → à jour ; `dotnet build --no-restore -c Debug` → **0 Warning(s), 0 Error(s)**.
- `dotnet test --no-build -c Debug` :
  - `MMV.Domain.Tests` → **297** réussis, 0 échec, 0 ignoré ;
  - `MMV.Application.Tests` → **263** réussis, 0 échec, 0 ignoré ;
  - `MMV.App.Tests` → **239** réussis, 0 échec, 0 ignoré ;
  - **Total exact : 799 tests**, 0 échec, 0 ignoré (baseline P3-4B 751 → +48 ; +4 vs la passe précédente,
    correspondant au remplacement du test de backfill accentué par les tests d'échec sûr et de couverture
    migration ASCII/grec/cyrillique/conservation/index/RESTRICT).
- `dotnet list MMV.sln package --vulnerable --include-transitive` → **aucune** vulnérabilité (tous projets,
  transitives incluses).
- `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build` → *No changes have
  been made to the model since the last migration.*
- `git diff --check` → propre (seuls avertissements informatifs LF→CRLF).

## 15. Frontières de couches

- `dotnet list src/MMV.Application/MMV.Application.csproj reference` → **`MMV.Domain` uniquement**.
- `dotnet list src/MMV.Application/MMV.Application.csproj package` → seul package direct
  `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` ; **aucune** dépendance EF/SQLite.
- L'interception du conflit concurrent utilise `PersistenceException`/`PersistenceErrorCategory` de
  `MMV.Domain.Exceptions` : frontière `Application → Domain` préservée.
- Aucune logique Produit ajoutée à l'UI ; aucun changement des workflows Stock/Commandes/Ventes/Fournisseurs.

## 16. Dettes reportées

- **P3-5** : écriture directe de `StockQuantity` par Create/Update (aucun mouvement de stock ; `IStockMutationService`
  intact) — reportée telle quelle.
- **P3-9** : validation/intégrité fournisseur détaillée — reportée telle quelle.
- **Alignement UI Produits** : le `ProductFormViewModel` ne consomme pas encore `IsValid`/`ValidationErrors` (il
  attrape `Exception` largement). Ce n'est pas une régression (le comportement pré-P3-4B était déjà « succès
  silencieux » côté UI) : l'alignement — sur le modèle de `PrescriptionFormViewModel` (P3-3C) — reste **à faire**
  et est **hors périmètre** de cette passe (« ne pas commencer l'UI Produits »).
- **`ProductService` legacy** toujours enregistré en DI (chemin d'écriture parallèle latent, déjà signalé en P3-4A).

## 17. Absence de modification UI

Aucun fichier sous `src/MMV.App/**` n'est modifié (vérifié par `git status`). Aucun ViewModel/vue/dialogue/style/
asset touché. Les seuls répertoires hors périmètre présents sont non suivis et non build : `design-handoff/`,
`design/`, `docs/ui/`.

## 18. Fichiers modifiés et créés

**Modifiés (suivis, 22)** — Architecture : `docs/architecture/P3-business-rules-roadmap.md` (statut P3-4B).
Domain : `Entities/Product.cs`, `Validators/ProductValidator.cs`,
`Interfaces/Repositories/IProductRepository.cs`. Application : `DependencyInjection.cs`,
`UseCases/Products/CreateProduct/{CreateProductUseCase,CreateProductResult}.cs`,
`UseCases/Products/UpdateProduct/{UpdateProductUseCase,UpdateProductResult}.cs`,
`UseCases/Products/DeleteProduct/DeleteProductUseCase.cs`,
`UseCases/Products/ListProductsForOrderPicker/ListProductsForOrderPickerUseCase.cs`,
`UseCases/Products/ListProductsForPicker/ListProductsForPickerUseCase.cs`,
`UseCases/Sales/GetSaleFormReferenceData/GetSaleFormReferenceDataUseCase.cs`. Infrastructure :
`Repositories/ProductRepository.cs`, `Data/Configurations/{Product,SaleItem,OrderItem,StockMovement}Configuration.cs`,
`Data/SqliteSchemaVerifier.cs`, `Migrations/OpticDbContextModelSnapshot.cs`. Tests :
`tests/MMV.Application.Tests/UseCases/Products/ProductUseCasesTests.cs`,
`tests/MMV.Domain.Tests/ValidatorTests/ProductValidatorTests.cs`.

**Créés (non suivis)** — `src/MMV.Application/UseCases/Products/SetProductActive/` ;
`src/MMV.Infrastructure/Migrations/20260717183027_AddProductNormalizedReferenceAndProtectHistory.cs` (+ `.Designer.cs`) ;
`tests/MMV.Application.Tests/Migrations/ProductNormalizedReferenceMigrationTests.cs` ;
`tests/MMV.Application.Tests/UseCases/Products/ProductBusinessRulesTests.cs` ;
`tests/MMV.Application.Tests/UseCases/Products/ProductSelectorActiveFilterTests.cs` ;
`docs/implementation/P3-4B-product-business-rules-report.md` (ce rapport).

## 19. État Git final

- `git diff --stat` → **22 fichiers suivis modifiés, +438 / −67** (dont la roadmap).
- `git diff --check` → propre (avertissements LF→CRLF informatifs uniquement).
- `git rev-parse HEAD` → `2709bfe89ef83534b062c02708da22b8e8b37c6a` — **inchangé, aucun commit**.
- Aucun fichier `src/MMV.App/**` dans les changements. Ce rapport et la roadmap font partie des changements locaux.

## 20. Verdict

**P3-4B = GO LOCAL**

La normalisation historique est **exacte** (références ASCII) **ou échoue sûrement** (tout caractère hors ASCII
imprimable), sans aucun chemin approximatif accepté : garanti par la garde de migration et prouvé par les tests
ASCII / accentué / grec-cyrillique / collisions sur bases jetables. Les **799 tests** passent (0 échec), aucune
vulnérabilité, aucune migration en attente, frontières de couches préservées. Le rapport physique existe dans
`docs/implementation/` et la roadmap est mise à jour. **Aucune UI modifiée, aucun commit, aucun push.**
