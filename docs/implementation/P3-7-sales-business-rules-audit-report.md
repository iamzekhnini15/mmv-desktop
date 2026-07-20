# P3-7 — Audit du domaine Ventes (backend multi-poste sûr)

> **MODE = AUDIT_AND_WRITE_REPORT_ONLY** — aucun code, test, migration ou UI modifié.
> Aucun commit, aucun push.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-7 — Ventes multi-poste sûres |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure / tests / doc) |
| HEAD attendu | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` |
| CI attendue | run `29706846895` |
| Date d'audit | 2026-07-20 |
| Livrable | `docs/implementation/P3-7-sales-business-rules-audit-report.md` |

---

## 2. État Git et CI

### 2.1 Git

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `53d689e…bff3e` | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` | ✅ |
| `git rev-parse origin/p3-business-rules` | idem | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` | ✅ |
| Fichiers suivis modifiés | aucun | aucun | ✅ |
| Non suivis | `design-handoff/`, `design/`, `docs/ui/` | exactement ces trois | ✅ |
| `git diff --check` | aucune sortie | aucune sortie | ✅ |

`git log -5 --oneline` :

```
53d689e feat(P3-6B): add versioned workshop sheets
a4d7380 feat(P3-6): enforce order workflow rules
3e7a1ab feat(P3-5): secure stock mutations
c246ebc feat(P3-4B): enforce product business rules
2709bfe docs(P3-4A): audit product business rules
```

### 2.2 CI

| Champ | Valeur |
|---|---|
| `databaseId` | `29706846895` |
| `headSha` | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | `success` |
| `url` | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29706846895 |

**Git ✅ / CI ✅ — SHA exact, branche exacte, `push` / `completed` / `success`.**

---

## 3. Baseline locale

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet restore MMV.sln` | OK | « All projects are up-to-date » | ✅ |
| Build erreurs | 0 | **0** | ✅ |
| Build avertissements | 0 | **1** | ⚠️ **écart** |
| Tests Domain | 430 | **430** | ✅ |
| Tests Application | 362 | **362** | ✅ |
| Tests App | 239 | **239** | ✅ |
| **Total** | **1031** | **1031** | ✅ |
| Échecs | 0 | **0** | ✅ |
| Ignorés | 0 | **0** | ✅ |
| Vulnérabilités (transitives incluses) | 0 | **0** sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration » | ✅ |
| Références `MMV.Application` | `MMV.Domain` seul | `..\MMV.Domain\MMV.Domain.csproj` seul | ✅ |
| Paquets `MMV.Application` | — | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (abstractions seules) | ✅ |

### 3.1 Écart de baseline documenté

```
tests\MMV.Application.Tests\UseCases\WorkshopSheets\WorkshopSheetConcurrencyHardeningTests.cs(200,92):
warning xUnit1031: Test methods should not use blocking task operations, as they can cause deadlocks.
```

- **Nature** : avertissement d'analyseur xUnit, **dans le code de test uniquement**, aucun impact runtime produit.
- **Origine** : introduit par le commit P3-6B `53d689e`, c'est-à-dire **présent au SHA exact validé vert par la CI `29706846895`**. Ce n'est donc pas une régression de P3-7 ; c'est une **dette héritée non détectée** par la clôture P3-6B (le rapport P3-6B annonce 0 avertissement).
- **Décision d'audit** : l'audit étant **strictement en lecture seule** et l'écart étant **prouvé préexistant et sans effet sur le produit**, l'audit se poursuit. L'écart est consigné ici et remonté en **dette 🟡** (§22) pour correction lors de la prochaine étape qui touchera ce fichier.

**Baseline 1031/1031 conforme, un écart d'avertissement documenté.**

---

## 4. Documents lus

| Document | Apport pour P3-7 |
|---|---|
| `docs/architecture/P3-business-rules-roadmap.md` §P3-7 | Cadre officiel : règles candidates, refus client archivé **obligatoire**, hors-périmètre (`Money`, TVA, paiements multiples, facturation) |
| `docs/implementation/P3-5-stock-and-movements-audit-report.md` + implémentation | Politique stock : `IStockMutationService` obligatoire, convention de signe, non-verres à la vente / verres à la fabrication |
| `docs/implementation/P3-6-orders-workflow-audit-report.md` + implémentation | Matrice `OrderStatus`, report explicite « réconciliation `SaleStatus` ↔ `OrderStatus` → P3-7 » |
| `docs/implementation/P3-6B-workshop-sheet-audit-report.md` + implémentation | Fiche atelier versionnée, FK `Restrict`, report « validations monétaires vente → P3-7 » |
| `docs/implementation/P3-2B-customer-archiving-and-deletion-report.md` | `Customer→Sale` passé en `Restrict` ; **absence de garde-fou à l'écriture** pour client archivé, explicitement délégué à P3-7 |
| `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md` | Base centrale partagée obligatoire ; toute règle P3 doit rester valide en accès concurrent |
| `docs/architecture/adr-application-boundaries.md` | Aucune fuite EF/`IQueryable` ; use case porte l'orchestration |
| `docs/architecture/adr-transaction-idempotency.md` | R-23 : pas d'écriture multi-étapes hors transaction |
| `docs/architecture/adr-numbering.md` | Séquences déterministes par CAS, numéro attribué **dans** la transaction |
| `docs/implementation/P3-0-business-audit-report.md` §7 | Constat d'origine : `SaleStatus` jamais avancée après création |
| `docs/architecture/adr-stock-concurrency.md` | Décrément conditionnel, jamais de stock négatif |

> **Le code réel prime.** Plusieurs écarts entre documentation et code sont relevés ci-après (notamment §8 : le type de colonne monétaire réel contredit les commentaires « OBLIGATOIRE: type decimal pour argent »).

---

## 5. Cartographie du domaine Ventes

| Élément | Chemin exact | Couche | Responsabilité actuelle |
|---|---|---|---|
| `Sale` | `src/MMV.Domain/Entities/Sale.cs` | Domain | Entité anémique : propriétés publiques `set`, **aucune méthode, aucune invariante** |
| `SaleItem` | `src/MMV.Domain/Entities/SaleItem.cs` | Domain | Ligne de vente + données optiques copiées ; anémique |
| `SaleStatus` | `src/MMV.Domain/Enums/SaleStatus.cs` | Domain | 6 valeurs : `Draft`, `AwaitingLenses`, `InFabrication`, `Ready`, `Delivered`, `Cancelled` |
| `PaymentStatus` | `src/MMV.Domain/Enums/…` | Domain | `Paid`, `Partial`, `Pending` — **jamais écrit par le flux de vente** |
| `OrderItemType` | `src/MMV.Domain/Enums/…` | Domain | `Frame`, `LensOd`, `LensOg`, `Accessory` |
| `ISaleService` / `SaleService` | `src/MMV.Domain/Services/SaleService.cs` | Domain | Service **orphelin** : contient les seules règles monétaires existantes mais **n'est appelé par aucun use case** |
| `BusinessRuleException` | `src/MMV.Domain/Exceptions/DomainExceptions.cs:26` | Domain | Exception métier stable (pattern P3-3B) |
| `InsufficientStockException` | `…DomainExceptions.cs:55` | Domain | Levée par le décrément conditionnel |
| `IStockMutationService` | `src/MMV.Domain/Interfaces/Persistence/IStockMutationService.cs` | Domain (port) | Décrément atomique conditionnel |
| `INumberSequenceService` | `src/MMV.Domain/Interfaces/Persistence/…` | Domain (port) | Numérotation CAS |
| `ITransactionRunner` | `src/MMV.Domain/Interfaces/Persistence/…` | Domain (port) | Frontière transactionnelle |
| `ISaleRepository` | `src/MMV.Domain/Interfaces/Repositories/ISaleRepository.cs` | Domain (port) | **Lecture uniquement** + `ExistsByCustomerIdAsync` ; aucune méthode d'écriture spécifique |
| `RegisterSaleUseCase` | `src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs` | Application | **Seul** point d'écriture d'une vente |
| `RegisterSaleCommand` | `…/RegisterSale/RegisterSaleCommand.cs` | Application | Contrat d'entrée (montants fournis par l'appelant) |
| `RegisterSaleLineCommand` | `…/RegisterSale/RegisterSaleLineCommand.cs` | Application | Ligne d'entrée |
| `RegisterSaleResult` | `…/RegisterSale/RegisterSaleResult.cs` | Application | Sortie ; **expose l'entité `Sale`** (transit toléré, migration) |
| `SettleOrderBalanceUseCase` | `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs` | Application | Encaissement du solde — **écrit sur `Sale`** depuis le domaine Commandes |
| `GetSaleFormReferenceDataUseCase` | `…/Sales/GetSaleFormReferenceData/…` | Application | Lecture (référentiel formulaire) |
| `GetCustomerPurchaseHistoryUseCase` | `…/Sales/GetCustomerPurchaseHistory/…` | Application | Lecture |
| `SaleRepository` | `src/MMV.Infrastructure/Repositories/SaleRepository.cs` | Infrastructure | Implémentation lecture ; écriture via `BaseRepository.CreateAsync` |
| `SaleConfiguration` | `src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs` | Infrastructure | Mapping EF, index, `DeleteBehavior` |
| `SaleItemConfiguration` | `src/MMV.Infrastructure/Data/Configurations/SaleItemConfiguration.cs` | Infrastructure | Mapping EF ligne |
| `EfStockMutationService` | `src/MMV.Infrastructure/Persistence/EfStockMutationService.cs` | Infrastructure | Décrément CAS |
| `EfNumberSequenceService` | `src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs` | Infrastructure | Séquence CAS, 50 tentatives |
| `DbInitializer` | `src/MMV.Infrastructure/Data/DbInitializer.cs:823-895` | Infrastructure | Seed de démo — **seul endroit qui calcule correctement** `TotalPrice`/`TotalAmount` |
| `SaleFormViewModel` | `src/MMV.App/ViewModels/SaleFormViewModel.cs` | UI (**hors périmètre**) | Calcule **tous** les montants et les transmet |
| `RegisterSaleUseCaseTests` | `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs` | Tests | 8 tests, **vrai SQLite** |
| `SaleServiceTests` | `tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs` | Tests | Teste le service **orphelin** |
| `SaleFormViewModelTransactionTests` | `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` | Tests | Garde anti double-soumission UI |

### 5.1 Relations

| Relation | Cardinalité | Nullable | Observation |
|---|---|---|---|
| `Sale → Customer` | N:1 | **`long?`** | Vente sans client possible **au niveau modèle** (mais pas via le use case — cf. §10) |
| `Sale → Staff (User)` | N:1 | `long?` | **`StaffId` n'est jamais renseigné** par `RegisterSaleUseCase` ⇒ toujours `null` |
| `Sale → SaleItem` | 1:N | — | Cascade |
| `Sale → Order` | 1:N | — | Cascade ; `Order` créé automatiquement si verres |
| `SaleItem → Product` | N:1 | `long?` | `Restrict` (P3-4B) |
| `Sale ↔ Prescription` | **aucune** | — | **Aucun lien FK** : les données optiques sont **copiées** dans `SaleItem`, jamais référencées |
| `Sale ↔ WorkshopSheet` | **indirecte** | — | Via `Order` uniquement (`WorkshopSheet → Order → Sale`) |
| `Sale ↔ StockMovement` | **aucune** | — | Lien **textuel seulement** (`Reason = "Vente VTE-000001 - Client #42"`) |

---

## 6. Inventaire exhaustif des écritures

Recherche menée sur : `SaleStatus`, `.TotalAmount =`, `.DiscountAmount =`, `.FinalAmount =`, `.DepositAmount =`, `.RemainingAmount =`, `.Quantity =`, `.UnitPrice =`, `.TotalPrice =`, `RegisterSale`, `SettleOrderBalance`, `DeleteSale`, `UpdateSale`, `CreateSale`, `ITransactionRunner`, `INumberSequenceService`, `DecrementStockAsync`, `StockMovement`, `Order`, `SaveChanges`, `ExecuteUpdate`, `ExecuteSql`, `CustomerId`, `IsArchived`.

| Chemin | Déclencheur | Valeurs fournies | Valeurs recalculées | Transaction | Garde backend | Multi-poste sûr |
|---|---|---|---|---|---|---|
| `RegisterSaleUseCase.cs:86-90` | Enregistrement vente | `TotalAmount`, `DiscountAmount`, `FinalAmount`, `DepositAmount`, `RemainingAmount` — **tous depuis la commande** | **AUCUNE** | ✅ `ITransactionRunner` | ❌ **aucune** | ⚠️ atomique mais **valeurs non vérifiées** |
| `RegisterSaleUseCase.cs:99` / `:105` | Type de vente | — | `Status = Delivered` (comptoir) / `AwaitingLenses` (fabrication) | ✅ | ✅ déterministe | ✅ |
| `RegisterSaleUseCase.cs:112-130` | Lignes | `ProductId`, `Quantity`, `UnitPrice`, optique | Normalisation `ItemType` seule | ✅ | ❌ aucune | ⚠️ |
| `RegisterSaleUseCase.cs:112-130` | Lignes | — | **`TotalPrice` JAMAIS AFFECTÉ ⇒ persiste à `0`** | ✅ | ❌ | 🔴 **corruption de données** |
| `RegisterSaleUseCase.cs:78` | Numéro vente | — | `INumberSequenceService` (CAS) | ✅ | ✅ | ✅ |
| `RegisterSaleUseCase.cs:145` | Numéro commande | — | `INumberSequenceService` (CAS) | ✅ | ✅ | ✅ |
| `RegisterSaleUseCase.cs:147-178` | Order auto | `Quantity`, `UnitPrice` recopiés des lignes verres | `Status = New`, dates | ✅ | ❌ aucune | ⚠️ |
| `RegisterSaleUseCase.cs:203` | Décrément stock | `Quantity` | Décrément CAS conditionnel | ✅ | ✅ `InsufficientStockException` | ✅ |
| `RegisterSaleUseCase.cs:206-214` | Mouvement stock | — | `Quantity = -line.Quantity` | ✅ | ✅ convention P3-5 | ✅ |
| `RegisterSaleUseCase.cs:138` / `:219` | Persistance | — | — | ✅ (2 `SaveChangesAsync` **dans** la transaction) | — | ✅ |
| `SettleOrderBalanceUseCase.cs:87-88` | Encaissement solde | **aucun montant fourni** | `DepositAmount = FinalAmount` ; `RemainingAmount = 0` **forcés** | ✅ | ❌ **aucune** | 🔴 **non idempotent, aucun update conditionnel** |
| `SettleOrderBalanceUseCase.cs:91` / `:108` | Persistance | — | — | ✅ (2 `SaveChangesAsync`) | — | ✅ |
| `SaleService.CreateSaleAsync:46-61` | — | `Sale` complète | `CalculateSaleAsync` | ❌ **hors transaction** | ✅ 3 règles | ⚠️ **code mort : aucun appelant** |
| `SaleService.CalculateSaleAsync:63-84` | — | — | `TotalAmount = Σ TotalPrice` ; `FinalAmount = Total − Discount` | ❌ | ✅ remise ≥ 0, remise ≤ total | ⚠️ **code mort** |
| `DbInitializer.cs:872-884` | Seed démo | — | `TotalAmount = Σ TotalPrice` ; remise arrondie `Math.Round(…, 2)` ; `FinalAmount` | — | — | hors production |

**Aucun** `ExecuteUpdate`, `ExecuteSql` ou SQL brut sur `Sales`/`SaleItems`.
**Aucun** use case `CreateSale`, `UpdateSale` ou `DeleteSale` n'existe.

> ⚠️ **Aucune validation UI n'est comptée comme protection backend.** La garde `OrderItems.Count == 0` de `SaleFormViewModel.ExecuteSave` (ligne 838) et la garde anti double-soumission `IsSaving` (ligne 835) sont **exclusivement UI** et contournables par tout autre appelant du use case.

---

## 7. Contrat réel de `RegisterSaleCommand`

### 7.1 `RegisterSaleCommand`

| Champ | Type réel | Fourni par l'appelant ? | Recalculé backend ? | Validé ? | Peut être falsifié ? |
|---|---|---:|---:|---:|---:|
| `CustomerId` | **`long`** (non nullable) | ✅ | ❌ | ❌ | ✅ |
| `IsCounterSale` | `bool` | ✅ | ❌ | ❌ | ✅ |
| `TotalAmount` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `DiscountAmount` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `FinalAmount` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `DepositAmount` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `RemainingAmount` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `PaymentMethod` | `PaymentMethod` | ✅ | ❌ | ❌ | ✅ |
| `Notes` | `string?` | ✅ | ❌ | ❌ (longueur DB 2000 seulement) | ✅ |
| `Lines` | `IReadOnlyList<…>` | ✅ | ❌ | ❌ (vide accepté) | ✅ |

**Champs absents du contrat** : `StaffId` / utilisateur courant, `PrescriptionId`, `SaleStatus`, `PaymentStatus`, `EstimatedDelivery`, `SaleDate`, montant de ligne.

### 7.2 `RegisterSaleLineCommand`

| Champ | Type réel | Fourni ? | Recalculé ? | Validé ? | Falsifiable ? |
|---|---|---:|---:|---:|---:|
| `ProductId` | `long?` | ✅ | ❌ | ❌ | ✅ |
| `ItemType` | `OrderItemType` | ✅ | normalisation seule | ❌ | ✅ |
| `Quantity` | `int` | ✅ | ❌ | ❌ | ✅ |
| `UnitPrice` | `decimal` | ✅ | ❌ | ❌ | ✅ |
| `UsageType`, `Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`, `PrismBase`, `VisualAcuity` | nullable (`double?`/`int?`/enum?/`string?`) | ✅ | ❌ | ❌ **copiés bruts** | ✅ |

**Aucun champ « montant de ligne »** n'existe dans la commande — d'où l'origine probable de l'oubli de `SaleItem.TotalPrice`.

### 7.3 Réponses aux 14 questions

| # | Question | Réponse prouvée |
|---:|---|---|
| 1 | Confiance à `TotalAmount` ? | **OUI, aveuglément.** `RegisterSaleUseCase.cs:86` |
| 2 | Confiance à `DiscountAmount` ? | **OUI.** `:87` |
| 3 | Confiance à `FinalAmount` ? | **OUI.** `:88` — jamais comparé à `Total − Discount` |
| 4 | Confiance à `DepositAmount` ? | **OUI.** `:89` |
| 5 | Confiance à `RemainingAmount` ? | **OUI.** `:90` — jamais comparé à `Final − Deposit` |
| 6 | Prix unitaire : catalogue ou commande ? | **Commande.** `Product` est chargé (`:192`) **uniquement pour lire `Category`** ; `Product.SalePrice` **n'est jamais lu**. |
| 7 | Quantité 0 ou négative possible ? | **Quantité 0 : OUI**, ligne persistée, `DecrementStockAsync` non appelé (le service rejette `quantity <= 0`, mais le `continue` du filtre catégorie n'intervient pas — voir §11.2). **Quantité négative : OUI** au niveau `SaleItem` ; le décrément lèverait `ArgumentException` (non métier). |
| 8 | Prix négatif possible ? | **OUI.** Aucune borne, aucun CHECK. |
| 9 | Remise > total possible ? | **OUI.** `FinalAmount` étant fourni, un `FinalAmount` négatif est persistable. |
| 10 | Acompte > montant final possible ? | **OUI.** Aucune comparaison. `RemainingAmount` négatif persistable. |
| 11 | NaN / infini possibles ? | **Pas via `decimal`** (les 7 montants C# sont `decimal`, qui ne connaît ni NaN ni ∞). **MAIS** voir §8.1 : le stockage réel est `REAL` — la conversion `decimal→double→decimal` est le vrai risque. Les champs optiques (`Sphere`, `Cylinder`, `Addition`, `PrismValue`) sont bien des `double?` **non validés** ⇒ NaN/∞ **acceptés et persistés**. |
| 12 | Valeurs arrondies ? | **NON.** Aucun `Math.Round` dans le flux de vente. Seul `DbInitializer.cs:877` arrondit (seed). |
| 13 | Type numérique réel ? | **C# : `decimal`. Base : `REAL` (double IEEE-754).** Voir §8.1 — divergence majeure. |
| 14 | Convention monétaire documentée ? | **NON.** Aucune ADR monnaie/arrondi/devise. Le symbole `€` est codé en dur dans une notification (`SettleOrderBalanceUseCase.cs:101`). `Money` VO explicitement **hors périmètre** P3-7. |

---

## 8. Données monétaires

### 8.1 🔴 Divergence `decimal` (C#) ↔ `REAL` (SQLite)

Les entités portent le commentaire *« OBLIGATOIRE: type decimal pour argent »* (`Sale.cs:42,47,52` ; `SaleItem.cs:36,41`) — **mais la configuration EF force un stockage flottant** :

| Colonne | Type C# | `HasColumnType` | Chemin |
|---|---|---|---|
| `Sale.TotalAmount` | `decimal` | **`REAL`** | `SaleConfiguration.cs:29` |
| `Sale.DiscountAmount` | `decimal` | **`REAL`** (défaut 0) | `:33` |
| `Sale.FinalAmount` | `decimal` | **`REAL`** | `:37` |
| `Sale.DepositAmount` | `decimal?` | **`REAL`** | `:41` |
| `Sale.RemainingAmount` | `decimal?` | **`REAL`** | `:44` |
| `SaleItem.UnitPrice` | `decimal` | **`REAL`** | `SaleItemConfiguration.cs:29` |
| `SaleItem.TotalPrice` | `decimal` | **`REAL`** | `:33` |

**Conséquence** : tout montant subit un aller-retour `decimal → double → decimal` à chaque écriture/lecture. Une égalité stricte (`Remaining == 0`, `Deposit == FinalAmount`) devient **non fiable** ; les sommes accumulent une dérive. C'est le **socle** de toute règle de recalcul P3-7 : sans décision explicite, un contrôle `FinalAmount == TotalAmount − DiscountAmount` peut échouer sur des données parfaitement légitimes.

> **Portabilité** : sur PostgreSQL/SQL Server (ADR-PROD-DB-001), `REAL` deviendrait `numeric`/`decimal`. La divergence est donc **spécifique SQLite** mais affecte **aujourd'hui** toutes les bases de production mono-poste.

### 8.2 Formule candidate de la roadmap — applicabilité

| Formule candidate | Applicable en l'état ? | Obstacle prouvé |
|---|---|---|
| `LineTotal = Quantity × UnitPrice` | ✅ **oui** | Aucun. Correspond au calcul UI (`SaleFormViewModel.cs:815`). **Corrige de fait** le `TotalPrice = 0`. |
| `TotalAmount = Σ LineTotal` | ✅ **oui** | Aucun. Identique au calcul UI (`:817`) et à `SaleService.CalculateSaleAsync` (`:71`). |
| `FinalAmount = TotalAmount − DiscountAmount` | ✅ **oui** | Aucun (`:818`). |
| `RemainingAmount = FinalAmount − DepositAmount` | ✅ **oui** | Aucun (`:827`). |

**La formule candidate est intégralement applicable** : elle reproduit exactement, côté backend, ce que l'UI calcule déjà. C'est un **déplacement de propriété**, pas un changement de sémantique — donc **aucune régression fonctionnelle attendue** sur le chemin nominal.

### 8.3 Audit des variantes

| Variante | Existe ? | Preuve |
|---|---|---|
| Remise **globale** | ✅ | `Sale.DiscountAmount`, champ unique |
| Remise **par ligne** | ❌ | Aucun champ sur `SaleItem`/`RegisterSaleLineCommand` |
| Prix personnalisé | ✅ **implicite** | `UnitPrice` vient du panier UI, jamais du catalogue |
| Remise négative | ✅ possible | Aucune borne backend (`SaleService` la refuserait, mais est mort) |
| Gratuité (`UnitPrice = 0`) | ✅ possible | Aucune borne — **à conserver** (geste commercial légitime) |
| Arrondi | ❌ absent | Aucun `Math.Round` dans le flux |
| Précision | ⚠️ | Flottante (§8.1) |
| Devise | ❌ | Aucune notion ; `€` codé en dur dans une notification |
| Incohérences historiques | ⚠️ **certaines** | **Toutes** les ventes créées via `RegisterSaleUseCase` ont `SaleItem.TotalPrice = 0` |

### 8.4 Propriété cible

| Montant | Source actuelle | Calcul actuel | Risque | Propriétaire cible |
|---|---|---|---|---|
| `SaleItem.TotalPrice` | **rien** | **jamais affecté ⇒ 0** | 🔴 corruption silencieuse ; toute somme/rapport basé dessus vaut 0 | **Backend** (`Quantity × UnitPrice`) |
| `SaleItem.UnitPrice` | commande (UI) | aucun | 🟠 prix arbitraire, catalogue ignoré | **Backend** — décision §20 |
| `Sale.TotalAmount` | commande (UI) | aucun | 🔴 falsifiable | **Backend** (`Σ LineTotal`) |
| `Sale.DiscountAmount` | commande (UI) | aucun | 🟠 négative possible | **Appelant, borné backend** (`≥ 0`, `≤ Total`) |
| `Sale.FinalAmount` | commande (UI) | aucun | 🔴 falsifiable, négatif possible | **Backend** (`Total − Discount`) |
| `Sale.DepositAmount` | commande (UI) | aucun | 🔴 négatif ou `> Final` possible | **Appelant, borné backend** (`0 ≤ Deposit ≤ Final`) |
| `Sale.RemainingAmount` | commande (UI) | aucun | 🔴 falsifiable, négatif possible | **Backend** (`Final − Deposit`) |
| `Sale.PaymentStatus` | **rien** | défaut EF `Paid` | 🟠 **une vente avec solde dû est marquée `Paid`** | **Backend** — décision §13/§20 |

---

## 9. Validation des lignes

| # | Question | Réponse prouvée | Classification |
|---:|---|---|---|
| 1 | Vente sans ligne possible ? | **OUI en backend.** `Lines` vide ⇒ `foreach` sans itération ⇒ vente à 0 ligne persistée avec un numéro consommé. Seule la garde UI `OrderItems.Count == 0` (`SaleFormViewModel.cs:838`) l'empêche. | **règle de vente** |
| 2 | Ligne sans produit possible ? | **OUI.** `ProductId` est `long?` ; `RegisterSaleUseCase.cs:188` fait `continue` si absent ⇒ ligne persistée, **aucun décrément de stock**. | **règle de vente** |
| 3 | Quantité strictement positive ? | **NON exigé.** Aucun contrôle. `Quantity = 0` ⇒ ligne persistée + `DecrementStockAsync(…, 0)` ⇒ `ArgumentException` **technique** (non métier). | **règle de vente** |
| 4 | Prix positif ou non négatif ? | **Aucune exigence.** Recommandation : **`≥ 0`** (gratuité légitime), pas `> 0`. | **règle de vente** |
| 5 | Plusieurs lignes même produit ? | **OUI, autorisé.** | **règle de vente** |
| 6 | Stock décrémenté plusieurs fois ? | **OUI, correctement.** Le `foreach` (`:185`) itère par ligne ; chaque ligne appelle `DecrementStockAsync`. **Prouvé** par `ExecuteAsync_MultipleLinesSameNonLensProduct_FinalStockCorrect`. | **règle stock** |
| 7 | Produit inactif vendable par ID direct ? | **OUI.** `Product.IsActive` existe (`Product.cs:112`) mais **n'est jamais consulté** par `RegisterSaleUseCase`. Le produit n'est chargé que pour lire `Category` (`:192-196`). | **règle produit** |
| 8 | Produit introuvable ⇒ erreur contrôlée ? | **NON.** `if (product != null)` (`:193`) ⇒ **échec silencieux** : la ligne est persistée, aucun stock décrémenté, **aucune erreur levée**. 🔴 | **règle produit** |
| 9 | Ligne verre sans données optiques possible ? | **OUI.** Tous les champs optiques sont nullable et non validés. | **règle optique** |
| 10 | Données optiques validées ou copiées ? | **Copiées brutes** (`:122-129`). Aucun appel à `PrescriptionValidator` ni à `OpticalAxisNormalizer` (pourtant existants et utilisés en P3-3B). NaN/∞ acceptés sur les `double?`. | **règle optique** |
| 11 | Ordonnance supprimée/modifiée affecte la vente ? | **NON — aucun lien.** Les valeurs sont copiées ; aucune FK `PrescriptionId`. La vente est un instantané. **Comportement correct**, à documenter. | **règle optique** |
| 12 | Vente fabrication : monture + verres + accessoires ? | **OUI.** Aucune contrainte de composition. | **règle de vente** |
| 13 | La commande créée contient-elle uniquement les lignes attendues ? | **OUI.** Filtre explicite `LensOd`/`LensOg` (`:158`). **Prouvé** par `ExecuteAsync_SaleWithLenses_CreatesSupplierOrder_…`. | **règle de vente** |

**Bilan** : sur 13 points, **aucune** validation de ligne backend n'existe. Les seules protections réelles sont UI (règle UI, non comptée) ou accidentelles (`ArgumentException` technique).

---

## 10. Client facultatif et client archivé

### 10.1 Contrat `CustomerId` — incohérence structurelle

| Niveau | Type | Conséquence |
|---|---|---|
| `Sale.CustomerId` | **`long?`** (`Sale.cs:24`) | Vente sans client **supportée par le modèle** |
| FK EF | `Restrict`, nullable (`SaleConfiguration.cs:68-71`) | Cohérent avec `long?` (P3-2B) |
| `RegisterSaleCommand.CustomerId` | **`long`** (non nullable) (`RegisterSaleCommand.cs:21`) | 🔴 **Une vente sans client est IMPOSSIBLE via le use case** |

**Preuve du défaut** : `SaleFormViewModel.cs:911` transmet `CustomerId = CustomerId`. Sans client sélectionné, la valeur est `0` ⇒ `Sale.CustomerId = 0` (**non null**) ⇒ violation de la FK `Customers` ⇒ `PersistenceException`. La vente comptoir anonyme, pourtant explicitement autorisée par la roadmap P3-7 (*« `CustomerId` reste nullable : une vente au comptoir sans client demeure valide »*), **ne fonctionne pas**.

### 10.2 Réponses

| # | Question | Réponse prouvée |
|---:|---|---|
| 1 | `RegisterSaleUseCase` charge-t-il le client ? | **NON.** Aucun `ICustomerRepository` injecté (constructeur `:35-43`). |
| 2 | Refuse-t-il `IsArchived = true` ? | **NON.** Recherche `IsArchived` dans `MMV.Application` : **aucune occurrence** dans le domaine Ventes. **Défaut confirmé**, exactement comme annoncé par la roadmap et P3-2B §10. |
| 3 | Une sélection UI ancienne peut-elle contourner le filtre ? | **OUI.** P3-2B exclut les archivés du sélecteur, mais un formulaire ouvert **avant** l'archivage (ou tout autre appelant) passe un `CustomerId` archivé sans obstacle. **Scénario multi-poste réel** : poste A archive le client pendant que poste B saisit la vente. |
| 4 | La vérification doit-elle être dans la transaction ? | **OUI.** Hors transaction, la fenêtre archivage↔vente reste ouverte. Le pattern P3-3B (`CreatePrescriptionUseCase.cs:109-114`) charge le client puis lève `BusinessRuleException`. |
| 5 | Une vente sans client doit-elle rester valide ? | **OUI** — exigence explicite de la roadmap (`CustomerId` nullable). Nécessite de rendre `RegisterSaleCommand.CustomerId` **`long?`** (§10.1). |
| 6 | Commande atelier sans client supportée ? | **OUI techniquement** : `Order.SaleId` référence la vente, jamais le client. Aucune dépendance à `CustomerId`. |
| 7 | Message métier stable cohérent P3-2/P3-3 ? | Réutiliser **littéralement** la constante `CustomerArchivedMessage` du pattern `CreatePrescriptionUseCase`, via `BusinessRuleException`. Cohérence de message et de type d'exception garantie. |

> **Note** : `RegisterSaleUseCase.cs:211` construit `Reason = $"Vente {sale.SaleNumber} - Client #{command.CustomerId}"`. Avec un `CustomerId` nullable, ce libellé produirait `Client #` — à traiter lors de l'implémentation.

---

## 11. Atomicité vente / stock / commande / numérotation

### 11.1 Ordre exact d'exécution (`RegisterSaleUseCase`)

```
ExecuteAsync (:59)
└── ITransactionRunner.RunAsync (:66)          ← frontière transactionnelle UNIQUE
    └── PersistSaleAsync (:74)
        1. NextNumberAsync(SALE)               :78   ← numéro DANS la transaction
        2. new Sale { montants recopiés }      :81-93
        3. Status + EstimatedDelivery          :96-107
        4. SaleItems (TotalPrice NON affecté)  :110-131
        5. SaleRepository.CreateAsync          :137
        6. SaveChangesAsync  (#1)              :138  ← obtient SaleId
        7. si verres : NextNumberAsync(ORDER)  :145
        8. si verres : Order + OrderItems      :147-177
        9. pour chaque ligne :
              GetByIdAsync(product)            :192
              si null → SILENCE (continue)     :193
              si VERRE/LENTILLE → skip         :196-198
              DecrementStockAsync (CAS)        :203
              StockMovement (Quantity < 0)     :206-214
       10. SaveChangesAsync  (#2)              :219
       11. commit implicite par le runner
```

**Aucune validation** — l'étape 0 attendue (validation de la commande) **n'existe pas**.

### 11.2 Réponses

| Question | Réponse prouvée |
|---|---|
| Tout dans un seul `ITransactionRunner` ? | ✅ **OUI.** Un unique `RunAsync` (`:66`) englobe numérotation, vente, commande, stock, mouvements. |
| Combien de `SaveChangesAsync` ? | **2** (`:138`, `:219`), **tous deux à l'intérieur** de la transaction. Le premier est nécessaire pour obtenir `SaleId` avant de créer l'`Order`. |
| Numéro consommé si une étape échoue ? | ❌ **NON** — le CAS de séquence participe à la transaction. **Prouvé** par `ExecuteAsync_CounterSale_InsufficientStock_RollsBack_NoSale_NoNumberConsumed`. |
| Stock restauré si la commande échoue ? | ✅ **OUI** — rollback global. |
| Vente annulée si une ligne est insuffisante ? | ✅ **OUI** — `InsufficientStockException` remonte, le runner annule tout. |
| Mouvements annulés ? | ✅ **OUI** — même transaction. |
| Fiche atelier concernée à ce stade ? | ❌ **NON.** La `WorkshopSheet` dérive de l'`Order` (P3-6B), créée plus tard dans le workflow commande. Hors périmètre de l'enregistrement. |
| Collision de numéro contrôlée ? | ✅ **OUI** — CAS avec 50 tentatives (`EfNumberSequenceService.cs:46,62`) **+** index unique `idx_sales_sale_number_unique` (`SaleConfiguration.cs:21-23`). Double protection. |
| Deux ventes simultanées, même numéro ? | ❌ **NON possible** — CAS + contrainte unique. |
| Deux postes, dernier produit : un seul réussit ? | ✅ **OUI** — décrément conditionnel P3-5, le perdant reçoit `InsufficientStockException` et sa vente entière est annulée. |
| Acte externe hors transaction ? | ❌ **NON** dans `RegisterSaleUseCase`. ✅ **Aucune notification** n'est émise à l'enregistrement (contrairement à `SettleOrderBalanceUseCase`). |

### 11.3 Tableau d'atomicité

| Étape | Même transaction ? | Échec possible | Rollback prouvé ? | Test existant |
|---|:---:|---|:---:|---|
| Numéro vente | ✅ | contention CAS | ✅ | `…_InsufficientStock_RollsBack_NoSale_NoNumberConsumed` |
| Création `Sale` | ✅ | FK client invalide | ✅ | — (**trou**) |
| Création `SaleItems` | ✅ | FK produit | ✅ | indirect |
| Numéro commande | ✅ | contention CAS | ✅ | `…_SaleWithLenses_CreatesSupplierOrder_…` |
| Création `Order` | ✅ | contrainte | ✅ | `…_SaleWithLenses_…` |
| Décrément stock | ✅ | `InsufficientStockException` | ✅ | `…_InsufficientStock_RollsBack_…` |
| Mouvements | ✅ | — | ✅ | `…_CounterSale_Frame_DecrementsStock_WritesMovement_…` |
| `SaveChanges` ×2 | ✅ | `PersistenceException` | ✅ | — (**trou**) |
| Commit | ✅ | — | — | — |

**Verdict §11 : l'atomicité est acquise et prouvée.** C'est le point fort du flux actuel. P3-7 doit **préserver** cette frontière et y **ajouter** validation et vérification client, sans la fragmenter.

---

## 12. Politique stock après P3-5

### 12.1 Conformité

| Exigence P3-5 | Respectée ? | Preuve |
|---|:---:|---|
| Tout décrément via `IStockMutationService` | ✅ | `RegisterSaleUseCase.cs:203` — aucun `-=` direct |
| Non-verres décrémentés à l'enregistrement | ✅ | `:196-198` (skip `VERRE`/`LENTILLE`) |
| Verres décrémentés au passage en fabrication | ✅ | `AdvanceOrderStatusUseCase.cs:272` |
| Aucune ligne décrémentée deux fois | ✅ | Un non-verre n'entre jamais dans l'`Order` (filtre `:158`) ; un verre n'est jamais décrémenté à la vente |
| Mouvement signé cohérent | ✅ | `Quantity = -line.Quantity` (`:210`) |
| Mouvement et vente dans la même transaction | ✅ | §11 |

**P3-7 ne réintroduit aucun contournement.** La politique P3-5 est intacte.

### 12.2 Scénarios

| Scénario | Comportement actuel | Verdict |
|---|---|---|
| Comptoir sans verre | Monture/accessoire décrémentés + mouvements ; pas d'`Order` | ✅ prouvé (test 1) |
| Comptoir avec verre | Verre **non** décrémenté ; `Order` **créé** malgré `IsCounterSale = true` (le déclencheur est `hasLenses`, pas le type de vente) ; `Sale.Status = Delivered` alors que l'`Order` est `New` | ⚠️ **incohérence** — voir §14 |
| Fabrication monture + verres | Monture décrémentée, verres différés | ✅ prouvé (test `…_FabricationSale_DecrementsNonLensAtSale_NotTheLens`) |
| Accessoires | Décrémentés comme non-verres | ✅ |
| Plusieurs lignes même produit | Décrémenté N fois, stock final correct | ✅ prouvé (test `…_MultipleLinesSameNonLensProduct_…`) |
| Stock insuffisant 1ʳᵉ ligne | Rollback total | ✅ prouvé |
| Stock insuffisant ligne ultérieure | Rollback total (lignes précédentes annulées) | ✅ même mécanisme, **non testé explicitement** |
| Vente concurrente stock limité | Un seul gagnant (CAS) | ✅ garanti P3-5, **non testé sur le chemin vente** |
| Commande générée puis rollback | Annulée | ✅ |
| **Produit introuvable** | `continue` **silencieux** : ligne persistée, stock non décrémenté | 🔴 **défaut** (§9.8) |
| **Ligne sans `ProductId`** | `continue` silencieux | ⚠️ intentionnel mais non documenté |

---

## 13. `SaleStatus`

### 13.1 Valeurs et écritures

`SaleStatus` (`src/MMV.Domain/Enums/SaleStatus.cs`) : `Draft`, `AwaitingLenses`, `InFabrication`, `Ready`, `Delivered`, `Cancelled`.

**Inventaire exhaustif des écritures** (recherche `SaleStatus.` sur `src/`) :

| Chemin | Valeur écrite | Moment |
|---|---|---|
| `Sale.cs:79` | `Draft` | initialiseur C# |
| `SaleConfiguration.cs:57` | `Draft` | défaut SQL |
| `RegisterSaleUseCase.cs:99` | `Delivered` | création, si `IsCounterSale` |
| `RegisterSaleUseCase.cs:105` | `AwaitingLenses` | création, sinon |

**Il n'existe aucune autre écriture dans tout `src/`.**

**Lectures** : `RegisterSaleResult.Status` (`:227`) — remonté à l'UI après création, puis plus jamais consulté pour piloter une règle.

### 13.2 Réponses

| # | Question | Réponse prouvée |
|---:|---|---|
| 1 | Statut initial ? | `Delivered` (comptoir) ou `AwaitingLenses` (fabrication) — **jamais `Draft`** en pratique, malgré le double défaut |
| 2 | Qui le fixe ? | `RegisterSaleUseCase` uniquement, à la création |
| 3 | Qui le modifie ? | **Personne.** |
| 4 | Jamais avancé après création ? | **Confirmé — jamais.** Valide le constat P3-0 §7. |
| 5 | Matrice de transitions ? | **Aucune.** (Contraste : `OrderStatus` en possède une depuis P3-6.) |
| 6 | Les écrans l'utilisent-ils ? | Retourné dans `RegisterSaleResult` ; aucun écran ne l'exploite comme règle |
| 7 | Dérivé d'`OrderStatus` ? | **NON.** Aucune synchronisation. |
| 8 | Vente fabrication éternellement dans son statut initial ? | **OUI.** Une vente reste `AwaitingLenses` **à jamais**, même une fois la commande `Delivered`. 🟠 |
| 9 | Comptoir et fabrication même statut ? | **NON** — `Delivered` vs `AwaitingLenses`, seule distinction réelle |
| 10 | Statut de paiement distinct ? | **OUI** — `Sale.PaymentStatus` (`Paid`/`Partial`/`Pending`) existe **mais n'est jamais écrit** par le flux de vente : défaut EF `Paid` (`SaleConfiguration.cs:52`). 🔴 **Une vente avec `RemainingAmount > 0` est enregistrée `Paid`.** |
| 11 | `RemainingAmount = 0` modifie-t-il le statut ? | **NON**, ni `Status` ni `PaymentStatus` (`SettleOrderBalanceUseCase` n'y touche pas) |
| 12 | Vente livrée peut-elle sembler « en attente » ? | **OUI** — cas nominal : `Order` `Delivered` + `Sale` `AwaitingLenses` |
| 13 | Code mort ou règle utile ? | **Quasi-mort** : porte une unique information binaire (comptoir vs fabrication) déjà déductible de l'existence d'un `Order`. Les valeurs `InFabrication`, `Ready`, `Cancelled` sont **totalement inatteignables**. |

### 13.3 Tableau

| `SaleStatus` | Créé quand | Modifié quand | Consommateurs | Cohérence réelle |
|---|---|---|---|---|
| `Draft` | défaut C#+SQL | jamais | aucun | ❌ **inatteignable** via le use case |
| `AwaitingLenses` | `IsCounterSale = false` | **jamais** | `RegisterSaleResult` | ❌ devient faux dès que l'`Order` avance |
| `InFabrication` | **jamais** | jamais | aucun | ❌ **valeur morte** |
| `Ready` | **jamais** | jamais | aucun | ❌ **valeur morte** |
| `Delivered` | `IsCounterSale = true` | jamais | `RegisterSaleResult` | ⚠️ correct au comptoir ; faux si la vente contient des verres (§12.2) |
| `Cancelled` | **jamais** | jamais | aucun | ❌ **valeur morte** — aucune annulation n'existe |

---

## 14. Relation `SaleStatus` / `OrderStatus`

| `SaleStatus` | `OrderStatus` | Possible aujourd'hui ? | Signification | Risque |
|---|---|:---:|---|---|
| `Delivered` | *(aucun Order)* | ✅ **nominal** | Vente comptoir sans verre, livrée | ✅ cohérent |
| `Delivered` | `New` | ✅ **nominal** | Comptoir **avec verres** : `hasLenses` crée l'`Order` sans regarder `IsCounterSale` | 🟠 **contradictoire** : « livrée » alors que les verres sont à commander |
| `AwaitingLenses` | `New` | ✅ nominal | Fabrication, verres à commander | ✅ cohérent |
| `AwaitingLenses` | `ToFabricate` | ✅ | Verres reçus | 🟠 `Sale` périmée |
| `AwaitingLenses` | `InProgress` | ✅ | Fabrication en cours | 🟠 `SaleStatus.InFabrication` existe mais **n'est jamais utilisée** |
| `AwaitingLenses` | `QualityCheck` | ✅ | Contrôle qualité | 🟠 `Sale` périmée |
| `AwaitingLenses` | `Ready` | ✅ | Prête au retrait | 🟠 `SaleStatus.Ready` existe, **jamais utilisée** |
| `AwaitingLenses` | `Delivered` | ✅ | **Livrée au client** | 🔴 **`Sale` affiche « en attente des verres » sur une vente terminée** |
| `AwaitingLenses` + `Remaining > 0` | `Delivered` | ✅ | Livrée, solde dû | 🔴 statut faux **et** `PaymentStatus = Paid` faux |
| `AwaitingLenses` + `Remaining = 0` | `Delivered` | ✅ | Livrée et soldée | 🔴 statut vente toujours faux |
| `InFabrication`/`Ready`/`Cancelled` | *(n'importe)* | ❌ | — | valeurs mortes |

### 14.1 Que signifie réellement `SaleStatus` ?

**Plusieurs notions mélangées, aucune correctement portée** :

- **fabrication** : prétendue (`AwaitingLenses`, `InFabrication`) — **réellement portée par `OrderStatus`** ;
- **livraison** : prétendue (`Ready`, `Delivered`) — **réellement portée par `OrderStatus`** ;
- **paiement** : **non portée** — `PaymentStatus` existe séparément, mais reste mort ;
- **cycle commercial** : `Draft`/`Cancelled` — **jamais atteintes**.

En pratique, `SaleStatus` n'encode qu'**un seul bit** figé à la création : *comptoir ou fabrication* — information déjà déductible de l'existence d'un `Order`.

### 14.2 Options candidates (aucune retenue à ce stade)

| Option | Description | Coût | Multi-poste | Commentaire |
|---|---|---|---|---|
| **A** — Conserver + règles propres | Machine à états `Sale` complète, synchronisée à chaque avancement d'`Order` | **élevé** | risque de désynchronisation entre deux agrégats | Duplique `OrderStatus` ; contredit « plus petit périmètre » |
| **B** — Synchroniser avec `OrderStatus` | `AdvanceOrderStatusUseCase` met aussi à jour `Sale.Status` | moyen | 2 écritures à garder cohérentes | Élargit P3-7 au domaine Commandes (déjà clos) |
| **C** — Dériver en lecture | Ne plus persister ; calculer depuis `Order`/paiement dans les DTO | faible **backend** | ✅ pas de désynchronisation possible | Touche les DTO de lecture ; champ persisté devient résiduel |
| **D** — Réduire à un statut de paiement | Abandonner `Status`, faire vivre `PaymentStatus` (déjà présent, déjà mort) | **faible** | ✅ dérivable de `RemainingAmount` | **Adresse un défaut 🔴 réel** (vente à solde dû marquée `Paid`) |
| **E** — Déprécier | Marquer `Sale.Status` obsolète, figer, documenter | **très faible** | ✅ neutre | Ne corrige rien mais **arrête la propagation d'une donnée fausse** |

> **Recommandation d'audit (à décider en implémentation)** : **E + D** — déprécier `Sale.Status` (documenter qu'il ne reflète que le type de vente à la création, ne jamais s'y fier) et **faire porter la vérité paiement par `PaymentStatus`, dérivé du `RemainingAmount` recalculé**. C'est le plus petit changement qui supprime une donnée fausse sans rouvrir P3-6.

---

## 15. Règlement du solde (`SettleOrderBalanceUseCase`)

| # | Question | Réponse prouvée |
|---:|---|---|
| 1 | Quel agrégat modifié ? | **`Sale`** — bien que le use case vive dans `UseCases/Orders/`. `Order` n'est modifié en rien (`UpdateAsync(fresh)` ne change aucun champ d'`Order`). 🟠 **frontière d'agrégat franchie**. |
| 2 | Quel montant fourni ? | **Aucun.** `SettleOrderBalanceCommand` ne porte que `OrderId`. |
| 3 | Solde recalculé ou forcé à zéro ? | **Forcé.** `DepositAmount = FinalAmount` ; `RemainingAmount = 0m` (`:87-88`). Aucun recalcul, aucune vérification. |
| 4 | Payer plus que le solde ? | **Sans objet** (aucun montant) — mais l'acompte est **écrasé** par `FinalAmount`, perdant la trace de l'acompte initial. |
| 5 | Payer un montant négatif ? | Sans objet. |
| 6 | Appeler deux fois ? | **OUI, sans erreur.** Le 2ᵉ appel réécrit les mêmes valeurs et **crée une 2ᵉ notification** annonçant `0,00 €` encaissés. **Non idempotent.** |
| 7 | Deux postes simultanément ? | **OUI — les deux réussissent.** Aucun update conditionnel, aucun token de version, aucun `RowVersion`. Les deux lisent `Remaining = 200`, les deux écrivent `0`, **deux notifications « 200,00 € encaissés »** sont créées. 🔴 **Un encaissement de 200 € peut être comptabilisé deux fois par la caisse.** |
| 8 | Update conditionnel ? | ❌ **NON.** Contraste net avec `EfStockMutationService` (CAS), `EfNumberSequenceService` (CAS) et `WorkshopSheet` (version, P3-6B). Le paiement est **le seul acte sensible sans protection concurrentielle**. |
| 9 | Paiement historisé ? | ❌ **NON.** Seule trace : une `Notification` (`Type = "PaymentReceived"`) — objet d'affichage, **pas un journal comptable**, et supprimable. |
| 10 | Entité `Payment` ? | ❌ **N'existe pas.** |
| 11 | Date / utilisateur enregistrés ? | ❌ **NON.** `Notification.CreatedAt` seul ; **aucun utilisateur** (`Sale.StaffId` reste `null`). |
| 12 | Statut de vente changé ? | ❌ **NON** (ni `Status` ni `PaymentStatus`). |
| 13 | Statut de commande changé ? | ❌ **NON.** |
| 14 | Vente non livrée soldable ? | **OUI.** Aucune condition sur `OrderStatus`. |
| 15 | Vente comptoir soldée différemment ? | **Non soldable du tout** : le use case exige un `OrderId`. Une vente comptoir sans `Order` (donc sans verres) **n'a aucun chemin d'encaissement de solde**. 🟠 |

### 15.1 Classement

| Constat | Classification |
|---|---|
| Double encaissement concurrent non protégé | **Exigence P3-7** — update conditionnel ou refus si `Remaining = 0` |
| Non-idempotence / double notification | **Exigence P3-7** — garde peu coûteuse |
| Solde forcé sans recalcul | **Exigence P3-7** — cohérent avec le recalcul monétaire §8 |
| Vente comptoir non soldable | **Dette** — nécessite un chemin de règlement par `SaleId` |
| Entité `Payment`, paiements partiels/multiples, échéanciers | **Hors périmètre** (roadmap §P3-7 : explicitement exclu) |
| Utilisateur / date d'encaissement | **Dette → P3-10** (utilisateur courant) |
| Frontière d'agrégat (`Orders/` écrit sur `Sale`) | **Dette** — refactoring, pas une exigence P3-7 |

---

## 16. Suppression et modification d'une vente

**Constat central : il n'existe aucun use case `DeleteSale` ni `UpdateSale`.** Une vente enregistrée est **immuable et indestructible** par l'application (hors `BaseRepository.DeleteAsync` générique, **appelé par personne** pour `Sale`).

| Relation | FK | `DeleteBehavior` | Effet d'une suppression `Sale` |
|---|---|---|---|
| `Sale → Customer` | `CustomerId` (null.) | **`Restrict`** (P3-2B) | Protège le client : un client porteur de ventes n'est pas supprimable |
| `Sale → Staff (User)` | `StaffId` (null.) | `SetNull` | — |
| `Sale → SaleItem` | `SaleId` | **`Cascade`** | 🔴 **Toutes les lignes détruites** |
| `Sale → Order` | `SaleId` | **`Cascade`** | 🔴 **Toutes les commandes détruites** |
| `Order → OrderItem` | `OrderId` | `Cascade` (hérité) | 🔴 Lignes de commande détruites |
| `Order → WorkshopSheet` | `OrderId` | **`Restrict`** (P3-6B) | ✅ **La base refuse** : une fiche atelier **bloque** la cascade ⇒ la suppression de la vente **échoue** |
| `SaleItem → Product` | `ProductId` (null.) | `Restrict` (P3-4B) | Protège le produit |
| `StockMovement → Sale` | **aucune FK** | — | 🔴 **Mouvements orphelins** : lien textuel seul (`Reason`), **jamais nettoyé, jamais retrouvable** |

### 16.1 Analyse par scénario

| Scénario | Comportement DB si suppression |
|---|---|
| Vente liée à une commande | `Order` + `OrderItems` cascadés |
| Vente liée à des mouvements | Mouvements **conservés et orphelins** (aucune FK) ⇒ **stock durablement faux vs historique** |
| Vente déjà livrée | Aucune protection |
| Vente avec acompte | Aucune protection ⇒ **perte de la trace d'un encaissement** |
| Vente avec fiche atelier | ✅ **Refusée par `Restrict`** (protection P3-6B, effet de bord bénéfique) |
| Restauration du stock | ❌ **Aucune** — le stock resterait décrémenté |

### 16.2 Risques de perte d'historique

| Rang | Risque | Gravité |
|---:|---|---|
| 1 | Cascade `Sale → Order → OrderItem` : historique de commande détruit | 🔴 |
| 2 | Mouvements de stock orphelins : comptabilité matière irréconciliable | 🔴 |
| 3 | Perte de la trace d'acompte encaissé (aucun `Payment`) | 🔴 |
| 4 | Aucune restauration de stock | 🟠 |
| 5 | Protection partielle et **accidentelle** via `WorkshopSheet` `Restrict` | 🟡 |

> **Aucune politique d'annulation n'est proposée** : aucune preuve métier ne permet de trancher entre annulation avec contre-mouvements, archivage ou statut `Cancelled`. **L'absence de chemin de suppression est aujourd'hui une protection de fait** ; la conserver en P3-7 est le choix le plus sûr.

---

## 17. Concurrence multi-poste

| Acte | Transaction | CAS / update conditionnel | Contrainte DB | Risque résiduel |
|---|:---:|:---:|---|---|
| Attribution numéro vente | ✅ | ✅ CAS (50 tentatives) | ✅ index unique `idx_sales_sale_number_unique` | ✅ **aucun** |
| Attribution numéro commande | ✅ | ✅ CAS | ✅ unique | ✅ **aucun** |
| Enregistrement vente | ✅ | ❌ n/a (insertion) | FK `Restrict` client/produit | 🔴 **montants non validés** ; 🔴 **client archivé accepté** |
| Décrément stock | ✅ | ✅ CAS conditionnel | — | ✅ **aucun** (P3-5) |
| Création `Order` | ✅ | ❌ n/a | unique `OrderNumber` | ✅ |
| Calcul du solde | — | ❌ | ❌ aucun CHECK | 🔴 **solde falsifiable à la source** |
| **Règlement du solde** | ✅ | ❌ **AUCUN** | ❌ aucun | 🔴 **double encaissement, double notification** |
| Modification vente | — | — | — | n/a (**n'existe pas**) |
| Suppression vente | — | — | cascades | n/a (**n'existe pas**) |

### 17.1 Classement des risques de concurrence

| Risque | Statut | Preuve |
|---|---|---|
| **Numéro dupliqué** | ✅ **protection acquise** | CAS + index unique |
| **Stock négatif** | ✅ **protection acquise** | Décrément conditionnel P3-5 |
| **Double vente (double-clic)** | ⚠️ **UI seulement** | `IsSaving` (`SaleFormViewModel.cs:835`) — **aucune idempotence backend** ; deux postes ou un appel direct passent |
| **Lost update (paiement)** | 🔴 **défaut prouvé** | `SettleOrderBalanceUseCase.cs:87-88` sans update conditionnel |
| **Double paiement** | 🔴 **défaut prouvé** | Deux postes ⇒ deux notifications d'encaissement du même solde |
| **Statut incohérent** | 🟠 **défaut prouvé** | `SaleStatus` figée vs `OrderStatus` mobile (§14) |
| **Commande dupliquée** | ✅ protégé | Une seule `Order` par exécution ; numéro unique |
| **Retry dangereux** | 🔴 **défaut** | Rejouer `SettleOrderBalance` réécrit et re-notifie ; rejouer `RegisterSale` **crée une 2ᵉ vente complète** (aucune clé d'idempotence) |
| **Exception technique exposée** | 🟠 **défaut partiel** | `Quantity <= 0` ⇒ `ArgumentException` (technique) au lieu d'une erreur métier ; `SaleFormViewModel.cs:877` affiche alors `ex.Message` brut |

### 17.2 Séparation des natures

- **Protection déjà acquise** : numérotation (CAS + unique), stock (CAS), frontière transactionnelle unique, FK `Restrict` client/produit/fiche atelier.
- **Défaut prouvé** (indépendant du provider) : absence de validation monétaire ; absence de contrôle client archivé ; `TotalPrice = 0` ; règlement du solde sans update conditionnel ; produit introuvable silencieux ; `SaleStatus` incohérente.
- **Limite SQLite** : sérialisation par verrou d'écriture global — masque partiellement les défauts de concurrence **en mono-fichier local**, mais **ne les corrige pas** et ne protège **pas** du lost update logique (deux transactions successives lisant la même valeur périmée).
- **Besoin futur PostgreSQL / SQL Server** : les CHECK monétaires seront pleinement exploitables ; le typage `numeric` remplacera `REAL` ; les écritures concurrentes deviendront réellement parallèles, **transformant les défauts ci-dessus en incidents fréquents**. (ADR-PROD-DB-001)

---

## 18. Persistance et contraintes

| Contrainte attendue | Existe ? | Preuve |
|---|:---:|---|
| `Quantity > 0` | ❌ | `SaleItemConfiguration.cs:25` — `IsRequired()` seul |
| `UnitPrice ≥ 0` | ❌ | `:28-30` |
| `TotalPrice ≥ 0` | ❌ | `:32-34` |
| `TotalAmount ≥ 0` | ❌ | `SaleConfiguration.cs:28-30` |
| `DiscountAmount ≥ 0` | ❌ | `:32-34` (défaut 0 seulement) |
| `FinalAmount ≥ 0` | ❌ | `:36-38` |
| `DepositAmount ≥ 0` | ❌ | `:40-41` |
| `RemainingAmount ≥ 0` | ❌ | `:43-44` |
| Unicité numéro de vente | ✅ | `:21-23` `idx_sales_sale_number_unique` |
| FK client | ✅ | `:68-71` `Restrict`, nullable |
| FK produit | ✅ | `SaleItemConfiguration.cs:54-57` `Restrict`, nullable |
| FK vente ↔ commande | ✅ | `SaleConfiguration.cs:83-86` `Cascade` |
| Index date de vente | ✅ | `:62-63` |
| Colonnes monétaires typées exact | ❌ | **`REAL`** partout (§8.1) |

**Aucune contrainte `CHECK` n'existe** sur `Sales`/`SaleItems`. Le seul `CHECK` du dépôt est un artifice de garde-fou de migration produit (`20260717183027_AddProductNormalizedReferenceAndProtectHistory.cs:63`).

---

## 19. Migrations éventuelles

| # | Question | Réponse |
|---:|---|---|
| 1 | Migration nécessaire au périmètre minimal ? | **NON.** Validation applicative + recalcul + contrôle client archivé se font **intégralement en Application/Domain**, à schéma constant. `dotnet ef migrations has-pending-model-changes` confirme l'absence de dérive. |
| 2 | Données historiques violeraient-elles une nouvelle contrainte ? | **OUI, de façon certaine.** Toute vente créée par `RegisterSaleUseCase` porte `SaleItem.TotalPrice = 0`. Un `CHECK (TotalPrice > 0)` **échouerait à la migration**. Un `CHECK (TotalPrice >= 0)` passerait mais serait sans valeur. De même, des montants incohérents peuvent exister. |
| 3 | Backfill possible sans inventer de montants ? | **Partiellement.** `TotalPrice = Quantity × UnitPrice` est **reconstituable exactement** (les deux colonnes sont fiables). En revanche, un `FinalAmount`/`RemainingAmount` incohérent **n'est pas réparable sans inventer** — impossible de savoir si l'erreur venait de la remise, de l'acompte ou du total. **Aucun backfill de ces colonnes ne doit être tenté.** |
| 4 | Une règle applicative suffit-elle ? | **OUI pour P3-7.** Le use case étant l'**unique** point d'écriture d'une vente (§6), une garde applicative couvre 100 % du chemin d'écriture réel. |
| 5 | `CHECK` portable vers le futur provider ? | **OUI** — `CHECK` est standard SQL (SQLite, PostgreSQL, SQL Server). Mais SQLite exige une **reconstruction de table** pour l'ajouter à une table existante (coûteux, risqué sur données historiques). |

> **Recommandation** : **aucune migration en P3-7.** Reporter les `CHECK` monétaires et la conversion `REAL → numeric` au chantier provider serveur (ADR-PROD-DB-001), où la reconstruction de table est de toute façon nécessaire. Un backfill **ciblé et sûr** de `SaleItem.TotalPrice` reste possible ultérieurement, **à décider séparément**.

---

## 20. Tests existants

| Fichier | Niveau | Tests |
|---|---|---:|
| `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs` | **Application, vrai SQLite** (`UseSqlite`, `Pooling=False`, jamais InMemory) | 8 |
| `tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs` | Domain | teste `SaleService`, **service orphelin non appelé en production** |
| `tests/MMV.App.Tests/ViewModels/SaleFormViewModelTransactionTests.cs` | UI | garde anti double-soumission |

Les 8 tests d'`RegisterSaleUseCaseTests` :

1. `ExecuteAsync_CounterSale_Frame_DecrementsStock_WritesMovement_AssignsSaleNumber`
2. `ExecuteAsync_SaleWithLenses_CreatesSupplierOrder_WithOrderNumber_AndNoStockMovement`
3. `ExecuteAsync_CounterSale_InsufficientStock_RollsBack_NoSale_NoNumberConsumed`
4. `ExecuteAsync_NullCommand_Throws`
5. `Constructor_WithoutTransactionRunner_Throws`
6. `ExecuteAsync_FabricationSale_DecrementsNonLensAtSale_NotTheLens`
7. `ExecuteAsync_MultipleLinesSameNonLensProduct_FinalStockCorrect`
8. `FrameAndLens_EachDecrementedAtTheRightMoment`

**Constat** : la couverture porte **exclusivement** sur le stock, la numérotation, l'atomicité et les gardes de construction. **Zéro test monétaire. Zéro test client. Zéro test de validation de ligne. Zéro test sur `SettleOrderBalanceUseCase`.**

---

## 21. Trous de couverture

| Règle ou risque | Test existant | Niveau | Suffisant ? | Test manquant |
|---|---|---|:---:|---|
| Vente sans ligne | ❌ | — | ❌ | Application/SQLite : `Lines` vide ⇒ refus, aucun numéro consommé |
| Quantité 0 | ❌ | — | ❌ | Refus métier (`BusinessRuleException`), pas `ArgumentException` |
| Quantité négative | ❌ | — | ❌ | Refus métier |
| Prix négatif | ❌ | — | ❌ | Refus métier |
| Prix 0 (gratuité) | ❌ | — | ❌ | **Accepté** (non-régression commerciale) |
| Remise négative | ❌ | — | ❌ | Refus métier |
| Remise > total | ❌ | — | ❌ | Refus métier (`FinalAmount` jamais négatif) |
| Acompte négatif | ❌ | — | ❌ | Refus métier |
| Acompte > final | ❌ | — | ❌ | Refus métier |
| Reste falsifié | ❌ | — | ❌ | `RemainingAmount` fourni faux ⇒ **recalculé**, valeur fournie ignorée |
| Total falsifié | ❌ | — | ❌ | `TotalAmount` fourni faux ⇒ **recalculé** depuis les lignes |
| `FinalAmount` falsifié | ❌ | — | ❌ | Recalculé depuis `Total − Discount` |
| **`SaleItem.TotalPrice`** | ❌ | — | ❌ | 🔴 **`TotalPrice == Quantity × UnitPrice` persisté** (aujourd'hui 0) |
| Recalcul backend complet | ❌ | — | ❌ | Chaîne `Line → Total → Final → Remaining` sur vente multi-lignes |
| **Client archivé** | ❌ | — | ❌ | 🔴 `BusinessRuleException`, message identique à P3-3B, **rollback complet** |
| Client introuvable | ❌ | — | ❌ | Erreur contrôlée, pas de `PersistenceException` brute |
| **Vente sans client** | ❌ | — | ❌ | 🔴 `CustomerId = null` ⇒ vente comptoir valide (impossible aujourd'hui) |
| Archivage concurrent | ❌ | — | ❌ | Client archivé pendant la saisie ⇒ refus **dans** la transaction |
| Produit inactif | ❌ | — | ❌ | Décision §22 puis test correspondant |
| **Produit introuvable** | ❌ | — | ❌ | 🔴 Erreur contrôlée au lieu du `continue` silencieux |
| Stock insuffisant 1ʳᵉ ligne | ✅ test 3 | Appli/SQLite | ✅ | — |
| Stock insuffisant ligne ultérieure | ❌ | — | ❌ | Rollback des décréments **déjà effectués** |
| Plusieurs lignes même produit | ✅ test 7 | Appli/SQLite | ✅ | — |
| Concurrence sur stock | ❌ (couvert P3-5 hors vente) | — | ⚠️ | Deux `RegisterSale` concurrents sur le dernier article |
| Rollback de la vente | ✅ test 3 | Appli/SQLite | ✅ | — |
| Rollback du numéro | ✅ test 3 | Appli/SQLite | ✅ | — |
| Création `Order` | ✅ test 2 | Appli/SQLite | ✅ | — |
| Données optiques | ❌ | — | ❌ | Copie fidèle ; décision NaN/∞ |
| `SaleStatus` | ❌ | — | ❌ | Selon option retenue §14.2 |
| `PaymentStatus` | ❌ | — | ❌ | 🔴 `Remaining > 0` ⇒ **ne doit pas être `Paid`** |
| **Règlement du solde** | ❌ **aucun test** | — | ❌ | 🔴 Nominal + idempotence + refus si `Remaining = 0` |
| **Double règlement concurrent** | ❌ | — | ❌ | 🔴 Deux postes ⇒ **un seul** encaissement, une seule notification |
| Suppression | ❌ | — | n/a | N'existe pas ⇒ **test de non-existence** (garde de non-régression) |
| Migration | — | — | ✅ | Aucune migration prévue |

**Bilan : 26 trous, dont 9 critiques.**

---

## 22. Risques classés

### 🔴 Critique

| # | Constat | Preuve (chemin exact) | Scénario | Impact | Couche | Étape cible |
|---:|---|---|---|---|---|---|
| C1 | **`SaleItem.TotalPrice` jamais affecté** | `RegisterSaleUseCase.cs:112-130` (absent) vs `SaleItemConfiguration.cs:32` `IsRequired()` | Toute vente enregistrée depuis la migration P2B-2C | **Toutes les lignes de vente valent 0 €** ; tout rapport, export ou recalcul basé sur `TotalPrice` est faux ; `SaleService.CalculateSaleAsync` recalculerait un total de **0** | Application | **P3-7** |
| C2 | **Montants intégralement falsifiables** | `RegisterSaleUseCase.cs:86-90` ; contrat `RegisterSaleCommand.cs:30-42` | Tout appelant fournit `Total=1000`, `Final=1`, `Remaining=-500` | Vente incohérente persistée sans obstacle ; caisse fausse | Application | **P3-7** |
| C3 | **Acompte > montant final** | Aucune comparaison `:89-90` | `Deposit = 500`, `Final = 100` | `RemainingAmount` **négatif** ; le client apparaît créditeur | Application | **P3-7** |
| C4 | **Reste à payer négatif** | idem | idem C3 | Solde négatif persisté et affiché | Application | **P3-7** |
| C5 | **Vente acceptée pour un client archivé** | Aucune occurrence `IsArchived` dans le domaine Ventes ; contraste `CreatePrescriptionUseCase.cs:113` | Poste A archive pendant que poste B saisit | Viole la règle P3-2B ; **exigence explicite de la roadmap P3-7** | Application | **P3-7** |
| C6 | **Double encaissement concurrent** | `SettleOrderBalanceUseCase.cs:87-88` — aucun update conditionnel | Deux postes règlent le même solde | **200 € encaissés comptés deux fois** ; deux notifications ; aucune trace corrective | Application | **P3-7** |
| C7 | **Règlement non idempotent** | `SettleOrderBalanceUseCase.cs:73-110` | Retry réseau ou double clic hors garde UI | Notifications fantômes ; acompte écrasé | Application | **P3-7** |
| C8 | **Produit introuvable ⇒ échec silencieux** | `RegisterSaleUseCase.cs:193` `if (product != null)` | `ProductId` supprimé/inexistant | Vente persistée, **stock jamais décrémenté**, aucune alerte ⇒ écart d'inventaire invisible | Application | **P3-7** |
| C9 | **Vente comptoir sans client impossible** | `RegisterSaleCommand.cs:21` `long` vs `Sale.cs:24` `long?` | Vente anonyme | `CustomerId = 0` ⇒ violation FK ⇒ vente **rejetée** ; fonctionnalité exigée par la roadmap inopérante | Application | **P3-7** |
| C10 | **Vente avec solde dû marquée `Paid`** | `SaleConfiguration.cs:52` défaut ; jamais écrit | Vente avec acompte partiel | `PaymentStatus = Paid` **faux** sur toute vente à crédit | Application | **P3-7** |

### 🟠 Important

| # | Constat | Preuve | Impact | Étape cible |
|---:|---|---|---|---|
| I1 | `SaleStatus` jamais avancée | `RegisterSaleUseCase.cs:99,105` seules écritures | Vente livrée affichée « en attente des verres » | **P3-7** (option §14.2) |
| I2 | `InFabrication`/`Ready`/`Cancelled` inatteignables | Inventaire §13.1 | Enum trompeuse | **P3-7** (documenter) |
| I3 | Aucune validation de ligne backend | §9, 13 points | Ligne vide, quantité 0, prix négatif persistables | **P3-7** |
| I4 | Produit inactif vendable | `Product.cs:112` jamais lu | Contourne la désactivation catalogue (P3-4B) | **P3-7** (décision) |
| I5 | `Quantity <= 0` ⇒ `ArgumentException` technique | `EfStockMutationService.cs:53` | Message technique affiché à l'utilisateur (`SaleFormViewModel.cs:877`) | **P3-7** |
| I6 | Paiement non historisé | Aucune entité `Payment` | Aucun journal d'encaissement | **dette** |
| I7 | Logique monétaire dupliquée et divergente | `SaleFormViewModel.cs:810-828` (actif) / `SaleService.cs:63-84` (mort) / `DbInitializer.cs:872-884` (seed) | Trois implémentations, une seule utilisée, aucune backend | **P3-7** (unifier) |
| I8 | `SaleService` orphelin mais testé | `SaleService.cs` ; `SaleServiceTests.cs` | Tests verts sur du code mort ⇒ **fausse assurance** | **P3-7** (documenter ou brancher) |
| I9 | `Order` créé même si `IsCounterSale` | `RegisterSaleUseCase.cs:141-142` (`hasLenses` seul) | Vente `Delivered` avec commande `New` | **P3-7** (documenter) |
| I10 | Vente comptoir sans `Order` non soldable | `SettleOrderBalanceCommand.cs:19` (`OrderId` seul) | Aucun chemin d'encaissement | **dette** |
| I11 | Données optiques non validées, NaN/∞ acceptés | `RegisterSaleUseCase.cs:122-129` ; `double?` | Verre incommandable, fiche atelier fausse | **P3-7** (borne minimale) ou report |
| I12 | Frontière d'agrégat franchie | `UseCases/Orders/SettleOrderBalance` écrit sur `Sale` | Responsabilité floue | **dette** |

### 🟡 Dette

| # | Constat | Preuve |
|---:|---|---|
| D1 | **Colonnes monétaires en `REAL`** malgré `decimal` C# | `SaleConfiguration.cs:29,33,37,41,44` ; `SaleItemConfiguration.cs:29,33` — **fixable seulement avec le provider serveur** |
| D2 | Aucune convention d'arrondi/devise documentée | Aucune ADR ; `€` codé en dur (`SettleOrderBalanceUseCase.cs:101`) |
| D3 | `Sale.StaffId` jamais renseigné | `RegisterSaleUseCase.cs:81-93` — aucune traçabilité vendeur |
| D4 | `RegisterSaleResult` expose l'entité `Sale` | `RegisterSaleResult.cs:39` — transit toléré, à retirer |
| D5 | `StockMovement` lié à la vente par texte | `RegisterSaleUseCase.cs:211` — pas de FK `SaleId` |
| D6 | Aucune contrainte `CHECK` | §18 |
| D7 | Aucune entité `Payment` | §15 |
| D8 | Aucune clé d'idempotence sur `RegisterSale` | §17.1 — un retry crée une 2ᵉ vente |
| D9 | **Avertissement de build xUnit1031** | `WorkshopSheetConcurrencyHardeningTests.cs:200` — hérité de P3-6B (§3.1) |
| D10 | Message `Client #` si `CustomerId` devient nullable | `RegisterSaleUseCase.cs:211` |

### 🔵 Report explicite

| Sujet | Destination |
|---|---|
| UI de vente (`SaleFormViewModel`, `SaleFormView`, `SalesView`) | **Redesign global** (décision « backend uniquement à partir de P3-4 ») |
| `Money` value object | **Futur** — explicitement hors périmètre roadmap P3-7 |
| TVA, facture, devis | **Hors P3** |
| Paiements multiples, échéanciers, entité `Payment` | **Hors P3** |
| Notifications (règles, anti-doublon, N+1) | **P3-8** |
| Rôles, utilisateur courant, `StaffId`, auteur d'encaissement | **P3-10** |
| Conversion `REAL → numeric`, `CHECK` DB, migration de typage | **Chantier production / provider serveur** (ADR-PROD-DB-001) |
| Annulation de vente, contre-mouvements, statut `Cancelled` | **Redesign métier futur** (aucune preuve métier) |
| Backfill de `SaleItem.TotalPrice` sur données historiques | **Décision séparée** (§19.3) |

---

## 23. Plan candidat P3-7

> Principe directeur : **le plus petit périmètre backend qui rend une vente cohérente, atomique et non falsifiable**, en réutilisant les patterns déjà validés (P3-3B pour le client archivé, P3-5 pour le stock, P3-1 pour la validation) et **sans toucher à l'UI, ni créer de migration**.

### 23.1 Obligatoire P3-7

| # | Action | Détail | Fichiers concernés |
|---:|---|---|---|
| **1** | **Validation des lignes** | Refus métier (`BusinessRuleException`) si : `Lines` vide ; `Quantity <= 0` ; `UnitPrice < 0`. **`UnitPrice = 0` accepté** (gratuité). Validation **avant** toute écriture, dans la transaction. | `RegisterSaleUseCase` + validateur Application |
| **2** | **Validation des montants** | `DiscountAmount >= 0` ; `DiscountAmount <= TotalAmount` (recalculé) ; `DepositAmount >= 0` ; `DepositAmount <= FinalAmount` (recalculé). | idem |
| **3** | **Formule de recalcul** | `TotalPrice = Quantity × UnitPrice` (**corrige C1**) ; `TotalAmount = Σ TotalPrice` ; `FinalAmount = TotalAmount − DiscountAmount` ; `RemainingAmount = FinalAmount − DepositAmount`. **Les valeurs `TotalAmount`, `FinalAmount`, `RemainingAmount` de la commande sont ignorées** (recalculées, jamais lues). | `RegisterSaleUseCase` |
| **4** | **Source du prix** | **Conserver `UnitPrice` de la commande** (prix négocié légitime), mais **le borner** (`>= 0`). Aligner sur le catalogue serait un **changement métier** non prouvé (remises, promotions, prix négociés) ⇒ **hors périmètre**, à documenter comme choix explicite. | — |
| **5** | **Produit introuvable** | Remplacer le `continue` silencieux (`:193`) par un refus métier explicite (**corrige C8**). | `RegisterSaleUseCase:192-196` |
| **6** | **Client archivé** | Injecter `ICustomerRepository` ; si `CustomerId` renseigné : charger, refuser si introuvable, lever `BusinessRuleException` si `IsArchived`. **Dans la transaction.** Message **identique** à `CreatePrescriptionUseCase` (**corrige C5**). | `RegisterSaleUseCase` |
| **7** | **Client facultatif** | Passer `RegisterSaleCommand.CustomerId` en **`long?`** ; adapter le libellé `Reason` (**corrige C9**). L'UI transmettant déjà un `long`, le changement est **source-compatible** côté appelant. | `RegisterSaleCommand`, `RegisterSaleUseCase:211` |
| **8** | **Transaction** | **Aucun changement** — la frontière unique est déjà correcte et prouvée (§11). Toute nouvelle vérification s'y insère, jamais à l'extérieur. | — |
| **9** | **Concurrence — règlement** | `SettleOrderBalanceUseCase` : refuser si `RemainingAmount <= 0` (idempotence, **corrige C7**) ; **update conditionnel** sur le solde attendu ou garde équivalente pour éliminer le lost update (**corrige C6**). Ne pas créer de notification si rien n'est encaissé. | `SettleOrderBalanceUseCase` |
| **10** | **`PaymentStatus`** | Le dériver du `RemainingAmount` **recalculé** : `Remaining == 0 ⇒ Paid` ; `0 < Remaining < Final ⇒ Partial` ; `Remaining == Final ⇒ Pending` (**corrige C10**). Le mettre à `Paid` lors du règlement du solde. | `RegisterSaleUseCase`, `SettleOrderBalanceUseCase` |
| **11** | **`SaleStatus`** | **Option E + D retenue à l'audit** : **déprécier** — documenter qu'il n'exprime que le type de vente à la création et **ne doit pas être lu** comme statut de fabrication/livraison (la vérité est `OrderStatus`). **Ne pas créer de machine à états** (rouvrirait P3-6). La vérité paiement passe à `PaymentStatus` (action 10). | `SaleStatus`, `Sale`, doc |
| **12** | **Suppression / modification** | **Ne rien créer.** L'absence de chemin est une protection (§16). Ajouter un test de non-régression documentant cette absence. | tests |
| **13** | **Migration** | **AUCUNE.** Schéma inchangé (§19). | — |
| **14** | **Tests** | Couvrir les 26 trous du §21, priorité aux 9 critiques. **Vrai SQLite** obligatoire (jamais InMemory), conformément à `RegisterSaleUseCaseTests`. Inclure : recalcul complet multi-lignes, montants falsifiés ignorés, `TotalPrice` persisté, acompte > final refusé, client archivé refusé, vente sans client acceptée, produit introuvable refusé, double règlement concurrent (un seul encaissement). | `RegisterSaleUseCaseTests` + nouveau fichier `SettleOrderBalanceUseCaseTests` |

### 23.2 Reportable (ne pas ouvrir en P3-7)

`Money` VO · TVA · facturation · devis · paiements multiples et échéanciers · entité `Payment` · annulation avancée et contre-mouvements · **toute UI** · audit utilisateur complet (`StaffId`, auteur d'encaissement) → **P3-10** · notifications → **P3-8** · conversion `REAL → numeric` et `CHECK` DB → **chantier provider serveur** · backfill de `TotalPrice` historique → **décision séparée** · validation optique fine des lignes verre → arbitrage en implémentation (borne minimale NaN/∞ seulement si peu coûteuse).

### 23.3 Points nécessitant un arbitrage à l'ouverture de P3-7

1. **Produit inactif** (I4) : refuser la vente d'un `IsActive = false` ? Cohérent avec P3-4B, mais **peut bloquer l'écoulement de stock existant** — arbitrage métier requis.
2. **Tolérance de comparaison** (D1) : le stockage `REAL` impose de décider si les recalculs se comparent **strictement** ou à tolérance. **Recommandation** : recalculer et **écraser** systématiquement plutôt que comparer — cela **évite entièrement** la question de la tolérance.
3. **`UnitPrice` catalogue vs commande** (action 4) : confirmer le maintien du prix fourni.

---

## 24. Reports explicites

Reports **maintenus** ou **créés** par cet audit :

| Report | Destination | Origine |
|---|---|---|
| UI de vente | Redesign global | Décision « backend uniquement à partir de P3-4 » |
| `Money` VO, TVA, facturation, devis | Futur / hors P3 | Roadmap §P3-7 |
| Paiements multiples, échéanciers, entité `Payment` | Hors P3 | Roadmap §P3-7 |
| Règles de notifications | **P3-8** | Roadmap |
| `StaffId`, utilisateur courant, auteur d'encaissement | **P3-10** | Roadmap + §22 D3 |
| `CHECK` monétaires, `REAL → numeric` | Chantier provider serveur | ADR-PROD-DB-001 + §19 |
| Annulation de vente, statut `Cancelled`, contre-mouvements | Redesign métier futur | §16 — aucune preuve métier |
| Backfill `SaleItem.TotalPrice` historique | Décision séparée | §19.3 |
| FK `StockMovement.SaleId` | Redesign métier futur | §22 D5 |
| Frontière d'agrégat `SettleOrderBalance` | Dette / refactoring | §15, §22 I12 |
| Vente comptoir sans `Order` non soldable | Dette | §15, §22 I10 |
| Clé d'idempotence sur `RegisterSale` | Dette | §22 D8 |
| Avertissement xUnit1031 (P3-6B) | Prochaine étape touchant ce fichier | §3.1, §22 D9 |

---

## 25. Verdict

### Critères de sortie

| Critère exigé | Statut | Référence |
|---|:---:|---|
| Toutes les écritures monétaires cartographiées | ✅ | §6 — 15 sites d'écriture, aucun SQL brut |
| Valeurs fournies / recalculées distinguées | ✅ | §7 (24 champs), §8.4 |
| Client archivé analysé | ✅ | §10 — défaut C5 prouvé + incohérence C9 |
| Atomicité vente/stock/commande/numéro prouvée | ✅ | §11 — frontière unique, rollback prouvé par test |
| `SaleStatus` et `OrderStatus` compris | ✅ | §13, §14 — 11 combinaisons, 5 options candidates |
| Règlement du solde audité | ✅ | §15 — 15 questions, C6/C7 prouvés |
| Concurrence analysée | ✅ | §17 — 9 actes, protections/défauts/limites séparés |
| Migration éventuelle cadrée | ✅ | §19 — **aucune migration**, justifié |
| Plan candidat précis | ✅ | §23 — 14 actions obligatoires, reports séparés |
| Rapport `.md` physique | ✅ | `docs/implementation/P3-7-sales-business-rules-audit-report.md` |
| Aucun code modifié | ✅ | §26 |

### Synthèse

Le flux de vente possède **un socle transactionnel solide et prouvé** (frontière unique, numérotation CAS, décrément de stock conditionnel, rollback complet) hérité de P2A et P3-5 — **rien de ce socle n'est à refaire**.

En revanche, **aucune règle métier ne protège son contenu** : `RegisterSaleUseCase` fait confiance à l'intégralité des montants fournis, ne valide aucune ligne, ne charge pas le client, n'oppose aucun refus au client archivé, ignore silencieusement un produit introuvable, et **omet totalement d'affecter `SaleItem.TotalPrice`** — ce dernier point corrompant, en base, chaque ligne de chaque vente enregistrée depuis P2B-2C.

Le règlement du solde est le **seul acte sensible du système dépourvu de protection concurrentielle**, alors que le stock, la numérotation et la fiche atelier en disposent tous — exposant un **double encaissement** en multi-poste.

**10 risques critiques, 12 importants, 10 dettes et 13 reports** sont documentés avec preuve, chemin exact, scénario, impact et étape cible. Le plan candidat tient en **14 actions backend, sans migration, sans UI**, et réutilise intégralement des patterns déjà validés dans les phases précédentes.

---

# ✅ P3-7 AUDIT = GO

---

## Décisions retenues pour l'implémentation

> Section ajoutée **à l'ouverture de l'implémentation P3-7**, pour figer les arbitrages laissés ouverts par
> l'audit (§23.3) et consigner les choix réellement appliqués. Détail complet et preuves :
> [rapport d'implémentation P3-7](P3-7-sales-business-rules-implementation-report.md).

| # | Décision | Portée retenue |
|---:|---|---|
| 1 | **Prix unitaire fourni conservé** | `command.Line.UnitPrice` reste le **prix négocié** de vente ; `Product.SalePrice` n'est jamais substitué. Seule borne : `≥ 0` (la gratuité reste un geste commercial légitime). Arbitrage §23.3.3 **confirmé**. |
| 2 | **Totaux recalculés** | `SalePricingPolicy` (Domain) devient le **propriétaire unique** du calcul. `TotalAmount`, `FinalAmount` et `RemainingAmount` fournis par la commande ne sont **ni lus, ni comparés, ni persistés** — écraser plutôt que comparer supprime entièrement la question de la tolérance `REAL` (arbitrage §23.3.2 **retenu**). `SaleItem.TotalPrice` est calculé (`Quantité × PrixUnitaire`), corrigeant C1 pour les **nouvelles** ventes ; aucun backfill historique n'est tenté. |
| 3 | **Client facultatif** | `RegisterSaleCommand.CustomerId` passe de `long` à `long?` : la vente au comptoir **sans client** devient possible (corrige C9). Le motif du mouvement de stock devient `Vente {n°} - Vente sans client` — plus jamais `Client #` sans identifiant (dette D10 close). |
| 4 | **Client actif acquis atomiquement** | Nouveau port `ICustomerRepository.TryAcquireActiveAsync` : `UPDATE … WHERE CustomerId = @id AND IsArchived = 0`, sans changement fonctionnel de valeur, servant de **prise de ligne** jusqu'au commit. Va **au-delà** du patron P3-3B (lecture puis test en mémoire), dont la course résiduelle est ici **fermée**. Message de refus **identique** à `CreatePrescriptionUseCase.CustomerArchivedMessage`. |
| 5 | **Produit obligatoire et actif** | Une ligne sans `ProductId`, dont le produit est introuvable, ou dont le produit est **inactif**, est refusée par une erreur métier stable. Le `continue` silencieux (C8) est supprimé. Arbitrage §23.3.1 **tranché : refuser** — la vente est le seul flux contraint ; `IStockMutationService` n'est **pas** durci, afin qu'une commande historique reste fabricable avec un produit désactivé. |
| 6 | **Produit actif acquis atomiquement** | Nouveau port `IProductRepository.TryAcquireActiveAsync`, même patron. Les identifiants sont **dédupliqués** : un produit présent sur plusieurs lignes n'est pris qu'une fois, chaque ligne décrémentant sa propre quantité. |
| 7 | **`PaymentStatus` = vérité paiement** | Dérivé explicitement des montants recalculés (`Paid` / `Partial` / `Pending`) ; le défaut EF `Paid` n'est plus jamais laissé décider (corrige C10). Option **D** de §14.2 retenue. |
| 8 | **`SaleStatus` non synchronisé, état initial corrigé** | Option **E** de §14.2 retenue : aucune machine à états, aucune synchronisation avec `OrderStatus`, aucune suppression d'enum ni de colonne. Seul l'état initial contradictoire est corrigé — le déclencheur devient la **présence de verres** (donc d'un `Order`), et non `IsCounterSale` : une vente qui attend des verres ne naît plus « livrée » (corrige I9). `Draft`, `InFabrication`, `Ready` et `Cancelled` restent une dette de modèle documentée. |
| 9 | **Règlement intégral atomique** | Nouveau port `ISaleRepository.TrySettleRemainingBalanceAsync` : `UPDATE … WHERE SaleId = @id AND RemainingAmount > 0`, posant `DepositAmount = FinalAmount`, `RemainingAmount = 0`, `PaymentStatus = Paid`. Corrige C6 et C7 : un seul passage à zéro, une seule notification, refus métier contrôlé pour le second appelant. Sémantique inchangée (règlement **intégral**, aucun montant fourni, aucune entité `Payment`, aucun `OrderStatus` touché). |
| 10 | **Aucune migration** | Schéma strictement inchangé (§19 confirmé). `CHECK` monétaires et conversion `REAL → numeric` restent reportés au chantier provider serveur. |
| 11 | **Correction du warning xUnit hérité** | `xUnit1031` (`WorkshopSheetConcurrencyHardeningTests.cs`, dette D9, héritée de P3-6B) corrigé par le plus petit changement asynchrone sûr, **test-only**, sans `Thread.Sleep` ni délai : build final **0 avertissement**. |

### Corrections apportées à l'audit par le code réel

Le code prime sur la documentation — y compris sur celle de cet audit. Un écart a été relevé pendant
l'implémentation :

| Constat de l'audit | Réalité vérifiée |
|---|---|
| §20 / §21 : « **Zéro test sur `SettleOrderBalanceUseCase`** » | **Inexact.** `tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs` existe depuis P2B-2G et couvre le nominal, l'absence de notification, l'introuvable, le rollback transactionnel et les gardes de construction. Ce qui manquait réellement était la couverture de l'**idempotence** et de la **concurrence** — ajoutée par P3-7 dans un fichier distinct. |

---

### Revue ciblée avant commit (ajout minimal, historique de l'audit inchangé)

> Section ajoutée lors de la dernière revue strictement ciblée précédant le commit P3-7. Ne modifie aucun constat
> ci-dessus ; référence uniquement l'état final. Détail complet et preuves :
> [rapport d'implémentation P3-7 § Revue ciblée avant commit](P3-7-sales-business-rules-implementation-report.md#revue-ciblée-avant-commit).

- **Tri déterministe des prises produit ajouté** : `AcquireProductsAsync` acquiert désormais les `ProductId`
  distincts par ordre **croissant** (`OrderBy(id => id)`), et non plus dans l'ordre du panier — deux ventes
  contenant les mêmes produits en ordres opposés acquièrent la même séquence de verrous (réduction du risque
  d'interblocage sur un futur provider serveur, ADR-PROD-DB-001).
- **Lectures fraîches non suivies ajoutées** après les prises atomiques (`ICustomerRepository.GetByIdFreshAsync`,
  `IProductRepository.GetByIdFreshAsync`, `AsNoTracking`) : ferme un risque concret où une entité Produit/Client
  déjà suivie dans le `DbContext` de la portée (p. ex. un écran catalogue/client resté ouvert) pouvait faire lire
  une catégorie produit périmée après une prise réussie, décidant à tort du moment du décrément de stock.
- **Portée historique exacte de `PaymentStatus`** : `PaymentStatus` est la vérité paiement pour les ventes
  **créées ou réglées par le backend P3-7** uniquement. **Aucun backfill historique** n'est réalisé — une vente
  antérieure à P3-7 peut encore porter le défaut EF `Paid` malgré un reste dû réel ; une réparation historique
  exigerait une décision séparée.

---

## 26. Validation documentaire

| Contrôle | Résultat |
|---|---|
| `git diff --check` | aucune sortie |
| `git diff --stat` | **aucun fichier suivi modifié** |
| `git status --short` | `?? design-handoff/`, `?? design/`, `?? docs/ui/` (préexistants) + `?? docs/implementation/P3-7-sales-business-rules-audit-report.md` |

**Seul nouveau fichier P3-7** : `docs/implementation/P3-7-sales-business-rules-audit-report.md`.

Aucun fichier Domain, Application, Infrastructure, test, migration ou UI modifié.
**Aucun commit. Aucun push. Aucune UI. STOP.**
