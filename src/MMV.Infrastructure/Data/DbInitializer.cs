using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Initialise la base de données avec des données de test.
/// </summary>
public static class DbInitializer
{
    public static void Initialize(OpticDbContext context)
    {
        // S'assurer que la BD est créée
        context.Database.EnsureCreated();

        // Si les clients existent déjà, ne pas ajouter
        if (context.Customers.Any())
        {
            return;
        }

        // Créer des clients
        var customers = new List<Customer>
        {
            new Customer
            {
                FirstName = "Jean",
                LastName = "Dupont",
                Email = "jean.dupont@email.com",
                Phone = "0123456789",
                BirthDate = new DateTime(1985, 5, 15),
                Address = "123 Rue de la Paix",
                City = "Paris",
                PostalCode = "75001",
                SocialSecurityNumber = "1850515123456",
                InsuranceName = "Mutuelle de France",
                Notes = "Client régulier",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Marie",
                LastName = "Martin",
                Email = "marie.martin@email.com",
                Phone = "0198765432",
                BirthDate = new DateTime(1990, 8, 22),
                Address = "456 Avenue des Champs",
                City = "Lyon",
                PostalCode = "69000",
                SocialSecurityNumber = "1900822654321",
                InsuranceName = "Allianz",
                Notes = "Nouveaux lunettes cette semaine",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Pierre",
                LastName = "Bernard",
                Email = "pierre.bernard@email.com",
                Phone = "0234567890",
                BirthDate = new DateTime(1978, 3, 10),
                Address = "789 Boulevard Saint-Germain",
                City = "Marseille",
                PostalCode = "13000",
                SocialSecurityNumber = "1780310654987",
                InsuranceName = "AXA",
                Notes = "Besoin de vérification annuelle",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Sophie",
                LastName = "Lefevre",
                Email = "sophie.lefevre@email.com",
                Phone = "0345678901",
                BirthDate = new DateTime(1995, 11, 30),
                Address = "321 Rue de Rivoli",
                City = "Toulouse",
                PostalCode = "31000",
                SocialSecurityNumber = "1951130987654",
                InsuranceName = "MAAF",
                Notes = "Astigmatisme",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Paul",
                LastName = "Gaudin",
                Email = "paul.gaudin@email.com",
                Phone = "0456789012",
                BirthDate = new DateTime(1988, 6, 14),
                Address = "654 Rue de Turenne",
                City = "Nice",
                PostalCode = "06000",
                SocialSecurityNumber = "1880614123789",
                InsuranceName = "Générali",
                Notes = "Presbytie progressive",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Claire",
                LastName = "Rousseau",
                Email = "claire.rousseau@email.com",
                Phone = "0567890123",
                BirthDate = new DateTime(1992, 9, 5),
                Address = "987 Rue Saint-Antoine",
                City = "Nantes",
                PostalCode = "44000",
                SocialSecurityNumber = "1920905456123",
                InsuranceName = "Mutuelle de France",
                Notes = "",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Marc",
                LastName = "Fontaine",
                Email = "marc.fontaine@email.com",
                Phone = "0678901234",
                BirthDate = new DateTime(1980, 2, 28),
                Address = "147 Boulevard de la Liberté",
                City = "Strasbourg",
                PostalCode = "67000",
                SocialSecurityNumber = "1800228789456",
                InsuranceName = "SwissLife",
                Notes = "Myopie légère",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Isabelle",
                LastName = "Mercier",
                Email = "isabelle.mercier@email.com",
                Phone = "0789012345",
                BirthDate = new DateTime(1987, 7, 19),
                Address = "258 Avenue Montaigne",
                City = "Bordeaux",
                PostalCode = "33000",
                SocialSecurityNumber = "1870719654987",
                InsuranceName = "Allianz",
                Notes = "Lentilles de contact",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Luc",
                LastName = "Renault",
                Email = "luc.renault@email.com",
                Phone = "0890123456",
                BirthDate = new DateTime(1975, 12, 8),
                Address = "369 Rue du Faubourg",
                City = "Lille",
                PostalCode = "59000",
                SocialSecurityNumber = "1751208987654",
                InsuranceName = "Thales",
                Notes = "Client VIP",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Nathalie",
                LastName = "Petit",
                Email = "nathalie.petit@email.com",
                Phone = "0901234567",
                BirthDate = new DateTime(1993, 4, 25),
                Address = "456 Rue des Écoles",
                City = "Rennes",
                PostalCode = "35000",
                SocialSecurityNumber = "1930425123456",
                InsuranceName = "Poste",
                Notes = "Suivi regular",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Daniel",
                LastName = "Dupuis",
                Email = "daniel.dupuis@email.com",
                Phone = "0912345678",
                BirthDate = new DateTime(1970, 1, 16),
                Address = "789 Boulevard Victor",
                City = "Reims",
                PostalCode = "51100",
                SocialSecurityNumber = "1700116654987",
                InsuranceName = "Malakoff",
                Notes = "",
                CreatedAt = DateTime.Now
            },
            new Customer
            {
                FirstName = "Valérie",
                LastName = "Leroy",
                Email = "valerie.leroy@email.com",
                Phone = "0923456789",
                BirthDate = new DateTime(1989, 10, 3),
                Address = "321 Avenue Foch",
                City = "Saint-Étienne",
                PostalCode = "42000",
                SocialSecurityNumber = "1891003987654",
                InsuranceName = "Allianz",
                Notes = "Verres progressifs",
                CreatedAt = DateTime.Now
            },
        };

        context.Customers.AddRange(customers);
        context.SaveChanges();

        // Ajouter des produits
        var products = new List<Product>
        {
            new Product
            {
                Reference = "MONT-001",
                Name = "Montures aviateur titanium",
                Description = "Montures légères en titanium de haute qualité",
                PurchasePrice = 95.00m,
                SalePrice = 189.99m,
                StockQuantity = 15,
                IsActive = true
            },
            new Product
            {
                Reference = "VERRE-001",
                Name = "Verres progressifs premium",
                Description = "Verres multifocaux avec traitement anti-reflet",
                PurchasePrice = 150.00m,
                SalePrice = 299.99m,
                StockQuantity = 32,
                IsActive = true
            },
            new Product
            {
                Reference = "LENT-001",
                Name = "Lentilles de contact mensuelles",
                Description = "Lentilles souples confortables",
                PurchasePrice = 20.00m,
                SalePrice = 45.99m,
                StockQuantity = 120,
                IsActive = true
            },
            new Product
            {
                Reference = "ETUI-001",
                Name = "Étui à lunettes cuir",
                Description = "Étui de protection en cuir véritable",
                PurchasePrice = 15.00m,
                SalePrice = 29.99m,
                StockQuantity = 45,
                IsActive = true
            },
            new Product
            {
                Reference = "SOL-001",
                Name = "Solutions nettoyantes",
                Description = "Produit nettoyant pour verres et lentilles",
                PurchasePrice = 5.00m,
                SalePrice = 12.99m,
                StockQuantity = 200,
                IsActive = true
            }
        };

        context.Products.AddRange(products);
        context.SaveChanges();
    }
}
