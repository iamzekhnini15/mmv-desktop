# ADR — Concurrence de stock et intégrité des quantités (P2A-1D)

> **Statut : ACCEPTÉ (portée P2A-1D, solution minimale temporaire).**
> Décision *implémentée* pour cette phase, **volontairement réversible et compatible** avec la future
> couche Application / vertical slice (cf. [migration-roadmap §Étape 1 et §Étape 2](migration-roadmap.md),
> [risk-register R-09/R-23](risk-register.md), [adr-candidates ADR-010](adr-candidates.md#adr-010)).
> Concrétise, pour le **décrément de stock**, l'orientation **différée** d'[ADR-010](adr-candidates.md#adr-010)
> (« (3) update conditionnel atomique pour le stock »).
> **Aucune règle métier nationale, aucun modèle monétaire, aucune numérotation fiable (R-03/ADR-006),
> aucune organisation/magasin (R-20), aucun stock par magasin** n'est traité ici. **Le module stock
> n'est pas refait** : seules les écritures critiques existantes sont sécurisées.
>
> **Révision R2 (11 juin 2026) — sécurisation de la sortie manuelle de stock.** La première version laissait
> [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs)
> autoriser un stock **négatif** sur une sortie manuelle *après confirmation opérateur*, ce qui contredisait
> le critère « mouvement de stock manuel : stock non négatif ». **R2 supprime cette confirmation** : la
> **sortie manuelle standard** utilise désormais le **même décrément atomique conditionnel** que la vente
> (`IStockMutationService.DecrementStockAsync`), enveloppé dans `ITransactionRunner` (décrément **et**
> mouvement atomiques). Une sortie qui dépasse le stock est **refusée** (`InsufficientStockException`) sans
> aucune écriture. Sections mises à jour : §1.1 (flux #2), §5, §6. Aucune décision de §2/§3 ne change.

Date : 11 juin 2026. Branche : `phase2a-stabilization`.

---

## 1. Contexte

L'audit Phase 1 a classé **R-09 (HIGH, Concurrence)** : *« Aucun concurrency token (SQLite n'a pas de
`rowversion` auto comme SQL Server) ; stock en lecture-modification-écriture »*, avec mitigation prévue
*« concurrency token applicatif + **mise à jour atomique conditionnelle** + gestion
`DbUpdateConcurrencyException` (ADR-010) »*. P2A-1C a posé la **frontière transactionnelle**
([`ITransactionRunner`](../../src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs)) mais a
**explicitement laissé** la concurrence de stock à P2A-1D ([ADR transaction §6.3](adr-transaction-idempotency.md)).

### 1.1 Inventaire des flux de stock (recensement réel du dépôt)

Tous les sites qui **écrivent** `Product.StockQuantity` ont été recensés (`StockQuantity (+=|-=|=)`) :

| # | Fichier · méthode | Opération | Contrôle stock dispo. | Transaction | Stock négatif ? | Concurrence | Erreur utilisateur actuelle | Décision P2A-1D |
|---|---|---|---|---|---|---|---|---|
| 1 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L1001) | **Sortie** (vente comptoir) | **aucun** (`-= qty` brut) | oui (`ITransactionRunner`, P2A-1C) | **OUI** (peut passer < 0) | **lost update** (read-modify-write) | générique | **SÉCURISÉ** : décrément atomique conditionnel |
| 2 | [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) | Entrée / **Sortie** / Ajustement | **OUI** (sortie : décrément atomique conditionnel) | **oui** (`ITransactionRunner`, R2) | **NON** (sortie standard impossible < 0, R2) | sécurisé (sortie) | `InsufficientStockException` (R2) | **SÉCURISÉ (R2)** : sortie standard via décrément atomique ; In = incrément ; Adjustment = correction absolue ≥ 0 |
| 3 | [`InventoryViewModel.ConfirmItemAdjustmentAsync`](../../src/MMV.App/ViewModels/InventoryViewModel.cs#L214) | Ajustement **absolu** | n/a (set absolu compté) | implicite (1 `SaveChanges`) | non (valeur comptée ≥ 0) | read puis set absolu | dialogue + message | **Inchangé** (set absolu, pas un décrément relatif) |
| 4 | [`OrderDetailViewModel.CreateStockMovementsForFabrication`](../../src/MMV.App/ViewModels/OrderDetailViewModel.cs#L409) | Sortie (fabrication) | **aucun** | appelant (`SaveChanges` plus haut) | **OUI** | read-modify-write | générique | **Inchangé** P2A-1D (hors flux vente ; suite Étape 2) ; risque résiduel documenté |
| 5 | [`ProductFormViewModel`](../../src/MMV.App/ViewModels/ProductFormViewModel.cs#L668-L689) | Saisie initiale (création/édition) | n/a | implicite | non | édition catalogue | validation formulaire | **Inchangé** (stock initial, pas un mouvement) |
| — | `DbInitializer` / `DbSeeder` | Seed démo | n/a | seed | non | n/a | n/a | **Inchangé** (données de démarrage) |

**Cible prioritaire de P2A-1D : le flux #1 (décrément de stock à la vente).** C'est le décrément **relatif
non contrôlé** sur le **flux utilisateur critique déjà transactionnel**. **R2** étend la même protection à
la **sortie manuelle standard** (#2) — désormais décrément atomique conditionnel + transaction, plus aucun
stock négatif. Restent volontairement inchangés : les affectations **absolues** (#3 inventaire, #5 saisie
catalogue, ≥ 0) et la sortie **hors périmètre vente** (#4 fabrication, à traiter avec le use case
`EnregistrerVente`, Étape 2).

### 1.2 Menaces concrètes sur le flux #1

```csharp
// AVANT (read-modify-write, non atomique, non borné) :
var product = await _productRepository.GetByIdAsync(item.ProductId.Value, ct); // lecture (peut être obsolète)
product.StockQuantity -= item.Quantity;                                        // calcul en mémoire
await _productRepository.UpdateAsync(product, ct);                             // écriture UPDATE ... SET qty = <valeur>
```

- **Stock négatif** : `qty=1`, vente de 3 ⇒ `StockQuantity = -2` persisté. Aucune garde.
- **Mise à jour perdue (lost update)** : deux ventes concurrentes lisent `qty=1`, calculent chacune
  `1-1=0`, écrivent toutes deux `0` ⇒ **deux** ventes honorées sur **une** unité. L'`UPDATE ... SET qty =
  <valeur absolue>` écrase la décision de l'autre transaction.
- **Lecture obsolète** : l'écart entre la lecture (chargement du formulaire / `GetByIdAsync`) et l'écriture
  n'est pas détecté.

---

## 2. Décision

Sécuriser le **décrément de stock du flux de vente** par un **décrément atomique conditionnel** exécuté
**dans la frontière transactionnelle existante** (`ITransactionRunner`), via une primitive réutilisable
neutre côté domaine. **Aucune migration**, **aucune entité modifiée**, **aucun token de ligne ajouté**.

1. **`MMV.Domain.Interfaces.Persistence.IStockMutationService`** — abstraction **sans dépendance
   EF/SQLite** :
   ```csharp
   Task DecrementStockAsync(long productId, int quantity, CancellationToken ct = default);
   ```
   Contrat : décrémente le stock **si et seulement si** le stock disponible le permet (jamais < 0) ;
   sinon lève une erreur **contrôlée** `InsufficientStockException` **sans** persister de modification.
2. **`MMV.Infrastructure.Persistence.EfStockMutationService`** — implémentation EF Core 8 / SQLite :
   ```csharp
   var rows = await _context.Products
       .Where(p => p.ProductId == productId && p.StockQuantity >= quantity)
       .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity), ct);
   ```
   Traduit en **un seul** `UPDATE Products SET StockQuantity = StockQuantity - @q WHERE ProductId = @id
   AND StockQuantity >= @q`. **`rows == 1`** ⇒ décrément appliqué ; **`rows == 0`** ⇒ stock insuffisant
   *ou* produit introuvable *ou* stock modifié entre-temps ⇒ relecture du stock courant et levée d'une
   `InsufficientStockException` (la transaction englobante est annulée par le runner).
3. **`MMV.Domain.Exceptions.InsufficientStockException`** — erreur **métier contrôlée** (sous-classe de
   `DomainException`) portant `ProductId`, `RequestedQuantity`, `AvailableQuantity` et un **message
   utilisateur assaini**. Propagée **inchangée** par `ITransactionRunner` (ce n'est pas une erreur de
   persistance technique) ⇒ rollback complet de la vente.
4. **`SaleFormViewModel.PersistSaleAsync`** — remplace le couple `StockQuantity -= …` / `UpdateAsync` par
   `await _stockMutationService.DecrementStockAsync(productId, qty, ct)`. Le **mouvement de stock**
   (`StockMovement` d'audit) reste créé. `ExecuteSave` ajoute un `catch (InsufficientStockException)` qui
   affiche le message contrôlé (la vente n'est **pas** persistée : rollback).

Le `IStockMutationService` est injecté par DI (`AddScoped`, **même portée/contexte** que repositories,
`UnitOfWork` et `ITransactionRunner`) et **fileté** jusqu'au ViewModel (`CustomersViewModel` →
`CustomerDetailViewModel` → `SaleFormViewModel`), comme le runner en P2A-1C.

---

## 3. Options comparées (exigées par la phase)

| # | Option | Avantages | Inconvénients | Verdict P2A-1D |
|---|---|---|---|---|
| **1** | **`rowversion` classique (SQL Server)** | détection native au commit | **indisponible en SQLite** (pas de `rowversion`/`timestamp` auto) ; couple au provider serveur | **Rejeté** (incompatible cible actuelle) |
| **2** | **Token applicatif entier `Version` / `ConcurrencyStamp`** (`IsConcurrencyToken`) | portable ; détecte toute écriture concurrente ; auditable | **migration** (nouvelle colonne) + mapping ; gère la concurrence mais **ne borne pas** le stock à ≥ 0 par lui-même ; gestion `DbUpdateConcurrencyException` requise ; surdimensionné pour un compteur | **Différé** (Étape 2, agrégats financiers/documents) |
| **3** | **Update conditionnel SQL atomique** (`UPDATE … SET qty = qty - n WHERE qty >= n`) | **borne le stock à ≥ 0** ET **évite la perte de mise à jour** sans colonne ni migration ; un seul aller-retour ; portable (SQLite/serveur) ; testable sur vrai SQLite | spécifique aux **compteurs** (stock) ; ne couvre pas les agrégats arbitraires | **RETENU** |
| **4** | **Verrou applicatif en mémoire** (`SemaphoreSlim` par produit) | simple, sans base | **ne protège pas** entre processus/instances ; faux sentiment de sûreté ; fuite mémoire (clés produit) ; non durable | **Rejeté** |
| **5** | **Traitement complet plus tard (couche Application)** | cible finale (use case `EnregistrerVente`) | **laisse le risque ouvert** maintenant ; hors périmètre P2A-1D | **Différé (Étape 2)** — réutilisera la primitive |

**Justification du choix (option 3).** C'est la solution **minimale** qui élimine *simultanément* le **stock
négatif** et la **mise à jour perdue** sur le flux critique, **sans migration ni colonne**, en **réutilisant**
la frontière transactionnelle de P2A-1C. Elle est **portable** (le `UPDATE … WHERE qty >= n` fonctionne
identiquement sous un futur SGBD serveur) et offre la **couture** exacte où l'Étape 2 branchera le use case :
le futur `EnregistrerVente` appellera le **même** `IStockMutationService`. Conformément à
[ADR-010](adr-candidates.md#adr-010), un **token de ligne** (option 2) reste réservé aux **agrégats
financiers/documents** et sera réévalué au choix du SGBD SaaS — il n'est **pas** requis pour borner un
compteur de stock.

---

## 4. Propriétés couvertes

### 4.1 Éviter le stock négatif
La clause `WHERE StockQuantity >= quantity` rend le décrément **impossible** s'il devait passer sous zéro :
l'`UPDATE` n'affecte **aucune ligne** (`rows == 0`) et **rien n'est écrit**. Prouvé sur **vrai SQLite** :
décrément exactement égal au stock ⇒ succès (stock = 0) ; décrément supérieur ⇒ refus, **aucune** valeur
négative persistée.

### 4.2 Détecter un stock modifié entre la lecture et l'écriture
L'opération **ne lit pas puis n'écrit pas** une valeur absolue : elle décrémente **relativement** et
**conditionnellement** en une seule instruction atomique. Une modification concurrente (autre vente,
mouvement manuel) intervenue entre l'affichage du formulaire et la validation est **prise en compte
automatiquement** : si le stock restant est devenu insuffisant, le `WHERE` échoue (`rows == 0`) et la vente
est refusée. Il n'y a donc **pas de fenêtre** « lecture obsolète → écriture aveugle ». La **mise à jour
perdue** est éliminée car aucune transaction n'écrase la valeur absolue calculée par une autre.

### 4.3 Mapper les erreurs en message utilisateur
- **Stock insuffisant / modifié entre-temps** ⇒ `InsufficientStockException` (métier, **contrôlée**) :
  message *« Stock insuffisant pour ce produit : N demandé(s), M disponible(s)… »*. Propagée inchangée par
  le runner ⇒ **rollback** de la vente ; `SaleFormViewModel` l'affiche.
- **Base occupée / verrouillée** (`SQLITE_BUSY`/`SQLITE_LOCKED`) ⇒ déjà couvert par P2A-1C :
  `PersistenceErrorMapper` ⇒ `PersistenceException(DatabaseBusy)` (*« base momentanément occupée, veuillez
  réessayer »*).
- **Autre erreur de persistance** ⇒ `PersistenceException` catégorisée (P2A-1C), rollback.

### 4.4 Limites SQLite (assumées)
- **Pas de `rowversion`** : aucun token de ligne natif (R-09) — d'où l'update conditionnel (option 3).
- **Écrivain unique** : SQLite sérialise les écritures (verrou base). La vraie concurrence simultanée est
  donc limitée sur poste mono-fichier ; l'update conditionnel garantit néanmoins la **correction** même
  si les transactions sont sérialisées **ou** entrelacées (le second décrément voit le stock déjà réduit).
- **Pas de stratégie de re-tentative** du provider : une transaction manuelle est correcte ; re-tentative
  et idempotence relèveront d'un futur provider serveur (Étape 2+).
- `ExecuteUpdateAsync` **contourne le change tracker** : le décrément est écrit directement en base dans la
  transaction courante ; l'entité éventuellement suivie n'est plus modifiée en mémoire (plus de double
  décrément), et le `StockMovement` d'audit reste persisté via le `SaveChanges` final.

### 4.5 Migration future vers la couche Application
`IStockMutationService` et `InsufficientStockException` vivent dans **`MMV.Domain`** (sans dépendance EF) :
le futur use case `EnregistrerVente` (Étape 2) les réutilise tels quels. Trajectoire :
1. **(fait, P2A-1D)** primitive de décrément atomique + erreur contrôlée + sécurisation du flux vente ;
2. **(Étape 2)** déplacer `PersistSaleAsync` vers un use case appelant le **même** `ITransactionRunner` et
   le **même** `IStockMutationService` ; supprimer l'accès repo depuis la VM ;
3. **(Étape 2+)** généraliser aux autres sorties (#4 fabrication) et, si un SGBD serveur est choisi,
   réévaluer un **token de ligne** (option 2) pour les agrégats financiers (ADR-010 option 5).

---

## 5. Conséquences

**Positives** : le flux de vente **ne peut plus** rendre le stock négatif ni « perdre » une mise à jour
concurrente ; erreur **contrôlée** et message utilisateur clair ; rollback de la vente sur stock
insuffisant (atomicité P2A-1C préservée) ; primitive **réutilisable** et **testée sur vrai SQLite** ;
**aucune migration EF**, **aucune entité modifiée**, `has-pending-model-changes = false` ; périmètre minimal.

Depuis **R2**, la **sortie manuelle standard** (#2) bénéficie de la même garantie (décrément atomique +
transaction, jamais négative).

**Négatives / dette assumée** : la concurrence n'est traitée que pour les **décréments** (compteur de stock),
pas pour les agrégats financiers (token différé, ADR-010) ; l'orchestration reste temporairement dans la VM
(R-05, Étape 2) ; la sortie #4 (fabrication, hors écran stock) conserve son comportement actuel (risque
résiduel documenté) ; l'**ajustement d'inventaire** (#3) reste une **correction absolue contrôlée** (≥ 0),
distincte d'une sortie standard.

---

## 6. Risques résiduels (hors périmètre, documentés)

1. **R-05 non clos** — logique d'écriture encore dans la VM. *Suite : use case Application (Étape 2).*
2. **Sortie fabrication #4 (`OrderDetailViewModel`)** — décrément non borné hors écran stock / flux vente.
   *Suite : intégrer au use case `EnregistrerVente`/fabrication (Étape 2), réutilisant `IStockMutationService`.*
3. **Ajustement d'inventaire #3 (`InventoryViewModel`)** — correction **absolue** (set ≥ 0), volontairement
   distincte d'une sortie standard ; concurrence non gérée (read→set). *Suite : Étape 2 si nécessaire.*
4. **Agrégats financiers concurrents** — aucun token de ligne (R-09 partiel). *Suite : ADR-010 option 2/5
   au choix du SGBD SaaS.*
5. **Idempotence / numérotation `Random` (R-03)** — inchangées. *Suite : ADR-006 (P2A-1E).*
