# ADR — Numérotation fiable des ventes et commandes (P2A-1E)

> **Statut : ACCEPTÉ (portée P2A-1E, solution minimale et extensible).**
> Décision *implémentée* pour cette phase, **volontairement réduite et compatible** avec la future couche
> Application / vertical slice (cf. [migration-roadmap §Étape 1 et §Étape 4](migration-roadmap.md),
> [risk-register R-03](risk-register.md), [adr-candidates ADR-006](adr-candidates.md#adr-006)).
> Concrétise, pour les numéros de **vente** et de **commande**, l'orientation **différée** d'[ADR-006](adr-candidates.md#adr-006)
> (« (1) table de séquences transactionnelle »).
> **Aucune fiscalité, aucun format légal national (BE/MA), aucune facture, aucun devis, aucun avoir, aucune
> organisation/magasin, aucun exercice fiscal** n'est traité ici. La numérotation reste **non fiscale** :
> les trous (numéros attribués puis abandonnés) sont **autorisés** à ce stade.

Date : 12 juin 2026. Branche : `phase2a-stabilization`.

---

## 1. Contexte

L'audit Phase 1 a classé **R-03 (CRITICAL, Numérotation)** : *« `SaleNumber`/`OrderNumber` via
`new Random().Next(10000)` face à index UNIQUE »* → *« Collision → vente refusée ; audit impossible »*,
mitigation prévue *« service de séquence transactionnel par org/magasin/exercice (ADR-006) »*. P2A-1C a posé
la **frontière transactionnelle** ([`ITransactionRunner`](../../src/MMV.Domain/Interfaces/Persistence/ITransactionRunner.cs))
et P2A-1D le **décrément de stock atomique** ; tous deux ont **explicitement laissé** la numérotation à P2A-1E.

### 1.1 Inventaire des générations de numéro (recensement réel du dépôt)

Recherche : `Random`, `SaleNumber`, `OrderNumber`, `Generate`, `Number`, `Sequence`, `Next`, `Guid`,
`DateTime.Now/UtcNow`. Sites de **génération** de numéro de document :

| # | Fichier · méthode | Type | Stratégie AVANT | Collision | Contrainte unicité | Concurrence | Décision P2A-1E |
|---|---|---|---|---|---|---|---|
| 1 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | `SaleNumber` | `VTE-{yyyy}-{Random.Next(10000):D4}` | **OUI** (anniversaire : ~0,5 % à 100 ventes/an, ↑) | index UNIQUE `idx_sales_sale_number_unique` | aléatoire non bornée | **REMPLACÉ** par séquence `SALE` (dans la transaction) |
| 2 | [`SaleFormViewModel.PersistSaleAsync`](../../src/MMV.App/ViewModels/SaleFormViewModel.cs) | `OrderNumber` (verres) | `CMD-{yyyy}-{Random.Next(10000):D4}` | **OUI** | index UNIQUE `idx_orders_order_number_unique` | aléatoire non bornée | **REMPLACÉ** par séquence `ORDER` (même transaction) |
| 3 | [`OrderFormViewModel.GenerateOrderNumberAsync`](../../src/MMV.App/ViewModels/OrderFormViewModel.cs) | `OrderNumber` | `CMD-{year}-{count(year)+1:D4}` | **OUI** (deux formulaires ouverts ⇒ même `count+1`) | idem | lecture-puis-écriture | **REMPLACÉ** par séquence `ORDER` (à l'ouverture) |
| — | [`DbInitializer`](../../src/MMV.Infrastructure/Data/DbInitializer.cs) | seed démo | `VTE-{yyyy}-{i:D4}` (déterministe) | non (index `i`) | idem | seed mono-thread | **Inchangé** (données de démonstration, hors flux production ; suite P2A-1F) |
| — | [`UserValidator.cs:89`](../../src/MMV.Domain/Validators/UserValidator.cs#L89) | mot de passe | `new Random()` | n/a | n/a | n/a | **Inchangé** (sécurité, R-18 ; `RandomNumberGenerator` plus tard) |

**Cibles P2A-1E : #1, #2, #3.** Le `Random` (#1, #2) garantit des collisions à terme face aux index UNIQUE
(« échec de vente garanti », audit 4.3) ; le `count+1` (#3) est sujet aux collisions concurrentes. La seule
sécurité existante était la **contrainte d'unicité** (qui transforme la collision en *échec* de vente).

---

## 2. Décision

Introduire une **table de séquences transactionnelle** (ADR-006 option 1) et un service neutre qui
**attribue** des numéros par **incrément atomique conditionnel**, exécuté **dans la transaction existante**
(P2A-1C) pour le flux de vente. **Une seule migration additive et non destructive.**

1. **`MMV.Domain.Entities.DocumentSequence`** — compteur persistant : `SequenceName` (PK), `Prefix`,
   `CurrentValue` (départ 0), `UpdatedAt`. Une ligne par type logique (`SALE`, `ORDER`).
2. **`MMV.Domain.Interfaces.Persistence.INumberSequenceService`** — abstraction **sans dépendance EF/SQLite** :
   ```csharp
   Task<string> NextNumberAsync(string sequenceName, CancellationToken ct = default);
   ```
   Contrat : numéro **unique**, séquences **indépendantes**, unicité sous **concurrence**, participation à
   la **transaction courante** (rollback ⇒ numéro non consommé). Clés centralisées dans
   `DocumentSequenceNames` (`SALE`, `ORDER`).
3. **`MMV.Infrastructure.Persistence.EfNumberSequenceService`** — implémentation EF Core 8 / SQLite par
   **compare-and-swap** :
   ```csharp
   // lit V, puis :
   UPDATE DocumentSequences SET CurrentValue = V+1, UpdatedAt = @now
   WHERE SequenceName = @name AND CurrentValue = V   // ExecuteUpdateAsync
   ```
   **1 ligne** ⇒ numéro `V+1` attribué ; **0 ligne** ⇒ un autre appel a incrémenté ⇒ **re-lecture + retry**.
   Format : `{Prefix}-{valeur:D6}` (ex. `VTE-000001`, `CMD-000001`).
4. **`MMV.Domain.Exceptions.NumberSequenceException`** — erreur **contrôlée** (séquence introuvable /
   contention persistante), propagée **inchangée** par `ITransactionRunner` (⇒ rollback de la vente).
5. **ViewModels** : `SaleFormViewModel.PersistSaleAsync` génère `SaleNumber` (séquence `SALE`) **puis**, si
   verres, `OrderNumber` (séquence `ORDER`) — **dans la transaction** ; `OrderFormViewModel` génère
   `OrderNumber` (séquence `ORDER`) à l'ouverture. Le service est **obligatoire** (constructeurs rejettent
   `null`), injecté par DI (`AddScoped`, **même portée/contexte** que le runner) et **fileté** jusqu'aux VM
   (`CustomersViewModel → CustomerDetailViewModel → SaleFormViewModel` ; `OrdersViewModel → OrderFormViewModel`).
6. **Seed** par `HasData` (compteurs `SALE`/`ORDER` à 0) : appliqué **et** par `Database.Migrate()` (InsertData)
   **et** par `Database.EnsureCreated()` (modèle) → les deux chemins de création obtiennent les compteurs.

---

## 3. Options comparées (exigées par la phase)

| # | Option | Avantages | Inconvénients | Verdict P2A-1E |
|---|---|---|---|---|
| **1** | **`Random` actuel** | trivial | **collisions** garanties face à l'index UNIQUE (R-03) ; audit impossible | **Rejeté** (cause du risque) |
| **2** | **GUID** | unicité quasi certaine, sans table | **non lisible**, non séquentiel, inadapté à un numéro de document/audit/facture | **Rejeté** |
| **3** | **Timestamp seul** (`yyyyMMddHHmmssfff`) | simple, croissant | **collisions** si deux ventes dans la même ms ; dépend de l'horloge ; non remis à zéro | **Rejeté** |
| **4** | **Table de séquences (par type de document)** | **portable SQLite/serveur** ; lisible ; auditable ; transactionnel ; remise à zéro/extension faciles | gérer la contention (résolu par CAS + retry) | **RETENU** |
| **5** | **Séquence SGBD native** | performante | **non portable SQLite** ; pas de reset par exercice simple ; couplage provider | **Différé** (futur SGBD serveur) |
| **6** | **Séquence par magasin / pays / exercice** | cible finale (R-20, fiscalité) | dépend d'`Organisation`/`Magasin`/exercice **inexistants** ; hors périmètre | **Différé** (Étape 4) — encodable dans `SequenceName` sans changer le contrat |

**Justification (option 4).** C'est la solution **minimale** qui élimine la collision **et** l'échec de vente,
**portable** (le compteur fonctionne identiquement sous un SGBD serveur), **lisible/auditable**, et qui offre
la **couture** d'extension exacte : un découpage futur par magasin/pays/exercice s'encodera dans la **clé**
`SequenceName` (ex. `SALE-FR-2027`) **sans** changer `INumberSequenceService`. Conforme à l'orientation 1
d'[ADR-006](adr-candidates.md#adr-006).

---

## 4. Propriétés couvertes

### 4.1 Format des numéros
`{Prefix}-{valeur:D6}` : `VTE-000001` (vente), `CMD-000001` (commande). Préfixe **stocké** (extensible).
Le compteur démarre à 0 ⇒ premier numéro émis = `1`. Padding 6 chiffres (au-delà, le numéro s'allonge
naturellement, sans collision). Le format **ne contient pas** d'année ni d'exercice (numérotation non
fiscale à ce stade) ; il **ne collisionne pas** avec le format de seed démo `VTE-{yyyy}-{i}`.

### 4.2 Unicité et concurrence
Le compare-and-swap (`UPDATE … WHERE CurrentValue = V`) garantit qu'**un même numéro ne peut être attribué
deux fois** : deux appels concurrents lisent `V` ; le premier passe `V→V+1` (1 ligne), le second voit `0`
ligne (la valeur a changé) et **retente** avec `V+1`. SQLite **sérialise** les écritures (verrou unique) et
`Microsoft.Data.Sqlite` **re-tente** sur `SQLITE_BUSY` jusqu'au délai de commande — la contention se résout
sans erreur. Prouvé sur **vrai SQLite** : 50 appels successifs et 10 appels **concurrents** ⇒ numéros tous
distincts, compteur final exact.

### 4.3 Rollback
Quand le service partage le `DbContext` de la transaction ouverte par `ITransactionRunner` (flux de vente),
l'`UPDATE` du compteur **participe à cette transaction**. Si la vente échoue (stock insuffisant P2A-1D, autre
erreur), le runner **annule tout** : l'incrément du compteur est **annulé** ⇒ le numéro **n'est pas consommé**
(prouvé : compteur inchangé après rollback, 0 vente persistée). Une transaction **validée** consomme le
numéro (prouvé).

### 4.4 Comportement hors transaction (trou autorisé)
À l'ouverture d'un formulaire de commande (`OrderFormViewModel`), le numéro est attribué **hors** transaction
(auto-commit). Si l'utilisateur **abandonne**, le numéro est **consommé** (trou de numérotation). C'est le
**comportement retenu** pour une numérotation **non fiscale** ; il est **prouvé** et documenté. Une
numérotation à continuité légale stricte (facture) relèvera de la fiscalité (gate `G-FACT-MA`, mentions BE),
hors P2A-1E.

### 4.5 Compatibilité des bases existantes (migration sûre)
`AddDocumentSequences` est **additive** (création de table + seed), **non destructive**. Les bases clientes
**antérieures à P2A-1E** ne contiennent pas la table :
- le **portail de compatibilité** ([`SqliteSchemaVerifier`](../../src/MMV.Infrastructure/Data/SqliteSchemaVerifier.cs))
  tolère l'**absence** de `DocumentSequences` (table **additive** créée par une migration en attente) — toute
  autre table manquante (cœur du modèle) reste **bloquante** ;
- l'**adoption** ([`SqliteDatabaseManager`](../../src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs))
  **exécute** réellement `AddDocumentSequences` (au lieu de la baseliner à tort) quand la table est absente —
  généralisation du mécanisme R-19 (FixDateTimeDefaultValues) — puis **vérifie physiquement** que la table
  existe (échec explicite sinon : pas de *baseline mensonger*). Prouvé sur vrai SQLite (base post-R19/pré-P2A-1E).

### 4.6 Migration future vers devis/facture/avoir/multi-magasin
`INumberSequenceService` et `DocumentSequence` vivent dans **`MMV.Domain`** (sans EF). Trajectoire :
1. **(fait, P2A-1E)** séquences `SALE`/`ORDER`, attribution transactionnelle, format simple ;
2. **(Étape 2)** déplacer la génération dans les use cases (`EnregistrerVente`), réutilisant le **même** service ;
3. **(Étape 4+)** ajouter des séquences `QUOTE`/`INVOICE`/`CREDIT_NOTE` et un découpage
   **magasin/pays/exercice** encodé dans `SequenceName` ; format légal et **continuité** validés par les gates
   réglementaires (`G-FACT-MA`, mentions BE). Aucune rupture du contrat actuel.

### 4.7 Limites SQLite (assumées)
- **Pas de séquence native** ⇒ table + CAS (option 4 plutôt que 5).
- **Écrivain unique** : la concurrence réelle est sérialisée sur poste mono-fichier ; le CAS reste **correct**
  que les transactions soient sérialisées ou entrelacées.
- `ExecuteUpdateAsync` **contourne le change tracker** : l'écriture est directe en base dans la transaction
  courante, sans entité suivie obsolète.

---

## 5. Conséquences

**Positives** : plus de **collision** ni d'**échec de vente** dû à la numérotation ; numéros **lisibles,
séquentiels, auditables** ; unicité **prouvée** sous concurrence ; **rollback** = numéro non consommé ;
primitive **réutilisable** et **testée sur vrai SQLite** ; migration **additive non destructive** ; bases
clientes existantes **adoptées sans perte** ; `has-pending-model-changes = false` ; périmètre minimal.

**Négatives / dette assumée** : numérotation **non fiscale** (trous autorisés hors transaction) — la
continuité légale (facture) est différée ; orchestration encore dans la VM (R-05, Étape 2) ; pas de découpage
magasin/pays/exercice (Étape 4) ; le seed de **démo** (`DbInitializer`) conserve son format propre (hors flux
production, suite P2A-1F).

---

## 6. Risques résiduels (hors périmètre, documentés)

1. **Continuité légale (facture/avoir)** — non garantie (trous autorisés). *Suite : fiscalité + gates `G-FACT-MA`/BE (Étape 8).*
2. **Découpage magasin/pays/exercice** — absent (org/magasin inexistants, R-20). *Suite : Étape 4, via `SequenceName`.*
3. **R-05 non clos** — génération encore déclenchée par la VM. *Suite : use case Application (Étape 2).*
4. **Seed de démo `DbInitializer`** — format `VTE-{yyyy}-{i}` distinct (sans collision avec la séquence). *Suite : P2A-1F (environnements/seeds).*
5. **`Random` mot de passe (R-18)** — inchangé (hors numérotation). *Suite : `RandomNumberGenerator`.*
