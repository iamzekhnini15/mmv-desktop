# P3-5 — Audit backend Stock et mouvements (rapport d'audit)

> **MODE** : AUDIT_AND_WRITE_REPORT_ONLY — cartographie et analyse des chemins de mutation du stock ; **aucun
> code, aucune migration, aucun test, aucune UI, aucun commit, aucun push**. Le seul fichier suivi produit est
> ce rapport.
>
> **Le code réel prime sur les documents.** Les ADR P2A (`adr-stock-concurrency`, `adr-transaction-idempotency`)
> décrivent un état antérieur où l'orchestration vivait dans les ViewModels ; la migration P2B/P2D a déplacé
> cette orchestration dans `MMV.Application`. Cet audit reflète l'**état réel du dépôt au HEAD ci-dessous**.

## 1. Paramètres

- **Repo** : iamzekhnini15/mmv-desktop · **Branche** : `p3-business-rules` · **Phase** : P3-5 · **Périmètre** : Backend uniquement.
- **HEAD attendu / réel** : `c246ebc4d023fae8f157b6acc1653944a6d36ff0` (`feat(P3-4B): enforce product business rules`).
- **CI attendue** : run push `29620335496`.

## 2. État Git et CI

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `c246ebc…6ff0` | `c246ebc4d023fae8f157b6acc1653944a6d36ff0` | ✅ |
| `git rev-parse origin/p3-business-rules` | = HEAD | `c246ebc4d023fae8f157b6acc1653944a6d36ff0` | ✅ |
| `git status --short` | seuls `design-handoff/`, `design/`, `docs/ui/` non suivis | idem (rien d'autre) | ✅ |
| `git diff --check` | propre | propre | ✅ |
| CI `29620335496` | `completed` / `success`, SHA exact | `completed` / `success`, `headSha = c246ebc…6ff0`, event `push`, branche `p3-business-rules`, job « Restore / Build / Test / Scan » vert (build, test, audit vuln, EF pending — tous `success`) | ✅ |

**Git et CI correspondent exactement. Audit autorisé à continuer.**

## 3. Baseline locale

| Commande | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet restore MMV.sln` | à jour | tous projets à jour | ✅ |
| `dotnet build MMV.sln --no-restore -c Debug` | 0 warning / 0 erreur | `Build succeeded. 0 Warning(s), 0 Error(s)` | ✅ |
| `dotnet test MMV.sln --no-build -c Debug` | 799 réussis, 0 échec, 0 ignoré | Domain **297** / Application **263** / App **239** = **799**, 0 échec, 0 ignoré | ✅ |
| `dotnet list MMV.sln package --vulnerable --include-transitive` | aucune vulnérabilité | aucune (7 projets, transitives incluses) | ✅ |
| `dotnet ef migrations has-pending-model-changes` | aucune en attente | *No changes have been made to the model since the last migration.* | ✅ |
| `dotnet list src/MMV.Application reference` | `MMV.Domain` uniquement | `..\MMV.Domain\MMV.Domain.csproj` uniquement | ✅ |
| `dotnet list src/MMV.Application package` | Domain + DI.Abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul package direct | ✅ |

**Baseline conforme.** Frontière `Application → Domain` préservée.

## 4. Documents lus

- `docs/architecture/P3-business-rules-roadmap.md` (§P3-5, §4 points durs, §5 impact multi-poste).
- `docs/implementation/P3-4A-product-domain-audit-report.md` (report « écriture directe du stock → P3-5 »).
- `docs/implementation/P3-4B-product-business-rules-report.md` (§16 dette P3-5 : écriture directe `StockQuantity` par Create/Update, aucun mouvement).
- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md` (§5 : décrément atomique obligatoire, `AdvanceOrderStatusUseCase` « doublement dangereux » en multi-poste).
- `docs/architecture/adr-application-boundaries.md` (§4 frontières, §9 validation, propriété de couche).
- `docs/architecture/adr-stock-concurrency.md` (P2A-1D : `IStockMutationService`, update conditionnel, flux #4 fabrication laissé inchangé).
- `docs/architecture/adr-transaction-idempotency.md` (P2A-1C : `ITransactionRunner`, atomicité multi-`SaveChanges`).

## 5. Cartographie

| Élément | Chemin exact | Couche | Responsabilité |
|---|---|---|---|
| `Product.StockQuantity` | `src/MMV.Domain/Entities/Product.cs:97` | Domain | Quantité en stock (int, propriété publique nue, aucun invariant) |
| `StockMovement` | `src/MMV.Domain/Entities/StockMovement.cs` | Domain | Ligne d'audit : `ProductId`, `MovementType`, `Quantity` (delta signé), `Reason` (texte libre), `PerformedByUserId?`, `CreatedAt` |
| `StockMovementType` | `src/MMV.Domain/Enums/StockMovementType.cs` | Domain | enum `In` / `Out` / `Adjustment` |
| `IStockMutationService` | `src/MMV.Domain/Interfaces/Persistence/IStockMutationService.cs` | Domain | Port : `DecrementStockAsync(productId, quantity, ct)` — décrément atomique conditionnel, jamais négatif |
| `InsufficientStockException` | `src/MMV.Domain/Exceptions/DomainExceptions.cs` | Domain | Erreur métier contrôlée (`ProductId`, `RequestedQuantity`, `AvailableQuantity`) |
| `ProductValidator` | `src/MMV.Domain/Validators/ProductValidator.cs` | Domain | `StockQuantity >= 0` (utilisé par Create/UpdateProduct) |
| `IStockMovementRepository` | `src/MMV.Domain/Interfaces/Repositories/IStockMovementRepository.cs` | Domain | CRUD + requêtes historiques mouvements |
| `IProductRepository` | `src/MMV.Domain/Interfaces/Repositories/IProductRepository.cs` | Domain | CRUD produit + `GetByIdAsync` |
| `ITransactionRunner` / `IUnitOfWork` | `src/MMV.Domain/Interfaces/Persistence`, `.../Repositories` | Domain | Frontière transactionnelle / `SaveChangesAsync` |
| `RegisterSaleUseCase` | `src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs` | Application | Vente : décrément **sûr** (comptoir, hors verres) + mouvement, dans `ITransactionRunner` |
| `AdvanceOrderStatusUseCase` | `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` | Application | Avancement statut : décrément **direct** `-=` à la fabrication (**contournement**) |
| `CreateStockMovementUseCase` | `src/MMV.Application/UseCases/Stock/CreateStockMovement/CreateStockMovementUseCase.cs` | Application | Mouvement manuel : Out (sûr), In (incrément), Adjustment (set absolu), dans `ITransactionRunner` |
| `CreateProductUseCase` / `UpdateProductUseCase` | `src/MMV.Application/UseCases/Products/{Create,Update}Product/…` | Application | Écriture **absolue directe** de `StockQuantity` (validée ≥ 0, **sans mouvement**) |
| `GetInventoryOverviewUseCase` | `src/MMV.Application/UseCases/Products/GetInventoryOverview/…` | Application | **Lecture** seule (inventaire) |
| `ListStockMovementsUseCase` | `src/MMV.Application/UseCases/Stock/ListStockMovements/…` | Application | **Lecture** seule (historique) |
| `EfStockMutationService` | `src/MMV.Infrastructure/Persistence/EfStockMutationService.cs` | Infrastructure | `UPDATE … SET qty = qty - @q WHERE ProductId=@id AND qty >= @q` via `ExecuteUpdateAsync` |
| `StockMovementRepository` | `src/MMV.Infrastructure/Repositories/StockMovementRepository.cs` | Infrastructure | Persistance mouvements (lecture riche, création simple) |
| `StockMovementConfiguration` | `src/MMV.Infrastructure/Data/Configurations/StockMovementConfiguration.cs` | Infrastructure | FK `Product` **Restrict** (P3-4B), FK `User` `SetNull`, index `ProductId`, **aucun CHECK** |
| `ProductConfiguration` | `src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs` | Infrastructure | Colonne `StockQuantity` INTEGER défaut 0, **aucune contrainte ≥ 0** |
| ViewModels stock | `SaleFormViewModel`, `OrderDetailViewModel`, `InventoryViewModel`, `StockMovementFormViewModel`, `ProductFormViewModel` | UI | **Délèguent tous** aux use cases ci-dessus — **plus aucune écriture directe de stock en VM** (vérifié) |
| Seed | `src/MMV.Infrastructure/Data/DbInitializer.cs:859`, `Seeders/DbSeeder.cs` | Infrastructure | Données démo (`-=` et set absolus) — **hors périmètre applicatif supporté** |

## 6. Recherche exhaustive des écritures

Résultat de `StockQuantity\s*(\+=|-=|=)` sur `src/**/*.cs` (hors lectures / projections DTO), croisé avec les
usages de `IStockMutationService`, `SaveChanges`, `ExecuteUpdate`, `ExecuteSql` :

| Chemin d'écriture | Déclencheur | Transaction | Mouvement créé | Négatif empêché | Multi-poste sûr |
|---|---|---|---|---|---|
| `RegisterSaleUseCase` L203 `DecrementStockAsync` | Vente comptoir, produit **non-verre** | ✅ `ITransactionRunner` | ✅ (`Out`, `Quantity = -qty`) | ✅ (update conditionnel) | ✅ (atomique conditionnel) |
| `AdvanceOrderStatusUseCase` L140 `item.Product.StockQuantity -= item.Quantity` | Transition `ToFabricate → InProgress` | ⚠️ `SaveChanges` unique implicite (pas de runner) | ✅ (`Out`, `Quantity = +qty`) | ❌ **aucune garde** | ❌ **read-modify-write** |
| `CreateStockMovementUseCase` L90 `DecrementStockAsync` (Out) | Sortie manuelle / vente atelier | ✅ `ITransactionRunner` | ✅ (`Out`) | ✅ | ✅ |
| `CreateStockMovementUseCase` L96 `+= command.Quantity` (In) | Entrée manuelle | ✅ `ITransactionRunner` | ✅ (`In`) | n/a (incrément) | ⚠️ read-modify-write, mais incrément (pas de perte critique) |
| `CreateStockMovementUseCase` L103 `= command.Quantity` (Adjustment) | Ajustement inventaire (valeur absolue) | ✅ `ITransactionRunner` | ✅ (`Adjustment`) | ✅ (quantité saisie ≥ 1) | ❌ **last-write-wins** (set absolu depuis valeur comptée) |
| `CreateProductUseCase` L75 `StockQuantity = command.StockQuantity` | Création produit | ✅ `ITransactionRunner` | ❌ **aucun** | ✅ (validateur ≥ 0) | ⚠️ set absolu à la création (peu concurrent) |
| `UpdateProductUseCase` L62 `StockQuantity = command.StockQuantity` | Édition produit | ✅ `ITransactionRunner` | ❌ **aucun** | ✅ (validateur ≥ 0) | ❌ **last-write-wins** (écrase tout décrément concurrent) |
| `DbInitializer` L859 / `DbSeeder` | Seed / démo | seed | ❌ | non | n/a (hors flux supporté) |

**Aucun** `ExecuteSql*` / `ExecuteUpdate` n'écrit le stock hors `EfStockMutationService` (l'update conditionnel).
`ExecuteSqlRaw` n'apparaît que dans `SqliteDatabaseManager` (journal de migration, sans rapport avec le stock).
**Une règle UI n'est jamais comptée comme protection backend** (tous les VM délèguent, mais la garde doit vivre
dans le use case / le Domain / la base).

## 7. Service de mutation (question A)

- **Interface** : `IStockMutationService.DecrementStockAsync(long productId, int quantity, ct)` (Domain, sans EF).
- **Implémentation** : `EfStockMutationService` — `_context.Products.Where(p => p.ProductId == id && p.StockQuantity >= quantity).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity))`. `rows == 1` ⇒ succès ; `rows == 0` ⇒ relecture du stock puis `InsufficientStockException`.
- **Use cases qui l'utilisent réellement** : `RegisterSaleUseCase` (comptoir, hors verres), `CreateStockMovementUseCase` (branche `Out`). Via l'UI : `SaleFormViewModel`, `StockMovementFormViewModel` (sortie), `InventoryViewModel` (ajustement — via la branche `Adjustment`, qui **n'appelle pas** le service mais fait un set absolu).
- **Use cases qui le contournent** : **`AdvanceOrderStatusUseCase`** (décrément direct `-=`), `CreateProductUseCase` / `UpdateProductUseCase` (set absolu direct), branches `In` et `Adjustment` de `CreateStockMovementUseCase` (par nature non-décrément).
- **Atomicité mutation + mouvement** : ✅ pour Register/CreateStockMovement (même transaction) ; ⚠️ pour Advance (mouvement + `-=` dans le même `SaveChanges` unique — atomique **techniquement**, mais le `-=` n'est pas conditionnel).
- **Read-modify-write classique ?** Non pour le service (update conditionnel atomique). **Oui** pour `AdvanceOrderStatusUseCase` et les set absolus produit/ajustement.
- **Mise à jour conditionnelle en base ?** Oui, uniquement dans `EfStockMutationService`.
- **Deux postes peuvent-ils décrémenter simultanément et rendre négatif ?** Via le **service** : non (le `WHERE qty >= @q` l'interdit). Via **`AdvanceOrderStatusUseCase`** : **oui** (voir §9).
- **Résultat en cas de stock insuffisant** : `InsufficientStockException` (métier, contrôlée), propagée inchangée par le runner ⇒ rollback. Message **stable et provider-neutre** (`InsufficientStockException`, aucun texte SQLite/EF brut).

## 8. Stock négatif (question B)

- **Où le négatif est-il bloqué aujourd'hui ?** Uniquement par `EfStockMutationService` (clause `WHERE qty >= @q`) — donc pour les flux qui passent par `DecrementStockAsync` (vente comptoir non-verre, sortie manuelle). Le validateur produit (`StockQuantity >= 0`) empêche un **set absolu négatif** à la création/édition produit.
- **Couche de la protection** : Infrastructure (update conditionnel) pour les décréments ; Domain (validateur) pour les set absolus produit. **Aucune protection Domain d'invariant** sur `Product.StockQuantity` lui-même (propriété nue). **Aucune contrainte DB** (`grep HasCheckConstraint` = 0 sur le stock ; colonne INTEGER défaut 0, sans CHECK).
- **`AdvanceOrderStatusUseCase` peut-il rendre le stock négatif ?** **OUI.** `item.Product.StockQuantity -= item.Quantity` (L140) sans aucune garde ni floor. Un produit à stock 1 dans une commande de quantité 2 aboutit à `-1` persisté.
- **`RegisterSaleUseCase` peut-il rendre le stock négatif ?** Non (passe par `DecrementStockAsync`).
- **`CreateStockMovementUseCase` peut-il accepter un décrément supérieur au stock ?** Non pour `Out` (service conditionnel → `InsufficientStockException`, rollback).
- **Create/Update Product peuvent-ils définir un stock négatif ?** Non (validateur `>= 0`) — mais **set absolu direct**, sans mouvement.
- **Contrainte DB empêchant une valeur négative ?** **Non.**
- **Une course entre deux postes peut-elle contourner une vérification préalable ?** Pour Advance : oui (aucune vérification, décrément aveugle). Pour les set absolus (Update Product, Adjustment) : la course écrase (last-write-wins) sans revenir négatif mais en **perdant** un décrément concurrent.

## 9. Double décrément (question C)

Scénarios, avec chemin de code prouvé :

| Scénario | Quand décrémenté | Use case | Mouvement | 2ᵉ exécution redécrémente ? | Statut empêche la répétition ? |
|---|---|---|---|---|---|
| Vente comptoir (non-verre) | à l'enregistrement | `RegisterSaleUseCase` (bloc `IsCounterSale`) | ✅ `Out` (−qty) | non (vente = acte unique, garde `IsSaving` UI) | n/a |
| Vente avec verres (fabrication) | **jamais à la vente** pour les verres | `RegisterSaleUseCase` (exclut les verres) | — | — | — |
| Passage en fabrication | `ToFabricate → InProgress` | `AdvanceOrderStatusUseCase` | ✅ `Out` (+qty) | **OUI** — voir ci-dessous | **NON** (aucune machine à états backend) |
| Livraison / autres transitions | — | `AdvanceOrderStatusUseCase` | non (seule la transition ci-dessus décrémente) | — | — |
| Mouvement manuel | à la saisie | `CreateStockMovementUseCase` | ✅ | oui si resoumis (mais acte manuel explicite) | n/a |
| Modification produit | set absolu | `UpdateProductUseCase` | ❌ | écrase (pas un décrément relatif) | n/a |

**Double décrément prouvé — `AdvanceOrderStatusUseCase` :** le use case décide le décrément sur
`command.CurrentStatus == ToFabricate && command.NextStatus == InProgress` (valeurs **fournies par la
ViewModel**, `AdvanceOrderStatusUseCase.cs:76`). Il recharge `fresh` (`GetWithItemsAsync`) et fait
`fresh.Status = nextStatus` **sans jamais vérifier que `fresh.Status == command.CurrentStatus`**. Conséquences
prouvées par le code :

1. **Deux postes concurrents** : postes A et B affichent la commande à `ToFabricate`. Chacun résout
   `next = InProgress` (`OrderDetailViewModel.GetNextStatus`) et appelle `ExecuteAsync`. **Les deux**
   exécutent `CreateStockMovementsForFabricationAsync` et **décrémentent** ⇒ **double décrément** (et deux
   mouvements `Out`), le stock pouvant passer négatif (aucun floor).
2. **Répétition mono-poste** : rien n'interdit `InProgress → ToFabricate → InProgress`. Comme le use case
   accepte **n'importe quelle** transition (aucune validation), un retour arrière puis ré-avancement
   redécrémente. La machine à états linéaire de `GetNextStatus` (VM) **n'empêche pas** un `CurrentStatus`
   arbitraire dans la commande.

**Double décrément vente + commande** : n'est **pas** prouvé pour un produit donné. Un produit **verre** est
exclu du décrément à la vente et décrémenté **une seule fois** à la fabrication. Un produit **non-verre** est
décrémenté à la vente comptoir et **n'entre jamais** dans une commande fournisseur (`RegisterSaleUseCase`
n'ajoute que les verres à l'`Order`, L158). **Pas de double comptage prouvé** entre ces deux flux.

## 10. Traçabilité (question D)

- **Chaque mutation crée-t-elle un mouvement ?** Non. **Create/Update Product** écrivent `StockQuantity`
  **sans** `StockMovement` (stock initial et corrections d'édition **non tracés**).
- **Contenu d'un `StockMovement`** : `ProductId`, `MovementType`, `Quantity` (delta), `Reason` (texte libre),
  `PerformedByUserId?`, `CreatedAt`. **Manquent** : quantité **avant/après** (seul le delta est stocké) ;
  référence métier **structurée** (pas de FK `SaleId`/`OrderId` — seul le numéro est **noyé dans `Reason`**).
- **Utilisateur** : `PerformedByUserId` existe (FK `User`, `SetNull`) mais **aucun des trois use cases ne le
  renseigne** ⇒ toujours `null`. **Perte d'imputabilité** (qui a fait le mouvement).
- **Incohérence du signe de `Quantity`** : la doc entité dit « positive pour entrée, négative pour sortie ».
  `RegisterSaleUseCase` écrit `-qty` pour un `Out` ; `AdvanceOrderStatusUseCase` et `CreateStockMovementUseCase`
  écrivent `+qty` pour un `Out`. **Le signe d'un mouvement `Out` n'est donc pas fiable** pour reconstituer un
  solde par sommation — il faut se fier au `MovementType`, jamais au signe.
- **Reconstitution de l'origine** : partielle. Le `MovementType` + `Reason` textuel permettent de deviner
  l'origine ; il n'existe **aucun lien référentiel** vers la vente/commande. La transposition « delta →
  solde » est faussée par l'incohérence de signe.
- **Un mouvement peut-il être modifié / supprimé ?** L'entité est mutable et le repository hérite d'un
  `BaseRepository` CRUD ; aucun use case applicatif ne modifie/supprime un mouvement aujourd'hui, mais
  **aucune règle** ne l'interdit (pas d'immutabilité). La **suppression du produit** est désormais protégée
  (FK `Restrict`, P3-4B) ⇒ les mouvements ne sont plus effacés en cascade.
- **Une transaction peut-elle modifier le stock sans mouvement ?** **Oui** : Create/Update Product.

## 11. Ajustement d'inventaire (question E)

- **Valeur absolue ou delta ?** **Valeur absolue** : `CreateStockMovementUseCase` branche `Adjustment` fait
  `product.StockQuantity = command.Quantity` (L103).
- **Qui calcule la différence ?** Personne côté backend : la quantité comptée est posée telle quelle.
  `InventoryViewModel` calcule un écart d'affichage (théorique vs compté) mais transmet la **valeur absolue**.
- **Basé sur une valeur en mémoire ?** Oui : la « quantité théorique » vient de `GetInventoryOverview`
  (lecture) et la valeur absolue est écrite sans revérifier le stock courant.
- **Deux postes ajustant simultanément peuvent-ils s'écraser ?** **Oui** (last-write-wins : le dernier set
  absolu gagne, l'ajustement de l'autre poste est silencieusement perdu).
- **Le mouvement conserve-t-il ancienne + nouvelle quantité ?** **Non** (seul `Quantity = valeur comptée` est
  enregistré, sans le stock antérieur).
- **Justification obligatoire ?** Non (`Reason` facultatif).
- **Ajustement négatif sous zéro ?** La quantité saisie est ≥ 1 (documenté), donc pas de négatif ; mais un
  ajustement à 0 est possible et **aucune** trace du solde antérieur n'est conservée.

> **Correction P3-5 (contradiction levée par le code réel).** Ce rapport d'audit affirmait à la fois une « quantité
> saisie ≥ 1 » (contrainte d'UI, tableau §6 L103) **et** qu'« un ajustement à 0 est possible » — deux affirmations
> incompatibles. La **règle backend réelle établie en P3-5** (`IStockMutationService.AdjustStockToAsync`) tranche :
> la **cible d'ajustement doit être ≥ 0**, **zéro est explicitement autorisé**, une cible **strictement négative**
> est refusée (`ArgumentOutOfRangeException`). Le seuil « ≥ 1 » n'était qu'une contrainte d'UI, jamais une règle
> backend. Le mouvement `Adjustment` enregistre désormais le **delta signé réel** (`nouvelle − ancienne`), et
> l'ajustement est **concurrent-safe** (plus de last-write-wins silencieux — cf. rapport d'implémentation §11).

## 12. Commandes (question F)

`AdvanceOrderStatusUseCase` :

- **Transition déclenchant une mutation** : uniquement `ToFabricate → InProgress` (décision sur les valeurs de
  la commande, pas sur `fresh.Status`).
- **Quantité utilisée** : `item.Quantity` pour chaque `OrderItem` ayant un `ProductId`.
- **Comportement si stock insuffisant** : **aucun** — décrément aveugle, stock négatif possible.
- **Écriture directe** : `item.Product.StockQuantity -= item.Quantity` (L140), **hors** `IStockMutationService`.
- **Mouvement** : créé (`Out`, `Quantity` positif — signe incohérent, cf. §10).
- **Transaction** : `SaveChanges` unique implicite ; **pas** de `ITransactionRunner` injecté. Le décrément
  n'étant pas conditionnel, l'atomicité EF ne protège **pas** du négatif ni du lost update.
- **Répétition** : possible (pas de validation de transition ; `fresh.Status` non comparé à
  `command.CurrentStatus`).
- **Interaction P3-6** : la **machine à états** (transitions autorisées, interdiction des sauts/retours) est
  un sujet **P3-6**. P3-5 doit traiter la **règle Stock** (décrément sûr, idempotent, non négatif) ; il est
  **impossible de garantir la non-répétition du décrément sans un minimum de garde d'état** — voir §17 pour la
  frontière proposée (garde de décrément côté P3-5 vs matrice complète côté P3-6).

## 13. Ventes (question G)

`RegisterSaleUseCase` :

- **Moment du décrément** : après création de la vente et de la commande fournisseur, dans le bloc
  `if (command.IsCounterSale)` (L183), pour les lignes **non-verre**.
- **Service utilisé** : `IStockMutationService.DecrementStockAsync` (sûr).
- **Atomicité vente + stock + mouvements** : ✅ tout est dans un unique `ITransactionRunner.RunAsync`
  (numérotation + vente + commande + décréments + mouvements). Échec ⇒ rollback complet, numéro non consommé.
- **Plusieurs lignes du même produit** : chaque ligne appelle son propre `DecrementStockAsync` conditionnel ;
  correct (le second décrément voit le stock déjà réduit).
- **Rollback si une ligne échoue** : ✅ (`InsufficientStockException` remonte au runner).
- **Concurrence** : ✅ (update conditionnel).
- **Interaction commande déjà décrémentée** : pas de double comptage (les verres, seuls mis en commande, ne
  sont pas décrémentés à la vente ; cf. §9).
- **Gap constaté (non double décrément mais décrément manquant)** : pour une **vente de fabrication**
  (`IsCounterSale == false`), le bloc de décrément est **entièrement sauté**, et l'`Order` créé ne contient
  **que les verres** (L158). Une **monture/accessoire** vendue dans une vente de fabrication n'est donc
  **jamais décrémentée** (ni à la vente, ni à la fabrication) ⇒ **stock surévalué**. Chemin prouvé.
- **Frontière P3-7** : les **validations monétaires** (recalcul `RemainingAmount`, garde acompte ≤ total,
  refus client archivé) appartiennent à **P3-7**. P3-5 ne traite que la **politique Stock**.

## 14. Produits (question H)

- **Écritures directes de `StockQuantity`** : `CreateProductUseCase.cs:75` (création) et
  `UpdateProductUseCase.cs:62` (édition) — **set absolu**, validé `>= 0`, **sans mouvement**.
- **Décision P3-5 (à trancher, après analyse)** : les options réalistes sont
  (a) **interdire la modification directe** du stock après création et forcer tout changement via un mouvement ;
  (b) **convertir le stock initial** de la création en `StockMovement` d'entrée (`In`) ;
  (c) **conserver temporairement** le comportement et ne tracer que par mouvements dédiés ;
  (d) **reporter** une partie.
  - **Constat factuel** : `UpdateProductUseCase` réécrit `StockQuantity` **à chaque édition** depuis la valeur
    du formulaire (`ProductFormViewModel` charge `product.StockQuantity` puis le renvoie dans la commande). En
    multi-poste, une édition catalogue (prix, nom) **réécrit aussi le stock** avec la valeur affichée à
    l'ouverture du formulaire ⇒ **écrase tout décrément survenu entre-temps** (lost update non borné, sans
    mouvement). C'est le risque le plus concret côté Produits.
  - **Recommandation candidate P3-5** (petit périmètre) : **découpler l'édition catalogue de la quantité** —
    `UpdateProductUseCase` ne doit **plus** écrire `StockQuantity` (le stock ne bouge que par mouvement) ; le
    stock **initial** de la création reste toléré (ou converti en mouvement `In`, option b). Décision à
    confirmer en implémentation ; voir §17.

## 15. Analyse multi-poste (§7 du protocole)

| Mutation | Transaction | Atomicité SQL | Update conditionnel | Last-write-wins | Lecture obsolète | Idempotent | Message métier | Mouvement même tx |
|---|---|---|---|---|---|---|---|---|
| Vente comptoir (`RegisterSale`) | ✅ runner | ✅ | ✅ | non | non (décision à l'écriture) | non requis (acte unique) | ✅ `InsufficientStock` | ✅ |
| Sortie / entrée manuelle (`CreateStockMovement`) | ✅ runner | ✅ | ✅ (Out) | In: n/a | non (Out) | non | ✅ (Out) | ✅ |
| Ajustement (`CreateStockMovement` Adjustment) | ✅ runner | ✅ | ❌ (set absolu) | **oui** | **oui** | non | — | ✅ |
| Fabrication (`AdvanceOrderStatus`) | ❌ (SaveChanges unique) | partielle | ❌ (`-=`) | **oui** | **oui** | **non** | ❌ (aucun) | ✅ (même SaveChanges) |
| Édition produit (`UpdateProduct`) | ✅ runner | ✅ | ❌ (set absolu) | **oui** | **oui** | non | ✅ (validateur) | ❌ (aucun) |

**Distinction demandée :**
- **Problème observé** : incohérence de signe des mouvements ; `PerformedByUserId` jamais renseigné.
- **Risque prouvé par le code** : négatif + double décrément + lost update par `AdvanceOrderStatusUseCase` ;
  lost update par `UpdateProductUseCase` (réécriture du stock à chaque édition) et par l'ajustement absolu ;
  décrément manquant des montures en vente de fabrication.
- **Risque théorique** : contention SQLite mono-fichier (sérialisation d'écriture) — atténue l'observabilité
  du double décrément en dev, **mais ne le corrige pas** (l'update conditionnel est requis pour la correction).
- **Sujet base PostgreSQL/SQL Server** : token de concurrence de ligne, migration provider — **hors P3-5**
  (ADR-PROD-DB-001 §7). **Ne pas proposer SQLite réseau.**

## 16. Tests existants et trous de couverture (§8)

| Règle ou risque | Test existant | Niveau | Suffisant ? | Test manquant |
|---|---|---|---|---|
| Incrément (In) | `CreateStockMovementUseCaseTests.ExecuteAsync_In_IncrementsStock…` | Application (vrai SQLite) | ✅ | — |
| Décrément valide (Out) | `…_Out_SufficientStock_Decrements…` ; `EfStockMutationServiceTests.DecrementStockAsync_SufficientStock…` | App + Infra | ✅ | — |
| Décrément insuffisant | `…_Out_MoreThanAvailable_Throws_AndWritesNothing` ; `EfStock…_MoreThanAvailable_ThrowsControlled` | App + Infra | ✅ | — |
| Stock exactement 0 | `…_Out_ExactlyAvailable_ReachesZero` ; `EfStock…_ExactlyAvailable_ReachesZero` | App + Infra | ✅ | — |
| Deux décréments concurrents (service) | `EfStock…_TwoConcurrentOnSingleUnit_OnlyOneSucceeds` | Infra | ✅ (pour le service) | concurrence **au niveau Advance** |
| Jamais négatif (service) | `EfStock…_NeverPersistsNegativeStock` | Infra | ✅ (service) | **négatif via `AdvanceOrderStatus`** |
| Mouvement créé | présent dans Register/CreateStockMovement/Advance | App | ✅ | mouvement **avec `PerformedByUserId`** |
| Rollback si mouvement/vente échoue | `RegisterSale…_InsufficientStock_RollsBack…` ; `EfStock…_SaleWithInsufficientStock_RollsBack…` | App + Infra | ✅ | rollback **Advance** (n'a pas de runner) |
| Plusieurs lignes même produit | (implicite Register) | App | ⚠️ partiel | test dédié multi-lignes même produit |
| Répétition du changement de statut | **aucun** | — | ❌ | **répétition ToFabricate→InProgress = double décrément** |
| Double décrément vente + commande | **aucun** | — | ❌ (non prouvé) | test prouvant l'absence de double comptage verre/monture |
| Ajustement absolu | `…_Adjustment_SetsAbsoluteStock…` | App | ✅ | ajustement **concurrent** (last-write-wins) |
| Ajustement concurrent | **aucun** | — | ❌ | deux ajustements concurrents |
| Stock initial produit | `ProductBusinessRulesTests` (validation ≥ 0) | App | ⚠️ | stock initial **→ mouvement** (si retenu) |
| Modification directe du stock produit | **aucun test de concurrence** | — | ❌ | édition produit **n'écrase pas** un décrément concurrent |
| Suppression / modification d'un mouvement | FK Restrict testée (P3-4B) | App | ⚠️ | immutabilité applicative d'un mouvement |
| **`AdvanceOrderStatus` décrément > stock** | **aucun** (seul le happy path 5→3 existe) | — | ❌ | **décrément insuffisant / négatif à la fabrication** |

**Trou majeur** : `AdvanceOrderStatusUseCaseTests` ne couvre **que** le chemin nominal
(`…_ToFabricateToInProgress_CreatesStockMovement_AndDecrementsStock`, stock 5 → 3). **Aucun** test de stock
insuffisant, négatif, répétition, ou concurrence sur ce flux.

## 17. Constats classés (§9)

### 🔴 Critiques

1. **Stock négatif + lost update via `AdvanceOrderStatusUseCase`** — `AdvanceOrderStatusUseCase.cs:140`
   `item.Product.StockQuantity -= item.Quantity` (read-modify-write, aucun floor, aucun `IStockMutationService`,
   aucun `ITransactionRunner`). *Scénario* : commande de qté 2 sur produit à stock 1 ⇒ `-1` persisté ; deux
   postes ⇒ double décrément. *Impact* : stock faux, invariant « jamais négatif » violé sur le flux fabrication.
   *Propriétaire* : Application (Stock) — **P3-5**.
2. **Double décrément de fabrication (répétition / concurrence)** — `AdvanceOrderStatusUseCase.cs:76` décide
   sur `command.CurrentStatus` sans vérifier `fresh.Status`. *Scénario* : deux postes (ou un retour arrière
   puis ré-avancement) redécrémentent. *Impact* : double sortie de stock, deux mouvements. *Propriétaire* :
   Application (garde d'idempotence de décrément) — **P3-5** ; matrice complète des transitions — **P3-6**.

### 🟠 Importants

3. **Lost update par `UpdateProductUseCase`** — `UpdateProductUseCase.cs:62` réécrit `StockQuantity` (valeur du
   formulaire) à **chaque** édition catalogue. *Scénario* : édition du prix rouvre l'écran, réécrit le stock
   affiché à l'ouverture, écrase un décrément concurrent. *Impact* : perte silencieuse de mouvements de stock,
   sans trace. *Propriétaire* : Application (Produits/Stock) — **P3-5** (découpler stock de l'édition catalogue).
4. **Ajustement d'inventaire non concurrent-safe** — `CreateStockMovementUseCase.cs:103` set absolu depuis une
   valeur comptée en mémoire. *Scénario* : deux ajustements concurrents ⇒ last-write-wins, l'un perdu. *Impact* :
   inventaire faux. *Propriétaire* : Application — **P3-5** (à cadrer : delta ou re-lecture).
5. **Décrément manquant des montures en vente de fabrication** — `RegisterSaleUseCase.cs:183/158` : non-comptoir
   ne décrémente rien et l'`Order` ne contient que des verres. *Scénario* : monture vendue en vente fabrication
   ⇒ stock jamais réduit. *Impact* : stock surévalué. *Propriétaire* : Application (politique Stock) — **P3-5**,
   en coordination avec la clarification du flux vente/commande (**P3-6/P3-7**).

### 🟡 Dette

6. **Signe de `Quantity` incohérent entre mouvements `Out`** — `RegisterSale` (−qty) vs `Advance`/`CreateStock`
   (+qty). *Impact* : un solde ne peut pas être reconstitué par sommation du delta ; il faut le `MovementType`.
   *Propriétaire* : Domain/Application — **P3-5** (choisir une convention unique et la documenter).
7. **`PerformedByUserId` jamais renseigné** — aucun des use cases ne le remplit ⇒ imputabilité perdue.
   *Propriétaire* : Application — **P3-5** (ou report explicite si l'identité utilisateur n'est pas fournie aux
   use cases aujourd'hui).
8. **Mouvement sans quantité avant/après ni référence métier structurée** — seul un delta + `Reason` textuel.
   *Propriétaire* : Domain (modèle `StockMovement`) — **P3-5** (a minima documenter ; enrichissement possible).
9. **Aucune contrainte DB `StockQuantity >= 0`** — filet base absent. *Propriétaire* : Infrastructure — **P3-5**
   (optionnel : CHECK ou report ; noter que SQLite supporte le CHECK).
10. **Couverture de test `AdvanceOrderStatus`** — happy path uniquement. *Propriétaire* : Tests — **P3-5**.
11. **`ProductService` legacy encore enregistré en DI** — chemin d'écriture parallèle latent (déjà signalé
    P3-4A/P3-4B). *Propriétaire* : Application/DI — surveillance (retrait possible en P3-5 ou plus tard).

### 🔵 Reports explicites

- **Machine à états des commandes** (transitions autorisées, interdiction des sauts/retours, suppression selon
  statut) → **P3-6**. P3-5 ne pose que la **garde de décrément** (idempotence + non négatif), pas la matrice.
- **Validations monétaires** (recalcul montants, acompte ≤ total, refus client archivé en vente) → **P3-7**.
- **UI** (alignement `StockMovementFormViewModel` / `InventoryViewModel` / affichages) → redesign global.
- **Provider PostgreSQL/SQL Server + token de concurrence de ligne** → chantier production futur
  (ADR-PROD-DB-001). **Pas de SQLite réseau.**
- **Immutabilité forte / versionnement des mouvements** → transverse, non requis pour P3-5.

## 18. Plan d'implémentation candidat (§10)

> Objectif P3-5 : **unifier la politique de décrément** et la traçabilité, **sans** ouvrir la machine à états
> (P3-6) ni les montants (P3-7). Le plus petit plan sûr :

1. **Corriger `AdvanceOrderStatusUseCase` (cœur du 🔴)** :
   - injecter `IStockMutationService` **et** `ITransactionRunner` ;
   - remplacer `item.Product.StockQuantity -= item.Quantity` par `DecrementStockAsync` (jamais négatif) ;
   - envelopper l'ensemble (statut + mouvements + décréments + notification) dans `ITransactionRunner`
     (`InsufficientStockException` ⇒ rollback complet, statut **non** avancé) ;
   - **garde de décrément idempotente** minimale : ne décrémenter que si `fresh.Status == command.CurrentStatus`
     **et** que la transition `ToFabricate → InProgress` n'a pas déjà produit ses mouvements (vérifier
     l'absence de mouvement de fabrication pour cette commande, ou refuser une transition dont l'état réel
     diffère de l'état attendu). *Ne pas* implémenter la matrice complète (P3-6).
2. **Découpler le stock de l'édition catalogue (🟠 #3)** : `UpdateProductUseCase` **cesse** d'écrire
   `StockQuantity` (le stock ne change que par mouvement). Décision à confirmer pour le **stock initial** de
   `CreateProductUseCase` : le conserver tel quel **ou** le matérialiser en mouvement `In`.
3. **Ajustement concurrent (🟠 #4)** : au choix — recalculer un **delta** (compté − courant relu dans la
   transaction) et l'appliquer atomiquement, **ou** documenter explicitement le last-write-wins comme accepté
   pour une correction d'inventaire. (Le petit périmètre privilégie la 2ᵉ option + test qui fige le contrat.)
4. **Convention de signe unique (🟡 #6)** : décider `Out` = delta **négatif** (ou stocker toujours positif +
   `MovementType`) et **aligner** les trois use cases ; documenter dans `StockMovement`.
5. **Décrément manquant montures en vente fabrication (🟠 #5)** : décider si la monture d'une vente de
   fabrication doit être décrémentée à la vente (comme le comptoir) — probable, à confirmer avec P3-6/P3-7.
6. **Tests à ajouter** (vrai SQLite) : Advance décrément insuffisant / négatif refusé / rollback ; répétition
   ToFabricate→InProgress = pas de double décrément ; concurrence Advance ; édition produit n'écrase pas un
   décrément concurrent ; convention de signe ; (option) ajustement concurrent.
7. **Migration** : **aucune migration requise** pour les corrections 1–6 (aucun changement de schéma).
   Une contrainte DB `CHECK(StockQuantity >= 0)` (🟡 #9) serait la **seule** raison d'une migration — **optionnelle**,
   à trancher (report possible).

**Ordre recommandé** : 1 (Advance) → 4 (signe) → 6 (tests Advance) → 2 (découplage produit) → 5 → 3 →
(éventuel) migration CHECK. **Effets Commandes** : le décrément passe par le service et devient idempotent
(interaction P3-6 documentée, matrice non implémentée). **Effets Ventes** : inchangés pour le comptoir ; #5
touche la vente fabrication (coordination P3-6/P3-7). **Compatibilité** : `IStockMutationService` et
`ITransactionRunner` existent déjà et sont testés ⇒ risque de régression faible ; le principal point de
vigilance est la garde d'idempotence d'Advance (ne pas casser le flux fabrication nominal).

## 19. Migrations éventuelles

- Corrections 1–6 : **aucune migration** (comportement applicatif uniquement, schéma inchangé).
- Optionnel : `CHECK(StockQuantity >= 0)` sur `Products` (filet base) ⇒ **une** migration ; **reportable**.
  Aucune migration créée dans cet audit.

## 20. Reports explicites

- Machine à états des commandes → **P3-6**.
- Validations monétaires / refus client archivé en vente → **P3-7**.
- UI stock/inventaire → redesign global (Claude Design).
- Provider serveur + token de concurrence de ligne → production future (ADR-PROD-DB-001), **pas** SQLite réseau.
- Immutabilité/versionnement des mouvements → transverse, hors P3-5.

## 21. Verdict

**P3-5 AUDIT = GO**

Tous les chemins d'écriture du stock sont cartographiés (§5–§6) ; les risques critiques (stock négatif et
double décrément par `AdvanceOrderStatusUseCase`, lost update par `UpdateProductUseCase`, ajustement absolu
concurrent, décrément manquant des montures en vente fabrication) sont **prouvés par le code** (§8–§14) ; le
plan candidat (§18) est assez précis pour implémenter en petit périmètre, sans migration obligatoire, en
laissant la machine à états à P3-6 et les montants à P3-7 ; le rapport `.md` existe physiquement dans
`docs/implementation/`. **Aucun code, aucune migration, aucun test, aucune UI, aucun commit, aucun push.**
