# P3-11 — Implémentation des scénarios métier de recette bout-en-bout

> **Mode : IMPLÉMENTATION DES TESTS DE RECETTE ET RÉDACTION DE RAPPORT.**
> **Premier cycle** : tests et documentation uniquement, aucun fichier `src/**`. **Second cycle**, après
> arbitrage explicite de périmètre (§20) : un fichier Domain créé et deux use cases Application modifiés pour
> corriger le défaut de classification `ItemType` ⇄ `Category` découvert par la recette. Dans les deux cycles :
> aucune migration, aucune UI, aucun package, aucun nouveau projet.

---

## 1. Paramètres

| Paramètre | Valeur |
|---|---|
| Dépôt | `iamzekhnini15/mmv-desktop` |
| Branche | `p3-business-rules` |
| Phase | P3-11 |
| Périmètre | Tests de recette backend et documentation uniquement |
| HEAD attendu | `8d4bf49da1c14ea1d6664eb94878c025b5420656` |
| Run CI attendu | `29938082120` |
| Tests baseline attendus | 1413 |
| Date | 22 juillet 2026 |

---

## 2. État Git et CI

### 2.1 Git

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `8d4bf49d…` | `8d4bf49da1c14ea1d6664eb94878c025b5420656` | ✅ |
| `git rev-parse origin/p3-business-rules` | idem | `8d4bf49da1c14ea1d6664eb94878c025b5420656` | ✅ |
| `git status --short` | aucun fichier suivi modifié | `?? design-handoff/`, `?? design/`, `?? docs/ui/`, `?? docs/implementation/P3-11-…-audit-report.md` | ✅ |
| `git diff --check` | vide | vide | ✅ |
| `git diff --stat` | vide | vide | ✅ |

HEAD local = HEAD distant. Le rapport d'audit P3-11 est bien le seul nouveau fichier P3-11 avant ce travail.

### 2.2 CI

`gh run view 29938082120` :

| Champ | Valeur |
|---|---|
| `databaseId` | `29938082120` |
| `headSha` | `8d4bf49da1c14ea1d6664eb94878c025b5420656` |
| `headBranch` | `p3-business-rules` |
| `event` | `push` |
| `status` | `completed` |
| `conclusion` | `success` |

**Portes Git et CI : ✅ franchies.**

---

## 3. Baseline locale

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `dotnet restore MMV.sln` | succès | « All projects are up-to-date for restore. » | ✅ |
| `dotnet build --no-restore -c Debug` | 0 erreur / 0 avertissement | `Build succeeded. 0 Warning(s) 0 Error(s)` | ✅ |
| `MMV.Domain.Tests` | 593 | 593 passés, 0 échec, 0 ignoré | ✅ |
| `MMV.Application.Tests` | 581 | 581 passés, 0 échec, 0 ignoré | ✅ |
| `MMV.App.Tests` | 239 | 239 passés, 0 échec, 0 ignoré | ✅ |
| **Total** | **1413** | **1413** | ✅ |
| `dotnet list package --vulnerable --include-transitive` | aucune | aucune vulnérabilité sur les 7 projets | ✅ |
| `dotnet ef migrations has-pending-model-changes` | aucune | « No changes have been made to the model since the last migration. » | ✅ |
| Références `MMV.Application` | Domain uniquement | `..\MMV.Domain\MMV.Domain.csproj` | ✅ |
| Paquets `MMV.Application` | — | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 | ✅ |

**Baseline : ✅ conforme.**

---

## 4. Décisions finales

| Décision | Valeur retenue |
|---|---|
| Emplacement | `tests/MMV.Application.Tests/Acceptance/` — aucun nouveau `.csproj` |
| Base | Un fichier SQLite **par test**, répertoire temporaire par instance, `Pooling=False` |
| Clés étrangères | **Activées** (le parcours par `RegisterSaleUseCase` ne produit jamais d'`Order` orpheline) |
| Schéma | `Database.Migrate()` — jamais `EnsureCreated`, jamais EF InMemory |
| Initialisation | Seed **Production sûr** (`DatabaseSeeder` + `new SeedOptions()`), jamais `DbInitializer`, jamais un seed Demo |
| DI | **Aucun conteneur.** Instanciation directe des dépôts et primitives autour d'un `OpticDbContext` partagé |
| Mutations | **Uniquement par use cases publics** ; aucune mutation métier directe par `DbContext` ou dépôt |
| Assertions | Contexte **neuf**, `AsNoTracking()`, aucun montant recalculé, aucun timestamp comparé exactement |
| Isolation | Une base par test ; aucune `IClassFixture` partagée ; aucun `[Collection]` |
| Assertion | FluentAssertions **6.12.0** — version inchangée (v7+ sous licence commerciale) |
| Scénarios | 9 (S1 à S9), S9 traité comme **gate d'intégrité** et non comme test de caractérisation |

### 4.1 Périmètre interdit

**Premier cycle** (avant l'arbitrage de périmètre, jusqu'au gate S9 rouge) :

- aucune modification `src/**` ;
- aucune modification d'un use case, du Domain, de l'Infrastructure, de l'UI ;
- aucune migration, aucun package, aucun nouveau projet de tests ;
- aucune correction de l'incohérence `ItemType` / `Category` ;
- arrêt avant S2 à S8, verdict `P3-11 = NO-GO LOCAL`.

**Second cycle** (après arbitrage — §20), respecté :

- correctif **limité** à un fichier Domain (`SaleLineStockFlowPolicy`) et deux use cases Application
  (`RegisterSaleUseCase`, `AdvanceOrderStatusUseCase`) ;
- **aucune** modification d'Infrastructure, de migration, de snapshot EF, d'UI, de package ou de nouveau
  projet ;
- **aucune** correction de la dette P3-8 ;
- P3-12 non commencé.

---

## 5. Corrections apportées à l'audit

### 5.1 « `Migrate()` seul n'insère aucune donnée » — **faux**

L'audit P3-11 affirme en §21.2 Q4 et §26 que `Migrate()` seul n'insère aucune donnée de démonstration et que
les tables sont vides après migration. **Le code contredit cette affirmation.**

La migration `20260127184542_InitialCreate` contient un `InsertData` explicite
(`src/MMV.Infrastructure/Migrations/20260127184542_InitialCreate.cs:324`) :

```csharp
migrationBuilder.InsertData(
    table: "Users",
    columns: new[] { "UserId", "CreatedAt", "FirstName", "IsActive", "LastLogin", "LastName", "PasswordHash", "Role", "Username" },
    values: new object[] { 1L, …, "Administrateur", true, null, "Système",
        "$2a$11$dXJ3SW6G7P50eS6xFJwFHeJ/hbtjiZlyCloO/sURR8EZ4/nqXJcOy", "Admin", "admin" });
```

Toute base migrée porte donc, dès `Migrate()`, un compte `admin` **actif** au hash faible connu — celui que
`DatabaseSeeder.KnownDefaultAdminPasswordHashes` répertorie précisément comme compte par défaut à neutraliser.

**Vérité observée et retenue :**

- les tables **métier** du parcours (`Customers`, `Suppliers`, `Products`, `Prescriptions`, `Sales`, `Orders`,
  `WorkshopSheets`, `StockMovements`, `Notifications`) sont bien **vides** après `Migrate()` ;
- la table `Users` ne l'est **pas** : elle contient le compte administrateur seedé par `InitialCreate` ;
- le socle de recette exécute donc, après `Migrate()`, le chemin **Production sûr** réellement appelé au
  démarrage desktop — `new DatabaseSeeder().Seed(context, new SeedOptions())`. Le défaut de `SeedOptions` est
  `Environment = Production`, `EnableDemoSeed = false`, `BootstrapAdminPassword = null` : aucun secret
  bootstrap n'est fourni, et le compte faible est **désactivé** (`IsActive = false`) ;
- `DbInitializer` et tout seed Demo/Development ne sont **jamais** appelés ;
- aucune variable d'environnement n'est modifiée : les options Production sont construites directement, comme
  le font déjà les tests existants.

L'audit est corrigé en conséquence (§ « Décisions retenues pour l'implémentation »).

---

### 5.2 « `Migrate()` laisse un compte administrateur seedé » — **faux aussi**

Le §4.3 du prompt P3-11 demande de ne **pas** exiger que `Users` soit vide, au motif que la migration
`InitialCreate` seede un administrateur. La mesure contredit également cette attente.

**Mesure effectuée** (migrations appliquées une par une, comptage de `Users` après chacune) :

```
20260127184542_InitialCreate                          = 1
20260129192001_AddProductEntryDate                    = 1
20260201181136_ProductSchemaRefactoring               = 1
20260202005050_AddNotifications                       = 1
20260212164646_AddCounterSaleFieldsToOrder            = 0   ← le compte disparaît ici
20260212173313_AddDepositAndRemainingAmountToOrder    = 0
20260212220902_RestoreSaleOrderSeparation             = 0
20260611114307_FixDateTimeDefaultValues               = 0
20260612071633_AddDocumentSequences                   = 0
20260713215132_AddCustomerArchivingAndProtectHistory  = 0
20260717183027_AddProductNormalizedReferenceAndProtectHistory = 0
20260719003826_AddWorkshopSheets                      = 0
20260720204330_AddNotificationResolution              = 0
20260721134634_AddNormalizedUsernameAndSecureLocalUsers = 0
```

**Mécanisme exact.** `20260212164646_AddCounterSaleFieldsToOrder.cs` ouvre son `Up()` (lignes 12–115) par :

```csharp
migrationBuilder.DeleteData(
    table: "Users",
    keyColumn: "UserId",
    keyValue: 1L);
```

et **ne le réinsère jamais** : l'unique `InsertData` de ce fichier (ligne 204) appartient à son `Down()`.

**Vérité retenue :** sur une base migrée jusqu'à HEAD, **toutes** les tables sont vides, `Users` comprise.
L'audit avait donc raison sur le **résultat** (« aucune donnée après migration ») mais pour une raison
inexacte, et le prompt avait raison sur `InitialCreate` sans tenir compte de la suppression ultérieure.

Le socle exécute malgré tout le seed **Production sûr** — c'est le chemin réel du démarrage desktop, il est
idempotent, et il resterait la seule protection si le compte réapparaissait. L'invariant asserté n'est donc pas
« `Users` n'est pas vide » mais **« aucun compte faible ACTIF ne subsiste »**, qui est vrai dans les deux cas.

---

## 6. Architecture des tests de recette

| Élément | Chemin | Rôle |
|---|---|---|
| Socle | `tests/MMV.Application.Tests/Acceptance/AcceptanceScenarioBase.cs` | Base migrée par test, seed Production sûr, portées, lectures fraîches |
| Portée | `AcceptanceScope` (même fichier) | Compose dépôts, primitives et use cases autour d'un `OpticDbContext` — **aucun conteneur DI** |
| Constantes | `AcceptanceData` (même fichier) | Montants, stocks, seuils et valeurs optiques nommés |
| Helpers | `AcceptanceActs` (même fichier) | **Un acte métier au plus** par helper, toujours via un use case public |
| S1 + santé du socle | `Acceptance/CompleteOpticianJourneyAcceptanceTests.cs` | Parcours nominal + vérification unique du socle |
| S9 | `Acceptance/SaleLineClassificationAcceptanceTests.cs` | **Gate d'intégrité** `ItemType` ⇄ `Category` |

Aucun nouveau projet, aucun package, aucune version modifiée.

---

## 7. Découverte du socle — enchaînement de transitions dans une même portée

Constat empirique fait lors de la mise au point de S1, **sans modification de production** :

Enchaîner deux transitions de statut dans **le même** `OpticDbContext` échoue sur
`OrderStatusConflictException`. Cause : à la fin d'une transition réussie, `AdvanceOrderStatusUseCase.cs:193`
exécute `fresh.Status = nextStatus` sur une entité **suivie**, pour réaligner l'affichage de la ViewModel après
un `ExecuteUpdateAsync` qui ne met pas à jour le change tracker. La propriété reste alors marquée « modifiée ».
Le `SaveChangesAsync` de la transition **suivante**, dans le même contexte, réécrit donc cette valeur périmée et
défait la prise atomique qui venait de réussir.

**Ce n'est pas un défaut du parcours de production** : en desktop, chaque action utilisateur ouvre une portée DI
neuve, donc un contexte neuf. Les scénarios de recette reproduisent fidèlement cette réalité
(`AcceptanceActs.AvancerStatutAsync` ouvre sa propre portée par transition). Le point est consigné comme
**dette de robustesse** (une portée réutilisée est un piège silencieux), sans correction en P3-11.

---

## 8. S1 — parcours nominal : ✅ VERT

`ParcoursOpticien_DeLaCreationClientAuReglementDuSolde_EstCoherentDeBoutEnBout` traverse les 13 actes du
parcours, tous par use cases publics, et vérifie l'état final depuis un contexte neuf `AsNoTracking()` :

| Bloc | Résultat observé |
|---|---|
| Vente | total `310.00`, remise `10.00`, final `300.00`, dépôt final `300.00`, solde `0`, `PaymentStatus = Paid`, `StaffId = null` |
| Lignes | 3 lignes, `TotalPrice = Quantity × UnitPrice` sur chacune, optiques copiées, axe canonique |
| Commande | 1 seule, `SaleId` correct, `Status = Delivered`, **2** `OrderItems` (verres seuls) |
| Ordonnance | identique champ à champ à sa création (aucune modification collatérale) |
| Stock | monture `5 → 4`, verre OD `4 → 3`, verre OG `4 → 3` |
| Mouvements | exactement **3**, tous `Out` et négatifs, `PerformedByUserId = null` ; 1 lié au `SaleNumber`, 2 à l'`OrderNumber` |
| Fiche atelier | 1 fiche `IsCurrent`, `Version = 1`, `QcStatus = Passed`, `QcCompletedAt` non nul, empreinte non vide, snapshots renseignés |
| Notifications | exactement **6** — 5 `OrderStatusChanged` + 1 `PaymentReceived`, toutes sur le bon `OrderId` ; **0** `LowStock` |

**Dette P3-7 constatée et NON masquée.** `Sale.Status` reste `AwaitingLenses` alors que la commande est
`Delivered` et la vente `Paid`. Ce n'est pas une incohérence introduite par la recette : c'est le contrat P3-7
explicite (« indicateur historique initial, jamais resynchronisé », `RegisterSaleUseCase.cs:46-52`). Le
scénario le fige tel quel, sans le présenter comme synchronisé avec la commande.

**Santé du socle : ✅ VERT.** Migrations réellement appliquées, `__EFMigrationsHistory` présente, dernière
migration P3-10 présente, index filtré `idx_notifications_active_low_stock_unique` présent, `PRAGMA
foreign_keys = 1`, 9 tables métier vides, aucun compte faible actif, un fichier de base par test.

---

## 9. S9 — gate d'intégrité `ItemType` ⇄ `Category` : 🔴 **DÉFAUT CONFIRMÉ**

### 9.1 Invariant opposé

Pour toute ligne de vente **acceptée** et menée jusqu'à la fabrication : soit l'incohérence est refusée par une
erreur métier, soit le produit est décrémenté **exactement une fois** sur l'ensemble vente + fabrication. Zéro
et deux décréments sont tous deux interdits.

### 9.2 Origine du défaut (lecture de code)

Deux clés indépendantes, jamais croisées :

| Décision | Clé utilisée | Emplacement |
|---|---|---|
| Présence d'une commande de fabrication (`hasLenses`) | **`ItemType` de la ligne** | `RegisterSaleUseCase.cs:225` |
| Lignes versées dans l'`Order` | **`ItemType` de la ligne** | `RegisterSaleUseCase.cs:297` |
| Saut du décrément à la vente | **`Category` du produit** | `RegisterSaleUseCase.cs:330` |
| Décrément à la fabrication | **tous les `OrderItems`**, sans filtre de catégorie | `AdvanceOrderStatusUseCase.cs:270` |

Ni `ValidateLines`, ni `NormalizeItemType`, ni `AcquireProductsAsync` ne vérifie leur cohérence.

### 9.3 Cas A — produit `MONTURE` vendu sur une ligne `LensOd`

**Commandes utilisées** (toutes par use cases publics) :

1. `CreateSupplierUseCase` → fournisseur ;
2. `CreateProductUseCase` → `MON-9001`, `Category = MONTURE`, `StockQuantity = 6`, seuil `1` ;
3. `CreateCustomerUseCase` → client ;
4. `RegisterSaleUseCase` → 1 ligne : `ProductId = MON-9001`, `ItemType = LensOd`, `Quantity = 1`,
   `UnitPrice = 100.00`, optiques OD valides ;
5. `AdvanceOrderStatusUseCase` `New → ToFabricate` ;
6. `AdvanceOrderStatusUseCase` `ToFabricate → InProgress`.

| Observation | Valeur |
|---|---|
| Vente refusée ? | **Non** — acceptée sans aucune erreur métier |
| Commande créée ? | **Oui** (`ItemType = LensOd` ⇒ `hasLenses`) |
| Stock initial | `6` |
| Stock final | **`4`** |
| Attendu par l'invariant | `5` |
| Mouvements `Out` sur le produit | **`2`** (attendu : `1`) |

➡️ **DOUBLE DÉCRÉMENT CONFIRMÉ.** Le produit est décrémenté à la vente (sa catégorie `MONTURE` n'est pas
exclue) **puis** à la fabrication (sa ligne figure dans l'`Order` par son `ItemType`).

### 9.4 Cas B — produit `VERRE` vendu sur une ligne `Frame`

**Commandes utilisées :**

1. `CreateSupplierUseCase` → fournisseur ;
2. `CreateProductUseCase` → `VER-9002`, `Category = VERRE`, `StockQuantity = 6`, seuil `1` ;
3. `CreateCustomerUseCase` → client ;
4. `RegisterSaleUseCase` → 1 ligne : `ProductId = VER-9002`, `ItemType = Frame`, `Quantity = 1`,
   `UnitPrice = 100.00`.

| Observation | Valeur |
|---|---|
| Vente refusée ? | **Non** — acceptée sans aucune erreur métier |
| Commande créée ? | **Non** (`ItemType = Frame` ⇒ pas de `hasLenses`) |
| Stock initial | `6` |
| Stock final | **`6`** |
| Attendu par l'invariant | `5` |
| Mouvements `Out` sur le produit | **`0`** (attendu : `1`) |

➡️ **ABSENCE TOTALE DE DÉCRÉMENT CONFIRMÉE.** Le produit est exclu du décrément de vente par sa catégorie
`VERRE`, et n'entre dans aucune commande faute d'`ItemType` verre : il n'est donc **jamais** sorti du stock.
Une vente réelle est encaissée sans contrepartie d'inventaire — un écart de stock silencieux.

### 9.5 Tables affectées et preuve

Toutes les lectures d'assertion sont faites depuis un `OpticDbContext` **neuf** ouvert sur le même fichier, en
`AsNoTracking()`, après libération des portées d'acte — indispensable puisque `DecrementStockAsync` passe par
`ExecuteUpdateAsync` et ne met pas à jour le change tracker.

| Table | Cas A | Cas B |
|---|---|---|
| `Products.StockQuantity` | `6 → 4` (perte de 2 pour 1 vendu) | `6 → 6` (perte de 0 pour 1 vendu) |
| `StockMovements` | 2 lignes `Out` | 0 ligne |
| `Sales` / `SaleItems` | vente et ligne persistées | vente et ligne persistées |
| `Orders` / `OrderItems` | commande créée, 1 ligne | aucune commande |

### 9.6 Traitement appliqué — **premier cycle** (avant arbitrage)

Conformément au §9 du prompt P3-11 initial :

1. ❌ **aucune modification de la production** ;
2. ❌ **aucune adaptation du test pour qu'il passe** — l'invariant strict est conservé tel quel ;
3. ❌ **aucun test marqué ignoré** — les deux tests restent **rouges**, ce qui est le résultat attendu d'un gate
   qui détecte ce qu'il devait détecter ;
4. ✅ rapport mis à jour immédiatement ;
5. ✅ commandes, état initial, état final, mouvements, tables et preuve par contexte neuf consignés ci-dessus ;
6. ✅ verdict **`P3-11 = NO-GO LOCAL`** ;
7. ❌ aucun commit ;
8. ❌ aucun push ;
9. ✅ **arrêt avant S2 à S8** — non implémentés.

### 9.7 Traitement appliqué — **second cycle** (après arbitrage de périmètre)

Le défaut a ensuite été **explicitement autorisé à être corrigé** dans P3-11 (arbitrage de périmètre). La
correction, ses gardes, ses limites et ses preuves sont détaillées en **§20**. Les deux cas S9 sont désormais des
**tests de refus** et sont **verts** ; l'invariant n'a pas été affaibli, il a été tranché (§20.7).

---

## 10. Scénarios S2 à S8 — implémentés et verts

Après remédiation et franchissement du gate, les sept scénarios restants ont été implémentés conformément à la
spécification de l'audit §24.1, sans en modifier les objectifs.

| # | Test | Domaines traversés | Ce que seule la recette prouve |
|---|---|---|---|
| **S2** | `VenteSansClient_ProduitUneVenteAnonymeCoherente_EtAucuneCommandeSansVerre` | Vente, Stock, Notification | Branche « aucun article différé ⇒ aucune commande » ; motif de mouvement « Vente sans client » ; 0 notification |
| **S3** | `VentePourClientArchive_EstRefusee_EtNeLaisseAucuneTraceDansLeParcours` | Client, Vente, Commande, Stock, Séquences | L'**absence d'effet transversal** du refus, y compris **aucune séquence consommée** |
| **S4** | `FabricationAvecStockVerreInsuffisant_AnnuleStatutFicheStockEtNotification` | Commande, Stock, Atelier, Notification | Le rollback conjoint **statut + fiche atelier + décrément partiel + notification** — jamais prouvé avec la fiche |
| **S5** | `PassageEnPret_EstBloqueSansControleQualite_PuisAutoriseApresValidation` | Commande, Atelier, QC, Notification | Blocage **et** déblocage sur le même parcours ; un refus n'ajoute aucune notification |
| **S6** | `TransitionRejouee_NeDecrementePasDeuxFoisLeStock_EtNeCreeNiSecondeFicheNiSecondeNotification` | Commande, Stock, Atelier, Notification | L'idempotence des effets **cumulés** (2 mouvements, pas 4 ; 1 fiche ; 2 notifications) |
| **S7** | `ReglementDuSoldeRejoue_NEncaisseQuUneFois_EtNeNotifieQuUneFois` | Vente, Commande, Paiement, Notification | Le rejeu sur des montants **réellement calculés par `SalePricingPolicy`** |
| **S8** | `ProduitDesactiveApresVente_ConserveLHistorique_EtNeBloquePasLeParcoursEnCours` | Produit, Vente, Commande, Atelier, Stock | Les deux faces de `IsActive` : le parcours engagé s'achève, la porte d'entrée se ferme |

**Points de méthode respectés :**

- **toutes** les mutations passent par des **use cases publics** — y compris l'archivage client (S3,
  `SetCustomerArchivedUseCase`), la mise à zéro d'un stock (S4, `CreateStockMovementUseCase` en `Adjustment`) et
  la désactivation produit (S8, `SetProductActiveUseCase`) ;
- une **portée neuve par action utilisateur**, et un **contexte frais `AsNoTracking()`** pour chaque assertion ;
- aucun `Thread.Sleep`, aucun timestamp comparé exactement, aucun montant recalculé par le test ;
- une **base migrée indépendante par test** (`Pooling=False`, FK actives, seed Production sûr).

**Ajout au socle :** `AcceptanceScope` expose désormais `SetCustomerArchived` (nécessaire à S3). Aucune autre
modification du socle, et **aucun** nouveau projet, package ou version.

---

## 11. Stock, mouvements et notifications

- **Chronologie nominale confirmée par S1** : la monture sort à la **vente**, les verres à la **fabrication** ;
  3 mouvements au total, tous `Out` et de quantité négative.
- **Lien textuel seulement** : `StockMovement.Reason` porte le `SaleNumber` ou l'`OrderNumber` ; aucune FK ne
  relie un mouvement à une vente ou à une commande (dette D5, inchangée).
- **Aucun auteur** : `Sale.StaffId` et `StockMovement.PerformedByUserId` restent `null` sur tout le parcours —
  figé en assertion de non-régression, pour qu'aucune valeur fausse ne soit inventée.
- **Notifications** : exactement 6 au nominal (5 `OrderStatusChanged` + 1 `PaymentReceived`). Aucune
  notification n'est créée par une décision QC ni par une génération de fiche. Aucune alerte `LowStock` : la
  réconciliation n'est appelée par aucun use case du parcours, et le stock reste au-dessus des seuils.
- **Aucun scénario stock bas supplémentaire n'a été créé** en P3-11.

---

## 12. Limites du modèle observées

| Limite | Statut |
|---|---|
| **Incohérence `ItemType` ⇄ `Category`** | ✅ **Corrigée (§20)** — confirmée empiriquement (§9), puis refusée avant écriture, avec garde historique à la fabrication |
| Aucun lien `Prescription ↔ Sale` | Confirmé : aucune FK `PrescriptionId`. La transmission est humaine ; S1 ne prétend prouver aucun lien |
| `Sale.Status` non resynchronisé | Confirmé : reste `AwaitingLenses` après livraison et paiement (contrat P3-7) |
| Vente sans commande irréglable | `SettleOrderBalanceCommand` porte un `OrderId`, jamais un `SaleId` |
| `Order.SupplierId` jamais renseigné | Confirmé par lecture de `RegisterSaleUseCase` |
| Aucun auteur (QC, vente, mouvement) | Confirmé : aucun use case du parcours ne prend d'identité |
| Portée réutilisée entre deux transitions | Piège silencieux constaté (§7) |
| Compte admin supprimé par une migration | Constaté (§5.2) — `Users` vide sur une base migrée à HEAD |

---

## 13. Dette P3-8 — conservée, non corrigée

`InspectActiveLowStockUniqueIndex` (`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs:878-921`) détermine
l'unicité de l'index en cherchant le mot `UNIQUE` dans le SQL de `sqlite_master` :

```csharp
var valid = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) && …
```

Or l'index se nomme `idx_notifications_active_low_stock_unique` : le suffixe `_unique` **du nom** satisfait à
lui seul le test, sans qu'aucun `CREATE UNIQUE INDEX` ne soit réellement présent. **Un index non unique portant
ce nom serait donc validé comme unique**, et le diagnostic annoncerait une protection anti-doublon qui n'existe
pas.

- **Correction attendue** : interroger `PRAGMA index_list('Notifications')` et lire la colonne `unique`,
  complété au besoin par `PRAGMA index_info` — deux approches que le nom de l'index ne peut pas satisfaire.
- **Dette bloquante avant le verdict final P3-12.**
- **Aucune correction appliquée en P3-11** : le périmètre exclut toute modification de production, et y toucher
  rouvrirait P3-8.

---

## 14. Fichiers créés et modifiés

**Créés — production (1 fichier) :**

- `src/MMV.Domain/Services/SaleLineStockFlowPolicy.cs`

**Créés — tests :**

- `tests/MMV.Domain.Tests/ServiceTests/SaleLineStockFlowPolicyTests.cs`
- `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleLineStockFlowGuardTests.cs`
- `tests/MMV.Application.Tests/UseCases/Orders/FabricationSaleLineStockFlowGuardTests.cs`
- `tests/MMV.Application.Tests/Acceptance/AcceptanceScenarioBase.cs`
- `tests/MMV.Application.Tests/Acceptance/CompleteOpticianJourneyAcceptanceTests.cs`
- `tests/MMV.Application.Tests/Acceptance/SaleLineClassificationAcceptanceTests.cs`
- `tests/MMV.Application.Tests/Acceptance/SaleEntryPointsAcceptanceTests.cs` *(S2, S3)*
- `tests/MMV.Application.Tests/Acceptance/FabricationAndQualityAcceptanceTests.cs` *(S4, S5, S6)*
- `tests/MMV.Application.Tests/Acceptance/SettlementAndCatalogAcceptanceTests.cs` *(S7, S8)*

**Créés — documentation :**

- `docs/implementation/P3-11-business-acceptance-scenarios-implementation-report.md`

**Modifiés — production (2 fichiers, tous deux Application) :**

- `src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs` — garde principale + unification de la
  classification sur la politique Domain ;
- `src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs` — garde historique à
  l'entrée en fabrication.

**Modifiés — tests :**

- `tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleTrackedStaleEntityTests.cs` — **seul** test préexistant
  dont le résultat attendu change (justification complète en §20.8).

**Modifiés — documentation :**

- `docs/implementation/P3-11-business-acceptance-scenarios-audit-report.md`
- `docs/architecture/P3-business-rules-roadmap.md`

**Aucune migration, aucun snapshot EF, aucun fichier d'Infrastructure, aucune UI, aucun package, aucun projet.**

---

## Arbitrage et remédiation du blocage ItemType/Category

*(Section §20 — rédigée lors du second cycle, après arbitrage de périmètre.)*

### 20.1 Décision : corriger dans P3-11

Le défaut découvert par le gate S9 a été **explicitement autorisé à être corrigé dans P3-11**. Cet arbitrage
repose sur quatre constats :

1. c'est un **correctif transverse découvert par la recette**, pas une nouvelle règle inventée ;
2. c'est une **dépendance bloquante** de P3-11 : S2 à S8 traversent tous le couple vente ⇄ fabrication, dont
   l'intégrité était compromise ;
3. il rétablit la **cohérence Vente / Commande / Stock**, qui est précisément l'objet des étapes déjà livrées ;
4. le laisser ouvert reviendrait à livrer une recette qui **documente** un écart d'inventaire silencieux au lieu
   de l'empêcher.

**Ce que cet arbitrage n'est pas :** ni une nouvelle phase officielle, ni une réouverture générale de P3-4B,
P3-5, P3-6 ou P3-7, ni une autorisation de corriger d'autres dettes. Les commits et verdicts historiques de ces
étapes sont **inchangés**, et rien ici ne prétend qu'ils contenaient déjà cette règle.

### 20.2 Propriétaire Domain unique

`src/MMV.Domain/Services/SaleLineStockFlowPolicy.cs` — politique Domain **pure** : aucun dépôt, aucun état,
aucune dépendance Application, EF ou SQLite.

Placée dans `Services/` aux côtés de `SaleLineOpticsPolicy`, `SalePricingPolicy`, `OrderStatusPolicy` et
`WorkshopSheetPolicy`, qui est la convention réelle du dépôt pour les politiques de ligne de vente
(`Policies/` ne contient que `UserIdentityPolicy`).

| Membre | Rôle |
|---|---|
| `IsFabricationItemType(OrderItemType)` | La ligne est-elle consommée **plus tard**, à la fabrication ? |
| `IsDeferredStockCategory(ProductCategoryEnum)` | Le produit est-il consommé **plus tard**, à la fabrication ? |
| `IsCompatible(OrderItemType, ProductCategoryEnum)` | Les deux clés désignent-elles le **même** moment ? |
| `SaleLineProductCategoryMismatchMessage` | Message métier stable, sans aucun détail technique |

### 20.3 Classification retenue

| Classe | Types de ligne | Catégories de produit |
|---|---|---|
| **Différée** (consommée à la fabrication) | `LensOd`, `LensOg` | `VERRE`, `LENTILLE` |
| **Immédiate** (consommée à la vente) | `Frame`, `Accessory` | `CLIPS`, `PLASTIC`, `MONTURE`, `SOLAIRE` |

**Invariant :** une ligne est compatible **si et seulement si**
`IsFabricationItemType(itemType) == IsDeferredStockCategory(category)`.

La condition de catégorie reproduit **à l'identique** celle qui pilotait déjà le saut du décrément à la vente
(`VERRE` ou `LENTILLE`) : aucune frontière métier n'est déplacée — elle reçoit un **propriétaire unique**.

**Aucun repli silencieux.** `IsCompatible` vérifie que les deux valeurs d'énumération sont réellement **définies**
avant toute comparaison. Sans cette vérification, une valeur castée hors domaine retomberait sur `false` dans les
deux classifications et serait déclarée compatible avec une catégorie immédiate — exactement le piège que la
politique doit interdire.

### 20.4 Limite explicitement assumée — ce n'est **pas** une taxonomie commerciale

La règle porte **uniquement sur la classe de consommation du stock**. Elle ne prétend **pas** que :

- `Frame` désigne exactement `MONTURE` ;
- `Accessory` désigne exactement `CLIPS` ;
- la correspondance type ⇄ catégorie soit commercialement exhaustive.

Conséquence directe et **testée** (`MontureVendueSurUneLigneAccessoire_ResteAcceptee_LaRegleNEstPasUneTaxonomie`) :
une monture vendue sur une ligne `Accessory` **reste acceptée**, car les deux se consomment immédiatement et
aucun écart d'inventaire n'en résulte. Une taxonomie plus fine relèverait d'une décision métier distincte, non
prise ici.

Ce que la règle garantit, en revanche : **exactement un** chemin de consommation du stock pour toute ligne
acceptée — jamais zéro, jamais deux.

### 20.5 Garde principale — `RegisterSaleUseCase`

`RequireConsistentStockFlow(lines, products)` s'exécute **dans** la transaction, après l'acquisition atomique des
produits actifs et la construction du dictionnaire `ProduitId → Product`, et **avant** le numéro de vente, le
numéro de commande, la vente, ses lignes, la commande, le décrément, le mouvement, la notification et tout
`SaveChangesAsync`. Un refus ne consomme donc **aucune séquence** et ne laisse aucune trace.

**Seule `Product.Category` chargée depuis la base fait foi** : aucune catégorie fournie par la commande, par
l'UI, par le nom ou par la référence du produit n'est consultée. La lecture utilisée est celle, **fraîche et non
suivie**, déjà mise en place par P3-7.

**Source de vérité partagée.** Les trois décisions qui divergeaient interrogent désormais la même politique :

| Décision | Avant | Après |
|---|---|---|
| `hasLenses` (création de la commande) | test local `is LensOd or LensOg` | `IsFabricationItemType` |
| Lignes versées dans l'`Order` | test local `is LensOd or LensOg` | `IsFabricationItemType` |
| Saut du décrément à la vente | test local `is VERRE or LENTILLE` | `IsDeferredStockCategory` |

Aucune copie divergente de cette classification ne subsiste dans `RegisterSaleUseCase`.

### 20.6 Garde historique — `AdvanceOrderStatusUseCase`

La garde de vente ne protège que les ventes enregistrées **après** P3-11. Une seconde garde est donc opposée à
`ToFabricate → InProgress`, **avant** la prise atomique du statut, la génération de fiche, le décrément, le
mouvement et la notification. Elle protège la **donnée**, pas seulement le flux nominal : commandes historiques,
commandes créées par un autre chemin d'écriture, ou lignes altérées en base.

Un refus laisse le statut, le stock, les mouvements, la fiche et les notifications **strictement inchangés**, et
oppose le **même** message métier stable que la vente.

**Aucune politique inventée sur les articles sans produit.** Un article dont `ProductId` est nul ou dont le
produit n'est pas chargé conserve **exactement** son comportement antérieur : il n'était décrémenté par personne
(`CreateStockMovementsForFabricationAsync` ne retient que les articles porteurs d'un `ProductId`), et P3-11 n'a
pas mandat pour trancher son sort. C'est figé en test
(`ArticleSansProduit_ConserveSonComportementHistorique`).

### 20.7 Chemins d'écriture secondaires — documentés, **non modifiés**

`OrderItem.ItemType` est également écrit par `CreateOrderUseCase` et `UpdateOrderUseCase`. Conformément au
périmètre :

- **aucun des deux n'est modifié** ;
- le `SaleId = 0` de `CreateOrderUseCase` n'est **pas** corrigé (dette #3, inchangée) ;
- P3-11 n'est **pas** étendu à la création manuelle de commande.

C'est précisément la garde de fabrication (§20.6) qui empêche qu'une commande incohérente **néanmoins
persistée** par ces chemins produise un effet de stock dangereux. La dette reste ouverte ; son **danger de
stock** est fermé.

### 20.8 Test préexistant dont le résultat attendu change

`RegisterSaleTrackedStaleEntityTests.ProduitSuiviCommeMonture_ChangeDeCategorieEnBase_LaDecisionDeStockSuitLaCategorieReelle`
(P3-7) affirmait que la décision suit la catégorie **réelle en base** et non la copie suivie et périmée. Il
l'affirmait en observant l'**absence de décrément** — c'est-à-dire, on le sait depuis la recette, **en gravant le
cas B du défaut** : une ligne `Frame` sur un produit `VERRE` encaissée sans jamais sortir du stock.

L'intention du test est **conservée**, son invariant observé devient le **refus métier**, et la discrimination
est strictement **plus nette** qu'avant : si la copie suivie `MONTURE` décidait, la ligne `Frame` serait
compatible et la vente **acceptée** ; c'est parce que la catégorie fraîche `VERRE` est lue que la vente est
**refusée**. Aucune tolérance n'est introduite. C'est le **seul** test préexistant dont le résultat attendu
change.

### 20.9 Tests ajoutés

**Domain — `SaleLineStockFlowPolicyTests` (40 tests) :**

- matrice **exhaustive** des 24 couples réels (4 types × 6 catégories), couples compatibles et incompatibles
  énumérés séparément ;
- `IsFabricationItemType` et `IsDeferredStockCategory` testées **directement**, pas seulement à travers
  `IsCompatible` ;
- valeurs hors domaine : type inconnu, catégorie inconnue, et les deux à la fois — **jamais** compatibles ;
- garde structurelle : si une valeur est ajoutée à l'une des deux énumérations, le test échoue et **impose** de
  trancher explicitement sa classe, au lieu de la laisser hériter d'un défaut silencieux ;
- message métier vérifié stable et **exempt** de tout détail technique.

**Application — `RegisterSaleLineStockFlowGuardTests` (11 tests, vraies bases SQLite) :** refus des deux sens et
d'un troisième couple (`Accessory` + `LENTILLE`), refus de la vente **entière** dès qu'une seule ligne est
incohérente, et cas nominaux (`MONTURE + Frame`, catégorie immédiate + `Accessory`, `VERRE`/`LENTILLE` ×
`LensOd`/`LensOg`) prouvant qu'aucun flux légitime n'est durci. Chaque refus est vérifié sur **tous** les effets
possibles : vente, ligne, commande, article de commande, stock, mouvement, notification et **séquences**.

**Application — `FabricationSaleLineStockFlowGuardTests` (4 tests) :** refus dans les deux sens sur un état
historique arrangé directement, non-régression d'une commande cohérente (statut avancé, 1 mouvement, fiche
générée), et conservation du comportement des articles sans produit.

### 20.10 Résultat S9 — avant / après

| Cas | Avant remédiation | Après remédiation |
|---|---|---|
| **A** — produit `MONTURE`, ligne `LensOd` | vente **acceptée**, stock `6 → 4`, **2** mouvements | **refusée** avant toute écriture ; stock `6`, 0 mouvement, 0 vente, 0 commande, **0 séquence consommée** |
| **B** — produit `VERRE`, ligne `Frame` | vente **acceptée**, stock `6 → 6`, **0** mouvement | **refusée** avant toute écriture ; état identique et intact |

Les deux tests sont **conservés**, **non ignorés**, **renommés** en
`…_EstRefuseeSansAucuneEcriture`, et l'invariant n'a pas été affaibli : il a été **tranché** vers l'issue « refus »
que la remédiation a retenue.

### 20.11 Ce que la remédiation n'a pas touché

| Élément | État |
|---|---|
| Migration | **aucune** — schéma strictement inchangé |
| UI | **aucune** — `MMV.App.Tests` reste exactement à **239** |
| Infrastructure, snapshot EF | **non modifiés** |
| Packages, versions, projets | **aucun ajout ni changement** |
| Dette P3-8 (`InspectActiveLowStockUniqueIndex`) | **non corrigée** (§13) |
| `CreateOrderUseCase`, `UpdateOrderUseCase` | **non modifiés** |
| Change tracker entre deux transitions | **non corrigé** (§7) |
| `Sale.Status` | **non synchronisé** (dette P3-7 conservée) |

---

## 15. Résultats

### 15.1 Résultat ciblé

```
dotnet test tests/MMV.Application.Tests/MMV.Application.Tests.csproj --filter "FullyQualifiedName~Acceptance"
```

| Test | Scénario | Résultat |
|---|---|---|
| `SocleDeRecette_ProduitUneBaseMigreeSaineEtNonPolluee` | santé du socle | ✅ vert |
| `ParcoursOpticien_DeLaCreationClientAuReglementDuSolde_EstCoherentDeBoutEnBout` | S1 | ✅ vert |
| `VenteSansClient_ProduitUneVenteAnonymeCoherente_EtAucuneCommandeSansVerre` | S2 | ✅ vert |
| `VentePourClientArchive_EstRefusee_EtNeLaisseAucuneTraceDansLeParcours` | S3 | ✅ vert |
| `FabricationAvecStockVerreInsuffisant_AnnuleStatutFicheStockEtNotification` | S4 | ✅ vert |
| `PassageEnPret_EstBloqueSansControleQualite_PuisAutoriseApresValidation` | S5 | ✅ vert |
| `TransitionRejouee_NeDecrementePasDeuxFoisLeStock_EtNeCreeNiSecondeFicheNiSecondeNotification` | S6 | ✅ vert |
| `ReglementDuSoldeRejoue_NEncaisseQuUneFois_EtNeNotifieQuUneFois` | S7 | ✅ vert |
| `ProduitDesactiveApresVente_ConserveLHistorique_EtNeBloquePasLeParcoursEnCours` | S8 | ✅ vert |
| `LigneVerreSurProduitMonture_EstRefuseeSansAucuneEcriture` | S9 cas A | ✅ vert *(était rouge)* |
| `LigneMontureSurProduitVerre_EstRefuseeSansAucuneEcriture` | S9 cas B | ✅ vert *(était rouge)* |

**11 tests de recette**, 9 scénarios (S1 à S9, S9 comptant deux cas) + 1 vérification de santé du socle.

Gates ciblés de la remédiation :

| Commande | Résultat |
|---|---|
| `--filter "FullyQualifiedName~SaleLineStockFlow"` (Domain) | **40** passés, 0 échec |
| `--filter "FullyQualifiedName~SaleLine"` (Application) | **17** passés, 0 échec |

### 15.2 Résultat complet

```
dotnet test MMV.sln --no-build -c Debug
```

| Projet | Total | Passés | Échecs | Ignorés | Δ baseline |
|---|---:|---:|---:|---:|---:|
| `MMV.Domain.Tests` | 633 | 633 | 0 | 0 | +40 |
| `MMV.Application.Tests` | 607 | 607 | 0 | 0 | +26 |
| `MMV.App.Tests` | 239 | 239 | 0 | 0 | **0** |
| **Total** | **1479** | **1479** | **0** | **0** | **+66** |

Baseline **1413** + 40 (policy Domain) + 15 (gardes Application ciblées) + 11 (recette) = **1479**.
**Aucune régression** sur les 1413 tests préexistants ; `MMV.App.Tests` reste **exactement** à 239, ce qui
confirme qu'aucune UI n'a été touchée.

### 15.3 Temps d'exécution

Les 11 tests de recette s'exécutent en ≈ 2 s au total (migrations comprises, ~0,3 s par base). L'impact sur la
durée de `MMV.Application.Tests` reste marginal (≈ 17 s).

---

## 16. Sécurité, EF et architecture

| Contrôle | Résultat |
|---|---|
| `dotnet list MMV.sln package --vulnerable --include-transitive` | aucune vulnérabilité sur les 7 projets |
| `dotnet ef migrations has-pending-model-changes` | « No changes have been made to the model since the last migration. » |
| Références `MMV.Application` | `MMV.Domain` uniquement |
| Paquets `MMV.Application` | `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 |
| Build | 0 erreur / 0 avertissement |
| Migrations créées | **aucune** |
| Packages ajoutés ou modifiés | **aucun** |
| Projets ajoutés | **aucun** |
| UI modifiée | **aucune** |
| Code de production modifié | **1 fichier Domain créé** (`SaleLineStockFlowPolicy.cs`) + **2 fichiers Application modifiés** (`RegisterSaleUseCase.cs`, `AdvanceOrderStatusUseCase.cs`) — Infrastructure et UI **inchangées** |

---

## 17. Dettes à traiter avant le verdict P3-12

| # | Dette | Rang |
|---|---|---|
| 1 | ~~**Incohérence `ItemType` ⇄ `Category`**~~ | ✅ **Résolue en P3-11** (§20) — arbitrage rendu, correctif appliqué, S9 vert |
| 2 | `InspectActiveLowStockUniqueIndex` : faux positif d'unicité (dette P3-8) | 🔴 Bloquante avant verdict P3-12 |
| 3 | `CreateOrderUseCase` produit une commande orpheline (`SaleId = 0`) inexploitable | P3-12 ou ultérieur |
| 4 | Entité `Order` suivie laissée « modifiée » après une transition (§7) | Robustesse |
| 5 | `DeleteData(Users, UserId = 1)` sans réinsertion dans `AddCounterSaleFieldsToOrder` | À caractériser |
| 6 | Double rôle d'`Order` (fournisseur ⇄ atelier), `Order.SupplierId` jamais renseigné | Post-P3 |
| 7 | Règlement impossible pour une vente sans commande | Post-P3 |
| 8 | Lien `Prescription ↔ Sale` absent | Post-P3 (décision métier) |
| 9 | Auteur du QC, `Sale.StaffId`, `StockMovement.PerformedByUserId` | Redesign UI |
| 10 | Horloge non injectable (`Now` ⇄ `UtcNow`) | Hors P3 |

---

## 18. État Git final

`git status --short` :

```
 M docs/architecture/P3-business-rules-roadmap.md
 M src/MMV.Application/UseCases/Orders/AdvanceOrderStatus/AdvanceOrderStatusUseCase.cs
 M src/MMV.Application/UseCases/Sales/RegisterSale/RegisterSaleUseCase.cs
 M tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleTrackedStaleEntityTests.cs
?? design-handoff/
?? design/
?? docs/implementation/P3-11-business-acceptance-scenarios-audit-report.md
?? docs/implementation/P3-11-business-acceptance-scenarios-implementation-report.md
?? docs/ui/
?? src/MMV.Domain/Services/SaleLineStockFlowPolicy.cs
?? tests/MMV.Application.Tests/Acceptance/
?? tests/MMV.Application.Tests/UseCases/Orders/FabricationSaleLineStockFlowGuardTests.cs
?? tests/MMV.Application.Tests/UseCases/Sales/RegisterSaleLineStockFlowGuardTests.cs
?? tests/MMV.Domain.Tests/ServiceTests/SaleLineStockFlowPolicyTests.cs
```

`git diff --check` : vide (aucune erreur d'espaces).

`git diff --stat` : 4 fichiers suivis modifiés, 138 insertions, 16 suppressions.

**Aucun fichier d'Infrastructure, aucune migration, aucun snapshot, aucune UI, aucun package, aucun projet.**

**Aucun commit. Aucun push.**

---

## 19. Verdict

```
P3-11 = GO LOCAL
```

**Motif.** Toutes les conditions du verdict sont réunies et vérifiées par exécution :

| Condition | État |
|---|---|
| S1 à S9 tous verts | ✅ 11 tests de recette, 0 échec |
| Les deux incohérences refusées **avant écriture** | ✅ S9 cas A et B, plus 15 tests ciblés |
| Chaque ligne acceptée suit **exactement un** chemin de consommation du stock | ✅ propriétaire Domain unique, matrice exhaustive |
| Garde de fabrication protégeant les données historiques incompatibles | ✅ `AdvanceOrderStatusUseCase`, refus sans aucun effet |
| S2 à S8 implémentés et verts | ✅ |
| Aucun test préexistant en régression | ✅ 1413 préexistants verts ; le seul dont le résultat attendu change est justifié en §20.8 |
| Aucune migration | ✅ |
| Aucune UI | ✅ `MMV.App.Tests` exactement à 239 |
| Aucune vulnérabilité | ✅ 7 projets |
| Aucun `pending model change` | ✅ |
| Rapports complets, roadmap à jour | ✅ |

**Ce que ce verdict ne couvre pas.** Il est **local** : le commit et la CI restent à faire, et P3-11 ne sera clos
qu'après une CI verte. La **dette P3-8** (§13, faux positif d'unicité d'index) reste **non corrigée et bloquante
avant le verdict P3-12**. Les dettes #3 à #10 (§17) restent ouvertes.

**Aucun commit. Aucun push. Aucune migration. Aucune UI.**

**STOP.**
