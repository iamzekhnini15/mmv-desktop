# Sprint 7 - Module Ordonnances Médicales ⏳ EN COURS

> **Date de début** : 3 Février 2026  
> **Date de fin estimée** : 10 Février 2026  
> **Durée estimée** : 1 semaine  
> **Statut** : ⏳ **EN COURS**

---

## 📋 Vue d'Ensemble

Le Sprint 7 vise à créer un **module complet de gestion des ordonnances médicales** permettant aux opticiens de :
- Saisir les prescriptions optiques (OD/OG)
- Valider les paramètres optiques selon les normes
- Visualiser les ordonnances clients
- Imprimer les prescriptions
- Lier directement une ordonnance à une commande

---

## 🎯 Objectifs du Sprint

- [ ] Liste des ordonnances par client
- [ ] Formulaire de saisie ordonnance structuré (OD/OG)
- [ ] Validation des valeurs optiques
- [ ] Fiche détaillée ordonnance
- [ ] Recherche d'ordonnances (par client, date, médecin)
- [ ] Lien direct vers création de commande
- [ ] Impression ordonnance
- [ ] Auto-complétion nom médecin
- [ ] Suggestion de verres basée sur prescription (reporté Sprint 8)
- [ ] Visualisation graphique (schéma œil) (reporté Sprint 8)

---

## 📦 Livrables Prévus

### ViewModels (5 fichiers)

#### 1. PrescriptionsViewModel.cs
**Rôle** : Coordinateur principal du module ordonnances

**Responsabilités** :
- Gestion de l'état global (liste / formulaire / détails)
- Navigation entre les différentes vues
- Coordination avec CustomerRepository

**Propriétés clés** :
```csharp
public bool IsInEditMode { get; set; }
public bool IsShowingDetail { get; set; }
public bool ShowList => !IsInEditMode && !IsShowingDetail;
public PrescriptionsListViewModel ListViewModel { get; }
public PrescriptionFormViewModel? FormViewModel { get; set; }
public PrescriptionDetailViewModel? DetailViewModel { get; set; }
```

---

#### 2. PrescriptionsListViewModel.cs
**Rôle** : Gestion de la liste avec recherche et filtres

**Propriétés** :
- `ObservableCollection<Prescription> Prescriptions` - Liste complète
- `ObservableCollection<Prescription> FilteredPrescriptions` - Résultats filtrés
- `ObservableCollection<Customer> Customers` - Pour filtrage par client
- `Customer? SelectedCustomer` - Filtre client actif
- `string SearchText` - Recherche médecin
- `DateTime? DateFrom, DateTo` - Filtre par période

**Commands** :
1. `CreateCommand` - Nouvelle ordonnance
2. `EditCommand` - Modifier ordonnance
3. `DeleteCommand` - Supprimer ordonnance
4. `RefreshCommand` - Recharger liste
5. `ViewDetailsCommand` - Voir détails
6. `PrintCommand` - Imprimer ordonnance
7. `CreateOrderCommand` - Créer commande depuis ordonnance
8. `NextPageCommand` - Pagination
9. `PreviousPageCommand` - Pagination

---

#### 3. PrescriptionFormViewModel.cs
**Rôle** : Formulaire de saisie avec validation stricte

**Structure des données** :
```csharp
// Informations générales
public long CustomerId { get; set; }
public Customer? SelectedCustomer { get; set; }
public DateTime PrescriptionDate { get; set; }
public string DoctorName { get; set; }

// Œil droit (OD)
public double RightSphere { get; set; }
public double RightCylinder { get; set; }
public int RightAxis { get; set; }
public double RightAddition { get; set; }
public double RightPrism { get; set; }

// Œil gauche (OG)
public double LeftSphere { get; set; }
public double LeftCylinder { get; set; }
public int LeftAxis { get; set; }
public double LeftAddition { get; set; }
public double LeftPrism { get; set; }

// Distance interpupillaire
public double PupillaryDistance { get; set; }

// Notes
public string? Notes { get; set; }
```

**Validation métier stricte** :
```csharp
private void ValidateSphere(double value, string eye) {
    if (value < -20 || value > 20) {
        SetError($"{eye}SphereError", "La sphère doit être entre -20 et +20");
    } else {
        ClearError($"{eye}SphereError");
    }
}

private void ValidateCylinder(double value, string eye) {
    if (value < -6 || value > 6) {
        SetError($"{eye}CylinderError", "Le cylindre doit être entre -6 et +6");
    } else {
        ClearError($"{eye}CylinderError");
    }
}

private void ValidateAxis(int value, string eye) {
    if (value < 0 || value > 180) {
        SetError($"{eye}AxisError", "L'axe doit être entre 0 et 180°");
    } else {
        ClearError($"{eye}AxisError");
    }
}

private void ValidateAddition(double value, string eye) {
    if (value < 0 || value > 4) {
        SetError($"{eye}AdditionError", "L'addition doit être entre 0 et +4");
    } else {
        ClearError($"{eye}AdditionError");
    }
}
```

**Commands** :
- `SaveCommand` - Sauvegarde avec validation complète
- `CancelCommand` - Annulation
- `SearchDoctorCommand` - Auto-complétion médecin

---

#### 4. PrescriptionDetailViewModel.cs
**Rôle** : Affichage détaillé ordonnance

**Propriétés** :
- `Prescription CurrentPrescription`
- `Customer Customer`
- `bool CanCreateOrder` - Si pas déjà liée à commande

**Méthodes** :
```csharp
public async Task LoadPrescriptionAsync(long prescriptionId) {
    CurrentPrescription = await _prescriptionRepository.GetByIdAsync(prescriptionId);
    Customer = await _customerRepository.GetByIdAsync(CurrentPrescription.CustomerId);
    
    // Vérifier si déjà liée à une commande
    var orders = await _orderRepository.GetByPrescriptionIdAsync(prescriptionId);
    CanCreateOrder = !orders.Any();
}
```

**Commands** :
- `PrintCommand` - Imprimer ordonnance
- `CreateOrderCommand` - Créer commande
- `EditCommand` - Modifier ordonnance
- `DeleteCommand` - Supprimer ordonnance

---

#### 5. DoctorAutoCompleteViewModel.cs
**Rôle** : Auto-complétion noms médecins (optionnel)

**Propriétés** :
- `ObservableCollection<string> DoctorSuggestions`
- `string SearchText`

**Méthode** :
```csharp
public async Task LoadSuggestionsAsync(string searchText) {
    // Récupérer les médecins uniques depuis les prescriptions existantes
    var doctors = await _prescriptionRepository.GetUniqueDoctorNamesAsync(searchText);
    DoctorSuggestions.Clear();
    foreach (var doctor in doctors) {
        DoctorSuggestions.Add(doctor);
    }
}
```

---

### Views (4 fichiers)

#### 1. PrescriptionsView.axaml
**Structure** :
- En-tête avec titre et compteur
- Barre de filtres (client, date, médecin)
- DataGrid ordonnances
- Footer avec pagination

**Gestion de visibilité** :
```xml
<!-- Liste visible par défaut -->
<ScrollViewer IsVisible="{Binding ShowList}">
    <!-- Filtres -->
    <StackPanel Orientation="Horizontal">
        <ComboBox ItemsSource="{Binding Customers}" 
                  SelectedItem="{Binding SelectedCustomer}"
                  PlaceholderText="Filtrer par client"/>
        <DatePicker SelectedDate="{Binding DateFrom}" 
                    Watermark="Date début"/>
        <DatePicker SelectedDate="{Binding DateTo}" 
                    Watermark="Date fin"/>
        <TextBox Text="{Binding SearchText}" 
                 Watermark="Rechercher médecin"/>
    </StackPanel>
    
    <!-- DataGrid -->
    <DataGrid ItemsSource="{Binding FilteredPrescriptions}">
        <DataGrid.Columns>
            <DataGridTextColumn Header="Date" 
                                Binding="{Binding PrescriptionDate, StringFormat=dd/MM/yyyy}"/>
            <DataGridTextColumn Header="Client" 
                                Binding="{Binding Customer.FullName}"/>
            <DataGridTextColumn Header="Médecin" 
                                Binding="{Binding DoctorName}"/>
            <DataGridTextColumn Header="OD Sphère" 
                                Binding="{Binding RightSphere, StringFormat={}{0:+0.00;-0.00}}"/>
            <DataGridTextColumn Header="OD Cylindre" 
                                Binding="{Binding RightCylinder, StringFormat={}{0:+0.00;-0.00}}"/>
            <DataGridTextColumn Header="OG Sphère" 
                                Binding="{Binding LeftSphere, StringFormat={}{0:+0.00;-0.00}}"/>
            <DataGridTextColumn Header="OG Cylindre" 
                                Binding="{Binding LeftCylinder, StringFormat={}{0:+0.00;-0.00}}"/>
        </DataGrid.Columns>
    </DataGrid>
</ScrollViewer>

<!-- Formulaire visible en mode édition -->
<ContentControl Content="{Binding FormViewModel}" 
                IsVisible="{Binding IsInEditMode}"/>

<!-- Détails visible quand ordonnance sélectionnée -->
<ContentControl Content="{Binding DetailViewModel}" 
                IsVisible="{Binding IsShowingDetail}"/>
```

---

#### 2. PrescriptionFormView.axaml
**5 sections organisées** :

**Section 1 : Informations générales**
```xml
<StackPanel>
    <ComboBox ItemsSource="{Binding Customers}" 
              SelectedItem="{Binding SelectedCustomer}"
              PlaceholderText="Sélectionner un client *"/>
    <DatePicker SelectedDate="{Binding PrescriptionDate}"/>
    <TextBox Text="{Binding DoctorName}" 
             Watermark="Nom du médecin *"/>
</StackPanel>
```

**Section 2 : Œil droit (OD)**
```xml
<Border BorderBrush="#3498DB" BorderThickness="2" Padding="15" Margin="0,10,0,0">
    <StackPanel>
        <TextBlock Text="👁️ Œil Droit (OD)" FontSize="16" FontWeight="Bold"/>
        
        <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto,Auto,Auto">
            <!-- Ligne 1 : Sphère + Cylindre -->
            <StackPanel Grid.Column="0" Margin="0,0,10,0">
                <TextBlock Text="Sphère (SPH)"/>
                <NumericUpDown Value="{Binding RightSphere}" 
                               Minimum="-20" Maximum="20" Increment="0.25"
                               FormatString="F2"/>
                <TextBlock Text="{Binding RightSphereError}" Foreground="Red"/>
            </StackPanel>
            
            <StackPanel Grid.Column="1">
                <TextBlock Text="Cylindre (CYL)"/>
                <NumericUpDown Value="{Binding RightCylinder}" 
                               Minimum="-6" Maximum="6" Increment="0.25"
                               FormatString="F2"/>
                <TextBlock Text="{Binding RightCylinderError}" Foreground="Red"/>
            </StackPanel>
            
            <!-- Ligne 2 : Axe + Addition -->
            <StackPanel Grid.Row="1" Grid.Column="0" Margin="0,10,10,0">
                <TextBlock Text="Axe (AXE)"/>
                <NumericUpDown Value="{Binding RightAxis}" 
                               Minimum="0" Maximum="180" Increment="1"/>
                <TextBlock Text="{Binding RightAxisError}" Foreground="Red"/>
            </StackPanel>
            
            <StackPanel Grid.Row="1" Grid.Column="1" Margin="0,10,0,0">
                <TextBlock Text="Addition (ADD)"/>
                <NumericUpDown Value="{Binding RightAddition}" 
                               Minimum="0" Maximum="4" Increment="0.25"
                               FormatString="F2"/>
                <TextBlock Text="{Binding RightAdditionError}" Foreground="Red"/>
            </StackPanel>
            
            <!-- Ligne 3 : Prisme -->
            <StackPanel Grid.Row="2" Grid.Column="0" Margin="0,10,0,0">
                <TextBlock Text="Prisme (optionnel)"/>
                <NumericUpDown Value="{Binding RightPrism}" 
                               Minimum="0" Maximum="10" Increment="0.25"
                               FormatString="F2"/>
            </StackPanel>
        </Grid>
    </StackPanel>
</Border>
```

**Section 3 : Œil gauche (OG)** (structure identique à OD)

**Section 4 : Distance interpupillaire**
```xml
<StackPanel Margin="0,10,0,0">
    <TextBlock Text="Distance Interpupillaire (PD)"/>
    <NumericUpDown Value="{Binding PupillaryDistance}" 
                   Minimum="50" Maximum="75" Increment="0.5"
                   FormatString="F1"/>
</StackPanel>
```

**Section 5 : Notes**
```xml
<StackPanel Margin="0,10,0,0">
    <TextBlock Text="Notes (optionnel)"/>
    <TextBox Text="{Binding Notes}" 
             TextWrapping="Wrap" 
             Height="100" 
             AcceptsReturn="True"/>
</StackPanel>
```

**Footer avec actions**
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
    <Button Content="Annuler" Command="{Binding CancelCommand}"/>
    <Button Content="Enregistrer" Command="{Binding SaveCommand}" Margin="10,0,0,0"/>
</StackPanel>
```

---

#### 3. PrescriptionDetailView.axaml
**Affichage en deux colonnes** :

**Colonne gauche : Informations client + médecin**
```xml
<StackPanel>
    <TextBlock Text="Client" FontWeight="Bold"/>
    <TextBlock Text="{Binding Customer.FullName}"/>
    
    <TextBlock Text="Date de prescription" FontWeight="Bold" Margin="0,10,0,0"/>
    <TextBlock Text="{Binding CurrentPrescription.PrescriptionDate, StringFormat=dd/MM/yyyy}"/>
    
    <TextBlock Text="Médecin prescripteur" FontWeight="Bold" Margin="0,10,0,0"/>
    <TextBlock Text="{Binding CurrentPrescription.DoctorName}"/>
</StackPanel>
```

**Colonne droite : Valeurs optiques**
```xml
<Border BorderBrush="#3498DB" BorderThickness="2" Padding="15">
    <Grid ColumnDefinitions="Auto,*,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto">
        <TextBlock Grid.Row="0" Grid.Column="0" Text="" FontWeight="Bold"/>
        <TextBlock Grid.Row="0" Grid.Column="1" Text="OD" FontWeight="Bold" HorizontalAlignment="Center"/>
        <TextBlock Grid.Row="0" Grid.Column="2" Text="OG" FontWeight="Bold" HorizontalAlignment="Center"/>
        
        <TextBlock Grid.Row="1" Grid.Column="0" Text="Sphère" Margin="0,5,10,0"/>
        <TextBlock Grid.Row="1" Grid.Column="1" 
                   Text="{Binding CurrentPrescription.RightSphere, StringFormat={}{0:+0.00;-0.00}}" 
                   HorizontalAlignment="Center"/>
        <TextBlock Grid.Row="1" Grid.Column="2" 
                   Text="{Binding CurrentPrescription.LeftSphere, StringFormat={}{0:+0.00;-0.00}}" 
                   HorizontalAlignment="Center"/>
        
        <!-- Répéter pour Cylindre, Axe, Addition, Prisme -->
    </Grid>
</Border>
```

**Footer avec actions**
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
    <Button Content="Imprimer" Command="{Binding PrintCommand}"/>
    <Button Content="Créer commande" 
            Command="{Binding CreateOrderCommand}"
            IsEnabled="{Binding CanCreateOrder}"
            Margin="10,0,0,0"/>
    <Button Content="Modifier" Command="{Binding EditCommand}" Margin="10,0,0,0"/>
    <Button Content="Supprimer" Command="{Binding DeleteCommand}" Margin="10,0,0,0"/>
</StackPanel>
```

---

### Repository (1 méthode à ajouter)

#### IPrescriptionRepository.cs
```csharp
public interface IPrescriptionRepository : IRepository<Prescription> {
    Task<IEnumerable<Prescription>> GetByCustomerIdAsync(long customerId);
    Task<IEnumerable<Prescription>> GetByDateRangeAsync(DateTime from, DateTime to);
    Task<IEnumerable<string>> GetUniqueDoctorNamesAsync(string searchText);
}
```

---

## 🎨 Fonctionnalités Clés

### 1. Validation Stricte des Valeurs Optiques
**Règles métier** :
- **Sphère** : -20.00 à +20.00 (pas 0.25)
- **Cylindre** : -6.00 à +6.00 (pas 0.25)
- **Axe** : 0° à 180° (pas 1°)
- **Addition** : 0.00 à +4.00 (pas 0.25)
- **Prisme** : 0.00 à +10.00 (optionnel)
- **Distance interpupillaire** : 50 à 75 mm

### 2. Formatage des Valeurs
- Sphère/Cylindre : Afficher signe (+/-) même pour valeurs positives
- Exemple : `+2.50` au lieu de `2.5`
- Format : `{0:+0.00;-0.00}`

### 3. Auto-complétion Médecins
- Liste des médecins déjà saisis
- Recherche incrémentale
- Permet saisie libre (nouveau médecin)

### 4. Lien vers Commande
- Bouton "Créer commande" dans fiche détaillée
- Pré-remplit le formulaire commande avec :
  - Client
  - Prescription liée
  - Suggestion de verres (si implémenté)

### 5. Impression Ordonnance
- Format A4 professionnel
- En-tête avec coordonnées opticien
- Données client + médecin
- Tableau OD/OG
- Signature + cachet

---

## ✅ Critères d'Achèvement

### Tests Fonctionnels à Effectuer

#### Tests Formulaire
- [ ] Créer ordonnance avec valeurs valides
- [ ] Validation : Sphère hors limites → Erreur
- [ ] Validation : Cylindre hors limites → Erreur
- [ ] Validation : Axe hors limites → Erreur
- [ ] Validation : Addition hors limites → Erreur
- [ ] Client requis : Erreur si non sélectionné
- [ ] Date prescription : Ne peut pas être future
- [ ] Médecin requis : Erreur si vide
- [ ] Sauvegarde : Ordonnance créée en base
- [ ] Annulation : Aucune modification

#### Tests Liste & Filtres
- [ ] Liste complète affichée au chargement
- [ ] Filtre par client : Fonctionne
- [ ] Filtre par date : Fonctionne
- [ ] Recherche médecin : Fonctionne
- [ ] Cumul de filtres : Fonctionne
- [ ] Pagination : Fonctionne

#### Tests Détails
- [ ] Affichage ordonnance complète
- [ ] Bouton "Créer commande" actif si pas déjà liée
- [ ] Bouton "Créer commande" désactivé si déjà liée
- [ ] Modification : Formulaire pré-rempli
- [ ] Suppression : Confirmation demandée
- [ ] Impression : PDF généré correctement

---

## 📊 Progression

| Tâche | Statut | Notes |
|-------|--------|-------|
| PrescriptionsViewModel | ⏳ À faire | - |
| PrescriptionsListViewModel | ⏳ À faire | - |
| PrescriptionFormViewModel | ⏳ À faire | Validation prioritaire |
| PrescriptionDetailViewModel | ⏳ À faire | - |
| PrescriptionsView | ⏳ À faire | - |
| PrescriptionFormView | ⏳ À faire | Sections OD/OG |
| PrescriptionDetailView | ⏳ À faire | - |
| Repository méthodes | ⏳ À faire | GetUniqueDoctorNames |
| Tests unitaires | ⏳ À faire | Validation métier |
| Tests manuels | ⏳ À faire | Checklist complète |

---

## 🚧 Risques & Défis

### 1. Validation Complexe
**Défi** : Nombreux champs avec règles strictes
**Mitigation** : Créer des méthodes de validation réutilisables

### 2. Formatage des Valeurs
**Défi** : Affichage avec signes +/- obligatoires
**Solution** : Utiliser `StringFormat={}{0:+0.00;-0.00}`

### 3. NumericUpDown Avalonia
**Défi** : Binding TwoWay parfois capricieux
**Solution** : Spécifier explicitement `Mode=TwoWay`

### 4. Auto-complétion
**Défi** : Pas de contrôle AutoComplete natif Avalonia
**Solution** : Utiliser TextBox + ListBox + filtrage manuel

---

## 📚 Documentation à Créer

- [ ] Guide utilisateur saisie ordonnance
- [ ] Normes optiques appliquées (référence)
- [ ] Format impression ordonnance

---

## 🎓 Objectifs d'Apprentissage

1. **Validation métier complexe** : Multiples champs interdépendants
2. **Formulaires structurés** : Sections OD/OG symétriques
3. **NumericUpDown avancé** : Pas personnalisés, formatage
4. **Liens entre entités** : Prescription → Commande

---

**Sprint démarré le 3 Février 2026**  
**Prochaine mise à jour** : Fin de semaine (7 Février 2026)
