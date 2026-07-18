# P3-5 — Implémentation backend Stock et mouvements multi-poste sûrs (rapport d'implémentation)

> **MODE** : IMPLEMENT_TEST_AND_WRITE_REPORT_NO_COMMIT — code, tests et rapport uniquement ; **aucune UI, aucune
> migration, aucun commit, aucun push**. Validé **localement** ; le verdict définitif P3-5 reste subordonné à la CI.

## 1. Paramètres et périmètre

- **Repo** : iamzekhnini15/mmv-desktop · **Branche** : `p3-business-rules` · **Phase** : P3-5 · **Périmètre** : Backend uniquement.
- **Base d'implémentation (HEAD de départ)** : `c246ebc4d023fae8f157b6acc1653944a6d36ff0` (`feat(P3-4B): enforce product business rules`).
- **Objectif** : rendre toutes les mutations de stock **sûres, atomiques, traçables et idempotentes en multi-poste**,
  au plus petit périmètre. Machine à états des commandes → **P3-6** ; validations monétaires → **P3-7** ; UI → redesign.

## 2. État Git et CI de départ

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `c246ebc…6ff0` | `c246ebc4d023fae8f157b6acc1653944a6d36ff0` | ✅ |
| `git rev-parse origin/p3-business-rules` | = HEAD | `c246ebc4d023fae8f157b6acc1653944a6d36ff0` | ✅ |
| `git status --short` | seul le rapport d'audit P3-5 (+ `design*/docs/ui` non suivis) | idem | ✅ |
| `git diff --check` | propre | propre | ✅ |

## 3. Baseline

| Commande | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet restore MMV.sln` | à jour | à jour | ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | 0 warning / 0 erreur | `0 Warning(s), 0 Error(s)` | ✅ |
| `dotnet test MMV.sln --no-build` | 799 réussis | Domain 297 / Application 263 / App 239 = **799**, 0 échec | ✅ |
| `dotnet ef migrations has-pending-model-changes` | aucune | *No changes have been made to the model since the last migration.* | ✅ |

## 4. Décisions retenues

1. **Politique unique de mutation** : les trois primitives (décrément, incrément, ajustement) vivent dans
   `IStockMutationService` (Domain, provider-neutral). Plus **aucune** écriture directe de `StockQuantity` dans les
   use cases de mutation. L'implémentation EF utilise des `ExecuteUpdateAsync` atomiques.
2. **Convention de signe unique** du mouvement : `In` = +q, `Out` = −q, `Adjustment` = (nouvelle − ancienne). Le
   `Quantity` d'un mouvement est **toujours un delta signé** ⇒ la somme reconstitue la variation de stock.
3. **Garde d'idempotence de décrément** pour la fabrication : prise de statut **atomique conditionnelle**
   (`IOrderRepository.TryTransitionStatusAsync`), pas une comparaison mémoire. **Pas** de matrice de transitions (P3-6).
4. **Ajustement concurrent-safe** : lecture puis mise à jour conditionnelle sur la valeur lue ; conflit ⇒
   `StockConcurrencyConflictException` (jamais de last-write-wins silencieux).
5. **Découplage stock / édition catalogue** : `UpdateProductUseCase` ne réécrit plus `StockQuantity` (colonne
   **exclue** de l'`UPDATE`), éliminant le lost update. Le **stock initial** de la création reste toléré (aucun
   mouvement `In` initial créé — report explicite).
6. **Non-verres décrémentés à la vente** (comptoir **et** fabrication) ; verres/lentilles décrémentés uniquement à
   la fabrication. Aucun double décrément (un non-verre n'entre jamais dans l'`Order`).
7. **Aucune migration** : tous les changements sont applicatifs/infra ; le schéma est inchangé.

## 5. Évolution de `IStockMutationService`

Interface Domain étendue (aucune dépendance EF introduite) :

| Méthode | Sémantique | Erreurs contrôlées |
|---|---|---|
| `DecrementStockAsync(id, q)` *(inchangée)* | `UPDATE … SET qty = qty − q WHERE id AND qty ≥ q` | `InsufficientStockException` (0 ligne) ; `ArgumentOutOfRangeException` (q ≤ 0) |
| `IncrementStockAsync(id, q) → int` *(nouveau)* | `UPDATE … SET qty = qty + q` puis relecture ; renvoie le nouveau stock | `ArgumentOutOfRangeException` (q ≤ 0) ; `EntityNotFoundException` (0 ligne) |
| `AdjustStockToAsync(id, target) → StockAdjustmentResult` *(nouveau)* | lit la valeur courante, puis `UPDATE … SET qty = target WHERE id AND qty = valeur lue` | `ArgumentOutOfRangeException` (target < 0) ; `EntityNotFoundException` (introuvable) ; `StockConcurrencyConflictException` (0 ligne) |

Nouveau type Domain `StockAdjustmentResult(ProductId, PreviousQuantity, NewQuantity)` avec `Delta = NewQuantity − PreviousQuantity`.
Implémentation `EfStockMutationService` : incrément/ajustement atomiques via `ExecuteUpdateAsync` ; relectures en
`AsNoTracking` (valeur réelle en base, jamais une entité suivie obsolète). Toutes les opérations partagent le
`DbContext` de la portée ⇒ participent à la transaction ouverte par `ITransactionRunner`.

## 6. Convention de signe

`StockMovement.Quantity` = **delta signé** (doc entité mise à jour) : `In` +q, `Out` −q, `Adjustment` (nouvelle − ancienne).
Producteurs alignés :

- `RegisterSaleUseCase` : déjà `−q` pour `Out` (conforme).
- `CreateStockMovementUseCase` : `Out` → `−q`, `In` → `+q`, `Adjustment` → delta réel.
- `AdvanceOrderStatusUseCase` : `Out` de fabrication passe de `+q` (incohérent) à `−q`.

Lecteurs vérifiés : aucun test ni ViewModel ne dépendait d'un `Out` positif (les affichages utilisent `MovementType`,
pas le signe). Les deux assertions de tests qui figeaient l'ancien signe positif ont été corrigées vers le signe négatif.

> **Réserve explicite (historique).** La convention `In` = positif, `Out` = négatif, `Adjustment` = delta signé est
> **garantie pour toutes les nouvelles écritures créées après P3-5**. **Aucune migration de données n'a été créée** :
> d'anciens `StockMovement` de type `Out` peuvent donc conserver une **quantité positive** s'ils ont été écrits par
> l'ancien comportement (pré-P3-5). Les lecteurs actuels utilisent `MovementType` et **aucun calcul identifié**
> ne reconstitue actuellement le stock par simple somme de `Quantity`. Une **normalisation historique dédiée**
> devra être décidée avant qu'un calcul comptable ou analytique dépende de cette somme.

## 7. Correction `AdvanceOrderStatusUseCase`

- Injection de `ITransactionRunner` **et** `IStockMutationService` (constructeur étendu ; `INotificationRepository?`
  reste optionnel en dernier).
- Tout le flux (prise atomique du statut → décréments → mouvements → notification → `SaveChanges`) est exécuté dans
  **un seul** `ITransactionRunner`. Toute erreur (stock, conflit) ⇒ **rollback total** (statut non avancé, aucun
  mouvement, aucune notification).
- Le décrément direct `item.Product.StockQuantity -= item.Quantity` est remplacé par
  `IStockMutationService.DecrementStockAsync` (jamais négatif ; `InsufficientStockException` ⇒ rollback).
- Le mouvement `Out` de fabrication utilise `Quantity = −item.Quantity`.

## 8. Prise atomique du statut

`IOrderRepository.TryTransitionStatusAsync(orderId, expectedStatus, nextStatus)` : un unique
`UPDATE Orders SET Status = @next WHERE OrderId = @id AND Status = @expected` (via `ExecuteUpdateAsync`).
`rows == 1` ⇒ transition prise ; `rows == 0` ⇒ statut stocké différent (déjà avancé, rejoué, ou introuvable) ⇒
`OrderStatusConflictException` ⇒ rollback. Deux postes exécutant simultanément `ToFabricate → InProgress`
produisent **exactement** : un succès, un ensemble de décréments/mouvements, et un **refus contrôlé** pour le second —
**jamais** de double décrément. Cette garde est **uniquement** l'idempotence de décrément de P3-5 ; la **matrice
complète** des transitions autorisées reste **P3-6**.

## 9. Correction `UpdateProductUseCase`

- L'écriture `product.StockQuantity = command.StockQuantity` est **supprimée**.
- La persistance passe par `IProductRepository.UpdateCatalogAsync`, qui marque toutes les colonnes modifiées **puis
  exclut explicitement `StockQuantity`** de l'`UPDATE` (`Entry(product).Property(p => p.StockQuantity).IsModified = false`).
  Une édition catalogue concurrente ne peut donc plus **écraser** un décrément survenu entre l'ouverture du formulaire
  et l'enregistrement (élimination du lost update, sans mouvement fantôme).
- `command.StockQuantity` reste dans la commande (compatibilité des appelants) mais **n'est jamais persisté**.
- La **création** (`CreateProductUseCase`) conserve son stock initial ; **aucun** mouvement `In` initial n'est créé
  (report explicite P3-5).

## 10. Correction `RegisterSaleUseCase`

- La boucle de décrément est **sortie** du garde `if (command.IsCounterSale)` : les produits **non-verre**
  (montures, clips, accessoires, solaires…) sont désormais décrémentés **à l'enregistrement de la vente**, que la
  vente soit **comptoir ou fabrication**.
- Les **verres/lentilles** (`VERRE`/`LENTILLE`) restent exclus du décrément à la vente (décrémentés à la fabrication).
- Aucun double décrément : un non-verre n'entre jamais dans l'`Order` (seuls les verres y sont ajoutés).
- Mutation + mouvement (`Out`, `Quantity = −q`) restent dans la transaction de la vente. Motif généralisé
  (« Vente {n°} — Client #… », le terme « comptoir » retiré car applicable aussi à la fabrication).

## 11. Ajustement concurrent-safe

`CreateStockMovementUseCase` branche `Adjustment` : `product.StockQuantity = command.Quantity` (+ `UpdateAsync`) est
remplacé par `IStockMutationService.AdjustStockToAsync(target)`. Le service lit la valeur courante puis tente un
`UPDATE … WHERE StockQuantity = valeur lue`. Un autre poste ayant modifié le stock entre-temps ⇒
`StockConcurrencyConflictException` ⇒ **rollback complet** (aucun mouvement conservé). Le mouvement enregistre le
**delta réel** (`nouvelle − ancienne`). **Règle exacte tranchée par le code** : cible ≥ 0, **zéro autorisé**,
négatif refusé (`ArgumentOutOfRangeException`) — la contradiction « ≥ 1 » du rapport d'audit (§11) a été corrigée
en conséquence.

## 12. Fichiers modifiés et créés

**Domain (6)** : `Entities/StockMovement.cs` (doc convention), `Exceptions/DomainExceptions.cs` (+`StockConcurrencyConflictException`,
+`OrderStatusConflictException`), `Interfaces/Persistence/IStockMutationService.cs` (+2 méthodes),
`Interfaces/Persistence/StockAdjustmentResult.cs` *(nouveau)*, `Interfaces/Repositories/IOrderRepository.cs`
(+`TryTransitionStatusAsync`), `Interfaces/Repositories/IProductRepository.cs` (+`UpdateCatalogAsync`).

**Infrastructure (3)** : `Persistence/EfStockMutationService.cs` (+increment/adjust), `Repositories/OrderRepository.cs`
(+transition atomique), `Repositories/ProductRepository.cs` (+update catalogue hors stock).

**Application (4)** : `Sales/RegisterSale/RegisterSaleUseCase.cs`, `Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs`,
`Stock/CreateStockMovement/CreateStockMovementUseCase.cs`, `Products/UpdateProduct/UpdateProductUseCase.cs`.

**Tests (6)** : `Domain.Tests/Persistence/EfStockMutationServiceTests.cs`,
`Application.Tests/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs`,
`Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs`,
`Application.Tests/UseCases/Stock/CreateStockMovementUseCaseTests.cs`,
`Application.Tests/UseCases/Products/ProductBusinessRulesTests.cs`,
`Application.Tests/UseCases/Products/ProductUseCasesTests.cs`.

**Docs** : rapport d'audit P3-5 (correction §11), ce rapport, roadmap P3-business-rules.

**Aucun** fichier `src/MMV.App/**`, **aucune** migration, **aucun** fichier `design*/docs/ui`.
DI : inchangée — `ITransactionRunner` et `IStockMutationService` étaient déjà enregistrés (App.axaml.cs), donc les
nouveaux paramètres de constructeur sont résolus automatiquement (aucun fichier App modifié).

## 13. Tests ajoutés (+20)

**`EfStockMutationServiceTests` (+10, Domain)** : incrément valide (renvoie nouveau stock) ; incrément quantité ≤ 0
(théorie ×2) ; incrément produit introuvable ; **deux incréments concurrents sans perte** ; ajustement cible haute
(delta +) ; cible basse (delta −) ; ajustement à zéro autorisé ; cible négative refusée ; **deux ajustements
concurrents (jamais de last-write-wins silencieux)**.

**`AdvanceOrderStatusUseCaseTests` (+3)** : fabrication à stock insuffisant → `InsufficientStockException`, **rollback
total** (statut/stock inchangés, aucun mouvement, aucune notification) ; **répétition** de la transition → refus
contrôlé, **pas de double décrément** ; **deux exécutions concurrentes** → au plus un succès, stock décrémenté au plus
une fois, au plus un mouvement.

**`CreateStockMovementUseCaseTests` (+2)** : ajustement enregistre le **delta signé** (−3 pour 5→2) ; **conflit
d'ajustement** → `StockConcurrencyConflictException`, rollback, aucun mouvement conservé.

**`RegisterSaleUseCaseTests` (+3)** : vente **fabrication** décrémente le non-verre à la vente (verre non) ; plusieurs
lignes du même non-verre → stock final correct ; combinaison monture + verre → **chacun décrémenté au bon moment**
(vente vs fabrication).

**`ProductBusinessRulesTests` (+2)** : édition catalogue ne réécrit pas le stock (autres champs mis à jour) ; **un
décrément concurrent survenu après le chargement n'est pas écrasé** par l'update.

**Existants ajustés** : `AdvanceOrderStatus` (mouvement `Out` −2, constructeurs) ; `CreateStockMovement` (`Out` −3) ;
`ProductUseCases` (Update ne change plus le stock).

*Note concurrence (SQLite mono-écrivain).* L'idempotence du décrément est **prouvée de façon déterministe** par le test
de répétition (`RepeatedFabricationTransition…`). Le test réellement concurrent (`TwoConcurrent…`) affirme l'**invariant
de sûreté** (jamais de double décrément, tout échec est un refus contrôlé), non un ordre d'exécution — approche robuste
et non flaky, cohérente avec le test de concurrence existant du service.

## 14. Résultats ciblés

- `dotnet test tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj` : **307** réussis (297 + 10), 0 échec.
- `dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj` : **273** réussis (263 + 10), 0 échec.
- Tests de concurrence rejoués **3×** : stables (aucune intermittence).

## 15. Résultat complet

`dotnet test MMV.sln --no-build -c Debug` :

| Assembly | Réussis | Échecs | Ignorés |
|---|---|---|---|
| MMV.Domain.Tests | 307 | 0 | 0 |
| MMV.Application.Tests | 273 | 0 | 0 |
| MMV.App.Tests | 239 | 0 | 0 |
| **Total** | **819** | **0** | **0** |

## 16. Migrations

**Aucune migration créée.** Aucun changement de modèle EF. `dotnet ef migrations has-pending-model-changes --project
src/MMV.Infrastructure --no-build` ⇒ *No changes have been made to the model since the last migration.* Aucune
contrainte SQL `CHECK` ajoutée (report explicite).

## 17. Frontières

- `dotnet list src/MMV.Application reference` ⇒ **`MMV.Domain` uniquement** (frontière Application → Domain préservée).
- `dotnet list src/MMV.Application package` ⇒ `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul
  (aucune dépendance EF/SQLite dans Application).
- `dotnet list MMV.sln package --vulnerable --include-transitive` ⇒ aucune vulnérabilité.
- Aucun fichier App requis pour les tests métier (tous au-dessus de vrai SQLite + repositories/ports réels).

## 18. Reports explicites (non traités, conformes au cadrage)

- **Matrice complète des statuts Commande** → P3-6 (P3-5 ne pose que la garde de décrément idempotente).
- **Validations monétaires / client archivé en vente** → P3-7.
- **`PerformedByUserId`** non renseigné (aucune identité utilisateur fournie proprement aux use cases).
- **Liens structurés `SaleId`/`OrderId`** dans `StockMovement` ; **immutabilité/versionnement** des mouvements.
- **Mouvement `In` initial** à la création produit (stock initial conservé tel quel).
- **Contrainte DB `CHECK(StockQuantity >= 0)`** (aucune migration en P3-5).
- **Provider PostgreSQL/SQL Server + token de concurrence de ligne** → chantier production futur (ADR-PROD-DB-001) ;
  **pas** de SQLite réseau.
- **UI** (`StockMovementFormViewModel`, `InventoryViewModel`, affichages) → redesign global. Vérifié : ces ViewModels
  attrapent déjà les exceptions de façon générique, donc les nouveaux refus contrôlés s'affichent proprement.
- **Service legacy `ProductService`** : conservé (n'empêche pas la compilation).

## 19. État Git final

- HEAD inchangé : `c246ebc4d023fae8f157b6acc1653944a6d36ff0` (**aucun commit, aucun push**).
- `git diff --check` : propre.
- Périmètre (`git diff --name-only` + non suivis) : Domain (stock/exceptions/ports), Application (stock/vente/commande/produit),
  Infrastructure (mutations atomiques + transition conditionnelle), tests backend, rapport d'audit (correction §11), ce
  rapport, roadmap, + `StockAdjustmentResult.cs` (nouveau, Domain). **Aucun** `src/MMV.App/**`, **aucune** migration,
  **aucun** `design*/docs/ui`.

## 20. Verdict

**P3-5 = GO LOCAL**

- ✅ Toutes les mutations post-création passent par `IStockMutationService`.
- ✅ Aucun décrément ne peut rendre le stock négatif (update conditionnel).
- ✅ La fabrication ne peut pas décrémenter deux fois (prise de statut atomique + idempotence prouvée).
- ✅ `UpdateProduct` ne réécrit plus le stock (colonne exclue de l'`UPDATE`).
- ✅ Les non-verres d'une vente fabrication sont décrémentés à la vente.
- ✅ Les mouvements utilisent une convention signée cohérente (In +q / Out −q / Adjustment delta).
- ✅ 819 tests réussis, 0 échec.
- ✅ Aucun modèle EF changé (aucune migration).
- ✅ Les rapports `.md` existent physiquement dans `docs/implementation/`.
- ✅ Aucune UI modifiée.

**Aucun commit. Aucun push.** Le verdict **définitif** P3-5 reste subordonné au vert de la CI.
