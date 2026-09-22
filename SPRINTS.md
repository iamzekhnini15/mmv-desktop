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
| **Sprint 4** | ✅ **TERMINÉ** | Interface Avalonia - Structure & Navigation | 2 semaines |
| **Sprint 5** | ✅ **TERMINÉ** | Module Gestion Clients (CRM) | 2 semaines |
| **Sprint 6** | ✅ **TERMINÉ** | Module Gestion Produits & Stock | 2 semaines |
| **Sprint 7** | ✅ **TERMINÉ** | Module Ordonnances Médicales | 2 jours |
| **Sprint 8** | ✅ **TERMINÉ** | Module Commandes (Workflow Atelier) | 2-3 semaines |
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
> Les commandes EF Core d'origine de ce sprint ont été retirées en P4-5E-C9. Elles prenaient `src/MMV.App` comme
> projet de démarrage et utilisaient `dotnet ef database update`, deux pratiques désormais interdites. La procédure
> officielle, pour les chaînes SQLite et PostgreSQL, est dans [CONTRIBUTING.md](CONTRIBUTING.md).

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

## Sprint 4 : Interface Avalonia - Structure & Navigation ✅ **TERMINÉ**

### Objectifs
- [x] Créer la fenêtre principale (MainWindow) avec layout professionnel
- [x] Implémenter MVVM pattern avec BaseViewModel
- [x] Mettre en place le ViewLocator pour résolution automatique des vues
- [x] Créer NavigationService pour navigation type-safe
- [x] Page Dashboard avec statistiques
- [x] Créer 8 vues placeholder pour tous les modules
- [x] Fixer les erreurs XAML de compilation (Grid Padding, x:DataType)
- [x] Build réussi et application lancée
- [x] Écran de connexion (LoginView) avec authentification
- [x] Commandes de navigation fonctionnelles (RelayCommand)
- [x] Tests unitaires LoginViewModel (9/9 tests ✅)
- [x] Composants réutilisables (SearchBox, LoadingSpinner, ActionButtons)

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
- ✅ `MMV.App/Views/LoginView.axaml` - Écran de connexion avec authentification
- ✅ `MMV.App/ViewModels/LoginViewModel.cs` - ViewModel pour login (admin/admin)
- ✅ `MMV.App/Commands/RelayCommand.cs` - Implémentation ICommand
- ✅ `MMV.App/Styles/AppStyles.axaml` - Styles professionnels Apple-inspired
- ✅ `MMV.App/Controls/SearchBox.axaml` - Composant de recherche
- ✅ `MMV.App/Controls/LoadingSpinner.axaml` - Indicateur de chargement
- ✅ `MMV.App/Controls/ActionButtons.axaml` - Boutons CRUD réutilisables
- ✅ `tests/MMV.App.Tests/ViewModels/LoginViewModelTests.cs` - 9 tests unitaires
- ✅ `docs/TESTS_LOGIN.md` - Guide de tests manuels (19 tests)
- ✅ `docs/CORRECTIONS_LOGIN.md` - Documentation des corrections

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
- ✅ Build : 0 erreurs, 0 avertissements
- ✅ Application lancée avec succès
- ✅ UI affichée correctement
- ✅ Navigation entre vues fonctionnelle (8 modules)
- ✅ Login fonctionnel (admin/admin)
- ✅ Tests unitaires : 9/9 tests passés
- ✅ Composants réutilisables créés

### Résumé Sprint 4

**Fonctionnalités Implémentées :**
1. ✅ **Architecture MVVM complète** avec BaseViewModel, RelayCommand, ViewLocator
2. ✅ **Système de navigation** type-safe avec historique
3. ✅ **Écran de login** avec validation, tests unitaires, documentation
4. ✅ **MainWindow professionnelle** : header, sidebar, zone de contenu
5. ✅ **Dashboard** avec 4 cartes statistiques colorées
6. ✅ **8 vues placeholder** pour tous les modules
7. ✅ **Styles Apple-inspired** : couleurs professionnelles, typographie
8. ✅ **Composants réutilisables** : SearchBox, LoadingSpinner, ActionButtons
9. ✅ **Tests complets** : 9 tests unitaires + 19 tests manuels documentés

**Corrections Techniques :**
- Résolution de 8 problèmes critiques dans LoginViewModel
- Gestion correcte du PasswordBox avec masquage
- Notifications CanExecuteChanged pour activation/désactivation du bouton
- Navigation clavier (Tab + Entrée)
- BaseViewModel retourne bool pour SetProperty

**Qualité :**
- 📊 Couverture de tests : 100% du LoginViewModel
- 🔧 0 erreurs de compilation
- 📝 Documentation complète (TESTS_LOGIN.md, CORRECTIONS_LOGIN.md)

**Statut :** ✅ **SPRINT 4 COMPLET - PRÊT POUR SPRINT 5**
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

### Sprint 5 : Module Gestion Clients (CRM) ✅ **TERMINÉ**

### Objectifs
- [x] Liste des clients (DataGrid paginé + recherche)
- [x] Formulaire création/édition client
- [x] Fiche détaillée client (avec 4 onglets)
- [x] Historique d'achats du client
- [x] Correction des patterns Avalonia (Command vs Click)
- [x] Documentation des bonnes pratiques MVVM
- [ ] Export Excel/PDF de la liste (reporté Sprint 6)
- [ ] Import CSV de clients (reporté Sprint 6)

### Écrans
1. ✅ **CustomersView** : Vue principale avec liste + détails
   - DataGrid avec 7 colonnes (Prénom, Nom, Email, Téléphone, Date naissance, Ville, Créé le)
   - Barre de recherche en temps réel (Nom, Email, Téléphone)
   - Pagination avec boutons Précédent/Suivant
   - Boutons CRUD (Nouveau, Modifier, Supprimer, Actualiser, Voir détails)
   - Indicateur de résultats filtrés
   - Gestion de la visibilité (liste vs formulaire vs détails)

2. ✅ **CustomerFormView** : Formulaire CRUD intégré
   - Section Informations personnelles (Prénom*, Nom*, Date naissance)
   - Section Coordonnées (Email, Téléphone)
   - Section Adresse (Adresse, Ville, Code postal)
   - Section Informations médicales (N° Sécu, Mutuelle)
   - Section Notes (Zone de texte multi-lignes)
   - Validation en temps réel avec messages d'erreur
   - Mode création/édition avec titre dynamique

3. ✅ **CustomerDetailView** : Fiche complète avec 4 onglets
   - Onglet "Nouvelle Vente" : Interface de création de vente
   - Onglet "Ordonnances" : Liste des prescriptions du client
   - Onglet "Informations Client" : Vue détaillée des données personnelles
   - Onglet "Historique d'achats" : DataGrid des commandes du client

### Livrables Actuels

#### ViewModels (6 fichiers)
- ✅ `CustomersViewModel.cs` (ViewModel racine)
  - Gestion de l'état global du module (liste/formulaire/détails)
  - Propriété `ShowList` pour visibilité conditionnelle
  - Integration de `IOrderRepository` pour historique
  - Coordination entre les différentes vues

- ✅ `CustomersListViewModel.cs` (324 lignes)
  - Gestion de la liste avec `ObservableCollection<Customer>`
  - Filtrage en temps réel via `SearchText` property
  - Pagination (CurrentPage, PageSize, TotalPages)
  - 7 Commands (Create, Edit, Delete, Refresh, ViewDetails, NextPage, PreviousPage)
  - 3 Events pour navigation inter-vues

- ✅ `CustomerFormViewModel.cs` (408 lignes)
  - Validation en temps réel (FirstName, LastName, Email, Phone)
  - Mode création/édition avec `IsEditMode` flag
  - Commands Save/Cancel avec gestion async
  - Gestion des erreurs par champ

- ✅ `CustomerDetailViewModel.cs`
  - ViewModel parent pour les 4 onglets
  - Intégration avec `IOrderRepository`
  - Propriété `PurchaseHistoryViewModel`
  - Initialisation des données du client

- ✅ `CustomerPurchaseHistoryViewModel.cs`
  - Chargement des commandes via `IOrderRepository.GetByCustomerIdAsync()`
  - ObservableCollection<Order> avec propriétés calculées
  - Gestion des états (loading, error, empty, data)
  - Propriétés `HasOrders` et `HasError` pour UI conditionnelle

#### Views (5 fichiers)
- ✅ `CustomersView.axaml` (gestion de visibilité avec `ShowList`)
  - DataGrid avec 7 colonnes
  - Barre de recherche avec compteur de résultats
  - Pagination dynamique
  - Affichage conditionnel liste/formulaire/détails

- ✅ `CustomerFormView.axaml`
  - 5 sections organisées en cards
  - Validation visuelle avec messages d'erreur
  - Footer avec boutons Command-based

- ✅ `CustomerDetailView.axaml`
  - TabControl avec 4 onglets
  - Nouvelle Vente / Ordonnances / Infos / Historique
  - Binding sur ContentControl pour chaque onglet

- ✅ `CustomerPurchaseHistoryView.axaml`
  - DataGrid avec colonnes : Date, N° Commande, Statut, Montant
  - Panels conditionnels (loading, error, empty, data)
  - Alternance de lignes avec RowBackground

- ✅ `CustomerPurchaseHistoryView.axaml.cs`
  - Code-behind minimal avec InitializeComponent()

### Fonctionnalités Clés Implémentées
- ✅ Validation en temps réel avec affichage des erreurs
- ✅ Calcul automatique du nombre de résultats filtrés
- ✅ Navigation par événements entre ViewModels
- ✅ Fiche détaillée client avec 4 onglets (TabControl)
- ✅ Historique d'achats avec chargement dynamique
- ✅ Visibilité conditionnelle avec propriété calculée `ShowList`
- ✅ Pattern Command pour boutons (pas d'événements Click)
- ✅ Notifications de propriétés pour bindings Avalonia
- ⏳ Auto-complétion sur champs (ville, assurance) - Reporté
- ⏳ Calcul automatique de l'âge depuis date naissance - Reporté

### Correctifs Techniques Appliqués (Sprint 5)
1. **Bouton "Nouveau client"** : Changement de `Click="OnNewCustomerClick"` à `Command="{Binding CreateCommand}"`
2. **AlternatingRowBackground** : Propriété non supportée par Avalonia, remplacée par `RowBackground` sur DataGrid
3. **Grid Padding** : Propriété non supportée, supprimée
4. **Expressions complexes dans bindings** : `!IsInEditMode && !IsShowingDetail` ne fonctionne pas en XAML
   - Solution : Propriété calculée `ShowList` avec notifications manuelles
5. **Build cache Avalonia** : Nécessité de `dotnet clean` après ajout de propriétés

### Documentation Créée
- ✅ `docs/GUIDE_BOUTONS_ET_BINDINGS.md` : Guide complet des patterns Avalonia
  - Différences WPF vs Avalonia
  - Quand utiliser Command vs Click
  - Gestion des expressions complexes
  - Examples pratiques

### Tests Unitaires
- ✅ `CustomerFormViewModelTests.cs` : 12 tests (validation, save, cancel)
- ⏳ Tests pour CustomersViewModel (reporté Sprint 6)
- ⏳ Tests pour CustomerPurchaseHistoryViewModel (reporté Sprint 6)

### Prochaines Étapes (Sprint 6)
1. Export Excel/PDF de la liste clients
2. Import CSV de clients
3. Tests unitaires complets pour tous les ViewModels du module
4. Tests d'intégration avec base de données
5. Module Gestion Produits & Stock

---

## Sprint 6 : Module Gestion Produits & Stock ✅ **TERMINÉ**

### Objectifs
- [x] Catalogue produits (liste + filtres par catégorie)
- [x] CRUD produits
- [x] Gestion catégories
- [x] Gestion fournisseurs
- [x] Alertes stock bas (notifications)
- [x] Mouvements de stock (IN/OUT/ADJUSTMENT)
- [x] Inventaire (scan/comptage)
- [ ] Étiquettes code-barres (reporté Sprint 7)

### Écrans
1. ✅ **ProductsListView** : Catalogue avec filtrage multi-critères
2. ✅ **ProductFormView** : Formulaire produit avec validation
3. ✅ **CategoriesView** : Gestion catégories
4. ✅ **SuppliersView** : Gestion fournisseurs
5. ✅ **StockMovementsView** : Historique mouvements
6. ✅ **InventoryView** : Interface d'inventaire physique
7. ✅ **ProductDetailView** : Fiche détaillée avec statistiques

### Livrables
- ✅ `ProductsViewModel.cs` - Coordinateur principal
- ✅ `ProductsListViewModel.cs` - Catalogue avec filtrage
- ✅ `ProductFormViewModel.cs` - Formulaire CRUD avec calcul marge
- ✅ `CategoriesViewModel.cs` - Gestion catégories
- ✅ `SuppliersViewModel.cs` - Gestion fournisseurs
- ✅ `StockMovementsViewModel.cs` - Gestion mouvements
- ✅ `InventoryViewModel.cs` - Interface inventaire
- ✅ `ProductDetailViewModel.cs` - Fiche détaillée
- ✅ 8 Views XAML correspondantes
- ✅ 3 Converters (StockColor, DifferenceColor, CategoryToDetail)

### Fonctionnalités Clés Implémentées
- ✅ **Calcul automatique de marge** : (SalePrice - PurchasePrice) / PurchasePrice
- ✅ **Indicateur visuel stock** : 🔴 (stock < seuil) / 🟢 (stock OK)
- ✅ **Filtrage multi-critères** : Catégorie + Recherche textuelle
- ✅ **Mouvements de stock** : 3 types (IN/OUT/ADJUSTMENT)
- ✅ **Interface d'inventaire** : Comptage physique avec barre de progression
- ✅ **Validation métier** : Prix vente > Prix achat, Suppression sécurisée
- ✅ **Statistiques produit** : Historique mouvements, CA généré

### Résultat
Module complet et fonctionnel, intégration parfaite avec l'architecture existante. Documentation détaillée dans `docs/SPRINT6_COMPLETE.md` (566 lignes).

---

## Sprint 7 : Module Ordonnances Médicales ✅ **TERMINÉ**

### Objectifs
- [x] Liste des ordonnances par client (intégré dans module Clients)
- [x] Formulaire de saisie ordonnance avec validation stricte
- [x] Validation des valeurs optiques (temps réel)
- [x] Visualisation graphique (schéma œil)
- [x] Fiche détaillée ordonnance
- [ ] Impression ordonnance (reporté Sprint 8)
- [ ] Historique des corrections (reporté Sprint 8)
- [ ] Auto-complétion médecins (reporté Sprint 8)

### Livrables
- ✅ `PrescriptionFormViewModel.cs` - Formulaire avec validation stricte (9 méthodes)
- ✅ `CustomerPrescriptionsViewModel.cs` - Gestion liste ordonnances client
- ✅ `PrescriptionDetailViewModel.cs` - Affichage détaillé
- ✅ `PrescriptionFormView.axaml` - Design professionnel OD/OG
- ✅ `CustomerPrescriptionsView.axaml` - Liste avec formatage +/-
- ✅ `PrescriptionDetailView.axaml` - Visualisation graphique
- ✅ Validation stricte : Sphère (-20/+20), Cylindre (-6/+6), Axe (0-180°), Addition (0-4)
- ✅ Affichage des erreurs dans la vue (TextBlock conditionnels)

### Fonctionnalités Clés Implémentées
- ✅ **Validation métier stricte** :
  - Sphère : -20 à +20
  - Cylindre : -6 à +6
  - Axe : 0 à 180°
  - Addition : 0 à +4
- ✅ **Formatage professionnel** : `+2.50`, `-1.75`, `180°`
- ✅ **Visualisation graphique** : Canvas avec cercles (OD bleu, OG orange)
- ✅ **Validation temps réel** : Messages affichés sous chaque champ
- ✅ **Navigation fluide** : Liste ↔ Formulaire ↔ Détails
- ⏳ **Auto-complétion nom médecin** (reporté Sprint 8)
- ⏳ **Suggestion de verres** basée sur prescription (reporté Sprint 8)
- ⏳ **Lien direct vers création de commande** (reporté Sprint 8)

### Résultat
Module complet et opérationnel, intégration parfaite dans module Clients. Validation stricte selon normes optiques professionnelles. Documentation détaillée dans `docs/SPRINT7_PROGRESS.md`.

---

## Sprint 8 : Module Commandes (Workflow Atelier) ✅ **TERMINÉ**

### Objectifs
- [x] Création de commande (lien client + prescription)
- [x] Ajout d'articles à la commande (monture, verres OD/OG, accessoires)
- [x] Workflow de statut complet (6 étapes)
- [x] Vue Kanban pour visualisation du workflow
- [x] Fiche de fabrication imprimable (bon atelier)
- [x] Suivi du délai avec indicateur de retard
- [x] Notifications de changement de statut
- [x] Contrôle qualité avec checklist (4 points)
- [x] Gestion des acomptes et encaissement du solde
- [x] Mise à jour automatique du stock lors de la fabrication
- [ ] Historique des corrections (reporté Sprint 11)
- [ ] Auto-complétion médecins (reporté Sprint 11)
- [ ] Export PDF fiche fabrication avec QR Code (reporté Sprint 11)

### Écrans
1. ✅ **OrdersView** : Vue conteneur principale
2. ✅ **OrdersListView** : Liste + filtres par statut + pagination
3. ✅ **OrderFormView** : Création commande avec articles multi-types
4. ✅ **OrderDetailView** : Suivi + workflow + contrôle qualité + encaissement
5. ✅ **OrderKanbanView** : Vue Kanban avec 6 colonnes de statut
6. ✅ **FabricationSheetView** : Bon atelier imprimable professionnel

### Livrables

#### ViewModels (6 fichiers)
- ✅ `OrdersViewModel.cs` (348 lignes) - Coordinateur principal avec navigation 5 vues
- ✅ `OrdersListViewModel.cs` (244 lignes) - Liste paginée avec filtres par statut
- ✅ `OrderFormViewModel.cs` (658 lignes) - Formulaire création avec classe OrderItemLine
- ✅ `OrderDetailViewModel.cs` (483 lignes) - Fiche détaillée + workflow + QC + encaissement
- ✅ `OrderKanbanViewModel.cs` (197 lignes) - Vue Kanban 6 colonnes avec compteurs
- ✅ `FabricationSheetViewModel.cs` (109 lignes) - Bon de fabrication imprimable

#### Views (5 fichiers XAML + 5 code-behind)
- ✅ `OrdersView.axaml` - Conteneur avec gestion visibilité
- ✅ `OrderFormView.axaml` (585 lignes) - Interface création professionnelle
- ✅ `OrderDetailView.axaml` (678 lignes) - Fiche détaillée avec sections multiples
- ✅ `OrderKanbanView.axaml` (335 lignes) - Design Kanban moderne avec couleurs
- ✅ `FabricationSheetView.axaml` (236 lignes) - Mise en page A4 optimisée

#### Composants Additionnels
- ✅ Converters : `OrderStatusToColorConverter`, `OrderStatusToStringConverter`
- ✅ Entité Notification avec repository
- ✅ Integration 7 repositories (Order, Customer, Product, Prescription, StockMovement, Notification, UnitOfWork)

### Workflow Statuts
```
NEW (Nouveau)
  ↓
TO_FABRICATE (À fabriquer)
  ↓
IN_PROGRESS (En fabrication)
  ↓
QUALITY_CHECK (Contrôle qualité) ← Checklist 4 points obligatoire
  ↓
READY (Prêt) ← Notification client automatique
  ↓
DELIVERED (Livré) ← Encaissement du solde requis
```

### Fonctionnalités Clés Implémentées

1. **Gestion Multi-Articles avec Types Spécifiques**
   - 🔲 Monture (Frame) - 1 par commande
   - 👁 Verre OD (LensOd) - Avec paramètres optiques (Sphère, Cylindre, Axe, Addition)
   - 👁 Verre OG (LensOg) - Avec paramètres optiques
   - 🔧 Accessoires (Accessory) - Quantité variable
   - Recherche produits avec popup auto-complétée
   - Calcul automatique du montant total

2. **Contrôle Qualité avec Checklist**
   - ☑ Alignement de la monture
   - ☑ Verre droit (OD)
   - ☑ Verre gauche (OG)
   - ☑ Propreté générale
   - Validation obligatoire des 4 points pour passer au statut READY

3. **Suivi des Délais avec Alertes**
   - Calcul automatique des jours restants
   - Indicateur visuel de retard :
     - 🟢 Plus de 3 jours restants
     - 🟡 1-3 jours restants
     - 🔴 En retard (dépassement)
   - Affichage "X jours restants" ou "🔴 En retard de X jours"

4. **Gestion Financière Intégrée**
   - Affichage de l'acompte versé (DepositAmount)
   - Calcul automatique du solde restant (RemainingAmount)
   - Bouton "Encaisser le solde" (visible si solde > 0)
   - Sélection du moyen de paiement (Cash, Card, Check, Transfer)
   - Mise à jour automatique de la vente associée
   - Création de notification d'encaissement

5. **Vue Kanban Interactive**
   - 6 colonnes de statut avec couleurs distinctives :
     - 🔵 Nouveau (#0A84FF)
     - 🟣 À fabriquer (#AF52DE)
     - 🟡 En fabrication (#FF9F0A)
     - 🔴 Contrôle qualité (#FF453A)
     - 🟢 Prêt (#32D74B)
     - ⚪ Livré (#98989D)
   - Compteurs en temps réel par colonne
   - Cartes avec effet hover
   - Bouton "➜" pour avancer au statut suivant
   - Actualisation manuelle avec bouton dédié

6. **Fiche de Fabrication Professionnelle**
   - Format A4 optimisé pour impression
   - Sections claires : Infos générales, Monture, Verres OD/OG, Accessoires
   - Paramètres optiques en grand pour lecture facile
   - Zone de notes techniques
   - CheckBox "Fabrication terminée"
   - Zone de signature du technicien
   - Prêt pour impression (Ctrl+P)

7. **Système de Notifications**
   - Notification automatique lors du changement de statut
   - Alerte spéciale pour commande prête (statut READY)
   - Notification d'encaissement du solde
   - Type : Info / Success
   - Icônes : 📦, ✅, 💰

8. **Intégration Stock Automatique**
   - Création automatique de mouvements de stock (type OUT)
   - Lors du passage TO_FABRICATE → IN_PROGRESS
   - Décrémentation des quantités en stock
   - Raison : "Fabrication commande [OrderNumber]"
   - Synchronisation parfaite Stock ↔ Commandes

9. **Validation Multi-Niveaux**
   - Validation UI avec messages d'erreur instantanés
   - Validation ViewModel (propriétés IsValid calculées)
   - Validation métier dans les services
   - Transactions atomiques (UnitOfWork)

### Résultat
Module complet et professionnel, intégration parfaite avec l'architecture existante. Le workflow d'atelier est fluide, la vue Kanban apporte une visualisation précieuse, et toutes les fonctionnalités critiques sont opérationnelles. Documentation détaillée dans `docs/SPRINT8_COMPLETE.md` (600+ lignes).

**Statistiques** :
- 6 ViewModels (2 041 lignes de C#)
- 5 Views XAML (1 880 lignes)
- 2 Converters personnalisés
- 7 Repositories intégrés
- 6 statuts de workflow
- 4 types d'articles supportés
- 4 points de contrôle qualité

**Total** : ~4 000 lignes de code pour un module enterprise-grade.

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

## Sprint 10 : Gestion Utilisateurs & Authentification ⏳ EN COURS

### 📊 État d'Avancement

**Déjà implémenté (Sprint 1-4)** :
- ✅ Entité `User` complète (UserId, Username, PasswordHash, FirstName, LastName, Role, IsActive, LastLogin, CreatedAt)
- ✅ Enum `UserRole` avec 3 rôles (Admin, Optician, Technician)
- ✅ `IUserRepository` avec méthodes : GetByUsernameAsync, GetActiveUsersAsync, GetByRoleAsync
- ✅ `UserRepository` implémenté dans Infrastructure
- ✅ `LoginView` + `LoginViewModel` basiques (Sprint 4) - authentification en dur (admin/admin)
- ✅ Configuration EF Core pour User avec conversions enum
- ✅ Data seeder avec 4 utilisateurs de test (PasswordHash BCrypt déjà présents)
- ✅ Tests unitaires UserRepository (3 tests passants)

**À implémenter (Sprint 10)** : 10 tâches principales

---

### 🎯 Objectifs du Sprint 10

#### Phase 1 : Authentification Réelle (2-3 jours)
- [ ] **1.1 Ajouter BCrypt.Net-Next** au projet MMV.Infrastructure
  - Package NuGet : `BCrypt.Net-Next` version 4.0.3+
  - Méthode : `dotnet add package BCrypt.Net-Next`

- [ ] **1.2 Créer AuthenticationService** dans `MMV.Domain/Services/`
  - Interface `IAuthenticationService` :
    - `Task<User?> AuthenticateAsync(string username, string password)`
    - `Task<bool> ValidatePasswordAsync(string password, string hash)`
    - `string HashPassword(string password)`
    - `Task UpdateLastLoginAsync(long userId)`
  - Implémentation dans `MMV.Infrastructure/Services/AuthenticationService.cs`
  - Utiliser `BCrypt.Verify(password, hash)` pour validation
  - Mettre à jour `LastLogin` après connexion réussie

- [ ] **1.3 Créer SessionService** (singleton) dans `MMV.App/Services/`
  - Interface `ISessionService` :
    - `User? CurrentUser { get; set; }`
    - `bool IsAuthenticated { get; }`
    - `bool HasRole(UserRole role)`
    - `bool IsAdmin { get; }`
    - `void Login(User user)`
    - `void Logout()`
    - `event EventHandler? SessionExpired`
  - Implémentation avec gestion timeout (30 minutes d'inactivité)
  - Timer pour détecter l'inactivité
  - Observable pour notifier les changements

- [ ] **1.4 Refactoriser LoginViewModel**
  - Injecter `IAuthenticationService` et `ISessionService`
  - Remplacer le code en dur par l'appel à `AuthenticateAsync`
  - Gérer les erreurs : "Identifiants incorrects", "Compte désactivé"
  - Ajouter indicateur de chargement (IsLoggingIn)
  - Mettre à jour `SessionService.CurrentUser` après login
  - Commande "Se souvenir de moi" (optionnel)

- [ ] **1.5 Ajouter déconnexion globale**
  - Bouton "Déconnexion" dans MainWindow (header ou menu utilisateur)
  - Command `LogoutCommand` dans MainWindowViewModel
  - Appeler `SessionService.Logout()`
  - Rediriger vers LoginView
  - Fermer la MainWindow et rouvrir LoginWindow

#### Phase 2 : Module Gestion Utilisateurs (3-4 jours)
- [ ] **2.1 Créer UsersViewModel** (coordinateur) dans `MMV.App/ViewModels/`
  - Propriétés :
    - `UsersListViewModel ListViewModel`
    - `UserFormViewModel? FormViewModel`
    - `bool IsInEditMode`
    - `bool ShowList` (calculée)
  - Navigation entre liste et formulaire
  - Vérifier permissions : seuls les ADMIN peuvent accéder

- [ ] **2.2 Créer UsersListViewModel**
  - `ObservableCollection<User> Users`
  - `ObservableCollection<User> FilteredUsers`
  - `string SearchText` (recherche par username, nom, prénom)
  - `string SelectedRole` (filtre par rôle : Tous, Admin, Optician, Technician)
  - `bool ShowActiveOnly` (afficher uniquement les utilisateurs actifs)
  - Pagination (20 utilisateurs par page)
  - Commands :
    - `CreateCommand` → Ouvre formulaire création
    - `EditCommand<User>` → Ouvre formulaire édition
    - `ToggleActiveCommand<User>` → Active/Désactive un utilisateur (pas de suppression)
    - `RefreshCommand` → Recharge la liste
  - Events :
    - `CreateUserRequested`
    - `EditUserRequested`

- [ ] **2.3 Créer UserFormViewModel**
  - Mode création/édition avec `IsEditMode`
  - Propriétés :
    - `string Username` (unique, validation)
    - `string FirstName` (requis)
    - `string LastName` (requis)
    - `UserRole SelectedRole` (dropdown)
    - `string Password` (requis en création, optionnel en édition)
    - `string ConfirmPassword` (doit correspondre)
    - `bool IsActive` (checkbox)
  - Validation en temps réel :
    - Username : minimum 3 caractères, pas d'espaces
    - Password : 8+ caractères, 1 majuscule, 1 minuscule, 1 chiffre
    - ConfirmPassword : identique à Password
  - Commands :
    - `SaveCommand` → Validation + hash password + sauvegarde
    - `CancelCommand` → Retour à la liste
    - `GeneratePasswordCommand` → Génère un mot de passe fort aléatoire
  - Utiliser `AuthenticationService.HashPassword()` avant sauvegarde

- [ ] **2.4 Créer UsersView.axaml** (vue conteneur)
  - ContentControl avec binding sur ShowList
  - Affiche UsersListView ou UserFormView

- [ ] **2.5 Créer UsersListView.axaml**
  - DataGrid avec colonnes :
    - Username
    - Nom complet (FirstName + LastName)
    - Rôle (avec badge coloré)
    - Actif (✅/❌)
    - Dernière connexion
    - Créé le
  - Barre de recherche + filtres (rôle, actif)
  - Boutons CRUD : Nouveau, Modifier, Activer/Désactiver, Actualiser
  - Pagination
  - Indicateur visuel : compte désactivé en gris

- [ ] **2.6 Créer UserFormView.axaml**
  - Section Identifiants :
    - Username (TextBox, requis, unique)
    - Password (TextBox, masqué, avec bouton "Afficher/Masquer")
    - Confirm Password (TextBox, masqué)
    - Bouton "Générer mot de passe" avec affichage temporaire
  - Section Informations personnelles :
    - FirstName (TextBox, requis)
    - LastName (TextBox, requis)
  - Section Rôle et statut :
    - Role (ComboBox avec 3 options)
    - IsActive (CheckBox "Compte actif")
  - Footer : Boutons Enregistrer/Annuler
  - Messages d'erreur de validation sous chaque champ

#### Phase 3 : Profil Utilisateur & Changement de Mot de Passe (1-2 jours)
- [ ] **3.1 Créer UserProfileViewModel**
  - Affiche les informations de `SessionService.CurrentUser`
  - Propriétés :
    - `string Username` (lecture seule)
    - `string FullName` (lecture seule)
    - `string RoleDisplay` (lecture seule)
    - `string LastLoginDisplay` (lecture seule)
    - `string CurrentPassword` (pour changement)
    - `string NewPassword`
    - `string ConfirmNewPassword`
  - Commands :
    - `ChangePasswordCommand` → Validation + hash + sauvegarde
    - `CancelCommand`
  - Validation :
    - CurrentPassword : vérifier avec BCrypt
    - NewPassword : politique forte (8+ caractères)
    - ConfirmNewPassword : identique à NewPassword

- [ ] **3.2 Créer UserProfileView.axaml**
  - Section Profil (lecture seule) :
    - Avatar (initiales dans cercle coloré)
    - Username
    - Nom complet
    - Rôle (badge)
    - Dernière connexion
  - Section Changement de mot de passe :
    - Mot de passe actuel (masqué)
    - Nouveau mot de passe (masqué, avec indicateur de force)
    - Confirmation (masqué)
    - Bouton "Changer le mot de passe"
  - Indicateur de force du mot de passe :
    - Faible (rouge) : < 8 caractères
    - Moyen (orange) : 8+ caractères, 1 type
    - Fort (vert) : 8+ caractères, majuscule + minuscule + chiffre

- [ ] **3.3 Ajouter menu utilisateur dans MainWindow**
  - Header : Afficher "👤 [Nom Utilisateur] ([Rôle])"
  - Menu déroulant (clic) avec options :
    - "👤 Mon Profil" → Ouvre UserProfileView
    - "⚙️ Paramètres" → Ouvre SettingsView (si existant)
    - "🚪 Déconnexion" → LogoutCommand

#### Phase 4 : Système de Permissions (2 jours)
- [ ] **4.1 Créer PermissionService** dans `MMV.App/Services/`
  - Interface `IPermissionService` :
    - `bool CanAccessModule(string moduleName)`
    - `bool CanCreate(string entityType)`
    - `bool CanEdit(string entityType)`
    - `bool CanDelete(string entityType)`
    - `bool CanViewReports()`
  - Implémentation basée sur `SessionService.CurrentUser.Role`
  - Matrice de permissions (selon tableau ci-dessous)

- [ ] **4.2 Appliquer les permissions dans l'UI**
  - MainWindowViewModel : masquer les modules selon permissions
    - Exemple : Module "Utilisateurs" visible uniquement pour ADMIN
  - Désactiver les boutons CRUD selon permissions :
    - Clients : ADMIN et OPTICIAN peuvent créer/modifier, TECHNICIAN lecture seule
    - Produits : idem
    - Commandes : ADMIN et OPTICIAN peuvent créer, tous peuvent voir, TECHNICIAN peut changer statut
  - Ajouter `IsVisible` ou `IsEnabled` basé sur `PermissionService`

- [ ] **4.3 Sécuriser les ViewModels**
  - Ajouter vérifications dans les Commands :
    ```csharp
    CreateCommand = new RelayCommand(
        ExecuteCreate,
        () => _permissionService.CanCreate("Customer")
    );
    ```
  - Afficher message d'erreur si l'utilisateur n'a pas les permissions

#### Phase 5 : Historique des Connexions (1 jour - Optionnel)
- [ ] **5.1 Créer entité LoginHistory** (optionnel)
  - LoginHistoryId (long, PK)
  - UserId (long, FK vers User)
  - LoginDate (DateTime)
  - IpAddress (string, optionnel)
  - Success (bool)
  - FailureReason (string, optionnel)

- [ ] **5.2 Ajouter LoginHistoryRepository**
  - Méthodes : GetByUserIdAsync, GetRecentAsync

- [ ] **5.3 Logger les connexions**
  - Dans `AuthenticationService.AuthenticateAsync` :
    - Enregistrer tentative réussie
    - Enregistrer tentative échouée avec raison
  - Limiter à 100 entrées par utilisateur (purge automatique)

- [ ] **5.4 Afficher l'historique dans UserProfileView** (optionnel)
  - Onglet "Historique de connexion"
  - DataGrid avec 5 dernières connexions :
    - Date
    - Succès/Échec
    - Raison échec (si applicable)

---

### 🛡️ Matrice de Permissions Détaillée

| Module | ADMIN | OPTICIAN | TECHNICIAN |
|--------|-------|----------|------------|
| **Dashboard** | ✅ Accès complet | ✅ Accès complet | 🔍 Lecture seule |
| **Clients** | ✅ CRUD + Export | ✅ CRUD + Export | 🔍 Lecture seule |
| **Produits** | ✅ CRUD + Import | ✅ CRUD + Import | 🔍 Lecture + Alerte stock |
| **Ordonnances** | ✅ CRUD | ✅ CRUD | 🔍 Lecture seule |
| **Commandes** | ✅ CRUD + Workflow | ✅ CRUD + Workflow | ✅ Changement statut uniquement |
| **Ventes (POS)** | ✅ CRUD + Remboursements | ✅ CRUD (pas remboursements) | ❌ Aucun accès |
| **Gestion Utilisateurs** | ✅ CRUD | ❌ Aucun | ❌ Aucun |
| **Rapports** | ✅ Tous les rapports + Export | 🔍 Lecture rapports basiques | ❌ Aucun |
| **Paramètres** | ✅ Configuration complète | 🔍 Lecture uniquement | ❌ Aucun |
| **Notifications** | ✅ Toutes | ✅ Toutes | ✅ Commandes uniquement |

---

### 📦 Livrables Attendus

#### Services (4 fichiers)
- `MMV.Domain/Services/IAuthenticationService.cs` (interface)
- `MMV.Infrastructure/Services/AuthenticationService.cs` (implémentation)
- `MMV.App/Services/ISessionService.cs` (interface)
- `MMV.App/Services/SessionService.cs` (implémentation singleton)
- `MMV.App/Services/IPermissionService.cs` (interface)
- `MMV.App/Services/PermissionService.cs` (implémentation)

#### ViewModels (6 fichiers)
- `MMV.App/ViewModels/UsersViewModel.cs` (coordinateur)
- `MMV.App/ViewModels/UsersListViewModel.cs` (liste avec filtres)
- `MMV.App/ViewModels/UserFormViewModel.cs` (création/édition)
- `MMV.App/ViewModels/UserProfileViewModel.cs` (profil + changement mot de passe)
- Refactorisation : `MMV.App/ViewModels/LoginViewModel.cs` (authentification réelle)
- Refactorisation : `MMV.App/ViewModels/MainWindowViewModel.cs` (menu utilisateur + permissions)

#### Views (4 fichiers XAML + code-behind)
- `MMV.App/Views/Users/UsersView.axaml`
- `MMV.App/Views/Users/UsersListView.axaml`
- `MMV.App/Views/Users/UserFormView.axaml`
- `MMV.App/Views/Users/UserProfileView.axaml`
- Refactorisation : `MMV.App/Views/LoginView.axaml` (améliorations UI)
- Refactorisation : `MMV.App/Views/MainWindow.axaml` (menu utilisateur)

#### Validators (1 fichier)
- `MMV.Domain/Validators/UserValidator.cs` (FluentValidation pour User)
  - Username unique
  - Password politique forte
  - FirstName et LastName requis

#### Entités Optionnelles
- `MMV.Domain/Entities/LoginHistory.cs` (si Phase 5 implémentée)

#### Tests Unitaires (3 fichiers minimum)
- `tests/MMV.Domain.Tests/ServiceTests/AuthenticationServiceTests.cs`
  - Test HashPassword
  - Test ValidatePassword
  - Test AuthenticateAsync (succès/échec)
- `tests/MMV.App.Tests/Services/SessionServiceTests.cs`
  - Test Login/Logout
  - Test timeout session
- `tests/MMV.App.Tests/ViewModels/UserFormViewModelTests.cs`
  - Test validation
  - Test SaveCommand

---

### 🔐 Sécurité - Bonnes Pratiques

1. **Hachage BCrypt** :
   ```csharp
   // Génération du hash avec work factor 11
   string hash = BCrypt.Net.BCrypt.HashPassword(password, 11);
   
   // Vérification
   bool isValid = BCrypt.Net.BCrypt.Verify(password, hash);
   ```

2. **Politique de mot de passe forte** :
   - Minimum 8 caractères
   - Au moins 1 majuscule
   - Au moins 1 minuscule
   - Au moins 1 chiffre
   - Caractères spéciaux recommandés (optionnel)

3. **Timeout session** :
   - 30 minutes d'inactivité par défaut
   - Timer reset à chaque action utilisateur
   - Event `SessionExpired` pour notification

4. **Protection contre brute force** (optionnel, Sprint 12) :
   - Bloquer compte après 5 tentatives échouées
   - Déblocage manuel par admin ou automatique après 15 minutes

5. **Audit trail** (Sprint 12) :
   - Logger les actions sensibles (création utilisateur, changement de rôle, etc.)
   - Utiliser Serilog pour les logs

---

### ✅ Critères de Validation

Le Sprint 10 sera considéré comme terminé quand :

1. ✅ Un utilisateur peut se connecter avec username/password réel (BCrypt)
2. ✅ Le mot de passe incorrect affiche un message d'erreur
3. ✅ La session utilisateur est stockée dans SessionService
4. ✅ L'utilisateur connecté est affiché dans le header de MainWindow
5. ✅ Un ADMIN peut créer/modifier/désactiver des utilisateurs
6. ✅ Un utilisateur peut changer son propre mot de passe
7. ✅ Les modules sont masqués selon les permissions du rôle
8. ✅ Les boutons CRUD sont désactivés selon les permissions
9. ✅ La déconnexion fonctionne et redirige vers LoginView
10. ✅ Le timeout de session (30 min) fonctionne
11. ✅ Les tests unitaires passent (AuthenticationService, SessionService, UserFormViewModel)
12. ✅ Un OPTICIAN ne peut pas accéder au module Utilisateurs
13. ✅ Un TECHNICIAN voit les commandes mais ne peut modifier que le statut

---

### 📝 Notes Importantes

- **BCrypt est déjà utilisé** dans DbInitializer pour les mots de passe de test
- **Pattern utilisé** : tous les hash commencent par `$2a$11$` (BCrypt work factor 11)
- **Mot de passe de test actuel** : tous les utilisateurs ont le mot de passe "admin"
- **Ne jamais supprimer un utilisateur** : utiliser `IsActive = false` pour désactivation
- **SessionService doit être un singleton** pour persister durant toute la session
- **Tester avec les 3 rôles** : admin, marie.optic (Optician), pierre.tech (Technician)

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
