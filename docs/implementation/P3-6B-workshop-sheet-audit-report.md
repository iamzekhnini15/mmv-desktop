# P3-6B — Audit backend : Fiche atelier de montage / technicien

> **Mode** : `AUDIT_AND_WRITE_REPORT_ONLY`. Backend uniquement. Aucun code, test, migration ou UI modifié.
> Aucun commit, aucun push. Ce rapport est le **seul** livrable. Le code réel prime toujours sur les documents cités.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | **P3-6B** (Fiche atelier de montage — backend uniquement, audit) |
| Périmètre | Backend uniquement (Domain / Application / Infrastructure / Tests **en lecture**) ; UI **en lecture seule** |
| HEAD attendu | `a4d7380b374167f4b27809e9b4a7e56b28bf3c48` |
| CI attendue | run `29665945780` |

**Interdits respectés** : aucune modification sous `src/**`, `tests/**`, migrations, UI ; aucun commit ni push.

---

## 2. État Git et CI

```
git branch --show-current   → p3-business-rules
git rev-parse HEAD          → a4d7380b374167f4b27809e9b4a7e56b28bf3c48
git rev-parse origin/…      → a4d7380b374167f4b27809e9b4a7e56b28bf3c48
git status --short          → ?? design-handoff/  ?? design/  ?? docs/ui/
git diff --check            → (aucune sortie)
git log -5 --oneline        → a4d7380 feat(P3-6): enforce order workflow rules
                              3e7a1ab feat(P3-5): secure stock mutations
                              c246ebc feat(P3-4B): enforce product business rules
                              2709bfe docs(P3-4A): audit product business rules
                              d8af704 feat(ui): redesign login screen
```

- Branche = `p3-business-rules` ✅ ; HEAD local **et** distant = `a4d7380…` ✅
- Aucun fichier **suivi** modifié ✅ ; seuls `design-handoff/`, `design/`, `docs/ui/` restent non suivis (autorisés) ✅

CI (`gh run view 29665945780`) :

| Champ | Valeur |
|---|---|
| `databaseId` | 29665945780 |
| `headSha` | `a4d7380b374167f4b27809e9b4a7e56b28bf3c48` |
| `headBranch` | `p3-business-rules` |
| `status` | `completed` |
| `conclusion` | `success` |
| `event` | `push` |

CI = même SHA exact, **completed / success** ✅. **Conditions de démarrage remplies.**

---

## 3. Baseline locale

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet build -c Debug` | 0 erreur / 0 warning | **0 / 0** | ✅ |
| `dotnet test -c Debug` | 869 réussis / 0 échec / 0 ignoré | **869** (Domain 333 + Application 297 + App 239) / 0 / 0 | ✅ |
| `dotnet list package --vulnerable --include-transitive` | 0 vulnérabilité | 0 vulnérabilité (7 projets) | ✅ |
| `ef migrations has-pending-model-changes` | aucune | *No changes … since the last migration* | ✅ |
| `MMV.Application` references | Domain uniquement | `..\MMV.Domain\MMV.Domain.csproj` seul (+ `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1`) | ✅ |

**Baseline conforme.** Le dépôt est propre et reproductible ; l'audit démarre sur cette base.

---

## 4. Documents lus

Lus **intégralement** :

- `docs/architecture/P3-business-rules-roadmap.md` — §P3-6B (objectif fiche atelier, blocs de données, règles
  candidates), §5 (transposition), §4 (points durs : double rôle `Order`, multi-poste).
- `docs/implementation/P3-6-orders-workflow-audit-report.md` et `…-implementation-report.md` — matrice
  `OrderStatusPolicy`, prise atomique `TryTransitionStatusAsync`, suppression limitée à `New`, modification
  bloquée dès `InProgress`, report explicite « fiche atelier persistée / QC bloquant / mesures montage → P3-6B ».
- `docs/implementation/P3-3A-prescription-domain-audit-report.md` et
  `docs/implementation/P3-3B-prescription-validation-and-archived-customer-report.md` — règles croisées
  cylindre⇄axe / prisme⇄base, `OpticalAxisNormalizer` (0→180), suppression de `PrescriptionService`, dette
  d'immutabilité **non résolue**, absence de FK `SaleItem.PrescriptionId`.
- `docs/domain/P3-workshop-sheet-and-transposition-notes.md` — cadrage fiche + formule de transposition.
- `docs/architecture/adr-prod-db-001-multi-poste-database-strategy.md` — V1 production multi-poste, fiche
  atelier **doit être persistée/centralisée/versionnée**, SQLite réseau interdit, pas de `rowversion`.
- `docs/architecture/adr-application-boundaries.md` — un propriétaire de couche par règle (invariants durs →
  Domain, préconditions → Application, ergonomie → UI).
- `docs/architecture/adr-transaction-idempotency.md` — `ITransactionRunner`, `PersistenceException`, SQLite
  sans jeton de concurrence structurel, frontière transactionnelle unique par `RunAsync`.
- `docs/architecture/adr-numbering.md` — `DocumentSequence` + `INumberSequenceService` : incrément atomique
  conditionnel (compare-and-swap), transactionnel, **réutilisable** pour un versionnement.

Le code réel a **primé** partout : chaque affirmation ci-dessous est ancrée sur un chemin de fichier vérifié.

---

## 5. Cartographie de l'existant

| Élément | Chemin exact | Couche | Responsabilité actuelle |
|---|---|---|---|
| `FabricationSheetViewModel` | `src/MMV.App/ViewModels/FabricationSheetViewModel.cs` | **UI** | Vue éphémère construite à la volée depuis un `OrderDetailsDto` ; **aucune persistance** |
| `FabricationSheetView.axaml` | `src/MMV.App/Views/Orders/FabricationSheetView.axaml` | **UI** | Mise en page imprimable ; bouton **« Imprimer »** sans commande liée ; **QC en checkboxes non bindées** |
| `FabricationSheetView.axaml.cs` | `src/MMV.App/Views/Orders/FabricationSheetView.axaml.cs` | **UI** | Code-behind (chargement de vue) |
| `Order` | `src/MMV.Domain/Entities/Order.cs` | Domain | Entité anémique ; `Status` défaut `New` ; `SupplierId`/`ReceivedDate` **inertes** |
| `OrderItem` | `src/MMV.Domain/Entities/OrderItem.cs` | Domain | Ligne + **données de fabrication** (`UsageType`, `Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`, `PrismBase`, `VisualAcuity`) |
| `OrderStatus` | `src/MMV.Domain/Enums/OrderStatus.cs` | Domain | 6 valeurs `New→ToFabricate→InProgress→QualityCheck→Ready→Delivered` |
| `Prescription` | `src/MMV.Domain/Entities/Prescription.cs` | Domain | Ordonnance source OD/OG (nullable) ; **aucun lien FK** depuis `Order`/`OrderItem` |
| `SaleItem` | `src/MMV.Domain/Entities/SaleItem.cs` | Domain | Copie par valeur des données optiques au moment de la vente |
| `Product` | `src/MMV.Domain/Entities/Product.cs` | Domain | Catalogue (référence/désignation/catégorie/détails) |
| `User` / `UserRole` | `src/MMV.Domain/Entities/User.cs`, `Enums/UserRole.cs` | Domain | `UserRole` possède déjà **`Technician`** ; pas de service « utilisateur courant » en Application |
| `OrderStatusPolicy` | `src/MMV.Domain/Services/OrderStatusPolicy.cs` | Domain | Matrice linéaire stricte **unique** (P3-6) |
| `OpticalAxisNormalizer` | `src/MMV.Domain/Optics/OpticalAxisNormalizer.cs` | Domain | Normalisation pure `axe 0 → 180` (P3-3B) ; **seul** service `Optics` existant |
| `PrescriptionValidator` | `src/MMV.Domain/Validators/PrescriptionValidator.cs` | Domain | Règles croisées cyl⇄axe, prisme⇄base (P3-3B) |
| `DocumentSequence` / `INumberSequenceService` | `src/MMV.Domain/Entities/DocumentSequence.cs`, `Interfaces/Persistence/INumberSequenceService.cs` | Domain | Compteur persistant + attribution atomique transactionnelle (`SALE`/`ORDER`) |
| Exceptions métier | `src/MMV.Domain/Exceptions/DomainExceptions.cs` | Domain | `BusinessRuleException`, `InvalidOrderStatusTransitionException`, `OrderStatusConflictException`, `InsufficientStockException` |
| `GetOrderDetailsUseCase` + DTOs | `src/MMV.Application/UseCases/Orders/GetOrderDetails/` | Application | `OrderDetailsDto`/`OrderDetailsItemDto`/`OrderDetailsSaleDto` : source **unique** de la fiche actuelle |
| `AdvanceOrderStatusUseCase` | `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/` | Application | Seul chemin d'avancement ; garde matrice + prise atomique + décrément fabrication ; transaction unique |
| `Create/Update/DeleteOrderUseCase` | `src/MMV.Application/UseCases/Orders/{CreateOrder,UpdateOrder,DeleteOrder}/` | Application | Gardes P3-6 (delete `New` seul ; update bloqué dès `InProgress`) |
| `OrderRepository` | `src/MMV.Infrastructure/Repositories/OrderRepository.cs` | Infrastructure | `TryTransitionStatusAsync` (UPDATE conditionnel atomique, `ExecuteUpdateAsync`) |
| `OrderConfiguration` | `src/MMV.Infrastructure/Data/Configurations/OrderConfiguration.cs` | Infrastructure | FK Sale **Cascade**, OrderItems **Cascade**, index unique `OrderNumber`, index `Status`/`SaleId` |
| `EfNumberSequenceService` | `src/MMV.Infrastructure/Persistence/EfNumberSequenceService.cs` | Infrastructure | Compare-and-swap sur `DocumentSequence` |
| `ITransactionRunner` / `EfTransactionRunner` | `src/MMV.Domain/…/ITransactionRunner.cs`, `src/MMV.Infrastructure/Persistence/EfTransactionRunner.cs` | Domain/Infra | Frontière transactionnelle unique |
| `IStockMutationService` | `src/MMV.Domain/…` + `EfStockMutationService` | Domain/Infra | Décrément atomique jamais négatif |
| `ISessionService` | `src/MMV.App/Services/ISessionService.cs` | **UI** | `CurrentUser` **vit en UI** ; aucune abstraction équivalente en Application |

**Relations réelles pertinentes** : `Order → Sale` (parent, Cascade) ; `Order → OrderItem` (enfants, Cascade,
portent les données optiques) ; **aucun FK** `Order`/`OrderItem` → `Prescription` (copie par valeur) ; aucun FK
`Order` → `User`. `StockMovement` porte un champ `PerformedByUserId` (précédent d'attribution d'utilisateur)
mais **il n'est renseigné par aucun use case** — il n'existe **aucun** fournisseur « utilisateur courant » côté
Application (report P3-5 §18).

---

## 6. La fiche actuelle (comportement réel)

`FabricationSheetViewModel` :

- reçoit un `OrderDetailsDto` via `Initialize(order)` et **n'effectue aucun calcul** : uniquement des accesseurs
  de projection (`FrameItem`, `LensOdItem`, `LensOgItem`, `Accessories`, indicateurs `HasFrame`…) ;
- expose `CustomerName` **et `CustomerPhone`** (`Order.Sale.Customer.Phone`) — donnée personnelle affichée ;
- `TotalAmount` = `Order.Sale.FinalAmount` (montant présent sur la fiche, contraire au principe §P3-6B « pas de
  prix sauf besoin explicite ») ;
- `StatusDisplay` réutilise `OrdersListViewModel.StatusEnumToDisplay` (logique UI).

`FabricationSheetView.axaml` :

- bloc **INFORMATIONS GÉNÉRALES** (date commande, livraison estimée, client + téléphone) ;
- bloc **MONTURE** (`FrameItem.Product.Name/Reference/Category`) ;
- bloc **INSTRUCTIONS / NOTES** (`Notes` de la commande) ;
- bloc **PARAMÈTRES TECHNIQUES DES VERRES** : tableau OD/OG avec **SPH / CYL / AXE / ADD** seulement
  (colonnes `LensOdItem.Sphere/Cylinder/Axis/Addition`) ;
- bloc **CONTRÔLE QUALITÉ ATELIER** : **6 `CheckBox` statiques, sans binding ni persistance** — « Conformité
  verres (Puiss./Axe) », « Centrage et Axage », « Tension des verres », « Ajustage branches/plaquettes »,
  « Nettoyage final », « Validation Responsable » ;
- bloc **VALEUR TOTALE COMMANDE** (montant) ;
- bouton **« Imprimer »** **sans `Command`** (décoratif) — l'impression n'est pas câblée.

**Conclusion** : la fiche est une **vue UI éphémère** ; le QC y est un **décor non fonctionnel** ; ni prisme, ni
usage, ni acuité, ni mesures porteur, ni cotes de montage, ni transposition, ni version n'existent.

---

## 7. Données disponibles

| Donnée | Source actuelle | Persistée où ? | Calculée ? | Historique stable ? |
|---|---|---|---:|---:|
| Numéro de commande | `Order.OrderNumber` | `Orders` (unique) | non | oui |
| Date de commande | `Order.OrderDate` | `Orders` | non | oui |
| Livraison estimée | `Order.EstimatedDelivery` | `Orders` | non | oui (modifiable en `New`/`ToFabricate`) |
| Date de réception | `Order.ReceivedDate` | `Orders` | non | **jamais renseignée** |
| Statut | `Order.Status` | `Orders` | non | évolue (workflow) |
| Client — nom | `Sale.Customer.FirstName/LastName` | `Customers` | non | **mutable** (renommage) |
| Client — **téléphone** | `Sale.Customer.Phone` | `Customers` | non | **mutable** ⚠️ minimisation |
| Monture — désignation/réf/catégorie | `OrderItem.Product.Name/Reference/Category` | `Products` | non | **mutable** (catalogue) |
| Verres OD/OG — produit | `OrderItem.Product` | `Products` | non | **mutable** |
| Sphère / Cylindre / Axe / Addition | `OrderItem.Sphere/Cylinder/Axis/Addition` | `OrderItems` (**copie par valeur**) | non | **stable** (copié à la création) |
| Fournisseur | — | — | — | **absent de la fiche** (`SupplierId` inerte) |
| Prisme / base du prisme | `OrderItem.PrismValue/PrismBase` **existe** | `OrderItems` | non | **stable**, mais **ni dans le DTO fiche ni affiché** |
| Usage (`UsageType`) | `OrderItem.UsageType` **existe** | `OrderItems` | non | **stable**, **ni dans le DTO ni affiché** |
| Acuité visuelle | `OrderItem.VisualAcuity` **existe** | `OrderItems` | non | **stable**, **ni dans le DTO ni affiché** |
| Notes / instructions | `Order.Notes` | `Orders` | non | mutable |
| Mesures porteur (PD, hauteurs…) | — | — | — | **totalement absentes du modèle** |
| Cotes de montage | — | — | — | **totalement absentes du modèle** |
| Décentrement | — | — | — | **absent** (aucun calcul) |
| Contrôle qualité | checkboxes UI | **nulle part** | non | **non persisté** |
| Montant total | `Sale.FinalAmount` | `Sales` | non | mutable |

**Fait structurant** : `OrderDetailsItemDto` ne transporte que `Sphere/Cylinder/Axis/Addition/Product` — **pas**
`UsageType/PrismValue/PrismBase/VisualAcuity`. Ces champs **existent sur `OrderItem`** mais sont **perdus dès la
projection Application** consommée par la fiche. Une fiche atelier fiable devra les lire depuis `OrderItem`.

Classification demandée :

- **Persistée source** : numéro/date/statut/notes commande, montants vente, client (nom/téléphone), produits.
- **Copiée par valeur** (stable) : `OrderItem.{Usage,Sphere,Cylinder,Axis,Addition,PrismValue,PrismBase,VisualAcuity}`.
- **Dérivée** (à recalculer) : notation transposée, décentrement — **aucune n'existe aujourd'hui**.
- **Uniquement UI** : les 6 cases QC, le formatage `StatusDisplay`.
- **Totalement absente** : mesures porteur, cotes de montage, décentrement, version, QC persisté, transposition.

---

## 8. Sens métier de la fiche

Factuellement, la fiche actuelle est un **mélange d'usages** :

- **vue imprimable de commande** (informations générales, monture, montant) ;
- **amorce de bon d'atelier** (paramètres techniques verres OD/OG) ;
- **amorce de fiche QC** (checkboxes décoratives).

Elle n'est **ni** une facture **ni** un devis (aucun statut fiscal). Le montant affiché est un **écart** au cadrage
P3-6B (« pas de prix sauf besoin explicite »).

| Question | Réponse (état réel + cible candidate) |
|---|---|
| Qui la crée ? | Aujourd'hui : n'importe quel écran ouvrant la vue. Cible : un **use case** déclenché depuis la commande (opticien/technicien). |
| Quand ? | Aujourd'hui : à l'affichage. Cible : à la préparation atelier (≥ `ToFabricate`). |
| Quand est-elle stable ? | Aujourd'hui : jamais (recalculée). Cible : au **snapshot** d'une version. |
| Quand devient-elle obsolète ? | Aujourd'hui : sans objet. Cible : si les données techniques de la commande changent (voir §11). |
| Quand est-elle validée ? | Aujourd'hui : jamais (QC non persisté). Cible : QC atelier explicite. |
| Réimprimée plusieurs fois ? | Oui (vue). **Une réimpression ne doit PAS créer de version** (pas de preuve métier). |
| Une modif technique crée-t-elle une version ? | Cible : **oui** — nouvelle version, l'ancienne obsolète. |

**Conclusion** : ne pas transformer une **impression** en événement métier. Le seul événement métier est le
**changement de données techniques** (nouvelle version) et la **validation QC**.

---

## 9. Snapshot ou références vivantes

Pour chaque bloc, décision candidate (à figer en implémentation) :

| Bloc | FK vivante | Snapshot par valeur | Dérivé | Recommandation |
|---|---|---|---|---|
| Identité commande | `OrderId` (FK) | numéro/date **copiés** | — | **FK + snapshot** (numéro figé) |
| Client | `CustomerId` **non** | **nom copié**, minimal | — | **Snapshot minimal** (nom seul) ; **pas** de FK, **pas** de téléphone |
| Monture / produits | `ProductId` (info) | désignation/réf **copiées** | — | **Snapshot par valeur** (le catalogue peut changer/désactiver) |
| Données optiques OD/OG | — | **copiées depuis `OrderItem`** | — | **Snapshot par valeur** depuis `OrderItem` (déjà lui-même une copie de l'ordonnance) |
| Notation transposée | — | — | **dérivée pure** de l'optique snapshot | **Dérivée**, recalculable, éventuellement figée dans le snapshot |
| Mesures porteur / cotes | — | (n'existent pas) | — | reportées (§13) |
| QC | `UserId` de validation (optionnel) | résultat **copié** | — | **Snapshot + éventuel FK auteur** |
| Version / horodatage | — | **propre à la fiche** | — | **propre à la fiche** |

Conséquences d'une modification ultérieure — réponses demandées :

1. **Une fiche ancienne doit-elle changer si le client/produit change ?** **Non.** Une fiche est un document
   d'atelier **historique** : elle doit refléter l'état **au moment de sa génération**. ⇒ **snapshot par valeur**,
   pas de relecture vivante.
2. **Doit-elle survivre à l'archivage d'un client/produit ?** **Oui.** Le snapshot la rend indépendante de
   l'archivage (P3-2 client, P3-4 produit) — cohérent avec les FK `Restrict` posées en P3-2B/P3-4B.
3. **Optique copiée depuis `OrderItem` plutôt que relue depuis `Prescription` ?** **Oui.** `OrderItem` est déjà la
   **copie contractuelle** utilisée pour fabriquer ; l'ordonnance peut être supprimée (dette P3-3, aucune FK). La
   fiche copie donc `OrderItem`, jamais `Prescription`.
4. **Quantité minimale de données client ?** **Nom complet uniquement** (identification de l'ordre d'atelier).
5. **Données personnelles à exclure ?** **Téléphone, email, adresse** — aucun besoin atelier prouvé ⇒ exclus
   (l'actuel `CustomerPhone` de la vue est un excès à ne pas reporter dans la fiche persistée).

**Principe de minimisation appliqué** : la fiche persistée ne copie **que** le nom du porteur.

---

## 10. Versionnement et immutabilité

Aucun pattern de version n'existe sur `Order`/`OrderItem`. Le **seul** mécanisme de compteur atomique du dépôt est
`DocumentSequence` + `INumberSequenceService` (compare-and-swap transactionnel). Le pattern de prise conditionnelle
(`ExecuteUpdateAsync … WHERE …`) de `TryTransitionStatusAsync`/`EfStockMutationService`/`EfNumberSequenceService`
est la **primitive multi-poste** réutilisable pour attribuer une version et basculer la version courante.

Modèle candidat (non implémenté) — entité `WorkshopSheet` :

| Champ | Rôle |
|---|---|
| `WorkshopSheetId` (PK) | identité de la fiche |
| `OrderId` (FK, **Restrict**) | commande d'origine |
| `Version` (int, ≥ 1) | numéro de version **par commande** |
| `IsCurrent` (bool) | **au plus une** version courante par commande |
| `IsObsolete` (bool, dérivable de `IsCurrent`) | version dépassée |
| `CreatedAt` / `CreatedByUserId?` | traçabilité (auteur optionnel, cf. §12) |
| `TechnicalFingerprint` (string) | empreinte des données techniques (détection d'obsolescence, §11) |
| Blocs snapshot | numéro/date commande, nom porteur, monture, verres OD/OG optiques, notation transposée figée |
| Bloc QC | résultat, commentaire, date, auteur (§12) |
| `QcStatus` | `Pending`/`Passed`/`Failed` (enum candidat) |

Réponses précises :

1. **Zéro, une ou plusieurs versions par commande ?** **0..N.** Zéro tant qu'aucune fiche n'est générée (fiches
   historiques sans objet, §14) ; N après régénérations.
2. **Prochain numéro de version atomiquement ?** Deux options : (a) séquence dédiée par commande via un
   `MAX(Version)+1` **sous prise conditionnelle** (`INSERT … WHERE NOT EXISTS(Version)` / retry sur violation
   d'unicité) ; (b) `DocumentSequence` **par commande** (`SequenceName = "WORKSHOP-{OrderId}"`) réutilisant
   `INumberSequenceService` **inchangé**. **Recommandé (a)** : contrainte unique `(OrderId, Version)` + retry sur
   collision — évite de multiplier les lignes de séquence.
3. **Deux postes, même version ?** Empêché par la **contrainte unique `(OrderId, Version)`** : le second `INSERT`
   échoue (violation d'unicité mappée en `PersistenceException`), retry recalcule `MAX+1`.
4. **Contrainte unique `(OrderId, Version)` nécessaire ?** **Oui** — c'est la garde structurelle anti-doublon
   multi-poste (analogue à `idx_orders_order_number_unique`).
5. **Une seule version courante ?** Oui. Garantie candidate : **index unique filtré** `(OrderId) WHERE IsCurrent`
   (index partiel PostgreSQL ; sous SQLite, index unique filtré supporté ⇒ testable) **plus** bascule atomique.
6. **Marquer l'ancienne obsolète sans course ?** Bascule en **une transaction** : `UPDATE … SET IsCurrent=0 WHERE
   OrderId=@id AND IsCurrent=1` (prise conditionnelle) **puis** `INSERT` de la nouvelle `IsCurrent=1` — le tout dans
   `ITransactionRunner`. La condition `WHERE IsCurrent=1` sérialise la bascule.
7. **Une fiche validée peut-elle être éditée ?** **Non.** Une fiche est **immuable** après snapshot ; toute
   évolution = **nouvelle version** (règle §P3-6B.2.3.4).
8. **Nouvelle version : copier la précédente ou reconstruire ?** **Reconstruire depuis la commande** (source de
   vérité `OrderItem`), pas depuis la fiche précédente — évite de propager une donnée technique périmée. Le QC
   **ne se reporte pas** d'une version à l'autre (une nouvelle version repart QC `Pending`).
9. **Version imprimée immuable ?** Toute version snapshotée est immuable **dès sa création** — l'impression n'est
   pas l'événement déclencheur (§8).
10. **Réimpression ⇒ nouvelle version ?** **Non** (pas de preuve métier ; l'impression est une lecture).

---

## 11. Détection d'obsolescence

P3-6 interdit déjà la modification de commande dès `InProgress`. Fenêtre de changement technique réelle :
`New`/`ToFabricate` (via `UpdateOrderUseCase`, reconstruction des lignes). Une fiche générée à `ToFabricate` puis
lignes modifiées ⇒ fiche périmée.

| Stratégie | Fiabilité | Coût | Multi-poste | Migration | Risque |
|---|---:|---:|---:|---:|---|
| Marquage explicite dans `UpdateOrderUseCase` (invalider les fiches courantes de la commande) | Élevée | Faible | Sûr (UPDATE conditionnel, même transaction que l'édition) | Aucune (code) | Oubli si un futur chemin d'écriture contourne le use case |
| Comparaison de « version de commande » (colonne `RowVersion` incrémentée) | Élevée | Moyen | Bon | Migration (colonne) | SQLite sans `rowversion` natif ⇒ jeton applicatif |
| **Empreinte déterministe** des données techniques (`TechnicalFingerprint`) | Élevée | Moyen | Sûr (comparaison de valeur) | Aucune (colonne string) | Doit figer champs/ordre/format/null (voir ci-dessous) |
| Régénérer systématiquement (pas de version stockée) | Faible | Faible | — | — | Perte de l'historique atelier (contraire à ADR multi-poste) |
| Aucune obsolescence automatique | Faible | Nul | — | — | Fiche fausse silencieuse |

**Recommandation** : combiner **marquage explicite** (dans `UpdateOrderUseCase`, qui possède déjà la garde P3-6) et
**empreinte déterministe** de contrôle (à la lecture de la version courante, on recompute l'empreinte depuis la
commande et on la compare à `TechnicalFingerprint` : divergence ⇒ affichage « obsolète »). Le marquage garantit la
cohérence en écriture ; l'empreinte est un filet de sécurité en lecture.

**Spécification obligatoire de l'empreinte** (à ne pas laisser flou) :

- **Champs inclus** (dans cet ordre exact) : pour chaque `OrderItem` **trié par `OrderItemId` croissant** —
  `ItemType`, `ProductId`, `Quantity`, `UsageType`, `Sphere`, `Cylinder`, `Axis`, `Addition`, `PrismValue`,
  `PrismBase`, `VisualAcuity`.
- **Format invariant** : nombres via `InvariantCulture` avec précision fixe (`F2` pour les dioptries, entier pour
  l'axe) ; enums via `ToString()` stable ; séparateur `|` entre champs, `;` entre lignes.
- **Valeurs nulles** : jeton littéral `∅` (jamais chaîne vide, pour distinguer « absent » de « vide »).
- **Persistance** : hachage (`SHA-256` hex) stocké dans `WorkshopSheet.TechnicalFingerprint`.
- **Usage** : calculé au snapshot ; recomparé à la lecture ; **jamais** utilisé comme clé métier ni pour l'unicité.

**Note montant/notes** : le montant et les `Notes` **n'entrent pas** dans l'empreinte technique (une note change
n'invalide pas une fabrication). Seules les données **techniques** de fabrication comptent.

---

## 12. Transposition optique

Audit du code : **aucun** `OpticalPrescriptionTranspositionService`. Le seul service `Optics` est
`OpticalAxisNormalizer` (0→180). Formule cible (notes §5, conservée) :
`sphère' = sphère + cylindre` ; `cylindre' = −cylindre` ; `axe' = axe + 90` ; `si axe' > 180 → −180` ; `0 → 180`.

Réponses :

1. **Types réels** : `Sphere` `double?`, `Cylinder` `double?`, `Axis` `int?`, `Addition` `double?`, `PrismValue`
   `double?`, `PrismBase` enum `PrismBase?`, `VisualAcuity` `string?` (identiques sur `OrderItem`/`SaleItem`/`Prescription`).
2. **Nullable** : **tous** les champs optiques sont nullable.
3. **Cylindre absent** : rien à transposer ⇒ renvoyer la valeur **inchangée** (sphère telle quelle, pas de
   cylindre, pas d'axe). Pas d'invention.
4. **Cylindre = 0** : sémantique « pas d'astigmatisme » (cf. `PrescriptionValidator.HasOrientableCylinder`) ⇒
   **identité** (`sphère' = sphère + 0`, `cylindre' = 0`, pas d'axe).
5. **Axe absent** : légal **seulement** si cylindre nul/zéro (règle P3-3). Si cylindre présent sans axe, la donnée
   est déjà **invalide en amont** (P3-3) : la transposition **ne doit pas** la corriger ; renvoyer une notation
   marquée incomplète (ou lever une erreur de programmation contrôlée), jamais inventer un axe.
6. **Axe 0 après P3-3 ?** Sur `Prescription`, `OpticalAxisNormalizer` canonicalise `0→180` **à l'écriture** des use
   cases prescription. Mais `OrderItem.Axis` est une **copie** ; le chemin **manuel** de commande peut encore poser
   `0` (aucun normaliseur câblé côté `Order`). ⇒ la transposition doit appliquer la **même** normalisation `0→180`
   sur son entrée et sa sortie pour rester cohérente.
7. **Précision / arrondi** : valeurs saisies au **quart de dioptrie** (fractions binaires exactes) ⇒ l'addition
   `sphère + cylindre` et l'opposé `−cylindre` sont **exacts** en `double` ; l'axe est **entier** (`+90 mod 180`
   exact). **Aucun arrondi** requis ; ne pas introduire de tolérance arbitraire.
8. **Préserver `Addition`/`PrismValue`/`PrismBase`/`VisualAcuity` ?** **Oui, inchangés** — la transposition ne
   touche que le trio sphère/cylindre/axe.
9. **Réversible exactement ?** **Oui** : double transposition = identité (dans la précision `.25` / axe entier),
   à tester exhaustivement.
10. **Automatique ou sur demande ?** **Dérivée** : calculée à la génération de la fiche pour la **notation
    atelier**, jamais imposée à la saisie. Peut être figée dans le snapshot (valeur d'affichage).
11. **Conserver les deux notations ?** **Oui** : la fiche affiche la **source** (intangible) **et** la **transposée**
    (labo/technicien), clairement étiquetées.
12. **Prouver que la source n'est jamais modifiée ?** La transposition est une **fonction pure** renvoyant un
    **nouveau** value object ; test : entrées `OrderItem`/valeurs inchangées après appel ; test d'architecture : le
    service `Optics` n'a **aucune** dépendance repo/`UnitOfWork`/EF.

**API Domain candidate (non implémentée)** :

```csharp
namespace MMV.Domain.Optics;

public readonly record struct OpticalCorrection(double? Sphere, double? Cylinder, int? Axis);

public static class OpticalPrescriptionTranspositionService
{
    // Pur, sans état : renvoie la forme transposée équivalente (sphère+cyl / −cyl / axe+90 normalisé).
    // Cylindre nul/absent ⇒ identité. Ne mute jamais l'entrée.
    public static OpticalCorrection Transpose(OpticalCorrection source);
}
```

`Transpose` **ne prend et ne renvoie que** le trio sphère/cylindre/axe : impossible d'atteindre `Addition`/prisme/
acuité, donc impossible de les altérer. **Jamais** appliqué à `Prescription`/`SaleItem`/`OrderItem` en écriture.

---

## 13. Mesures porteur et cotes de montage

Recherche exhaustive : **aucun** champ de mesure porteur ou cote de montage n'existe dans le modèle.

| Mesure | Existe ? | Source | Niveau | Unité | Obligatoire ? |
|---|---:|---|---|---|---:|
| Écart pupillaire mono/bino (PD) | **Non** | — | porteur | mm | — |
| Hauteur de montage | **Non** | — | montage | mm | — |
| Distance verre-œil | **Non** | — | porteur | mm | — |
| Angle pantoscopique | **Non** | — | porteur | ° | — |
| Galbe | **Non** | — | monture | ° | — |
| Largeur / hauteur de monture | **Non** | — | monture | mm | — |
| Pont | **Non** | — | monture | mm | — |
| Diamètre utile / minimal | **Non** | — | montage | mm | — |
| Décentrement | **Non** (aucun calcul) | — | montage | mm | — |
| Sphère/Cylindre/Axe/Addition | **Oui** | `OrderItem` | examen (copié) | dioptrie/° | non |
| Prisme / base | **Oui** | `OrderItem` | examen | Δ / orientation | non |
| Usage / acuité | **Oui** | `OrderItem` | examen | — | non |

**Diagnostic** :

- **Déjà disponibles** : uniquement les **mesures d'examen** (`OrderItem`), et encore, tronquées par le DTO fiche.
- **Uniquement dans l'UI** : rien (les checkboxes QC ne sont pas des mesures).
- **Totalement absentes** : **toutes** les mesures **porteur** et **cotes de montage**, et le décentrement.
- **Nécessaires à une V1 de fiche atelier** : les mesures d'examen (déjà là) + la notation transposée. Les mesures
  porteur/cotes de montage sont **souhaitables** mais **non bloquantes** pour une V1 « snapshot + version + QC ».
- **Reportables** : PD, hauteurs, pantoscopique, galbe, cotes, décentrement calculé.

**Ne pas** transformer P3-6B en logiciel de centrage : introduire ces mesures **sans preuve d'unité/obligation**
serait une invention. Recommandation : V1 = distinguer explicitement les **trois natures** de mesures dans le
modèle (bloc « examen » rempli, blocs « porteur » et « montage » **présents mais vides/nullable**), en documentant
qu'ils sont reportés — plutôt que de les omettre et devoir re-migrer plus tard.

---

## 14. Contrôle qualité

Recherche : **aucun** champ/enum/use case QC persisté. Le seul QC est **décoratif** (6 checkboxes non bindées,
§6). `OrderStatus.QualityCheck` **existe** (statut de workflow) mais n'exprime **aucun** résultat de contrôle.

Questions métier (réponses candidates) :

1. **Oui/non simple ?** Insuffisant : la fiche UI suggère déjà des **points de contrôle**. Cible : un **résultat
   global** (`Passed`/`Failed`) **plus** éventuellement des points, mais V1 minimale = résultat global + commentaire.
2. **Points séparés ?** Souhaitable (montage/finition) mais **reportable** ; V1 = résultat global suffit pour
   bloquer le workflow.
3. **Commentaire obligatoire en cas d'échec ?** **Oui** (règle candidate : `Failed` ⇒ commentaire non vide, garde
   Application/Domain).
4. **Fiche refusée : correction ou nouvelle version ?** Une fiche étant **immuable** (§10.7), un échec QC suivi
   d'une correction technique ⇒ **nouvelle version** (l'ancienne reste `Failed`, historisée).
5. **Qui peut valider ?** `UserRole.Technician` **et** `Optician`/`Admin` (le rôle existe déjà) — décision métier à
   confirmer ; ne **pas** inventer de nouveau rôle.
6. **Identité utilisateur propre en Application ?** **Non.** `CurrentUser` vit dans `ISessionService` (**UI**).
   `StockMovement.PerformedByUserId` existe mais n'est jamais rempli et **aucun** `ICurrentUserService` Application
   n'existe. ⇒ l'auteur QC devra être **passé en paramètre de commande** depuis l'UI (`ValidatedByUserId`), ou le
   champ auteur reste **nullable/reporté** en V1. **Ne pas** introduire de dépendance UI→Application.
7. **Validation annulable ?** V1 : **non** (immuable). Une « ré-ouverture » relèverait d'une nouvelle version.
8. **Atomique et idempotente ?** **Oui** : validation via prise conditionnelle (`UPDATE … WHERE QcStatus=Pending`),
   même transaction ⇒ deux validations concurrentes ⇒ une seule prend.
9. **Deux postes valident simultanément ?** La prise conditionnelle (`WHERE QcStatus=Pending`) sérialise : la
   seconde échoue proprement (résultat déjà pris).
10. **Version obsolète validable ?** **Non** : la validation exige `IsCurrent=1 AND NOT obsolète` (garde combinée
    §11) — valider une version périmée est refusé.

---

## 15. Intégration au workflow Commande (blocage Ready/Delivered)

P3-6 autorise `InProgress→QualityCheck→Ready→Delivered`. Point d'intervention P3-6B : **`AdvanceOrderStatusUseCase`**
(seul chemin d'avancement, déjà porteur de la garde matrice + prise atomique + transaction unique).

Règle cible candidate (à valider métier, non implémentée) :

- **Avant** de prendre la transition `QualityCheck → Ready`, exiger : il **existe** une fiche `IsCurrent`, **non
  obsolète**, avec `QcStatus = Passed`. Sinon ⇒ `BusinessRuleException` (message métier stable), **aucune** écriture.
- `Ready → Delivered` : ne re-vérifie pas le QC (déjà garanti au passage `Ready`), mais peut exiger que la fiche
  courante soit toujours non obsolète (option, à trancher).
- **Aucune** exigence sur `New→ToFabricate` ni `ToFabricate→InProgress` (la fiche n'existe pas encore ; le décrément
  stock P3-5 reste inchangé).

Contraintes de conception (obligatoires) :

- La garde vit en **Application/Domain**, **jamais** en UI (les deux copies UI de `GetNextStatus` ne sont **pas**
  autoritaires — report P3-6).
- La vérification QC s'exécute **avant** la prise atomique de statut, **dans la même transaction** que
  `TryTransitionStatusAsync`, pour éviter une course « QC lu OK » → « transition prise » alors qu'un autre poste
  invalide la fiche entre-temps. `TryTransitionStatusAsync` est **conservé** ; on **ajoute** une lecture-garde QC
  dans le `RunAsync`, pas un second chemin.
- Aucun impact sur le rollback P3-5/P3-6 : la garde lève **avant** tout décrément/mouvement.

**Scénario concurrent** (A valide QC ; B rend la fiche obsolète / crée une v.suivante ; C tente `QualityCheck→Ready`) :

- La **version autoritaire** est **toujours** `IsCurrent = 1`. Si B crée une nouvelle version, l'ancienne passe
  `IsCurrent = 0` **atomiquement** (§10.6) et repart `QcStatus = Pending` (la nouvelle n'hérite pas du QC de A).
- Quand C tente la transition, la garde relit **dans la transaction** la version courante : elle est `Pending` ⇒
  **refus** (`BusinessRuleException`). Le QC de A (sur une version désormais non courante) **ne débloque pas** C.
- ⇒ règle : **seule** la version courante non obsolète et `Passed` autorise `Ready`. Le multi-poste est couvert par
  la combinaison « unique `IsCurrent` » + « prise conditionnelle du statut ».

---

## 16. Compatibilité historique

Inspection : `DbInitializer` (seed démo) crée des commandes/ventes ; les tests créent des commandes à divers
statuts. Après déploiement de la garde QC, des commandes **préexistantes** peuvent être `QualityCheck`, `Ready`,
`Delivered` **sans aucune fiche**.

| Question | Réponse candidate |
|---|---|
| Commande `Ready` sans fiche après déploiement ? | **Aucune régression** : la garde ne s'applique qu'**au moment de la transition** `QualityCheck→Ready`. Une commande déjà `Ready`/`Delivered` n'est jamais re-transitionnée ⇒ pas de blocage rétroactif. |
| Commande `QualityCheck` existante peut-elle avancer ? | Avec garde **stricte**, **non** tant qu'aucune fiche `Passed` n'existe ⇒ **blocage** d'une commande légitime en cours. ⇒ nécessite une **stratégie de compatibilité** (ci-dessous). |
| Générer une fiche initiale à la migration ? | **Possible** (snapshot depuis `OrderItem`) mais **interdit d'inventer un QC `Passed`** : au mieux une fiche `QcStatus = Pending`, ce qui **bloquerait quand même** l'avancement. |
| Appliquer la garde uniquement aux commandes ayant une fiche ? | **Option la plus sûre** : garde QC active **seulement** si la commande possède une fiche courante ; sinon comportement P3-6 inchangé. Évite tout blocage rétroactif. |
| Marqueur de compatibilité ? | Alternative : colonne/flag « workflow QC requis » posé à partir d'une date, ou simplement **absence de fiche = QC non requis** (implicite). |
| Migration créant des snapshots fiables ? | Partiellement : les données `OrderItem` existent, mais **mesures porteur/QC réel n'existent pas** ⇒ tout backfill serait incomplet. |
| Risque de données incomplètes anciennes commandes ? | Élevé (pas de QC historique, pas de mesures) ⇒ **ne pas** fabriquer d'historique QC. |

Classement des options de compatibilité :

- **Sûre** : garde QC active **uniquement si une fiche courante existe** (les commandes historiques sans fiche
  suivent le workflow P3-6 inchangé). **Recommandée.**
- **Acceptable avec dette** : backfill de fiches `Pending` **sans** garde rétroactive (fiches présentes, mais QC non
  exigé pour les commandes antérieures).
- **Dangereuse** : garde QC stricte rétroactive ⇒ **bloque** des commandes légitimes en cours.
- **Impossible sans décision métier** : inventer un `QcStatus = Passed` historique (fausse la réalité qualité).

**Consigne respectée** : aucun backfill n'invente une validation qualité passée.

---

## 17. Persistance

Le périmètre P3-6B (fiche persistée, versionnée, QC) **exige** de nouvelles tables (rien de réutilisable sur
`Order`/`OrderItem`).

| Élément | Table dédiée ? | JSON ? | Colonnes explicites ? | Pourquoi ? |
|---|---:|---:|---:|---|
| Fiche principale + version | **Oui** (`WorkshopSheets`) | Non | Oui | entité versionnée requêtable (unicité, `IsCurrent`, QC) |
| Optique snapshot OD/OG | intégrée à `WorkshopSheets` **ou** table lignes | Non (colonnes) | Oui | peu de champs, requêtables ; JSON nuirait aux contraintes et à la lisibilité (convention dépôt = colonnes typées) |
| Notation transposée | colonnes dérivées figées | Non | Oui | affichage stable ; recalculable |
| QC | colonnes sur `WorkshopSheets` | Non | Oui | résultat + commentaire + date + auteur ; V1 sans points séparés |
| Mesures porteur / cotes | **reportées** | — | — | absentes du modèle, non V1 (§13) |
| Empreinte technique | colonne `TechnicalFingerprint` (string) | Non | Oui | comparaison d'obsolescence |

Convention dépôt : **colonnes typées explicites** (aucun JSON dans le modèle actuel), enums en `HasConversion<string>`,
index nommés (`idx_…`). FK candidates :

| FK | Cible | Nullable | `DeleteBehavior` candidat | Justification |
|---|---|---|---|---|
| `WorkshopSheet.OrderId` | `Order` | Non | **Restrict** | une fiche historique ne doit **pas** être supprimée en cascade avec la commande (cohérent P3-4B/P3-2B) ; d'ailleurs la suppression de commande est déjà limitée à `New` (P3-6), statut où aucune fiche n'existe |
| `WorkshopSheet.CreatedByUserId?` / `ValidatedByUserId?` | `User` | Oui | **Restrict** ou `SetNull` | conserver la fiche même si l'utilisateur est désactivé (P3-10) ; auteur optionnel (§12) |

Une fiche **ne doit jamais** être supprimée silencieusement par cascade (règle explicite).

---

## 18. Migration candidate

**Aucune migration créée dans cet audit.** Stratégie candidate (à exécuter en implémentation) :

- **Nom conceptuel** : `AddWorkshopSheets` (date_préfixe, cf. convention `20260717183027_AddProductNormalizedReferenceAndProtectHistory`).
- **Tables** : `WorkshopSheets` (+ éventuelle `WorkshopSheetLenses` si lignes séparées).
- **Colonnes** : identité, `OrderId`, `Version`, `IsCurrent`, `QcStatus`, QC (commentaire/date/auteur), snapshot
  (numéro/date commande, nom porteur, monture, verres OD/OG source + transposé), `TechnicalFingerprint`,
  `CreatedAt`, `CreatedByUserId?`.
- **Index / contraintes** : **unique `(OrderId, Version)`** ; **unique filtré `(OrderId) WHERE IsCurrent`** ; index
  `OrderId` ; FK `Order` **Restrict**, FK `User` **Restrict/SetNull**.
- **Comportement sur données existantes** : **purement additive** (création de tables) ⇒ **aucune** donnée existante
  touchée ; **aucun** backfill de fiche par défaut (option §16 : ne pas inventer de QC). `has-pending-model-changes`
  doit repasser à **false** après génération.
- **Rollback** : `Down` supprime les tables (aucune donnée métier antérieure perdue, tables neuves).
- **Tests base jetable** : migration up/down sur SQLite jetable ; unicité `(OrderId, Version)` ; unicité `IsCurrent` ;
  FK `Restrict` (suppression de commande porteuse refusée — cohérent avec la garde P3-6 delete `New`).
- **Compatibilité multi-poste** : additive, pas de mutation de schéma existant ⇒ adoption sûre (mécanisme
  `SqliteSchemaVerifier`/`SqliteDatabaseManager` déjà en place pour les tables additives).

---

## 19. Multi-poste et transactions

| Acte | Transaction | Garde atomique | Contrainte DB | Conflit métier attendu |
|---|---|---|---|---|
| Générer la 1ʳᵉ fiche (v1) | `ITransactionRunner` | `INSERT` (retry sur collision) | unique `(OrderId, Version)` | doublon v1 si deux postes ⇒ un seul `INSERT` réussit |
| Générer une nouvelle version | `ITransactionRunner` | `UPDATE … SET IsCurrent=0 WHERE IsCurrent=1` puis `INSERT` IsCurrent=1 | unique `(OrderId, Version)` + unique filtré `IsCurrent` | deux versions courantes ⇒ empêché par unique filtré |
| Marquer l'ancienne obsolète | même transaction que la génération | prise conditionnelle `WHERE IsCurrent=1` | unique filtré `IsCurrent` | course de bascule ⇒ sérialisée |
| Valider QC | `ITransactionRunner` | `UPDATE … WHERE QcStatus=Pending AND IsCurrent=1` | — | double validation ⇒ une seule prend |
| Refuser QC | `ITransactionRunner` | idem (`WHERE QcStatus=Pending`) | — | idem |
| Transition `QualityCheck→Ready` | `ITransactionRunner` (celui de `AdvanceOrderStatusUseCase`) | lecture-garde QC **puis** `TryTransitionStatusAsync` | — | fiche non `Passed`/obsolète ⇒ `BusinessRuleException` avant prise |
| Lecture version courante | (lecture) | — | — | lecture non bloquante ; recompute empreinte pour signaler obsolescence |

Risques multi-poste couverts : **last-write-wins** (évité par prises conditionnelles), **doublon de version**
(unique `(OrderId, Version)`), **deux versions courantes** (unique filtré `IsCurrent`), **validation d'une version
obsolète** (garde `IsCurrent AND non obsolète`), **transition sans QC** (garde dans la transaction).

Séparation demandée :

- **Applicable dès maintenant** (SQLite compris) : transactions `ITransactionRunner`, prises conditionnelles
  `ExecuteUpdateAsync`, contraintes uniques (y compris index filtré, supporté par SQLite).
- **Limite SQLite de test** : écrivain unique ⇒ la « concurrence » réelle est sérialisée ; les tests prouvent la
  **correction** du CAS, pas la charge.
- **Besoin futur PostgreSQL/SQL Server** : index partiel natif, jeton de concurrence (`xmin`/`rowversion`),
  concurrence réelle multi-connexion (ADR-PROD-DB-001). **SQLite réseau reste interdit.**

---

## 20. API Application candidate

Le plus petit ensemble backend (non implémenté) :

| Use case | Command/Query | Préconditions | Transaction | Résultat / exception |
|---|---|---|---|---|
| `GenerateWorkshopSheetUseCase` | Command | commande existe ; statut ≥ `ToFabricate` (à trancher) | `ITransactionRunner` (INSERT + bascule) | fiche v1 ou v(n+1) ; `OrderFound=false` si absente |
| `GetCurrentWorkshopSheetUseCase` | Query | — | lecture | fiche courante (+ flag obsolète recalculé) ou `null` |
| `ListWorkshopSheetVersionsUseCase` | Query | — | lecture | historique des versions (lecture DTO) |
| `ValidateWorkshopSheetQcUseCase` | Command | fiche courante, non obsolète, `Pending` ; `ValidatedByUserId` fourni | `ITransactionRunner` (prise conditionnelle) | `Passed` ; refus si obsolète/non courante/déjà prise |
| `RejectWorkshopSheetQcUseCase` | Command | idem + commentaire non vide | `ITransactionRunner` | `Failed` |
| Garde d'éligibilité `Ready` | **intégrée** à `AdvanceOrderStatusUseCase` | fiche courante `Passed` non obsolète | transaction existante | `BusinessRuleException` sinon |

**Exclus explicitement** (non backend / non P3-6B) : génération PDF, dialogue/rendu d'impression, ouverture de
fichier, mise en page Avalonia, export UI. La **transposition** est une **fonction Domain pure** consommée par
`GenerateWorkshopSheetUseCase` (§12), pas un use case autonome.

---

## 21. Tests existants

| Règle / risque | Test existant | Niveau | Suffisant ? |
|---|---|---|---|
| Fiche de fabrication (génération/persistance) | **aucun** | — | ❌ (UI-only, non testable backend) |
| Transposition optique | **aucun** | — | ❌ (service inexistant) |
| Snapshot / minimisation client | **aucun** | — | ❌ |
| Versionnement / obsolescence | **aucun** | — | ❌ |
| QC persisté / bloquant | **aucun** | — | ❌ (QC = checkboxes UI) |
| Matrice de statut | `OrderStatusPolicyTests` (Domain) | Domain | ✅ (workflow P3-6, hors QC) |
| Transitions légales/illégales + effets stock | `AdvanceOrderStatusUseCaseTests` | SQLite réel | ✅ (P3-6, sans garde QC) |
| Suppression selon statut | `DeleteOrderUseCaseTests` | SQLite réel | ✅ (P3-6) |
| Modification bloquée dès `InProgress` | `UpdateOrderUseCaseTests` | SQLite réel | ✅ (P3-6) |
| Règles croisées optique | `PrescriptionValidatorTests` | Domain | ✅ (P3-3, hors transposition) |
| Lecture détails commande | `GetOrderDetailsUseCaseTests` | Application | ✅ (mais DTO tronqué : prisme/usage/acuité absents) |

Aucun test **P3-6B** n'existe : le concept est entièrement UI-only aujourd'hui.

---

## 22. Trous de couverture (à créer **en implémentation**, pas maintenant)

| Règle / risque | Test manquant |
|---|---|
| Génération depuis commande valide | v1 créée, `IsCurrent=1`, snapshot conforme à `OrderItem` |
| Commande introuvable | `OrderFound=false`, aucune écriture |
| Commande sans ligne | refus ou fiche vide selon décision métier |
| Snapshot des données | modifier ensuite client/produit/`OrderItem` ⇒ fiche **inchangée** |
| Minimisation client | seul le **nom** est copié ; **pas** de téléphone/email |
| Transposition | table exhaustive (signes, `axe+90` mod 180, `0→180`, cylindre nul/absent, axe absent, réversibilité) |
| Source non modifiée | `Transpose` ne mute pas l'entrée ; service sans dépendance repo (test d'architecture) |
| Version 1 / nouvelle version | v1 puis v2 : v1 `IsCurrent=0`, QC de v2 repart `Pending` |
| Génération concurrente | deux `INSERT` v1 ⇒ un seul réussit (unique `(OrderId, Version)`) |
| Unicité `(OrderId, Version)` | violation levée/retry |
| Version courante unique | impossible d'avoir deux `IsCurrent=1` |
| Fiche validée immuable | tentative d'édition d'une version snapshotée refusée |
| Version obsolète non validable | QC refusé si non courante/obsolète |
| QC validé / refusé | `Passed` ; `Failed` exige commentaire |
| Double validation | seconde validation refusée (prise conditionnelle) |
| Transition `Ready` sans QC | `BusinessRuleException`, statut inchangé |
| Transition `Ready` avec QC | succès |
| Transition concurrente vs invalidation QC (§15) | version courante `Pending` ⇒ refus, QC d'une version non courante ne débloque pas |
| Données historiques sans fiche | commande `Ready` préexistante non bloquée (compatibilité §16) |
| Suppression de commande liée à une fiche | refusée (FK `Restrict`) |
| Migration / backfill | up/down sur base jetable ; aucune fiche inventée ; `has-pending-model-changes=false` |

---

## 23. Risques classés

### 🔴 Critique

1. **Modification de la prescription source par la transposition** — *prévention* : la transposition doit être une
   fonction Domain **pure** (§12) ; jamais appliquée à `Prescription`/`SaleItem`/`OrderItem`. Preuve : aucun service
   existant. Propriétaire : **Domain**. Cible : **P3-6B**.
2. **Fiche historique mutable** — sans immutabilité, une fiche imprimée pourrait être réécrite. *Prévention* :
   snapshot immuable + nouvelle version (§10). Propriétaire : **Domain/Application**. Cible : **P3-6B**.
3. **Deux versions courantes en multi-poste** — sans contrainte, deux postes régénèrent ⇒ ambiguïté d'autorité.
   *Prévention* : unique filtré `(OrderId) WHERE IsCurrent` + bascule atomique (§10.6, §19). Cible : **P3-6B**.
4. **Validation QC d'une mauvaise version** — valider une version obsolète débloquerait à tort. *Prévention* : garde
   `IsCurrent AND non obsolète AND Pending` (§12.10, §14.10, §15). Cible : **P3-6B**.
5. **Transition `Ready` sans contrôle requis** — aujourd'hui `QualityCheck→Ready` est libre (QC non backend).
   *Prévention* : garde QC dans `AdvanceOrderStatusUseCase`, dans la transaction (§15). Preuve :
   `AdvanceOrderStatusUseCase.cs` (aucune garde QC). Propriétaire : **Application**. Cible : **P3-6B**.
6. **Migration inventant une validation qualité historique** — *prévention* : aucun backfill de `Passed` (§16).
   Cible : **P3-6B**.
7. **Perte de fiche par cascade** — une FK `Cascade` vers `Order` effacerait l'historique. *Prévention* : FK
   **Restrict** (§17). Preuve : `OrderConfiguration.cs` (OrderItems déjà Cascade — à **ne pas** reproduire pour la
   fiche). Cible : **P3-6B**.

### 🟠 Important

8. **Snapshot incomplet** — le DTO fiche actuel perd `PrismValue/PrismBase/UsageType/VisualAcuity` (§7). Le snapshot
   doit lire `OrderItem`, pas `OrderDetailsItemDto`. Cible : **P3-6B**.
9. **Concurrence de version** — génération simultanée (§19). Contrainte unique + retry. Cible : **P3-6B**.
10. **Mesures mélangées** — examen / porteur / montage doivent rester **distincts** (§13). Ne pas fusionner.
    Cible : **P3-6B** (structure) / report (remplissage).
11. **Données personnelles excessives** — `CustomerPhone` sur la fiche actuelle (§6). Minimisation = nom seul (§9).
    Propriétaire : **Application/Domain**. Cible : **P3-6B**.
12. **Transposition imprécise** — risque si arrondi/tolérance introduits. *Prévention* : exactitude `.25`/axe entier
    (§12.7). Cible : **P3-6B**.
13. **Absence de propriétaire de règle QC** — aujourd'hui QC = UI décorative. Le propriétaire doit être
    **Domain/Application** (§14). Cible : **P3-6B**.

### 🟡 Dette

14. **Affichage / formatage / export** — `StatusDisplay`, mise en page, bouton « Imprimer » non câblé → redesign UI.
15. **Montant sur la fiche** — `TotalAmount` affiché (§6/§8) ; à retirer de la fiche persistée (pas de prix).
16. **Identité technicien non disponible en Application** — `ISessionService` en UI, pas d'`ICurrentUserService`
    (§14.6) ⇒ auteur QC passé en paramètre ou nullable en V1.
17. **Calcul de décentrement / centrage avancé** — non nécessaire V1 (§13).
18. **Mesures porteur / cotes de montage absentes** — structure à prévoir, remplissage reporté.

### 🔵 Reports explicites

- **UI, impression, PDF, mise en page** → **redesign global**.
- **Validations monétaires vente / client archivé en vente** → **P3-7**.
- **Notifications générales** (fiche générée, QC) → **P3-8**.
- **Rôles utilisateurs fins / `ICurrentUserService`** → **P3-10** (ou chantier transverse).
- **Provider serveur, jeton de concurrence natif, index partiel natif** → **chantier production** (ADR-PROD-DB-001).
- **Facturation / devis / prix sur documents** → **hors périmètre P3**.
- **Immutabilité forte de l'ordonnance source, FK `SaleItem/OrderItem.PrescriptionId`** → dette P3-3 **non résolue**,
  hors P3-6B.

---

## 24. Plan candidat

Le plus petit plan backend réaliste (non implémenté).

### Obligatoire P3-6B (sortie officielle : fiche persistée, versionnée, QC, source intacte)

1. **Domain** : entité `WorkshopSheet` versionnée (snapshot minimal + QC + `TechnicalFingerprint`) ; enum `QcStatus`
   (`Pending`/`Passed`/`Failed`) ; `OpticalPrescriptionTranspositionService` **pur** (§12) + `OpticalCorrection`
   value object ; réutilisation d'`OpticalAxisNormalizer` (0→180) ; nouvelle `BusinessRuleException` (QC requis).
2. **Snapshot** : copie par valeur depuis `OrderItem` (optique **complète**, prisme/usage/acuité compris) + nom
   porteur **seul** ; **jamais** de FK vivante pour les données affichées ; empreinte technique déterministe (§11).
3. **Versionnement** : contrainte unique `(OrderId, Version)` + unique filtré `IsCurrent` ; bascule atomique en
   transaction ; nouvelle version **reconstruite** depuis la commande, QC repart `Pending`.
4. **Transposition** : pure, dérivée, deux notations conservées ; source prouvée intacte (tests).
5. **QC** : `Validate`/`Reject` atomiques idempotents ; `Failed` ⇒ commentaire ; auteur passé en paramètre (nullable
   accepté V1) ; validation refusée sur version obsolète/non courante.
6. **Intégration workflow** : garde QC dans `AdvanceOrderStatusUseCase` pour `QualityCheck→Ready`, **dans la
   transaction**, `TryTransitionStatusAsync` conservé ; **compatibilité** : garde active **seulement si** une fiche
   courante existe (pas de blocage rétroactif, §16).
7. **Repositories / EF** : `IWorkshopSheetRepository` + config EF (FK `Restrict`, index) ; prise conditionnelle
   `ExecuteUpdateAsync` pour bascule/validation.
8. **Migration** : `AddWorkshopSheets` additive, aucun backfill de QC, `has-pending-model-changes=false` (§18).
9. **Tests** : couverture §22 (transposition exhaustive, snapshot, versions, unicité, QC, garde `Ready`, concurrence,
   compatibilité, migration jetable).
10. **Frontière** : règle QC/transposition = Domain ; orchestration = Application ; DI = Infrastructure ; **aucune**
    règle en ViewModel ; `MMV.Application` reste pure (Domain seul).

### Reportable (hors backend minimal)

- Mise en page, PDF, impression, export → redesign UI.
- Mesures porteur / cotes de montage / décentrement → structure prévue, remplissage reporté.
- Points de contrôle QC séparés (montage/finition) → V2.
- Rôles fins / `ICurrentUserService` → P3-10.
- Retrait du montant de la fiche → à faire lors du câblage UI (redesign).

---

## 25. Reports explicites (récapitulatif)

| Sujet | Étape |
|---|---|
| UI / impression / PDF / mise en page / bouton « Imprimer » | **redesign global** |
| Validations monétaires vente, client archivé en vente | **P3-7** |
| Notifications (fiche générée, QC) | **P3-8** |
| `ICurrentUserService` / rôles fins / auteur QC obligatoire | **P3-10** / transverse |
| Provider serveur, jeton de concurrence natif, index partiel natif | **chantier production** (ADR-PROD-DB-001) |
| Facturation / devis / prix sur documents | **hors périmètre P3** |
| Immutabilité forte ordonnance, FK `PrescriptionId` | **dette P3-3 non résolue** |
| Mesures porteur / cotes de montage / décentrement | **V2 fiche** (structure prévue, remplissage reporté) |

---

## 26. Validation documentaire

```
git diff --check   → (aucune sortie)
git diff --stat    → (aucun fichier suivi modifié)
git status --short → ?? design-handoff/  ?? design/  ?? docs/ui/
                     ?? docs/implementation/P3-6B-workshop-sheet-audit-report.md
```

Seul nouveau fichier P3-6B = **ce rapport**. Aucun fichier Domain, Application, Infrastructure, test, migration ou
UI modifié. Aucune autre documentation touchée.

---

## Verdict

- Fiche existante **cartographiée** (§5, §6, §7) — vue UI éphémère, QC décoratif, DTO tronqué ✅
- Modèle de **snapshot** défini (copie par valeur depuis `OrderItem`, minimisation client) ✅
- Stratégie de **versionnement multi-poste** précise (unique `(OrderId, Version)` + unique filtré `IsCurrent` +
  bascule atomique) ✅
- **Transposition** cadrée **sans mutation de source** (fonction Domain pure, API candidate) ✅
- Règle **QC** et intégration workflow définies (garde dans `AdvanceOrderStatusUseCase`, dans la transaction) ✅
- **Compatibilité historique** traitée (garde active seulement si fiche courante ; aucun QC inventé) ✅
- **Migration candidate** sûre (additive, sans backfill de QC) ✅
- **Plan d'implémentation** suffisamment précis (obligatoire / reportable) ✅
- Rapport `.md` **présent** ✅ ; **aucun code modifié** ✅

## **P3-6B AUDIT = GO**

Aucun commit. Aucun push. Aucune UI. STOP.

---

## 27. Décisions retenues pour l'implémentation

> Section ajoutée **après** l'audit, à l'ouverture de l'implémentation backend P3-6B. Elle enregistre les points
> où la mise en œuvre **tranche** ou **dépasse** le plan candidat du §24. Détail complet :
> [rapport d'implémentation P3-6B](P3-6B-workshop-sheet-implementation-report.md).

1. **Génération automatique de la première fiche (renforcement du §16).** L'audit proposait « garde QC active
   seulement si une fiche existe ». Pris seul, ce principe permettrait à une commande **neuve** de contourner
   définitivement P3-6B en n'ayant jamais de fiche. La règle retenue ajoute donc la **création automatique**, à
   l'intérieur de la transaction d'`AdvanceOrderStatusUseCase`, à **deux** points : `ToFabricate → InProgress`
   (cas nominal) et `InProgress → QualityCheck` (filet pour une commande déjà `InProgress` au déploiement).
   L'opération est idempotente : jamais de seconde fiche. Conséquence garantie et testée : **toute commande
   créée après P3-6B possède une fiche avant `QualityCheck`**.
2. **Compatibilité historique inchangée sur le fond.** `QualityCheck → Ready` reste autorisé lorsqu'**aucune**
   fiche n'existe (commande déjà en contrôle qualité au déploiement). Aucune fiche rétroactive, **aucun QC
   inventé**. Lorsqu'une fiche existe, elle doit être `Passed` **et** non obsolète.
3. **Snapshot en agrégat avec table de lignes.** Retenu : `WorkshopSheet` (en-tête + QC) **+**
   `WorkshopSheetItem` (une ligne par `OrderItem`), plutôt que deux emplacements fixes OD/OG — afin de préserver
   monture, verres, accessoires, quantités et ordre des lignes réels.
4. **Instructions incluses dans l'empreinte.** `Order.Notes` est affiché comme consigne d'atelier : une
   modification doit rendre la fiche obsolète. En revanche le **statut de commande** et l'**état QC** en sont
   exclus (sinon toute fiche deviendrait obsolète à la transition suivante).
5. **Aucun auteur de QC en V1.** L'identité utilisateur n'est pas disponible proprement côté Application
   (`ISessionService` vit en UI). Le champ est **volontairement absent** plutôt que faux — report explicite vers
   P3-10 ou un chantier transverse.
6. **Génération manuelle restreinte à `InProgress` / `QualityCheck`.** Avant, la fabrication n'a pas commencé ;
   après, le travail d'atelier est terminé. La commande n'est jamais rouverte à l'édition (P3-6 inchangé).
7. **QC refusé ⇒ nouvelle version de reprise.** Une version `Failed` reste historique et immuable ; la reprise
   crée une nouvelle version repartant `Pending`. Le résultat QC n'est **jamais** recopié d'une version à l'autre.
8. **Écart assumé vs §20 — pas de repository dédié.** Les opérations de fiche vivent sur **`IOrderRepository`**
   (déjà enregistré au composition root) et non sur un `IWorkshopSheetRepository`. Motif : les repositories sont
   enregistrés dans `src/MMV.App/App.axaml.cs`, **hors périmètre backend** ; un nouveau contrat n'aurait pas pu
   être résolu en production. La fiche étant un agrégat strictement possédé par la commande (jamais créée, lue ni
   versionnée autrement que par `OrderId`, jamais supprimée), le placement reste cohérent. Un contrat dédié
   pourra être extrait lorsque le redesign touchera la composition root.
9. **Ajout non prévu par l'audit — adoption des bases historiques.** `SqliteSchemaVerifier` et
   `SqliteDatabaseManager` ont dû être étendus pour traiter `WorkshopSheets`/`WorkshopSheetItems` comme des
   tables **additives tolérées** et pour **exécuter** (et non baseliner) `AddWorkshopSheets`. Sans cela,
   l'adoption d'une base antérieure échouait — régression détectée par les tests d'adoption existants. Même
   mécanisme que le précédent `DocumentSequences` (ADR-numbering §4.5).

10. **Durcissement de concurrence appliqué après implémentation (ne réécrit pas cet audit).** Deux invariants
    multi-poste que cet audit avait cadrés en §15 et §19 n'étaient pas réellement garantis par la première
    implémentation : la transition `QualityCheck → Ready` reposait sur une lecture du QC suivie d'une prise de
    statut ne vérifiant que `Order.Status` (§15 supposait à tort que la lecture « dans la transaction »
    suffisait), et la génération de version utilisait `MAX(Version) + 1`, l'option (a) envisagée en §10.2, qui
    laisse deux demandes concurrentes créer une v2 **et** une v3 sans conflit. Les deux sont corrigés — prise
    atomique liée à la fiche autoritaire, et compare-and-swap sur la version attendue — ainsi que le traitement
    des schémas partiels. Preuves, primitives et tests : **§24bis** du rapport d'implémentation.
