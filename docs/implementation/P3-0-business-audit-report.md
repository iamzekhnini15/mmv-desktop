# P3-0 — Rapport d'audit métier initial

> Ouverture de la phase **P3 (règles métier)**. Audit du métier existant + production de la roadmap P3.
> **Aucune logique applicative, aucun test et aucun code source modifiés.** Seuls des documents ont été créés.

## 1. Paramètres reçus

| Paramètre | Valeur |
|---|---|
| `TARGET_PHASE_ID` | P3-0 |
| `EXECUTION_MODE` | AUDIT_AND_ROADMAP |
| `SOURCE_BRANCH` | p3-business-rules |
| `ALLOW_CODE_CHANGES` | false |
| `ALLOW_DOC_CHANGES` | true |
| `ALLOW_COMMIT` | false |
| `ALLOW_PUSH` | false |

## 2. État Git initial

- Branche courante : `p3-business-rules` ✅
- Working tree : **propre** au démarrage (`git status --short` vide) ✅
- Merge P2D présent : `1e28f1e merge(P2D): complete query use case cleanup` ✅ (HEAD)
- Branche issue de `main` après merge P2D ✅

**Précondition = GO.**

## 3. Baseline

| Contrôle | Attendu | Résultat |
|---|---|---|
| `dotnet restore MMV.sln` | OK | OK (à jour) ✅ |
| `dotnet build … -c Debug` | vert | **Réussi — 0 avertissement, 0 erreur** ✅ |
| `dotnet test … --no-build` | ≥ 585 | **585 réussis / 0 échec / 0 ignoré** ✅ |
| ` ` → détail | — | App.Tests 192 · Application.Tests 170 · Domain.Tests 223 |
| `dotnet list … --vulnerable --include-transitive` | 0 | **0 vulnérabilité** (7 projets) ✅ |
| `dotnet tool restore` | OK | dotnet-ef 8.0.27 restauré ✅ |
| `dotnet ef migrations has-pending-model-changes` | false | **false** (*No changes … since the last migration*) ✅ |
| Aucune migration ajoutée | — | aucune ✅ |
| `MMV.Application` référence projet | `MMV.Domain` seul | `..\MMV.Domain\MMV.Domain.csproj` seul ✅ |
| `MMV.Application` packages | `DI.Abstractions` seul | `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.1` seul ✅ |

**Baseline = GO.**

## 4. Inventaire métier par domaine

**Entités** (`src/MMV.Domain/Entities`, 19) : `Customer`, `Prescription`, `Product`, `ProductCategory`,
`GlassDetail`, `LensDetail`, `AccessoryDetail`, `GlassPricingTier`, `GlassSupplement`, `Supplement`,
`Supplier`, `StockMovement`, `Order`, `OrderItem`, `Sale`, `SaleItem`, `User`, `Notification`,
`DocumentSequence`.

**Enums** (`src/MMV.Domain/Enums`, 15) : `UserRole`, `ProductCategory`, `OrderStatus`, `SaleStatus`,
`OrderItemType`, `PaymentMethod`, `PaymentStatus`, `StockMovementType`, `LensUsageType`, `LensDuration`,
`LensMaterial`, `LensType`, `GlassMaterial`, `GlassType`, `PrismBase`.

**Use cases Application** : **41** classes `*UseCase` (24 command / écriture, 17 query / lecture) — inventaire
complet dérivé de P2 (les lectures ont été extraites en P2D, cf. [roadmap P2D](../architecture/P2D-read-application-roadmap.md)).

**Validateurs** : **uniquement Domain** — `CustomerValidator`, `PrescriptionValidator`, `ProductValidator`,
`UserValidator`. **`MMV.Application` ne contient aucun validateur** (`rg *Validator* src/MMV.Application` = 0).

| Domaine | Entités | Enums clés | Command use cases | Query use cases | Validation existante |
|---|---|---|---|---|---|
| Clients | `Customer` | — | Create/Update/Delete | ListCustomers(ForPicker), historique via Sales | `CustomerValidator` |
| Ordonnances | `Prescription` | `PrismBase` | Create/Update/Delete | ListPrescriptionsByCustomer | `PrescriptionValidator` (plages seules) |
| Produits | `Product` (+ Glass/Lens/Accessory) | `ProductCategory`, `Glass*`, `Lens*` | Create/Update/Delete | ListProducts, ListForPicker, GetInventoryOverview | `ProductValidator` |
| Stock | `StockMovement` | `StockMovementType` | CreateStockMovement | ListStockMovements | via `IStockMutationService` |
| Commandes | `Order`, `OrderItem` | `OrderStatus`, `OrderItemType` | Create/Update/Delete/AdvanceStatus/SettleBalance | ListOrders, GetOrderDetails | **aucune (transitions libres)** |
| Ventes | `Sale`, `SaleItem` | `SaleStatus`, `Payment*` | RegisterSale | GetSaleFormReferenceData, GetCustomerPurchaseHistory | **aucune (montants non revalidés)** |
| Notifications | `Notification` | — | GenerateLowStock, MarkAllRead | ListNotifications, CountUnread | anti-doublon partiel |
| Fournisseurs | `Supplier` | — | Create/Update/Delete | ListSuppliers, GetSupplierWithProducts | **aucun validateur** |
| Utilisateurs | `User` | `UserRole` | Create/Update/SetActive | ListUsers | `UserValidator` (+ politique MDP statique) |
| Fiche atelier (futur) | *(UI seule)* `FabricationSheetViewModel` | `OrderItemType` | — | GetOrderDetails (réutilisé) | — |
| Transposition (futur) | — | — | — | — | — (inexistant) |

## 5. Règles métier existantes identifiées

- **Vente** (`RegisterSaleUseCase`) : numérotation fiable (`INumberSequenceService`) ; vente comptoir →
  `Delivered` + décrément stock (hors verres) via `IStockMutationService` ; vente fabrication →
  `AwaitingLenses` + création automatique d'une **commande fournisseur** `Order` si verres ; le tout dans
  `ITransactionRunner` (atomique).
- **Stock** (`CreateStockMovementUseCase`) : `Out` → décrément **atomique conditionnel** (jamais négatif,
  `InsufficientStockException`) ; `In` → incrément ; `Adjustment` → valeur absolue. Sous `ITransactionRunner`.
- **Commande** (`AdvanceOrderStatusUseCase`) : change de statut + sortie de stock au passage
  `ToFabricate → InProgress` + notification optionnelle ; `SettleOrderBalanceUseCase` : encaisse le solde
  (`Deposit ← Final`, `Remaining ← 0`) sous transaction.
- **Notifications** (`GenerateLowStockNotificationsUseCase`) : crée une notif « stock bas » par produit sous
  le seuil, avec anti-doublon sur les non-lues.
- **Contrats « introuvable »** homogènes : les use cases chargent l'entité et renvoient `*Found = false` sans
  écrire (Delete/Advance/Settle).

## 6. Validations existantes identifiées

- `CustomerValidator` : `FirstName`/`LastName` requis (≤100) ; email valide si présent (≤200) ; `Phone` ≤20 ;
  `SocialSecurityNumber` ≤30.
- `PrescriptionValidator` : `IssueDate ≤ now+1j` ; par œil, plages **quand la valeur est présente** :
  sphère [−20,20], cylindre [−6,6], axe [0,180], addition [0,4]. **Pas de cohérence croisée.**
- `ProductValidator` : `Name`/`Reference` requis ; `SalePrice`/`PurchasePrice` ≥ 0 ; `StockQuantity`/
  `StockAlertThreshold` ≥ 0. **Pas d'unicité de référence.**
- `UserValidator` : `Username` (3–50, sans espace, `[a-zA-Z0-9._-]`) ; noms requis ; rôle in enum ; +
  helpers statiques `ValidatePasswordPolicy` (8+, maj/min/chiffre), `GetPasswordStrength`, `GenerateStrongPassword`.
- **Absents** : aucun validateur `Supplier` ; aucune validation de **commande** côté Application (les
  préconditions des use cases ne sont pas centralisées).

## 7. Incohérences / risques métier

| # | Sévérité | Constat | Emplacement | Étape cible |
|---|---|---|---|---|
| 1 | **Élevée** | **Suppressions sans garde-fou d'historique** : `DeleteCustomer/Product/Supplier/Order` suppriment via cascade EF. Supprimer un client efface potentiellement ses ventes ; un produit référencé en `SaleItem`/`OrderItem` ; un fournisseur dont `Product.SupplierId` est **non nullable**. Aucun archivage. | `Delete*UseCase.cs` | P3-2/4/9 |
| 2 | **Élevée** | **Transitions de statut libres** : `AdvanceOrderStatusUseCase` accepte n'importe quel `NextStatus` (sauts d'étape / retours arrière possibles, QC contournable). | `AdvanceOrderStatusUseCase.cs` | P3-6 |
| 3 | **Élevée** | **Décrément de stock non sûr dans la fabrication** : `AdvanceOrderStatus` fait `Product.StockQuantity -= item.Quantity` en **direct**, **hors** `IStockMutationService` → peut rendre le stock **négatif**. Viole la règle « jamais décrémenter hors service de mutation ». | `AdvanceOrderStatusUseCase.cs:140` | P3-5 |
| 4 | **Moyenne** | **Vente sans revalidation** : `RegisterSaleUseCase` fait confiance aux montants (`Total/Discount/Final/Deposit/Remaining`) et aux quantités du `Command` ; pas de garde `Deposit ≤ Final`, ni recalcul de `Remaining`, ni `Quantity > 0`. | `RegisterSaleUseCase.cs` | P3-7 |
| 5 | **Moyenne** | **Double machine à états** : `SaleStatus` (Draft/AwaitingLenses/InFabrication/Ready/Delivered/Cancelled) n'est **jamais avancée** après création (seulement Draft/AwaitingLenses/Delivered posés à la création) ; le workflow réel tourne sur `OrderStatus`. `Sale.Status` et `Order.Status` divergent. | `Sale`, `RegisterSale`, `AdvanceOrderStatus` | P3-6/7 |
| 6 | **Moyenne** | **Double rôle d'`Order`** : documenté « commande fournisseur (verres) » mais `OrderStatus` est un workflow **atelier** (ToFabricate/InProgress/QualityCheck/Ready/Delivered) et une **fiche de fabrication** en dérive. `SupplierId` est optionnel et non renseigné par `RegisterSale`. | `Order`, `OrderStatus`, `FabricationSheetViewModel` | P3-6/6B |
| 7 | **Moyenne** | **Cohérence optique croisée absente** : cylindre non nul sans axe requis ; prisme sans base (et inversement) ; axe significatif sans cylindre. Seules les **plages** sont validées. | `PrescriptionValidator.cs` | P3-3 |
| 8 | **Faible** | **N+1 notifications** : `GenerateLowStockNotifications` recharge **toutes** les notifications à chaque produit dans la boucle ; pas de **résolution** quand le stock repasse au-dessus du seuil. | `GenerateLowStockNotificationsUseCase.cs:47` | P3-8 |
| 9 | **Faible** | **Unicité de référence produit** documentée mais non vérifiée (validateur : non-vide seulement). | `ProductValidator.cs` | P3-4 |
| 10 | **Faible** | **Fournisseur sans validateur** : email/téléphone non validés, contrairement à `Customer`. | `Supplier` | P3-9 |
| 11 | **Faible** | **Incohérence d'horloge** : mélange `DateTime.Now` / `DateTime.UtcNow` (entités par défaut `UtcNow`, use cases posent `Now`, fabrication `UtcNow`). Pas d'horloge injectable. | transverse | transverse (hors P3 sauf décision) |
| 12 | **Info** | **Validation Application absente** : socle de validation de commande à poser avant les domaines. | `MMV.Application` | P3-1 |

## 8. Priorités P3 recommandées

1. **P3-1** (socle) — poser la validation Application + convention `Result` (débloque les domaines).
2. **P3-5 / P3-6 / P3-7** — sujets à **risque élevé** (stock non sûr, transitions libres, vente non revalidée)
   à traiter tôt une fois le socle posé.
3. **P3-2 / P3-4 / P3-9** — garde-fous de **suppression / archivage** (risque de perte d'historique).
4. **P3-3** — cohérence optique (prépare aussi la transposition).
5. **P3-6B** — fiche atelier versionnée + QC (dérive de P3-6, alimente P3-7).
6. **P3-8 / P3-10 / P3-11 / P3-12** — fiabilisation, recette, clôture.

L'ordre nominal de la [roadmap](../architecture/P3-business-rules-roadmap.md) reste valable ; ces priorités
signalent les **risques** à ne pas laisser traîner derrière le numéro d'étape.

## 9. Fiche atelier de montage / technicien

**Intégrée explicitement** à la roadmap en **P3-6B** (après Commandes, avant Ventes) et détaillée dans
[les notes atelier & transposition](../domain/P3-workshop-sheet-and-transposition-notes.md).

Constat clé : une fiche de fabrication **existe déjà** en UI éphémère (`FabricationSheetViewModel` +
`FabricationSheetView.axaml`), **non persistée / non versionnée / sans QC / sans transposition / sans mesures
porteur / sans cotes de montage**. P3-6B **formalise** ce concept (entité `WorkshopSheet` versionnée,
`GenerateWorkshopSheetUseCase`, QC bloquant, minimisation des données client, obsolescence si les données
techniques changent). La fiche ne modifie **jamais** l'ordonnance source.

## 10. Transposition sphère-cylindre-axe

**Intégrée explicitement** à la roadmap (§5) et détaillée dans les notes domaine.

- Règle **transverse**, consommée par P3-3 (cohérence) et P3-6B (notation atelier), **pas** une étape autonome.
- Ordonnance originale **toujours conservée** ; transposition = **vue** dérivée, jamais un remplacement.
- Formule : `sphère' = sphère + cylindre` ; `cylindre' = −cylindre` ; `axe' = axe + 90` ; `>180 → −180` ;
  normaliser `0 → 180`. Exemple : `+2.00 (−1.00) axe 180` ⇔ `+1.00 (+1.00) axe 90`.
- Cible : `OpticalPrescriptionTranspositionService` (Domain pur, tests exhaustifs). **Non implémenté en P3-0.**

## 11. Hors périmètre confirmé

SaaS, `Organization`, `Store`, multi-tenant, `Subscription`, `Plan`, `Payment` avancé / en ligne, `Invoice`,
`Quote`, TVA (Belgique/Maroc/France), facturation légale, signature électronique, cloud, email automatique.
**Aucune** de ces zones n'a été ouverte ni modifiée. Aucune valeur fiscale/monétaire (TVA, devise, barème)
n'a été introduite.

## 12. Fichiers modifiés

**Créés (docs uniquement) :**

- `docs/architecture/P3-business-rules-roadmap.md`
- `docs/implementation/P3-0-business-audit-report.md` (ce fichier)
- `docs/domain/P3-workshop-sheet-and-transposition-notes.md`

**Aucun** fichier sous `src/**`, `tests/**`, `migrations/**`, `.github/**` modifié.

## 13. Contrôles exécutés

- `git branch --show-current` / `git status --short` / `git log -10 --oneline`
- `dotnet restore` · `dotnet build -c Debug` · `dotnet test --no-build -c Debug`
- `dotnet list … --vulnerable --include-transitive` · `dotnet tool restore`
- `dotnet ef migrations has-pending-model-changes --project src/MMV.Infrastructure --no-build`
- `dotnet list src/MMV.Application/MMV.Application.csproj reference` / `… package`
- Scan code : entités, enums, use cases (41), validateurs (4 Domain, 0 Application), `RegisterSale`,
  `AdvanceOrderStatus`, `CreateStockMovement`, `SettleOrderBalance`, `Delete*`, `GenerateLowStock`,
  `FabricationSheetViewModel` ; `rg Transposition/Workshop/Fabrication/Technician`.
- Contrôles finaux (§14 ci-dessous) après création des docs.

## 14. Résultats (contrôles finaux)

Voir la section « Contrôles finaux » exécutée après rédaction : working tree = **seuls les 3 docs P3-0
créés** ; `git diff --check` propre ; build vert ; **585 tests** ; 0 vulnérabilité ;
`has-pending-model-changes = false` ; aucune migration ; `MMV.Application` pure ; aucun code source modifié.

## 15. Verdict

**P3-0 = GO local.**

- Audit métier complet par domaine ✅
- Roadmap P3 créée (`P3-business-rules-roadmap.md`) ✅
- Fiche atelier intégrée **explicitement** (P3-6B + notes domaine) ✅
- Transposition intégrée **explicitement** (§5 + notes domaine) ✅
- Aucun code source / test / migration modifié ✅
- Build vert · 585 tests · 0 vulnérabilité · EF vert · `MMV.Application` pure ✅

## 16. Prochaine étape recommandée

**P3-1 — Standardisation des validations / `Result`** : poser le socle de validation **côté Application**
(aujourd'hui absent) et la convention `Result`/exceptions, avec un domaine pilote, **avant** d'attaquer les
domaines métier. Ne **pas** démarrer P3-1 dans ce cycle : P3-0 s'arrête ici (pas de commit, pas de push).
