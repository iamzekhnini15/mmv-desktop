# P3-11 — Audit des scénarios métier de recette bout-en-bout

> **Mode : AUDIT ET RÉDACTION DE RAPPORT UNIQUEMENT.**
> Aucun code, aucun test, aucune migration, aucune UI n'a été modifié. Aucun commit, aucun push.

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
| Tests attendus | 1413 |
| Date d'audit | 22 juillet 2026 |

Parcours cible officiel :

```
client → ordonnance → vente de produits optiques → commande → fabrication
      → fiche atelier → contrôle qualité → livraison → encaissement du solde
```

---

## 2. État Git et CI

### 2.1 Git

| Contrôle | Attendu | Observé | Verdict |
|---|---|---|---|
| `git branch --show-current` | `p3-business-rules` | `p3-business-rules` | ✅ |
| `git rev-parse HEAD` | `8d4bf49d…` | `8d4bf49da1c14ea1d6664eb94878c025b5420656` | ✅ |
| `git rev-parse origin/p3-business-rules` | idem | `8d4bf49da1c14ea1d6664eb94878c025b5420656` | ✅ |
| `git status --short` | aucun fichier suivi modifié | `?? design-handoff/`, `?? design/`, `?? docs/ui/` | ✅ |
| `git diff --check` | vide | vide | ✅ |

`git log -8 --oneline` :

```
8d4bf49 feat(P3-10): secure local user accounts
057df0f feat(P3-9): enforce supplier business rules
b30f774 feat(P3-8): secure business notifications
ae0e35d feat(P3-7): enforce sale business rules
53d689e feat(P3-6B): add versioned workshop sheets
a4d7380 feat(P3-6): enforce order workflow rules
3e7a1ab feat(P3-5): secure stock mutations
c246ebc feat(P3-4B): enforce product business rules
```

Les seuls dossiers non suivis sont exactement les trois tolérés. **HEAD local = HEAD distant.**

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
| `url` | https://github.com/iamzekhnini15/mmv-desktop/actions/runs/29938082120 |

Job unique **« Restore / Build / Test / Scan »** — `conclusion: success`. Les 13 étapes sont en succès, dont
`Build`, `Test`, `Audit des packages vulnérables (JSON + sévérité)` et `Check EF Core pending model changes`.
**Aucun job obligatoire en échec.** Le SHA de la CI correspond exactement au HEAD.

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
| Références `MMV.Application` | `MMV.Domain` uniquement | `..\MMV.Domain\MMV.Domain.csproj` (seule référence projet) | ✅ |

Paquets de `MMV.Application` : `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.1 uniquement.
**La pureté de la couche Application est intacte.**

**Baseline : ✅ conforme.**

---

## 4. Documents lus

### 4.1 Architecture

| Document | Apport pour P3-11 |
|---|---|
| `docs/architecture/P3-business-rules-roadmap.md` | §3 définit P3-11 : « recette bout-en-bout de parcours opticien … tests de scénario haut niveau, non-régression métier ». §4 liste les points durs transverses : politique de stock unique (P3-5/6/7), double machine à états `SaleStatus`/`OrderStatus`, **double rôle d'`Order`** (commande fournisseur *et* ordre de fabrication), cohérence horloge `Now`/`UtcNow` non injectable. |
| `adr-prod-db-001-multi-poste-database-strategy.md` | Production V1 sur base centrale partagée ⇒ toute écriture sensible doit rester atomique et robuste au multi-poste. Interdit de supposer une exclusivité mono-poste dans les scénarios. |
| `adr-application-boundaries.md` | Application dépend du Domain seul ; les règles métier appartiennent au Domain ou aux use cases, jamais aux ViewModels. Contraint P3-11 à passer par les use cases publics. |
| `adr-transaction-idempotency.md` | Origine d'`ITransactionRunner`, `PersistenceException` et `PersistenceErrorMapper`. Confirme que seule une écriture **multi-`SaveChanges`** exige une transaction explicite. |
| `adr-sqlite-lifecycle.md` | Cycle de vie de la base SQLite, adoption/baseline des bases historiques `EnsureCreated`. |

### 4.2 Rapports d'implémentation P3

Lus intégralement : P3-2B (archivage/suppression client), P3-3B (validation ordonnance + client archivé),
P3-4B (règles produit), P3-5 (stock et mouvements), P3-6 (workflow commandes), P3-6B (fiches atelier
versionnées), P3-7 (règles de vente), P3-8 (notifications), P3-9 (fournisseurs), P3-10 (utilisateurs locaux).

### 4.3 Documents ciblés

`docs/domain/P3-workshop-sheet-and-transposition-notes.md` (transposition sphère/cylindre/axe comme **vue**
dérivée, jamais un remplacement de la prescription source).

### 4.4 Primauté du code

> **Le code réel prime sur les documents.** Toutes les affirmations des sections 6 à 18 ci-dessous sont
> établies par lecture du code à `8d4bf49`. Là où un rapport et le code divergent, le code fait foi et
> l'écart est signalé.

---

## 5. Architecture de test

### 5.1 Inventaire

| Élément | Chemin exact | Type | Réutilisable P3-11 ? | Limites |
|---|---|---|---:|---|
| `MMV.Domain.Tests` | `tests/MMV.Domain.Tests/` | Projet (593 tests) | Non | Pas d'accès à la couche Application ; propriétaire des politiques pures. |
| `MMV.Application.Tests` | `tests/MMV.Application.Tests/` | Projet (581 tests) | **Oui — propriétaire désigné** | Référence Domain + Application + **Infrastructure** ; SQLite réel déjà en place. |
| `MMV.App.Tests` | `tests/MMV.App.Tests/` | Projet (239 tests) | Non | ViewModels/navigation/architecture UI ; hors périmètre backend. |
| Socle SQLite jetable (fiches) | `tests/MMV.Application.Tests/UseCases/WorkshopSheets/WorkshopSheetTestBase.cs` | Classe de base `IDisposable` | **Oui — patron de référence** | `EnsureCreated` (pas `Migrate`) ; `Foreign Keys=False` ; seed spécifique atelier. |
| Fabrique de `DbContext` | `WorkshopSheetTestBase.CreateContext` + copies locales dans ~30 fichiers | Méthode statique privée | Patron oui, code non | **Dupliquée** dans presque chaque fichier de test ; aucune fabrique partagée. |
| Helper de schéma | `EnsureSchema(path)` (dupliqué) | Méthode statique | Patron oui | `context.Database.EnsureCreated()`. |
| Helper de migration | `tests/MMV.Application.Tests/Migrations/ProductNormalizedReferenceMigrationTests.cs:62` | `ctx.Database.Migrate()` | Référence unique | **Seul** endroit du dépôt de test appelant `Migrate()`. |
| Helpers de seed | `SeedOrder`, `SeedOrderWithoutItems`, `SeedCustomer`, `SeedProduct`, … | Méthodes statiques par fichier | Patron oui, code non | Écrivent **directement par `DbContext`** (contournent les use cases) — acceptable pour l'arrangement, jamais pour les actes. |
| Assertions par contexte neuf | `AssertNothingPersisted` (`RegisterSaleBusinessRulesTests`) | Méthode statique | **Oui — patron de référence** | Ouvre un `DbContext` neuf pour relire ; exactement le patron voulu en P3-11. |
| Mutateur d'obsolescence | `ChangeOrderTechnicalData` (`WorkshopSheetTestBase`) | Méthode statique | Oui | Rend toute fiche existante obsolète en modifiant `Sphere`. |
| Fakes / Moq | `Moq` 4.20.72 référencé | Paquet | Marginalement | À proscrire sur les dépôts métier principaux en P3-11. |
| Intercepteurs SQL | *aucun* | — | — | Aucun intercepteur ni décorateur de dépôt n'existe. |
| Décorateurs de dépôt | *aucun* | — | — | — |
| Helpers de transaction | `new EfTransactionRunner(context)` construit à la main | Instanciation directe | Oui | Aucun conteneur DI n'est utilisé dans les tests. |
| Tests multi-contextes | `LowStockConcurrencyAndQueryCountTests`, `WorkshopSheetConcurrencyHardeningTests`, `SettleOrderBalanceAtomicityTests`, `RegisterSaleTrackedStaleEntityTests` | Fichiers | **Oui — patron de référence** | Prouvent la concurrence **déterministe** par deux `DbContext` sur le **même fichier** SQLite, sans `Sleep`. |
| Conteneur DI réel | `src/MMV.App/App.axaml.cs:122-176` | `ServiceCollection` | **Non** | Enregistre aussi Avalonia, ViewModels, `ISessionService`, `INavigationService`, `IDialogService` : inutilisable sans UI. |

### 5.2 Réponses aux douze questions

1. **Existe-t-il déjà un dossier `Acceptance`, `Scenarios`, `EndToEnd` ou équivalent ?**
   **Non.** L'arborescence de `MMV.Application.Tests` est `Architecture/`, `Migrations/` et
   `UseCases/{Customers,Notifications,Orders,Prescriptions,Products,Sales,Stock,Suppliers,Users,WorkshopSheets}`.
   Aucun dossier de recette n'existe dans aucun des trois projets.

2. **Quel projet est le meilleur propriétaire des scénarios P3-11 ?**
   **`tests/MMV.Application.Tests`.** Seul projet référençant simultanément `MMV.Application` (les use cases,
   les actes) et `MMV.Infrastructure` (le `DbContext` réel, les dépôts, les primitives), avec
   `Microsoft.EntityFrameworkCore.Sqlite` 8.0.27 déjà présent. Aucun paquet nouveau n'est nécessaire.

3. **Les tests peuvent-ils appeler uniquement les use cases Application ?**
   **Pour les actes métier : oui, sans exception.** Vérifié point par point :
   - Fournisseur → `CreateSupplierUseCase` ✅
   - Produits et **stock initial** → `CreateProductUseCase` (`CreateProductCommand.StockQuantity`) ✅
   - Réapprovisionnement éventuel → `CreateStockMovementUseCase` (type `In`) ✅
   - Client, ordonnance, vente, transitions, fiche, QC, règlement → §6.1 ✅

   Conclusion : le parcours nominal est intégralement réalisable par use cases publics. Les lectures
   d'assertion, elles, se font par `DbContext` neuf (autorisé explicitement par le prompt §20).

4. **Faut-il construire le conteneur DI réel ?**
   **Non.** Le conteneur vit dans `MMV.App` (Avalonia) et mélange persistance, session UI, navigation et
   ViewModels. Le reconstruire dans les tests créerait la « seconde application » que le prompt §5.10
   redoute. Le patron existant — instancier les dépôts et primitives à la main autour d'un `OpticDbContext`
   partagé — reproduit **exactement** la portée DI de production (`AddScoped` sur un même contexte) pour un
   coût nul et sans couplage à l'UI.

5. **Existe-t-il un bootstrap complet de l'Infrastructure ?**
   **Partiellement.** `SqliteDatabaseManager` (`src/MMV.Infrastructure/Data/`) sait migrer, diagnostiquer et
   adopter une base ; `DbInitializer` appelle `EnsureCreated()` ; `DatabaseSeeder` / `SeedOptionsResolver`
   gouvernent le seed par environnement. Mais **aucun** point d'entrée ne compose « base + dépôts + use
   cases » : cette composition n'existe que dans `App.axaml.cs`.

6. **Les tests utilisent-ils `EnsureCreated` ou `Migrate` ?**
   **`EnsureCreated` quasi partout** (~30 occurrences), **`Migrate` une seule fois**
   (`ProductNormalizedReferenceMigrationTests.cs:62`, qui teste précisément une migration).

7. **Quel mécanisme reflète le mieux la production desktop actuelle ?**
   **`Migrate()`.** La production applique les migrations (`SqliteDatabaseManager.cs:222/240/502`) ;
   `EnsureCreated()` construit le schéma depuis le modèle **sans** `__EFMigrationsHistory` et **sans**
   exécuter le SQL personnalisé des migrations. Écart concret et vérifié : l'index unique filtré
   `idx_notifications_active_low_stock_unique` (P3-8) est créé par du SQL brut de migration. Une base
   `EnsureCreated` **n'a pas** cet index, donc l'anti-doublon d'alerte stock bas n'y est **pas** protégé au
   niveau base. **Recommandation P3-11 : `Migrate()`**, précisément parce que les scénarios de recette
   doivent observer la base telle qu'elle est en production.

8. **Comment garantir une base neuve par scénario ?**
   Patron déjà éprouvé : répertoire temporaire `Path.Combine(Path.GetTempPath(), "mmv-p3-11-" + Guid.NewGuid().ToString("N"))`
   créé au constructeur, **un fichier `.db` distinct par test**, `SqliteConnection.ClearAllPools()` puis
   suppression récursive best-effort dans `Dispose()`. Chaîne de connexion `Pooling=False` obligatoire
   (sinon le fichier reste verrouillé et le nettoyage échoue sous Windows).

9. **Comment relire les résultats depuis un contexte neuf ?**
   Libérer tous les `DbContext` d'acte, puis ouvrir un `OpticDbContext` neuf sur le **même chemin** et lire
   en `AsNoTracking()`. Patron déjà présent (`AssertNothingPersisted`). Indispensable : les primitives
   atomiques (`ExecuteUpdateAsync`) **ne mettent pas à jour le change tracker**, donc relire depuis le
   contexte d'acte renverrait des valeurs périmées.

10. **Quel code de test risque de devenir une seconde application ?**
    Quatre risques identifiés : (a) un conteneur DI de test reconstruisant `App.axaml.cs` ; (b) une fabrique
    « qui construit tous les use cases » devenant un mini-composeur ; (c) des helpers de seed recalculant les
    montants (donc dupliquant `SalePricingPolicy`) ; (d) un helper « ParcoursComplet() » unique masquant tout
    le scénario. **Garde-fou retenu** : les montants et statuts attendus sont **relus depuis la base**, jamais
    recalculés dans le test, et chaque étape du parcours nominal reste visible dans le corps du test.

---

## 6. Parcours nominal réel

> **Avertissement.** Le parcours cible officiel suppose un enchaînement automatique de bout en bout. **Le code
> n'en offre pas un.** Deux arêtes du parcours cible ne sont pas des arêtes de données (§6.2), et le passage
> `New → ToFabricate` n'est déclenché par rien d'autre qu'un appel explicite.

### 6.1 Séquence réellement permise par le code

| Ordre | Use case exact | Entrées indispensables | Écriture | Sortie utilisée ensuite |
|---:|---|---|---|---|
| 1 | `CreateSupplierUseCase` | `Name` | `Suppliers` | `SupplierId` |
| 2 | `CreateProductUseCase` ×3 | `Reference`, `Name`, `Category`, `SupplierId`, prix, `StockQuantity`, `StockAlertThreshold` | `Products` (+ `GlassDetails`/`AccessoryDetails`) | `ProductId` monture, verre OD, verre OG |
| 3 | `CreateCustomerUseCase` | `FirstName`, `LastName` | `Customers` | `CustomerId` |
| 4 | `CreatePrescriptionUseCase` | `CustomerId`, `IssueDate`, valeurs optiques | `Prescriptions` | `PrescriptionId` — **non transmis à l'étape suivante** (§6.2) |
| 5 | `RegisterSaleUseCase` | `CustomerId?`, `Lines[]` (`ProductId`, `ItemType`, `Quantity`, `UnitPrice`, optiques), `DiscountAmount`, `DepositAmount`, `PaymentMethod` | `Sales`, `SaleItems`, **`Orders` + `OrderItems` si verres**, `StockMovements` (non-verres), `DocumentSequences` | `SaleId`, `SaleNumber`, **`OrderId`**, `OrderNumber`, `RemainingAmount` |
| 6 | `AdvanceOrderStatusUseCase` | `OrderId`, `CurrentStatus=New`, `NextStatus=ToFabricate` | `Orders.Status`, `Notifications` | statut |
| 7 | `AdvanceOrderStatusUseCase` | `ToFabricate → InProgress` | `Orders.Status`, **`WorkshopSheets` v1 + `WorkshopSheetItems`**, décrément stock verres, `StockMovements`, `Notifications` | `WorkshopSheetId` (via `GetCurrentWorkshopSheetUseCase`) |
| 8 | `AdvanceOrderStatusUseCase` | `InProgress → QualityCheck` | `Orders.Status`, fiche si absente (filet de compatibilité), `Notifications` | statut |
| 9 | `GetCurrentWorkshopSheetUseCase` | `OrderId` | *(lecture)* | `WorkshopSheetId` |
| 10 | `ValidateWorkshopSheetQcUseCase` | `WorkshopSheetId`, `Comment?` | `WorkshopSheets.QcStatus=Passed`, `QcComment`, `QcCompletedAt` | fiche validée |
| 11 | `AdvanceOrderStatusUseCase` | `QualityCheck → Ready` | `Orders.Status` (prise **enrichie** fiche), `Notifications` | statut |
| 12 | `AdvanceOrderStatusUseCase` | `Ready → Delivered` | `Orders.Status`, `Notifications` | statut terminal |
| 13 | `SettleOrderBalanceUseCase` | **`OrderId`** | `Sales.DepositAmount/RemainingAmount/PaymentStatus`, `Notifications` | solde nul, `Paid` |
| 14 *(optionnel)* | `GenerateLowStockNotificationsUseCase` | *(aucune)* | `Notifications` (ouvre/ferme les alertes) | alertes réconciliées |

### 6.2 Les deux arêtes manquantes du parcours cible

**(A) `ordonnance → vente` n'est pas une arête de données.**
Il n'existe **aucune** clé `PrescriptionId` sur `Sale` ni sur `SaleItem` (vérifié par recherche exhaustive :
`PrescriptionId` n'apparaît que dans le domaine ordonnance lui-même). `SaleLineOpticsPolicy` le documente
explicitement : *« Aucun lien avec une ordonnance. Les données optiques d'une vente sont un instantané copié :
il n'existe aucune clé étrangère `PrescriptionId` »*. La transmission ordonnance → vente est **humaine** :
l'opticien lit l'ordonnance et saisit les valeurs dans les lignes. **Conséquence P3-11 :** aucun invariant
« la vente correspond à l'ordonnance » n'est assertable. Le scénario nominal doit créer l'ordonnance (elle
fait partie du parcours métier et son refus « client archivé » est testable) mais ne peut **pas** prétendre
prouver un lien qui n'existe pas.

**(B) `livraison → encaissement du solde` passe obligatoirement par une commande.**
`SettleOrderBalanceCommand` porte un `OrderId`, jamais un `SaleId`, et `SettleAsync` retourne
`OrderFound = false` si `fresh.Sale is null`. **Une vente sans verres n'a pas de commande** (§7) et son solde
n'est donc **réglable par aucun use case**. C'est une limite réelle du modèle, à documenter, pas à contourner.

### 6.3 Détail par étape

**Préparation.** Aucun utilisateur local n'est nécessaire (§13). Le fournisseur est **obligatoire** pour créer
un produit (`CreateProductUseCase.SupplierRequiredMessage`, `SupplierNotFoundMessage`).

**Vente (`RegisterSaleUseCase`, 448 lignes).** Ordre d'exécution : validation pure → transaction → prise du
client actif → prise des produits actifs (par `ProductId` croissant, ordre déterministe anti-interblocage) →
numérotation → écriture. Points établis :
- `command.TotalAmount`, `FinalAmount`, `RemainingAmount` sont **ignorés** ; tout provient de `SalePricingPolicy`.
- `UnitPrice` reste le prix **négocié** de l'appelant (borne `≥ 0`, gratuité permise).
- `SaleItem.TotalPrice` est **calculé** (`pricing.Lines[i].LineTotal`), jamais fourni.
- `Sale.Status = hasLenses ? AwaitingLenses : Delivered` — indicateur historique initial, **jamais** remis à
  jour ensuite.
- `Sale.EstimatedDelivery = IsCounterSale ? Now : Now.AddDays(14)` — piloté par `IsCounterSale`, donc
  **désaligné** possible avec `Status` (dette P3-7 documentée).
- Un refus ne consomme **aucun** numéro de séquence (validation avant `NextNumberAsync`).

**Commande et atelier.** Statut initial `New`. Transitions **linéaires strictes** (§7). La fiche v1 est créée
automatiquement à `ToFabricate → InProgress`, avec filet de compatibilité à `InProgress → QualityCheck`.

**Paiement.** `TrySettleRemainingBalanceAsync` = `UPDATE Sales SET DepositAmount = FinalAmount,
RemainingAmount = 0, PaymentStatus = Paid WHERE SaleId = @id AND RemainingAmount > 0`. `RemainingAmount`
étant `nullable`, un `NULL` ne satisfait pas `> 0` : une vente au solde inconnu n'est jamais réglée à l'aveugle.

---

## 7. Vérité sur la création de commande

1. **Une vente crée-t-elle toujours une commande ?**
   **Non.** Uniquement si `hasLenses` — c'est-à-dire s'il existe au moins une ligne dont l'`ItemType` vaut
   `LensOd` ou `LensOg` (`RegisterSaleUseCase.cs:225`).

2. **Une vente comptoir sans fabrication crée-t-elle une commande ?**
   **Non**, si elle ne contient aucune ligne verre. Mais attention : `IsCounterSale` **n'intervient pas** dans
   la décision. Une vente déclarée « comptoir » **contenant des verres crée quand même une commande**. Le
   commentaire du code est explicite : *« Une commande fournisseur naît dès qu'un verre est présent —
   indépendamment de `IsCounterSale` »*.

3. **Quels produits déclenchent une commande ?**
   Aucun **produit** ne déclenche une commande : c'est l'**`ItemType` de la ligne** (`LensOd`/`LensOg`) qui
   décide, pas `Product.Category`.

4. **Les verres et montures suivent-ils le même chemin ?**
   **Non.** Verres/lentilles : mis dans l'`Order`, **non** décrémentés à la vente, décrémentés à
   `ToFabricate → InProgress`. Montures et accessoires : **jamais** dans l'`Order`, décrémentés à la vente.

5. **Une commande peut-elle être créée manuellement ?**
   **Oui — mais le résultat est inutilisable.** `CreateOrderUseCase` (`CreateOrder/CreateOrderUseCase.cs:45-53`)
   ne renseigne **jamais** `SaleId`. Or `Order.SaleId` est un `long` **non nullable** et `Order.Sale` est une
   navigation **requise** (`public virtual Sale Sale { get; set; } = null!;`). L'ordre créé a donc `SaleId = 0`,
   orphelin. `OrderRepository.GetWithItemsAsync` fait `.Include(o => o.Sale)` sur une navigation requise ⇒ EF
   Core 8 génère un **INNER JOIN** ⇒ la commande orpheline est **introuvable**. En conséquence,
   `AdvanceOrderStatusUseCase` renvoie `OrderFound = false` et `SettleOrderBalanceUseCase` également : **une
   commande créée manuellement ne peut ni avancer, ni être réglée.** Ce défaut est déjà connu (rapport
   P2B-2D §16.2) et les tests existants le contournent en désactivant les clés étrangères.

6. **Existe-t-il plusieurs chemins de création ?**
   Oui, deux : `RegisterSaleUseCase` (automatique, `SaleId` renseigné, **le seul viable**) et
   `CreateOrderUseCase` (manuel, `SaleId` orphelin).

7. **Le scénario P3-11 doit-il tester les deux chemins ?**
   **Non pour le parcours nominal** — il doit emprunter le chemin `RegisterSaleUseCase`, seul chemin
   fonctionnel. Un scénario **optionnel** peut caractériser la limite du chemin manuel, mais sa valeur est
   faible : elle documente un défaut déjà connu plutôt qu'une règle métier. **Recommandation : ne pas le
   retenir en P3-11 ; l'inscrire comme dette pour P3-12.**

8. **Quelle commande est considérée comme le parcours principal P3 ?**
   La commande **issue d'une vente contenant des verres**, via `RegisterSaleUseCase`.

9. **Le lien `Sale ↔ Order` existe-t-il réellement ?**
   **Oui.** `Order.SaleId` (FK obligatoire) + `Sale.Orders` (collection) + `Order.Sale` (navigation requise).
   `IOrderRepository.GetBySaleIdAsync(saleId)` permet la navigation inverse.

10. **Quels identifiants permettent de retrouver la commande issue d'une vente ?**
    Directement : `RegisterSaleResult.OrderId` et `RegisterSaleResult.OrderNumber` (renseignés seulement si
    des verres sont présents ; `null` sinon). Indirectement : `GetBySaleIdAsync(SaleId)`.

11. **Une ambiguïté existe-t-elle entre commande fournisseur et ordre atelier ?**
    **Oui, et elle est structurelle.** La roadmap P3 §4.4 la nomme « double rôle d'`Order` ». La même entité
    est créée avec `Notes = "Commande verres pour vente {SaleNumber}"` (sémantique fournisseur) et porte
    ensuite le workflow d'atelier `New → … → Delivered` ainsi que les fiches atelier (sémantique fabrication).
    `Order.SupplierId` existe mais **n'est jamais renseigné** par `RegisterSaleUseCase`. **Conséquence P3-11 :**
    les scénarios doivent parler d'« ordre de fabrication » et ne rien asserter sur une sémantique fournisseur
    qui n'est pas alimentée.

12. **Quelle dette doit rester explicitement reportée ?**
    - `CreateOrderUseCase` produit une commande orpheline inexploitable (§7.5).
    - Le double rôle d'`Order` (§7.11).
    - `Order.SupplierId` jamais renseigné.
    - Une vente **sans commande** au solde non nul est **irréglable** (§6.2 B).
    - **Incohérence `ItemType` ⇄ `Category`** (§10.11) — la plus sérieuse.

---

## 8. Données minimales du scénario nominal

| Entité | Champs indispensables | Valeurs candidates | Justification |
|---|---|---|---|
| Fournisseur | `Name` | `"Optiverre Distribution"` | `CreateProductUseCase` refuse `SupplierId` nul ou introuvable. |
| Client | `FirstName`, `LastName` | `"Camille"`, `"Berger"` | `CustomerValidator` ; non archivé par défaut. |
| Ordonnance | `CustomerId`, `IssueDate`, optiques OD/OG | OD `Sph +2.00 / Cyl −1.00 / Axe 180 / Add 2.50` ; OG `Sph +1.75 / Cyl −0.75 / Axe 90` | Axe présent car cylindre non nul (invariant `PrescriptionValidator`). Add ⇒ progressif, cohérent avec l'atelier. |
| Produit monture | `Reference`, `Name`, `Category=MONTURE`, `SupplierId`, `PurchasePrice`, `SalePrice`, `StockQuantity`, `StockAlertThreshold` | `MON-1001`, stock `5`, seuil `2`, PV `120.00` | Décrémentée **à la vente**. Stock 5 > 1 pour rester au-dessus du seuil au nominal. |
| Produit verre OD | idem, `Category=VERRE` | `VER-OD-2001`, stock `4`, seuil `1`, PV `95.00` | Décrémenté **à la fabrication**. |
| Produit verre OG | idem, `Category=VERRE` | `VER-OG-2002`, stock `4`, seuil `1`, PV `95.00` | Référence **distincte** : `NormalizedReference` est unique (P3-4B). |
| Accessoire *(optionnel)* | `Category=CLIPS` | `ACC-3001`, stock `3` | À n'inclure que dans le scénario dédié « accessoire ». |
| Vente | `CustomerId`, 3 lignes, `DiscountAmount`, `DepositAmount`, `PaymentMethod` | Lignes : monture (`Frame`, q1, 120.00), verre OD (`LensOd`, q1, 95.00, optiques OD), verre OG (`LensOg`, q1, 95.00, optiques OG). Remise `10.00`, acompte `100.00` | Total `310.00` → final `300.00` → solde `200.00` → `PaymentStatus = Partial`. Toutes les branches monétaires sont exercées d'un coup. |
| Commande | *(aucune saisie)* | issue de la vente | Créée automatiquement car deux lignes verre. |
| Fiche atelier | *(aucune saisie)* | v1 | Créée automatiquement à `ToFabricate → InProgress`. |
| Utilisateur | **non requis** | — | §13 : aucun use case du parcours ne prend d'`UserId`. |

### 8.1 Règles réelles vérifiées

| Règle | Où elle vit | Statut |
|---|---|---|
| Client actif / non archivé | `RegisterSaleUseCase.AcquireCustomerAsync` (prise atomique) ; `CreatePrescriptionUseCase` (lecture) | ✅ opposée |
| Fournisseur existant | `CreateProductUseCase.SupplierNotFoundMessage` | ✅ |
| Produits actifs | `ProductRepository.TryAcquireActiveAsync` | ✅ atomique |
| Références uniques | index unique sur `NormalizedReference` (P3-4B) | ✅ base |
| Catégories/détails cohérents | `CreateProductUseCase` (détails par catégorie) | ✅ |
| Ordonnance valide | `PrescriptionValidator` via `CommandValidation` | ✅ exécuté depuis P3-3B |
| Valeurs optiques finies | `SaleLineOpticsPolicy.RequireFinite` | ✅ (lignes verre seules) |
| Cylindre ⇄ axe | `SaleLineOpticsPolicy` + `PrescriptionValidator` | ✅ (messages partagés) |
| Prisme ⇄ base | idem | ✅ |
| Stock suffisant | `EfStockMutationService.DecrementStockAsync` (`WHERE StockQuantity >= @qty`) | ✅ atomique |
| Prix et quantités | `SalePricingPolicy` (`Quantity > 0`, `UnitPrice >= 0`) | ✅ |
| Rôle utilisateur valide | `CreateUserCommand.Role` nullable + validation (P3-10) | ✅ — hors parcours |
| Mot de passe conforme | politique P3-10 sur les 3 chemins runtime | ✅ — hors parcours |

**Ne pas surcharger.** Sont volontairement **exclus** du nominal : détails verre (`GlassIndex`, `PowerLimit…`),
`RecommendedPrice`, `Notes`, `DoctorName`, `VisualAcuity` — aucun n'a d'effet sur un invariant du parcours.

---

## 9. Invariants du parcours nominal

| Étape | Invariant | Entité/table | Assertion par contexte neuf |
|---|---|---|---:|
| Client créé | `IsArchived = false` ; `ArchivedAt = null` | `Customers` | ✅ |
| Client créé | exactement 1 ligne, aucune donnée inattendue | `Customers` | ✅ |
| Ordonnance créée | `CustomerId` correct | `Prescriptions` | ✅ |
| Ordonnance créée | sphères/cylindres/additions **intacts** | `Prescriptions` | ✅ |
| Ordonnance créée | axe canonique (`0 → 180`), **aucune transposition destructive** | `Prescriptions` | ✅ |
| Produits créés | `SupplierId` renseigné et existant | `Products` | ✅ |
| Produits créés | `NormalizedReference` unique et normalisée | `Products` | ✅ |
| Produits créés | `IsActive = true`, `StockQuantity` = valeur initiale | `Products` | ✅ |
| Vente | `TotalAmount = Σ(Quantity × UnitPrice)` = `310.00` | `Sales` | ✅ |
| Vente | `DiscountAmount = 10.00` (borné ≤ total) | `Sales` | ✅ |
| Vente | `FinalAmount = 300.00` | `Sales` | ✅ |
| Vente | `DepositAmount = 100.00` (borné ≤ final) | `Sales` | ✅ |
| Vente | `RemainingAmount = 200.00` | `Sales` | ✅ |
| Vente | `PaymentStatus = Partial` | `Sales` | ✅ |
| Vente | `Status = AwaitingLenses` (verres présents) | `Sales` | ✅ |
| Vente | 3 lignes, quantités et prix conformes | `SaleItems` | ✅ |
| Vente | `TotalPrice` de chaque ligne **calculé** (≠ 0) | `SaleItems` | ✅ |
| Vente | optiques **copiées** sur les lignes verre, axe canonique | `SaleItems` | ✅ |
| Vente | `CustomerId` renseigné | `Sales` | ✅ |
| Vente | 1 commande créée, `SaleId` = vente, `Status = New` | `Orders` | ✅ |
| Vente | commande contient **2** lignes (verres **seuls**) | `OrderItems` | ✅ |
| Vente | stock monture `5 → 4` ; verres **inchangés** (`4`) | `Products` | ✅ |
| Vente | **1** mouvement (`Out`, `−1`, monture) | `StockMovements` | ✅ |
| Vente | ordonnance **inchangée** (aucun couplage) | `Prescriptions` | ✅ |
| Transitions | statut exact après chaque appel | `Orders.Status` | ✅ |
| Transitions | une seule transition effective par appel | `Orders.Status` | ✅ |
| Transitions | aucun saut possible (matrice) | — | ✅ (par refus) |
| Transitions | aucune double mutation sur rejeu | `Orders`, `StockMovements` | ✅ |
| Transitions | aucune notification en double | `Notifications` | ✅ |
| Fiche | exactement **1** fiche `IsCurrent = true` | `WorkshopSheets` | ✅ |
| Fiche | `Version = 1` | `WorkshopSheets` | ✅ |
| Fiche | snapshot **par valeur** (`OrderNumberSnapshot`, `CustomerNameSnapshot`, `ProductReferenceSnapshot`…) | `WorkshopSheets`, `WorkshopSheetItems` | ✅ |
| Fiche | ordonnance source **inchangée** | `Prescriptions` | ✅ |
| Fiche | données minimisées (aucun montant sur la fiche) | `WorkshopSheetItems` | ✅ |
| Fiche | `TechnicalFingerprint` non vide et cohérent | `WorkshopSheets` | ✅ |
| QC | `QcStatus = Passed`, `QcCompletedAt` renseigné | `WorkshopSheets` | ✅ |
| QC | fiche courante = fiche passée | `WorkshopSheets` | ✅ |
| QC | seconde décision refusée (`QcAlreadyDecidedMessage`) | — | ✅ |
| Ready | `Status = Ready` autorisé **après** QC seulement | `Orders` | ✅ |
| Livraison | `Status = Delivered` | `Orders` | ✅ |
| Livraison | aucune transition ultérieure (terminal) | — | ✅ (par refus) |
| Livraison | stock **non** redécrémenté | `Products`, `StockMovements` | ✅ |
| Règlement | `RemainingAmount = 0` | `Sales` | ✅ |
| Règlement | `DepositAmount = FinalAmount = 300.00` | `Sales` | ✅ |
| Règlement | `PaymentStatus = Paid` | `Sales` | ✅ |
| Règlement | exactement **1** notification `PaymentReceived` | `Notifications` | ✅ |
| Règlement | rejeu ⇒ `AlreadySettled`, aucun second paiement | `Sales`, `Notifications` | ✅ |

---

## 10. Stock et mouvements

### 10.1 Chronologie du scénario nominal

| Étape | Produit | Stock avant | Delta | Stock après | Mouvement attendu |
|---|---|---:|---:|---:|---|
| `RegisterSale` | Monture `MON-1001` | 5 | −1 | 4 | `Out`, `Quantity = −1`, `Reason = "Vente VTE-xxxxxx - Client #<id>"` |
| `RegisterSale` | Verre OD `VER-OD-2001` | 4 | 0 | 4 | **aucun** (catégorie `VERRE` ⇒ ignorée) |
| `RegisterSale` | Verre OG `VER-OG-2002` | 4 | 0 | 4 | **aucun** |
| `New → ToFabricate` | *(tous)* | — | 0 | — | **aucun** |
| `ToFabricate → InProgress` | Verre OD | 4 | −1 | 3 | `Out`, `Quantity = −1`, `Reason = "Fabrication commande CMD-xxxxxx"` |
| `ToFabricate → InProgress` | Verre OG | 4 | −1 | 3 | `Out`, `Quantity = −1`, `Reason = "Fabrication commande CMD-xxxxxx"` |
| `ToFabricate → InProgress` | Monture | 4 | 0 | 4 | **aucun** (la monture n'est pas dans l'`Order`) |
| `InProgress → QualityCheck` | *(tous)* | — | 0 | — | **aucun** |
| QC / `Ready` / `Delivered` | *(tous)* | — | 0 | — | **aucun** |
| `SettleOrderBalance` | *(tous)* | — | 0 | — | **aucun** |
| **Total** | | | | monture 4, verres 3/3 | **3 mouvements** |

### 10.2 Réponses

1. **Quel stock est décrémenté à la vente ?** Toute ligne dont le **produit** n'est pas de catégorie `VERRE`
   ou `LENTILLE` (`RegisterSaleUseCase.cs:330`).
2. **Quel stock est décrémenté pendant la fabrication ?** Tous les `OrderItems` de la commande portant un
   `ProductId` (`CreateStockMovementsForFabricationAsync`), **sans** filtre de catégorie.
3. **Quels produits ne sont jamais redécrémentés ?** Les montures et accessoires : ils ne sont jamais insérés
   dans l'`Order` (seules les lignes `LensOd`/`LensOg` le sont).
4. **Combien de mouvements doivent exister ?** 3 au nominal (1 vente + 2 fabrication).
5. **Quels signes ?** **Toujours négatifs** pour les sorties : `Quantity = -line.Quantity` dans les deux
   chemins (convention de signe P3-5).
6. **Quel motif ou type ?** `MovementType = StockMovementType.Out` dans les deux cas. `Reason` est un lien
   **textuel** (`"Vente {SaleNumber} - Client #{id}"` ou `"… - Vente sans client"` ; `"Fabrication commande
   {OrderNumber}"`). **Aucune FK vers la vente ou la commande n'existe** (dette D5 documentée).
7. **Les mouvements connaissent-ils l'auteur ?** `StockMovement.PerformedByUserId` **existe** (`long?`) mais
   n'est renseigné par **aucun** des trois chemins d'écriture (vente, fabrication, mouvement manuel). Toujours
   `null`.
8. **Verres ?** Jamais décrémentés à la vente ; décrémentés une fois à `ToFabricate → InProgress`.
9. **Monture ?** Décrémentée une fois à la vente ; jamais ensuite.
10. **Accessoire ?** Comme la monture (toute catégorie hors `VERRE`/`LENTILLE`).
11. **Comment prouver l'absence de double décrément ?** Relire `Products.StockQuantity` **et** compter les
    `StockMovements` par produit depuis un contexte neuf, après avoir rejoué chaque transition une seconde
    fois. La prise atomique de statut (`UPDATE … WHERE Status = @expected`) garantit qu'un rejeu lève
    `OrderStatusConflictException` **avant** tout décrément.
12. **Rollback sur stock insuffisant ?** Poser un stock verre à `0` avant `ToFabricate → InProgress` :
    `DecrementStockAsync` lève `InsufficientStockException`, `EfTransactionRunner` annule **tout** — statut
    resté `ToFabricate`, aucun mouvement, **aucune fiche atelier**, aucune notification. Assertions depuis un
    contexte neuf sur les 5 tables.

### 10.3 🔴 Incohérence `ItemType` ⇄ `Category` (constat de lecture de code)

`hasLenses` et l'appartenance à l'`Order` sont décidés par l'**`ItemType` de la ligne** ; le saut du décrément
de vente est décidé par la **`Category` du produit**. Ces deux clés ne sont **jamais** croisées : ni
`ValidateLines`, ni `NormalizeItemType`, ni `AcquireProductsAsync` ne vérifie leur cohérence. Deux
conséquences se déduisent du code :

| Combinaison | Dans l'`Order` ? | Décrémenté à la vente ? | Décrémenté à la fabrication ? | Effet net |
|---|---:|---:|---:|---|
| `ItemType=LensOd` + `Category=VERRE` | oui | non | oui | ✅ **1 fois** (nominal) |
| `ItemType=Frame` + `Category=MONTURE` | non | oui | non | ✅ **1 fois** (nominal) |
| `ItemType=LensOd` + `Category=MONTURE` | oui | **oui** | **oui** | ⚠️ **2 fois** |
| `ItemType=Frame` + `Category=VERRE` | non | non | non | ⚠️ **0 fois** |

> **Statut de ce constat : déduit de la lecture du code, non exécuté.** Il est précisément le genre de défaut
> qu'aucun test ciblé ne peut voir (il ne se manifeste qu'en traversant vente **puis** fabrication) et qu'un
> scénario de recette révèle immédiatement. **Recommandation : en faire un scénario P3-11 caractérisant le
> comportement actuel** (§24, scénario S9), sans corriger le code en P3-11. Si le scénario confirme le double
> décrément, il devient une dette **bloquante pour P3-12**, au même rang que la dette P3-8 (§26).

---

## 11. Fiche atelier et QC

1. **À quelle transition la première fiche est-elle créée ?**
   `ToFabricate → InProgress` (cas nominal). Un **filet de compatibilité** existe à
   `InProgress → QualityCheck` pour les commandes déjà `InProgress` au déploiement de P3-6B.

2. **Peut-elle être créée deux fois ?**
   **Non.** `WorkshopSheetOperations.EnsureCurrentSheetAsync` lit d'abord la version courante et ne crée que
   s'il n'y en a aucune — idempotent par nature. En cas de course, l'unicité `(OrderId, Version)` arbitre en
   conflit contrôlé plutôt qu'en doublon.

3. **Comment est calculée la version ?**
   Par `IOrderRepository.CreateNextWorkshopSheetVersionAsync(order, precondition, createdAt, ct)`, à partir de
   la version courante. La première est `Version = 1`.

4. **Comment une fiche devient-elle obsolète ?**
   Son `TechnicalFingerprint` (calculé par `WorkshopSheetFingerprint.Compute(order)`) cesse de correspondre à
   la commande. `WorkshopSheetOperations.IsUpToDate` compare les deux en `StringComparison.Ordinal`.

5. **Comment créer une nouvelle version ?**
   `GenerateWorkshopSheetUseCase`. Statuts autorisés : **`InProgress` et `QualityCheck` uniquement**
   (`WorkshopSheetPolicy.CanGenerateForStatus`). Refus si la commande n'a aucune ligne. La version courante
   lue est transmise en **précondition de concurrence** : deux demandes simultanées parties de la même version
   produisent une seule version suivante.

6. **Un QC `Failed` peut-il être remplacé ?**
   Pas sur la même fiche. Il faut **générer une nouvelle version**, qui repart `Pending` — *« le résultat de
   contrôle d'une version précédente n'est jamais recopié »*.

7. **Une décision QC est-elle modifiable ?**
   **Non — définitive.** `WorkshopSheetPolicy.CanTakeQcDecision(sheet.QcStatus)` n'accepte que `Pending` ;
   sinon `QcAlreadyDecidedMessage`.

8. **Comment la concurrence est-elle protégée ?**
   Double garde : (a) préconditions métier lues (version courante, fiche à jour, décision non prise) pour un
   **message précis** ; (b) `TryTakeWorkshopSheetQcDecisionAsync` — prise **atomique conditionnelle** qui
   tranche seule. Un échec de prise lève `QcAlreadyDecidedMessage`, aucune écriture conservée.
   Pour `QualityCheck → Ready`, `TryTransitionWithWorkshopSheetAsync` évalue statut **et** exigence de fiche
   dans **la même instruction** (`EXISTS`/`NOT EXISTS` corrélé).

9. **Le scénario nominal doit-il inclure une seule fiche ou un renouvellement ?**
   **Une seule fiche (v1, QC `Passed`).** Le renouvellement est une reprise d'atelier, pas le parcours
   nominal. Il mérite un scénario dédié (§24, S5b).

10. **Quel scénario négatif doit prouver le blocage avant `Ready` ?**
    `QualityCheck → Ready` sans décision QC ⇒ `BusinessRuleException(ReadyRequiresPassedQcMessage)`, statut
    resté `QualityCheck`, **aucune** notification créée (la garde précède l'écriture).

11. **Quel scénario doit prouver qu'une fiche obsolète bloque `Ready` ?**
    Modifier une donnée technique de la commande **après** un QC `Passed` (patron `ChangeOrderTechnicalData`),
    puis tenter `QualityCheck → Ready` ⇒ `ReadyRequiresUpToDateSheetMessage`.

12. **Comment vérifier que l'ordonnance source n'a pas été modifiée ?**
    Relire la ligne `Prescriptions` depuis un contexte neuf en fin de scénario et comparer champ à champ aux
    valeurs créées à l'étape 4. La fiche est un **snapshot par valeur** (`*Snapshot`) ; aucune écriture ne
    remonte vers l'ordonnance — d'autant qu'aucun lien ne les relie (§6.2 A).

---

## 12. Notifications

### 12.1 Notifications du parcours nominal

| Événement | Type | EntityType | EntityId | Nombre attendu | Résolution |
|---|---|---|---:|---:|---|
| `New → ToFabricate` | `OrderStatusChanged` | `Order` | `OrderId` | 1 | jamais résolue |
| `ToFabricate → InProgress` | `OrderStatusChanged` | `Order` | `OrderId` | 1 | jamais résolue |
| `InProgress → QualityCheck` | `OrderStatusChanged` | `Order` | `OrderId` | 1 | jamais résolue |
| `QualityCheck → Ready` | `OrderStatusChanged` | `Order` | `OrderId` | 1 | jamais résolue |
| `Ready → Delivered` | `OrderStatusChanged` | `Order` | `OrderId` | 1 | jamais résolue |
| Règlement du solde | `PaymentReceived` | `Order` | `OrderId` | 1 | jamais résolue |
| Décision QC | *(aucune)* | — | — | **0** | — |
| Génération de fiche | *(aucune)* | — | — | **0** | — |
| Stock bas | `LowStock` | `Product` | `ProductId` | **0 au nominal** | `ResolvedAt` posé par réconciliation |
| **Total nominal** | | | | **6** | |

### 12.2 Réponses

1. **Quelles transitions créent une notification ?** **Toutes**, sans exception, dès que
   `INotificationRepository` est fourni (il est **optionnel**, `null` par défaut). Le titre et le message
   utilisent `command.NextStatusDisplay` / `CurrentStatusDisplay` / `CustomerDisplayName` — des libellés
   **fournis par l'appelant**, non dérivés du domaine. Les scénarios doivent donc les passer explicitement.
2. **Le règlement crée-t-il une notification ?** **Oui** — `PaymentReceived`, et **uniquement si** la prise
   atomique a réussi. Un rejeu ne notifie pas.
3. **Le stock bas doit-il être généré explicitement ?** **Oui.** `GenerateLowStockNotificationsUseCase` est un
   **réconciliateur** qu'aucun autre use case n'appelle. Ni la vente ni la fabrication ne le déclenchent.
4. **Une vente peut-elle faire passer le stock sous le seuil sans créer l'alerte ?** **Oui — c'est le
   comportement normal et voulu.** L'alerte n'apparaîtra qu'au prochain appel de la réconciliation.
5. **Le scénario P3-11 doit-il appeler la réconciliation ?** **Pas dans le parcours nominal** (le stock y reste
   au-dessus du seuil, cf. §8). Oui dans un scénario **secondaire** dédié (§24, S8).
6. **Quel test prouve l'absence de doublon après retry ?** Deux appels consécutifs de la réconciliation, même
   état de stock ⇒ compter les `Notifications` de type `LowStock` non résolues pour le produit : **1**.
   ⚠️ Ce test n'est **pleinement représentatif que sur une base migrée** : l'index unique filtré est créé par
   SQL de migration et **absent** d'une base `EnsureCreated` (§5.2 Q7).
7. **Quel test prouve la résolution après réapprovisionnement ?** `CreateStockMovementUseCase` (type `In`) pour
   repasser au-dessus du seuil, puis réconciliation ⇒ `ResolvedAt` non nul sur l'alerte.
8. **Parcours principal ou secondaire ?** **Secondaire.** Le stock bas est un cycle de vie propre, orthogonal
   au parcours opticien.

> **La conception P3-8 n'est pas rouverte.** Les points ci-dessus décrivent le comportement existant.

---

## 13. Utilisateur et attribution

1. **Le parcours backend exige-t-il une authentification ?** **Non.** Aucun use case du parcours ne consulte
   `IAuthenticationService` ni aucune session.
2. **Les use cases prennent-ils un `UserId` explicite ?** **Aucun.** Ni `RegisterSaleCommand`, ni
   `AdvanceOrderStatusCommand`, ni `SettleOrderBalanceCommand`, ni `ValidateWorkshopSheetQcCommand`, ni
   `CreateStockMovementCommand` ne porte d'identifiant d'auteur.
3. **La vente exige-t-elle un `StaffId` ?** **Non.** `Sale.StaffId` (`long?`) et `Sale.Staff` existent, mais
   `RegisterSaleUseCase` ne les renseigne **jamais** ⇒ toujours `null`.
4. **Les mouvements de stock exigent-ils un auteur ?** **Non.** `StockMovement.PerformedByUserId` (`long?`) et
   `PerformedByUser` existent, jamais renseignés par aucun des trois chemins ⇒ toujours `null`.
5. **Le QC porte-t-il l'auteur ?** **Non**, et c'est explicite dans le code : *« Aucun auteur enregistré en V1.
   L'identité de l'utilisateur courant n'est pas disponible proprement côté Application (elle vit dans
   `ISessionService`, en UI) … Le champ est donc volontairement absent plutôt que faux — report explicite. »*
6. **Une session UI est-elle nécessaire ?** **Non.** `ISessionService` vit dans `MMV.App`, jamais atteint
   depuis Application.
7. **Peut-on exécuter le scénario sans UI ?** **Oui, intégralement.** C'est le résultat le plus important de
   cette section : **aucun blocage P3-11 ne provient de l'utilisateur.**
8. **Quelles attributions doivent être vérifiées ?** Seulement des **absences**, en assertion de
   non-régression : `Sale.StaffId == null`, `StockMovement.PerformedByUserId == null` — pour figer le fait
   qu'aucune valeur fausse n'est inventée.
9. **Quelles attributions sont absentes du modèle ?** L'auteur du QC (aucune colonne), l'auteur d'une
   transition de statut, l'auteur d'une fiche atelier.
10. **L'auteur du QC reste-t-il reporté ?** **Oui.** La roadmap le renvoyait à P3-10 ; P3-10 a livré des
    comptes locaux sûrs mais **n'a pas** introduit de propagation d'identité vers Application. Le report reste
    entier et doit être réinscrit pour P3-12 / redesign UI.

> **`ICurrentUser` n'est pas introduit** et ne doit pas l'être en P3-11.

---

## 14. Scénario nominal obligatoire

### 14.1 Nom retenu

```
ParcoursOpticien_DeLaCreationClientAuReglementDuSolde_EstCoherentDeBoutEnBout
```

Le nom évite « Complet » (le parcours n'est pas complet au sens du modèle : §6.2) et nomme les deux bornes
réelles du parcours exécutable par le code.

### 14.2 Propriétés obligatoires

| Propriété | Comment elle est tenue |
|---|---|
| Lisible comme une histoire métier | Un `// --- Acte N : …` par étape, dans l'ordre du parcours ; aucun helper géant. |
| Vraie base SQLite jetable | Fichier unique sous `%TEMP%/mmv-p3-11-<guid>/`, `Pooling=False`. |
| Construit depuis le schéma **migré** | `context.Database.Migrate()` (et non `EnsureCreated`) — §5.2 Q7. |
| Uniquement via use cases publics | Tous les actes passent par les use cases ; **aucune** mutation directe par dépôt. |
| Pas d'appel direct aux dépôts pour les mutations | Les dépôts ne sont instanciés que pour **composer** les use cases. |
| Relu par contexte neuf | Les `DbContext` d'acte sont libérés ; les assertions ouvrent un contexte neuf en `AsNoTracking()`. |
| Sans UI | Aucune référence à `MMV.App` (déjà impossible : le projet de test ne le référence pas). |
| Sans seed de démonstration | `DatabaseSeeder` n'est jamais appelé ; les données viennent des use cases du scénario. |
| Sans temps réel ni `Thread.Sleep` | Aucune attente. Les dates ne sont comparées que par **bornes** (§21 Q6). |
| Déterministe | Aucun parallélisme interne, aucun `Guid` dans les assertions, ordre des lignes fixé par `Position`/`ProductId`. |

### 14.3 Actes et assertions

**Arrange** — *(par use cases)*
1. `CreateSupplierUseCase` → `supplierId`.
2. `CreateProductUseCase` ×3 → `frameId`, `lensOdId`, `lensOgId` (stocks 5 / 4 / 4).
3. `CreateCustomerUseCase` → `customerId`.

**Act**
4. `CreatePrescriptionUseCase(customerId, optiques OD/OG)` → `prescriptionId`.
5. `RegisterSaleUseCase` (3 lignes, remise 10.00, acompte 100.00) → `saleId`, `orderId`, `orderNumber`.
6. `AdvanceOrderStatusUseCase` `New → ToFabricate`.
7. `AdvanceOrderStatusUseCase` `ToFabricate → InProgress`.
8. `AdvanceOrderStatusUseCase` `InProgress → QualityCheck`.
9. `GetCurrentWorkshopSheetUseCase(orderId)` → `workshopSheetId`.
10. `ValidateWorkshopSheetQcUseCase(workshopSheetId, "Montage conforme")`.
11. `AdvanceOrderStatusUseCase` `QualityCheck → Ready`.
12. `AdvanceOrderStatusUseCase` `Ready → Delivered`.
13. `SettleOrderBalanceUseCase(orderId)` → `AmountEncashed = 200.00`.

**Assert** — *(contexte neuf unique, `AsNoTracking`)*
- **Vente** : `TotalAmount = 310.00`, `DiscountAmount = 10.00`, `FinalAmount = 300.00`,
  `DepositAmount = 300.00`, `RemainingAmount = 0`, `PaymentStatus = Paid`, `Status = AwaitingLenses`,
  `StaffId = null`.
- **Lignes** : 3 lignes ; `TotalPrice` de chacune = `Quantity × UnitPrice` ; optiques copiées sur les deux
  lignes verre, axe canonique.
- **Commande** : 1 seule, `SaleId = saleId`, `Status = Delivered`, **2** `OrderItems` (verres seuls).
- **Fiche** : exactement 1 `IsCurrent = true`, `Version = 1`, `QcStatus = Passed`, `QcCompletedAt` non nul,
  snapshots renseignés, `TechnicalFingerprint` non vide.
- **Ordonnance** : identique champ à champ à l'étape 4 (aucune modification collatérale).
- **Stock** : monture `4`, verre OD `3`, verre OG `3`.
- **Mouvements** : exactement **3**, tous `Out` et `Quantity < 0` ; 1 pour la monture (motif contenant le
  `SaleNumber`), 2 pour les verres (motif contenant l'`OrderNumber`) ; `PerformedByUserId = null` partout.
- **Notifications** : exactement **6** — 5 `OrderStatusChanged` + 1 `PaymentReceived`, toutes
  `EntityType = "Order"`, `EntityId = orderId` ; **0** `LowStock`.

---

## 15. Scénarios de refus obligatoires

Classement selon la couverture ciblée **existante**. « Recette » = à remonter au niveau P3-11 parce que le
refus n'a de sens qu'en traversée ; « Ciblé » = déjà suffisamment prouvé par un test unitaire ou d'intégration
existant, à **ne pas** dupliquer.

| Domaine | Refus | Couverture ciblée existante | Niveau P3-11 |
|---|---|---|---|
| Client | archivé à la création d'ordonnance | `CreatePrescriptionUseCaseTests` | Ciblé |
| Client | archivé à la vente | `RegisterSaleBusinessRulesTests` | **Recette** (S3 : prouve que rien n'est écrit dans **aucune** table du parcours) |
| Ordonnance | cylindre/axe incohérent | `PrescriptionValidator` tests + `SaleLineOpticsPolicy` tests | Ciblé |
| Ordonnance | prisme/base incohérent | idem | Ciblé |
| Ordonnance | valeur non finie | idem | Ciblé |
| Ordonnance | partielle valide (OD seul) | `CreatePrescriptionUseCaseTests` | Ciblé |
| Produit | inactif | `RegisterSaleBusinessRulesTests`, `ProductSelectorActiveFilterTests` | Ciblé |
| Produit | fournisseur inexistant | `ProductSupplierGuardTests` | Ciblé |
| Produit | référence dupliquée | `ProductBusinessRulesTests` | Ciblé |
| Produit | catégorie/détails incohérents | `ProductBusinessRulesTests` | Ciblé |
| **Stock** | **insuffisant à la fabrication ⇒ rollback transversal** | partiellement (`AdvanceOrderStatusUseCaseTests`) | **Recette** (S4 : prouve statut + stock + mouvements + **fiche** + notification tous annulés) |
| Stock | insuffisant à la vente ⇒ aucune vente/commande/mouvement partiel | `RegisterSaleBusinessRulesTests.AssertNothingPersisted` | Ciblé (déjà transversal) |
| Vente | quantité nulle | `SalePricingPolicy` tests | Ciblé |
| Vente | prix négatif | idem | Ciblé |
| Vente | acompte > montant final | idem | Ciblé |
| Vente | montants appelant falsifiés | `RegisterSaleBusinessRulesTests` | Ciblé |
| Vente | client inexistant | `RegisterSaleBusinessRulesTests` | Ciblé |
| Commande | saut de statut | `AdvanceOrderStatusUseCaseTests` (matrice) | Ciblé |
| Commande | retour arrière | idem | Ciblé |
| Commande | même statut | idem | Ciblé |
| Commande | **transition après `Delivered`** | idem | **Recette** (intégré à S1 : terminalité en fin de parcours réel) |
| Commande | modification après `InProgress` | `UpdateOrderUseCaseTests` | Ciblé |
| Commande | suppression après `New` | `DeleteOrderUseCaseTests` | Ciblé |
| **Atelier** | **`Ready` sans QC** | `AdvanceOrderStatusWorkshopSheetTests` | **Recette** (S5 : blocage **puis** réussite après QC, sur le parcours réel) |
| Atelier | QC `Failed` bloque `Ready` | `WorkshopSheetQcUseCaseTests` | Ciblé (S5b optionnel) |
| Atelier | fiche obsolète bloque `Ready` | `AdvanceOrderStatusWorkshopSheetTests` | Ciblé (S5b optionnel) |
| Atelier | seconde décision sur la même fiche | `WorkshopSheetQcUseCaseTests` | Ciblé |
| Atelier | rejeu de génération | `GenerateWorkshopSheetUseCaseTests` | Ciblé |
| **Paiement** | **règlement déjà effectué / rejeu** | `SettleOrderBalanceAtomicityTests` | **Recette** (S7 : rejeu **après** un parcours complet, pas sur une vente fabriquée à la main) |
| Paiement | solde nul | `SettleOrderBalanceUseCaseTests` | Ciblé |
| Utilisateur | compte inactif | `UserLifecycleTests` | Ciblé — **hors parcours** |
| Utilisateur | mot de passe faible | `UserPasswordPolicyTests` | Ciblé — **hors parcours** |
| Utilisateur | login de casse différente déjà pris | `UserIdentityAndRoleTests` | Ciblé — **hors parcours** |

**Conclusion.** Sur 33 refus recensés, **27 sont déjà suffisamment couverts** par des tests ciblés et ne
doivent pas être recopiés. Seuls **6** justifient une remontée en recette, tous parce que leur valeur réside
dans la **traversée** et non dans la règle elle-même.

---

## 16. Scénarios de rollback

| Scénario | Écriture 1 | Écriture 2 | Point d'échec | État attendu après rollback |
|---|---|---|---|---|
| **R1 — Vente atomique** | `Sale` + `SaleItems` + numéro de vente | `Order` + `OrderItems` + décréments + `StockMovements` | `DecrementStockAsync` (stock monture insuffisant) | 0 vente, 0 ligne, 0 commande, 0 mouvement, stock inchangé, **séquence de vente non consommée** |
| **R2 — Fabrication atomique** ⭐ | `Orders.Status = InProgress` (prise atomique) + `WorkshopSheet` v1 + `WorkshopSheetItems` | décréments verres + `StockMovements` + `Notification` | `DecrementStockAsync` (verre OG à 0) | statut resté `ToFabricate`, **aucune fiche**, stock verre OD **inchangé** (le décrément OD déjà appliqué est annulé), 0 mouvement, 0 notification |
| **R3 — QC + statut** | `WorkshopSheets.QcStatus` | *(aucune seconde écriture)* | — | **Pas un rollback transversal** : `ValidateWorkshopSheetQc` est mono-écriture. **Écarté.** |
| **R4 — Règlement + notification** | `Sales` (solde) via `ExecuteUpdate` | `Notification` | échec de la notification | solde **non** réglé, aucune notification. Déjà prouvé par `SettleOrderBalanceAtomicityTests` — **écarté** (pas de plus-value transversale). |
| **R5 — Produit + fournisseur** | `Product` | `GlassDetail`/`AccessoryDetail` | fournisseur introuvable | aucun produit, aucun détail. Déjà couvert par `ProductSupplierGuardTests` — **écarté**. |
| **R6 — Utilisateur + unicité** | `User` | index unique `NormalizedUsername` | collision | aucun utilisateur. Déjà couvert par `UserIdentityAndRoleTests` — **écarté**. |

**Retenus pour P3-11 : R1 et R2.** R2 (⭐) est le plus précieux : c'est le **seul** endroit du système où une
transition de statut, une création de fiche atelier, plusieurs décréments de stock, des mouvements et une
notification partagent **une seule** frontière transactionnelle. Prouver que la fiche atelier disparaît avec
le rollback est un invariant que **aucun** test ciblé actuel n'observe. R1 est largement couvert par
`AssertNothingPersisted` ; il est conservé en **assertion intégrée** à S4 plutôt qu'en scénario distinct.

---

## 17. Idempotence et retry

| Acte | Premier appel | Second appel identique | État final | Notification supplémentaire |
|---|---|---|---|---:|
| Réconciliation stock bas | ouvre l'alerte manquante | aucune ouverture (`ResolvedAt IS NULL` fait foi) | 1 alerte active | **0** |
| Transition de commande | statut avancé, effets appliqués | `OrderStatusConflictException` (prise atomique échoue) | statut avancé une fois | **0** |
| Génération de fiche (auto) | crée v1 | `EnsureCurrentSheetAsync` ne crée rien | 1 fiche courante | **0** |
| Génération de fiche (explicite) | crée v(n+1), bascule la précédente | 2ᵉ appel légitime ⇒ crée v(n+2) — **non idempotent par conception** | versions successives | **0** |
| Décision QC | `Pending → Passed` | `BusinessRuleException(QcAlreadyDecidedMessage)` | 1 décision | **0** |
| Règlement du solde | solde à 0, `Paid`, 1 notification | `AlreadySettled = true`, **aucune** écriture | solde 0 | **0** |
| Désactivation (produit/utilisateur) | `IsActive = false` | idem, sans effet | inactif | **0** |
| Création avec clé unique | créé | rejet par index unique | 1 ligne | **0** |

**Retenus pour P3-11 :**
- **Rejeu de transition** (intégré à S6) — le seul rejeu capable de produire un **double décrément de stock**
  s'il n'était pas gardé. Valeur transversale maximale.
- **Rejeu de règlement** (S7) — après un parcours complet, pour prouver l'absence de second encaissement **et**
  de seconde notification.
- **Rejeu de réconciliation stock bas** (S8, optionnel) — attention à la réserve §12.2 Q6 (index unique absent
  d'une base `EnsureCreated`, argument supplémentaire pour `Migrate()`).

**Écartés :** génération de fiche (déjà prouvée), décision QC (déjà prouvée), désactivation et clé unique
(sans traversée).

---

## 18. Concurrence multi-poste

Aucun `Thread.Sleep`, aucun test fondé sur la durée, aucune prétention sur PostgreSQL / SQL Server. Le patron
déterministe existant est : **deux `OpticDbContext` distincts sur le même fichier SQLite**, actes séquencés
explicitement par le test.

| Course | Protection | Test ciblé existant | Valeur ajoutée d'un scénario P3-11 |
|---|---|---|---|
| Deux ventes sur le même stock | `DecrementStockAsync` (`WHERE StockQuantity >= @qty`) | `RegisterSaleBusinessRulesTests`, `RegisterSaleProductAcquisitionOrderTests` | **Faible** — la garde est locale au décrément. Écarté. |
| **Deux transitions sur la même commande** | `TryTransitionStatusAsync` (`WHERE Status = @expected`) | `AdvanceOrderStatusUseCaseTests` | **Élevée** — c'est la seule course dont l'échec produirait un **double décrément de stock verres** et une **double fiche**. **Retenue (S6).** |
| Deux créations de fiche/version | unicité `(OrderId, Version)` + précondition de version | `WorkshopSheetConcurrencyHardeningTests` | Faible — déjà prouvé finement. Écarté. |
| Deux décisions QC | `TryTakeWorkshopSheetQcDecisionAsync` | `WorkshopSheetQcUseCaseTests` | Faible. Écarté. |
| **Deux règlements** | `TrySettleRemainingBalanceAsync` (`WHERE RemainingAmount > 0`) | `SettleOrderBalanceAtomicityTests` | **Moyenne** — la plus-value est de le prouver sur une vente **issue du parcours réel** (montants calculés par `SalePricingPolicy`), pas sur une vente fabriquée à la main. **Retenue, intégrée à S7.** |
| Deux créations d'utilisateur, même login | index unique `NormalizedUsername` | `UserIdentityAndRoleTests` | Nulle — hors parcours. Écarté. |
| Suppression fournisseur vs création produit | garde P3-9 | `SupplierBusinessRulesTests`, `ProductSupplierGuardTests` | Faible — hors parcours nominal. Écarté. |

**Bilan : 2 courses retenues sur 7.** Le principe directeur est de ne conserver une course que si son échec
produirait une incohérence **entre domaines** (stock ⇄ commande ⇄ atelier ⇄ paiement), jamais à l'intérieur
d'un seul.

---

## 19. Frontière des scénarios

### 19.1 Ce que P3-11 doit tester (niveau recette)

- **Orchestration de plusieurs use cases** dans l'ordre réel du métier.
- **Cohérence entre domaines** : vente ⇄ commande ⇄ stock ⇄ atelier ⇄ notification ⇄ paiement.
- **État final observable** relu depuis un contexte neuf.
- **Rollback transversal** franchissant une frontière de domaine (R2).
- **Idempotence de parcours** : rejouer une étape au milieu d'un parcours réel.
- **Conservation de l'historique** : ce qui a été écrit reste, et rien de collatéral n'est modifié.

### 19.2 Ce que P3-11 ne doit **pas** retester

- Chaque branche interne d'un validateur (`PrescriptionValidator`, `UserValidator`, `SupplierValidator`).
- Chaque valeur de frontière numérique (`SalePricingPolicy` : signes, bornes, dépassement `decimal`).
- Chaque méthode de dépôt isolée.
- Chaque détail de migration (`ProductNormalizedReferenceMigrationTests`).
- Chaque garde d'architecture (`ApplicationArchitectureTests` et consorts).
- Chaque code d'erreur SQLite étendu (`PersistenceErrorMapper`).

> **Règle d'arbitrage retenue.** Un invariant appartient à P3-11 **si et seulement si** l'observer exige
> d'avoir traversé **au moins deux** use cases appartenant à **deux** domaines différents. Sinon il appartient
> au test ciblé, où il est déjà. Les 1413 tests existants ne doivent pas être recopiés.

---

## 20. Support de test

| Option | Réalisme | Coût | Couplage | Recommandation |
|---|---:|---:|---:|---|
| **Test Application + `DbContext` réel** | **Élevé** | **Faible** | **Faible** | ✅ **RETENUE** — patron déjà éprouvé par ~30 fichiers ; compose les use cases exactement comme la portée DI de production. |
| Test via conteneur DI complet | Moyen | Élevé | **Très élevé** | ❌ Le conteneur vit dans `MMV.App` avec Avalonia, ViewModels, session, navigation. Le reconstruire = seconde application. |
| Test via ViewModels | Faible | Élevé | Très élevé | ❌ Ce serait un test d'UI ; le prompt l'exclut et P2C/P2D ont précisément sorti la logique des ViewModels. |
| Test via dépôts directs | **Nul pour les actes** | Faible | Faible | ❌ pour les actes (contournerait toutes les règles P3). ✅ **pour les lectures d'assertion uniquement.** |
| Nouveau projet `Acceptance` | Élevé | Moyen | Faible | ❌ Aucune nécessité prouvée : `MMV.Application.Tests` référence déjà Domain + Application + Infrastructure + SQLite. Un projet de plus ajoute CI, `.csproj`, duplication de helpers, sans gain. |

### 20.1 Contraintes respectées

| Contrainte | Respect |
|---|---|
| Aucune UI | ✅ le projet ne référence pas `MMV.App` |
| Aucune mutation directe par dépôt dans les actes | ✅ règle de convention (§25) |
| SQLite réel | ✅ `Microsoft.EntityFrameworkCore.Sqlite` 8.0.27 déjà présent |
| Migrations réelles préférées | ✅ `Migrate()` retenu (§5.2 Q7) |
| Assertions finales par lecture directe autorisées | ✅ contexte neuf `AsNoTracking()` |
| Pas d'EF InMemory | ✅ jamais utilisé dans le dépôt |
| Pas de seed de démonstration | ✅ `DatabaseSeeder` jamais appelé |
| Aucune dépendance réseau | ✅ |

### 20.2 Emplacement décidé

```
tests/MMV.Application.Tests/Acceptance/
```

**`Acceptance/` plutôt que `Scenarios/`** : le mot dit ce que ces tests sont (une **recette**), et `Scenarios`
se confondrait avec les « cas de test » des dossiers `UseCases/`. Le dossier est frère de `UseCases/`,
`Architecture/` et `Migrations/`, marquant clairement un niveau différent. **Aucun projet supplémentaire n'est
créé.**

---

## 21. Construction de l'environnement

### 21.1 Setup d'un scénario

1. **Base jetable** — répertoire `%TEMP%/mmv-p3-11-<guid32>/`, **un fichier `.db` par test**, chaîne
   `Data Source={path};Pooling=False`. Clés étrangères **activées** (le parcours par
   `RegisterSaleUseCase` ne produit **jamais** d'`Order` orpheline, contrairement aux tests P3-6/6B qui
   devaient les désactiver — c'est un gain de réalisme propre à P3-11).
2. **Migrations** — `context.Database.Migrate()` dans un contexte dédié, puis libération.
3. **Conteneur** — **aucun**. Une méthode `CreateUseCases(OpticDbContext)` instancie les dépôts et primitives
   autour du contexte partagé, reproduisant `AddScoped`.
4. **Services** — un `OpticDbContext` par « portée » simulée ; une portée par acte ou par groupe d'actes.
5. **Données de référence** — créées **par use cases** (§14.3, Arrange).
6. **Actes** — appels de use cases dans l'ordre métier.
7. **Libération de la portée** — `Dispose()` de tous les `DbContext` d'acte.
8. **Nouvelle portée** — `DbContext` neuf sur le même fichier.
9. **Assertions finales** — lectures `AsNoTracking()`.
10. **Suppression** — `SqliteConnection.ClearAllPools()` puis `Directory.Delete(recursive: true)` best-effort
    dans `Dispose()`.

### 21.2 Réponses

1. **Migrations à chaque test ?** **Oui.** Coût mesuré à l'échelle du dépôt : 14 migrations sur un fichier
   SQLite local, de l'ordre de 100–300 ms. Pour ~8 scénarios, c'est ≤ 3 s — négligeable face aux 15 s
   actuelles de `MMV.Application.Tests`. La simplicité et l'isolation valent ce coût.
2. **Une fixture de classe peut-elle partager une base ?** Techniquement oui (`IClassFixture<T>`).
   **Recommandation : non.**
3. **Le partage introduirait-il un ordre de tests ?** **Oui**, et c'est rédhibitoire : xUnit ne garantit pas
   l'ordre d'exécution au sein d'une classe. Un parcours nominal et un scénario de rollback partageant une
   base produiraient des échecs dépendants de l'ordre. **Une base par test, sans exception.**
4. **Comment éviter le seed de production ?** Ne jamais appeler `DatabaseSeeder`, `SeedOptionsResolver` ni
   `DbInitializer`. `Migrate()` seul n'insère aucune donnée de démonstration. À vérifier par une assertion de
   garde en tête du scénario nominal : `Customers`, `Products`, `Sales`, `Orders` vides après `Migrate()`.
5. **Comment contrôler l'horloge non injectable ?** **On ne la contrôle pas.** Le code mélange délibérément
   `DateTime.Now` (vente, notifications, mouvement de vente) et `DateTime.UtcNow` (fiche, QC, mouvement de
   fabrication) — point dur transverse n° 5 de la roadmap, **hors P3**. La seule stratégie sûre est de ne
   jamais comparer un timestamp à une valeur exacte.
6. **Quels timestamps ne doivent pas être comparés exactement ?** **Tous** : `Sale.SaleDate`,
   `Sale.EstimatedDelivery`, `Order.OrderDate`, `Order.EstimatedDelivery`, `WorkshopSheet.CreatedAt`,
   `WorkshopSheet.QcCompletedAt`, `StockMovement.CreatedAt`, `Notification.CreatedAt`, `Notification.ResolvedAt`.
   Assertions autorisées : **non-nullité**, **ordre relatif** entre deux timestamps de même horloge, et bornes
   larges (`BeOnOrAfter(avant)` / `BeOnOrBefore(après)`) capturées autour de l'acte. ⚠️ Ne **jamais** comparer
   un `Now` à un `UtcNow` : sur une machine non-UTC (le cas ici, Windows/Europe), l'écart est structurel.
7. **Comment capter les identifiants créés ?** Par les résultats des use cases :
   `CreateXxxResult.XxxId`, `RegisterSaleResult.{SaleId, OrderId, OrderNumber, RemainingAmount}`,
   `GetCurrentWorkshopSheetUseCase` pour le `WorkshopSheetId`. **Jamais** par une requête « le dernier créé ».
8. **Comment reconstruire les portées ?** Une méthode `WithScope(path, async useCases => { … })` qui crée le
   contexte, compose les use cases, exécute et libère — sans devenir un conteneur.
9. **Comment éviter les entités suivies périmées ?** C'est le risque n° 1 de ces scénarios : toutes les
   primitives atomiques (`TryTransitionStatusAsync`, `TrySettleRemainingBalanceAsync`, `DecrementStockAsync`,
   `TryAcquireActiveAsync`) passent par `ExecuteUpdateAsync` et **ne mettent pas à jour le change tracker**.
   Trois règles : (a) portée neuve entre les grands actes ; (b) **toujours** `AsNoTracking()` en assertion ;
   (c) ne jamais réutiliser une entité renvoyée par un résultat de use case comme source de vérité — le code de
   production réaligne certaines valeurs à la main (`SettleOrderBalanceUseCase.cs:152-154`,
   `AdvanceOrderStatusUseCase.cs:193`), ce qui est destiné à l'affichage, pas à l'assertion.
10. **Comment garantir l'isolation parallèle ?** Chaque classe de test xUnit est une collection distincte
    exécutée en parallèle avec les autres par défaut ; comme chaque test possède **son** fichier, l'isolation
    est structurelle. Aucun `[Collection]` ni `DisableTestParallelization` n'est nécessaire.

---

## 22. Tests existants réutilisables

| Invariant P3-11 | Test actuel | Chemin | Suffisant seul ? |
|---|---|---|---:|
| Client archivé refusé (ordonnance) | `CreatePrescriptionUseCaseTests` | `tests/MMV.Application.Tests/UseCases/Prescriptions/CreatePrescriptionUseCaseTests.cs` | ✅ |
| Client archivé refusé (vente) | `RegisterSaleBusinessRulesTests` | `…/UseCases/Sales/RegisterSaleBusinessRulesTests.cs` | ⚠️ prouve le refus, pas l'absence d'effet sur commande/atelier |
| Archivage / désarchivage | `SetCustomerArchivedUseCaseTests` | `…/UseCases/Customers/SetCustomerArchivedUseCaseTests.cs` | ✅ |
| Suppression client avec historique | `DeleteCustomerUseCaseTests` | `…/UseCases/Customers/DeleteCustomerUseCaseTests.cs` | ✅ |
| Validation ordonnance | `CreatePrescriptionUseCaseTests`, `UpdatePrescriptionUseCaseTests` | `…/UseCases/Prescriptions/` | ✅ |
| Règles produit (référence, catégorie) | `ProductBusinessRulesTests` | `…/UseCases/Products/ProductBusinessRulesTests.cs` | ✅ |
| Produit ⇄ fournisseur | `ProductSupplierGuardTests` | `…/UseCases/Products/ProductSupplierGuardTests.cs` | ✅ |
| Règles fournisseur | `SupplierBusinessRulesTests` | `…/UseCases/Suppliers/SupplierBusinessRulesTests.cs` | ✅ |
| Décrément sûr / mouvements | `CreateStockMovementUseCaseTests` | `…/UseCases/Stock/CreateStockMovementUseCaseTests.cs` | ✅ |
| Montants de vente, `PaymentStatus` | `RegisterSaleBusinessRulesTests` (1198 l.) | `…/UseCases/Sales/RegisterSaleBusinessRulesTests.cs` | ✅ |
| Aucune écriture après refus de vente | `AssertNothingPersisted` (idem) | idem | ✅ **patron à réutiliser** |
| Ordre d'acquisition déterministe | `RegisterSaleProductAcquisitionOrderTests` | `…/UseCases/Sales/RegisterSaleProductAcquisitionOrderTests.cs` | ✅ |
| Entité suivie périmée | `RegisterSaleTrackedStaleEntityTests` | `…/UseCases/Sales/RegisterSaleTrackedStaleEntityTests.cs` | ✅ **patron à réutiliser** |
| Matrice de transitions | `AdvanceOrderStatusUseCaseTests` (627 l.) | `…/UseCases/Orders/AdvanceOrderStatusUseCaseTests.cs` | ✅ |
| Verrouillage d'édition après `InProgress` | `UpdateOrderUseCaseTests` | `…/UseCases/Orders/UpdateOrderUseCaseTests.cs` | ✅ |
| Suppression de commande | `DeleteOrderUseCaseTests` | `…/UseCases/Orders/DeleteOrderUseCaseTests.cs` | ✅ |
| Fiche : génération, versions | `GenerateWorkshopSheetUseCaseTests` (509 l.) | `…/UseCases/WorkshopSheets/GenerateWorkshopSheetUseCaseTests.cs` | ✅ |
| Fiche : concurrence | `WorkshopSheetConcurrencyHardeningTests` (518 l.) | `…/UseCases/WorkshopSheets/WorkshopSheetConcurrencyHardeningTests.cs` | ✅ **patron multi-contexte** |
| QC : décisions, obsolescence | `WorkshopSheetQcUseCaseTests` | `…/UseCases/WorkshopSheets/WorkshopSheetQcUseCaseTests.cs` | ✅ |
| Fiche auto à l'entrée en fabrication | `AdvanceOrderStatusWorkshopSheetTests` | `…/UseCases/WorkshopSheets/AdvanceOrderStatusWorkshopSheetTests.cs` | ⚠️ ne part pas d'une vente réelle |
| Règlement atomique | `SettleOrderBalanceAtomicityTests` | `…/UseCases/Orders/SettleOrderBalanceAtomicityTests.cs` | ⚠️ vente fabriquée à la main |
| Notifications d'événement | `EventNotificationsNonRegressionTests` | `…/UseCases/Notifications/EventNotificationsNonRegressionTests.cs` | ✅ |
| Stock bas : réconciliation | `LowStockReconciliationTests` (487 l.) | `…/UseCases/Notifications/LowStockReconciliationTests.cs` | ✅ |
| Stock bas : concurrence + N+1 | `LowStockConcurrencyAndQueryCountTests` | `…/UseCases/Notifications/LowStockConcurrencyAndQueryCountTests.cs` | ✅ |
| Utilisateurs : identité, rôle | `UserIdentityAndRoleTests` (524 l.) | `…/UseCases/Users/UserIdentityAndRoleTests.cs` | ✅ |
| Utilisateurs : mot de passe | `UserPasswordPolicyTests` | `…/UseCases/Users/UserPasswordPolicyTests.cs` | ✅ |
| Utilisateurs : cycle de vie | `UserLifecycleTests` | `…/UseCases/Users/UserLifecycleTests.cs` | ✅ |
| Migration référence normalisée | `ProductNormalizedReferenceMigrationTests` | `…/Migrations/ProductNormalizedReferenceMigrationTests.cs` | ✅ **seul usage de `Migrate()`** |
| Pureté de l'Application | `ApplicationArchitectureTests` + 3 autres | `…/Architecture/` | ✅ |

**Aucun test n'est déplacé pendant cet audit.** Trois d'entre eux sont désignés comme **patrons de référence**
pour l'implémentation P3-11 : `AssertNothingPersisted`, `WorkshopSheetConcurrencyHardeningTests` (multi-contexte
déterministe) et `ProductNormalizedReferenceMigrationTests` (usage de `Migrate()`).

---

## 23. Trous de couverture de recette

| Parcours ou invariant transversal | Couvert bout-en-bout ? | Tests isolés existants | Scénario P3-11 requis |
|---|---:|---|---:|
| client → ordonnance → vente | ❌ | `CreateCustomer…`, `CreatePrescription…`, `RegisterSale…` (séparés) | ✅ S1 |
| vente → commande (lien `SaleId`, contenu verres seuls) | ❌ | `RegisterSaleUseCaseTests` vérifie `OrderId` non nul | ✅ S1 |
| commande → fiche atelier depuis une **vente réelle** | ❌ | `AdvanceOrderStatusWorkshopSheetTests` part d'un `SeedOrder` manuel | ✅ S1 |
| fiche → QC → `Ready` | ⚠️ partiel | `WorkshopSheetQcUseCaseTests`, `AdvanceOrderStatusWorkshopSheetTests` | ✅ S1 + S5 |
| `Ready` → `Delivered` + terminalité | ⚠️ partiel | `AdvanceOrderStatusUseCaseTests` (matrice seule) | ✅ S1 |
| vente → règlement du solde | ❌ | `SettleOrderBalance…` part d'une vente fabriquée à la main | ✅ S1 + S7 |
| vente → stock/mouvements (chronologie **complète** des 2 phases) | ❌ | décréments testés **séparément** par phase | ✅ S1 |
| retry de transition (pas de double décrément **ni** double fiche) | ❌ | `AdvanceOrderStatusUseCaseTests` teste le conflit, pas les effets cumulés | ✅ S6 |
| retry de règlement après parcours réel | ⚠️ partiel | `SettleOrderBalanceAtomicityTests` | ✅ S7 |
| rollback insuffisance stock **en fabrication** (statut + fiche + mouvements + notif) | ❌ | rollback de stock testé, **jamais** avec la fiche atelier | ✅ S4 |
| historique conservé après désactivation | ⚠️ partiel | `SetCustomerArchivedUseCaseTests`, `SetProductActive…` | ⚠️ voir §24 |
| notifications du parcours (compte exact sur 6) | ❌ | `EventNotificationsNonRegressionTests` (par événement) | ✅ S1 |
| lecture finale depuis un contexte neuf | ⚠️ partiel | patron présent mais local | ✅ S1 (systématisé) |
| **cohérence `ItemType` ⇄ `Category`** | ❌ | **aucun** | ✅ S9 |
| vente **sans** client (comptoir anonyme) de bout en bout | ❌ | `RegisterSaleBusinessRulesTests` (vente seule) | ✅ S2 |

**Constat central : il n'existe aujourd'hui aucun test bout-en-bout dans le dépôt.** Les 1413 tests sont tous
des tests ciblés d'un use case ou d'une politique, souvent excellents et sur vrai SQLite, mais **aucun** ne
franchit plus d'un domaine. C'est exactement la lacune que P3-11 doit combler — et la raison pour laquelle
P3-11 doit rester **petit** : il n'a rien à re-prouver, seulement à relier.

---

## 24. Scénarios candidats

### 24.1 Obligatoires (8)

| # | Nom candidat | Domaine traversé | Actes | Assertions finales | Valeur de recette |
|---|---|---|---|---|---|
| **S1** | `ParcoursOpticien_DeLaCreationClientAuReglementDuSolde_EstCoherentDeBoutEnBout` | Fournisseur, Produit, Client, Ordonnance, Vente, Commande, Stock, Atelier, QC, Notification, Paiement | §14.3 (13 actes) | §14.3 (8 blocs) | **Maximale** — le parcours n'existe nulle part ailleurs. |
| **S2** | `VenteSansClient_ProduitUneVenteAnonymeCoherente_EtAucuneCommandeSansVerre` | Vente, Stock, Notification | Créer produits ; `RegisterSale` sans `CustomerId`, monture seule | `Sale.CustomerId = null` ; **0 commande** ; `Status = Delivered` ; motif du mouvement = `"… - Vente sans client"` ; 0 notification | **Élevée** — prouve la branche « pas de verre ⇒ pas de commande » **et** la limite §6.2 B (solde irréglable). |
| **S3** | `VentePourClientArchive_EstRefusee_EtNeLaisseAucuneTraceDansLeParcours` | Client, Vente, Commande, Stock, Séquences | Archiver le client ; `RegisterSale` avec verres | `BusinessRuleException(CustomerArchivedMessage)` ; 0 vente, 0 ligne, **0 commande**, 0 mouvement, stock intact, **séquence non consommée** | **Élevée** — le refus est ciblé, l'**absence d'effet transversal** ne l'est pas. |
| **S4** | `FabricationAvecStockVerreInsuffisant_AnnuleStatutFicheStockEtNotification` ⭐ | Commande, Stock, Atelier, Notification | Parcours jusqu'à `ToFabricate` ; mettre le verre OG à 0 ; tenter `ToFabricate → InProgress` | `InsufficientStockException` ; statut resté `ToFabricate` ; **0 fiche atelier** ; stock verre OD **inchangé** ; 0 mouvement de fabrication ; **1** notification (celle de `New → ToFabricate`), pas 2 | **Maximale** — R2, le seul rollback vraiment multi-domaine. |
| **S5** | `PassageEnPret_EstBloqueSansControleQualite_PuisAutoriseApresValidation` | Commande, Atelier, QC, Notification | Parcours jusqu'à `QualityCheck` ; tenter `→ Ready` ; QC `Passed` ; retenter | 1ᵉʳ refus `ReadyRequiresPassedQcMessage`, statut `QualityCheck`, **aucune** notification ajoutée ; 2ᵉ succès, `Status = Ready`, 1 notification | **Élevée** — blocage **et** déblocage sur le même parcours. |
| **S6** | `TransitionRejouee_NeDecrementePasDeuxFoisLeStock_EtNeCreeNiSecondeFicheNiSecondeNotification` | Commande, Stock, Atelier, Notification | Parcours jusqu'à `InProgress` ; **rejouer** `ToFabricate → InProgress` (même portée **et** depuis un second `DbContext`) | `OrderStatusConflictException` ; stocks verres `3`/`3` (pas `2`/`2`) ; **2** mouvements de fabrication (pas 4) ; **1** fiche courante `Version = 1` ; **2** notifications (pas 3) | **Maximale** — l'idempotence des effets cumulés n'est prouvée nulle part. |
| **S7** | `ReglementDuSoldeRejoue_NEncaisseQuUneFois_EtNeNotifieQuUneFois` | Vente, Commande, Paiement, Notification | Parcours complet ; `SettleOrderBalance` ; **rejouer** depuis un second `DbContext` | 1ᵉʳ : `AmountEncashed = 200.00` ; 2ᵉ : `AlreadySettled = true`, `AmountEncashed = 0` ; `RemainingAmount = 0`, `DepositAmount = 300.00`, `PaymentStatus = Paid` ; **1** notification `PaymentReceived` | **Élevée** — sur montants réellement calculés par `SalePricingPolicy`. |
| **S8** | `ProduitDesactiveApresVente_ConserveLHistorique_EtNeBloquePasLeParcoursEnCours` | Produit, Vente, Commande, Atelier, Stock | Parcours jusqu'à `InProgress` ; `SetProductActive(false)` sur la monture ; poursuivre jusqu'à `Delivered` et régler | Parcours achevé ; ligne de vente, mouvements et fiche **intacts** ; `Product.IsActive = false` ; aucune écriture perdue | **Moyenne-élevée** — répond à « historique conservé après désactivation » (§24 obligatoire n° 8) avec une assertion transverse **réellement utile**. |

> **Sur l'item obligatoire n° 8 du prompt** (« historique conservé après désactivation … *uniquement si le
> modèle réel permet une assertion transverse utile* »). Le modèle **le permet** : `SetProductActiveUseCase`
> existe, `Product.IsActive` est lu par `TryAcquireActiveAsync` (donc bloquerait une **nouvelle** vente) mais
> **jamais** par `AdvanceOrderStatusUseCase` ni par le décrément de fabrication. S8 prouve donc quelque chose de
> non trivial : **une désactivation en cours de parcours n'interrompt pas la fabrication déjà engagée.** C'est
> un invariant métier réel et non couvert. **Retenu.**

### 24.2 Optionnels

| # | Nom candidat | Valeur | Décision |
|---|---|---|---|
| **S9** | `LigneVerreSurProduitNonVerre_CaracteriseLeComportementDeStockActuel` | Caractériserait l'incohérence `ItemType` ⇄ `Category` (§10.3) | ✅ **RETENU** — c'est précisément le type de défaut qu'un test bout-en-bout existe pour trouver. À écrire comme test de **caractérisation** (il constate le comportement actuel, sans le juger), en signalant en commentaire qu'un double décrément confirmé devient une dette bloquante P3-12. |
| S10 | Nouvel épisode de stock bas (réconciliation → réappro → résolution) | Déjà couvert finement par `LowStockReconciliationTests` | ❌ Écarté — duplication. |
| S11 | Fiche obsolète bloque `Ready` | Couvert par `AdvanceOrderStatusWorkshopSheetTests` | ❌ Écarté. |
| S12 | Collision de login | Hors parcours, couvert par `UserIdentityAndRoleTests` | ❌ Écarté. |
| S13 | Suppression fournisseur refusée | Hors parcours, couvert par `SupplierBusinessRulesTests` | ❌ Écarté. |
| S14 | Migration / adoption de base | Couvert par `ProductNormalizedReferenceMigrationTests` et `SqliteDatabaseManager` | ❌ Écarté. |
| S15 | Commande créée manuellement (`CreateOrderUseCase`) | Caractériserait le défaut `SaleId = 0` (§7.5) | ❌ Écarté de P3-11 — documente un défaut connu (P2B-2D §16.2) plutôt qu'une règle métier ; **inscrit comme dette P3-12**. |

**Total retenu : 9 scénarios** (8 obligatoires + S9), dans la fourchette « 5 à 10 » demandée.

---

## 25. Conventions de lisibilité

| Convention | Règle retenue |
|---|---|
| Langue des noms | **Français** pour les noms de tests de recette (`ParcoursOpticien_…`), cohérent avec les commentaires, la documentation et les messages métier du dépôt, tous en français. Les noms de types et de membres restent en anglais (convention .NET), comme partout ailleurs. |
| Structure | Sections explicites `// --- Arrange`, `// --- Act : <étape métier>`, `// --- Assert : <invariant>`. Dans S1, un `// --- Acte N` par étape du parcours. |
| Helpers de données | Nommés selon le métier : `CreerFournisseurEtProduitsOptiques`, `CreerClientAvecOrdonnance`, `EnregistrerVenteLunettesCompletes`. **Jamais** `Setup1`, `SeedAll`, `Arrange`. |
| Valeurs magiques | Chaque montant, stock et seuil est une constante nommée en tête de classe (`StockInitialMonture = 5`, `PrixMonture = 120.00m`) avec un commentaire justifiant le choix quand il porte une intention (ex. « > seuil, pour qu'aucune alerte stock bas n'apparaisse au nominal »). |
| Assertions | Une assertion lisible par invariant. **Les attendus sont exprimés à partir des entrées** (`total.Should().Be(PrixMonture + 2 * PrixVerre)`), jamais recopiés en dur — patron déjà appliqué par `RegisterSaleBusinessRulesTests`. |
| Mocks | **Aucun mock** des dépôts métier principaux. `Moq` reste disponible mais ne doit pas apparaître dans `Acceptance/`. |
| Dépendance entre tests | **Aucune.** Une base par test (§21 Q3). |
| Capture SQL | **Aucune**, sauf si un invariant l'exige explicitement (aucun ne l'exige aujourd'hui). |
| Duplication du code de production | Interdite : **aucun** recalcul de montant, de statut ou d'empreinte dans les tests. Les attendus se lisent en base. |
| Helper géant | Interdit : aucun helper ne doit exécuter plus d'une étape du parcours de S1. Les 13 actes de S1 restent visibles dans le corps du test. |
| Bibliothèque d'assertion | **FluentAssertions 6.12.0** — utilisée par les trois projets de test. ⚠️ **Épinglée en 6.x volontairement : la v7+ est sous licence commerciale.** Ne pas monter de version en P3-11. |

---

## 26. Risques

### 🔴 Bloquant P3-11

**Aucun.** Les six conditions bloquantes énumérées par le prompt §26 ont été vérifiées une par une :

| Condition bloquante | Vérification | Statut |
|---|---|---|
| Impossible de construire le parcours via les use cases | Les 13 actes de S1 existent tous comme use cases publics | ✅ non bloquant |
| Transaction rompue entre domaines | `EfTransactionRunner` se rattache à une transaction existante plutôt que d'en ouvrir une seconde ; chaque use case sensible en ouvre une | ✅ non bloquant |
| Lien indispensable absent | `Order.SaleId` existe et `RegisterSaleResult.OrderId` l'expose. Le lien `Prescription ↔ Sale` est absent, mais il n'est **pas indispensable** au parcours (§6.2 A) — il est documenté comme limite, pas comme blocage | ✅ non bloquant |
| État final incohérent | Le parcours atteint `Order.Status = Delivered` + `Sale.PaymentStatus = Paid` + stocks cohérents | ✅ non bloquant |
| Scénario nominal impossible | S1 est entièrement spécifié §14.3 | ✅ non bloquant |
| Test de recette nécessitant l'UI | §13 : aucun use case du parcours n'exige d'utilisateur, de session ni de ViewModel | ✅ non bloquant |

### 🟠 Important

- **`EnsureCreated` vs `Migrate`** — l'index unique filtré P3-8 est absent d'une base `EnsureCreated` ; un test
  d'anti-doublon d'alerte y serait faussement rassurant. **Mitigation : `Migrate()` en P3-11** (§20.1).
- **Fixtures trop couplées** — la fabrique de `DbContext` est dupliquée dans ~30 fichiers. P3-11 ne doit pas
  ajouter une 31ᵉ copie ad hoc mais un socle `AcceptanceScenarioBase` propre au dossier `Acceptance/`.
- **Dépendance à l'ordre** — écartée par « une base par test » (§21 Q3).
- **Entités suivies périmées** — risque n° 1 (§21 Q9) : toutes les primitives atomiques contournent le change
  tracker. Mitigation : portée neuve + `AsNoTracking()` systématiques.
- **Absence de lecture fraîche** — même mitigation.
- **Temps non injectable** — mélange `Now`/`UtcNow` structurel (roadmap §4.5, hors P3). Mitigation : aucune
  comparaison exacte de timestamp, et jamais de comparaison croisée `Now` ⇄ `UtcNow` (§21 Q6).
- **Tests trop lents** — `Migrate()` ×9 scénarios ≈ ≤ 3 s ajoutées. Acceptable ; à surveiller si le nombre de
  scénarios croît.
- **Duplication massive** — risque réel si P3-11 recopie les refus déjà couverts. Mitigation : la règle
  d'arbitrage §19.2 (deux use cases, deux domaines) et l'écartement explicite de 27 refus sur 33 (§15).
- **Seed de démonstration indispensable** — **non applicable** : `Migrate()` seul n'insère aucune donnée.

### 🟡 Dette

- Helpers de seed historiques dupliqués dans chaque fichier de test.
- Nommage hétérogène des helpers (`SeedOrder`, `SeedCustomer`, `SeedProduct`, sans convention commune).
- Pagination non testée en recette (hors périmètre).
- Exactitude des timestamps non assertable (conséquence de l'horloge non injectable).
- Ergonomie des builders : aucun builder d'entité n'existe, chaque test construit ses objets à la main.
- `Sale.EstimatedDelivery` piloté par `IsCounterSale` alors que `Sale.Status` est piloté par `hasLenses` —
  désalignement possible, dette P3-7 documentée.
- `Sale.Status` : `Draft`, `InFabrication`, `Ready`, `Cancelled` restent **inatteignables** (dette de modèle P3-7).
- `Order.SupplierId` jamais renseigné.
- Aucune FK reliant `StockMovement` à la vente ou à la commande (lien textuel seul, dette D5).

### 🔵 Reports

UI ; impression / PDF ; provider serveur (PostgreSQL / SQL Server) ; SaaS ; multi-magasin ; facturation ;
audit utilisateur complet ; **auteur du QC** (§13 Q10) ; autorisation par acteur ; performance en charge ;
tests système sur PostgreSQL / SQL Server ; politique d'annulation / remboursement de vente ; horloge
injectable ; lien `Prescription ↔ Sale` ; correction de `CreateOrderUseCase` (`SaleId` orphelin).

### 26.1 Dette connue P3-8 — à traiter avant le verdict P3-12

`InspectActiveLowStockUniqueIndex` (`src/MMV.Infrastructure/Data/SqliteDatabaseManager.cs:878-921`) détermine
l'unicité de l'index en cherchant le mot `UNIQUE` dans le SQL renvoyé par `sqlite_master` :

```csharp
var valid = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
    && sql.Contains("\"Type\"", StringComparison.Ordinal)
    // …
```

Or le nom de l'index est `idx_notifications_active_low_stock_unique`
(`SqliteDatabaseManager.cs:622`), et ce nom **figure dans le texte du `CREATE INDEX`**. Le test
`Contains("UNIQUE", OrdinalIgnoreCase)` est donc satisfait par le suffixe `_unique` **du nom**, sans qu'aucun
mot-clé `CREATE UNIQUE INDEX` ne soit réellement présent. **Un index non unique portant ce nom serait validé
comme unique**, et le diagnostic annoncerait une protection anti-doublon d'alerte stock bas qui n'existe pas.

**Traitement :**

- ❌ **N'est pas corrigée en P3-11** — le périmètre est « tests de recette et documentation uniquement », et
  toucher ce code reviendrait à rouvrir P3-8.
- ❌ **N'est pas masquée** — elle est enregistrée ici explicitement.
- ✅ **Doit être traitée avant le verdict final P3-12.**
- ⚠️ **Ne doit pas être confondue avec un défaut des scénarios de recette.** Si un scénario P3-11 de stock bas
  se comportait de façon inattendue, la première hypothèse à écarter serait cette dette, non le scénario.

**Correction indicative pour P3-12** (à ne pas appliquer maintenant) : interroger `PRAGMA index_list('Notifications')`
et lire la colonne `unique`, ou tester `sql.StartsWith("CREATE UNIQUE INDEX", OrdinalIgnoreCase)` — deux
approches qui ne peuvent pas être satisfaites par le nom de l'index.

**Dette candidate de même rang, née de cet audit :** l'incohérence `ItemType` ⇄ `Category` (§10.3). Si le
scénario S9 confirme le double décrément, elle rejoint cette section pour P3-12.

---

## 27. Plan candidat d'implémentation

> **Le plus petit plan P3-11 possible.** Aucune implémentation n'est faite ici. Aucune sous-phase officielle
> n'est inventée.

| Élément | Décision |
|---|---|
| **Emplacement** | `tests/MMV.Application.Tests/Acceptance/` — nouveau dossier, **aucun nouveau projet** |
| **Fixture** | `AcceptanceScenarioBase` (classe abstraite `IDisposable`) : répertoire temporaire par instance, `PathFor`, `CreateContext`, `ApplyMigrations`, `CreateUseCases(context)`, `ReadFresh<T>` |
| **Base SQLite** | Un fichier `.db` **par test**, `Data Source={path};Pooling=False`, clés étrangères **activées** |
| **Migrations** | `context.Database.Migrate()` — jamais `EnsureCreated`, jamais EF InMemory |
| **DI** | **Aucun conteneur.** Instanciation directe des dépôts (`SaleRepository`, `OrderRepository`, `ProductRepository`, `CustomerRepository`, `SupplierRepository`, `PrescriptionRepository`, `StockMovementRepository`, `NotificationRepository`, `UserRepository`) et primitives (`UnitOfWork`, `EfTransactionRunner`, `EfStockMutationService`, `EfNumberSequenceService`) autour d'un `OpticDbContext` partagé — reproduit `AddScoped` |
| **Données minimales** | §8 : 1 fournisseur, 1 client, 1 ordonnance, 3 produits (monture + 2 verres), stocks 5/4/4, seuils 2/1/1 |
| **Scénarios obligatoires** | S1 à S8 (§24.1) + S9 caractérisation (§24.2) — **9 tests** |
| **Scénarios reportés** | S10 à S15 (§24.2), tous écartés avec justification |
| **Assertions finales** | Contexte neuf `AsNoTracking()` ; aucun montant recalculé ; aucun timestamp comparé exactement |
| **Isolation** | Une base par test ; aucune `IClassFixture` partagée ; aucun `[Collection]` |
| **Parallélisme** | Parallélisme xUnit par défaut conservé (isolation structurelle par fichier) |
| **Temps d'exécution estimé** | ~9 × (migrations ~0,3 s + actes ~0,2 s) ≈ **4 à 6 s**, soit +30 à 40 % sur les 15 s actuelles de `MMV.Application.Tests`. Acceptable |
| **Documentation** | Rapport d'implémentation `docs/implementation/P3-11-business-acceptance-scenarios-implementation-report.md` + mise à jour de la section P3-11 de la roadmap, **au moment de l'implémentation, pas maintenant** |
| **Migration** | **Aucune** — P3-11 ne touche pas au schéma |
| **UI** | **Aucune** — P3-11 ne touche pas à `MMV.App` |
| **Fichiers touchés (prévision)** | `Acceptance/AcceptanceScenarioBase.cs` + 3 à 4 fichiers de scénarios groupés par thème (parcours nominal, refus et rollback, idempotence et rejeu, caractérisation). **Aucun fichier de production modifié.** |
| **Impact sur le total de tests** | 1413 → environ **1422** |

### Ordre d'implémentation suggéré

1. `AcceptanceScenarioBase` + **S1** seul. C'est l'étape à risque : si S1 passe, tout le reste est mécanique.
2. S2 et S3 (refus sans trace).
3. S4 (rollback transversal ⭐).
4. S5, S6, S7 (blocage QC, rejeu de transition, rejeu de règlement).
5. S8 (désactivation en cours de parcours).
6. S9 (caractérisation `ItemType` ⇄ `Category`) — **en dernier**, car son résultat peut ouvrir une dette
   bloquante pour P3-12 et doit être documenté avec soin.

---

## 28. Reports explicites

Sont **explicitement reportés hors P3-11** et devront être repris en P3-12 ou plus tard :

| Report | Destination |
|---|---|
| Correction de `InspectActiveLowStockUniqueIndex` (dette P3-8, §26.1) | **P3-12 — bloquant avant verdict** |
| Statut de l'incohérence `ItemType` ⇄ `Category` (§10.3), selon le résultat de S9 | **P3-12 — potentiellement bloquant** |
| Correction de `CreateOrderUseCase` (`SaleId` orphelin, §7.5) | P3-12 ou ultérieur |
| Réconciliation du double rôle d'`Order` (fournisseur ⇄ atelier) | Post-P3 |
| Règlement d'une vente **sans** commande (§6.2 B) | Post-P3 |
| Lien `Prescription ↔ Sale` | Post-P3 (décision métier) |
| Auteur du QC, `Sale.StaffId`, `StockMovement.PerformedByUserId` | Redesign UI / propagation d'identité |
| Horloge injectable (`Now` ⇄ `UtcNow`) | Hors P3, décision explicite requise |
| `Sale.Status` : valeurs inatteignables et désalignement avec `EstimatedDelivery` | Post-P3 |
| Politique d'annulation / remboursement de vente | Post-P3 |
| UI, impression, PDF | Redesign global |
| Provider serveur, SaaS, multi-magasin, facturation | Post-P3 |
| Performance en charge, tests système PostgreSQL / SQL Server | Post-P3 |

**Ni P3-12 ni le redesign UI ne sont commencés par ce document.**

---

## 29. Verdict

### Contrôles de sortie

| Critère du prompt §30 | Statut |
|---|---|
| Le parcours réel est entièrement cartographié | ✅ §6 — 14 étapes, y compris les **deux arêtes manquantes** du parcours cible officiel |
| Les use cases exacts sont identifiés | ✅ §6.1 — nommés, avec entrées, écritures et sorties |
| Les données minimales sont définies | ✅ §8 — fiche complète avec valeurs candidates et justifications |
| Les invariants finaux sont listés | ✅ §9 — 47 invariants, tous assertables par contexte neuf |
| Les limites du modèle sont honnêtement documentées | ✅ §6.2, §7, §10.3, §13, §26 |
| Les scénarios obligatoires sont limités et précis | ✅ §24 — **9 scénarios**, 27 refus sur 33 explicitement écartés |
| Le support de test est décidé | ✅ §20 — `tests/MMV.Application.Tests/Acceptance/`, sans nouveau projet, sans conteneur DI |
| L'isolation SQLite est définie | ✅ §21 — une base migrée par test, `Pooling=False`, nettoyage best-effort |
| Les rollbacks et retries pertinents sont sélectionnés | ✅ §16 (R1, R2 sur 6) et §17 (3 rejeux sur 8) |
| La dette P3-8 est enregistrée pour P3-12 | ✅ §26.1 — mécanisme exact, ligne exacte, correction indicative, non appliquée |
| Le plan candidat est exécutable | ✅ §27 — emplacement, fixture, ordre, estimation de durée, impact sur le total |
| Le rapport `.md` existe | ✅ `docs/implementation/P3-11-business-acceptance-scenarios-audit-report.md` |
| Aucun code ou test n'a été modifié | ✅ voir ci-dessous |

### Validation documentaire

`git status --short` après rédaction :

```
?? design-handoff/
?? design/
?? docs/implementation/P3-11-business-acceptance-scenarios-audit-report.md
?? docs/ui/
```

`git diff --check` : vide. `git diff --stat` : vide (aucun fichier **suivi** modifié).

Le seul nouveau fichier P3-11 est bien
`docs/implementation/P3-11-business-acceptance-scenarios-audit-report.md`. **Aucun fichier Domain,
Application, Infrastructure, test, migration ou UI n'a été modifié.** Aucune autre documentation n'a été mise
à jour.

### Verdict

```
P3-11 AUDIT = GO
```

Aucun commit. Aucun push. Aucune migration. Aucune UI.

**STOP.**

---

## 30. Décisions retenues pour l'implémentation

*(section ajoutée lors de l'implémentation P3-11 — voir le [rapport
d'implémentation](P3-11-business-acceptance-scenarios-implementation-report.md))*

| Décision | Valeur retenue |
|---|---|
| Emplacement | `tests/MMV.Application.Tests/Acceptance/` — **aucun nouveau projet** |
| Base | Un fichier SQLite **migré par test** (`Database.Migrate()`), `Pooling=False`, clés étrangères **activées** |
| Initialisation | **Seed Production sûr** après migration (`DatabaseSeeder` + `new SeedOptions()`) ; **aucun seed Demo**, `DbInitializer` jamais appelé |
| Mutations | **Uniquement par use cases publics** ; aucune mutation directe par `DbContext` ni par dépôt |
| Assertions | **Contexte neuf** `AsNoTracking()` ; aucun montant recalculé ; aucun timestamp comparé exactement |
| Scénarios | 9 prévus ; **S9 transformé en gate d'intégrité**, et non en test de caractérisation du défaut |
| Production | **aucun code de production**, **aucune migration**, **aucune UI**, aucun package modifié |

### 30.1 Correction — « `Migrate()` seul n'insère aucune donnée » (§21.2 Q4, §26)

L'affirmation est **exacte dans son résultat, inexacte dans son raisonnement**, et doit être remplacée par le
constat mesuré suivant :

1. la migration `20260127184542_InitialCreate` **insère bien** un compte administrateur
   (`InsertData` sur `Users`, `UserId = 1`, login `admin`, hash faible connu, `IsActive = true`) ;
2. mais la migration `20260212164646_AddCounterSaleFieldsToOrder` ouvre son `Up()` par
   `DeleteData(table: "Users", keyColumn: "UserId", keyValue: 1L)` et **ne le réinsère jamais** — son unique
   `InsertData` appartient à son `Down()` ;
3. **résultat mesuré** (migrations appliquées une par une) : le compte existe jusqu'à `AddNotifications`
   (`Users = 1`) puis disparaît exactement à `AddCounterSaleFieldsToOrder` (`Users = 0`) et ne revient jamais.
   Sur une base migrée jusqu'à HEAD, **`Users` est vide**, comme toutes les tables métier.

Le socle de recette exécute néanmoins le **seed Production sûr** — c'est le chemin réel du démarrage desktop,
il est idempotent, et il resterait la seule protection si le compte réapparaissait. L'invariant asserté est donc
**« aucun compte faible ACTIF ne subsiste »**, vrai dans les deux cas, plutôt qu'une hypothèse sur le contenu
de `Users`.

### 30.2 Résultat de l'implémentation

Le socle et le **parcours nominal S1 sont verts**. Le **gate S9 a confirmé** l'incohérence `ItemType` ⇄
`Category` pressentie en §10.3 de cet audit : la ligne incohérente est **acceptée**, et produit un **double
décrément** (produit `MONTURE` sur ligne `LensOd`) ou **aucun décrément** (produit `VERRE` sur ligne `Frame`).

Conformément au protocole, S2 à S8 n'ont **pas** été implémentés, le défaut n'a **pas** été corrigé et les
tests n'ont **pas** été adaptés. Verdict d'implémentation : **`P3-11 = NO-GO LOCAL`**.

La dette §10.3 de cet audit cesse donc d'être « déduite de la lecture du code » : elle est **confirmée par
exécution**, et rejoint la dette P3-8 (§26.1) au rang **bloquant avant le verdict P3-12**.

---

## 31. Addendum — arbitrage du défaut `ItemType` ⇄ `Category`

> Ajouté après le second cycle P3-11. **L'audit d'origine n'est pas réécrit** : cette section enregistre
> uniquement ce que l'arbitrage a changé. Les analyses, chiffres et priorités des sections 1 à 30 restent tels
> qu'ils ont été produits.

### 31.1 Défaut confirmé

L'incohérence pressentie en §10.3 est **confirmée par exécution** (et non plus par lecture de code) : une ligne
de vente dont l'`ItemType` contredit la `Category` du produit était **acceptée**, et produisait

- un **double décrément** — produit `MONTURE` vendu sur une ligne `LensOd` (stock `6 → 4`, 2 mouvements) ;
- **aucun décrément** — produit `VERRE` vendu sur une ligne `Frame` (stock `6 → 6`, 0 mouvement).

### 31.2 Arbitrage accepté

La correction a été **explicitement autorisée dans P3-11**, en tant que correctif transverse découvert par la
recette et bloquant pour S2 à S8. Cet arbitrage **ne rouvre pas** P3-4B, P3-5, P3-6 ni P3-7 : leurs commits, CI
et verdicts historiques restent inchangés, et il n'est pas prétendu qu'ils contenaient déjà cette règle.

### 31.3 S9 devient un test de refus

La formulation d'origine du gate admettait deux issues valides — refus métier, **ou** décrément exactement
unique. La remédiation ayant retenu le **refus**, les deux cas S9 exigent désormais cette issue précise et
vérifient qu'elle ne laisse aucune trace (ni document, ni stock, ni mouvement, ni notification, ni **numéro
consommé**). Les deux tests sont conservés et renommés `…_EstRefuseeSansAucuneEcriture` ; aucun n'est ignoré et
l'invariant n'est pas affaibli.

### 31.4 Portée de la règle — limitée au flux de consommation du stock

La règle introduite ne décrit **pas** une taxonomie commerciale : elle n'affirme ni `Frame == MONTURE`, ni
`Accessory == CLIPS`. Elle n'oppose que la question qui décide réellement d'une mutation de stock — **le produit
sort-il à la vente, ou plus tard à la fabrication ?** — et garantit **exactement un** chemin de consommation par
ligne acceptée. Une monture vendue sur une ligne `Accessory` reste donc acceptée.

Détail complet, gardes, limites et preuves : **§20 du rapport d'implémentation P3-11**.

### 31.5 Ce qui reste inchangé dans cet audit

- la **dette P3-8** (§26.1) reste **non corrigée** et **bloquante avant le verdict P3-12** ;
- la spécification de S1 à S8 (§24.1) est **conservée telle quelle** : aucun objectif n'a été modifié ;
- les chemins d'écriture secondaires (`CreateOrderUseCase`, `UpdateOrderUseCase`, `SaleId = 0`) restent des
  dettes **ouvertes**, hors périmètre.
