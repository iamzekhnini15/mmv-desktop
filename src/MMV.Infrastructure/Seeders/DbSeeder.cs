using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Seeders;

/// <summary>
/// Classe de seed pour l'initialisation des données de test dans la base de données.
/// Utilisée en développement et pour les tests.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Initialise la base de données avec des données de test.
    /// </summary>
    public static async Task SeedAsync(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Si les données existent déjà, ne pas re-seeder
        if (context.ProductCategories.Any())
        {
            return;
        }

        // 1. Créer les catégories de produits
        var categories = new List<ProductCategory>
        {
            new ProductCategory
            {
                Name = "Montures",
                Description = "Montures optiques"
            },
            new ProductCategory
            {
                Name = "Verres",
                Description = "Verres optiques"
            },
            new ProductCategory
            {
                Name = "Accessoires",
                Description = "Accessoires optiques"
            },
            new ProductCategory
            {
                Name = "Solutions de nettoyage",
                Description = "Produits de nettoyage et entretien"
            }
        };

        context.ProductCategories.AddRange(categories);
        await context.SaveChangesAsync();

        // 2. Créer les fournisseurs
        var suppliers = new List<Supplier>
        {
            new Supplier
            {
                Name = "Vision Plus",
                ReferenceCode = "VP001",
                ContactEmail = "contact@visionplus.fr",
                Phone = "+33 1 23 45 67 89"
            },
            new Supplier
            {
                Name = "Optics International",
                ReferenceCode = "OI002",
                ContactEmail = "info@optics-intl.com",
                Phone = "+33 2 34 56 78 90"
            },
            new Supplier
            {
                Name = "Luxe Frames",
                ReferenceCode = "LF003",
                ContactEmail = "sales@luxeframes.fr",
                Phone = "+33 3 45 67 89 01"
            }
        };

        context.Suppliers.AddRange(suppliers);
        await context.SaveChangesAsync();

        // 3. Créer les produits
        var montures = new List<Product>
        {
            new Product
            {
                Name = "Ray-Ban Aviator",
                Reference = "RB001",
                CategoryId = categories[0].CategoryId,
                SupplierId = suppliers[0].SupplierId,
                PurchasePrice = 60.00m,
                SalePrice = 129.99m,
                StockQuantity = 15,
                StockAlertThreshold = 5,
                IsActive = true,
                TechnicalSpecs = """{"material": "Métal", "color": "Gold", "style": "Aviator"}"""
            },
            new Product
            {
                Name = "Warby Parker Home Try-On",
                Reference = "WP002",
                CategoryId = categories[0].CategoryId,
                SupplierId = suppliers[1].SupplierId,
                PurchasePrice = 45.00m,
                SalePrice = 95.00m,
                StockQuantity = 8,
                StockAlertThreshold = 3,
                IsActive = true,
                TechnicalSpecs = """{"material": "Acétate", "color": "Black", "style": "Rond"}"""
            }
        };

        var verres = new List<Product>
        {
            new Product
            {
                Name = "Verre Progressif Standard",
                Reference = "VP001",
                CategoryId = categories[1].CategoryId,
                SupplierId = suppliers[0].SupplierId,
                PurchasePrice = 80.00m,
                SalePrice = 199.99m,
                StockQuantity = 30,
                StockAlertThreshold = 10,
                IsActive = true,
                TechnicalSpecs = """{"type": "Progressif", "indice": "1.5", "traitement": "Anti-reflet"}"""
            },
            new Product
            {
                Name = "Verre Bifocal",
                Reference = "VB002",
                CategoryId = categories[1].CategoryId,
                SupplierId = suppliers[1].SupplierId,
                PurchasePrice = 50.00m,
                SalePrice = 119.99m,
                StockQuantity = 20,
                StockAlertThreshold = 8,
                IsActive = true,
                TechnicalSpecs = """{"type": "Bifocal", "indice": "1.5", "traitement": "Standard"}"""
            }
        };

        var accessoires = new List<Product>
        {
            new Product
            {
                Name = "Étui rigide premium",
                Reference = "EC001",
                CategoryId = categories[2].CategoryId,
                SupplierId = suppliers[2].SupplierId,
                PurchasePrice = 5.00m,
                SalePrice = 12.99m,
                StockQuantity = 50,
                StockAlertThreshold = 15,
                IsActive = true,
                TechnicalSpecs = """{"material": "Cuir", "color": "Black"}"""
            },
            new Product
            {
                Name = "Chiffon microfibre",
                Reference = "CM002",
                CategoryId = categories[2].CategoryId,
                SupplierId = suppliers[0].SupplierId,
                PurchasePrice = 0.50m,
                SalePrice = 1.99m,
                StockQuantity = 200,
                StockAlertThreshold = 50,
                IsActive = true,
                TechnicalSpecs = """{"material": "Microfibre", "size": "Standard"}"""
            }
        };

        var solutions = new List<Product>
        {
            new Product
            {
                Name = "Solution nettoyante 500ml",
                Reference = "SN001",
                CategoryId = categories[3].CategoryId,
                SupplierId = suppliers[1].SupplierId,
                PurchasePrice = 2.00m,
                SalePrice = 5.99m,
                StockQuantity = 100,
                StockAlertThreshold = 25,
                IsActive = true,
                TechnicalSpecs = """{"volume": "500ml", "type": "Spray"}"""
            }
        };

        var allProducts = new List<Product>();
        allProducts.AddRange(montures);
        allProducts.AddRange(verres);
        allProducts.AddRange(accessoires);
        allProducts.AddRange(solutions);

        context.Products.AddRange(allProducts);
        await context.SaveChangesAsync();

        // 4. Créer les clients
        var customers = new List<Customer>
        {
            new Customer
            {
                FirstName = "Marie",
                LastName = "Dupont",
                Email = "marie.dupont@example.com",
                Phone = "+33 6 12 34 56 78",
                BirthDate = new DateTime(1985, 3, 15),
                Address = "123 Rue de la Paix, 75000 Paris",
                City = "Paris",
                PostalCode = "75000",
                SocialSecurityNumber = "1850315123456789",
                InsuranceName = "MAAF"
            },
            new Customer
            {
                FirstName = "Jean",
                LastName = "Martin",
                Email = "jean.martin@example.com",
                Phone = "+33 6 98 76 54 32",
                BirthDate = new DateTime(1978, 7, 22),
                Address = "456 Avenue des Champs, 75008 Paris",
                City = "Paris",
                PostalCode = "75008",
                SocialSecurityNumber = "1780722123456790",
                InsuranceName = "Axa"
            },
            new Customer
            {
                FirstName = "Sophie",
                LastName = "Bernard",
                Email = "sophie.bernard@example.com",
                Phone = "+33 6 45 67 89 01",
                BirthDate = new DateTime(1990, 11, 8),
                Address = "789 Rue de Rivoli, 75001 Paris",
                City = "Paris",
                PostalCode = "75001",
                SocialSecurityNumber = "1901108123456800",
                InsuranceName = "Mutuelle France"
            }
        };

        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();

        // 5. Créer les ordonnances
        var prescriptions = new List<Prescription>
        {
            new Prescription
            {
                CustomerId = customers[0].CustomerId,
                DoctorName = "Dr. Leclerc",
                IssueDate = DateTime.Now.AddMonths(-6),
                OdSphere = -1.50,
                OdCylinder = -0.75,
                OdAxis = 180,
                OdAddition = 2.00,
                OdPrismValue = 1.5,
                OdPrismBase = PrismBase.In,
                OgSphere = -1.25,
                OgCylinder = -0.50,
                OgAxis = 175,
                OgAddition = 2.00,
                OgPrismValue = 1.0,
                OgPrismBase = PrismBase.Out
            },
            new Prescription
            {
                CustomerId = customers[1].CustomerId,
                DoctorName = "Dr. Moreau",
                IssueDate = DateTime.Now.AddMonths(-3),
                OdSphere = -2.00,
                OdCylinder = -1.00,
                OdAxis = 90,
                OdAddition = 1.50,
                OdPrismValue = 0.0,
                OdPrismBase = PrismBase.In,
                OgSphere = -2.25,
                OgCylinder = -1.00,
                OgAxis = 85,
                OgAddition = 1.50,
                OgPrismValue = 0.0,
                OgPrismBase = PrismBase.In
            }
        };

        context.Prescriptions.AddRange(prescriptions);
        await context.SaveChangesAsync();
    }
}
