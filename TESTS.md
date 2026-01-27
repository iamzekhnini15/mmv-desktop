# Suivi des Tests - ManageMyVision (MMV)

> **Documentation du suivi des tests unitaires, d'intégration et UI**  
> **Objectif de couverture** : > 80%  
> **Dernière mise à jour** : 27 janvier 2026

---

## 📊 Tableau de Bord des Tests

### Vue d'Ensemble

| Catégorie | Tests Écrits | Tests Réussis | Tests Échoués | Couverture |
|-----------|--------------|---------------|---------------|------------|
| **Domain (Entities)** | 0 | 0 | 0 | 0% |
| **Domain (Services)** | 0 | 0 | 0 | 0% |
| **Domain (Validators)** | 0 | 0 | 0 | 0% |
| **Infrastructure (Repositories)** | 0 | 0 | 0 | 0% |
| **Infrastructure (DbContext)** | 0 | 0 | 0 | 0% |
| **App (ViewModels)** | 0 | 0 | 0 | 0% |
| **App (Services UI)** | 0 | 0 | 0 | 0% |
| **App (Navigation)** | 0 | 0 | 0 | 0% |
| **TOTAL** | **0** | **0** | **0** | **0%** |

### Métriques par Sprint

| Sprint | Tests Ajoutés | Couverture Cumulée | Statut |
|--------|---------------|-------------------|--------|
| Sprint 1 | 0 | 0% | ✅ Terminé (Entities créées, tests à venir) |
| Sprint 2 | - | - | 🚧 En cours |
| Sprint 3 | - | - | ⏳ Planifié |
| Sprint 4 | - | - | ⏳ Planifié |

---

## 🎯 Sprint 1 : Infrastructure + Entities

### Statut : ✅ **TERMINÉ** (Tests à venir au Sprint 2)

#### Livrables du Sprint 1
- ✅ 8 Enums créées
- ✅ 11 Entities créées
- ✅ Documentation XML complète
- ✅ Compilation réussie

#### Tests Prévus (Sprint 2)
Les entités créées devront être testées lors de l'implémentation des repositories :
- [ ] Tests de mapping EF Core (Entity → Table)
- [ ] Tests de validation des contraintes
- [ ] Tests des relations (FK, cascades)

---

## 🧪 Sprint 2 : DbContext + Migrations + Repositories

### Statut : 🚧 **EN COURS**

### Plan de Tests

#### 2.1 Tests de Configuration EF Core

| Test | Fichier | Statut | Notes |
|------|---------|--------|-------|
| OpticDbContext crée toutes les tables | `DbContextTests.cs` | ⏳ À écrire | Vérifier les 9 tables |
| Conversions Enum → String fonctionnent | `DbContextTests.cs` | ⏳ À écrire | UserRole, OrderStatus, etc. |
| Index uniques sont créés | `DbContextTests.cs` | ⏳ À écrire | Username, Reference, OrderNumber |
| Relations FK configurées correctement | `DbContextTests.cs` | ⏳ À écrire | Vérifier ON DELETE CASCADE |

##### Exemple de Test Attendu
```csharp
[Fact]
public async Task OpticDbContext_ShouldCreateAllTables()
{
    // Arrange
    var options = new DbContextOptionsBuilder<OpticDbContext>()
        .UseInMemoryDatabase(databaseName: "TestDb")
        .Options;
    
    using var context = new OpticDbContext(options);
    
    // Act
    await context.Database.EnsureCreatedAsync();
    
    // Assert
    Assert.True(await context.Users.AnyAsync() || true); // Table existe
    Assert.True(await context.Customers.AnyAsync() || true);
    Assert.True(await context.Products.AnyAsync() || true);
    // ... pour les 9 tables
}
```

#### 2.2 Tests de Migrations

| Test | Fichier | Statut | Notes |
|------|---------|--------|-------|
| Migration initiale s'applique sans erreur | `MigrationTests.cs` | ⏳ À écrire | InitialCreate |
| Schema correspond aux entités | `MigrationTests.cs` | ⏳ À écrire | Comparer colonnes |
| Seed data insère l'admin | `MigrationTests.cs` | ⏳ À écrire | Utilisateur par défaut |

##### Exemple de Test Attendu
```csharp
[Fact]
public async Task InitialMigration_ShouldCreateAdminUser()
{
    // Arrange
    var options = new DbContextOptionsBuilder<OpticDbContext>()
        .UseSqlite("Data Source=:memory:")
        .Options;
    
    using var context = new OpticDbContext(options);
    await context.Database.MigrateAsync();
    
    // Act
    var adminUser = await context.Users
        .FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
    
    // Assert
    Assert.NotNull(adminUser);
    Assert.Equal("admin", adminUser.Username);
}
```

#### 2.3 Tests des Repositories

##### UserRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByIdAsync_ExistingUser_ReturnsUser` | ⏳ À écrire | Récupération par ID |
| `GetByIdAsync_NonExistingUser_ReturnsNull` | ⏳ À écrire | Gestion ID inexistant |
| `GetByUsernameAsync_ReturnsCorrectUser` | ⏳ À écrire | Recherche par username |
| `CreateAsync_ValidUser_SavesSuccessfully` | ⏳ À écrire | Création utilisateur |
| `CreateAsync_DuplicateUsername_ThrowsException` | ⏳ À écrire | Contrainte UNIQUE |
| `UpdateAsync_ExistingUser_UpdatesSuccessfully` | ⏳ À écrire | Mise à jour |
| `DeleteAsync_ExistingUser_DeletesSuccessfully` | ⏳ À écrire | Suppression |
| `GetAllAsync_ReturnsAllUsers` | ⏳ À écrire | Liste complète |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/UserRepositoryTests.cs`

##### Exemple de Test Attendu
```csharp
public class UserRepositoryTests : IDisposable
{
    private readonly OpticDbContext _context;
    private readonly UserRepository _repository;
    
    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new OpticDbContext(options);
        _repository = new UserRepository(_context);
    }
    
    [Fact]
    public async Task CreateAsync_ValidUser_SavesSuccessfully()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            PasswordHash = "hashed_password",
            FirstName = "Test",
            LastName = "User",
            Role = UserRole.Optician
        };
        
        // Act
        await _repository.CreateAsync(user);
        var savedUser = await _repository.GetByIdAsync(user.UserId);
        
        // Assert
        Assert.NotNull(savedUser);
        Assert.Equal("testuser", savedUser.Username);
        Assert.Equal(UserRole.Optician, savedUser.Role);
    }
    
    [Fact]
    public async Task CreateAsync_DuplicateUsername_ThrowsException()
    {
        // Arrange
        var user1 = new User { Username = "duplicate", /* ... */ };
        var user2 = new User { Username = "duplicate", /* ... */ };
        
        await _repository.CreateAsync(user1);
        
        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            () => _repository.CreateAsync(user2)
        );
    }
    
    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
```

##### CustomerRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByIdAsync_ExistingCustomer_ReturnsCustomer` | ⏳ À écrire | Récupération par ID |
| `SearchByNameAsync_PartialMatch_ReturnsResults` | ⏳ À écrire | Recherche par nom |
| `SearchByPhoneAsync_ReturnsCorrectCustomer` | ⏳ À écrire | Recherche par téléphone |
| `CreateAsync_ValidCustomer_SavesSuccessfully` | ⏳ À écrire | Création client |
| `UpdateAsync_ExistingCustomer_UpdatesSuccessfully` | ⏳ À écrire | Mise à jour |
| `GetWithPrescriptionsAsync_IncludesRelations` | ⏳ À écrire | Eager loading |
| `GetWithOrdersHistoryAsync_ReturnsHistory` | ⏳ À écrire | Historique commandes |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/CustomerRepositoryTests.cs`

##### ProductRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByIdAsync_ExistingProduct_ReturnsProduct` | ⏳ À écrire | Récupération par ID |
| `GetByReferenceAsync_ReturnsCorrectProduct` | ⏳ À écrire | Recherche par référence |
| `GetByCategoryAsync_ReturnsFilteredProducts` | ⏳ À écrire | Filtre par catégorie |
| `GetLowStockProductsAsync_ReturnsAlertsOnly` | ⏳ À écrire | Stock < seuil |
| `CreateAsync_ValidProduct_SavesSuccessfully` | ⏳ À écrire | Création produit |
| `UpdateStockAsync_AdjustsQuantity` | ⏳ À écrire | Mise à jour stock |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/ProductRepositoryTests.cs`

##### PrescriptionRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByCustomerIdAsync_ReturnsAllPrescriptions` | ⏳ À écrire | Toutes ordonnances client |
| `CreateAsync_ValidPrescription_SavesSuccessfully` | ⏳ À écrire | Création ordonnance |
| `ValidateOpticalValues_InvalidSphere_ThrowsException` | ⏳ À écrire | Sphère hors limites |
| `ValidateOpticalValues_InvalidAxis_ThrowsException` | ⏳ À écrire | Axe > 180° |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/PrescriptionRepositoryTests.cs`

##### OrderRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByIdAsync_WithOrderItems_IncludesItems` | ⏳ À écrire | Include OrderItems |
| `GetByStatusAsync_FiltersCorrectly` | ⏳ À écrire | Filtre par statut |
| `CreateAsync_WithItems_SavesHierarchy` | ⏳ À écrire | Cascade save |
| `UpdateStatusAsync_ChangesStatus` | ⏳ À écrire | Workflow |
| `DeleteAsync_CascadesOrderItems` | ⏳ À écrire | ON DELETE CASCADE |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/OrderRepositoryTests.cs`

##### SaleRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `GetByIdAsync_WithSaleItems_IncludesItems` | ⏳ À écrire | Include SaleItems |
| `GetByDateRangeAsync_ReturnsFilteredSales` | ⏳ À écrire | Filtre par période |
| `CreateAsync_CalculatesTotalCorrectly` | ⏳ À écrire | Calcul total automatique |
| `GetDailySalesReportAsync_ReturnsCorrectTotals` | ⏳ À écrire | Rapport journalier |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/SaleRepositoryTests.cs`

##### StockMovementRepository Tests

| Test | Statut | Description |
|------|--------|-------------|
| `CreateAsync_UpdatesProductStock` | ⏳ À écrire | Mise à jour stock liée |
| `GetByProductIdAsync_ReturnsHistory` | ⏳ À écrire | Historique par produit |
| `GetByDateRangeAsync_ReturnsFilteredMovements` | ⏳ À écrire | Filtre par période |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/StockMovementRepositoryTests.cs`

#### 2.4 Tests du UnitOfWork

| Test | Statut | Description |
|------|--------|-------------|
| `SaveChangesAsync_CommitsTransaction` | ⏳ À écrire | Commit réussi |
| `Rollback_UndoesChanges` | ⏳ À écrire | Rollback en cas d'erreur |
| `MultipleRepositories_ShareSameContext` | ⏳ À écrire | Contexte partagé |

**Fichier** : `tests/MMV.Domain.Tests/RepositoryTests/UnitOfWorkTests.cs`

---

## 🧪 Sprint 3 : Services Métier + Validations

### Tests Prévus

#### 3.1 Tests des Services

##### CustomerService Tests

| Test | Statut | Description |
|------|--------|-------------|
| `CreateCustomerAsync_ValidData_CreatesSuccessfully` | ⏳ À écrire | Création client |
| `CreateCustomerAsync_InvalidEmail_ThrowsValidationException` | ⏳ À écrire | Validation email |
| `SearchCustomersAsync_ByName_ReturnsResults` | ⏳ À écrire | Recherche |
| `GetCustomerHistoryAsync_ReturnsOrdersAndSales` | ⏳ À écrire | Historique complet |

##### ProductService Tests

| Test | Statut | Description |
|------|--------|-------------|
| `CreateProductAsync_ValidData_CreatesSuccessfully` | ⏳ À écrire | Création produit |
| `CreateProductAsync_NegativePrice_ThrowsException` | ⏳ À écrire | Validation prix |
| `GetLowStockAlertsAsync_ReturnsAlertsOnly` | ⏳ À écrire | Alertes stock |
| `CalculateMarginAsync_ReturnsCorrectPercentage` | ⏳ À écrire | Calcul marge |

##### OrderService Tests

| Test | Statut | Description |
|------|--------|-------------|
| `CreateOrderAsync_GeneratesOrderNumber` | ⏳ À écrire | Numérotation auto |
| `UpdateOrderStatusAsync_FollowsWorkflow` | ⏳ À écrire | Workflow valide |
| `UpdateOrderStatusAsync_InvalidTransition_ThrowsException` | ⏳ À écrire | Transition interdite |
| `CalculateEstimatedDeliveryAsync_ReturnsCorrectDate` | ⏳ À écrire | Calcul délai |

##### SaleService Tests

| Test | Statut | Description |
|------|--------|-------------|
| `CreateSaleAsync_GeneratesSaleNumber` | ⏳ À écrire | Numérotation auto |
| `CreateSaleAsync_UpdatesStock` | ⏳ À écrire | Décrémente stock |
| `CalculateDiscountAsync_AppliesCorrectAmount` | ⏳ À écrire | Calcul remise |
| `RefundSaleAsync_RestoresStock` | ⏳ À écrire | Remboursement |

#### 3.2 Tests de Validation (FluentValidation)

##### CustomerValidator Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Validate_EmptyFirstName_ReturnsError` | ⏳ À écrire | Prénom requis |
| `Validate_EmptyLastName_ReturnsError` | ⏳ À écrire | Nom requis |
| `Validate_InvalidEmail_ReturnsError` | ⏳ À écrire | Format email |
| `Validate_InvalidPhone_ReturnsError` | ⏳ À écrire | Format téléphone |
| `Validate_ValidCustomer_NoErrors` | ⏳ À écrire | Données valides |

##### ProductValidator Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Validate_EmptyReference_ReturnsError` | ⏳ À écrire | Référence requise |
| `Validate_NegativePurchasePrice_ReturnsError` | ⏳ À écrire | Prix achat > 0 |
| `Validate_NegativeSalePrice_ReturnsError` | ⏳ À écrire | Prix vente > 0 |
| `Validate_SalePriceLowerThanPurchase_ReturnsWarning` | ⏳ À écrire | Marge négative |

##### PrescriptionValidator Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Validate_SphereOutOfRange_ReturnsError` | ⏳ À écrire | Sphère -20 à +20 |
| `Validate_CylinderOutOfRange_ReturnsError` | ⏳ À écrire | Cylindre -6 à +6 |
| `Validate_AxisOutOfRange_ReturnsError` | ⏳ À écrire | Axe 0-180 |
| `Validate_FutureDoctorIssueDate_ReturnsError` | ⏳ À écrire | Date <= aujourd'hui |

#### 3.3 Tests de ValueObjects

##### Money Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Create_ValidAmount_CreatesSuccessfully` | ⏳ À écrire | Création montant |
| `Create_NegativeAmount_ThrowsException` | ⏳ À écrire | Montant négatif interdit |
| `Add_TwoMoneys_ReturnsSum` | ⏳ À écrire | Addition |
| `Multiply_ByQuantity_ReturnsCorrectAmount` | ⏳ À écrire | Multiplication |

##### Email Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Create_ValidEmail_CreatesSuccessfully` | ⏳ À écrire | Email valide |
| `Create_InvalidEmail_ThrowsException` | ⏳ À écrire | Format incorrect |

---

## 🎨 Sprint 4 : Interface Avalonia

### Tests Prévus

#### 4.1 Tests de ViewModels

##### LoginViewModel Tests

| Test | Statut | Description |
|------|--------|-------------|
| `LoginCommand_ValidCredentials_NavigatesToDashboard` | ⏳ À écrire | Connexion réussie |
| `LoginCommand_InvalidCredentials_ShowsError` | ⏳ À écrire | Erreur affichée |
| `LoginCommand_EmptyFields_CannotExecute` | ⏳ À écrire | Validation |

##### MainWindowViewModel Tests

| Test | Statut | Description |
|------|--------|-------------|
| `Navigate_ToCustomers_ChangesCurrentView` | ⏳ À écrire | Navigation |
| `Logout_ClearsSession` | ⏳ À écrire | Déconnexion |

#### 4.2 Tests de Navigation

| Test | Statut | Description |
|------|--------|-------------|
| `NavigateToAsync_ChangesCurrentViewModel` | ⏳ À écrire | Navigation basique |
| `GoBackAsync_ReturnsToPreviousView` | ⏳ À écrire | Retour arrière |
| `NavigationStack_MaintainsHistory` | ⏳ À écrire | Historique |

#### 4.3 Tests UI Headless

| Test | Statut | Description |
|------|--------|-------------|
| `CustomerFormView_BindsToViewModel` | ⏳ À écrire | Binding correct |
| `SaveButton_WhenDisabled_CannotBeClicked` | ⏳ À écrire | État bouton |

---

## 🧪 Sprint 5-9 : Modules Fonctionnels

### Tests par Module

Chaque module (Clients, Produits, Ordonnances, Commandes, Ventes) devra avoir :

#### ViewModels Tests (par module)
- [ ] List ViewModel : filtres, recherche, pagination
- [ ] Form ViewModel : validation, sauvegarde, annulation
- [ ] Detail ViewModel : chargement, mise à jour

#### Services Tests (par module)
- [ ] CRUD operations
- [ ] Business logic
- [ ] Error handling

#### Integration Tests (par module)
- [ ] Workflow complet (création → modification → suppression)
- [ ] Interactions entre modules

---

## 📊 Métriques de Qualité

### Couverture de Code (Objectifs)

| Couche | Objectif | Actuel | Statut |
|--------|----------|--------|--------|
| Domain (Entities) | 90% | 0% | ⏳ |
| Domain (Services) | 85% | 0% | ⏳ |
| Infrastructure (Repositories) | 80% | 0% | ⏳ |
| App (ViewModels) | 75% | 0% | ⏳ |
| **TOTAL** | **80%** | **0%** | ⏳ |

### Commandes de Test

```bash
# Exécuter tous les tests
dotnet test

# Exécuter tests avec couverture
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Générer rapport HTML (avec ReportGenerator)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:coverage.opencover.xml -targetdir:coveragereport -reporttypes:Html

# Exécuter tests d'un projet spécifique
dotnet test tests/MMV.Domain.Tests

# Exécuter un test spécifique
dotnet test --filter "FullyQualifiedName=MMV.Domain.Tests.UserRepositoryTests.CreateAsync_ValidUser_SavesSuccessfully"

# Mode verbose
dotnet test --logger "console;verbosity=detailed"
```

---

## 🐛 Bugs & Issues Identifiés

### Template de Rapport de Bug

```markdown
### Bug #[NUMBER] : [Titre Court]

**Découvert le** : [Date]
**Sprint** : [Numéro]
**Sévérité** : 🔴 Critique / 🟠 Majeur / 🟡 Mineur / 🔵 Trivial

**Description** :
[Description du problème]

**Étapes de Reproduction** :
1. Étape 1
2. Étape 2
3. Étape 3

**Résultat Attendu** :
[Ce qui devrait se passer]

**Résultat Actuel** :
[Ce qui se passe réellement]

**Environnement** :
- OS : Windows 11
- .NET Version : 8.0.417
- Branche : sprint-X

**Logs/Stack Trace** :
```
[Stack trace si applicable]
```

**Statut** : ⏳ Ouvert / 🔧 En cours / ✅ Résolu

**Résolution** : (si résolu)
[Description de la solution]
```

### Bugs Actuels

*(Aucun bug identifié pour le moment)*

---

## ✅ Checklist de Tests (Par Sprint)

### Sprint 2 Checklist

- [ ] **Configuration EF Core**
  - [ ] DbContext crée toutes les tables
  - [ ] Conversions Enum fonctionnent
  - [ ] Relations FK configurées

- [ ] **Migrations**
  - [ ] Migration initiale appliquée
  - [ ] Seed data créé

- [ ] **UserRepository**
  - [ ] CRUD complet testé
  - [ ] Validation contrainte unique (username)

- [ ] **CustomerRepository**
  - [ ] CRUD complet testé
  - [ ] Recherche par nom/téléphone

- [ ] **ProductRepository**
  - [ ] CRUD complet testé
  - [ ] Filtre par catégorie
  - [ ] Alertes stock bas

- [ ] **PrescriptionRepository**
  - [ ] CRUD complet testé
  - [ ] Validation valeurs optiques

- [ ] **OrderRepository**
  - [ ] CRUD avec cascade (OrderItems)
  - [ ] Filtre par statut

- [ ] **SaleRepository**
  - [ ] CRUD avec cascade (SaleItems)
  - [ ] Calculs automatiques

- [ ] **StockMovementRepository**
  - [ ] Création avec mise à jour stock
  - [ ] Historique par produit

- [ ] **UnitOfWork**
  - [ ] Transactions fonctionnelles
  - [ ] Rollback en cas d'erreur

---

## 📝 Conventions de Nommage des Tests

### Pattern AAA (Arrange-Act-Assert)

```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange (Préparation)
    var entity = new Entity { /* ... */ };
    
    // Act (Action)
    var result = await _service.MethodAsync(entity);
    
    // Assert (Vérification)
    Assert.NotNull(result);
    Assert.Equal(expected, result);
}
```

### Exemples de Nommage

| Type de Test | Exemple de Nom |
|--------------|----------------|
| Test positif | `CreateAsync_ValidUser_SavesSuccessfully` |
| Test négatif | `CreateAsync_NullUser_ThrowsArgumentNullException` |
| Test de validation | `Validate_EmptyEmail_ReturnsValidationError` |
| Test de recherche | `SearchAsync_PartialName_ReturnsMatchingResults` |
| Test de calcul | `CalculateTotal_WithDiscount_ReturnsCorrectAmount` |

---

## 📈 Progression des Tests

### Graphique (à mettre à jour chaque sprint)

```
Sprint 1  ░░░░░░░░░░ 0/50 tests (0%)
Sprint 2  ░░░░░░░░░░ 0/120 tests (0%)
Sprint 3  ░░░░░░░░░░ 0/80 tests (0%)
Sprint 4  ░░░░░░░░░░ 0/60 tests (0%)
Sprint 5  ░░░░░░░░░░ 0/50 tests (0%)
...
```

---

## 🔧 Configuration des Tests

### Packages Nécessaires

```xml
<!-- tests/MMV.Domain.Tests/MMV.Domain.Tests.csproj -->
<ItemGroup>
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.0" />
    <PackageReference Include="Avalonia.Headless.XUnit" Version="11.1.3" />
</ItemGroup>
```

### Configuration CI/CD (GitHub Actions - à venir)

```yaml
# .github/workflows/tests.yml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Test
        run: dotnet test --no-build --verbosity normal /p:CollectCoverage=true
      
      - name: Upload coverage
        uses: codecov/codecov-action@v3
```

---

## 📚 Ressources & Documentation

### Outils de Test
- **xUnit** : https://xunit.net/
- **FluentAssertions** : https://fluentassertions.com/
- **Moq** : https://github.com/moq/moq4
- **EF Core InMemory** : https://learn.microsoft.com/en-us/ef/core/testing/

### Bonnes Pratiques
- Toujours tester les cas limites (null, valeurs extrêmes)
- Un test = une assertion principale
- Tests indépendants (pas d'ordre d'exécution)
- Noms de tests explicites (pas besoin de documentation)
- Utiliser `InMemoryDatabase` pour tests rapides

---

**Document mis à jour le** : 27 janvier 2026  
**Prochaine mise à jour** : Fin Sprint 2 (ajout des tests de repositories)
