# Plan de Développement - ManageMyVision (MMV)

> **Projet** : Application de gestion pour opticiens  
> **Stack** : .NET 8 + Avalonia UI + SQLite + EF Core  
> **Architecture** : Clean Architecture + MVVM  
> **Démarrage** : Janvier 2026

---

## 📋 Vue d'Ensemble des Sprints

| Sprint | Statut | Objectif Principal | Durée Estimée |
|--------|--------|-------------------|---------------|
| **Sprint 1** | ✅ **TERMINÉ** | Infrastructure + Entities | 1 semaine |
| **Sprint 2** | ✅ **TERMINÉ** | DbContext + Migrations + Repositories | 1-2 semaines |
| **Sprint 3** | ✅ **TERMINÉ** | Configuration DI + Services Métier | 1-2 semaines |
| **Sprint 4** | 🚧 **EN COURS** | Interface Avalonia - Structure & Navigation | 2 semaines |
| **Sprint 5** | ⏳ Planifié | Module Gestion Clients (CRM) | 2 semaines |
| **Sprint 6** | ⏳ Planifié | Module Gestion Produits & Stock | 2 semaines |
| **Sprint 7** | ⏳ Planifié | Module Ordonnances Médicales | 1-2 semaines |
| **Sprint 8** | ⏳ Planifié | Module Commandes (Workflow Atelier) | 2-3 semaines |
| **Sprint 9** | ⏳ Planifié | Module Point de Vente (POS/Caisse) | 2-3 semaines |
| **Sprint 10** | ⏳ Planifié | Gestion Utilisateurs & Authentification | 1-2 semaines |
| **Sprint 11** | ⏳ Planifié | Tableaux de Bord & Rapports | 2 semaines |
| **Sprint 12** | ⏳ Planifié | Tests, Optimisation & Déploiement | 2-3 semaines |

**Durée totale estimée** : 18-26 semaines (~4-6 mois)

---

## Sprint 1 : Infrastructure + Entities ✅ **TERMINÉ**

### Objectifs
- [x] Créer la structure de la solution (.NET 8)
- [x] Configurer les 4 projets (Domain, Infrastructure, App, Tests)
- [x] Créer tous les Enums (8 enums)
- [x] Créer toutes les Entities (11 entités)
- [x] Ajouter un .gitignore adapté
- [x] Configurer les packages NuGet de base
- [x] Point d'entrée Avalonia (Program.cs, App.axaml)

### Livrables
- ✅ Solution MMV.sln avec 4 projets
- ✅ 8 Enums dans `MMV.Domain/Enums/`
- ✅ 11 Entities dans `MMV.Domain/Entities/`
- ✅ Documentation XML complète
- ✅ Compilation réussie (`dotnet build`)

### Notes Importantes
- **Respect strict des règles de mapping SQL → C#**
  - IDs : `long` (pas `int`)
  - Argent : `decimal` (jamais `double`/`float`)
  - Données optiques : `double`
  - Propriétés de navigation : `virtual`

---

## Sprint 2 : DbContext + Migrations + Repositories ✅ **TERMINÉ**

### Objectifs
- [x] Créer `OpticDbContext` avec configuration EF Core
- [x] Configurer le mapping Fluent API pour toutes les entités
- [x] Gérer les conversions Enum ↔ String
- [x] Configurer les relations (FK, cascades)
- [x] Créer la première migration EF Core
- [x] Implémenter les interfaces de repository (Domain)
- [x] Implémenter les repositories concrets (Infrastructure)
- [x] Créer une classe de seed (données de test)
- [ ] Tests unitaires des repositories (reportés Sprint 3)

### Livrables
- `MMV.Infrastructure/Data/OpticDbContext.cs`
- `MMV.Infrastructure/Data/Configurations/` (11 EntityTypeConfiguration)
- `MMV.Infrastructure/Migrations/20260127184542_InitialCreate.*`
- `MMV.Infrastructure/Data/OpticDbContextFactory.cs`
- `MMV.Infrastructure/Seeders/DbSeeder.cs`
- `MMV.Domain/Interfaces/Repositories/` (10 interfaces + IUnitOfWork)
- `MMV.Infrastructure/Repositories/` (9 implémentations + BaseRepository + UnitOfWork)
- Tests dans `MMV.Domain.Tests/RepositoryTests/` (à ajouter)

### Tâches Détaillées

#### 2.1 Configuration EF Core
- Configurer la chaîne de connexion SQLite
- Implémenter `OpticDbContext` avec `DbSet<>` pour chaque entité
- OnModelCreating : Fluent API pour toutes les tables

#### 2.2 Conversions Enum
```csharp
// Exemple: UserRole.Admin → "ADMIN" en base
modelBuilder.Entity<User>()
    .Property(u => u.Role)
    .HasConversion<string>();
```

#### 2.3 Relations & Contraintes
- Configurer les FK avec `OnDelete` approprié
- Index uniques (Username, Reference, OrderNumber, SaleNumber)
- Check constraints si supportés

#### 2.4 Migrations
```bash
dotnet ef migrations add InitialCreate --project src/MMV.Infrastructure --startup-project src/MMV.App
dotnet ef database update --project src/MMV.Infrastructure --startup-project src/MMV.App
```

#### 2.5 Repositories Pattern
Interfaces à créer :
- `IUserRepository`
- `ICustomerRepository`
- `IProductRepository`
- `IProductCategoryRepository`
- `ISupplierRepository`
- `IPrescriptionRepository`
- `IOrderRepository`
- `ISaleRepository`
- `IStockMovementRepository`
- `IUnitOfWork` (transaction management)

### Critères de Succès
- ✅ Base de données créée avec succès
- ✅ Toutes les tables présentes avec contraintes
- ✅ Seed de données fonctionnel
- ✅ Tests de CRUD passent pour chaque repository

---

## Sprint 3 : Configuration DI + Services Métier ✅ **TERMINÉ**

### Objectifs
- [x] Configurer Microsoft.Extensions.DependencyInjection (MMV.Infrastructure/DependencyInjection.cs)
- [x] Créer les services métier (Domain/Application Layer)
- [x] Implémenter les validations (FluentValidation)
- [x] Créer des Value Objects (Money, Email, PhoneNumber)
- [x] Gestion des erreurs et exceptions personnalisées
- [x] Tests unitaires des services, validateurs et value objects (64/64 tests ✅)

### Livrables
- ✅ `MMV.Infrastructure/DependencyInjection.cs`
- ✅ `MMV.Domain/Services/` - 5 services complets
- ✅ `MMV.Domain/Validators/` - 3 validateurs FluentValidation
- ✅ `MMV.Domain/ValueObjects/` - 4 value objects
- ✅ `MMV.Domain/Exceptions/` - Hiérarchie d'exceptions
- ✅ `tests/MMV.Domain.Tests/` - 64 tests unitaires

### Résumé des Tests Implémentés (Sprint 3)

**12 fichiers de test** (64 tests unitaires, tous ✅ PASSANTS) :

1. **ValueObjectTests/** (4 fichiers)
   - MoneyTests: rounding, validation, factory method
   - EmailTests: MailAddress validation, equality
   - PhoneNumberTests: normalization, length enforcement
   - AddressTests: constructor validation, nullable Line2

2. **ValidatorTests/** (3 fichiers)
   - CustomerValidatorTests: FluentValidation for names/email/phone
   - ProductValidatorTests: Reference/name/prices/stock validation
   - PrescriptionValidatorTests: Date range, optical parameter ranges

3. **ServiceTests/** (5 fichiers)
   - CustomerServiceTests: CRUD + timestamp validation
   - ProductServiceTests: CRUD + margin calculation + low stock
   - PrescriptionServiceTests: CRUD + validation + customer history
   - OrderServiceTests: CRUD + status workflow validation
   - SaleServiceTests: CRUD + discount calculation + customer sales

**Statut** : 64/64 tests ✅ - **TOUS PASSANTS**

---

## Sprint 4 : Interface Avalonia - Structure & Navigation 🚧 **EN COURS**

### Objectifs
- [x] Créer la fenêtre principale (MainWindow) avec layout professionnel
- [x] Implémenter MVVM pattern avec BaseViewModel
- [x] Mettre en place le ViewLocator pour résolution automatique des vues
- [x] Créer NavigationService pour navigation type-safe
- [x] Page Dashboard avec statistiques
- [x] Créer 8 vues placeholder pour tous les modules
- [x] Fixer les erreurs XAML de compilation (Grid Padding, x:DataType)
- [x] Build réussi et application lancée
- [ ] Configurer le thème FluentAvalonia
- [ ] Écran de connexion (Login)
- [ ] Commandes de navigation fonctionnelles
- [ ] Transitions entre vues

### Livrables
- ✅ `MMV.App/Views/MainWindow.axaml` - Fenêtre principale avec header, sidebar, content area
- ✅ `MMV.App/ViewModels/MainWindowViewModel.cs` - Gestion navigation + état global
- ✅ `MMV.App/ViewModels/BaseViewModel.cs` - Base MVVM (INotifyPropertyChanged)
- ✅ `MMV.App/Services/NavigationService.cs` - Navigation type-safe avec historique
- ✅ `MMV.App/ViewLocator.cs` - Résolution automatique View/ViewModel
- ✅ `MMV.App/Views/DashboardView.axaml` - Dashboard avec 4 cartes statistiques
- ✅ `MMV.App/ViewModels/DashboardViewModel.cs` - Stats (Clients, Produits, Ventes, Commandes)
- ✅ `MMV.App/ViewModels/PageViewModels.cs` - 7 ViewModels pour modules
- ✅ `MMV.App/Views/CustomersView.axaml` - Vue Clients (placeholder)
- ✅ `MMV.App/Views/ProductsView.axaml` - Vue Produits (placeholder)
- ✅ `MMV.App/Views/PrescriptionsView.axaml` - Vue Ordonnances (placeholder)
- ✅ `MMV.App/Views/OrdersView.axaml` - Vue Commandes (placeholder)
- ✅ `MMV.App/Views/SalesView.axaml` - Vue Point de Vente (placeholder)
- ✅ `MMV.App/Views/ReportsView.axaml` - Vue Rapports (placeholder)
- ✅ `MMV.App/Views/SettingsView.axaml` - Vue Paramètres (placeholder)
- [ ] `MMV.App/Views/LoginView.axaml` (à faire)
- [ ] `MMV.App/Styles/CustomTheme.axaml` (à faire)

### Architecture Implémentée

#### MVVM Pattern
```
BaseViewModel (INotifyPropertyChanged)
├── Title, IsLoading, ErrorMessage
├── SetProperty<T>() helper
└── OnPropertyChanged() notifications

MainWindowViewModel : BaseViewModel
├── NavigationItems (ObservableCollection)
├── CurrentView (BaseViewModel)
└── NavigateTo(type) method

DashboardViewModel : BaseViewModel
├── TotalCustomers
├── TotalProducts
├── TotalSales
└── PendingOrders
```

#### Navigation System
```csharp
INavigationService
├── RegisterViewModel<T>()
├── NavigateTo<T>()
├── GoBack()
└── NavigationHistory (Stack)

ViewLocator : IDataTemplate
└── Build(ViewModel) → View (convention-based)
```

### Structure de Navigation
```
MainWindow
├── Header (#2D3E50 - Dark Blue Grey)
│   └── "📊 ManageMyVision - Gestion Complète pour Opticiens"
├── Sidebar (#34495E - Sidebar Grey)
│   ├── 📊 Tableau de Bord
│   ├── 👥 Clients
│   ├── 📦 Produits
│   ├── 📋 Ordonnances
│   ├── 🏭 Commandes (Atelier)
│   ├── 💰 Ventes (POS)
│   ├── 📈 Rapports
│   └── ⚙️ Paramètres
└── ContentControl (Zone de contenu dynamique)
```

### Dashboard - Cartes Statistiques
- **Carte Clients** : #3498DB (Bleu) - Affiche nombre total clients
- **Carte Produits** : #2ECC71 (Vert) - Affiche nombre total produits
- **Carte Ventes** : #F39C12 (Orange) - Affiche chiffre d'affaires total
- **Carte Commandes** : #E74C3C (Rouge) - Affiche nombre commandes en attente

### Correctifs Techniques Appliqués
1. **Grid Padding** : Remplacé Grid.Padding par Border avec Padding (Avalonia ne supporte pas Padding sur Grid)
2. **ColumnSpacing** : Remplacé par margins individuelles sur les enfants Border
3. **x:DataType** : Ajouté déclarations pour bindings compilés (MainWindowViewModel, DashboardViewModel)
4. **ViewLocator** : Ajouté `using Avalonia.Controls.Templates;` pour IDataTemplate
5. **StringFormat Currency** : Simplifié `{Binding TotalSales}` (format C causait erreurs XAML)

### Tests & Validation
- ✅ Build : 0 erreurs, 1 warning mineur (nullable reference)
- ✅ Application lancée avec succès
- ✅ UI affichée correctement
- ⏳ Navigation entre vues (à tester après implémentation des commandes)

### Tâches Restantes Sprint 4
1. Implémenter RelayCommand/DelegateCommand pour navigation buttons
2. Lier les boutons sidebar aux commandes NavigateTo
3. Ajouter transitions/animations entre vues
4. Créer LoginView avec authentification
5. Configurer thème FluentAvalonia
6. Tests UI (navigation, bindings, états)
│   ├── 💰 Ventes (POS)
│   ├── 📈 Rapports
│   └── ⚙️ Paramètres
└── ContentControl (Zone de contenu dynamique)
```

### Composants Réutilisables
- `MMV.App/Controls/DataGridControl.axaml` (liste paginée)
- `MMV.App/Controls/SearchBox.axaml`
- `MMV.App/Controls/ActionButtons.axaml` (CRUD)
- `MMV.App/Controls/LoadingSpinner.axaml`

---

## Sprint 5 : Module Gestion Clients (CRM)

### Objectifs
- [ ] Liste des clients (DataGrid paginé + recherche)
- [ ] Formulaire création/édition client
- [ ] Fiche détaillée client
- [ ] Historique d'achats du client
- [ ] Export Excel/PDF de la liste
- [ ] Import CSV de clients

### Écrans
1. **CustomersListView** : Liste + filtres + recherche
2. **CustomerFormView** : Formulaire CRUD
3. **CustomerDetailView** : Fiche complète + onglets
   - Informations générales
   - Ordonnances
   - Historique commandes
   - Historique ventes
   - Notes

### Fonctionnalités Clés
- Validation en temps réel (CommunityToolkit.Mvvm)
- Auto-complétion sur champs (ville, assurance)
- Calcul automatique de l'âge depuis date naissance
- Liaison avec prescriptions

---

## Sprint 6 : Module Gestion Produits & Stock

### Objectifs
- [ ] Catalogue produits (liste + filtres par catégorie)
- [ ] CRUD produits
- [ ] Gestion catégories
- [ ] Gestion fournisseurs
- [ ] Alertes stock bas (notifications)
- [ ] Mouvements de stock (IN/OUT/ADJUSTMENT)
- [ ] Inventaire (scan/comptage)
- [ ] Étiquettes code-barres

### Écrans
1. **ProductsListView** : Catalogue avec vignettes
2. **ProductFormView** : Formulaire produit
3. **CategoriesView** : Gestion catégories
4. **SuppliersView** : Gestion fournisseurs
5. **StockMovementsView** : Historique mouvements
6. **InventoryView** : Interface d'inventaire

### Fonctionnalités Clés
- **Calcul automatique de marge** : (SalePrice - PurchasePrice) / PurchasePrice
- **Indicateur visuel stock** : 🔴 (stock < seuil) / 🟢 (stock OK)
- **Stockage JSON pour TechnicalSpecs** (propriétés dynamiques)
- **Gestion des images produits** (stockage dans dossier local)

---

## Sprint 7 : Module Ordonnances Médicales

### Objectifs
- [ ] Liste des ordonnances par client
- [ ] Formulaire de saisie ordonnance
- [ ] Validation des valeurs optiques
- [ ] Visualisation graphique (schéma œil)
- [ ] Impression ordonnance
- [ ] Historique des corrections

### Écrans
1. **PrescriptionsListView** : Liste avec recherche par client
2. **PrescriptionFormView** : Saisie OD/OG structurée
3. **PrescriptionDetailView** : Visualisation + impression

### Fonctionnalités Clés
- **Validation métier** :
  - Sphère : -20 à +20
  - Cylindre : -6 à +6
  - Axe : 0 à 180°
  - Addition : 0 à +4
- **Auto-complétion nom médecin**
- **Suggestion de verres** basée sur la prescription
- **Lien direct vers création de commande**

---

## Sprint 8 : Module Commandes (Workflow Atelier)

### Objectifs
- [ ] Création de commande (lien client + prescription)
- [ ] Ajout d'articles à la commande (monture, verres, accessoires)
- [ ] Workflow de statut (Kanban ou liste)
- [ ] Fiche de fabrication (impression atelier)
- [ ] Suivi du délai (indicateur retard)
- [ ] Notifications de changement de statut
- [ ] Contrôle qualité (checklist)

### Écrans
1. **OrdersListView** : Liste + filtres par statut
2. **OrderFormView** : Création commande
3. **OrderDetailView** : Suivi + actions
4. **OrderKanbanView** : Vue Kanban (NEW → DELIVERED)
5. **FabricationSheetView** : Bon atelier imprimable

### Workflow Statuts
```
NEW → TO_FABRICATE → IN_PROGRESS → QUALITY_CHECK → READY → DELIVERED
```

### Fonctionnalités Clés
- **Numérotation automatique** : CMD-YYYY-XXXX
- **Calcul automatique TotalAmount** (somme OrderItems)
- **Alerte dépassement délai**
- **Mise à jour stock automatique** (fabrication = sortie verres)
- **Impression code QR** (traçabilité)

---

## Sprint 9 : Module Point de Vente (POS/Caisse)

### Objectifs
- [ ] Interface caisse (rapide, tactile)
- [ ] Ajout produits au panier
- [ ] Scan code-barres
- [ ] Calcul remise (%, montant fixe)
- [ ] Choix moyen de paiement
- [ ] Impression ticket de caisse
- [ ] Historique des ventes
- [ ] Remboursements/Avoir
- [ ] Clôture de caisse

### Écrans
1. **POSView** : Interface caisse complète
   - Recherche produit
   - Panier en temps réel
   - Calculs automatiques
2. **SalesHistoryView** : Liste des ventes
3. **RefundView** : Gestion remboursements
4. **CashRegisterView** : Clôture caisse

### Fonctionnalités Clés
- **Calculs automatiques** :
  - TotalAmount = Σ(quantity × unitPrice)
  - FinalAmount = TotalAmount - DiscountAmount
- **Raccourcis clavier** (F1-F12 pour actions rapides)
- **Impression automatique** du ticket
- **Mise à jour stock** immédiate après vente
- **Statistiques caisse** (espèces, cartes, totaux)

---

## Sprint 10 : Gestion Utilisateurs & Authentification

### Objectifs
- [ ] Écran de connexion sécurisé
- [ ] Hachage mot de passe (BCrypt/Argon2)
- [ ] Gestion des rôles (ADMIN, OPTICIAN, TECHNICIAN)
- [ ] Permissions par module
- [ ] CRUD utilisateurs (admin uniquement)
- [ ] Historique des connexions
- [ ] Changement de mot de passe
- [ ] Session utilisateur (singleton)

### Écrans
1. **LoginView** : Écran de connexion
2. **UsersManagementView** : Liste + CRUD utilisateurs
3. **UserProfileView** : Profil + changement mot de passe

### Sécurité
- **Hachage** : Utiliser BCrypt.Net ou Argon2
- **Politique de mot de passe** :
  - Minimum 8 caractères
  - Majuscule + minuscule + chiffre
- **Timeout session** : 30 minutes d'inactivité
- **Audit trail** : Logger les actions sensibles

### Permissions par Rôle

| Module | ADMIN | OPTICIAN | TECHNICIAN |
|--------|-------|----------|------------|
| Clients | ✅ CRUD | ✅ CRUD | 🔍 Lecture |
| Produits | ✅ CRUD | ✅ CRUD | 🔍 Lecture |
| Ordonnances | ✅ CRUD | ✅ CRUD | 🔍 Lecture |
| Commandes | ✅ CRUD | ✅ CRUD | ✅ Mise à jour statut |
| Ventes | ✅ CRUD | ✅ CRUD | ❌ Aucun |
| Utilisateurs | ✅ CRUD | ❌ Aucun | ❌ Aucun |
| Rapports | ✅ Tous | 🔍 Lecture | ❌ Aucun |

---

## Sprint 11 : Tableaux de Bord & Rapports

### Objectifs
- [ ] Dashboard principal (KPIs)
- [ ] Graphiques (LiveCharts/OxyPlot)
- [ ] Rapports de ventes (quotidien, mensuel, annuel)
- [ ] Rapport de stock (valorisation)
- [ ] Rapport commandes (délais moyens)
- [ ] Top clients/produits
- [ ] Export Excel/PDF des rapports

### KPIs Dashboard
- **Ventes du jour** : Chiffre d'affaires journalier
- **Commandes en cours** : Nombre par statut
- **Stock critique** : Produits sous seuil
- **Clients du mois** : Nouveaux clients
- **CA mensuel** : Graphique évolution
- **Top 5 produits** : Meilleures ventes

### Rapports à Implémenter
1. **Rapport Ventes** :
   - CA par période
   - Répartition par moyen de paiement
   - Ventes par vendeur
   
2. **Rapport Stock** :
   - Valorisation du stock (quantité × prixAchat)
   - Produits à rotation lente
   - Mouvements du mois

3. **Rapport Commandes** :
   - Délai moyen de fabrication
   - Taux de respect délai
   - Répartition par statut

4. **Rapport Clients** :
   - Top clients (CA généré)
   - Nouveaux clients
   - Taux de fidélisation

---

## Sprint 12 : Tests, Optimisation & Déploiement

### Objectifs
- [ ] Tests unitaires (couverture > 80%)
- [ ] Tests d'intégration (repositories)
- [ ] Tests UI (Avalonia.HeadlessXunit)
- [ ] Optimisation performance (requêtes EF Core)
- [ ] Gestion des erreurs (GlobalExceptionHandler)
- [ ] Logging (Serilog)
- [ ] Configuration production
- [ ] Script de déploiement
- [ ] Documentation utilisateur
- [ ] Formation utilisateurs

### Tests à Compléter

#### Tests Unitaires
- Tous les services métier
- Validateurs FluentValidation
- ValueObjects
- Logique métier complexe

#### Tests d'Intégration
- Repositories avec base de test
- Transactions (UnitOfWork)
- Migrations

#### Tests UI
- Navigation
- Formulaires (validation)
- Workflows complets

### Optimisation
- **Index base de données** (Username, Reference, etc.)
- **Requêtes EF Core** : Utiliser `AsNoTracking()` pour lecture seule
- **Pagination** : Ne pas charger toutes les données
- **Lazy loading** : Désactiver, utiliser `Include()` explicite
- **Caching** : Données statiques (catégories, fournisseurs)

### Logging avec Serilog
```csharp
Log.Information("Vente créée : {SaleNumber}", sale.SaleNumber);
Log.Warning("Stock bas : {ProductName}, Quantité: {Quantity}", product.Name, product.StockQuantity);
Log.Error(ex, "Erreur lors de la sauvegarde commande {OrderId}", orderId);
```

### Configuration Production
- Chaîne de connexion (hors code source)
- Backup automatique de la base SQLite
- Rotation des logs
- Monitoring santé application

### Déploiement
**Options de déploiement** :
1. **Self-contained** : Inclut .NET Runtime
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true
   ```
2. **Framework-dependent** : Nécessite .NET 8 installé
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained false
   ```

**Installateur** :
- Utiliser Inno Setup ou WiX pour créer un setup.exe
- Inclure SQLite bundlé
- Configuration initiale (premier utilisateur admin)

---

## 📦 Fonctionnalités Transversales

Ces fonctionnalités seront intégrées progressivement dans tous les sprints :

### 1. Internationalisation (i18n)
- Support multi-langues (FR/EN)
- Ressources RESX
- Changement de langue à la volée

### 2. Thèmes
- Thème clair/sombre
- Personnalisation couleurs

### 3. Aide Contextuelle
- Tooltips explicatifs
- Documentation intégrée (F1)

### 4. Raccourcis Clavier
- Ctrl+N : Nouveau
- Ctrl+S : Sauvegarder
- Ctrl+F : Rechercher
- Esc : Annuler

### 5. Notifications
- Toasts/Snackbars pour actions
- Alertes système (stock bas, retards)

### 6. Impression
- Tickets de caisse
- Factures
- Bons de commande atelier
- Étiquettes produits

---

## 🚀 Évolutions Futures (Post-V1)

Fonctionnalités à envisager après la version 1.0 :

1. **Mode Hors-ligne** : Synchronisation cloud optionnelle
2. **Application Mobile** : Consultation stocks/commandes (Xamarin/MAUI)
3. **EDI Fournisseurs** : Import automatique catalogues
4. **Télétransmission Sécurité Sociale** : SESAM-Vitale
5. **Gestion Multi-magasins** : Base centralisée
6. **API REST** : Pour intégrations tierces
7. **CRM Avancé** : Marketing, campagnes SMS/Email
8. **Caisse tactile** : Interface tablette optimisée
9. **Business Intelligence** : Tableaux de bord avancés (Power BI)
10. **Intégration Comptable** : Export vers logiciels comptables

---

## 📊 Métriques de Succès

### Critères d'Achèvement de l'Application
- ✅ Toutes les fonctionnalités des sprints 1-12 implémentées
- ✅ Couverture de tests > 80%
- ✅ Documentation complète (technique + utilisateur)
- ✅ Performance : < 2s pour ouverture de chaque module
- ✅ Stabilité : 0 crash critique en production
- ✅ Formation utilisateurs effectuée

---

## 🛠️ Conventions & Bonnes Pratiques

### Commits Git
Format : `[SPRINT-X] Type: Description courte`

Exemples :
- `[SPRINT-2] feat: Add OpticDbContext with Fluent API`
- `[SPRINT-5] fix: Customer search filter not working`
- `[SPRINT-8] refactor: Simplify OrderService workflow logic`

### Branches
- `main` : Production stable
- `develop` : Intégration
- `sprint-X` : Branches par sprint
- `feature/nom-feature` : Fonctionnalités

### Code Review
- Revue systématique avant merge
- Respect des conventions C#
- Tests obligatoires pour nouvelle feature

---

**Document mis à jour le** : 27 janvier 2026  
**Dernière modification** : Sprint 2 en cours
