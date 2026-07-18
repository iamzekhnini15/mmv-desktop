# P3-6 — Audit backend : Commandes et machine à états

> **Mode** : `AUDIT_AND_WRITE_REPORT_ONLY`. Aucun code, test, migration ou UI modifié. Aucun commit, aucun push.
> Ce rapport est le **seul** livrable. Le code réel prime toujours sur les documents cités.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | **P3-6** (Commandes — backend uniquement) |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure / Tests en lecture) |
| HEAD attendu | `3e7a1ab3a5f7936555b1126c488c748d0dcc0096` |
| CI attendue | run `29623464755` |

---

## 2. État Git et CI

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → 3e7a1ab3a5f7936555b1126c488c748d0dcc0096
git rev-parse origin/…      → 3e7a1ab3a5f7936555b1126c488c748d0dcc0096
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
git diff --check            → (aucune sortie)
```

- Branche = `p3-business-rules` ✅
- HEAD local **et** distant = `3e7a1ab…` ✅
- Aucun fichier **suivi** modifié ✅ ; seuls `design-handoff/`, `design/`, `docs/ui/` restent non suivis (autorisés) ✅

CI (`gh run view 29623464755`) :

| Champ | Valeur |
|---|---|
| `databaseId` | 29623464755 |
| `headSha` | `3e7a1ab3a5f7936555b1126c488c748d0dcc0096` |
| `headBranch` | `p3-business-rules` |
| `status` | `completed` |
| `conclusion` | `success` |
| `event` | `push` |

CI = même SHA exact, **completed / success** ✅. **Conditions de démarrage remplies.**

---

## 3. Baseline locale

| Contrôle | Attendu (P3-5) | Observé | Verdict |
|---|---|---|---|
| `dotnet build -c Debug` | sans erreur | **0 erreur / 0 warning** | ✅ |
| `dotnet test -c Debug` | 819 réussis / 0 échec / 0 ignoré | **819** (Domain 307 + Application 273 + App 239) / 0 / 0 | ✅ |
| `dotnet list package --vulnerable` | 0 vulnérabilité | 0 vulnérabilité (7 projets) | ✅ |
| `ef migrations has-pending-model-changes` | aucune | *No changes … since the last migration* | ✅ |
| `MMV.Application` references | Domain uniquement | `..\MMV.Domain\MMV.Domain.csproj` seul (+ `DI.Abstractions` package) | ✅ |

**Baseline conforme.** Le dépôt est propre et reproductible ; l'audit démarre sur cette base.

---

## 4. Documents lus

- `docs/architecture/P3-business-rules-roadmap.md` — §2 (ordre), **§P3-6** (objectif : formaliser le workflow ;
  aujourd'hui « `AdvanceOrderStatusUseCase` accepte **n'importe quelle** transition »), §4 (points durs :
  double machine à états, double rôle d'`Order`, suppression vs archivage, multi-poste).
- `docs/implementation/P3-5-stock-and-movements-audit-report.md` et
  `…-implementation-report.md` — origine de `TryTransitionStatusAsync`, convention de signe P3-5,
  report explicite « matrice complète des statuts Commande → **P3-6** ».
- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md` — V1 production multi-poste (base
  centrale partagée) ; toute règle P3-6 doit rester atomique et robuste à la concurrence.
- `docs/architecture/adr-application-boundaries.md` — un propriétaire de couche par règle ; invariants durs → Domain.
- `docs/architecture/adr-transaction-idempotency.md` — `ITransactionRunner`/`PersistenceException` ; SQLite sans
  `rowversion` (aucun concurrency token structurel) ; frontière transactionnelle unique par `RunAsync`.

Rapports connexes consultés : P3-3 §« prisme perdu au pré-remplissage → P3-6 », P3-4A/P3-4B (FK `Restrict`
`OrderItem.ProductId`), P2B-2D/2H/2I (extraction Create/Delete/Update Order).

---

## 5. Cartographie du domaine Commandes

| Élément | Chemin exact | Couche | Responsabilité |
|---|---|---|---|
| `Order` | `src/MMV.Domain/Entities/Order.cs` | Domain | Entité anémique (pas d'invariant/méthode) ; statut par défaut `New` |
| `OrderItem` | `src/MMV.Domain/Entities/OrderItem.cs` | Domain | Ligne + **données optiques de fabrication** |
| `OrderStatus` | `src/MMV.Domain/Enums/OrderStatus.cs` | Domain | Enum 6 valeurs (voir §6) |
| `OrderItemType` | `src/MMV.Domain/Enums/OrderItemType.cs` | Domain | Frame / LensOd / LensOg / Accessory |
| `IOrderService` / `OrderService` | `src/MMV.Domain/Services/OrderService.cs` | Domain | **Contient une matrice de transitions** — mais **code mort** (§6.3) |
| `OrderStatusConflictException` | `src/MMV.Domain/Exceptions/DomainExceptions.cs:122` | Domain | Levée si la prise atomique n'affecte aucune ligne (P3-5) |
| `IOrderRepository` | `src/MMV.Domain/Interfaces/Repositories/IOrderRepository.cs` | Domain | Contrat repo + `TryTransitionStatusAsync` |
| `CreateOrderUseCase` | `src/MMV.Application/UseCases/Orders/CreateOrder/` | Application | Création manuelle (formulaire) — statut `New` |
| `UpdateOrderUseCase` | `…/UpdateOrder/` | Application | Édition champs + reconstruction lignes ; **ne touche pas** le statut |
| `DeleteOrderUseCase` | `…/DeleteOrder/` | Application | **Suppression physique**, aucune garde |
| `AdvanceOrderStatusUseCase` | `…/AdvanceOrderStatus/` | Application | **Seul** chemin d'avancement de statut ; effets stock + notification |
| `SettleOrderBalanceUseCase` | `…/SettleOrderBalance/` | Application | Encaissement du solde — **n'écrit pas** `Status` (hors périmètre statut) |
| `GetOrderDetails` / `ListOrders` | `…/GetOrderDetails/`, `…/ListOrders/` | Application | Lectures (DTO) |
| `OrderRepository` | `src/MMV.Infrastructure/Repositories/OrderRepository.cs` | Infrastructure | `TryTransitionStatusAsync` via `ExecuteUpdateAsync` |
| `OrderConfiguration` | `src/MMV.Infrastructure/Data/Configurations/OrderConfiguration.cs` | Infrastructure | FK Sale (**Cascade**), FK OrderItems (**Cascade**), index Status/SaleId/OrderNumber(unique) |
| `OrderItemConfiguration` | `…/OrderItemConfiguration.cs` | Infrastructure | Mappe **tous** les champs optiques ; FK Product (**Restrict**, P3-4B) |
| Migrations Order | `…/Migrations/*_AddCounterSaleFieldsToOrder`, `*_AddDepositAndRemainingAmountToOrder`, `*_RestoreSaleOrderSeparation` | Infrastructure | Historique schéma commande |
| `RegisterSaleUseCase` | `src/MMV.Application/UseCases/Sales/RegisterSale/` | Application | **Crée automatiquement** l'Order si la vente contient des verres |
| `OrderDetailViewModel` | `src/MMV.App/ViewModels/OrderDetailViewModel.cs` | UI | Machine à états **vivante** (`GetNextStatus`) + gating QC |
| `OrderKanbanViewModel` | `src/MMV.App/ViewModels/OrderKanbanViewModel.cs` | UI | **Deuxième** copie de `GetNextStatus` |

Relations réelles d'`Order` : **`Sale`** (parent obligatoire, `SaleId` non nullable, cascade) ; **`OrderItem`**
(enfants, cascade) ; **`Product`** (via `OrderItem.ProductId`, nullable, Restrict) ; **`SupplierId`** (nullable,
**jamais renseigné** par aucun chemin d'écriture) ; **`Customer`** seulement indirectement via `Sale.Customer`.
Aucun lien FK vers `Prescription` (copie par valeur), `StockMovement` ou `Notification`.

---

## 6. Enum et machine à états actuelle

### 6.1 Valeurs réelles de `OrderStatus`

Six valeurs, **dans cet ordre** : `New`, `ToFabricate`, `InProgress`, `QualityCheck`, `Ready`, `Delivered`.
**Il n'existe aucune valeur `Cancelled`/`Canceled`.** Persistée en `string` (`HasConversion<string>`), défaut DB `New`.
Statut initial réel = `New` (défaut propriété + défaut DB + affectation explicite dans les deux chemins de création).

### 6.2 Qui décide du prochain statut ?

**La ViewModel**, pas le backend. `OrderDetailViewModel.GetNextStatus()` (et une copie identique dans
`OrderKanbanViewModel.GetNextStatus()`) calcule le cran suivant selon la séquence linéaire, puis transmet le
couple `CurrentStatus → NextStatus` à `AdvanceOrderStatusUseCase` via `AdvanceOrderStatusCommand`.

```
New → ToFabricate → InProgress → QualityCheck → Ready → Delivered   (Delivered ⇒ null : bout de chaîne)
```

### 6.3 Le backend possède-t-il une matrice de transitions ?

**Non, pas dans le chemin vivant.** Trois faits à distinguer :

1. **`AdvanceOrderStatusUseCase` ne valide aucune matrice.** Il prend `CurrentStatus`/`NextStatus` **tels que
   fournis** par l'appelant et appelle `TryTransitionStatusAsync(OrderId, CurrentStatus, NextStatus)`.
2. **`TryTransitionStatusAsync` ne valide pas la légalité du couple.** Son `UPDATE … WHERE OrderId=@id AND
   Status=@expected` vérifie seulement que le **statut stocké** est encore `expected` (garde de concurrence
   optimiste, P3-5). Il applique **n'importe quel** `nextStatus` demandé.
3. **`OrderService.IsValidStatusTransition`** contient bien la matrice linéaire — **mais `OrderService` est du
   code mort** : enregistré dans `DependencyInjection.cs:70` (`AddScoped<IOrderService, OrderService>()`) et
   **consommé par aucun ViewModel ni use case** (vérifié : `IOrderService` n'apparaît qu'en définition + DI ;
   cf. `phase-1-technical-audit.md:160`). Il constitue une **seconde source de vérité trompeuse**.

**Conséquence** : un appelant peut envoyer **n'importe quel** couple `(CurrentStatus, NextStatus)`. La seule
protection est que le statut stocké doit être égal à `CurrentStatus`.

### 6.4 Matrice réelle (comportement actuel du backend)

En notant `S` le statut réellement stocké, `AdvanceOrderStatusUseCase.ExecuteAsync` **réussit** dès que
`CurrentStatus == S` (sinon `OrderStatusConflictException`), **quel que soit `NextStatus`** :

| Depuis (fourni = stocké) | Vers (fourni) | Accepté aujourd'hui ? | Effets déclenchés | Risque |
|---|---|---|---|---|
| `New` | `ToFabricate` | ✅ | Notification | Nominal |
| `ToFabricate` | `InProgress` | ✅ | **Décrément stock + mouvement `Out`** + notification | Nominal (sûr P3-5) |
| `InProgress` | `QualityCheck` | ✅ | Notification | Nominal |
| `QualityCheck` | `Ready` | ✅ | Notification | Nominal (QC non gardé backend — cf. §12) |
| `Ready` | `Delivered` | ✅ | Notification | Nominal (`ReceivedDate` **non renseignée** — cf. §9) |
| `New` | `Delivered` | ✅ (saut d'étapes) | Notification **seule** | 🔴 Livré sans fabrication ni décrément stock |
| `New` | `InProgress` | ✅ (saut) | **Décrément stock** (car `previous==ToFabricate`? **non** → aucun décrément) | 🔴 « En fabrication » sans sortie de stock |
| `Delivered` | `Ready` / `New` / … | ✅ (retour arrière) | Notification | 🔴 Réouverture d'une commande livrée |
| `X` | `X` (même statut) | ✅ (1 ligne affectée) | Notification (+ stock si `X==…`) | 🟠 Avancement no-op « réussi », notification en double |
| `A` (≠ stocké) | `B` | ❌ `OrderStatusConflictException` | Aucun (rollback) | Garde de concurrence P3-5 |

> **Note importante sur le décrément** : les mouvements de stock ne se déclenchent **que** sur la condition
> **codée en dur** `previousStatus == ToFabricate && nextStatus == InProgress`
> (`AdvanceOrderStatusUseCase.cs:96`). Un saut `ToFabricate → Ready` **avance le statut sans jamais décrémenter
> le stock** ; un `New → InProgress` passe « en fabrication » **sans** décrément. La cohérence statut↔stock
> repose donc **entièrement** sur la discipline de la ViewModel, pas sur le backend.

### 6.5 Effets par changement de statut (état réel)

| Effet | Déclencheur réel | Dans la transaction ? |
|---|---|---|
| Décrément stock + mouvement `Out` | `ToFabricate → InProgress` **uniquement** | Oui (runner unique) |
| Notification `OrderStatusChanged` | **Toute** transition acceptée (si repo notif ≠ null) | Oui |
| Date métier (`ReceivedDate`, `EstimatedDelivery`) | **Aucune** — jamais modifiée par l'avancement | — |
| `SaleStatus` synchronisé | **Aucune** — `SaleStatus` reste figé après création (dette P3-7) | — |

Tous les effets sont bien dans **une seule** frontière transactionnelle (`ITransactionRunner.RunAsync`) : un
échec (stock insuffisant, conflit de statut) annule **tout** (prouvé par test §13).

### 6.6 Matrice cible **candidate** (non implémentée)

Séquence linéaire stricte, transitions arrière/sauts interdites, terminal `Delivered` :

| Depuis | Transition autorisée unique |
|---|---|
| `New` | → `ToFabricate` |
| `ToFabricate` | → `InProgress` |
| `InProgress` | → `QualityCheck` |
| `QualityCheck` | → `Ready` |
| `Ready` | → `Delivered` |
| `Delivered` | *(terminal — aucune)* |

À **valider métier** avant de coder (ne pas imposer aveuglément) : (a) faut-il un statut d'**annulation**
distinct (aujourd'hui inexistant — l'annulation passe par une **suppression** destructrice, §8) ? (b)
`ReceivedDate` doit-elle être posée à `Delivered` (ou à un futur statut « reçu du fournisseur », qui révélerait
le double rôle §10) ? Ces deux questions touchent le **modèle** et sont donc signalées, pas tranchées ici.

---

## 7. Inventaire exhaustif des écritures de statut

Recherche `\.Status\s*=` / `ExecuteUpdate` / usages `OrderStatus` sur `src/**`.

| Chemin | Déclencheur | Statut avant | Statut après | Transaction | Garde backend | Multi-poste sûr |
|---|---|---|---|---|---|---|
| `CreateOrderUseCase.cs:52` | Création manuelle (formulaire) | — | `New` | 1 `SaveChanges` (atomique) | Aucune (statut forcé `New`) | ⚠️ voir §9 (`SaleId=0`) |
| `RegisterSaleUseCase.cs:153` | Vente contenant des verres → Order auto | — | `New` | `ITransactionRunner` | Aucune (statut forcé `New`) | ✅ |
| `AdvanceOrderStatusUseCase` → `TryTransitionStatusAsync` (`OrderRepository.cs:72`) | Avancement (seul chemin) | `expected` fourni | `next` fourni | `ITransactionRunner` | **Concurrence seule** (stored==expected) ; **pas de matrice** | ✅ concurrence / ❌ légalité |
| `UpdateOrderUseCase.cs:99` | Édition | `S` | `S` (**inchangé** — relu, pas réécrit) | 1 `SaveChanges` | N/A (ne touche pas le statut) | ⚠️ last-write-wins (§9) |
| `OrderService.cs:52` / `:71` | *(mort)* | — | `New` / `newStatus` avec matrice | 1 `SaveChanges` | Matrice **mais dead code** | — (jamais exécuté) |

**Seul chemin vivant d'avancement = `AdvanceOrderStatusUseCase`.** Il est invoqué par **deux** consommateurs UI
(`OrderDetailViewModel.AdvanceStatusAsync`, `OrderKanbanViewModel.AdvanceStatusAsync`), chacun portant sa propre
copie de `GetNextStatus()`.

---

## 8. Suppression des commandes

`DeleteOrderUseCase` : charge l'entité (`GetByIdAsync`), `DeleteAsync(order)` + un `SaveChanges`. **Aucune garde.**

| Question | Réponse (code réel) |
|---|---|
| Suppression physique ? | **Oui** (hard delete, aucun `IsArchived`/soft-delete sur `Order`) |
| Garde selon le statut ? | **Non** |
| Une commande `Delivered` peut-elle être supprimée ? | **Oui** |
| Une commande `InProgress` (stock déjà décrémenté) ? | **Oui** |
| Une commande liée à une vente ? | **Oui** — `Order` est le **dépendant** ; supprimer l'Order ne touche pas la `Sale` |
| Mécanisme d'annulation / archivage ? | **Aucun** (ni statut `Cancelled`, ni `IsArchived`) |

**Effet en cascade réel** (config EF) :

| Relation | FK | Nullable | DeleteBehavior | Effet d'une suppression d'`Order` |
|---|---|---|---|---|
| `Order` → `Sale` | `Order.SaleId` | Non | **Cascade** (mais Order = dépendant) | La `Sale` **survit** (sens : supprimer la *Sale* cascade vers l'Order, pas l'inverse) |
| `OrderItem` → `Order` | `OrderItem.OrderId` | Non | **Cascade** | Les `OrderItem` (+ **données optiques**) sont **supprimés** |
| `OrderItem` → `Product` | `OrderItem.ProductId` | Oui | **Restrict** (P3-4B) | Sans objet (on supprime la ligne, pas le produit) |
| `StockMovement` → `Order` | *(inexistant)* | — | — | Les mouvements de fabrication **survivent** (liés à `ProductId` seul) ; le stock **reste décrémenté** |
| `Notification` (EntityId=OrderId) | *(pas de FK)* | — | — | Les notifications **survivent** en pointant un `OrderId` **disparu** (référence pendante) |

**Synthèse suppression** : une commande **livrée ou déjà fabriquée** peut être **physiquement effacée**. On perd
alors l'historique de fabrication/commande **et ses lignes optiques**, tandis que (a) le stock reste décrémenté
sans trace reliable à la commande et (b) les notifications deviennent orphelines. La base ne refuse **aucun** cas
de suppression d'`Order`. Une **annulation devrait être un statut** (ou un archivage), pas une suppression — mais
ni statut ni archivage n'existent aujourd'hui. **Preuve métier requise avant d'ajouter `Cancelled`** (impact modèle
+ migration) ; à défaut, la garde minimale P3-6 est de **refuser la suppression selon le statut**.

Tests de suppression existants : `New` + introuvable uniquement. **Aucun test de suppression selon le statut.**

---

## 9. Création et modification

### 9.1 Création

Deux chemins, **fidélité optique différente** :

| Champ | `RegisterSaleUseCase` (auto, vente→verres) | `CreateOrderUseCase` (formulaire manuel) |
|---|---|---|
| `SaleId` | ✅ `sale.SaleId` | 🔴 **jamais renseigné** (`SaleId = 0` — `CreateOrderCommand` n'a pas de champ) |
| Statut initial | `New` | `New` |
| Numéro | séquence `Order` (dans la transaction) | séquence `Order` attribuée à l'ouverture du formulaire (VM), transmise |
| `Sphere/Cylinder/Axis/Addition` | ✅ | ✅ |
| `UsageType` | ✅ | 🟡 **perdu** (absent du DTO `CreateOrderLineCommand`) |
| `PrismValue` / `PrismBase` | ✅ | 🟡 **perdu** (absent du DTO) |
| `VisualAcuity` | ✅ | 🟡 **perdu** (absent du DTO) |
| Garde « ≥ 1 ligne » | Non (mais garanti par `hasLenses`) | **Non** (`OrderService` l'imposait — dead) |
| Anti-doublon | index unique DB sur `OrderNumber` | idem |

> Le chemin **de production principal** (vente → commande verres) copie **toutes** les données optiques
> fidèlement. Le chemin **manuel** perd `Prism*`/`UsageType`/`VisualAcuity` **au niveau du DTO Application**
> (champs absents du modèle de commande) — c'est une perte **backend réelle sur ce chemin**, distincte de la perte
> UI de pré-remplissage signalée en P3-3 (§11).

**`CreateOrderUseCase` n'affecte jamais `SaleId`** : la commande manuelle naît **orpheline** de toute vente. Les
tests `AdvanceOrderStatusUseCaseTests`/`CreateOrderUseCaseTests` **désactivent l'enforcement FK**
(`Foreign Keys=False`) pour contourner ce défaut préexistant (documenté P2B-2D §16.2). Incohérence latente en
multi-poste (une commande sans vente parente réelle).

### 9.2 Modification (`UpdateOrderUseCase`)

- **Champs éditables** : `OrderNumber` (réaffecté à l'identique), `EstimatedDelivery`, `Notes`, **et
  reconstruction complète des lignes** (`OrderItems.Clear()` puis ré-ajout).
- **Statut** : **ni modifié ni modifiable** par ce use case (seul `AdvanceOrderStatusUseCase` change le statut). ✅
- **Aucune garde de statut** : une commande **`Delivered` ou `InProgress` reste pleinement éditable**. On peut
  **vider et reconstruire les lignes d'une commande dont le stock a déjà été décrémenté** (au passage
  `InProgress`) **sans aucun mouvement de stock compensatoire** → incohérence stock↔commande. 🔴
- **Lignes ajoutables/supprimables après fabrication** : oui, sans contrôle.
- **Concurrence** : `UpdateOrderUseCase` est **last-write-wins** (aucun token ; seule la *transition de statut* a
  une garde atomique). Deux postes éditant la même commande → la seconde écriture écrase la première en silence.
- **Fiche atelier obsolète** : un changement technique de ligne rendrait une fiche de fabrication obsolète — mais
  la fiche n'est pas persistée (P3-6B) ; sans objet pour l'instant.

---

## 10. Double rôle d'`Order`

Preuves **contradictoires** dans le code :

| Indice « commande fournisseur » | Indice « ordre de fabrication atelier » |
|---|---|
| Doc `Order.cs` : « commande fournisseur (uniquement verres) » | `OrderStatus` = workflow atelier (`ToFabricate → InProgress → QualityCheck → Ready`) |
| `SupplierId`, `EstimatedDelivery`, `ReceivedDate`, `OrderNumber = CMD-…` | `OrderItem` porte **Sphère/Cylindre/Axe/Addition/Prisme/UsageType/AcuVis** (données de **fabrication**) |
| Créé automatiquement depuis la vente (verres commandés au fournisseur) | Décrément du **stock local** au passage `InProgress` (« Fabrication commande … ») |
| `Notes = "Commande verres pour vente …"` | Écrans consommateurs : `OrderKanbanView` (tableau atelier), `FabricationSheetViewModel` (fiche montage) |

**Contradictions concrètes** : (1) `SupplierId` **n'est jamais renseigné** par aucun chemin d'écriture — le volet
« fournisseur » est nominal ; (2) `ReceivedDate` (réception fournisseur) **n'est jamais posée** par le workflow ;
(3) le stock **local** est consommé « à la fabrication », comportement d'un **ordre d'atelier**, pas d'une commande
d'achat fournisseur (qui *incrémenterait* le stock à réception). `Order` est donc **de facto un ordre de
fabrication atelier** portant quelques attributs fournisseur inertes.

**Classement des décisions** :

| Décision | Étape cible |
|---|---|
| Matrice de transitions + garde suppression + garde modif-après-fabrication | **P3-6 (obligatoire)** |
| Fiche atelier persistée/versionnée, gating QC, mesures porteur/cotes | **P3-6B** |
| Réconciliation `SaleStatus` ↔ `OrderStatus`, validation monétaire vente | **P3-7** |
| Séparation formelle « ordre de fabrication » vs « commande fournisseur » (nouveau modèle) | **futur redesign métier** (ne pas refondre en P3-6) |

---

## 11. Données optiques et pré-remplissage

Champs réels transportés vers `OrderItem` : `UsageType`, `Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`,
`PrismBase`, `VisualAcuity` (tous mappés par `OrderItemConfiguration`, colonnes `REAL`/`string`).

| Champ | Perte backend (auto vente) | Perte backend (create/update manuel) | Perte UI (pré-remplissage) |
|---|---|---|---|
| Sphère/Cylindre/Axe/Addition | Non | Non | — |
| `UsageType` | Non | **Oui** (absent du DTO) | — |
| `PrismValue` / `PrismBase` | Non | **Oui** (absent du DTO) | **Oui** (`AutoFillFromPrescription`, signalé P3-3) |
| `VisualAcuity` | Non | **Oui** (absent du DTO) | — |

**Diagnostic** : le **modèle** `OrderItem` et l'auto-création depuis la vente sont **complets**. La perte est
localisée sur (a) le **DTO de commande manuelle** (`Create/UpdateOrderLineCommand` — champ **absent du modèle**) et
(b) le **pré-remplissage UI** du prisme. Conformément à la consigne : la perte **UI** reste **documentée, non
corrigée** ici (redesign) ; la perte **DTO backend** du chemin manuel est classée **dette** (§14) car le chemin de
production principal ne la subit pas.

---

## 12. Notifications et effets secondaires

- **Création** : une notification `OrderStatusChanged` par transition acceptée, **si** `INotificationRepository`
  ≠ null (dépendance **optionnelle**). `EntityType="Order"`, `EntityId=OrderId`.
- **Transaction** : **commune** (même `RunAsync`) — rollback supprime la notification en cas d'échec stock/conflit.
- **Idempotence / doublon** : **aucune clé d'idempotence**. Une répétition de transition créerait un doublon, mais
  la répétition est bloquée en amont par `OrderStatusConflictException` (statut déjà avancé). Risque de doublon
  résiduel via les transitions no-op `X→X` (§6.4).
- **Résolution / suppression** : aucune ; les notifications survivent à la suppression de la commande (§8).

Dépendance nécessaire à P3-6 : **aucune règle générale de notification** (→ P3-8). P3-6 doit seulement veiller à ce
que la matrice n'introduise pas de nouveau chemin de doublon.

---

## 13. Tests existants

| Règle / risque | Test existant | Niveau | Suffisant ? |
|---|---|---|---|
| Statut initial `New` | `CreateOrderUseCaseTests`, `RegisterSaleUseCaseTests` | Application/SQLite | ✅ |
| `New → ToFabricate` (notif, pas de stock) | `AdvanceOrderStatusUseCaseTests` (1) | SQLite réel | ✅ |
| `ToFabricate → InProgress` (mouvement + décrément) | (2) | SQLite réel | ✅ |
| Sans repo notif → pas de notif | (3) | SQLite réel | ✅ |
| Commande introuvable → aucune écriture | (4) | SQLite réel | ✅ |
| Commande nulle / ctor null | (5)(6) | Application | ✅ |
| Stock insuffisant → rollback total | (7) | SQLite réel | ✅ |
| **Répétition** même transition → refus, pas de double décrément | (8) | SQLite réel | ✅ (concurrence) |
| **Deux transitions concurrentes** → jamais double décrément | (9) | SQLite réel | ✅ (concurrence) |
| Suppression `New` / introuvable | `DeleteOrderUseCaseTests` | SQLite réel | ✅ (mais statut `New` seul) |
| Édition champs + lignes | `UpdateOrderUseCaseTests` | SQLite réel | ✅ (comportement, pas garde) |
| Matrice `OrderService` | `OrderServiceTests` (Domain) | Domain | ⚠️ teste du **code mort** |

### Trous de couverture (aucun test — à créer **en P3-6**, pas maintenant)

| Règle / risque | Test manquant |
|---|---|
| Transition illégale refusée | `New → Delivered`, `New → InProgress`, `ToFabricate → Ready` **doivent être refusés** (aujourd'hui **acceptés**) |
| Saut d'étape | idem ci-dessus |
| Retour arrière | `Delivered → Ready/New` refusé |
| Répétition `X → X` | statut identique refusé (ou explicitement no-op sans notif) |
| Réouverture d'une commande livrée | refus |
| Suppression selon statut | suppression `InProgress`/`Ready`/`Delivered` refusée |
| Modification après livraison/fabrication | édition lignes refusée quand stock déjà décrémenté |
| Cohérence statut↔stock sur saut | un saut ne doit pas laisser « en fabrication » sans décrément |
| Données optiques (prisme) chemin manuel | round-trip `PrismValue/PrismBase` sur create/update manuel |

---

## 14. Risques classés

### 🔴 Critique

1. **Suppression d'une commande livrée / fabriquée**
   - Preuve : `DeleteOrderUseCase.cs:47-54` (aucune garde) ; `OrderConfiguration.cs:48-51` (OrderItems Cascade) ;
     `StockMovement` sans FK vers Order.
   - Scénario : commande `Delivered` supprimée → lignes + données optiques effacées, stock resté décrémenté,
     notifications orphelines, aucune trace fiable.
   - Impact : perte d'historique métier/fabrication + incohérence stock. Propriétaire : **Application** (garde) +
     **Domain** (règle). Cible : **P3-6**.

2. **Toute transition acceptée par le backend (pas de matrice)**
   - Preuve : `AdvanceOrderStatusUseCase.cs:87` + `OrderRepository.cs:72-81` (seule condition : `stored==expected`).
   - Scénario : appel direct `New → Delivered` ⇒ commande « livrée » sans fabrication ni décrément stock ;
     `Delivered → New` ⇒ réouverture d'une commande livrée.
   - Impact : états incohérents, contournement du décrément stock. Propriétaire : **Domain** (matrice) +
     **Application** (garde). Cible : **P3-6**.

3. **Modification d'une commande déjà fabriquée sans compensation stock**
   - Preuve : `UpdateOrderUseCase.cs:74-89` (`Clear()` + ré-ajout, aucune garde de statut).
   - Scénario : commande `InProgress`/`Delivered` (stock décrémenté) → lignes remplacées ⇒ le stock consommé ne
     correspond plus aux lignes ; aucun mouvement compensatoire.
   - Impact : incohérence stock↔commande. Propriétaire : **Application**. Cible : **P3-6** (règle de modification).

### 🟠 Important

4. **Machine à états absente côté backend / dupliquée** — la matrice vit dans **2 ViewModels** + **1 copie morte**
   (`OrderService`). Preuve : `OrderDetailViewModel.cs:303`, `OrderKanbanViewModel.cs:191`, `OrderService.cs:76`.
   Aucun propriétaire de couche unique. Cible **P3-6**.

5. **Modification illimitée** — aucune restriction de champ/statut sur `UpdateOrderUseCase` ; last-write-wins
   multi-poste. Cible **P3-6** (règle) ; token de concurrence → futur multi-poste.

6. **`CreateOrderUseCase` n'affecte pas `SaleId`** (`SaleId = 0`, commande orpheline ; FK désactivée dans les
   tests). Preuve : `CreateOrderCommand.cs` (pas de `SaleId`), `CreateOrderUseCase.cs:45-53`. Semantique/intégrité.
   Décision de propriétaire à trancher (relève partiellement du double rôle §10).

7. **Double rôle d'`Order`** (fournisseur vs atelier) — sémantique ambiguë (§10). Décision de **sens** avant tout
   durcissement. Cible : partie **P3-6** (workflow), partie **P3-6B/redesign**.

### 🟡 Dette

8. **`OrderService` (+ matrice) mort mais enregistré en DI** — seconde source de vérité trompeuse ;
   `OrderServiceTests` teste du code non exécuté. Envisager suppression/branchement en P3-6.

9. **Perte optique backend du chemin manuel** — `Create/UpdateOrderLineCommand` sans `PrismValue/PrismBase/
   UsageType/VisualAcuity` (champ absent du DTO). Chemin de production principal non affecté.

10. **`ReceivedDate` jamais renseignée** par le workflow (aucun statut ne la pose).

11. **Notifications** : pas de clé d'idempotence ; doublon possible via `X→X` ; survie à la suppression.

12. **Trous de tests** énumérés au §13 (transitions illégales, suppression par statut, modif après fabrication).

### 🔵 Reports explicites

- **Fiche atelier / versionnement / QC bloquant / mesures montage** → **P3-6B**.
- **Validations monétaires, garde client archivé en vente, réconciliation `SaleStatus`** → **P3-7**.
- **Règles générales de notifications** (anti-doublon, résolution, N+1) → **P3-8**.
- **Corrections UI** (pré-remplissage prisme, formulaires) → **redesign global**.
- **Token de concurrence `rowversion` / provider serveur** (PostgreSQL/SQL Server) → **futur chantier production**
  (SQLite sans `rowversion`, ADR-transaction-idempotency §4.6).

---

## 15. Plan d'implémentation candidat (non implémenté)

Le plus petit périmètre backend sûr pour P3-6 :

1. **Propriétaire de la matrice = Domain.** Introduire une règle **pure** (ex. `OrderStatusPolicy.IsAllowed(from,
   to)` ou `GetNext(from)`), source de vérité **unique**. Retirer/brancher la copie morte `OrderService` pour
   éliminer la seconde source de vérité (§14.8).
2. **Représentation** : ensemble explicite des couples autorisés (matrice §6.6), facilement testable, sans état.
3. **Validation de transition** dans `AdvanceOrderStatusUseCase`, **avant** `TryTransitionStatusAsync` : couple
   illégal ⇒ **exception typée Domain** (ex. `InvalidOrderStatusTransitionException`), aucune écriture.
4. **Réutiliser `TryTransitionStatusAsync`** tel quel pour la **concurrence** (la matrice couvre la *légalité*, la
   prise atomique couvre le *conflit multi-poste* — les deux restent nécessaires).
5. **Effets transactionnels inchangés** (décrément + mouvement + notification dans le même runner).
6. **Règle de suppression** : `DeleteOrderUseCase` refuse selon le statut (au minimum `Delivered` ; probablement
   tout statut ≥ `InProgress` car stock consommé) ⇒ `BusinessRuleException`. **Décision métier requise** :
   suppression-garde seule (sans migration) **vs** introduction d'un statut `Cancelled`/archivage (impact modèle +
   migration). Recommandation minimale P3-6 : **garde de suppression sans nouveau statut** ; annulation/archivage
   décidés séparément avec preuve métier.
7. **Règle de modification** : `UpdateOrderUseCase` interdit la reconstruction de lignes une fois le stock
   décrémenté (statut ≥ `InProgress`) ⇒ `BusinessRuleException` ; champs purement descriptifs éventuellement
   tolérés.
8. **Tests** (SQLite réel) : matrice autorisée/refusée, suppression par statut, modif-après-fabrication, cohérence
   statut↔stock sur saut. Cf. §13.
9. **Migration** : **aucune requise** pour le périmètre minimal (garde + matrice = code, schéma inchangé). Une
   migration ne devient nécessaire **que** si l'on ajoute un statut `Cancelled` ou un `IsArchived` — décision
   reportée.
10. **Reports** : QC bloquant/fiche atelier → P3-6B ; monétaire/vente → P3-7 ; notifications → P3-8.

Ne pas créer de sous-phase officielle ; ne pas implémenter ici.

---

## 16. Migrations éventuelles

- **Périmètre minimal P3-6 (matrice + gardes suppression/modification)** : **aucune migration** — schéma inchangé,
  règles portées par du code Domain/Application. `has-pending-model-changes = false` doit rester vrai.
- **Uniquement si** décision métier d'ajouter `OrderStatus.Cancelled` **ou** `Order.IsArchived` : migration EF
  dédiée + backfill, à assumer et documenter séparément (hors périmètre de cet audit).

---

## 17. Reports explicites (récapitulatif)

| Sujet | Étape |
|---|---|
| Fiche atelier persistée, versionnement, QC bloquant, mesures/cotes de montage | **P3-6B** |
| Validation monétaire vente, garde client archivé, `SaleStatus` ↔ `OrderStatus` | **P3-7** |
| Règles générales de notifications (anti-doublon, résolution, N+1) | **P3-8** |
| Corrections UI (pré-remplissage prisme, formulaires, redesign) | **redesign global** |
| Token de concurrence `rowversion`, provider serveur (PostgreSQL/SQL Server) | **futur production** |
| Séparation formelle « ordre de fabrication » vs « commande fournisseur » | **futur redesign métier** |

---

## 18. Validation documentaire

```
git diff --check   → (aucune sortie)
git diff --stat    → (aucun fichier suivi modifié)
git status --short → ?? design-handoff/  ?? design/  ?? docs/ui/
                     ?? docs/implementation/P3-6-orders-workflow-audit-report.md
```

Seul nouveau fichier P3-6 = **ce rapport**. Aucun fichier backend, test, migration ou UI modifié. Aucune autre
documentation touchée.

---

## 19. Verdict

Tous les critères de sortie sont remplis : chemins d'écriture de statut cartographiés (§7), suppression analysée
(§8), effets stock/notification identifiés (§6.5, §12), concurrence comprise (§7, §6.4), rôle réel d'`Order`
documenté (§10), plan candidat précis (§15), rapport `.md` créé, aucun code modifié.

## **P3-6 AUDIT = GO**

Aucun commit. Aucun push. Aucune UI. STOP.
