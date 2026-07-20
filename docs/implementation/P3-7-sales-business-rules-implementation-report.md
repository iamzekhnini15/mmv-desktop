# P3-7 — Implémentation backend des règles métier Ventes

> **MODE = IMPLEMENT_TEST_AND_WRITE_REPORT_NO_COMMIT** — backend uniquement.
> Aucun commit, aucun push, aucune UI, aucune migration.

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
| Date | 2026-07-20 |
| Livrable | `docs/implementation/P3-7-sales-business-rules-implementation-report.md` |

---

## 2. État Git / CI de départ

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `53d689e…bff3e` | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` | ✅ |
| `git rev-parse origin/p3-business-rules` | idem | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` | ✅ |
| Fichiers suivis modifiés au départ | aucun | aucun | ✅ |
| Non suivis au départ | audit P3-7 + `design-handoff/`, `design/`, `docs/ui/` | exactement ces quatre | ✅ |
| `git diff --check` | aucune sortie | aucune sortie | ✅ |

CI du SHA exact (`gh run view 29706846895`) :

| Champ | Valeur |
|---|---|
| `databaseId` | `29706846895` |
| `headSha` | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | **`success`** |

**Git ✅ / CI ✅** — seul le rapport d'audit P3-7 était présent comme changement P3-7 ; aucun fichier UI modifié ;
`design-handoff/`, `design/` et `docs/ui/` sont restés hors périmètre du début à la fin.

---

## 3. Baseline et avertissement hérité

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet restore MMV.sln` | OK | OK | ✅ |
| Build erreurs | 0 | **0** | ✅ |
| Tests Domain | 430 | **430** | ✅ |
| Tests Application | 362 | **362** | ✅ |
| Tests App | 239 | **239** | ✅ |
| **Total** | **1031** | **1031** | ✅ |
| Échecs / ignorés | 0 / 0 | **0 / 0** | ✅ |
| Vulnérabilités (transitives incluses) | 0 | **0** sur les 7 projets | ✅ |
| Migrations en attente | 0 | « No changes have been made to the model since the last migration » | ✅ |
| Références `MMV.Application` | `MMV.Domain` seul | `..\MMV.Domain\MMV.Domain.csproj` seul | ✅ |

### 3.1 Avertissement hérité (dette D9)

```
tests\MMV.Application.Tests\UseCases\WorkshopSheets\WorkshopSheetConcurrencyHardeningTests.cs(200,92):
warning xUnit1031: Test methods should not use blocking task operations, as they can cause deadlocks.
```

- **Origine** : commit P3-6B `53d689e` — donc **présent au SHA exact validé vert par la CI `29706846895`**. Ce
  n'est pas une régression P3-7 mais une dette héritée, non détectée par la clôture P3-6B.
- **Nature** : avertissement d'analyseur, **code de test uniquement**, aucun impact runtime produit.
- **Traitement** : corrigé par P3-7, cf. §17.

---

## 4. Décisions finales

Les onze décisions retenues sont consignées dans l'audit
([§ « Décisions retenues pour l'implémentation »](P3-7-sales-business-rules-audit-report.md)) et appliquées telles
quelles. Synthèse :

1. prix unitaire fourni **conservé** (prix négocié) ;
2. totaux **recalculés** par le backend, valeurs fournies jamais lues ni comparées ;
3. client **facultatif** ;
4. client actif **acquis atomiquement** ;
5. produit **obligatoire, existant et actif** ;
6. produit actif **acquis atomiquement**, identifiants dédupliqués ;
7. `PaymentStatus` = **vérité paiement** ;
8. `SaleStatus` **non synchronisé**, état initial corrigé ;
9. règlement du solde **intégral et atomique** ;
10. **aucune migration** ;
11. correction du **warning xUnit hérité**.

---

## 5. Politique monétaire

**Fichier créé** : `src/MMV.Domain/Services/SalePricingPolicy.cs`.

Politique Domain **pure** (aucun dépôt, aucun `UnitOfWork`, aucun état, aucun effet de bord), **propriétaire
unique** du calcul monétaire d'une création de vente.

**Entrées** : `IReadOnlyList<SaleLinePricingInput>` (`Quantity`, `UnitPrice`), `discountAmount`, `depositAmount`.

**Sortie immuable** (`SalePricing`) : total par ligne, `TotalAmount`, `DiscountAmount`, `FinalAmount`,
`DepositAmount`, `RemainingAmount`, `PaymentStatus`.

| Règle | Implémentation |
|---|---|
| au moins une ligne | collection nulle **ou** vide ⇒ `EmptyLinesMessage` |
| `Quantity > 0` | `QuantityMessage` |
| `UnitPrice >= 0` | `UnitPriceMessage` — **`UnitPrice = 0` reste autorisé** |
| `LineTotal = Quantity × UnitPrice` | seule source de `SaleItem.TotalPrice` |
| `TotalAmount = Σ LineTotal` | — |
| `DiscountAmount >= 0` | `DiscountNegativeMessage`, opposé **avant** tout calcul |
| `DiscountAmount <= TotalAmount` | `DiscountAboveTotalMessage` |
| `FinalAmount = TotalAmount − DiscountAmount` | jamais négatif |
| `DepositAmount >= 0` | `DepositNegativeMessage`, opposé **avant** tout calcul |
| `DepositAmount <= FinalAmount` | `DepositAboveFinalMessage` |
| `RemainingAmount = FinalAmount − DepositAmount` | jamais négatif |

**Aucun arrondi automatique.** **Aucun `Money` value object.** **Aucune comparaison** entre les montants fournis
par la commande et les montants calculés : les valeurs calculées **remplacent systématiquement** les valeurs
fournies — ce qui supprime entièrement la question de la tolérance vis-à-vis du stockage SQLite `REAL`
(audit §8.1, arbitrage §23.3.2).

**Dépassement de capacité `decimal`** : l'arithmétique `decimal` lève toujours en dépassement, indépendamment du
contexte `checked`. Le calcul est enveloppé, et toute `OverflowException` est convertie en `BusinessRuleException`
portant le message métier stable `AmountOutOfRangeMessage`. **Aucune `OverflowException` brute ne remonte** — trois
tests le prouvent, dont un `NotThrow<OverflowException>` explicite.

---

## 6. Validation des lignes

Effectuée **avant toute transaction et toute numérotation** : une commande invalide ne consomme **aucun numéro de
vente** et ne touche jamais le dépôt.

| Refus | Message | Propriétaire de la règle |
|---|---|---|
| collection `Lines` **nulle** | `SalePricingPolicy.EmptyLinesMessage` | `RegisterSaleUseCase.RequireProductOnEveryLine` |
| collection **vide** | `SalePricingPolicy.EmptyLinesMessage` | idem |
| `ProductId == null` | `RegisterSaleUseCase.ProductRequiredMessage` | idem |
| `Quantity <= 0` | `SalePricingPolicy.QuantityMessage` | `SalePricingPolicy` |
| `UnitPrice < 0` | `SalePricingPolicy.UnitPriceMessage` | `SalePricingPolicy` |

Chaque règle est évaluée **une seule fois, par son propriétaire** — aucune formule n'est recopiée dans le use case.

Le comportement silencieux d'avant P3-7 (une ligne sans produit, ou dont le produit était introuvable, était
**persistée sans mutation de stock**) est supprimé : toute ligne invalide annule la transaction complète.

**Plusieurs lignes du même produit restent autorisées** : la déduplication porte uniquement sur les **prises**
atomiques, jamais sur les lignes ; chaque ligne conserve son propre `SaleItem`, son propre décrément et son propre
mouvement de stock.

`Quantity <= 0` produit désormais une **erreur métier**, et non plus l'`ArgumentException` **technique** que
levait le décrément de stock et que l'UI affichait brute (audit §22 I5).

---

## 7. Validation optique

**Fichier créé** : `src/MMV.Domain/Services/SaleLineOpticsPolicy.cs` — politique Domain pure, appliquée **aux
seules lignes `LensOd` / `LensOg`** (comportement d'origine conservé pour les autres types).

Invariants opposés, strictement ceux **déjà établis par P3-3** :

| Invariant | Message |
|---|---|
| `Sphere`, `Cylinder`, `Addition`, `PrismValue` **finies** (NaN et infinis refusés) | `PrescriptionValidator.FiniteValueMessage` |
| cylindre non nul ⇒ axe obligatoire | `PrescriptionValidator.AxisRequiredMessage` |
| axe sans cylindre orientable ⇒ refus | `PrescriptionValidator.AxisWithoutCylinderMessage` |
| axe dans `[0, 180]` (plage réellement admise par le dépôt) | `SaleLineOpticsPolicy.AxisOutOfRangeMessage` |
| prisme renseigné ⇒ base obligatoire | `PrescriptionValidator.PrismBaseRequiredMessage` |
| base renseignée ⇒ prisme obligatoire | `PrescriptionValidator.PrismBaseWithoutValueMessage` |
| prisme négatif refusé | `PrescriptionValidator.PrismValueNegativeMessage` |
| **normalisation `Axis 0 → 180`** | `OpticalAxisNormalizer.NormalizeAxis` |

Les messages sont **réutilisés littéralement** depuis `PrescriptionValidator` : une même incohérence optique
produit le même texte, qu'elle soit saisie sur une ordonnance (P3-3B) ou sur une ligne de vente (P3-7).

**Les champs optiques restent facultatifs** : aucune ordonnance complète n'est exigée, un œil entièrement vide
reste valide. **Aucune prescription existante n'est lue ni modifiée**, **aucune FK `PrescriptionId` n'est créée** :
les données de vente demeurent un **instantané copié** (comportement confirmé correct par l'audit §9.11), ce qu'un
test prouve explicitement.

Les contrôles sont **centralisés** dans la politique ; le use case n'en duplique aucun. La structure d'entrée
(`SaleLineOptics`) est un `record struct` immuable : la valeur canonique est **renvoyée**, jamais écrite dans
l'entrée.

---

## 8. Client facultatif

`RegisterSaleCommand.CustomerId` : **`long` → `long?`**.

- Une vente sans client est **acceptée** ; aucun client n'est chargé ; `Sale.CustomerId = null` est réellement
  persisté.
- Les champs `TotalAmount`, `FinalAmount` et `RemainingAmount` sont **conservés** dans le contrat pour la
  compatibilité source de l'appelant UI existant, mais documentés comme **hérités et ignorés par le backend** —
  jamais lus pour persister la vente. **Aucun `[Obsolete]`** n'a été posé (il produirait des avertissements dans
  l'UI, hors périmètre).
- `DiscountAmount` et `DepositAmount` restent de **véritables entrées métier**, validées et bornées.
- **Aucune modification UI n'a été nécessaire** : `SaleFormViewModel.BuildRegisterSaleCommand` affecte un `long` à
  un `long?`, ce qui est source-compatible.

**Motif du mouvement de stock** :

| Cas | Motif |
|---|---|
| avec client | `Vente {SaleNumber} - Client #{CustomerId}` |
| sans client | `Vente {SaleNumber} - Vente sans client` |

Plus aucun `Client #` sans identifiant n'est produisible (dette D10 close).

---

## 9. Acquisition atomique du client actif

**Port créé** : `ICustomerRepository.TryAcquireActiveAsync(long customerId, CancellationToken)`.
**Implémentation** : `CustomerRepository` —

```
UPDATE Customers SET IsArchived = 0 WHERE CustomerId = @id AND IsArchived = 0
```

- **1 ligne affectée** ⇒ client actif pris ; la ligne reste prise **jusqu'au commit** de la transaction de la
  vente.
- **0 ligne** ⇒ refus. Une lecture **purement diagnostique** distingue alors « introuvable » d'« archivé ` ; elle
  ne décide **jamais** de l'écriture, déjà tranchée par la condition atomique.

| Cas | Exception | Message |
|---|---|---|
| client archivé | `BusinessRuleException` | `CreatePrescriptionUseCase.CustomerArchivedMessage` — **identique à P3-3B** |
| client introuvable | `BusinessRuleException` | `RegisterSaleUseCase.CustomerNotFoundMessage` |

**Aucun message EF/SQLite n'est exposé.**

**Pourquoi une prise plutôt qu'une lecture.** Le patron P3-3B (charger le client, tester `IsArchived` en mémoire)
laisse une course explicitement assumée : un autre poste peut archiver le client entre la lecture et le commit.
Ici, la condition est évaluée par la **même instruction** que l'écriture — il n'existe plus d'intervalle entre
décider et écrire. La mise à jour **ne change aucune donnée fonctionnelle** (elle réaffecte à `IsArchived` la
valeur qu'il doit déjà porter) et sert exclusivement de prise de ligne, suivant les conventions déjà établies par
`TryTransitionStatusAsync` (P3-5) et le décrément conditionnel de stock (P2A-1D). Un test le vérifie :
après une vente réussie, `IsArchived` du client vaut toujours `false`.

**Aucune migration n'a été créée.**

> **Note de cohérence de message.** Le message P3-3B mentionne « …avant de créer une nouvelle **ordonnance** »,
> alors qu'il est ici opposé à une **vente**. Sa réutilisation littérale était **explicitement exigée** (message
> stable identique à P3-3B), et garantit qu'un unique texte est à reconnaître par l'appelant. La reformulation de
> ce libellé partagé relève d'une décision produit, hors périmètre P3-7.

---

## 10. Produits

Pour **tous les `ProductId` distincts** de la commande :

- le produit doit **exister** ;
- le produit doit être **actif** ;
- sa **catégorie réelle est chargée par le backend** — l'appelant ne peut ni la fournir ni la contourner ;
- fournir directement l'identifiant d'un produit inactif ne permet **plus** de contourner `IsActive` (audit I4).

| Cas | Message |
|---|---|
| produit introuvable | `RegisterSaleUseCase.ProductNotFoundMessage` |
| produit inactif | `RegisterSaleUseCase.ProductInactiveMessage` |

**`IStockMutationService` n'a pas été durci** : une commande historique doit rester fabricable même si son produit
a été désactivé entre-temps (le décrément de fabrication passe par `AdvanceOrderStatusUseCase`). La contrainte
« produit actif » est portée par le **flux de vente seul**.

---

## 11. Acquisition atomique du produit actif

**Port créé** : `IProductRepository.TryAcquireActiveAsync(long productId, CancellationToken)`.
**Implémentation** : `ProductRepository` —

```
UPDATE Products SET IsActive = 1 WHERE ProductId = @id AND IsActive = 1
```

- **1 ligne** ⇒ produit actif pris jusqu'au commit ; **0 ligne** ⇒ refus, puis lecture **purement diagnostique**
  pour distinguer introuvable / inactif.
- **Aucune donnée catalogue n'est modifiée** — en particulier **jamais `StockQuantity`** (invariant P3-5).
- Une **désactivation concurrente** survenue entre la constitution du panier et l'écriture est donc refusée : un
  test dédié désactive le produit depuis un autre contexte avant l'appel et vérifie le refus.
- Les identifiants sont **dédupliqués** : un produit présent sur plusieurs lignes n'est pris et chargé
  qu'**une seule fois**, tandis que chaque ligne décrémente sa propre quantité (test dédié).

---

## 12. Recalcul et persistance

`RegisterSaleUseCase` a été réorganisé en conservant **une seule transaction** :

```
ExecuteAsync
 ├─ 0. garde command != null
 ├─ 0. validation PURE des lignes + calcul monétaire            ← hors transaction, hors dépôt
 └─ ITransactionRunner.RunAsync                                  ← frontière UNIQUE, inchangée
     ├─ 1. acquisition atomique du client actif (si fourni)
     ├─ 2. acquisition atomique + chargement des produits actifs distincts
     ├─ 3. attribution du numéro de vente (CAS)
     ├─ 4. création de Sale — montants recalculés, PaymentStatus explicite, SaleStatus cohérent
     ├─ 5. création des SaleItem — TotalPrice = Quantité × PrixUnitaire, optique canonique
     ├─ 6. SaveChangesAsync #1                                   ← obtient SaleId (conservé : réellement nécessaire)
     ├─ 7. création de l'Order si lignes verre (numéro ORDER, CAS)
     ├─ 8. décrément des non-verres via IStockMutationService
     ├─ 9. mouvements de stock signés (Quantity < 0)
     ├─ 10. SaveChangesAsync #2
     └─ 11. commit
```

Les **deux** `SaveChangesAsync` sont conservés : le premier est réellement nécessaire à l'obtention de `SaleId`
avant la création de l'`Order`.

**Montants de la commande** : `command.TotalAmount`, `command.FinalAmount` et `command.RemainingAmount` sont
**ignorés** — ni lus, ni comparés. **Aucune tolérance** liée au stockage SQLite `REAL` n'a été introduite.

**Prix** : `command.Line.UnitPrice` est conservé comme prix négocié ; `Product.SalePrice` n'est jamais substitué
(test dédié comparant les deux).

**Produit introuvable** : le `continue` silencieux est **supprimé**. Toute ligne invalide annule la transaction
complète avant qu'une vente partielle ne subsiste.

---

## 13. Stock et atomicité

**La politique P3-5 est intacte** — aucun contournement n'a été réintroduit :

| Exigence P3-5 | État |
|---|---|
| tout décrément via `IStockMutationService` | ✅ inchangé |
| non-verres décrémentés à l'enregistrement | ✅ inchangé |
| verres décrémentés au passage en fabrication | ✅ inchangé |
| aucune ligne décrémentée deux fois | ✅ inchangé |
| mouvement signé cohérent (`-quantity`) | ✅ inchangé |
| mouvement et vente dans la même transaction | ✅ inchangé |

**Tous les tests P3-5 existants sont conservés et passent** (`RegisterSaleUseCaseTests`, 8 tests).

Tests d'atomicité **ajoutés** :

- décrément **réussi** sur la première ligne, puis stock insuffisant sur la suivante ⇒ **rollback du décrément
  déjà effectué**, aucune vente, aucun mouvement, numéro non consommé ;
- rollback de l'**`Order`** éventuel et de son numéro `ORDER` ;
- **deux ventes concurrentes sur le dernier article** ⇒ **exactement un succès**, un refus métier
  (`InsufficientStockException`), stock **jamais négatif**, vente perdante **totalement annulée**, **aucun
  mouvement résiduel**.

**Limite SQLite documentée honnêtement.** SQLite n'admet qu'un seul écrivain : les écritures y sont **sérialisées**.
Exécuter deux tentatives concurrentes depuis deux connexions simultanées produirait un **verrou**, pas une course.
Les tests exercent donc les deux tentatives **l'une après l'autre sur le même état de départ**, ce qui reproduit
exactement la décision que l'écriture conditionnelle doit prendre — **sans aucun délai fragile, sans
`Thread.Sleep`**. La protection réelle provient de la primitive conditionnelle, pas de l'ordonnancement du test ;
c'est précisément ce que ces tests vérifient.

---

## 14. `SaleStatus`

**Aucune nouvelle machine à états n'a été créée. Aucune synchronisation avec `OrderStatus` n'a été introduite.**
L'enum et sa colonne sont **conservées** (les supprimer exigerait une migration et toucherait des lecteurs
potentiels).

Seul l'**état initial contradictoire** est corrigé — le déclencheur devient la présence de verres (donc la création
d'un `Order`), et non `IsCounterSale` :

| Cas | Statut initial |
|---|---|
| au moins une ligne verre (un `Order` sera créé) | `SaleStatus.AwaitingLenses` |
| sinon | `SaleStatus.Delivered` |

Une vente déclarée « comptoir » mais contenant un verre ne naît donc **plus** « livrée » alors que sa commande
fournisseur vient d'être créée en `New` (audit §22 I9).

**Documenté dans le code et ici** :

- `SaleStatus` est un **indicateur historique initial** ;
- il **ne doit pas** être utilisé comme vérité de fabrication ou de livraison ;
- **`OrderStatus` reste la vérité du workflow atelier** ;
- **`PaymentStatus` devient la vérité du paiement** ;
- `Draft`, `InFabrication`, `Ready` et `Cancelled` restent une **dette de modèle**, sans nouvelle règle P3-7.

Un test prouve l'**absence** de synchronisation : la commande avance en `ToFabricate`, la vente reste
`AwaitingLenses`.

> **Dette relevée et non traitée (volontairement).** `Sale.EstimatedDelivery` reste piloté par `IsCounterSale`
> (sémantique d'origine conservée telle quelle), et peut donc se désaligner du statut sur une vente comptoir
> contenant un verre. La consigne limitant la correction au **seul** statut initial, ce point est **documenté ici
> comme dette** plutôt que modifié hors périmètre.

---

## 15. `PaymentStatus`

Calculé **explicitement** par `SalePricingPolicy.DerivePaymentStatus` — le défaut EF `Paid` n'est **jamais** laissé
décider (audit §22 C10 : toute vente à crédit était enregistrée « payée ») :

| Condition | Statut |
|---|---|
| `FinalAmount == 0` **ou** `RemainingAmount == 0` | `Paid` |
| `DepositAmount == 0` **et** `RemainingAmount > 0` | `Pending` |
| `DepositAmount > 0` **et** `RemainingAmount > 0` | `Partial` |

Lors du règlement complet du solde : `DepositAmount = FinalAmount`, `RemainingAmount = 0`,
`PaymentStatus = Paid`.

Chaque état est couvert par des tests, au niveau Domain (politique) **et** Application (valeur réellement
persistée en base), y compris un test vérifiant qu'une vente à crédit **n'est pas** `Paid`.

---

## 16. Règlement du solde

**Port créé** : `ISaleRepository.TrySettleRemainingBalanceAsync(long saleId, CancellationToken)`.
**Implémentation** : `SaleRepository` —

```
UPDATE Sales
SET DepositAmount = FinalAmount, RemainingAmount = 0, PaymentStatus = Paid
WHERE SaleId = @id AND RemainingAmount > 0
```

- **1 ligne affectée** ⇒ succès.
- **0 ligne** ⇒ diagnostiqué proprement : vente introuvable, solde déjà nul, ou conflit concurrent. Le résultat
  expose `AlreadySettled = true`, `AmountEncashed = 0`, `HasNotification = false`. **Aucun message EF/SQLite.**
- `RemainingAmount` étant nullable, une valeur `NULL` ne satisfait pas `> 0` : une vente au solde inconnu n'est
  **jamais** réglée à l'aveugle.
- `DepositAmount` est posé à la valeur de `FinalAmount` **telle qu'elle est en base**, jamais une valeur lue puis
  renvoyée : aucun encaissement ne peut se fonder sur un montant périmé.

**Sémantique conservée** : l'opération règle l'**intégralité** du solde restant ; aucun montant partiel n'est
fourni. **Aucune entité `Payment` n'est créée**, **aucun paiement partiel n'est traité**, **aucun statut de commande
n'est modifié** (test dédié : un `Order` en `InProgress` le reste).

**Notification** : le solde est lu **avant** la prise atomique **uniquement** pour libeller le montant du message.
La notification n'est créée **que si** l'UPDATE conditionnel a réussi. La lecture de diagnostic ne décide jamais
de l'écriture.

**Alignement de l'entité renvoyée.** `ExecuteUpdateAsync` contourne le change tracker : l'entité `Sale` chargée
reste porteuse du solde périmé. Les valeurs sont donc réalignées en mémoire **après le dernier
`SaveChangesAsync`**, si bien qu'elles ne sont **jamais réécrites** par le tracker — l'UPDATE conditionnel demeure
l'**unique auteur** du règlement, tandis que la ViewModel réaffiche un solde correct.

---

## 17. Concurrence

| Acte | Protection avant P3-7 | Protection après P3-7 |
|---|---|---|
| numéro de vente / commande | ✅ CAS + index unique | ✅ inchangé |
| décrément de stock | ✅ décrément conditionnel (P2A-1D) | ✅ inchangé |
| **client archivé en vente** | ❌ **aucune** | ✅ **prise atomique conditionnelle** |
| **produit inactif en vente** | ❌ **aucune** | ✅ **prise atomique conditionnelle** |
| **montants de vente** | ❌ **falsifiables** | ✅ **recalculés backend** |
| **règlement du solde** | 🔴 **aucune — double encaissement** | ✅ **UPDATE conditionnel atomique** |

Deux appels concurrents au règlement produisent : **un seul succès**, **un seul passage à zéro**, **une seule
notification**, **un refus métier contrôlé** pour l'autre, **aucun message EF/SQLite** — prouvé par test.

---

## 18. Code mort supprimé

Recherche exhaustive `git grep -n -E "ISaleService|SaleService" -- src tests` avant suppression :

| Occurrence | Nature |
|---|---|
| `src/MMV.Domain/Services/SaleService.cs` (×3) | la définition elle-même |
| `src/MMV.Infrastructure/DependencyInjection.cs:72` | enregistrement DI |
| `tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs` (×5) | tests du code mort |

**Aucun consommateur vivant** — constat de l'audit confirmé par le code.

**Supprimés** :

- `src/MMV.Domain/Services/SaleService.cs` (interface + implémentation, 85 lignes) ;
- son enregistrement DI (remplacé par un commentaire explicatif, suivant le précédent P3-3B / P3-6) ;
- `tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs` (5 tests, 158 lignes) — des tests **verts sur du code
  mort**, donc une **assurance fausse**.

Leur valeur utile est **remplacée** par les 24 tests de `SalePricingPolicyTests`, qui exercent la formule
**réellement exécutée**.

**Aucune deuxième formule monétaire codée en dur ne subsiste dans le backend vivant** — vérifié par un test
d'architecture (`UneSeulePolitiqueMonetaireDeVente_ExisteDansLeDomaine`). `SaleFormViewModel` (UI) et
`DbInitializer` (seed de démonstration) conservent leurs calculs, tous deux **hors backend de production** et hors
périmètre.

**Aucune logique UI n'a été modifiée.**

---

## 19. Absence de suppression / modification

**Rien n'a été créé** : aucun `DeleteSaleUseCase`, aucun `UpdateSaleUseCase`, aucun statut d'annulation, aucun
contre-mouvement.

Un test de non-régression le verrouille :
`ApplicationArchitectureTests.Application_DoesNotExpose_SaleDeletionOrMutationUseCase`.

La politique d'annulation **reste reportée** faute de règle métier validée. L'absence de chemin de suppression est
aujourd'hui une **protection de fait** (audit §16) : une cascade `Sale → Order → OrderItem` détruirait l'historique
de commande tout en laissant des mouvements de stock **orphelins** (aucune FK ne les relie à la vente).

---

## 20. Fichiers modifiés et créés

### Créés (7)

| Fichier | Rôle |
|---|---|
| `src/MMV.Domain/Services/SalePricingPolicy.cs` | politique monétaire pure, propriétaire unique |
| `src/MMV.Domain/Services/SaleLineOpticsPolicy.cs` | invariants optiques purs d'une ligne de vente |
| `tests/MMV.Domain.Tests/ServiceTests/SalePricingPolicyTests.cs` | 24 tests |
| `tests/MMV.Domain.Tests/ServiceTests/SaleLineOpticsPolicyTests.cs` | 27 tests |
| `tests/MMV.Domain.Tests/ServiceTests/SalesBusinessRulesArchitectureTests.cs` | 5 tests |
| `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleBusinessRulesTests.cs` | 40 tests (vrai SQLite) |
| `tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceAtomicityTests.cs` | 7 tests (vrai SQLite) |

### Modifiés (14)

| Fichier | Changement |
|---|---|
| `src/MMV.Domain/Interfaces/Repositories/ICustomerRepository.cs` | + `TryAcquireActiveAsync` |
| `src/MMV.Domain/Interfaces/Repositories/IProductRepository.cs` | + `TryAcquireActiveAsync` |
| `src/MMV.Domain/Interfaces/Repositories/ISaleRepository.cs` | + `TrySettleRemainingBalanceAsync` |
| `src/MMV.Infrastructure/Repositories/CustomerRepository.cs` | implémentation conditionnelle |
| `src/MMV.Infrastructure/Repositories/ProductRepository.cs` | implémentation conditionnelle |
| `src/MMV.Infrastructure/Repositories/SaleRepository.cs` | implémentation conditionnelle |
| `src/MMV.Infrastructure/DependencyInjection.cs` | retrait de l'enregistrement `ISaleService` |
| `src/MMV.Application/UseCases/Sales/RegisterSaleCommand.cs` | `CustomerId` → `long?` ; champs hérités documentés |
| `src/MMV.Application/UseCases/Sales/RegisterSaleUseCase.cs` | réorganisation complète du flux |
| `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs` | règlement atomique |
| `src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceResult.cs` | + `AlreadySettled` |
| `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs` | signature du constructeur |
| `tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs` | signature du constructeur |
| `tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs` | + garde Delete/Update Sale |
| `tests/MMV.Application.Tests/UseCases/WorkshopSheets/WorkshopSheetConcurrencyHardeningTests.cs` | correction xUnit1031 (test-only) |

### Supprimés (2)

- `src/MMV.Domain/Services/SaleService.cs`
- `tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs`

### Documentation

- `docs/implementation/P3-7-sales-business-rules-audit-report.md` — section « Décisions retenues pour
  l'implémentation » ajoutée ;
- `docs/implementation/P3-7-sales-business-rules-implementation-report.md` — **ce rapport** ;
- `docs/architecture/P3-business-rules-roadmap.md` — §P3-7 mis à jour **minimalement**.

**Aucun fichier sous `src/MMV.App/**`, `design-handoff/**`, `design/**`, `docs/ui/**`,
`src/MMV.Infrastructure/Migrations/**`, ni aucune configuration EF de `Sale`/`SaleItem` n'a été touché.**

---

## 21. Tests ajoutés

**103 tests ajoutés** (56 Domain + 47 Application), **5 supprimés** (code mort).

Tous les tests Application sensibles utilisent de **vraies bases SQLite jetables** (`UseSqlite`, `Pooling=False`,
répertoire temporaire nettoyé) — **jamais le provider EF InMemory**.

| Domaine couvert | Fichier | Tests |
|---|---|---:|
| calcul monétaire (lignes, remise, acompte, 4 montants, 3 `PaymentStatus`, dépassement `decimal`) | `SalePricingPolicyTests` | 24 |
| invariants optiques (finitude, cylindre ⇄ axe, prisme ⇄ base, plage, normalisation, périmètre, non-mutation) | `SaleLineOpticsPolicyTests` | 27 |
| architecture Domain (service mort absent, politique unique, pureté) | `SalesBusinessRulesArchitectureTests` | 5 |
| recalcul, lignes, produits, client, optique, `SaleStatus`, stock et atomicité | `RegisterSaleBusinessRulesTests` | 40 |
| règlement nominal, idempotence, concurrence, introuvable | `SettleOrderBalanceAtomicityTests` | 7 |
| garde Delete/Update Sale | `ApplicationArchitectureTests` | 1 |

Points notables prouvés :

- totaux fournis **falsifiés** (`Total = 1`, `Final = 1`, `Remaining = −500`) **ignorés** et recalculés ;
- `SaleItem.TotalPrice` réellement persisté, et explicitement **différent de 0** ;
- prix négocié conservé et **différent** du prix catalogue ;
- vente sans ligne refusée **avant** consommation d'un numéro ;
- refus ⇒ **aucune** vente, aucun `SaleItem`, aucun `Order`, aucun mouvement, **numéro non consommé** ;
- `CustomerId = null` **réellement persisté** et motif de mouvement adapté ;
- archivage et désactivation **concurrents** refusés ;
- axe `0` normalisé en `180` **sur la vente et sur la commande fournisseur** ;
- ordonnance existante **non modifiée** par une vente ;
- vente comptoir contenant un verre ⇒ `AwaitingLenses` ;
- concurrence stock et concurrence règlement ⇒ **exactement un succès** dans les deux cas.

Aucun total n'a été **fixé à l'avance** : chaque attendu est exprimé à partir des entrées du test.

---

## 22. Résultats ciblés

```
dotnet test tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj --no-build -c Debug
  Passed!  Failed: 0, Passed: 481, Skipped: 0, Total: 481

dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj --no-build -c Debug
  Passed!  Failed: 0, Passed: 410, Skipped: 0, Total: 410
```

| Suite ciblée | Résultat |
|---|---|
| `SalePricingPolicyTests` | 24 / 24 ✅ |
| `SaleLineOpticsPolicyTests` | 27 / 27 ✅ |
| `SalesBusinessRulesArchitectureTests` | 5 / 5 ✅ |
| `RegisterSaleBusinessRulesTests` | 40 / 40 ✅ |
| `SettleOrderBalanceAtomicityTests` | 7 / 7 ✅ |

---

## 23. Résultat complet

```
dotnet build MMV.sln --no-restore -c Debug
  Build succeeded.
      0 Warning(s)
      0 Error(s)

dotnet test MMV.sln --no-build -c Debug
  Passed!  Failed: 0, Passed: 239, Skipped: 0, Total: 239  - MMV.App.Tests.dll
  Passed!  Failed: 0, Passed: 410, Skipped: 0, Total: 410  - MMV.Application.Tests.dll
  Passed!  Failed: 0, Passed: 481, Skipped: 0, Total: 481  - MMV.Domain.Tests.dll
```

| Contrôle | Baseline | Après P3-7 |
|---|---:|---:|
| Tests Domain | 430 | **481** |
| Tests Application | 362 | **410** |
| Tests App | 239 | **239** (inchangé — aucune UI touchée) |
| **Total** | **1031** | **1130** |
| Échecs | 0 | **0** |
| Ignorés | 0 | **0** |
| Erreurs de build | 0 | **0** |
| Avertissements de build | **1** (hérité) | **0** |
| Vulnérabilités (transitives incluses) | 0 | **0** sur les 7 projets |

---

## 24. Migration

**Aucune migration P3-7.**

Aucune configuration EF, aucune ancienne migration, aucun snapshot EF, aucun type de colonne monétaire et aucune
contrainte `CHECK` n'ont été modifiés.

```
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
  No changes have been made to the model since the last migration.
```

Les colonnes SQLite `REAL`, les `CHECK` monétaires et le backfill historique de `SaleItem.TotalPrice` restent
**reportés** au chantier provider serveur (ADR-PROD-DB-001) ou à une décision séparée. Les nouvelles ventes portent
un `TotalPrice` correct ; **les ventes historiques ne sont pas réécrites**.

---

## 25. Architecture

| Invariant | Vérification |
|---|---|
| `MMV.Application` référence **uniquement** `MMV.Domain` | `dotnet list … reference` ⇒ `..\MMV.Domain\MMV.Domain.csproj` seul ✅ |
| Paquets `MMV.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 (abstractions seules) ✅ |
| Aucun EF dans Domain / Application | tests d'architecture Domain **et** Application ✅ |
| Aucune fuite Avalonia / Infrastructure / App | `ApplicationArchitectureTests` ✅ |
| `SaleService` absent | `SalesBusinessRulesArchitectureTests` ✅ |
| Une seule politique monétaire backend | `SalesBusinessRulesArchitectureTests` ✅ |
| Aucun use case Delete/Update Sale | `ApplicationArchitectureTests` ✅ |
| Politiques Domain **pures** (aucune dépendance de persistance) | `SalesBusinessRulesArchitectureTests` ✅ |
| Aucun fichier UI nécessaire à la règle | `MMV.App.Tests` inchangé à 239, aucun fichier `src/MMV.App/**` modifié ✅ |

Les nouveaux ports sont **provider-neutraux** : les interfaces vivent dans `MMV.Domain`, les `ExecuteUpdateAsync`
exclusivement dans `MMV.Infrastructure`.

---

## 26. Reports explicites

| Sujet | Destination | Statut |
|---|---|---|
| UI de vente (`SaleFormViewModel`, `SaleFormView`, `SalesView`) | Redesign global | **maintenu** |
| `Money` value object, TVA, facturation, devis | Futur / hors P3 | **maintenu** |
| Paiements multiples, échéanciers, entité `Payment` | Hors P3 | **maintenu** |
| Règles générales de notifications | **P3-8** | **maintenu** |
| `StaffId`, utilisateur courant, auteur d'encaissement | **P3-10** | **maintenu** |
| `CHECK` monétaires, conversion `REAL → numeric` | Chantier provider serveur (ADR-PROD-DB-001) | **maintenu** |
| Annulation de vente, statut `Cancelled`, contre-mouvements | Redesign métier futur | **maintenu** |
| Backfill de `SaleItem.TotalPrice` historique | Décision séparée | **maintenu** |
| FK `StockMovement.SaleId` | Redesign métier futur | **maintenu** |
| Frontière d'agrégat (`UseCases/Orders/` écrit sur `Sale`) | Dette / refactoring | **maintenu** |
| Vente comptoir sans `Order` non soldable | Dette | **maintenu** |
| Clé d'idempotence sur `RegisterSale` | Dette | **maintenu** |
| Valeurs mortes `Draft` / `InFabrication` / `Ready` / `Cancelled` de `SaleStatus` | Dette de modèle | **maintenu**, documenté §14 |
| `Sale.EstimatedDelivery` piloté par `IsCounterSale` | Dette | **créé par ce rapport** (§14) |
| Libellé du message « client archivé » partagé ordonnance / vente | Décision produit | **créé par ce rapport** (§9) |
| Avertissement xUnit1031 (D9) | — | **CLOS** (§17 de la consigne, §3.1 et ci-dessous) |

### Correction du warning hérité

- **Warning hérité** du commit P3-6B `53d689e` (présent au SHA validé vert par la CI `29706846895`).
- **Aucune régression produit** : la correction est **test-only**, sur un fichier de test.
- Plus aucune opération bloquante (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) dans le test concerné ;
  **aucun `Thread.Sleep`**, **aucun délai fragile**.
- **Comportement et invariant P3-6B inchangés** : la même prise conditionnelle est exercée, sur le même état, avec
  la même assertion — seule l'attente devient asynchrone (`[Fact] public async Task` + `await`).
- **Résultat** : build final **0 erreur / 0 avertissement**.

---

## 27. État Git final

```
 M src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceResult.cs
 M src/MMV.Application/UseCases/Orders/SettleOrderBalance/SettleOrderBalanceUseCase.cs
 M src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleCommand.cs
 M src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs
 M src/MMV.Domain/Interfaces/Repositories/ICustomerRepository.cs
 M src/MMV.Domain/Interfaces/Repositories/IProductRepository.cs
 M src/MMV.Domain/Interfaces/Repositories/ISaleRepository.cs
 D src/MMV.Domain/Services/SaleService.cs
 M src/MMV.Infrastructure/DependencyInjection.cs
 M src/MMV.Infrastructure/Repositories/CustomerRepository.cs
 M src/MMV.Infrastructure/Repositories/ProductRepository.cs
 M src/MMV.Infrastructure/Repositories/SaleRepository.cs
 M tests/MMV.Application.Tests/Architecture/ApplicationArchitectureTests.cs
 M tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceUseCaseTests.cs
 M tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleUseCaseTests.cs
 M tests/MMV.Application.Tests/UseCases/WorkshopSheets/WorkshopSheetConcurrencyHardeningTests.cs
 D tests/MMV.Domain.Tests/ServiceTests/SaleServiceTests.cs
?? src/MMV.Domain/Services/SaleLineOpticsPolicy.cs
?? src/MMV.Domain/Services/SalePricingPolicy.cs
?? tests/MMV.Application.Tests/UseCases/Orders/SettleOrderBalanceAtomicityTests.cs
?? tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleBusinessRulesTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/SaleLineOpticsPolicyTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/SalePricingPolicyTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/SalesBusinessRulesArchitectureTests.cs
?? docs/implementation/P3-7-sales-business-rules-audit-report.md
(+ docs/implementation/P3-7-sales-business-rules-implementation-report.md, roadmap modifiée)
?? design-handoff/   ?? design/   ?? docs/ui/     (préexistants, hors périmètre, intacts)
```

`git diff --check` : **aucune erreur d'espacement**.

**Aucun commit. Aucun push.**

### Vérification du périmètre

| Interdit | Touché ? |
|---|---|
| `src/MMV.App/**` | ❌ **non** |
| `design-handoff/**`, `design/**`, `docs/ui/**` | ❌ **non** |
| `src/MMV.Infrastructure/Migrations/**` | ❌ **non** |
| `OpticDbContextModelSnapshot.cs` | ❌ **non** |
| Configurations EF de `Sale` / `SaleItem` | ❌ **non** |
| Migration créée | ❌ **aucune** |

---

## 28. Verdict

| Critère de sortie | Statut |
|---|:---:|
| `SaleItem.TotalPrice` est **calculé** | ✅ |
| Aucun montant final falsifiable n'est persisté | ✅ |
| Toutes les lignes sont valides | ✅ |
| Client nul accepté | ✅ |
| Client archivé / introuvable refusé | ✅ |
| Produit introuvable / inactif refusé | ✅ |
| Acquisitions client et produit compatibles multi-poste | ✅ |
| Stock et transaction P3-5 restent intacts | ✅ |
| `PaymentStatus` cohérent | ✅ |
| Règlement du solde atomique et non doublonné | ✅ |
| Aucune nouvelle machine `SaleStatus` | ✅ |
| Tous les tests passent (1134, 0 échec, 0 ignoré) | ✅ |
| Build sans avertissement | ✅ |
| Aucune migration | ✅ |
| Rapport `.md` présent | ✅ |
| Aucune UI modifiée | ✅ |

Le socle transactionnel hérité de P2A et P3-5 — frontière unique, numérotation CAS, décrément conditionnel,
rollback complet — a été **entièrement préservé** ; P3-7 n'a rien fragmenté et s'est inséré à l'intérieur.

Les dix risques critiques de l'audit sont traités : `TotalPrice` calculé (C1), montants recalculés (C2/C3/C4),
client archivé refusé de façon **atomique** (C5), double encaissement fermé (C6), règlement idempotent (C7),
produit introuvable refusé (C8), vente sans client rendue possible (C9), `PaymentStatus` cohérent (C10).

---

## Revue ciblée avant commit

> Section ajoutée lors de la dernière revue strictement ciblée précédant le commit P3-7 (acquisitions
> client/produit + règlement atomique). Aucune revue générale n'a été menée ; aucune migration, aucune UI.

### Ordre réel des acquisitions et tri déterministe ajouté

**Constat.** `AcquireProductsAsync` dédupliquait déjà les `ProductId` (`.Distinct()`) mais les prenait dans
l'ordre de **restitution de cette déduplication**, lui-même dépendant de l'**ordre du panier** (`lines.Select(l =>
l.ProductId)`), jamais trié. Deux ventes contenant les produits A et B dans des ordres de panier opposés
acquéraient donc leurs verrous dans des séquences potentiellement opposées — un facteur d'interblocage inutile sur
un futur provider serveur multi-connexions (ADR-PROD-DB-001).

**Correction appliquée.** `RegisterSaleUseCase.AcquireProductsAsync` trie désormais explicitement les identifiants
distincts par ordre croissant avant toute prise :

```csharp
foreach (var productId in lines.Select(l => l.ProductId).Distinct().OrderBy(id => id))
```

La séquence réelle est donc : acquisition du client (si fourni) → déduplication des `ProductId` → **tri croissant
déterministe** → prise des produits dans cet ordre → chargement des données produit nécessaires. Deux ventes
contenant les mêmes produits en ordres de panier opposés acquièrent désormais **toujours** la même séquence de
verrous.

### Stratégie contre les entités suivies périmées

**Constat.** Les prises atomiques (`ICustomerRepository.TryAcquireActiveAsync`,
`IProductRepository.TryAcquireActiveAsync`) s'exécutent via `ExecuteUpdateAsync`, qui contourne délibérément le
change tracker : aucune entité déjà suivie dans le `DbContext` de la portée n'est mise à jour par cette écriture.
Les lectures effectuées **après** la prise (diagnostic « introuvable vs refusé », et — pour les produits —
chargement de la catégorie réelle décidant du moment du décrément de stock) utilisaient `GetByIdAsync`, hérité de
`BaseRepository` et implémenté via `_dbSet.FindAsync(...)`. `FindAsync` renvoie **sans requêter la base** toute
entité déjà présente dans le cache local du change tracker. Un produit ou un client chargé **avec tracking** plus
tôt dans le **même** `DbContext` (p. ex. un écran catalogue/client resté ouvert dans la même portée applicative)
pouvait donc faire lire une valeur **périmée** au lieu de l'état réel de la base.

- Le diagnostic « introuvable vs archivé/inactif » (`AcquireCustomerAsync`, `AcquireProductsAsync`) ne teste que la
  nullité du résultat, jamais `IsArchived`/`IsActive` : la décision de refus elle-même restait correcte quelle que
  soit la fraîcheur de la lecture (déjà tranchée par la prise atomique).
- La lecture de la **catégorie réelle** du produit après une prise réussie **décide** en revanche du moment du
  décrément de stock (`ProductCategoryEnum.VERRE`/`LENTILLE` exclus du décrément à la vente) : une catégorie
  périmée pouvait faire décrémenter à tort un produit reclassé en verre entre le chargement tracké et
  l'enregistrement de la vente. **Défaut concret confirmé par test** (voir ci-dessous).

**Correction appliquée.** Ajout de `GetByIdFreshAsync` à `ICustomerRepository` et `IProductRepository`
(implémentations `CustomerRepository`/`ProductRepository`, requêtes `AsNoTracking()` explicites) — le plus petit
mécanisme conforme aux conventions du dépôt (le repository produit dispose déjà d'un `GetQueryable()` privé
`AsNoTracking`). `RegisterSaleUseCase` utilise désormais `GetByIdFreshAsync` pour les trois lectures effectuées
après une prise atomique (diagnostic client, diagnostic produit, chargement de la catégorie produit). Aucun
`ChangeTracker.Clear()` global n'a été utilisé : il aurait détaché des entités potentiellement utiles au reste de
la transaction (la vente elle-même, ajoutée peu après).

### Vérification du règlement avec change tracker

Revue de `SettleOrderBalanceUseCase` / `SaleRepository.TrySettleRemainingBalanceAsync` contre les sept invariants
demandés : l'`UPDATE` conditionnel (`WHERE RemainingAmount > 0`) reste l'**unique** écriture du règlement ; la
notification n'est créée **qu'après** un succès (`if (!settled) return …` avant tout accès au dépôt de
notification) ; un résultat à 0 ligne ne crée aucune notification (`AlreadySettled = true`, `HasNotification =
false`) ; `PaymentStatus = Paid`, `DepositAmount = FinalAmount` et `RemainingAmount = 0` sont bien posés par
l'instruction `UPDATE` elle-même, jamais par le change tracker ; aucun `OrderStatus` n'est modifié.

Le réalignement en mémoire de l'entité `fresh.Sale` (`DepositAmount`/`RemainingAmount`/`PaymentStatus`, lignes
151-153) intervient **après** le dernier `SaveChangesAsync` de la méthode : ces valeurs ne sont donc jamais
retransmises à la base par ce `SaveChangesAsync`, et aucun autre n'est appelé avant le `CommitAsync` du
`ITransactionRunner` (qui ne déclenche pas lui-même de `SaveChangesAsync`). Ce patron est **identique** à celui
déjà accepté en P3-6 (`AdvanceOrderStatusUseCase.cs`, réalignement de `fresh.Status` après le dernier
`SaveChangesAsync`, commenté « post-sauvegarde : non re-persisté ») : il ne s'agit donc pas d'un défaut introduit
par P3-7 mais d'une convention de dépôt déjà revue et validée en CI (P3-6, commit `a4d7380`). Aucune modification
n'a été apportée à `SettleOrderBalanceUseCase` — aucun défaut concret n'y a été trouvé.

La couverture existante (`SettleOrderBalanceAtomicityTests`, 7 tests) satisfait déjà le scénario demandé : la vente
(`fresh.Sale`) est chargée **suivie** avant la primitive atomique dans chaque appel, puis vérifiée avec un
**nouveau** `DbContext` après le dernier `SaveChangesAsync`
(`ReglementNominal_SoldeLaVente_EtCreeUneSeuleNotification`) — les trois valeurs persistent, une seule notification
existe, aucune valeur périmée n'a écrasé l'`UPDATE`. Aucun test supplémentaire n'a été ajouté sur ce point : la
preuve existait déjà.

### Tests ajoutés

Deux fichiers, 4 tests (Application, vrai SQLite, jamais InMemory) :

| Fichier | Tests | Objet |
|---|---:|---|
| `RegisterSaleProductAcquisitionOrderTests.cs` | 2 | Ordre déterministe des prises (décorateur d'observation autour d'`IProductRepository`, panier « high puis low » vs « low puis high » ⇒ même séquence triée) ; déduplication (un produit sur plusieurs lignes n'est pris qu'une fois) |
| `RegisterSaleTrackedStaleEntityTests.cs` | 2 | Produit suivi comme `MONTURE`, reclassé `VERRE` en base par un autre contexte avant la prise ⇒ décision de décrément fondée sur la catégorie **réelle** (preuve négative : le test échoue sans `GetByIdFreshAsync`, confirmé par réversion temporaire) ; client suivi comme actif, archivé en base par un autre contexte avant la prise ⇒ vente refusée comme archivée |

### Portée historique exacte de `PaymentStatus`

- `PaymentStatus` est la **source de vérité** pour les ventes **créées ou réglées par le backend P3-7**
  (`SalePricingPolicy.DerivePaymentStatus` à la création ; `PaymentStatus = Paid` posé par l'`UPDATE` conditionnel
  au règlement).
- **Les ventes historiques ne sont pas réécrites.** P3-7 ne réalise **aucun backfill** de `PaymentStatus`.
- Une vente historique créée **avant** P3-7 peut donc **encore porter le défaut EF `Paid`** malgré un reste dû
  réel : ce défaut (audit §22 C10) n'est corrigé que pour le **chemin d'écriture**, pas pour les lignes déjà en
  base.
- **Aucune donnée historique n'est inventée ni corrigée silencieusement** : aucune requête de correction en masse
  n'a été exécutée ou envisagée par P3-7.
- Une éventuelle réparation historique de `PaymentStatus` (comme celle, déjà documentée, de
  `SaleItem.TotalPrice`) **exige une décision séparée**, hors périmètre P3-7.

### Total final exact des tests

```
dotnet test tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj --no-build -c Debug
  Passed!  Failed: 0, Passed: 481, Skipped: 0, Total: 481

dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj --no-build -c Debug
  Passed!  Failed: 0, Passed: 414, Skipped: 0, Total: 414

dotnet test MMV.sln --no-build -c Debug
  Passed!  Failed: 0, Passed: 239, Skipped: 0, Total: 239  - MMV.App.Tests.dll
  Passed!  Failed: 0, Passed: 414, Skipped: 0, Total: 414  - MMV.Application.Tests.dll
  Passed!  Failed: 0, Passed: 481, Skipped: 0, Total: 481  - MMV.Domain.Tests.dll
```

**Total : 1134** (Domain 481 · Application 414 · App 239), soit **+4** par rapport aux 1130 tests de
l'implémentation initiale (les 4 tests de cette revue ciblée). 0 échec, 0 ignoré.

### Absence de migration

```
dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build
  No changes have been made to the model since the last migration.
```

Aucune configuration EF, aucun snapshot, aucune migration modifiés par cette revue. Les deux nouvelles méthodes de
repository (`GetByIdFreshAsync`) sont des requêtes `AsNoTracking()` pures, sans impact sur le modèle.

### Absence d'UI

Aucun fichier sous `src/MMV.App/**` modifié par cette revue. `MMV.App.Tests` reste à 239 tests, inchangé.

---

## Préparation du commit

| Élément | Valeur |
|---|---|
| HEAD de départ | `53d689eb60821f05ab501a4fa38ec822b0fbff3e` |
| CI de départ | run [`29706846895`](https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29706846895), conclusion `success` |
| Résultat du build | `Build succeeded` — **0 erreur / 0 avertissement** |
| Tests Domain | **481** |
| Tests Application | **414** |
| Tests App | **239** |
| **Total** | **1134**, 0 échec, 0 ignoré |
| Vulnérabilités (transitives incluses) | **0** sur les 7 projets |
| État EF | `has-pending-model-changes` = **false** — aucune migration |
| Périmètre prévu du commit | Domain (politiques de vente, ports repositories `GetByIdFreshAsync`, suppression `SaleService`) ; Application (`RegisterSale`, `SettleOrderBalance` inchangé) ; Infrastructure (repositories client/produit/vente, DI) ; Tests (règles Domain, ventes, règlement, architecture, correction xUnit1031, 4 tests de revue ciblée) ; Documentation (audit P3-7, implémentation P3-7, roadmap) |

**P3-7 = GO LOCAL**

> P3-7 est validé **localement**, y compris la revue ciblée ci-dessus. **Sous réserve de la CI du commit** créé à
> partir de cet état exact.

---

# ✅ P3-7 = GO LOCAL

> Validation **locale** uniquement, revue ciblée incluse. Sous réserve de la CI du commit. Aucune UI. **STOP
> avant commit.**
