# Guide Complet : Boutons, Bindings et Architecture MVVM

Ce guide explique en détail comment fonctionnent les boutons dans l'application MMV Desktop, depuis l'interface utilisateur jusqu'à la base de données.

## Table des Matières

1. [Architecture Générale](#1-architecture-générale)
2. [Comment lier AXAML, Code-Behind et ViewModel](#2-comment-lier-axaml-code-behind-et-viewmodel)
3. [Le Pattern Command](#3-le-pattern-command)
4. [Flux Complet : Du Clic au Database](#4-flux-complet--du-clic-au-database)
5. [Exemples Pratiques](#5-exemples-pratiques)
6. [Erreurs Courantes et Solutions](#6-erreurs-courantes-et-solutions)

---

## 1. Architecture Générale

### Structure en Couches

```
┌─────────────────────────────────────────────────────────────────┐
│                        INTERFACE (UI)                           │
│  ┌─────────────────┐    ┌─────────────────┐                     │
│  │  Vue (.axaml)   │ ←→ │ Code-Behind     │                     │
│  │                 │    │ (.axaml.cs)     │                     │
│  └────────┬────────┘    └─────────────────┘                     │
│           │ DataContext Binding                                 │
├───────────┼─────────────────────────────────────────────────────┤
│           ▼                                                     │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                    VIEWMODEL                                ││
│  │  • Propriétés (FirstName, LastName, etc.)                   ││
│  │  • Commandes (SaveCommand, CancelCommand)                   ││
│  │  • Événements (CustomerSaved, Cancelled)                    ││
│  │  • Logique de présentation                                  ││
│  └────────────────────────────┬────────────────────────────────┘│
├───────────────────────────────┼─────────────────────────────────┤
│                               ▼                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                    DOMAIN                                   ││
│  │  • Entités (Customer, Order, Product)                       ││
│  │  • Interfaces (ICustomerRepository, IUnitOfWork)            ││
│  │  • Règles métier                                            ││
│  └────────────────────────────┬────────────────────────────────┘│
├───────────────────────────────┼─────────────────────────────────┤
│                               ▼                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                 INFRASTRUCTURE                              ││
│  │  • Repositories (CustomerRepository)                        ││
│  │  • DbContext (AppDbContext)                                 ││
│  │  • Migrations                                               ││
│  └────────────────────────────┬────────────────────────────────┘│
│                               ▼                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │                   DATABASE (SQLite)                         ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Comment lier AXAML, Code-Behind et ViewModel

### 2.1 Déclarer le DataType dans AXAML

Pour que les bindings compilés fonctionnent, il faut déclarer le type de données :

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MMV.App.ViewModels"
             x:Class="MMV.App.Views.Clients.CustomerFormView"
             x:DataType="vm:CustomerFormViewModel">
```

**Explication :**
- `xmlns:vm="using:MMV.App.ViewModels"` : Importe le namespace des ViewModels
- `x:DataType="vm:CustomerFormViewModel"` : Indique le type du DataContext attendu
- Cela active les **bindings compilés** qui sont vérifiés à la compilation

### 2.2 Assigner le DataContext

Le DataContext peut être assigné de plusieurs façons :

#### Option 1 : Dans le XAML parent
```xml
<!-- Dans CustomersView.axaml -->
<views:CustomerFormView DataContext="{Binding CustomerFormViewModel}"/>
```

#### Option 2 : Dans le code-behind
```csharp
// Dans CustomersView.axaml.cs
public CustomersView()
{
    InitializeComponent();
    DataContext = new CustomersViewModel();
}
```

#### Option 3 : Via injection de dépendances (recommandé)
```csharp
// Dans App.axaml.cs ou ViewLocator
public override void OnFrameworkInitializationCompleted()
{
    var services = ConfigureServices();
    var mainWindow = new MainWindow
    {
        DataContext = services.GetRequiredService<MainWindowViewModel>()
    };
}
```

### 2.3 Le Code-Behind (.axaml.cs)

Le code-behind est la classe partielle associée au fichier XAML :

```csharp
// CustomerFormView.axaml.cs
namespace MMV.App.Views.Clients;

public partial class CustomerFormView : UserControl
{
    public CustomerFormView()
    {
        InitializeComponent();  // Charge le XAML
    }

    // Optionnel : intercepter les changements de DataContext
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        if (DataContext is CustomerFormViewModel vm)
        {
            // Le ViewModel est maintenant disponible
            Console.WriteLine($"DataContext assigné : {vm.GetType().Name}");
        }
    }
}
```

---

## 3. Le Pattern Command

### 3.1 Pourquoi utiliser des Commands ?

| Click Handler | Command |
|---------------|---------|
| Logique dans le code-behind | Logique dans le ViewModel |
| Difficile à tester | Facilement testable |
| Couplage fort View-Logique | Séparation des responsabilités |
| Pas de gestion CanExecute | CanExecute intégré |

### 3.2 L'interface ICommand

```csharp
public interface ICommand
{
    // Méthode appelée pour exécuter la commande
    void Execute(object? parameter);
    
    // Détermine si la commande peut être exécutée
    // Si false → le bouton est grisé (disabled)
    bool CanExecute(object? parameter);
    
    // Événement déclenché quand CanExecute change
    event EventHandler? CanExecuteChanged;
}
```

### 3.3 RelayCommand - Implémentation

```csharp
// Commands/RelayCommand.cs
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) 
        => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) 
        => _execute();

    public event EventHandler? CanExecuteChanged;

    // Appeler cette méthode quand l'état change
    public void RaiseCanExecuteChanged() 
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

### 3.4 Utilisation dans un ViewModel

```csharp
public class CustomerFormViewModel : BaseViewModel
{
    // Propriétés
    private string _firstName = string.Empty;
    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value))
            {
                // Notifier que CanExecute a peut-être changé
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // Commandes
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public CustomerFormViewModel(ICustomerRepository repo, IUnitOfWork uow)
    {
        // Associer Execute et CanExecute
        SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel, CanExecuteCancel);
    }

    // CanExecute : détermine si le bouton est actif
    private bool CanExecuteSave()
    {
        return !IsSaving && 
               !string.IsNullOrWhiteSpace(FirstName) && 
               !string.IsNullOrWhiteSpace(LastName);
    }

    private bool CanExecuteCancel() => !IsSaving;

    // Execute : la logique du bouton
    private async void ExecuteSave()
    {
        IsSaving = true;
        // ... logique de sauvegarde ...
        IsSaving = false;
    }

    private void ExecuteCancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
```

### 3.5 Binding dans XAML

```xml
<!-- ❌ MAUVAIS : Click handler -->
<Button Content="Enregistrer" Click="ButtonEnregistrer_Click"/>

<!-- ✅ BON : Command binding -->
<Button Content="Enregistrer" Command="{Binding SaveCommand}"/>

<!-- Avec paramètre -->
<Button Content="Supprimer" 
        Command="{Binding DeleteCommand}" 
        CommandParameter="{Binding SelectedCustomer}"/>
```

---

## 4. Flux Complet : Du Clic au Database

### 4.1 Diagramme de Séquence

```
┌─────────┐     ┌─────────────┐     ┌────────────┐     ┌────────────┐     ┌──────────┐
│  User   │     │   View      │     │ ViewModel  │     │ Repository │     │ Database │
└────┬────┘     └──────┬──────┘     └─────┬──────┘     └─────┬──────┘     └────┬─────┘
     │                 │                  │                  │                 │
     │  Clique sur     │                  │                  │                 │
     │  "Enregistrer"  │                  │                  │                 │
     │────────────────>│                  │                  │                 │
     │                 │                  │                  │                 │
     │                 │  CanExecute()?   │                  │                 │
     │                 │─────────────────>│                  │                 │
     │                 │                  │                  │                 │
     │                 │  true            │                  │                 │
     │                 │<─────────────────│                  │                 │
     │                 │                  │                  │                 │
     │                 │  Execute()       │                  │                 │
     │                 │─────────────────>│                  │                 │
     │                 │                  │                  │                 │
     │                 │                  │  CreateAsync()   │                 │
     │                 │                  │─────────────────>│                 │
     │                 │                  │                  │                 │
     │                 │                  │                  │  INSERT INTO    │
     │                 │                  │                  │────────────────>│
     │                 │                  │                  │                 │
     │                 │                  │  SaveChanges()   │  COMMIT         │
     │                 │                  │─────────────────>│────────────────>│
     │                 │                  │                  │                 │
     │                 │  PropertyChanged │                  │                 │
     │                 │<─────────────────│                  │                 │
     │                 │                  │                  │                 │
     │  UI mise à jour │                  │                  │                 │
     │<────────────────│                  │                  │                 │
     │                 │                  │                  │                 │
```

### 4.2 Code Step-by-Step

#### Étape 1 : L'utilisateur clique sur "Enregistrer"

```xml
<!-- CustomerFormView.axaml -->
<Button Content="Enregistrer" Command="{Binding SaveCommand}"/>
```

#### Étape 2 : Avalonia vérifie CanExecute

```csharp
// CustomerFormViewModel.cs
private bool CanExecuteSave()
{
    // Le bouton n'est actif que si :
    // - On n'est pas en train de sauvegarder
    // - FirstName n'est pas vide
    // - LastName n'est pas vide
    return !IsSaving && 
           !string.IsNullOrWhiteSpace(FirstName) && 
           !string.IsNullOrWhiteSpace(LastName);
}
```

#### Étape 3 : Si CanExecute = true, Execute est appelé

```csharp
// CustomerFormViewModel.cs
private async void ExecuteSave()
{
    IsSaving = true;  // Désactive les boutons
    
    try
    {
        // Créer l'entité Customer
        var customer = new Customer
        {
            FirstName = FirstName,
            LastName = LastName,
            Email = Email,
            // ... autres propriétés
        };

        // Appel au Repository
        await _customerRepository.CreateAsync(customer);
        
        // Commit dans la base de données
        await _unitOfWork.SaveChangesAsync();
        
        // Notifier le succès
        CustomerSaved?.Invoke(this, customer);
    }
    catch (Exception ex)
    {
        ErrorMessage = ex.Message;
    }
    finally
    {
        IsSaving = false;  // Réactive les boutons
    }
}
```

#### Étape 4 : Le Repository interagit avec EF Core

```csharp
// Infrastructure/Repositories/CustomerRepository.cs
public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _context;

    public async Task CreateAsync(Customer customer)
    {
        await _context.Customers.AddAsync(customer);
    }
}
```

#### Étape 5 : UnitOfWork sauvegarde en DB

```csharp
// Infrastructure/Data/UnitOfWork.cs
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
}
```

#### Étape 6 : L'événement ferme le formulaire

```csharp
// CustomersViewModel.cs
private async void OnCustomerFormSaved(object? sender, Customer customer)
{
    // Rafraîchir la liste
    await CustomersListViewModel.LoadCustomersAsync();
    
    // Fermer le formulaire
    IsInEditMode = false;
    IsCreatingNew = false;
}
```

---

## 5. Exemples Pratiques

### 5.1 Bouton avec validation

```csharp
// ViewModel
public class LoginViewModel : BaseViewModel
{
    public string Username { get; set; }
    public string Password { get; set; }
    
    public RelayCommand LoginCommand { get; }
    
    public LoginViewModel()
    {
        LoginCommand = new RelayCommand(
            execute: async () => await LoginAsync(),
            canExecute: () => !string.IsNullOrEmpty(Username) 
                           && !string.IsNullOrEmpty(Password)
        );
    }
}
```

```xml
<!-- View -->
<TextBox Text="{Binding Username}"/>
<TextBox Text="{Binding Password}"/>
<Button Content="Se connecter" Command="{Binding LoginCommand}"/>
```

### 5.2 Bouton avec paramètre

```csharp
// ViewModel
public RelayCommand<Customer> EditCommand { get; }

public MyViewModel()
{
    EditCommand = new RelayCommand<Customer>(
        execute: customer => OpenEditForm(customer),
        canExecute: customer => customer != null
    );
}
```

```xml
<!-- View -->
<Button Content="Modifier" 
        Command="{Binding EditCommand}" 
        CommandParameter="{Binding SelectedCustomer}"/>
```

### 5.3 Bouton de suppression avec confirmation

```csharp
public RelayCommand DeleteCommand { get; }

private async void ExecuteDelete()
{
    if (SelectedCustomer == null) return;
    
    // Demander confirmation (via un service de dialogue)
    var confirmed = await _dialogService.ConfirmAsync(
        "Confirmer la suppression",
        $"Supprimer {SelectedCustomer.FullName} ?"
    );
    
    if (!confirmed) return;
    
    await _repository.DeleteAsync(SelectedCustomer);
    await _unitOfWork.SaveChangesAsync();
    await LoadCustomersAsync();
}
```

---

## 6. Erreurs Courantes et Solutions

### ❌ Problème : Le bouton ne fait rien

**Cause possible 1 : Click handler au lieu de Command**
```xml
<!-- ❌ Mauvais -->
<Button Click="Button_Click"/>

<!-- ✅ Correct -->
<Button Command="{Binding MyCommand}"/>
```

**Cause possible 2 : DataContext incorrect**
```csharp
// Vérifier dans OnDataContextChanged
protected override void OnDataContextChanged(EventArgs e)
{
    base.OnDataContextChanged(e);
    Console.WriteLine($"DataContext: {DataContext?.GetType().Name ?? "NULL"}");
}
```

### ❌ Problème : Le bouton est grisé (disabled)

**Cause : CanExecute retourne false**

```csharp
// Vérifier la logique de CanExecute
private bool CanExecuteSave()
{
    Console.WriteLine($"CanExecuteSave: IsSaving={IsSaving}, " +
                      $"FirstName='{FirstName}', LastName='{LastName}'");
    return !IsSaving && 
           !string.IsNullOrWhiteSpace(FirstName) && 
           !string.IsNullOrWhiteSpace(LastName);
}
```

**Solution : S'assurer que RaiseCanExecuteChanged est appelé**
```csharp
public string FirstName
{
    get => _firstName;
    set
    {
        if (SetProperty(ref _firstName, value))
        {
            SaveCommand.RaiseCanExecuteChanged(); // ← Important !
        }
    }
}
```

### ❌ Problème : Les données ne sont pas sauvegardées

**Cause : SaveChangesAsync() n'est pas appelé**
```csharp
// ❌ Incomplet
await _repository.CreateAsync(customer);
// Les changements sont en mémoire mais pas en DB !

// ✅ Correct
await _repository.CreateAsync(customer);
await _unitOfWork.SaveChangesAsync();  // ← Commit en DB
```

### ❌ Problème : L'événement n'est pas reçu

**Cause : Pas d'abonnement ou désinscription prématurée**
```csharp
// Dans le parent ViewModel
CustomerFormViewModel.CustomerSaved += OnCustomerFormSaved;  // ← S'abonner
CustomerFormViewModel.Cancelled += OnCustomerFormCancelled;

// Ne pas oublier de se désabonner quand le formulaire est fermé
private void CloseForm()
{
    if (CustomerFormViewModel != null)
    {
        CustomerFormViewModel.CustomerSaved -= OnCustomerFormSaved;
        CustomerFormViewModel.Cancelled -= OnCustomerFormCancelled;
    }
    CustomerFormViewModel = null;
}
```

---

## Résumé

| Élément | Fichier | Rôle |
|---------|---------|------|
| Vue | `.axaml` | Interface utilisateur, bindings |
| Code-behind | `.axaml.cs` | Initialisation, gestion technique |
| ViewModel | `*ViewModel.cs` | Logique, propriétés, commandes |
| Repository | `*Repository.cs` | Accès aux données |
| Entity | `*.cs` (Domain) | Modèle de données |

### Checklist pour un bouton fonctionnel

- [ ] Le bouton utilise `Command="{Binding MyCommand}"` (pas `Click`)
- [ ] Le ViewModel expose `public RelayCommand MyCommand { get; }`
- [ ] Le Command est initialisé dans le constructeur
- [ ] `CanExecute` retourne les bonnes conditions
- [ ] `RaiseCanExecuteChanged()` est appelé quand les conditions changent
- [ ] Le `DataContext` est correctement assigné
- [ ] Le `x:DataType` est déclaré dans le XAML
