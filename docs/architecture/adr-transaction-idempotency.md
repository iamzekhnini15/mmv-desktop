# ADR — Frontière transactionnelle, idempotence et erreurs de persistance (P2A-1C)

> **Statut : ACCEPTÉ (portée P2A-1C, solution minimale temporaire).**
> Décision *implémentée* pour cette phase, **volontairement réversible et compatible** avec la future
> couche Application / vertical slice (cf. [migration-roadmap §Étape 1 et §Étape 2](migration-roadmap.md),
> [risk-register R-05/R-23](risk-register.md), [adr-candidates ADR-001](adr-candidates.md#adr-001)).
> **Aucune règle métier nationale, aucun modèle monétaire, aucune concurrence de stock (R-09/ADR-010),
> aucune numérotation fiable (R-03/ADR-006)** n'est traitée ici.

Date : 11 juin 2026. Branche : `phase2a-stabilization`.

---

## 1. Contexte

L'audit Phase 1 a identifié deux constats qui se rejoignent sur les écritures :

- **R-05** (CRITICAL) — la logique d'orchestration des écritures vit dans les ViewModels
  ([`SaleFormViewModel.ExecuteSave`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs)), les services de
  domaine étant contournés.
- **R-23** (HIGH) — [`UnitOfWork.RollbackAsync`](../../src/MMV.Infrastructure/Repositories/UnitOfWork.cs)
  **ne fait pas** de rollback (il se contente de `DisposeAsync` le contexte) et les primitives
  `BeginTransactionAsync`/`CommitAsync` **ne sont pas utilisées** par le flux de vente.

Le flux prioritaire `SaleFormViewModel.ExecuteSave` exécute **deux `SaveChangesAsync` sans transaction** :

1. `await _saleRepository.CreateAsync(sale)` puis `await _unitOfWork.SaveChangesAsync()` — pour obtenir
   le `SaleId` généré ;
2. création éventuelle de la **commande fournisseur** (verres) + **mouvements de stock** (vente comptoir),
   puis un **second** `await _unitOfWork.SaveChangesAsync()`.

Conséquences si le second `SaveChanges` échoue (contrainte, panne, verrou SQLite) :

- **écriture partielle** : la vente est persistée mais la commande / les mouvements de stock ne le sont
  pas ⇒ incohérence vente ↔ commande ↔ stock (cf. menace « corruption d'intégrité » du threat model) ;
- la commande `SaveCommand = new RelayCommand(ExecuteSave)` n'a **aucun `CanExecute`** et `ExecuteSave`
  ne teste pas `IsSaving` à l'entrée ⇒ **double soumission** possible (double vente) ;
- l'erreur technique brute (`DbUpdateException`/`SqliteException`) est affichée telle quelle à
  l'utilisateur.

La roadmap prévoit explicitement, **à l'Étape 1**, « des **transactions à la frontière d'écriture**
(assainir `UnitOfWork`, R-23) — *au niveau primitive de transaction, avant l'existence des use cases* »,
réutilisées **à l'Étape 2** par un use case transactionnel. **P2A-1C construit cette primitive** et
l'applique au pire flux, **sans** bâtir la couche Application ni enfermer durablement la logique métier
dans les ViewModels.

### Périmètre de l'inventaire (flux d'écriture recensés)

| Flux | `SaveChanges` | Transaction | Double-soumission | Décision P2A-1C |
|---|---|---|---|---|
| [`SaleFormViewModel.ExecuteSave`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | **2** | absente | **non gardée** | **Protégé** : transaction + garde `IsSaving` + erreur contrôlée |
| [`OrderFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) | 1 (atomique) | implicite (1 SaveChanges) | `CanSave()` teste `!IsSaving` + `if (!CanSave()) return;` | Inchangé (déjà gardé, mono-écriture) |
| [`StockMovementFormViewModel.SaveAsync`](../../src/MMV.App/ViewModels/StockMovementFormViewModel.cs) | 1 (atomique) | implicite | **non gardée** | **Risque résiduel documenté** (mono-écriture ; garde à ajouter en Étape 2) |
| Autres formulaires (`Customer`, `Supplier`, `Product`, `Prescription`…) | 1 | implicite | gardés (`!IsSaving`) ou `CommitAsync` | Inchangés (mono-écriture) |
| Services de domaine (`SaleService`, `OrderService`…) | 1 chacun | implicite | n/a | Inchangés (non utilisés par `ExecuteSave`) |

> Un `SaveChanges` **unique** est déjà atomique (EF l'enveloppe dans une transaction implicite). Le besoin
> de transaction explicite ne concerne donc que les flux **multi-`SaveChanges`** : aujourd'hui le seul est
> `SaleFormViewModel.ExecuteSave`.

---

## 2. Décision

Introduire une **primitive de transaction réutilisable** (`ITransactionRunner`) et une **erreur de
persistance contrôlée** (`PersistenceException`), puis **protéger le flux prioritaire** `ExecuteSave` :

1. **`MMV.Domain.Interfaces.Persistence.ITransactionRunner`** — abstraction **sans dépendance EF/SQLite** :
   exécute une opération dans **une** transaction, **commit** si succès, **rollback** si exception.
2. **`MMV.Infrastructure.Persistence.EfTransactionRunner`** — implémentation EF/SQLite partageant le
   `OpticDbContext` de la portée DI (même instance que repositories + `UnitOfWork`). Transaction explicite
   `BeginTransactionAsync` → opération → `CommitAsync` ; en cas d'exception : **rollback** explicite (+
   annulation à la libération si non validée) puis transformation des erreurs de persistance.
3. **`MMV.Domain.Exceptions.PersistenceException`** (+ `PersistenceErrorCategory`) — erreur **contrôlée**
   portant un message utilisateur assaini et conservant le détail technique en `InnerException`.
4. **`MMV.Infrastructure.Persistence.PersistenceErrorMapper`** — mappe `DbUpdateException`/`SqliteException`
   (unicité, contrainte, base occupée/verrouillée, base inaccessible) vers une `PersistenceException`
   catégorisée ; **toute autre exception est renvoyée inchangée** (neutralité de comportement).
5. **`SaleFormViewModel`** — `ExecuteSave` :
   - **garde anti double-soumission** : `if (IsSaving) return;` en tête (avant le premier `await`) ;
   - corps multi-écriture déplacé dans `PersistSaleAsync(ct)` exécuté **via** `ITransactionRunner` ;
   - `catch (PersistenceException)` ⇒ affichage du message contrôlé ; les autres exceptions conservent le
     message générique existant.

Le `ITransactionRunner` est injecté via DI (`AddScoped`) et **fileté** jusqu'au ViewModel
(`CustomersViewModel` → `CustomerDetailViewModel` → `SaleFormViewModel`).

**Décision R2 (durcissement) — runner OBLIGATOIRE, aucun repli non transactionnel.** Le flux prioritaire
`SaleFormViewModel.ExecuteSave` **ne possède plus de repli non transactionnel**. `ITransactionRunner` est
un paramètre **requis** (non nullable) de `SaleFormViewModel`, `CustomerDetailViewModel` et
`CustomersViewModel` ; chaque constructeur **rejette `null`** (`ArgumentNullException`). L'absence de
runner est considérée comme une **erreur de configuration** et **ne permet aucune persistance** : il est
**structurellement impossible** de construire un `SaleFormViewModel` capable d'exécuter le flux
multi-écriture sans frontière transactionnelle. (La première version laissait un repli inline non
transactionnel lorsque le runner était absent : ce comportement, qui réintroduisait le risque d'écriture
partielle que cette phase doit éliminer, a été **supprimé** en R2.)

---

## 3. Options comparées (exigées par la phase)

| # | Option | Avantages | Inconvénients | Verdict P2A-1C |
|---|---|---|---|---|
| **1** | **Transactions directement dans les ViewModels** (`BeginTransaction`/`Commit`/`Rollback` au fil d'`ExecuteSave`) | rapide, local | **aggrave R-05** (encore plus de logique d'infra dans la VM) ; non réutilisable ; couple la VM au provider ; difficile à tester | **Rejeté** |
| **2** | **Transactions dans les services Domain existants** (`SaleService`, etc.) | rapproche la logique du domaine | `SaleService` est **contourné** par `ExecuteSave` ; le faire utiliser imposerait un mini-refactor du flux (≈ amorce de couche Application) ; `IUnitOfWork` (Domain) ne doit pas porter de sémantique transactionnelle provider-spécifique | **Rejeté pour P2A-1C** (relève de l'Étape 2) |
| **3** | **Service Infrastructure de transaction (`ITransactionRunner` Domain + `EfTransactionRunner` Infra)** | primitive **réutilisable** ; abstraction neutre réutilisable par la future couche Application ; testable (vrai SQLite) ; **n'enferme pas** la logique métier dans la VM ; assainit R-23 sur le flux protégé | nécessite l'injection d'une dépendance supplémentaire | **RETENU** |
| **4** | **Couche Application / use case transactionnel complet** (vertical slice) | cible architecturale finale (R-05) | **hors périmètre P2A-1C** (interdit) ; dépend de fondations (monétaire, org/magasin, numérotation) ; trop large pour une stabilisation | **Différé (Étape 2)** |

**Justification du choix (option 3)** : c'est la solution **minimale** qui (a) corrige le risque réel
(écriture partielle, R-23) sur le **flux prioritaire**, (b) **ne déplace pas** durablement la logique
métier dans la VM (l'orchestration reste dans `ExecuteSave` mais la **frontière transactionnelle** est
une primitive neutre extractible), et (c) offre **exactement la couture** où l'Étape 2 branchera un use
case `EnregistrerVente` : le futur use case appellera le **même** `ITransactionRunner`, et `ExecuteSave`
délèguera au use case sans changer la primitive.

---

## 4. Propriétés couvertes

### 4.1 Frontière transactionnelle
Une transaction unique par appel `RunAsync`. Le `EfTransactionRunner` partage le `OpticDbContext` de la
portée (repositories + `UnitOfWork` enrôlent automatiquement leurs `SaveChanges` dans la transaction
courante). Les **deux** `SaveChangesAsync` d'`ExecuteSave` sont ainsi **atomiques**.

### 4.2 Rollback
Rollback **explicite** sur toute exception (avec `CancellationToken.None` pour aboutir même si le jeton
d'origine est annulé), **doublé** par l'annulation à la libération (`await using`) si la transaction n'a
pas été validée. Remplace, sur le flux protégé, le `UnitOfWork.RollbackAsync` défaillant (R-23, qui se
contentait de disposer le contexte). Prouvé par test (vrai SQLite) : après une panne au milieu, **0
écriture** ne subsiste.

### 4.3 Idempotence (minimale)
P2A-1C vise l'**atomicité** (tout ou rien), prérequis de l'idempotence. La garde anti double-soumission
empêche la **ré-émission** côté UI. Une **vraie** idempotence de bout en bout (clé d'idempotence,
re-tentative sûre, numérotation déterministe) dépend de la **numérotation fiable** (R-03/ADR-006) et de la
couche Application : **explicitement hors périmètre** et documentée comme suite. Le `SaleNumber` reste
généré par `Random` (R-03) — **inchangé** ; bénéfice collatéral : une éventuelle collision d'unicité est
désormais **atomiquement annulée** et **transformée** en message contrôlé au lieu d'une écriture partielle.

### 4.4 Double soumission
Garde `if (IsSaving) return;` en tête d'`ExecuteSave`, avant le premier `await` : sur le thread UI,
`IsSaving` est positionné de façon synchrone, donc un second déclenchement pendant la sauvegarde est
**rejeté**. (Les flux mono-écriture déjà gardés — `OrderForm`, `Supplier`, `Customer` — sont inchangés.)

### 4.5 Erreurs techniques
`PersistenceErrorMapper` transforme `DbUpdateException`/`SqliteException` en `PersistenceException`
catégorisée (unicité, contrainte, base occupée/verrouillée, inaccessible) avec **message utilisateur
assaini** ; le détail technique reste en `InnerException` (journalisable). Les exceptions **non** liées à
la persistance sont **propagées inchangées** (aucun changement de comportement métier).

### 4.6 Limites SQLite (assumées)
- SQLite **ne supporte pas** les transactions imbriquées : le runner **se rattache** à une transaction
  déjà ouverte au lieu d'en créer une seconde (frontière imbriquée tolérée, testée).
- Le provider SQLite **n'a pas** de stratégie d'exécution avec re-tentative : une transaction manuelle
  est correcte ; la re-tentative/idempotence relèvera d'un futur provider serveur (Étape 2+).
- SQLite **n'a pas** de `rowversion` : aucun **concurrency token** n'est introduit ici (R-09/ADR-010,
  P2A-1D). Le verrou écrivain unique de SQLite limite la concurrence réelle sur poste mono-utilisateur.

### 4.7 Migration future vers la couche Application
`ITransactionRunner` et `PersistenceException` vivent dans **`MMV.Domain`** (sans dépendance EF) : un
futur use case `EnregistrerVente` (Étape 2) les réutilise tels quels. La trajectoire :
1. **(fait, P2A-1C)** primitive de transaction + erreur contrôlée + protection du flux prioritaire ;
2. **(Étape 2)** déplacer le corps `PersistSaleAsync` vers un use case Application appelant le **même**
   runner ; `ExecuteSave` ne fera qu'invoquer le use case ; **supprimer l'accès repo depuis la VM** ;
3. **(Étape 2)** assainir / retirer les primitives transactionnelles inutilisées de `UnitOfWork`
   (`RollbackAsync` trompeur, R-23) une fois tous les flux migrés.

---

## 5. Conséquences

**Positives** : intégrité du flux de vente (plus d'écriture partielle) ; double vente empêchée ; erreurs
de persistance lisibles ; primitive réutilisable et testée ; **aucune** migration EF, **aucune** entité
modifiée, périmètre minimal.

**Négatives / dette assumée** : l'orchestration reste temporairement dans la VM (R-05 non clos — sera
traité Étape 2) ; les primitives transactionnelles défaillantes de `UnitOfWork` restent présentes mais
**non utilisées** sur le flux protégé ; `StockMovementFormViewModel` reste sans garde de double-soumission
(mono-écriture, risque résiduel documenté).

---

## 6. Risques résiduels (hors périmètre, documentés)

1. **R-05 non clos** — logique d'écriture encore dans la VM. *Suite : use case Application (Étape 2).*
2. **R-03 (numérotation `Random`)** — inchangé ; une collision est désormais annulée atomiquement et
   signalée, mais la cause demeure. *Suite : ADR-006 (P2A-1E).*
3. **R-09 (concurrence stock)** — aucun concurrency token introduit. *Suite : ADR-010 (P2A-1D).*
4. **`UnitOfWork.RollbackAsync` trompeur (R-23)** — conservé pour rétro-compatibilité, contourné sur le
   flux protégé. *Suite : assainissement à l'Étape 2.*
5. **`StockMovementFormViewModel` double-soumission** — mono-écriture (atomique) mais non gardé.
   *Suite : garde `IsSaving` à généraliser à l'Étape 2.*
6. **Idempotence complète** — non couverte (dépend de R-03 + couche Application).
