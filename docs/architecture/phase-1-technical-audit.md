# PHASE 1 — AUDIT TECHNIQUE ET ARCHITECTURAL MULTI-PAYS DE MMV

> **Date : 9 juin 2026.** Audit réalisé sans aucune modification de code applicatif.
> Référentiel métier officiel : fichiers `docs/domain/` consolidés en Phase 0.5D.
> Preuves : chaque constat cite le dépôt réel (chemin, symbole, plage de lignes) tel qu'observé à cette date sur la branche `refactor/redesignLightAndDarkMode`.
>
> Cet audit n'est ni un avis juridique, ni une certification de conformité. Les valeurs réglementaires (TVA, montants, périodicités) restent des **validation gates** Phase 0.5D et ne sont jamais codées.

Documents liés :
- [Carte de dépendances](dependency-map.md)
- [Registre des risques](risk-register.md)
- [ADR candidats](adr-candidates.md)
- [Feuille de route de migration](migration-roadmap.md)

---

## 0. Méthode et fiabilité des preuves

- Lecture intégrale des entités, value objects, services, repositories, configurations EF, ViewModels critiques, composition root, tests.
- Recherches globales du dépôt (`Grep`/`Glob`) pour qualifier les absences.
- Build et tests réellement exécutés (section 2).
- Convention :
  - `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` : recherche complète effectuée.
  - `NON_VÉRIFIÉ_DANS_LE_DÉPÔT` : non garanti exhaustif.
- Les anciens documents présents dans le dépôt (`docs/PHASE0-CARTOGRAPHIE.md`, `ARCHITECTURE.md`, `TESTS.md`) **n'ont pas servi de source de vérité** ; ils ne sont mentionnés qu'à titre d'artefacts existants. Toutes les observations ci-dessous ont été re-vérifiées sur le code.

---

## 1. Résumé exécutif

MMV est une application **desktop Avalonia/.NET 8 mono-magasin, mono-pays (France implicite), mono-devise (EUR implicite)**, structurée en trois projets (`MMV.Domain`, `MMV.Infrastructure`, `MMV.App`) plus deux projets de tests. La séparation des couches est **partiellement saine au niveau des références projet** : `MMV.Domain` ne dépend ni d'EF Core, ni d'Avalonia, ni de l'Infrastructure (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` : aucun `using Microsoft.EntityFrameworkCore`/`Avalonia`/`MMV.Infrastructure` dans `src/MMV.Domain/**`).

Cependant, l'**implémentation réelle s'écarte fortement de l'intention DDD/Clean affichée** :

1. **Le cœur métier ne porte aucune des fondations multi-pays exigées par la Phase 0.5D.** Aucun concept d'`Organisation`, `Magasin`, `Pays`, `Devise`, `Taxe`, `Politique nationale`, `Audit` n'existe dans le code (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`).
2. **Les value objects existent mais sont du code mort** : `Money`, `Address`, `Email`, `PhoneNumber` ne sont **jamais instanciés** dans `src/**` (0 occurrence de `new Money(`, `new Address(`, `new Email(`, `new PhoneNumber(`). Les entités utilisent des primitifs (`decimal`, `string`).
3. **L'argent est stocké en virgule flottante** (`HasColumnType("REAL")`) et **sans devise**, ce qui est doublement disqualifiant pour une facturation BE/MA correcte.
4. **La logique métier critique vit dans les ViewModels** (`SaleFormViewModel.ExecuteSave`), qui **court-circuitent les services de domaine** (eux-mêmes non enregistrés dans la DI réelle).
5. **La numérotation des ventes/commandes est aléatoire** (`new Random().Next(10000)`) face à une contrainte d'unicité en base → collisions et échecs de vente garantis à terme.
6. **La base est créée via `EnsureCreated()`**, ce qui **contourne les 7 migrations existantes** ; **deux chemins physiques de base de données distincts** coexistent, répartis sur plusieurs sites de configuration (décompte précis en §3.3).
7. **Aucune internationalisation** : 606 littéraux français codés en dur dans les vues, aucun `.resx`, aucun `x:Uid`, aucun support RTL.
8. **Aucun journal d'audit**, aucune trace d'accès aux données de santé, autorisation présente mais **purement UI**. *(Ces absences sont des lacunes d'« accountability » et de sécurité à fort risque pour des données de santé — et non une conclusion juridique : elles appellent une analyse de risque et une validation juridique locale, cf. §4.7.)*

Le projet **n'est pas, en l'état, apte à recevoir le cœur commun multi-pays sans un travail de fondation substantiel**. En revanche, certaines briques sont réutilisables (modèle optique d'ordonnance riche, abstraction repository/UnitOfWork, authentification BCrypt, suite de tests existante).

**Verdict Phase 2 (motivé et scindé en section 9, conformément à la Phase 1B) :**
- **Phase 2A — stabilisation & fondations techniques : `GO` sous conditions** (corriger d'abord les `BLOCKER` : suite de tests rouge, stratégie de persistance, modèle monétaire).
- **Fonctionnalités nationales BE/MA : `NO-GO` à ce stade** — bloquées tant que les fondations 2A ne sont pas livrées et tant que les gates métier ne sont pas levées par des experts locaux.

---

## 2. État du build et des tests

### 2.1 Environnement

| Élément | Valeur observée |
|---|---|
| SDK .NET installés | `8.0.417`, `10.0.102`, `10.0.103` (`dotnet --list-sdks`) |
| `global.json` | Absent (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`) → SDK par défaut = `10.0.103` |
| TargetFramework de tous les projets | `net8.0` (5 `.csproj` lus) |
| `Nullable` | `enable` partout |

> Remarque : l'absence de `global.json` fait construire des projets `net8.0` avec le SDK .NET 10 par défaut. Le build réussit, mais ce choix n'est pas verrouillé et peut diverger entre postes/CI.

### 2.2 Commandes exécutées et résultats

| Commande | Résultat |
|---|---|
| `dotnet restore MMV.sln` | **Succès** — « Tous les projets sont à jour pour la restauration. » |
| `dotnet build MMV.sln --no-restore -c Debug` | **Succès — 0 avertissement, 0 erreur** (13 s) |
| `dotnet test MMV.sln --no-build -c Debug` | **ÉCHEC global** — `MMV.App.Tests` : 79/79 ✅ ; `MMV.Domain.Tests` : **108 réussis, 5 échoués / 113** |

**Total : 187 tests réussis, 5 échoués.** La suite est **rouge sur un checkout propre**.

### 2.3 Détail des 5 tests en échec (non masqués)

| Test | Cause racine vérifiée |
|---|---|
| `Data.DbContextTests.ShouldCreateDatabaseAndSeedAdmin` ([DbContextTests.cs:24-29](../../tests/MMV.Domain.Tests/Data/DbContextTests.cs#L24-L29)) | Le test attend qu'`EnsureCreatedAsync()` crée un admin (`UserId == 1`). Or le seed admin est dans `DbInitializer.Initialize()` (impératif, non appelé ici), **pas** dans le modèle EF (`HasData`). → `admin` est `null`. |
| `RepositoryTests.UserRepositoryTests.GetActiveUsersAsync_ShouldReturnOnlyActive` ([UserRepositoryTests.cs:87](../../tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs#L87)) | Même cause : commentaire « admin seed + active user » attend 2, obtient 1 (pas de seed admin via `EnsureCreated`). |
| `ServiceTests.ProductServiceTests.CreateProductAsync_ShouldSucceed` ([ProductServiceTests.cs:30-39](../../tests/MMV.Domain.Tests/ServiceTests/ProductServiceTests.cs#L30-L39)) | `Product.SupplierId` est une FK requise (`OnDelete.Restrict`). Le test ne crée aucun `Supplier` → `SQLite Error 19: FOREIGN KEY constraint failed`. |
| `ServiceTests.ProductServiceTests.GetProductAsync_ShouldReturnProduct` | Idem (échoue dès `CreateProductAsync`). |
| `ServiceTests.ProductServiceTests.DeleteProductAsync_ShouldSucceed` | Idem (échoue dès `CreateProductAsync`). |

Ces échecs révèlent **deux dérives structurelles** :
- **Stratégie de seed incohérente** : les tests supposent un seeding via le modèle EF ; le runtime utilise un seeding impératif (`DbInitializer`) + `EnsureCreated`. Les deux ne sont pas alignés.
- **Fixtures de test fragiles** vis-à-vis des contraintes FK (Product↔Supplier), signe de tests non maintenus après évolution du schéma.

### 2.4 Couverture / qualité de la suite

- Pas de collecte de couverture activée par défaut hors `coverlet.collector` dans `MMV.App.Tests` (non exécuté ici). `NON_VÉRIFIÉ_DANS_LE_DÉPÔT` pour un chiffre de couverture.
- Les tests `SaleServiceTests` couvrent le **`SaleService` contourné en production** (cf. §3.4) — donc le chemin réel (`SaleFormViewModel.ExecuteSave`) **n'est pas testé**.
- Aucun test de migration, de localisation, de concurrence, d'unicité de numérotation, ni du multi-tenant (inexistant).

### 2.5 Dépendances vulnérables (ajout Phase 1B)

Commande exécutée : `dotnet list MMV.sln package --vulnerable --include-transitive` (source : `api.nuget.org`).

| Package (transitif) | Version résolue | Gravité | Avis | Projets concernés |
|---|---|---|---|---|
| `Microsoft.Extensions.Caching.Memory` | 8.0.0 | High | GHSA-qj66-m88j-hmgj | tous |
| `System.Text.Json` | 8.0.0 | High | GHSA-hh2w-p6rv-4g7w ; GHSA-8g4q-xg66-9fp4 | tous |
| `Tmds.DBus.Protocol` | 0.20.0 | High | GHSA-xrw6-gwf8-vvr9 | `MMV.App`, `MMV.App.Tests` |
| `System.Net.Http` | 4.3.0 | High | GHSA-7jgj-8wvc-jh57 | `MMV.App.Tests` |
| `System.Text.RegularExpressions` | 4.3.0 | High | GHSA-cmhx-cq75-c4mj | `MMV.App.Tests` |

`MMV.Domain` ne comporte **aucun** package vulnérable. Toutes les vulnérabilités sont **transitives** (héritées d'EF Core / Avalonia / SDK de test) et **High** ; aucune n'est `Critical`. **Aucun package n'est déclaré vulnérable sans ce résultat de commande.** Remédiation = mise à jour des versions cadres (hors Phase 1, à planifier en Étape 0 de la roadmap). Voir risque R-24.

---

## 3. Cartographie (synthèse — détail dans `dependency-map.md`)

### 3.1 Solution et projets

`MMV.sln` regroupe 5 projets :

| Projet | Type | Rôle | Dépendances projet |
|---|---|---|---|
| `MMV.Domain` | classlib net8.0 | Entités, VO, enums, interfaces repo, services, validators | *(aucune)* + NuGet `FluentValidation 11.9.0` |
| `MMV.Infrastructure` | classlib net8.0 | EF Core, DbContext, repos, UoW, seed, auth | → `MMV.Domain` ; EF Core Sqlite 8.0.0, BCrypt.Net-Next 4.0.3, Config.Abstractions |
| `MMV.App` | WinExe net8.0 | Avalonia/MVVM, vues, VM, services UI, DI | → `MMV.Domain`, `MMV.Infrastructure` ; Avalonia 11.2.8, CommunityToolkit.Mvvm 8.2.2, MS.DI/Hosting 8.0.0, FluentAvaloniaUI |
| `MMV.Domain.Tests` | xunit net8.0 | tests domaine + infra | → `MMV.Domain`, `MMV.Infrastructure` |
| `MMV.App.Tests` | xunit net8.0 | tests VM/services UI | → `MMV.App` |

**Constat structurel majeur : il n'existe pas de projet `Application`.** Les « services de domaine » (`SaleService`, `OrderService`, etc.) jouent le rôle de services applicatifs et orchestrent directement la persistance via `IUnitOfWork`. C'est un **transaction script**, pas un modèle de domaine riche.

### 3.2 Points d'entrée et composition root

- Point d'entrée : [`Program.cs:15`](../../src/MMV.App/Program.cs#L15) → `BuildAvaloniaApp().StartWithClassicDesktopLifetime`.
- Composition root **réel** : [`App.ConfigureServices()`](../../src/MMV.App/App.axaml.cs#L102-L169).
- Un **deuxième** point de configuration existe : [`Infrastructure.DependencyInjection.AddInfrastructure()`](../../src/MMV.Infrastructure/DependencyInjection.cs#L17-L51).

**Double DI divergente (vérifiée) :**

| Aspect | `App.ConfigureServices()` (runtime réel) | `AddInfrastructure()` |
|---|---|---|
| Appelé ? | Oui (OnFrameworkInitializationCompleted) | **Jamais** (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` : `Grep "AddInfrastructure"` → seulement la définition + docs) |
| Services de domaine (`ISaleService`…) | **Non enregistrés** (seul `IAuthenticationService` l'est, [App.axaml.cs:124](../../src/MMV.App/App.axaml.cs#L124)) | Enregistrés ([DependencyInjection.cs:43-48](../../src/MMV.Infrastructure/DependencyInjection.cs#L43-L48)) |
| Connexion SQLite | `Data Source=mmv-optic.db` (relatif, [App.axaml.cs:108](../../src/MMV.App/App.axaml.cs#L108)) | `%LOCALAPPDATA%\ManageMyVision\mmv.db` |

Conséquence : la couche de services métier (correcte sur le principe) est **morte au runtime**, et les ViewModels accèdent directement aux repositories.

### 3.3 Modèle de données (DbContext)

[`OpticDbContext`](../../src/MMV.Infrastructure/Data/OpticDbContext.cs) expose **18** `DbSet` (recompté en Phase 1B, lignes 23-115) : `Users, Suppliers, ProductCategories, Products, GlassDetails, LensDetails, AccessoryDetails, Supplements, GlassSupplements, GlassPricingTiers, Customers, Prescriptions, Orders, OrderItems, Sales, SaleItems, StockMovements, Notifications`.

- **Aucune** entité `Organisation`, `Magasin`, `Pays`, `Devise`, `Facture`, `Taxe`, `Paiement` (séparé), `OrganismeCouverture`, `Audit`, `Garantie`, `SAV` (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`).
- **Configuration de la connexion — décompte précis (corrigé en Phase 1B) :**
  - **4 sites de configuration** : `App.ConfigureServices` ([App.axaml.cs:107-108](../../src/MMV.App/App.axaml.cs#L107-L108)) ; `OpticDbContext.OnConfiguring` ([117-139](../../src/MMV.Infrastructure/Data/OpticDbContext.cs#L117-L139), fallback **non déclenché** quand les options sont déjà fournies par la DI) ; `OpticDbContextFactory` design-time ([16-29](../../src/MMV.Infrastructure/Data/OpticDbContextFactory.cs#L16-L29)) ; `AddInfrastructure.ResolveConnectionString` ([66-78](../../src/MMV.Infrastructure/DependencyInjection.cs#L66-L78), **non appelé**).
  - **2 chaînes de connexion distinctes** : `Data Source=mmv-optic.db` et `Data Source=%LOCALAPPDATA%\ManageMyVision\mmv.db` (cette dernière apparaît dans 3 sites).
  - **2 chemins physiques distincts** : un relatif (`mmv-optic.db`, runtime réel) et un sous `%LOCALAPPDATA%\ManageMyVision\mmv.db` (design-time/migrations + fallbacks). Conséquence inchangée : les migrations sont générées/appliquées sur un chemin **différent** de celui qu'utilise l'application au runtime.

### 3.4 Localisation de la logique métier (vérifiée)

`SaleFormViewModel.ExecuteSave` ([SaleFormViewModel.cs:809-980](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L809-L980)) :
- crée l'entité `Sale`, calcule les totaux, crée la `Order` fournisseur, décrémente le stock et écrit les `StockMovement` — **directement dans le ViewModel** ;
- utilise `ISaleRepository`, `IOrderRepository`, `IProductRepository`, `IStockMovementRepository`, `IUnitOfWork` — **pas** `ISaleService`/`IOrderService` ;
- enchaîne **plusieurs `SaveChangesAsync`** ([884-887](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L884-L887), [965](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L965)) **sans transaction explicite** (alors que `IUnitOfWork.BeginTransactionAsync` existe). Une panne entre les deux laisse une vente sans commande/mouvements.

`ISaleService`/`IOrderService`/`ICustomerService`/etc. ne sont référencés que dans leur propre définition et dans `DependencyInjection.cs` (non appelé) — `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` : aucun usage dans `MMV.App/**`.

---

## 4. Audit architectural par thème (Étape C)

### 4.1 Frontières et dépendances

| Point | Constat | Preuve | Sévérité |
|---|---|---|---|
| Domaine pur | `MMV.Domain` sans EF/Avalonia/Infra | recherche d'usings (0) | ✅ Atout |
| Couche Application absente | services de domaine = transaction scripts couplés à `IUnitOfWork` | `SaleService.cs:19-26` | MEDIUM |
| Logique dans l'UI | `ExecuteSave` orchestre tout | `SaleFormViewModel.cs:809-980` | CRITICAL |
| Services contournés | DI réelle n'enregistre pas les services | `App.axaml.cs:111-146` | HIGH |
| Double DI / 2 chemins DB physiques | divergence runtime/migrations | §3.2, §3.3 | HIGH |
| Interfaces repo dans le domaine | bon emplacement (`MMV.Domain/Interfaces/Repositories`) | dossier | ✅ |

### 4.2 Modèle de domaine

- **Entités anémiques** : tous les champs en `get; set;` publics, aucun invariant, aucune encapsulation. Ex. `Sale` ([Sale.cs](../../src/MMV.Domain/Entities/Sale.cs)) expose `TotalAmount`/`FinalAmount` en écriture libre.
- **Value objects morts** : `Money` (EUR par défaut, [Money.cs:11](../../src/MMV.Domain/ValueObjects/Money.cs#L11), [Money.cs:29](../../src/MMV.Domain/ValueObjects/Money.cs#L29)), `Address` (porte un `Country`, [Address.cs:12](../../src/MMV.Domain/ValueObjects/Address.cs#L12)), `Email`, `PhoneNumber` — définis, **testés**, mais **jamais utilisés** par les entités.
- **Client / porteur / payeur non séparés** : `Customer` est à la fois acheteur et porteur ; pas de payeur tiers, pas de foyer/mineurs. `SocialSecurityNumber` ([Customer.cs:56](../../src/MMV.Domain/Entities/Customer.cs#L56)) et `InsuranceName` ([Customer.cs:61](../../src/MMV.Domain/Entities/Customer.cs#L61)) sont des chaînes libres ; **pas de champ Pays** sur le client.
- **Ordonnance** : modèle OD/OG **riche et réutilisable** ([Prescription.cs](../../src/MMV.Domain/Entities/Prescription.cs)) — sphère, cylindre, axe, addition, prisme, base, acuité. Mais : `IssueDate` sans **date de validité**, `DoctorName` en texte libre (pas d'identifiant prescripteur national), pas de lien magasin/pays.
- **Vente / facture / paiement / commande** : `Sale` agrège `SaleItem` + `Order` ; **aucune entité Facture** distincte, **aucune TVA**, **aucune devise**, paiement réduit à `PaymentMethod`/`PaymentStatus` + acompte. Pas de modèle d'avoir.
- **Stock / atelier** : `StockMovement` (In/Out/Adjustment) réutilisable mais **global, sans magasin/emplacement** ([StockMovement.cs](../../src/MMV.Domain/Entities/StockMovement.cs)). Workflow atelier porté par `OrderStatus` ([OrderService.cs:76-88](../../src/MMV.Domain/Services/OrderService.cs#L76-L88)).
- **SAV / réparation / garantie** : `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` (aucune entité).
- **Money / devise / arrondis** : `Money` arrondit `ToEven` à 2 décimales mais inutilisé ; en base, montants en `REAL` (cf. 4.3).
- **Identifiants / numérotation** : `Random` dans le VM (cf. 4.3 et risque R-03).
- **Temps** : mélange `DateTime.Now` (23 occurrences, dont `Notification.CreatedAt = DateTime.Now` [Notification.cs:48](../../src/MMV.Domain/Entities/Notification.cs#L48)) et `DateTime.UtcNow` (défauts d'entités) — pas d'horloge injectable, donc non testable.
- **Règles nationales / datées** : `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`.

### 4.3 Persistance et intégrité

| Sujet | Constat | Preuve |
|---|---|---|
| **Argent en flottant** | Tous les montants (`Sale`, `SaleItem`, `OrderItem`, `Product`) en `HasColumnType("REAL")` | [SaleConfiguration.cs:28-44](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L28-L44), [ProductConfiguration.cs:32-40](../../src/MMV.Infrastructure/Data/Configurations/ProductConfiguration.cs#L32-L40), [SaleItemConfiguration.cs:29-33](../../src/MMV.Infrastructure/Data/Configurations/SaleItemConfiguration.cs#L29-L33), [OrderItemConfiguration.cs:28-44](../../src/MMV.Infrastructure/Data/Configurations/OrderItemConfiguration.cs#L28-L44) |
| Incohérence decimal | Les grilles verres utilisent `HasPrecision(5,2)` (decimal) — incohérent avec REAL ailleurs | [GlassPricingTierConfiguration.cs:18-22](../../src/MMV.Infrastructure/Data/Configurations/GlassPricingTierConfiguration.cs#L18-L22) |
| **`EnsureCreated` au runtime** | `DbInitializer.Initialize` appelle `context.Database.EnsureCreated()` → **migrations ignorées** | [DbInitializer.cs:16](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L16) |
| Valeur par défaut figée | `HasDefaultValue(DateTime.UtcNow)` capture une constante au build du modèle (pas `CURRENT_TIMESTAMP`) | [SaleConfiguration.cs:26](../../src/MMV.Infrastructure/Data/Configurations/SaleConfiguration.cs#L26), [CustomerConfiguration.cs:48-52](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L48-L52) |
| **Pas de concurrence optimiste** | aucun `IsConcurrencyToken`/`IsRowVersion` | `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` (0 dans le snapshot) |
| Numérotation par `Random` | `VTE-{yyyy}-{Random.Next(10000):D4}` / `CMD-…` face à index UNIQUE | [SaleFormViewModel.cs:835](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L835), [:896](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L896) ; UNIQUE : snapshot l.316, l.630 |
| Transaction de vente | plusieurs `SaveChangesAsync` sans `BeginTransaction` | [SaleFormViewModel.cs:884-965](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L884-L965) |
| `UnitOfWork.RollbackAsync` | « rollback » = `DisposeAsync()` du contexte (trompeur) | [UnitOfWork.cs:72-75](../../src/MMV.Infrastructure/Repositories/UnitOfWork.cs#L72-L75) |
| `BaseRepository.UpdateAsync` | `_dbSet.Update` (graphe complet) + `Task.CompletedTask` « simulation async » | [BaseRepository.cs:62-69](../../src/MMV.Infrastructure/Repositories/BaseRepository.cs#L62-L69) |
| Cascades | `Customer → Prescriptions` en `Cascade` (suppression de données de santé) | [CustomerConfiguration.cs:61-64](../../src/MMV.Infrastructure/Data/Configurations/CustomerConfiguration.cs#L61-L64) |
| Index uniques | `Sale.SaleNumber`, `Order.OrderNumber`, `Product.Reference`, `User.Username`, `ProductCategory.Name` | snapshot l.316/528/554/630/833 |
| Seed = données de démo | 50 clients/100 produits/60 ventes factices au 1er démarrage | [DbInitializer.cs:13-87](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L13-L87) ; lancé en prod [App.axaml.cs:158](../../src/MMV.App/App.axaml.cs#L158) |
| Seeder mort | `DbSeeder` (≠ `DbInitializer`) non référencé | `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` |
| Sauvegarde/restauration | aucune stratégie | `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` |
| Migrations présentes | 7 migrations (2026-01-27 → 2026-02-12) + snapshot | dossier `Migrations/` |

### 4.4 Multi-organisation et multi-magasin

- **Capacité actuelle : nulle.** Aucun `OrganizationId`/`StoreId`/`TenantId` dans le code (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` — seule occurrence dans un doc Markdown, pas dans `src/`).
- Utilisateurs non rattachés à un magasin ([User.cs](../../src/MMV.Domain/Entities/User.cs)), stock global, numérotation globale, pas de filtrage de requêtes par tenant.
- Trois approches d'isolation comparées dans [`adr-candidates.md`](adr-candidates.md) (colonne discriminante partagée / schéma par tenant / base par organisation) — **aucune n'est implémentée**, conformément à l'interdiction Phase 1.
- **Nuance Phase 1B (modèles de déploiement)** : pour un déploiement **réellement mono-organisation** (un opticien, un poste), l'absence d'isolation inter-tenant n'est pas un défaut bloquant en soi. Elle devient une **fondation indispensable** dès le **multi-organisation** et pour le futur **SaaS B2B**. L'audit traite donc R-01 comme bloquant *pour la cible multi-pays/SaaS*, pas comme une faille du produit mono-poste actuel.

### 4.5 Internationalisation et localisation

| Point | Constat |
|---|---|
| Ressources `.resx` | **Aucune** (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`) |
| `x:Uid` / mécanisme i18n Avalonia | **Aucun** |
| Chaînes codées en dur | **606 littéraux** (`Text=`/`Watermark=`/`Header=`/`Content=`) dans 38 vues `.axaml` |
| Chaînes FR dans le code | options de paiement, messages d'erreur, etc. ([SaleFormViewModel.cs:137-140](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L137-L140), [:681](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L681)) |
| Culture / formats | `CultureInfo` n'apparaît que dans les signatures auto-générées des `IValueConverter` — pas de gestion de culture applicative |
| RTL (`FlowDirection`) | **Aucun** → arabe impossible |
| Recherche latin/arabe | recherche `ToLower().Contains` ([SaleFormViewModel.cs:649-653](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L649-L653)) — pas de normalisation/translittération |
| Libellés catalogue | stockés en clair mono-langue (FR) dans les entités |

### 4.6 Fiscalité, facturation et politiques nationales

- **Aucune abstraction fiscale** : pas de `TaxRate`, `VatCategory`, `TaxPolicy` (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`). Le calcul de vente ([SaleService.CalculateSaleAsync:63-84](../../src/MMV.Domain/Services/SaleService.cs#L63-L84) et `CalculateFinalAmount` du VM) ne connaît **que** total − remise, **sans TVA**.
- **Aucune entité Facture** : la `Sale` n'a ni mentions légales, ni distinction B2C/B2B, ni ICE/BCE, ni représentation PDF, ni canal d'échange (Peppol). `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT`.
- **Aucun barème de remboursement, aucune validité de prescription, aucune périodicité de prise en charge** modélisés.
- Conformément à la Phase 0.5D, **aucune valeur de gate ne doit être codée** : l'audit confirme qu'aucune n'est codée (ce qui est correct), mais aussi qu'**aucun point d'extension n'existe** pour les recevoir. Les approches (configuration versionnée / services de politique / tables de règles / combinaison) sont comparées dans `adr-candidates.md`.
- **Précision Phase 1B — l'« absence de fiscalité » n'est pas un « taux 0 % ».** Un taux de 0 % (exonération, hors-champ) est une **vraie règle fiscale** à configurer, pas un défaut technique. La capacité métier attendue doit donc distinguer conceptuellement les états : **`NotConfigured`** (aucune politique fiscale pour le marché/la catégorie/la date → **aucune facture fiscale définitive émissible** ; seuls brouillon, simulation ou tests avec politique fictive sont permis), **`Configured`** (politique valide et applicable) et **`InvalidOrExpired`** (politique périmée/incohérente → blocage de l'émission définitive). Aucune classe définitive n'est imposée ici ; c'est une capacité à concevoir (cf. ADR-007).

### 4.7 Sécurité et protection des données

- **Authentification** : BCrypt work factor 11, `Verify` avec gestion `SaltParseException` ([AuthenticationService.cs:50-72](../../src/MMV.Infrastructure/Services/AuthenticationService.cs#L50-L72)). Correct pour un desktop.
- **Mot de passe admin par défaut faible** : hash de `"admin"` seedé ([DbInitializer.cs:93-101](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L93-L101)). Tous les comptes de démo partagent ce hash.
- **Politique de mot de passe** : `UserValidator.ValidatePasswordPolicy` (8+, maj/min/chiffre) existe ([UserValidator.cs:37-55](../../src/MMV.Domain/Validators/UserValidator.cs#L37-L55)), mais `GenerateStrongPassword` utilise `System.Random` **non cryptographique** ([UserValidator.cs:89](../../src/MMV.Domain/Validators/UserValidator.cs#L89)).
- **Autorisation purement UI** : `PermissionService` (matrice de rôles codée en dur, par chaînes) vit dans `MMV.App` ([PermissionService.cs](../../src/MMV.App/Services/PermissionService.cs)) ; **aucune** application au niveau domaine/repository → un appel direct au service/repo n'est pas contrôlé.
- **Pas de cloisonnement par organisation/magasin** (corollaire de 4.4). **Nuance selon le modèle de déploiement** : non applicable à un déploiement réellement **mono-organisation** (desktop d'un seul opticien) ; en revanche **fondation indispensable** dès qu'il y a multi-organisation, et **a fortiori** pour le futur **SaaS** (sans quoi : risque de fuite inter-tenant).
- **Pas de journal d'audit ni de trace d'accès aux données de santé** (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` ; les 6 occurrences de « journal/audit » sont des faux positifs « Journalières »/`JOURNALIERE`). **À qualifier, pas à conclure** : il s'agit d'une **lacune d'« accountability » et de sécurité à fort risque** pour des données de santé (catégorie particulière RGPD côté BE ; sensible loi 09-08/CNDP côté MA), **probablement nécessaire** mais dont la portée exacte relève d'une **analyse de risque et d'une validation juridique locale** — ce n'est pas une preuve de non-conformité en soi.
- **Chiffrement au repos** : aucun ; SQLite local en clair, données de santé (ordonnances) et données sociales (`SocialSecurityNumber`) non protégées.
- **Rétention / export / suppression** : `DeleteCustomerAsync` supprime en cascade les ordonnances ; pas de gestion de rétention ni d'export des preuves.
- **Session** : timeout d'inactivité 30 min ([SessionService.cs:20](../../src/MMV.App/Services/SessionService.cs#L20)), en mémoire, sans audit d'événement.
- **Pas de limitation des tentatives de connexion / verrouillage** : `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` (0 `lockout`/`throttle`/`failedAttempt` dans `src/`). `AuthenticateAsync` ne compte pas les échecs ([AuthenticationService.cs:25-47](../../src/MMV.Infrastructure/Services/AuthenticationService.cs#L25-L47)).
- **Pas de changement obligatoire du mot de passe initial** : `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` ; l'admin seedé (« admin ») reste utilisable tel quel.
- **Dépendances vulnérables (vérifié par commande, cf. §2.5)** : packages transitifs **High** (`System.Text.Json 8.0.0`, `Microsoft.Extensions.Caching.Memory 8.0.0`, `Tmds.DBus.Protocol 0.20.0`, et dans les tests `System.Net.Http 4.3.0`, `System.Text.RegularExpressions 4.3.0`).
- **Livraison / packaging (analysé en Phase 1B)** : `app.manifest` + `OutputType=WinExe` présents, mais **aucun packaging/installeur, aucune signature de binaire, aucun mécanisme de mise à jour ni de vérification d'intégrité** implémentés (`ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` — les 4 occurrences `velopack/squirrel/WiX/MSIX/PublishSingleFile` sont **uniquement dans la documentation** `ARCHITECTURE.md`/`SPRINTS.md`/`docs/SAAS_LICENSING.md`, pas dans la configuration de build).
- **Stockage des secrets** : la chaîne de connexion est en clair dans le code ; aucune gestion de secret, base SQLite non chiffrée. **Logs/diagnostics** : sorties `Debug.WriteLine` ([App.axaml.cs:153-165](../../src/MMV.App/App.axaml.cs#L153-L165)) sans données personnelles observées, mais aucune politique de journalisation maîtrisant les données sensibles.
- Mini threat model en [`risk-register.md` §Threat model](risk-register.md#threat-model).

### 4.8 Performance et résilience

- **Lectures complètes non paginées (démontré)** : `GetAllAsync` charge toute la table en `AsNoTracking` ([BaseRepository.cs:42-47](../../src/MMV.Infrastructure/Repositories/BaseRepository.cs#L42-L47)), puis filtrage **en mémoire** côté VM ([SaleFormViewModel.cs:413-414](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L413-L414), [:445-519](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L445-L519)). Ne tient pas à l'échelle multi-magasin.
- **Lazy loading désactivé → pas de N+1 par proxies (corrigé en Phase 1B)** : `ABSENCE_VÉRIFIÉE_PAR_RECHERCHE_DU_DÉPÔT` — aucun `UseLazyLoadingProxies`, aucun `ILazyLoader`, aucun package `Microsoft.EntityFrameworkCore.Proxies` (0 occurrence). Les propriétés `virtual` **ne déclenchent donc pas** de chargement paresseux : **le lien causal « `virtual` ⇒ N+1 » est retiré**. Le chargement est au contraire **eager via `.Include()`/`.ThenInclude()`** dans les repositories ; certains `Include` sont **larges et sur-collectent** (ex. [ProductRepository.cs:27-28](../../src/MMV.Infrastructure/Repositories/ProductRepository.cs#L27-L28) charge `OrderItems → Order` pour chaque produit). Aucun N+1 par proxies n'est démontré ; seuls le chargement complet de table + filtrage mémoire et l'over-fetching d'`Include` le sont.
- **Décrément de stock en lecture-modification-écriture** sans jeton de concurrence ([SaleFormViewModel.cs:938-948](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L938-L948)) → mises à jour perdues.
- **Idempotence / retries** : aucun (pertinent pour futurs connecteurs INAMI/Peppol/AMO).
- `async void ExecuteSave` ([SaleFormViewModel.cs:809](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L809)) : exceptions difficiles à propager (capturées localement, sinon crash).

### 4.9 Tests et qualité

- Suite réelle : 192 tests (187 ✅ / 5 ❌). Bonne base (xUnit + FluentAssertions + Moq) mais :
  - couvre des chemins **non utilisés en production** (services), pas `ExecuteSave` ;
  - aucune injection d'horloge/identifiants → non déterministe sur le temps ;
  - tests de VO **morts** (Money/Address/Email/PhoneNumber testés mais inutilisés) ;
  - fichiers obsolètes laissés : `ProductDetailView.old.axaml.cs`, `CustomerRepositoryTests.cs.old` ;
  - vues dupliquées (`Views/UsersView.axaml` et `Views/Users/UsersView.axaml`, `Views/UserProfileView.axaml` et `Views/Users/UserProfileView.axaml`).

---

## 5. Confrontation avec le référentiel métier (Étape D)

Statuts : `PRESENT_REUSABLE`, `PRESENT_COUPLED`, `PARTIAL`, `ABSENT`, `INCORRECT_ASSUMPTION`, `NOT_VERIFIED`. Matrice **reconstruite à partir du code actuel**.

Colonnes (complétées en Phase 1B) : **Statut · Preuve · Risque · Dette · Impact BE · Impact MA · Remédiation · Prérequis · Priorité · Confiance**. La dette est exprimée en effort relatif `S/M/L/XL` ; la confiance en `Élevée/Moyenne/Faible`.

| Capacité (réf. 0.5D) | Statut | Preuve dépôt | Risque | Dette | Impact BE | Impact MA | Options de remédiation | Prérequis | Priorité | Confiance |
|---|---|---|---|---|---|---|---|---|---|---|
| Organisation / Magasin / Pays | **ABSENT** | 0 `OrganizationId/StoreId/Country` sur entités (recherche globale) | R-01 | XL | pas de multi-site/pays | idem + CNDP | ADR-002 | — | Fondation A (cible) | Élevée |
| Devise & arrondis | **ABSENT** (concept) / **INCORRECT_ASSUMPTION** (VO) | `Sale`/`Product` sans devise ; `Money` EUR inutilisé ([Money.cs:11](../../src/MMV.Domain/ValueObjects/Money.cs#L11)) | R-02 | L | EUR implicite | **MAD impossible** | ADR-004 | — | Fondation A | Élevée |
| Montant monétaire fiable | **INCORRECT_ASSUMPTION** | montants en `REAL` (flottant) | R-02 | L | erreurs d'arrondi | idem | ADR-004 | — | Fondation A | Élevée |
| Localisation & RTL | **ABSENT** | 0 `.resx`, 606 littéraux FR, 0 `FlowDirection` | R-07 | L | NL/DE impossibles | AR/RTL impossible | ADR-008 | — | Fondation (lot dédié) | Élevée |
| Temps & règles datées | **PARTIAL/PRESENT_COUPLED** | 23 `Now` / 59 `UtcNow`, pas d'horloge injectable | R-15 | M | fuseau Bruxelles non géré | Casablanca non géré | ADR-005 | — | Fondation A | Élevée |
| Client / porteur / payeur | **PARTIAL** | `Customer` unique, pas de payeur tiers ([Customer.cs](../../src/MMV.Domain/Entities/Customer.cs)) | R-11 | L | tiers/mineurs mal modélisés | idem | refonte modèle (lot dédié) | fondations | Fondation (lot dédié) | Élevée |
| Identifiant national | **INCORRECT_ASSUMPTION** | `SocialSecurityNumber` string nullable, **non unique** ([Customer.cs:56](../../src/MMV.Domain/Entities/Customer.cs#L56)) | R-11 | M | RRN mal typé | CIN/CNSS confondus | typage des identifiants | client/porteur/payeur | Critique | Élevée |
| Organisme / régime / droits | **PARTIAL (texte libre)** | `InsuranceName` string ([Customer.cs:61](../../src/MMV.Domain/Entities/Customer.cs#L61)) | R-14 | M | mutualité non structurée | AMO/régime non structurés | modèle organisme générique | client/porteur/payeur | Fondation/P0 | Élevée |
| Adresse avec pays | **PARTIAL** | `Customer` sans pays ; `Address` VO (avec `Country`) inutilisé | R-11 | S | validation locale limitée | idem | brancher pays | — | Haute | Élevée |
| Fiscalité / TVA configurable | **ABSENT** | aucune abstraction ; calcul sans TVA ([SaleService.cs:63-84](../../src/MMV.Domain/Services/SaleService.cs#L63-L84)) | R-06 | L | facture incomplète | facture incomplète | ADR-007 + cycle `NotConfigured/Configured/Invalid` | monétaire | Critique (valeurs = gates) | Élevée |
| Facture (modèle structuré) | **ABSENT** | pas d'entité Facture (« FACTURE » = simple en-tête lié à `SaleNumber`) | R-06 | L | B2C/B2B non séparés | mentions/ICE non gérées | ADR-007 | monétaire+fiscalité | Critique | Élevée |
| Facture B2B structurée / Peppol | **ABSENT** | aucun connecteur | R-06 | XL | obligation BE non couverte | gate future | connecteurs découplés (ADR-007) | facture | P0 BE | Élevée |
| Documents de remboursement | **ABSENT** | aucun modèle | R-14 | L | attestation de délivrance absente | dossier MA absent | modèles configurables | fondations | P0/gate | Élevée |
| Numérotation fiable | **INCORRECT_ASSUMPTION** | `Random` + index unique | R-03 | M | collisions/audit | collisions/audit | ADR-006 | org/magasin | Critique | Élevée |
| Prescription OD/OG | **PRESENT_REUSABLE** | [Prescription.cs](../../src/MMV.Domain/Entities/Prescription.cs) | faible | S | réutilisable | réutilisable | ajouter validité/contexte | — | Vérifier | Élevée |
| Validité de prescription | **ABSENT** | pas de champ validité | gate | S | gate `G-PRESC-BE` | gate `G-PRESC-MA` | politique datée | couche App | Gate (non bloquant audit) | Élevée |
| Éligibilité / périodicité remb. | **ABSENT** | aucun moteur de règles | R-14 | L | INAMI à versionner | gate `G-R-MA` | politiques datées (ADR-001) | couche App | P0 BE / gate MA | Élevée |
| Mesures de centrage | **ABSENT** | pas d'EP/hauteur dans les entités | moyen | M | requis fabrication | requis fabrication | ajouter champs | — | P0 | Élevée |
| Prix & traitements | **PARTIAL** | suppléments/grilles verres ([DbInitializer.cs:553-631](../../src/MMV.Infrastructure/Data/DbInitializer.cs#L553-L631)) en `REAL`, sans taxe | R-02 | M | partiel | partiel | monétaire + fiscalité | monétaire | P0 | Élevée |
| Stock | **PRESENT_REUSABLE / PARTIAL** | `StockMovement` global, **sans magasin** | R-01/R-09 | M | réutilisable mono-site | idem | stock par magasin | org/magasin | Vérifier | Élevée |
| Atelier / livraison | **PARTIAL** | `OrderStatus` workflow ([OrderService.cs:76-88](../../src/MMV.Domain/Services/OrderService.cs#L76-L88)) | moyen | M | partiel | partiel | étendre workflow | — | P1 | Moyenne |
| SAV / réparation / garantie | **ABSENT** | aucune entité | moyen | L | absent | absent | nouveau modèle | fondations | P1 | Élevée |
| Sécurité & audit | **PARTIAL** | auth OK ; **pas d'audit** ; authz UI-only | R-08/R-13 | L | « accountability » RGPD à renforcer | loi 09-08/CNDP à renforcer | ADR-009 (lot dédié) | couche App + tenant | Fondation (lot dédié) | Élevée |
| Isolation des données | **ABSENT** (selon déploiement) | pas de tenant | R-01 | XL | mélange si multi-org | idem + CNDP | ADR-002 | org/magasin | Fondation (SaaS) | Élevée |
| Transferts de données | **NOT_VERIFIED** | hébergement local, pas de politique | R-08 | M | RGPD | art. 43-44 | politique de déploiement | déploiement | Gate déploiement | Faible |
| Logique métier (placement) | **PRESENT_COUPLED** | dans le VM ([SaleFormViewModel.cs:809-980](../../src/MMV.App/ViewModels/SaleFormViewModel.cs#L809-L980)) | R-05 | L | règles non testables | idem | couche Application (ADR-001) | — | Critique | Élevée |
| Services de domaine | **PRESENT_COUPLED (contournés)** | non enregistrés/inutilisés (double DI) | R-05 | M | règles contournables | idem | DI unifiée | couche App | Haute | Élevée |
| Politiques nationales extensibles | **ABSENT** | aucun point d'extension | R-14 | L | — | — | ADR-001 | couche App | Fondation/P0 | Élevée |
| Barèmes versionnés | **ABSENT** | grilles verres figées au seed | R-14 | L | INAMI 2026 non importable | TNR non importable | import/config datés | fondations | P0 BE / gate MA | Élevée |

### 5.1 Hypothèses de l'ancienne matrice corrigées

- « `Customer.SocialSecurityNumber` **unique** » → **faux** : aucune contrainte d'unicité ni index sur ce champ (vérifié dans le snapshot).
- « `Money` aurait une valeur EUR par défaut » → **vrai mais sans portée** : la valeur EUR par défaut existe ([Money.cs:11](../../src/MMV.Domain/ValueObjects/Money.cs#L11)) mais `Money` est **inutilisé** ; le vrai problème est l'**absence totale de devise** sur les entités et le **stockage REAL**.
- « `Random` dans le flux de vente » → **confirmé et aggravé** par l'index UNIQUE (collision = échec de vente).
- « services présents mais non câblés » → **confirmé** (double DI, `AddInfrastructure` jamais appelé).

---

## 6. Principaux constats (détail et sévérité dans `risk-register.md`)

- **BLOCKER** : fondations multi-pays absentes (org/magasin/pays/devise/i18n) ; argent en flottant ; persistance via `EnsureCreated` contournant les migrations (+ 2 chemins DB physiques) ; suite de tests rouge.
- **CRITICAL** : numérotation `Random` vs index unique ; logique métier dans les VM + services contournés ; absence de TVA/facture/audit ; absence de localisation/RTL.
- **HIGH** : pas de jeton de concurrence ; entités anémiques + VO morts ; cascade de suppression des données de santé ; authz UI-only ; pas de CI ; **dépendances transitives vulnérables (High)** ; **packaging/signature/mise à jour absents** (livraison).
- **MEDIUM/LOW** : seed de démo en prod, mot de passe par défaut faible, **pas de limitation des tentatives de connexion**, **pas de changement obligatoire du mot de passe initial**, `System.Random` pour mots de passe, fichiers/vues obsolètes, valeurs `HasDefaultValue(DateTime.UtcNow)` figées.

---

## 7. Éléments réellement réutilisables

1. **Direction des dépendances projet saine** (`Domain` pur).
2. **Modèle d'ordonnance OD/OG riche** (`Prescription`) — base optique solide.
3. **Abstraction repository + Unit of Work** (interfaces dans le domaine) — réutilisable après assainissement (transactions, concurrence).
4. **Authentification BCrypt** correcte (work factor 11).
5. **Suite de tests et outillage** (xUnit/FluentAssertions/Moq) — fondation à étendre.
6. **Workflow atelier** (`OrderStatus` + transitions) — réutilisable.
7. **Catalogue verres/lentilles/suppléments/grilles** — structure exploitable une fois le modèle monétaire corrigé.

---

## 8. Gates métier qui restent externes (rappel Phase 0.5D)

`G-TVA-BE`, `G-PRESC-BE`, `G-EINV-BE`, `G-TVA-MA`, `G-R-MA`, `G-CNDP`, `G-FACT-MA`, `G-EINV-MA`, `G-PRESC-MA`, `L-1`. **Aucune valeur correspondante n'est codée dans le dépôt** (vérifié) et **aucune ne doit l'être** : l'architecture cible doit les recevoir comme paramètres datés/versionnés.

---

## 9. Conclusion — verdict scindé (corrigé en Phase 1B)

Le verdict est **scindé en deux décisions distinctes**, déterminées par les preuves de l'audit, pas imposées.

### 9.1 Phase 2A — Stabilisation et fondations techniques : **`GO` (sous conditions)**

Justification (preuves) : le domaine est découplé (`Domain` pur, 0 `using` EF/Avalonia/Infra), la suite de tests et l'outillage existent, le périmètre des manques est cartographié, et **aucune règle réglementaire non confirmée n'est codée**. Rien n'empêche un travail de fondation **non national**.

Conditions de levée des `BLOCKER`, dans l'ordre (cf. [`migration-roadmap.md`](migration-roadmap.md)) :
1. remettre la suite de tests au vert + ajouter une CI (build+test) + traiter les dépendances vulnérables (R-24) ;
2. fixer la stratégie de persistance (décision ADR-003 : migrations vs `EnsureCreated`, chemin DB unique, transactions, jeton de concurrence) ;
3. corriger le modèle monétaire (devise explicite + représentation décimale fiable, ADR-004) **avant** toute logique fiscale ;
4. introduire les fondations `Organisation/Magasin/Pays` puis, par lots distincts, localisation/RTL, client/porteur/payeur, audit/sécurité.

### 9.2 Développement des fonctionnalités nationales Belgique/Maroc : **`NO-GO` à ce stade**

Justification (preuves) : aucune des fondations requises (devise fiable, isolation, i18n/RTL, modèle Facture/fiscalité, audit, politiques datées) n'existe, et **les gates métier Phase 0.5D ne sont pas levées**. Démarrer un vertical pays maintenant coderait des hypothèses non confirmées ou bâtirait sur des fondations absentes.

**Condition de bascule vers `GO` national** : livraison vérifiée des fondations 2A **et** levée des gates concernées par des experts locaux (fiscaliste, juriste, autorités). Le premier vertical recommandé (roadmap §7) reste **non national** et commun BE/MA.

Aucune implémentation n'est engagée. Les corrections de Phase 1B portent **uniquement** sur les documents `docs/architecture/`.
