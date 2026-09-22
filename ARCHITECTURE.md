# Architecture Technique - ManageMyVision (MMV)

> **Documentation complète de l'architecture de l'application**  
> **Version** : 1.0  
> **Dernière mise à jour** : 27 janvier 2026

---

## 📐 Vue d'Ensemble de l'Architecture

MMV suit une **Clean Architecture** avec séparation stricte des responsabilités en 3 couches principales :

```
┌─────────────────────────────────────────────────────────────┐
│                    MMV.App (Presentation)                    │
│              Avalonia UI + ViewModels (MVVM)                │
│                    CommunityToolkit.Mvvm                     │
└──────────────────────────┬──────────────────────────────────┘
                           │ Dépend de ↓
┌──────────────────────────▼──────────────────────────────────┐
│              MMV.Infrastructure (Data Access)                │
│        EF Core + SQLite + Repositories + Migrations          │
└──────────────────────────┬──────────────────────────────────┘
                           │ Dépend de ↓
┌──────────────────────────▼──────────────────────────────────┐
│              MMV.Domain (Core Business Logic)                │
│       Entities + Interfaces + Services + ValueObjects        │
│                   AUCUNE DÉPENDANCE                          │
└─────────────────────────────────────────────────────────────┘
```

### Principe de Dépendance (Dependency Rule)
- **Domain** : Aucune référence externe (pur C#)
- **Infrastructure** : Référence Domain uniquement
- **App** : Référence Domain + Infrastructure

---

## 🗄️ Couche 1 : MMV.Domain (Cœur Métier)

### Localisation
```
src/MMV.Domain/
```

### Responsabilités
- Définir les **entités métier** (agrégats, entities)
- Définir les **interfaces** (repositories, services)
- Contenir la **logique métier pure** (règles de gestion)
- **Pas de dépendances externes** (ni EF Core, ni Avalonia)

### Structure des Dossiers

```
MMV.Domain/
├── Entities/              # 11 entités principales
│   ├── User.cs
│   ├── Customer.cs
│   ├── Product.cs
│   ├── ProductCategory.cs
│   ├── Supplier.cs
│   ├── Prescription.cs
│   ├── Order.cs
│   ├── OrderItem.cs
│   ├── Sale.cs
│   ├── SaleItem.cs
│   └── StockMovement.cs
│
├── Enums/                 # 8 énumérations
│   ├── UserRole.cs
│   ├── PaymentMethod.cs
│   ├── PaymentStatus.cs
│   ├── OrderStatus.cs
│   ├── OrderItemType.cs
│   ├── LensUsageType.cs
│   ├── PrismBase.cs
│   └── StockMovementType.cs
│
├── Interfaces/            # Contrats (repositories, services)
│   ├── IRepositories/
│   │   ├── IGenericRepository.cs
│   │   ├── IUserRepository.cs
│   │   ├── ICustomerRepository.cs
│   │   ├── IProductCategoryRepository.cs
│   │   ├── ISupplierRepository.cs
│   │   ├── IProductRepository.cs
│   │   ├── IPrescriptionRepository.cs
│   │   ├── IOrderRepository.cs
│   │   ├── ISaleRepository.cs
│   │   ├── IStockMovementRepository.cs
│   │   └── IUnitOfWork.cs
│   └── IServices/ (prévu Sprint 3)
│       ├── IAuthenticationService.cs
│       ├── ICustomerService.cs
│       └── IProductService.cs
│
├── ValueObjects/          # Objets-valeur (prévu Sprint 3)
├── Validators/            # FluentValidation (prévu Sprint 3)
├── Exceptions/            # Exceptions métier personnalisées (prévu Sprint 3)
└── Services/              # Logique métier (prévu Sprint 3)
```

### Règles Métier Clés

#### 1. Mapping Types SQL → C# (CRITIQUE)

| Type SQL | Usage | Type C# | Justification |
|----------|-------|---------|---------------|
| `INTEGER PRIMARY KEY AUTOINCREMENT` | IDs | `long` | SQLite utilise 64-bit pour PKs |
| `REAL` (Prix, Montants) | Argent | `decimal` | Précision exacte pour calculs financiers |
| `REAL` (Sphère, Cylindre) | Optique | `double` | Valeurs scientifiques avec négatifs |
| `TEXT CHECK(... IN (...))` | Énumérations | `enum` | Type-safety + IntelliSense |
| `INTEGER` (0/1) | Booléens | `bool` | Mapping automatique EF Core |
| `DATETIME` | Dates | `DateTime` | Type standard .NET |
| `TEXT` | Chaînes | `string` | Type standard .NET |

#### 2. Propriétés de Navigation (EF Core)
```csharp
// TOUJOURS virtual pour lazy loading (même si désactivé)
public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

// Initialisation obligatoire pour éviter NullReferenceException
```

#### 3. Validation des Données Optiques
```csharp
// Prescriptions : valeurs acceptables
Sphère    : -20.00 à +20.00 (pas 0.25)
Cylindre  : -6.00 à +6.00 (pas 0.25)
Axe       : 0 à 180° (entier)
Addition  : 0.00 à +4.00 (pas 0.25)
Prisme    : 0.00 à 10.00
```

---

## 🏗️ Couche 2 : MMV.Infrastructure (Accès Données)

### Localisation
```
src/MMV.Infrastructure/
```

### Responsabilités
- **Persistance des données** (SQLite via EF Core)
- **Implémentation des repositories**
- **Configuration EF Core** (Fluent API)
- **Migrations de base de données**

### Structure des Dossiers

```
MMV.Infrastructure/
├── Data/
│   ├── OpticDbContext.cs            # DbContext principal
│   ├── OpticDbContextFactory.cs     # Factory design-time pour les migrations
│   ├── Seeders/DbSeeder.cs          # Données de test/démo
│   └── Configurations/              # Fluent API par entité (11 fichiers)
│       ├── UserConfiguration.cs
│       ├── SupplierConfiguration.cs
│       ├── ProductCategoryConfiguration.cs
│       ├── ProductConfiguration.cs
│       ├── CustomerConfiguration.cs
│       ├── PrescriptionConfiguration.cs
│       ├── OrderConfiguration.cs
│       ├── OrderItemConfiguration.cs
│       ├── SaleConfiguration.cs
│       ├── SaleItemConfiguration.cs
│       └── StockMovementConfiguration.cs
│
├── Repositories/                     # Implémentations
│   ├── BaseRepository.cs             # Repository générique
│   ├── UserRepository.cs
│   ├── CustomerRepository.cs
│   ├── ProductCategoryRepository.cs
│   ├── SupplierRepository.cs
│   ├── ProductRepository.cs
│   ├── PrescriptionRepository.cs
│   ├── OrderRepository.cs
│   ├── SaleRepository.cs
│   ├── StockMovementRepository.cs
│   └── UnitOfWork.cs                 # Pattern UnitOfWork
│
├── Migrations/                       # Migrations EF Core : chaîne SQLite uniquement
│                                     # Chaîne PostgreSQL : src/MMV.Infrastructure.PostgreSQL.Migrations/
│                                     # Voir « Migrations EF Core » ci-dessous et CONTRIBUTING.md
│
└── DependencyInjection.cs           # Configuration MS.DI (à créer Sprint 3)
```

---

## 🗃️ Base de Données SQLite

### Localisation & Chemin

#### En Développement
```
C:\Users\[USERNAME]\AppData\Local\ManageMyVision\mmv.db
```

#### En Production
```
%PROGRAMDATA%\ManageMyVision\Data\mmv.db
(C:\ProgramData\ManageMyVision\Data\mmv.db)
```

### Configuration Chaîne de Connexion

**appsettings.json** (MMV.App) :
```json
{
  "ConnectionStrings": {
    "OpticDatabase": "Data Source=mmv.db"
  },
  "DatabaseSettings": {
    "EnableSensitiveDataLogging": false,
    "AutoMigrate": true
  }
}
```

**En code (OpticDbContext)** :
```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    if (!optionsBuilder.IsConfigured)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ManageMyVision",
            "mmv.db"
        );
        
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }
}
```

### Caractéristiques de la Base de Données

#### Avantages SQLite pour ce Projet
- ✅ **Pas de serveur** : fichier unique, simple à déployer
- ✅ **Performance** : excellente pour < 100 000 transactions/jour
- ✅ **Portable** : fonctionne sur Windows/macOS/Linux
- ✅ **Transactionnel** : ACID compliant
- ✅ **Taille légère** : < 1 MB pour l'application type opticien

#### Limites à Connaître
- ❌ **Pas de concurrence intensive** : max ~10 écritures simultanées
- ❌ **Pas de réseau natif** : ne peut pas être partagé directement
- ⚠️ **Types limités** : pas de DECIMAL natif (émulé avec TEXT ou REAL)

### Schéma de la Base de Données

**9 Tables Principales** :

```sql
-- STAFF
users (12 colonnes)
  ├── user_id (PK, AUTOINCREMENT)
  ├── username (UNIQUE)
  ├── password_hash
  └── role (CHECK: ADMIN, OPTICIAN, TECHNICIAN)

-- CATALOGUE
suppliers (6 colonnes)
product_categories (3 colonnes)
products (11 colonnes)
  ├── product_id (PK)
  ├── purchase_price (REAL → decimal en C#)
  ├── sale_price (REAL → decimal en C#)
  └── technical_specs (JSON stocké en TEXT)

-- CRM
customers (14 colonnes)
  ├── customer_id (PK)
  ├── social_security_number
  └── insurance_name

-- MÉDICAL
prescriptions (22 colonnes)
  ├── prescription_id (PK)
  ├── customer_id (FK → customers)
  ├── od_sphere, od_cylinder, od_axis... (Œil Droit)
  └── og_sphere, og_cylinder, og_axis... (Œil Gauche)

-- WORKFLOW ATELIER
orders (9 colonnes)
  ├── order_id (PK)
  ├── order_number (UNIQUE, format: CMD-YYYY-XXXX)
  └── status (CHECK: NEW, TO_FABRICATE, ..., DELIVERED)

order_items (15 colonnes)
  ├── order_item_id (PK)
  ├── order_id (FK → orders, ON DELETE CASCADE)
  ├── item_type (CHECK: FRAME, LENS_OD, LENS_OG, ACCESSORY)
  └── sphere, cylinder, axis... (données de fabrication)

-- POINT DE VENTE
sales (10 colonnes)
  ├── sale_id (PK)
  ├── sale_number (UNIQUE, format: VTE-YYYY-XXXX)
  ├── total_amount, discount_amount, final_amount (REAL → decimal)
  └── payment_method (CHECK: CASH, CARD, CHECK, TRANSFER)

sale_items (6 colonnes)
  ├── sale_item_id (PK)
  ├── sale_id (FK → sales, ON DELETE CASCADE)
  └── total_price (REAL → decimal)

-- INVENTAIRE
stock_movements (7 colonnes)
  ├── movement_id (PK)
  ├── product_id (FK → products)
  ├── movement_type (CHECK: IN, OUT, ADJUSTMENT)
  └── quantity (INTEGER, positif ou négatif)
```

### Index & Performances

**Index Automatiques** (PKs, FKs) :
- Toutes les colonnes `*_id` sont indexées

**Index à Créer Manuellement** :
```sql
-- Recherche rapide
CREATE INDEX idx_customers_lastname ON customers(last_name);
CREATE INDEX idx_customers_phone ON customers(phone);
CREATE INDEX idx_products_reference ON products(reference);
CREATE INDEX idx_products_category ON products(category_id);

-- Jointures fréquentes
CREATE INDEX idx_orders_customer ON orders(customer_id);
CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_sales_date ON sales(sale_date);

-- Tri par date
CREATE INDEX idx_prescriptions_issue_date ON prescriptions(issue_date DESC);
```

### Migrations EF Core

> **Section mise à jour en P4-5E-C8 (22 septembre 2026).** La procédure complète, les interdits et la revue
> avant commit sont dans [CONTRIBUTING.md](CONTRIBUTING.md#règle-de-double-migration), qui fait référence.

Le modèle EF est unique. Les migrations forment **deux chaînes**, une par moteur, qui ne se mélangent jamais :

| Chaîne | Dossier des migrations | `--project` et `--startup-project` | Variable design-time |
|---|---|---|---|
| SQLite | `src/MMV.Infrastructure/Migrations/` | `src/MMV.Infrastructure` | aucune |
| PostgreSQL | `src/MMV.Infrastructure.PostgreSQL.Migrations/Migrations/` | `src/MMV.Infrastructure.PostgreSQL.Migrations` | `MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql` |

`src/MMV.App` n'est **jamais** le projet de démarrage de `dotnet ef` : EF exécuterait l'application, ce qui
ouvre la fenêtre et prépare réellement la base locale.

#### Commandes Principales

Depuis la racine du dépôt, après `dotnet tool restore` (dotnet-ef 8.0.27) et `dotnet build MMV.sln -c Debug` :

```bash
# Chaîne SQLite : sans variable
dotnet ef migrations add NomMigration \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure
dotnet ef migrations has-pending-model-changes \
  --project src/MMV.Infrastructure --startup-project src/MMV.Infrastructure --no-build

# Chaîne PostgreSQL : variable limitée à la commande (forme PowerShell dans CONTRIBUTING.md)
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations add NomMigration \
  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations list --no-connect \
  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations --no-build
MMV_DESIGNTIME_DATABASE_PROVIDER=postgresql dotnet ef migrations has-pending-model-changes \
  --project src/MMV.Infrastructure.PostgreSQL.Migrations \
  --startup-project src/MMV.Infrastructure.PostgreSQL.Migrations --no-build
```

Aucune de ces commandes ne se connecte à une base. `dotnet ef database update` n'est pas utilisé. La base
SQLite locale est préparée par l'application au démarrage (`SqliteDatabaseManager`). L'application des
migrations PostgreSQL n'est pas encore décidée (P4-6). Toute évolution du modèle exige une migration sur
**chaque** chaîne, dans le même commit.

#### Migration Initiale (Sprint 2)
```csharp
// 20260127_InitialCreate.cs
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Création des 9 tables
        // Configuration des contraintes
        // Seed des données de base (rôle Admin)
    }
}
```

---

## 🛡️ Sécurité de la Base de Données

### 1. Protection du Fichier SQLite

#### Permissions Fichier (Windows)
```csharp
// Restreindre l'accès au fichier en production
var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
                          "ManageMyVision", "Data", "mmv.db");

// Créer le dossier avec permissions restreintes
var directory = Path.GetDirectoryName(dbPath);
if (!Directory.Exists(directory))
{
    Directory.CreateDirectory(directory!);
    
    // Accès uniquement à Administrators + Utilisateur courant
    var dirInfo = new DirectoryInfo(directory);
    var security = dirInfo.GetAccessControl();
    security.SetAccessRuleProtection(true, false); // Bloquer héritage
    
    // Ajouter règles explicites
    security.AddAccessRule(new FileSystemAccessRule(
        WindowsIdentity.GetCurrent().Name,
        FileSystemRights.FullControl,
        AccessControlType.Allow));
    
    dirInfo.SetAccessControl(security);
}
```

#### Chiffrement SQLite (Optionnel)
Pour des données sensibles (ordonnances) :
```bash
# Installer SQLCipher (alternative chiffrée à SQLite)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite.Cipher
```

```csharp
// Activer le chiffrement
optionsBuilder.UseSqlite($"Data Source={dbPath};Password={encryptionKey}");
```

### 2. Hachage des Mots de Passe

**JAMAIS** stocker les mots de passe en clair !

```csharp
// Installation
dotnet add package BCrypt.Net-Next

// Hachage lors de la création utilisateur
public class UserService
{
    public User CreateUser(string username, string password, UserRole role)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);
        
        return new User
        {
            Username = username,
            PasswordHash = passwordHash,
            Role = role
        };
    }
    
    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }
}
```

### 3. Sauvegarde & Restauration

#### Backup Automatique Quotidien
```csharp
public class BackupService
{
    private readonly string _dbPath;
    private readonly string _backupDirectory;
    
    public async Task CreateBackupAsync()
    {
        var backupFileName = $"mmv_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var backupPath = Path.Combine(_backupDirectory, backupFileName);
        
        // Copier le fichier DB
        File.Copy(_dbPath, backupPath, overwrite: false);
        
        // Garder uniquement les 30 derniers backups
        CleanOldBackups(30);
    }
    
    private void CleanOldBackups(int keepCount)
    {
        var backups = Directory.GetFiles(_backupDirectory, "mmv_backup_*.db")
            .OrderByDescending(f => f)
            .Skip(keepCount);
        
        foreach (var oldBackup in backups)
        {
            File.Delete(oldBackup);
        }
    }
}
```

**Emplacement des backups** :
```
%PROGRAMDATA%\ManageMyVision\Backups\
```

#### Restauration
```csharp
public async Task RestoreBackupAsync(string backupFilePath)
{
    // Fermer toutes les connexions EF Core
    await _dbContext.Database.CloseConnectionAsync();
    
    // Remplacer le fichier DB
    File.Copy(backupFilePath, _dbPath, overwrite: true);
    
    // Rouvrir les connexions
    await _dbContext.Database.OpenConnectionAsync();
}
```

### 4. Audit Trail (Traçabilité)

#### Table d'Audit (Optionnelle)
```sql
CREATE TABLE audit_logs (
    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER,
    action TEXT NOT NULL,        -- CREATE, UPDATE, DELETE
    table_name TEXT NOT NULL,
    record_id INTEGER,
    old_values TEXT,             -- JSON
    new_values TEXT,             -- JSON
    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

#### Implémentation avec EF Core Interceptors
```csharp
public class AuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, 
                                                           InterceptionResult<int> result)
    {
        var context = eventData.Context;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                // Logger les changements
                LogAudit(entry);
            }
        }
        return base.SavingChanges(eventData, result);
    }
}
```

---

## 💻 Couche 3 : MMV.App (Interface Utilisateur)

### Localisation
```
src/MMV.App/
```

### Responsabilités
- **Interface graphique** (Avalonia UI + XAML)
- **ViewModels** (logique de présentation, MVVM)
- **Navigation** entre les écrans
- **Binding** des données (UI ↔ ViewModels)

### Structure des Dossiers

```
MMV.App/
├── Program.cs                       # Point d'entrée
├── App.axaml                        # Application root
├── App.axaml.cs
├── app.manifest                     # Config Windows
│
├── Views/                           # Vues XAML
│   ├── MainWindow.axaml             # Fenêtre principale + navigation
│   ├── LoginView.axaml              # Écran de connexion
│   ├── DashboardView.axaml          # Tableau de bord
│   │
│   ├── Customers/                   # Module Clients
│   │   ├── CustomersListView.axaml
│   │   ├── CustomerFormView.axaml
│   │   └── CustomerDetailView.axaml
│   │
│   ├── Products/                    # Module Produits
│   │   ├── ProductsListView.axaml
│   │   └── ProductFormView.axaml
│   │
│   ├── Prescriptions/               # Module Ordonnances
│   ├── Orders/                      # Module Commandes
│   ├── Sales/                       # Module Ventes/POS
│   └── Settings/                    # Paramètres
│
├── ViewModels/                      # ViewModels MVVM
│   ├── BaseViewModel.cs             # Classe de base
│   ├── MainWindowViewModel.cs
│   ├── LoginViewModel.cs
│   ├── DashboardViewModel.cs
│   │
│   ├── Customers/
│   │   ├── CustomersListViewModel.cs
│   │   ├── CustomerFormViewModel.cs
│   │   └── CustomerDetailViewModel.cs
│   │
│   ├── Products/
│   └── ... (idem pour chaque module)
│
├── Controls/                        # Composants réutilisables
│   ├── DataGridControl.axaml        # Liste paginée
│   ├── SearchBox.axaml              # Barre de recherche
│   ├── ActionButtons.axaml          # Boutons CRUD
│   └── LoadingSpinner.axaml         # Indicateur de chargement
│
├── Services/                        # Services UI
│   ├── NavigationService.cs         # Navigation entre vues
│   ├── DialogService.cs             # Dialogs (confirmation, erreur)
│   ├── NotificationService.cs       # Toasts/Snackbars
│   └── PrintService.cs              # Impression (tickets, factures)
│
├── Converters/                      # Value Converters XAML
│   ├── BoolToVisibilityConverter.cs
│   ├── DecimalToStringConverter.cs
│   └── EnumToStringConverter.cs
│
├── Styles/                          # Thèmes & Styles
│   ├── CustomTheme.axaml            # Thème personnalisé
│   ├── Colors.axaml                 # Palette de couleurs
│   └── Fonts.axaml                  # Typographie
│
└── Resources/                       # Ressources (images, icônes)
    ├── Icons/
    └── Images/
```

---

## 🎨 Architecture MVVM (Presentation)

### Principe MVVM

```
┌───────────────┐           ┌───────────────┐           ┌───────────────┐
│     View      │  Binding   │   ViewModel   │  Calls    │     Model     │
│    (XAML)     │◄──────────►│  (C# Logic)   │──────────►│  (Entities)   │
│               │            │               │           │               │
│ • UI Controls │            │ • Properties  │           │ • Customer    │
│ • DataContext │            │ • Commands    │           │ • Product     │
│ • Binding     │            │ • Validation  │           │ • Order       │
└───────────────┘            └───────────────┘           └───────────────┘
```

### Exemple Concret : Customer

#### 1. Entity (Model) - Domain
```csharp
namespace MMV.Domain.Entities;

public class Customer
{
    public long CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    // ... autres propriétés
}
```

#### 2. ViewModel - App
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MMV.App.ViewModels.Customers;

public partial class CustomerFormViewModel : BaseViewModel
{
    private readonly ICustomerService _customerService;
    
    [ObservableProperty]
    private string _firstName = string.Empty;
    
    [ObservableProperty]
    private string _lastName = string.Empty;
    
    [ObservableProperty]
    private string? _phone;
    
    [ObservableProperty]
    private string? _email;
    
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            await _dialogService.ShowErrorAsync("Prénom et nom requis");
            return;
        }
        
        // Créer l'entité
        var customer = new Customer
        {
            FirstName = FirstName,
            LastName = LastName,
            Phone = Phone,
            Email = Email
        };
        
        // Sauvegarder via le service
        await _customerService.CreateCustomerAsync(customer);
        
        // Navigation retour
        await _navigationService.GoBackAsync();
    }
    
    [RelayCommand]
    private async Task CancelAsync()
    {
        await _navigationService.GoBackAsync();
    }
}
```

#### 3. View (XAML) - App
```xaml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MMV.App.ViewModels.Customers"
             x:Class="MMV.App.Views.Customers.CustomerFormView"
             x:DataType="vm:CustomerFormViewModel">
    
    <StackPanel Spacing="10" Margin="20">
        <TextBlock Text="Nouveau Client" FontSize="24" FontWeight="Bold"/>
        
        <TextBox Header="Prénom *" 
                 Text="{Binding FirstName}" 
                 Watermark="Entrez le prénom"/>
        
        <TextBox Header="Nom *" 
                 Text="{Binding LastName}" 
                 Watermark="Entrez le nom"/>
        
        <TextBox Header="Téléphone" 
                 Text="{Binding Phone}" 
                 Watermark="+33 6 12 34 56 78"/>
        
        <TextBox Header="Email" 
                 Text="{Binding Email}" 
                 Watermark="client@example.com"/>
        
        <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Right">
            <Button Content="Annuler" 
                    Command="{Binding CancelCommand}"/>
            <Button Content="Enregistrer" 
                    Command="{Binding SaveCommand}" 
                    Theme="{StaticResource AccentButtonTheme}"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

---

## 🚀 Démarrage & Développement de l'Interface

### Lancer l'Application en Mode Debug

#### Depuis Visual Studio Code
```bash
# Méthode 1 : Terminal
cd src/MMV.App
dotnet run

# Méthode 2 : F5 (Debug)
# Utiliser la configuration de launch.json
```

#### Configuration launch.json
```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": ".NET Core Launch (MMV.App)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/src/MMV.App/bin/Debug/net8.0/MMV.App.dll",
            "args": [],
            "cwd": "${workspaceFolder}/src/MMV.App",
            "stopAtEntry": false,
            "console": "internalConsole"
        }
    ]
}
```

### Hot Reload (Rechargement à Chaud)

Avalonia supporte le hot reload pour XAML :

```bash
# Lancer avec hot reload activé
dotnet watch run --project src/MMV.App
```

**Modifications rechargées automatiquement** :
- ✅ XAML (Views, Styles)
- ✅ Styles (axaml)
- ❌ Code C# (nécessite redémarrage)

### Prévisualisation XAML (Design-Time)

#### Extension Avalonia for VS Code
1. Installer `Avalonia for VSCode` depuis Marketplace
2. Ouvrir un fichier `.axaml`
3. Cliquer sur l'icône "Preview" dans la barre latérale

#### Previewer Standalone
```bash
# Installer le previewer
dotnet tool install --global Avalonia.Designer.HostApp

# Lancer le previewer
avalonia-designer
```

### Déboguer l'Interface

#### 1. DevTools Intégré (Debug Mode)
Avalonia inclut des outils de développement :

```csharp
// App.axaml.cs - Activer DevTools
public override void OnFrameworkInitializationCompleted()
{
    #if DEBUG
    this.AttachDevTools(); // F12 dans l'app pour ouvrir DevTools
    #endif
    
    base.OnFrameworkInitializationCompleted();
}
```

**DevTools inclut** :
- Inspecteur visuel (arbre XAML live)
- Propriétés des contrôles
- Mesure de performance
- Console de logs

#### 2. Logging des Binding Errors
```csharp
// Program.cs
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace(areas: new[] { 
            LogArea.Binding,        // Erreurs de binding
            LogArea.Layout,         // Problèmes de layout
            LogArea.Animations      // Animations
        });
```

#### 3. Breakpoints dans ViewModels
- Placez des breakpoints dans les commandes (`[RelayCommand]`)
- Inspectez les propriétés observables (`[ObservableProperty]`)

---

## 🧪 Tests de l'Interface Avalonia

### 1. Tests Headless (sans UI visible)

```bash
# Installer le package de test
dotnet add package Avalonia.Headless.XUnit --version 11.1.3
```

```csharp
// Exemple de test UI headless
public class CustomerFormViewTests
{
    [AvaloniaFact]
    public void FirstName_WhenEmpty_ShouldShowValidationError()
    {
        // Arrange
        var viewModel = new CustomerFormViewModel();
        var view = new CustomerFormView { DataContext = viewModel };
        
        // Act
        viewModel.FirstName = "";
        var canExecute = viewModel.SaveCommand.CanExecute(null);
        
        // Assert
        Assert.False(canExecute);
    }
}
```

### 2. Tests de Navigation
```csharp
public class NavigationServiceTests
{
    [Fact]
    public async Task NavigateToCustomerList_ShouldChangeCurrentView()
    {
        // Arrange
        var navigationService = new NavigationService();
        
        // Act
        await navigationService.NavigateToAsync<CustomersListViewModel>();
        
        // Assert
        Assert.IsType<CustomersListViewModel>(navigationService.CurrentViewModel);
    }
}
```

### 3. Tests de ViewModels (sans UI)
```csharp
public class CustomerFormViewModelTests
{
    private readonly Mock<ICustomerService> _customerServiceMock;
    
    [Fact]
    public async Task SaveCommand_WithValidData_ShouldCallService()
    {
        // Arrange
        var viewModel = new CustomerFormViewModel(_customerServiceMock.Object)
        {
            FirstName = "Jean",
            LastName = "Dupont"
        };
        
        // Act
        await viewModel.SaveCommand.ExecuteAsync(null);
        
        // Assert
        _customerServiceMock.Verify(s => s.CreateCustomerAsync(It.IsAny<Customer>()), 
                                    Times.Once);
    }
}
```

---

## 📦 Build & Publication

### Build Release

```bash
# Build optimisé
dotnet build -c Release

# Publish avec runtime inclus (Windows x64)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Output :
# src/MMV.App/bin/Release/net8.0/win-x64/publish/MMV.App.exe
```

### Créer un Installateur (Optionnel)

#### Inno Setup (Windows)
```iss
; Script Inno Setup
[Setup]
AppName=ManageMyVision
AppVersion=1.0
DefaultDirName={pf}\ManageMyVision
OutputDir=Output
OutputBaseFilename=MMV-Setup

[Files]
Source: "src\MMV.App\bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{commondesktop}\ManageMyVision"; Filename: "{app}\MMV.App.exe"
```

---

## 📊 Diagramme d'Architecture Complet

```
┌─────────────────────────────────────────────────────────────────┐
│                          UTILISATEUR                             │
└──────────────────────────┬──────────────────────────────────────┘
                           │ Interact avec
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                      MMV.App (Avalonia UI)                       │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐   │
│  │   Views      │  │  ViewModels  │  │  Services (UI)     │   │
│  │   (XAML)     │◄─┤  (C# + MVVM) │◄─┤  - Navigation      │   │
│  │              │  │              │  │  - Dialog          │   │
│  │ • Customers  │  │ • Commands   │  │  - Notification    │   │
│  │ • Products   │  │ • Properties │  │  - Print           │   │
│  │ • Orders     │  │ • Validation │  └────────────────────┘   │
│  └──────────────┘  └──────────────┘                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │ Appelle
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                   MMV.Infrastructure (EF Core)                   │
│  ┌──────────────────┐  ┌─────────────────┐  ┌──────────────┐  │
│  │  Repositories    │  │  OpticDbContext │  │  Migrations  │  │
│  │  (CRUD)          │──┤  (EF Core)      │◄─┤  (Schema)    │  │
│  │                  │  │                 │  │              │  │
│  │ • UserRepo       │  │ DbSet<User>     │  │ InitialCreate│  │
│  │ • CustomerRepo   │  │ DbSet<Customer> │  │ AddOrders... │  │
│  │ • ProductRepo    │  │ DbSet<Product>  │  │              │  │
│  └──────────────────┘  └─────────────────┘  └──────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ Accède à
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                  SQLite Database (mmv.db)                        │
│  ┌───────────┐ ┌──────────┐ ┌──────────┐ ┌─────────────────┐  │
│  │   users   │ │customers │ │ products │ │  prescriptions  │  │
│  └───────────┘ └──────────┘ └──────────┘ └─────────────────┘  │
│  ┌───────────┐ ┌──────────┐ ┌──────────┐ ┌─────────────────┐  │
│  │  orders   │ │  sales   │ │  stock_  │ │ product_        │  │
│  │           │ │          │ │movements │ │ categories      │  │
│  └───────────┘ └──────────┘ └──────────┘ └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼ Backups quotidiens
┌─────────────────────────────────────────────────────────────────┐
│           Backups/ (mmv_backup_YYYYMMDD_HHMMSS.db)              │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔧 Configuration & Variables d'Environnement

### appsettings.json

```json
{
  "ConnectionStrings": {
    "OpticDatabase": "Data Source=mmv.db"
  },
  "DatabaseSettings": {
    "AutoMigrate": true,
    "EnableSensitiveDataLogging": false
  },
  "Security": {
    "PasswordMinLength": 8,
    "SessionTimeoutMinutes": 30,
    "MaxLoginAttempts": 5
  },
  "Backup": {
    "Enabled": true,
    "IntervalHours": 24,
    "RetentionDays": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

---

**Document mis à jour le** : 27 janvier 2026
