namespace MMV.App.ViewModels;

/// <summary>ViewModel pour la gestion des produits.</summary>
public class ProductsViewModel : BaseViewModel
{
    public ProductsViewModel()
    {
        Title = "Gestion des Produits";
    }
}

/// <summary>ViewModel pour la gestion des ordonnances.</summary>
public class PrescriptionsViewModel : BaseViewModel
{
    public PrescriptionsViewModel()
    {
        Title = "Gestion des Ordonnances";
    }
}

/// <summary>ViewModel pour la gestion des commandes.</summary>
public class OrdersViewModel : BaseViewModel
{
    public OrdersViewModel()
    {
        Title = "Gestion des Commandes";
    }
}

/// <summary>ViewModel pour la gestion des ventes (POS).</summary>
public class SalesViewModel : BaseViewModel
{
    public SalesViewModel()
    {
        Title = "Point de Vente";
    }
}

/// <summary>ViewModel pour les rapports et statistiques.</summary>
public class ReportsViewModel : BaseViewModel
{
    public ReportsViewModel()
    {
        Title = "Rapports & Statistiques";
    }
}

/// <summary>ViewModel pour les paramètres de l'application.</summary>
public class SettingsViewModel : BaseViewModel
{
    public SettingsViewModel()
    {
        Title = "Paramètres";
    }
}
