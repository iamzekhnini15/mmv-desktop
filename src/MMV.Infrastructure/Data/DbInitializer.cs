using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Initialise la base de données avec des données de test complètes pour simulation.
/// </summary>
public static class DbInitializer
{
    private static Random _random = new Random(42); // Seed fixe pour reproductibilité

    public static void Initialize(OpticDbContext context)
    {
        // S'assurer que la BD est créée
        context.Database.EnsureCreated();

        // Si les clients existent déjà, ne pas ajouter le jeu de données
        if (context.Customers.Any())
        {
            return;
        }

        // 1. Créer les utilisateurs (Staff). Idempotent sur le nom d'utilisateur : si la base a déjà été
        //    migrée, un compte « admin » existe (InsertData de la migration InitialCreate). On n'ajoute
        //    alors que les utilisateurs de démonstration absents, pour éviter une collision sur l'index
        //    unique Username (P2A-1F).
        var existingUsernames = context.Users.Select(u => u.Username).ToHashSet();
        var users = CreateUsers().Where(u => !existingUsernames.Contains(u.Username)).ToList();
        if (users.Count > 0)
        {
            context.Users.AddRange(users);
            context.SaveChanges();
        }

        // Utilisateurs réellement présents (seedés ici + éventuel admin de migration) : sert aux références
        // FK (mouvements de stock, ventes). L'utilisateur « exécutant » par défaut est le premier (admin).
        var staffUsers = context.Users.OrderBy(u => u.UserId).ToList();
        var performingUser = staffUsers[0];

        // 2. Créer les catégories de produits
        var categories = CreateProductCategories();
        context.ProductCategories.AddRange(categories);
        context.SaveChanges();

        // 3. Créer les fournisseurs (25)
        var suppliers = CreateSuppliers();
        context.Suppliers.AddRange(suppliers);
        context.SaveChanges();

        // 4. Créer les clients (50)
        var customers = CreateCustomers();
        context.Customers.AddRange(customers);
        context.SaveChanges();

        // 5. Créer les suppléments pour verres
        var supplements = CreateSupplements();
        context.Supplements.AddRange(supplements);
        context.SaveChanges();

        // 6. Créer les produits (100)
        var products = CreateProducts(categories, suppliers);
        context.Products.AddRange(products);
        context.SaveChanges();

        // 7. Créer les détails spécifiques par type de produit
        CreateProductDetails(context, products, supplements);
        context.SaveChanges();

        // 8. Créer les ordonnances pour les clients
        var prescriptions = CreatePrescriptions(customers);
        context.Prescriptions.AddRange(prescriptions);
        context.SaveChanges();

        // 9. Créer les mouvements de stock initiaux
        var stockMovements = CreateInitialStockMovements(products, performingUser);
        context.StockMovements.AddRange(stockMovements);
        context.SaveChanges();

        // 10. Créer les commandes (Orders)
        var orders = CreateOrders(customers, staffUsers, products);
        context.Orders.AddRange(orders);
        context.SaveChanges();

        // 11. Créer les ventes (Sales)
        var sales = CreateSales(customers, staffUsers, products);
        context.Sales.AddRange(sales);
        context.SaveChanges();

        // 12. Créer les mouvements de stock pour ventes
        var salesStockMovements = CreateSalesStockMovements(sales, performingUser);
        context.StockMovements.AddRange(salesStockMovements);
        context.SaveChanges();

        // 13. Créer les notifications
        var notifications = CreateNotifications(products);
        context.Notifications.AddRange(notifications);
        context.SaveChanges();
    }

    #region User Creation

    private static List<User> CreateUsers()
    {
        // Hash BCrypt (work factor 11) pour le mot de passe "admin"
        const string adminHash = "$2a$11$QA85M79Q7bLajCAGRrVS1ONdstKLbV0S/vX6OKtN3CsCF3MWSNMoi";

        return new List<User>
        {
            new User
            {
                Username = "admin",
                PasswordHash = adminHash,
                FirstName = "Administrateur",
                LastName = "Système",
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-12),
                LastLogin = DateTime.UtcNow.AddHours(-2)
            },
            new User
            {
                Username = "marie.optic",
                PasswordHash = adminHash,
                FirstName = "Marie",
                LastName = "Durand",
                Role = UserRole.Optician,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-10),
                LastLogin = DateTime.UtcNow.AddHours(-1)
            },
            new User
            {
                Username = "pierre.tech",
                PasswordHash = adminHash,
                FirstName = "Pierre",
                LastName = "Moreau",
                Role = UserRole.Technician,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-8),
                LastLogin = DateTime.UtcNow.AddHours(-3)
            },
            new User
            {
                Username = "sophie.optic",
                PasswordHash = adminHash,
                FirstName = "Sophie",
                LastName = "Lambert",
                Role = UserRole.Optician,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-6),
                LastLogin = DateTime.UtcNow.AddDays(-1)
            }
        };
    }

    #endregion

    #region Category Creation

    private static List<ProductCategory> CreateProductCategories()
    {
        return new List<ProductCategory>
        {
            new ProductCategory { Name = "Montures", Description = "Montures optiques et solaires" },
            new ProductCategory { Name = "Verres", Description = "Verres correcteurs" },
            new ProductCategory { Name = "Lentilles", Description = "Lentilles de contact" },
            new ProductCategory { Name = "Accessoires", Description = "Accessoires optiques" },
            new ProductCategory { Name = "Clips", Description = "Clips solaires" },
            new ProductCategory { Name = "Plastiques", Description = "Produits en plastique et nettoyage" }
        };
    }

    #endregion

    #region Supplier Creation

    private static List<Supplier> CreateSuppliers()
    {
        var supplierNames = new[]
        {
            ("Vision Plus France", "VP001", "contact@visionplus.fr", "+33 1 23 45 67 89", "15 Rue de la Paix, 75002 Paris"),
            ("Optics International", "OI002", "info@optics-intl.com", "+33 2 34 56 78 90", "45 Avenue des Champs, 69000 Lyon"),
            ("Luxe Frames", "LF003", "sales@luxeframes.fr", "+33 3 45 67 89 01", "78 Boulevard Haussmann, 75008 Paris"),
            ("EuroLens Distribution", "EL004", "contact@eurolens.eu", "+33 4 56 78 90 12", "22 Rue Victor Hugo, 13000 Marseille"),
            ("Premium Optics", "PO005", "info@premiumoptics.fr", "+33 5 67 89 01 23", "10 Place Bellecour, 69002 Lyon"),
            ("TechVision Supplies", "TV006", "sales@techvision.com", "+33 1 78 90 12 34", "33 Rue de Rivoli, 75001 Paris"),
            ("Global Eyewear", "GE007", "contact@globaleyewear.com", "+33 4 89 01 23 45", "88 La Canebière, 13001 Marseille"),
            ("Crystal Lenses", "CL008", "info@crystallenses.fr", "+33 5 90 12 34 56", "55 Cours de l'Intendance, 33000 Bordeaux"),
            ("Modern Frames Co", "MF009", "sales@modernframes.fr", "+33 2 01 23 45 67", "12 Rue du Commerce, 44000 Nantes"),
            ("Essilor France", "ES010", "contact@essilor.fr", "+33 1 64 89 34 56", "147 Rue de Paris, 94220 Charenton"),
            ("Zeiss Vision Care", "ZV011", "info@zeiss.fr", "+33 1 49 73 40 00", "10 Avenue de l'Europe, 78400 Chatou"),
            ("Varilux Premium", "VX012", "sales@varilux.fr", "+33 3 20 45 67 89", "25 Rue Nationale, 59000 Lille"),
            ("Ray-Ban Europe", "RB013", "contact@rayban.eu", "+33 4 78 90 12 34", "66 Quai de la Loire, 69007 Lyon"),
            ("Oakley France", "OK014", "info@oakley.fr", "+33 1 40 12 34 56", "8 Rue du Faubourg, 75008 Paris"),
            ("Silhouette Int", "SI015", "sales@silhouette.at", "+43 732 3848 0", "Ellbognerstrasse 24, 4020 Linz"),
            ("Transitions Optical", "TR016", "contact@transitions.com", "+33 1 55 89 90 00", "30 Avenue Montaigne, 75008 Paris"),
            ("Hoya Lens France", "HY017", "info@hoya.fr", "+33 1 47 68 02 10", "5 Rue Bellini, 92800 Puteaux"),
            ("Rodenstock France", "RD018", "sales@rodenstock.fr", "+33 1 48 18 53 00", "20 Avenue de la Grande Armée, 75017 Paris"),
            ("CooperVision", "CV019", "contact@coopervision.fr", "+33 1 49 07 33 00", "65 Quai Georges Gorse, 92100 Boulogne"),
            ("Bausch & Lomb", "BL020", "info@bausch.fr", "+33 1 47 10 45 00", "17 Rue Spontini, 75116 Paris"),
            ("Johnson & Johnson Vision", "JJ021", "sales@jnjvision.fr", "+33 1 55 00 40 00", "1 Rue Camille Desmoulins, 92130 Issy"),
            ("Alcon Laboratories", "AL022", "contact@alcon.fr", "+33 1 47 10 48 00", "4 Rue Henri Sainte-Claire Deville, 92500 Rueil"),
            ("Ciba Vision France", "CB023", "info@cibavision.fr", "+33 1 58 04 34 00", "12 Avenue Maurice Thorez, 94200 Ivry"),
            ("Menicon France", "MN024", "sales@menicon.fr", "+33 1 46 04 05 06", "28 Rue de Châteaudun, 75009 Paris"),
            ("Acuvue France", "AC025", "contact@acuvue.fr", "+33 1 55 00 40 10", "1 Rue Camille Desmoulins, 92130 Issy")
        };

        return supplierNames.Select(s => new Supplier
        {
            Name = s.Item1,
            ReferenceCode = s.Item2,
            ContactEmail = s.Item3,
            Phone = s.Item4,
            Address = s.Item5
        }).ToList();
    }

    #endregion

    #region Customer Creation

    private static List<Customer> CreateCustomers()
    {
        var firstNames = new[] { "Jean", "Marie", "Pierre", "Sophie", "Luc", "Claire", "Marc", "Isabelle", "Paul", "Nathalie",
            "Jacques", "Catherine", "François", "Sylvie", "Philippe", "Martine", "Bernard", "Françoise", "Michel", "Nicole",
            "Alain", "Monique", "Christian", "Jacqueline", "Daniel", "Patricia", "André", "Christine", "René", "Annie",
            "Georges", "Dominique", "Robert", "Hélène", "Claude", "Brigitte", "Henri", "Chantal", "Louis", "Danielle",
            "Roger", "Michèle", "Guy", "Jeanne", "Charles", "Valérie", "Serge", "Sandrine", "Laurent", "Véronique" };

        var lastNames = new[] { "Martin", "Bernard", "Dubois", "Thomas", "Robert", "Richard", "Petit", "Durand", "Leroy", "Moreau",
            "Simon", "Laurent", "Lefebvre", "Michel", "Garcia", "David", "Bertrand", "Roux", "Vincent", "Fournier",
            "Morel", "Girard", "André", "Mercier", "Dupont", "Lambert", "Bonnet", "François", "Martinez", "Legrand",
            "Garnier", "Faure", "Rousseau", "Blanc", "Guerin", "Muller", "Henry", "Roussel", "Nicolas", "Perrin",
            "Morin", "Mathieu", "Clement", "Gauthier", "Dumont", "Lopez", "Fontaine", "Chevalier", "Robin", "Masson" };

        var cities = new[] { "Paris", "Lyon", "Marseille", "Toulouse", "Nice", "Nantes", "Strasbourg", "Montpellier", "Bordeaux", "Lille",
            "Rennes", "Reims", "Saint-Étienne", "Le Havre", "Toulon", "Grenoble", "Dijon", "Angers", "Nîmes", "Villeurbanne" };

        var insurances = new[] { "Mutuelle de France", "Allianz", "AXA", "MAAF", "Generali", "SwissLife", "Malakoff Humanis",
            "Harmonie Mutuelle", "MGEN", "MMA", "Groupe Apicil", "AG2R La Mondiale" };

        var notes = new[] { "Client régulier", "Préfère les RDV le matin", "Astigmatisme", "Myopie légère", "Presbytie progressive",
            "Client VIP", "Sensible à la lumière", "Sport régulier", "Travail sur écran", "", "", "" };

        var customers = new List<Customer>();
        for (int i = 0; i < 50; i++)
        {
            var birthYear = 1945 + _random.Next(0, 60);
            var birthMonth = _random.Next(1, 13);
            var birthDay = _random.Next(1, 28);

            customers.Add(new Customer
            {
                FirstName = firstNames[i % firstNames.Length],
                LastName = lastNames[i % lastNames.Length],
                Email = $"{firstNames[i % firstNames.Length].ToLower()}.{lastNames[i % lastNames.Length].ToLower()}@email.com",
                Phone = $"0{_random.Next(1, 10)}{_random.Next(10000000, 99999999)}",
                BirthDate = new DateTime(birthYear, birthMonth, birthDay),
                Address = $"{_random.Next(1, 200)} {new[] { "Rue", "Avenue", "Boulevard", "Place" }[_random.Next(4)]} {new[] { "de la Paix", "Victor Hugo", "des Champs", "Saint-Michel", "de Rivoli", "Nationale" }[_random.Next(6)]}",
                City = cities[_random.Next(cities.Length)],
                PostalCode = $"{_random.Next(10, 99):D2}{_random.Next(0, 1000):D3}",
                SocialSecurityNumber = $"{(birthYear % 100):D2}{birthMonth:D2}{birthDay:D2}{_random.Next(100000, 999999)}",
                InsuranceName = insurances[_random.Next(insurances.Length)],
                Notes = notes[_random.Next(notes.Length)],
                CreatedAt = DateTime.UtcNow.AddMonths(-_random.Next(1, 24))
            });
        }

        return customers;
    }

    #endregion

    #region Supplement Creation

    private static List<Supplement> CreateSupplements()
    {
        return new List<Supplement>
        {
            new Supplement { Name = "Anti-reflet", SupplementPrice = 50.00m },
            new Supplement { Name = "Anti-rayures", SupplementPrice = 30.00m },
            new Supplement { Name = "Anti-lumière bleue", SupplementPrice = 70.00m },
            new Supplement { Name = "Photochromique", SupplementPrice = 120.00m },
            new Supplement { Name = "Polarisant", SupplementPrice = 90.00m },
            new Supplement { Name = "Hydrophobe", SupplementPrice = 40.00m },
            new Supplement { Name = "Traitement UV", SupplementPrice = 35.00m },
            new Supplement { Name = "Miroir", SupplementPrice = 60.00m },
            new Supplement { Name = "Durcissement", SupplementPrice = 25.00m },
            new Supplement { Name = "Antistatique", SupplementPrice = 20.00m }
        };
    }

    #endregion

    #region Product Creation

    private static List<Product> CreateProducts(List<ProductCategory> categories, List<Supplier> suppliers)
    {
        var products = new List<Product>();

        // 30 Montures
        products.AddRange(CreateFrameProducts(categories[0], suppliers));

        // 25 Verres
        products.AddRange(CreateGlassProducts(categories[1], suppliers));

        // 20 Lentilles
        products.AddRange(CreateLensProducts(categories[2], suppliers));

        // 15 Accessoires
        products.AddRange(CreateAccessoryProducts(categories[3], suppliers));

        // 5 Clips
        products.AddRange(CreateClipProducts(categories[4], suppliers));

        // 5 Produits plastiques/nettoyage
        products.AddRange(CreatePlasticProducts(categories[5], suppliers));

        return products;
    }

    private static List<Product> CreateFrameProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var frames = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Aviateur Titanium", "Monture aviateur légère en titanium", 95.00m, 189.99m, 15),
            ("Rectangle Acétate", "Monture rectangulaire acétate noir", 75.00m, 149.99m, 22),
            ("Ronde Vintage", "Monture ronde style vintage doré", 85.00m, 169.99m, 18),
            ("Sport Flex", "Monture sport flexible avec grip", 105.00m, 209.99m, 12),
            ("Papillon Femme", "Monture papillon élégante", 90.00m, 179.99m, 20),
            ("Clubmaster Classic", "Monture clubmaster bi-matière", 110.00m, 219.99m, 14),
            ("Wayfarer Bold", "Monture wayfarer épaisse", 80.00m, 159.99m, 25),
            ("Cat-Eye Chic", "Monture cat-eye pour femme", 95.00m, 189.99m, 16),
            ("Pilote Métal", "Monture pilote métal argenté", 70.00m, 139.99m, 30),
            ("Géométrique Design", "Monture géométrique moderne", 115.00m, 229.99m, 10),
            ("Minimaliste Ultra-light", "Monture ultra-légère minimaliste", 125.00m, 249.99m, 8),
            ("Business Pro", "Monture professionnelle discrète", 88.00m, 175.99m, 19),
            ("Teen Fashion", "Monture colorée pour ados", 65.00m, 129.99m, 28),
            ("Senior Confort", "Monture confortable pour seniors", 72.00m, 143.99m, 24),
            ("Sport Aqua", "Monture sport nautique", 98.00m, 195.99m, 11),
            ("Luxe Premium", "Monture haut de gamme designer", 250.00m, 499.99m, 6),
            ("Eco Bambou", "Monture écologique en bambou", 105.00m, 209.99m, 13),
            ("Flex Memory", "Monture à mémoire de forme", 92.00m, 183.99m, 17),
            ("Kids Fun", "Monture enfant robuste", 55.00m, 109.99m, 32),
            ("Oval Classic", "Monture ovale classique", 68.00m, 135.99m, 26),
            ("Square Bold", "Monture carrée épaisse", 82.00m, 163.99m, 21),
            ("Demi-Cerclée", "Monture demi-cerclée légère", 77.00m, 153.99m, 23),
            ("Sans Monture", "Monture percée invisible", 135.00m, 269.99m, 9),
            ("Double Pont", "Monture à double pont métal", 89.00m, 177.99m, 15),
            ("Vintage Années 80", "Monture rétro années 80", 74.00m, 147.99m, 18),
            ("Hipster Wood", "Monture en bois hipster", 118.00m, 235.99m, 7),
            ("Transparent Trendy", "Monture transparente tendance", 71.00m, 141.99m, 20),
            ("Gradient Frame", "Monture avec dégradé de couleur", 86.00m, 171.99m, 16),
            ("Executive Elite", "Monture executive premium", 145.00m, 289.99m, 5),
            ("Urban Street", "Monture style urbain", 79.00m, 157.99m, 22)
        };

        return frames.Select((f, i) => new Product
        {
            Reference = $"MONT-{(i + 1):D3}",
            Name = f.name,
            Description = f.desc,
            PurchasePrice = f.purchase,
            SalePrice = f.sale,
            StockQuantity = f.stock,
            StockAlertThreshold = 5,
            Category = ProductCategoryEnum.MONTURE,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 18))
        }).ToList();
    }

    private static List<Product> CreateGlassProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var glasses = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Verre Unifocal Organique 1.5", "Verre simple focale indice 1.5", 45.00m, 89.99m, 80),
            ("Verre Unifocal Organique 1.6", "Verre simple focale indice 1.6", 65.00m, 129.99m, 75),
            ("Verre Unifocal Organique 1.67", "Verre simple focale indice 1.67", 85.00m, 169.99m, 60),
            ("Verre Unifocal Organique 1.74", "Verre simple focale indice 1.74", 125.00m, 249.99m, 40),
            ("Verre Unifocal Minéral", "Verre simple focale minéral", 55.00m, 109.99m, 50),
            ("Verre Bifocal Standard", "Verre double focale classique", 75.00m, 149.99m, 45),
            ("Verre Bifocal HD", "Verre double focale haute définition", 95.00m, 189.99m, 35),
            ("Verre Progressif Confort", "Verre progressif de base", 150.00m, 299.99m, 55),
            ("Verre Progressif Premium", "Verre progressif haute performance", 220.00m, 439.99m, 40),
            ("Verre Progressif Elite", "Verre progressif top gamme", 310.00m, 619.99m, 25),
            ("Verre Progressif Personnalisé", "Verre progressif sur mesure", 380.00m, 759.99m, 18),
            ("Verre Anti-Lumière Bleue", "Verre avec filtre lumière bleue", 95.00m, 189.99m, 62),
            ("Verre Photochromique", "Verre qui s'assombrit au soleil", 135.00m, 269.99m, 48),
            ("Verre Polarisant", "Verre polarisé anti-reflets", 115.00m, 229.99m, 52),
            ("Verre Solaire Teinté", "Verre teinté pour soleil", 68.00m, 135.99m, 70),
            ("Verre Solaire Dégradé", "Verre avec dégradé de teinte", 78.00m, 155.99m, 58),
            ("Verre Sport Incassable", "Verre polycarbonate sport", 88.00m, 175.99m, 44),
            ("Verre Conduite Nuit", "Verre optimisé conduite nocturne", 105.00m, 209.99m, 38),
            ("Verre Gaming", "Verre optimisé pour gaming", 98.00m, 195.99m, 42),
            ("Verre Ordinateur", "Verre anti-fatigue écran", 85.00m, 169.99m, 65),
            ("Verre Variable Digital", "Verre adapté au numérique", 125.00m, 249.99m, 36),
            ("Verre Aminci Ultra-Thin", "Verre ultra-aminci", 145.00m, 289.99m, 28),
            ("Verre Asphérique", "Verre asphérique mince", 110.00m, 219.99m, 50),
            ("Verre Prisme", "Verre avec correction prismatique", 165.00m, 329.99m, 22),
            ("Verre Haute Courbe", "Verre haute courbe sport", 135.00m, 269.99m, 30)
        };

        return glasses.Select((g, i) => new Product
        {
            Reference = $"VERR-{(i + 1):D3}",
            Name = g.name,
            Description = g.desc,
            PurchasePrice = g.purchase,
            SalePrice = g.sale,
            StockQuantity = g.stock,
            StockAlertThreshold = 10,
            Category = ProductCategoryEnum.VERRE,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 15))
        }).ToList();
    }

    private static List<Product> CreateLensProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var lenses = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Lentilles Journalières Confort", "Lentilles souples journalières confort", 18.00m, 35.99m, 150),
            ("Lentilles Journalières Premium", "Lentilles journalières haut de gamme", 25.00m, 49.99m, 130),
            ("Lentilles Mensuelles Standard", "Lentilles mensuelles classiques", 20.00m, 39.99m, 140),
            ("Lentilles Mensuelles Silicone", "Lentilles mensuelles silicone-hydrogel", 28.00m, 55.99m, 120),
            ("Lentilles Mensuelles Torique", "Lentilles mensuelles pour astigmatisme", 32.00m, 63.99m, 95),
            ("Lentilles Mensuelles Multifocales", "Lentilles mensuelles progressives", 38.00m, 75.99m, 85),
            ("Lentilles Bimensuelles", "Lentilles souples bimensuelles", 22.00m, 43.99m, 110),
            ("Lentilles Trimestrielles", "Lentilles longue durée 3 mois", 35.00m, 69.99m, 70),
            ("Lentilles Annuelles Rigides", "Lentilles rigides annuelles", 125.00m, 249.99m, 40),
            ("Lentilles Colorées Naturel", "Lentilles colorées tons naturels", 28.00m, 55.99m, 88),
            ("Lentilles Colorées Fantasy", "Lentilles colorées tons vifs", 30.00m, 59.99m, 75),
            ("Lentilles UV Protection", "Lentilles avec protection UV", 32.00m, 63.99m, 92),
            ("Lentilles Nuit & Jour", "Lentilles port prolongé 30 jours", 45.00m, 89.99m, 65),
            ("Lentilles Sport Active", "Lentilles pour sportifs", 35.00m, 69.99m, 78),
            ("Lentilles Sensibles", "Lentilles pour yeux sensibles", 36.00m, 71.99m, 82),
            ("Lentilles Hydratantes", "Lentilles haute hydratation", 33.00m, 65.99m, 90),
            ("Lentilles Oxygène Plus", "Lentilles haute perméabilité", 38.00m, 75.99m, 73),
            ("Lentilles Minces HD", "Lentilles ultra-minces HD", 42.00m, 83.99m, 68),
            ("Lentilles Anti-Dépôts", "Lentilles résistantes dépôts protéiques", 34.00m, 67.99m, 86),
            ("Lentilles Ado/Teen", "Lentilles adaptées adolescents", 24.00m, 47.99m, 105)
        };

        return lenses.Select((l, i) => new Product
        {
            Reference = $"LENT-{(i + 1):D3}",
            Name = l.name,
            Description = l.desc,
            PurchasePrice = l.purchase,
            SalePrice = l.sale,
            StockQuantity = l.stock,
            StockAlertThreshold = 20,
            Category = ProductCategoryEnum.LENTILLE,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 12))
        }).ToList();
    }

    private static List<Product> CreateAccessoryProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var accessories = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Étui Rigide Cuir", "Étui rigide en cuir véritable", 15.00m, 29.99m, 65),
            ("Étui Souple Tissu", "Étui souple en tissu microfibre", 8.00m, 15.99m, 95),
            ("Étui Sport Waterproof", "Étui étanche pour sport", 18.00m, 35.99m, 45),
            ("Chaînette Lunettes Or", "Chaînette pour lunettes dorée", 12.00m, 23.99m, 55),
            ("Chaînette Lunettes Argent", "Chaînette pour lunettes argentée", 12.00m, 23.99m, 52),
            ("Cordon Sport", "Cordon sportif élastique", 6.00m, 11.99m, 85),
            ("Solution Nettoyante 100ml", "Solution nettoyante multifonction", 5.00m, 9.99m, 150),
            ("Solution Nettoyante 250ml", "Solution nettoyante grand format", 10.00m, 19.99m, 120),
            ("Spray Nettoyant", "Spray nettoyant anti-buée", 8.00m, 15.99m, 100),
            ("Lingettes Nettoyantes x30", "Lingettes jetables préhumidifiées", 7.00m, 13.99m, 110),
            ("Chiffon Microfibre Premium", "Chiffon microfibre haute qualité", 4.00m, 7.99m, 180),
            ("Kit Entretien Complet", "Kit complet entretien lunettes", 22.00m, 43.99m, 58),
            ("Tournevis Multifonction", "Kit tournevis micro pour lunettes", 9.00m, 17.99m, 72),
            ("Plaquettes Nasales x5", "Lot de plaquettes de remplacement", 5.00m, 9.99m, 140),
            ("Branches de Rechange", "Branches universelles ajustables", 15.00m, 29.99m, 48)
        };

        return accessories.Select((a, i) => new Product
        {
            Reference = $"ACCE-{(i + 1):D3}",
            Name = a.name,
            Description = a.desc,
            PurchasePrice = a.purchase,
            SalePrice = a.sale,
            StockQuantity = a.stock,
            StockAlertThreshold = 15,
            Category = ProductCategoryEnum.PLASTIC,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 10))
        }).ToList();
    }

    private static List<Product> CreateClipProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var clips = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Clip Solaire Magnétique", "Clip solaire à fixation magnétique", 28.00m, 55.99m, 42),
            ("Clip Solaire Universel", "Clip solaire universel à pince", 22.00m, 43.99m, 55),
            ("Clip Polarisant Sport", "Clip polarisant pour activités sportives", 35.00m, 69.99m, 38),
            ("Clip Relevable", "Clip solaire relevable pratique", 32.00m, 63.99m, 45),
            ("Clip Vision Nocturne", "Clip anti-éblouissement nuit", 25.00m, 49.99m, 50)
        };

        return clips.Select((c, i) => new Product
        {
            Reference = $"CLIP-{(i + 1):D3}",
            Name = c.name,
            Description = c.desc,
            PurchasePrice = c.purchase,
            SalePrice = c.sale,
            StockQuantity = c.stock,
            StockAlertThreshold = 10,
            Category = ProductCategoryEnum.CLIPS,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 8))
        }).ToList();
    }

    private static List<Product> CreatePlasticProducts(ProductCategory category, List<Supplier> suppliers)
    {
        var plastics = new List<(string name, string desc, decimal purchase, decimal sale, int stock)>
        {
            ("Embouts Branche Silicone", "Embouts de branche en silicone confort", 3.00m, 5.99m, 200),
            ("Protège-Plaquettes", "Protège-plaquettes transparents", 2.50m, 4.99m, 250),
            ("Silicone Adjust", "Adaptateurs silicone ajustables", 4.00m, 7.99m, 180),
            ("Anti-Dérapant Sport", "Patchs anti-dérapants sport", 5.00m, 9.99m, 160),
            ("Strap Néoprène Kids", "Strap néoprène pour enfants", 8.00m, 15.99m, 95)
        };

        return plastics.Select((p, i) => new Product
        {
            Reference = $"PLAS-{(i + 1):D3}",
            Name = p.name,
            Description = p.desc,
            PurchasePrice = p.purchase,
            SalePrice = p.sale,
            StockQuantity = p.stock,
            StockAlertThreshold = 30,
            Category = ProductCategoryEnum.PLASTIC,
            CategoryId = category.CategoryId,
            SupplierId = suppliers[_random.Next(suppliers.Count)].SupplierId,
            IsActive = true,
            EntryDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 6))
        }).ToList();
    }

    #endregion

    #region Product Details Creation

    private static void CreateProductDetails(OpticDbContext context, List<Product> products, List<Supplement> supplements)
    {
        // Détails pour les montures (AccessoryDetail)
        var frames = products.Where(p => p.Category == ProductCategoryEnum.MONTURE).ToList();
        var colors = new[] { "Noir", "Marron", "Bleu", "Or", "Argent", "Rouge", "Vert", "Transparent", "Écaille", "Gris" };
        var materials = new[] { "Acétate", "Métal", "Titanium", "Plastique", "Bois", "Carbone", "Aluminium" };

        foreach (var frame in frames)
        {
            context.AccessoryDetails.Add(new AccessoryDetail
            {
                ProductId = frame.ProductId,
                Color = colors[_random.Next(colors.Length)],
                Size = $"{_random.Next(48, 58)}-{_random.Next(16, 22)}",
                Material = materials[_random.Next(materials.Length)]
            });
        }

        // Détails pour les verres (GlassDetail)
        var glasses = products.Where(p => p.Category == ProductCategoryEnum.VERRE).ToList();
        var glassTypes = new[] { GlassType.SF, GlassType.DF, GlassType.MF };
        var glassMaterials = new[] { GlassMaterial.ORGANIQUE, GlassMaterial.MINERAL, GlassMaterial.COMPOSITE };
        var indices = new[] { 1.5m, 1.6m, 1.67m, 1.74m };
        var diameters = new[] { "65/70", "70/75", "75/80" };

        foreach (var glass in glasses)
        {
            var glassDetail = new GlassDetail
            {
                ProductId = glass.ProductId,
                Material = glassMaterials[_random.Next(glassMaterials.Length)],
                GlassType = glassTypes[_random.Next(glassTypes.Length)],
                Diameter = diameters[_random.Next(diameters.Length)],
                Index = indices[_random.Next(indices.Length)],
                PowerLimitMin = -8.00m,
                PowerLimitMax = 8.00m
            };
            context.GlassDetails.Add(glassDetail);

            // Ajouter quelques suppléments disponibles pour chaque verre
            var availableSupplements = supplements.OrderBy(x => _random.Next()).Take(_random.Next(3, 7)).ToList();
            foreach (var supplement in availableSupplements)
            {
                context.GlassSupplements.Add(new GlassSupplement
                {
                    GlassId = glass.ProductId,
                    SupplementId = supplement.SupplementId
                });
            }

            // Ajouter une grille tarifaire simple
            context.GlassPricingTiers.AddRange(new[]
            {
                new GlassPricingTier
                {
                    GlassId = glass.ProductId,
                    PowerMin = 0.00m,
                    PowerMax = 2.00m,
                    PurchasePriceGrid = glass.PurchasePrice,
                    SalePriceGrid = glass.SalePrice
                },
                new GlassPricingTier
                {
                    GlassId = glass.ProductId,
                    PowerMin = 2.00m,
                    PowerMax = 4.00m,
                    PurchasePriceGrid = glass.PurchasePrice + 20m,
                    SalePriceGrid = glass.SalePrice + 40m
                },
                new GlassPricingTier
                {
                    GlassId = glass.ProductId,
                    PowerMin = 4.00m,
                    PowerMax = 8.00m,
                    PurchasePriceGrid = glass.PurchasePrice + 40m,
                    SalePriceGrid = glass.SalePrice + 80m
                }
            });
        }

        // Détails pour les lentilles (LensDetail)
        var lenses = products.Where(p => p.Category == ProductCategoryEnum.LENTILLE).ToList();
        var lensBrands = new[] { "Acuvue", "Bausch+Lomb", "CooperVision", "Alcon", "Menicon" };
        var lensModels = new[] { "Comfort", "Premium", "HD", "Ultra", "Active", "Hydra", "Vision" };
        var lensMaterials = new[] { LensMaterial.SOUPLE, LensMaterial.DURE };
        var lensTypes = new[] { LensType.UNIFOCAL, LensType.MULTIFOCAL, LensType.TORIQUE };
        var lensDurations = new[] { LensDuration.JOURNALIERE, LensDuration.MENSUELLE, LensDuration.TRIMESTRIELLE, LensDuration.ANNUELLE };

        foreach (var lens in lenses)
        {
            context.LensDetails.Add(new LensDetail
            {
                ProductId = lens.ProductId,
                Brand = lensBrands[_random.Next(lensBrands.Length)],
                Model = lensModels[_random.Next(lensModels.Length)],
                Material = lensMaterials[_random.Next(lensMaterials.Length)],
                LensType = lensTypes[_random.Next(lensTypes.Length)],
                Diameter = 14.0m + (_random.Next(0, 5) * 0.2m),
                BaseCurve = 8.4m + (_random.Next(0, 4) * 0.2m),
                IsColored = lens.Name.Contains("Colorée"),
                Duration = lensDurations[_random.Next(lensDurations.Length)]
            });
        }

        // Détails pour les accessoires
        var accessories = products.Where(p => p.Category == ProductCategoryEnum.PLASTIC && p.Reference.StartsWith("ACCE")).ToList();
        foreach (var accessory in accessories)
        {
            context.AccessoryDetails.Add(new AccessoryDetail
            {
                ProductId = accessory.ProductId,
                Color = colors[_random.Next(colors.Length)],
                Size = "Standard",
                Material = accessory.Name.Contains("Cuir") ? "Cuir" :
                          accessory.Name.Contains("Tissu") ? "Tissu" :
                          accessory.Name.Contains("Microfibre") ? "Microfibre" : "Plastique"
            });
        }

        // Détails pour les clips
        var clips = products.Where(p => p.Category == ProductCategoryEnum.CLIPS).ToList();
        foreach (var clip in clips)
        {
            context.AccessoryDetails.Add(new AccessoryDetail
            {
                ProductId = clip.ProductId,
                Color = colors[_random.Next(colors.Length)],
                Size = "Universel",
                Material = "Plastique polarisant"
            });
        }

        // Détails pour les produits plastiques
        var plastics = products.Where(p => p.Category == ProductCategoryEnum.PLASTIC && p.Reference.StartsWith("PLAS")).ToList();
        foreach (var plastic in plastics)
        {
            context.AccessoryDetails.Add(new AccessoryDetail
            {
                ProductId = plastic.ProductId,
                Color = "Transparent",
                Size = "Standard",
                Material = "Silicone"
            });
        }
    }

    #endregion

    #region Prescription Creation

    private static List<Prescription> CreatePrescriptions(List<Customer> customers)
    {
        var prescriptions = new List<Prescription>();
        var doctors = new[] { "Dr. Martin", "Dr. Dubois", "Dr. Lefebvre", "Dr. Garcia", "Dr. Moreau", "Dr. Bernard" };
        var prismBases = new[] { PrismBase.In, PrismBase.Out, PrismBase.Up, PrismBase.Down };

        // Créer des ordonnances pour 35 clients (70% des clients)
        var customersWithPrescriptions = customers.OrderBy(x => _random.Next()).Take(35).ToList();

        foreach (var customer in customersWithPrescriptions)
        {
            // Certains clients ont plusieurs ordonnances (historique)
            int prescriptionCount = _random.Next(1, 4);

            for (int i = 0; i < prescriptionCount; i++)
            {
                var hasPrism = _random.Next(100) < 10; // 10% ont un prisme

                prescriptions.Add(new Prescription
                {
                    CustomerId = customer.CustomerId,
                    IssueDate = DateTime.UtcNow.AddMonths(-_random.Next(1, 36)).AddDays(-_random.Next(0, 30)),
                    DoctorName = doctors[_random.Next(doctors.Length)],

                    // Œil droit
                    OdSphere = RandomSphere(),
                    OdCylinder = _random.Next(100) < 40 ? RandomCylinder() : null,
                    OdAxis = _random.Next(100) < 40 ? _random.Next(0, 181) : null,
                    OdAddition = customer.BirthDate?.Year < 1975 ? RandomAddition() : null,
                    OdPrismValue = hasPrism ? Math.Round(_random.NextDouble() * 3 + 0.5, 2) : null,
                    OdPrismBase = hasPrism ? prismBases[_random.Next(prismBases.Length)] : null,
                    OdVisualAcuity = $"{_random.Next(5, 11)}/10",

                    // Œil gauche
                    OgSphere = RandomSphere(),
                    OgCylinder = _random.Next(100) < 40 ? RandomCylinder() : null,
                    OgAxis = _random.Next(100) < 40 ? _random.Next(0, 181) : null,
                    OgAddition = customer.BirthDate?.Year < 1975 ? RandomAddition() : null,
                    OgPrismValue = hasPrism ? Math.Round(_random.NextDouble() * 3 + 0.5, 2) : null,
                    OgPrismBase = hasPrism ? prismBases[_random.Next(prismBases.Length)] : null,
                    OgVisualAcuity = $"{_random.Next(5, 11)}/10",

                    Notes = i == 0 ? "" : "Ancienne ordonnance",
                    CreatedAt = DateTime.UtcNow.AddMonths(-_random.Next(1, 36))
                });
            }
        }

        return prescriptions;
    }

    private static double RandomSphere()
    {
        // Génère une sphère entre -8.00 et +6.00
        return Math.Round((_random.NextDouble() * 14 - 8) * 4) / 4; // Arrondis au quart
    }

    private static double RandomCylinder()
    {
        // Génère un cylindre entre -4.00 et 0.00
        return Math.Round(-_random.NextDouble() * 4 * 4) / 4;
    }

    private static double RandomAddition()
    {
        // Génère une addition entre +0.75 et +3.50
        return Math.Round((_random.NextDouble() * 2.75 + 0.75) * 4) / 4;
    }

    #endregion

    #region Stock Movement Creation

    private static List<StockMovement> CreateInitialStockMovements(List<Product> products, User user)
    {
        var movements = new List<StockMovement>();

        foreach (var product in products)
        {
            movements.Add(new StockMovement
            {
                ProductId = product.ProductId,
                MovementType = StockMovementType.In,
                Quantity = product.StockQuantity,
                Reason = "Stock initial",
                CreatedAt = product.EntryDate,
                PerformedByUserId = user.UserId
            });
        }

        return movements;
    }

    #endregion

    #region Order Creation

    private static List<Order> CreateOrders(List<Customer> customers, List<User> users, List<Product> products)
    {
        // TODO: Mettre à jour les seeds après la migration Sales/Orders
        // Orders sont maintenant liés à Sales (commandes fournisseurs uniquement)
        return new List<Order>();
    }

    #endregion

    #region Sale Creation

    private static List<Sale> CreateSales(List<Customer> customers, List<User> users, List<Product> products)
    {
        var sales = new List<Sale>();
        var paymentMethods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Check, PaymentMethod.Transfer };
        var paymentStatuses = new[] { PaymentStatus.Paid, PaymentStatus.Partial, PaymentStatus.Pending };

        // Créer 60 ventes
        for (int i = 0; i < 60; i++)
        {
            var hasCustomer = _random.Next(100) < 80; // 80% des ventes ont un client identifié
            var customer = hasCustomer ? customers[_random.Next(customers.Count)] : null;
            var staff = users.Where(u => u.Role != UserRole.Technician).OrderBy(x => _random.Next()).FirstOrDefault() ?? users[0];
            var saleDate = DateTime.UtcNow.AddDays(-_random.Next(1, 180));

            var sale = new Sale
            {
                SaleNumber = $"VTE-{DateTime.UtcNow.Year}-{(i + 1):D4}",
                CustomerId = customer?.CustomerId,
                StaffId = staff.UserId,
                SaleDate = saleDate,
                PaymentMethod = paymentMethods[_random.Next(paymentMethods.Length)],
                PaymentStatus = paymentStatuses[_random.Next(paymentStatuses.Length)],
                Notes = ""
            };

            var saleItems = new List<SaleItem>();

            // Nombre d'articles dans la vente (1 à 5)
            int itemCount = _random.Next(1, 6);

            for (int j = 0; j < itemCount; j++)
            {
                var product = products[_random.Next(products.Count)];
                int quantity = _random.Next(1, 4);

                // Mettre à jour le stock
                product.StockQuantity -= quantity;

                saleItems.Add(new SaleItem
                {
                    Sale = sale,
                    ProductId = product.ProductId,
                    Quantity = quantity,
                    UnitPrice = product.SalePrice,
                    TotalPrice = product.SalePrice * quantity
                });
            }

            sale.SaleItems = saleItems;
            sale.TotalAmount = saleItems.Sum(si => si.TotalPrice);

            // Remise aléatoire (20% des ventes)
            if (_random.Next(100) < 20)
            {
                sale.DiscountAmount = Math.Round(sale.TotalAmount * (_random.Next(5, 21) / 100m), 2);
            }
            else
            {
                sale.DiscountAmount = 0;
            }

            sale.FinalAmount = sale.TotalAmount - sale.DiscountAmount;

            sales.Add(sale);
        }

        return sales;
    }

    #endregion

    #region Sales Stock Movements

    private static List<StockMovement> CreateSalesStockMovements(List<Sale> sales, User user)
    {
        var movements = new List<StockMovement>();

        foreach (var sale in sales)
        {
            foreach (var saleItem in sale.SaleItems)
            {
                if (saleItem.ProductId.HasValue)
                {
                    movements.Add(new StockMovement
                    {
                        ProductId = saleItem.ProductId.Value,
                        MovementType = StockMovementType.Out,
                        Quantity = saleItem.Quantity,
                        Reason = $"Vente {sale.SaleNumber}",
                        CreatedAt = sale.SaleDate,
                        PerformedByUserId = user.UserId
                    });
                }
            }
        }

        return movements;
    }

    #endregion

    #region Notification Creation

    private static List<Notification> CreateNotifications(List<Product> products)
    {
        var notifications = new List<Notification>();

        // Notifications de stock bas
        var lowStockProducts = products.Where(p => p.StockQuantity <= p.StockAlertThreshold).ToList();
        foreach (var product in lowStockProducts)
        {
            notifications.Add(new Notification
            {
                Type = "LowStock",
                Title = "Stock faible",
                Message = $"Le produit '{product.Name}' a un stock faible ({product.StockQuantity} unités)",
                EntityId = product.ProductId,
                EntityType = "Product",
                IsRead = _random.Next(100) < 30,
                CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(1, 7))
            });
        }

        // Notifications de rupture de stock
        var outOfStockProducts = products.Where(p => p.StockQuantity == 0).ToList();
        foreach (var product in outOfStockProducts)
        {
            notifications.Add(new Notification
            {
                Type = "StockOut",
                Title = "Rupture de stock",
                Message = $"Le produit '{product.Name}' est en rupture de stock",
                EntityId = product.ProductId,
                EntityType = "Product",
                IsRead = _random.Next(100) < 20,
                CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(1, 5))
            });
        }

        // Quelques notifications générales
        notifications.Add(new Notification
        {
            Type = "Info",
            Title = "Nouveau catalogue",
            Message = "Le nouveau catalogue de montures est disponible",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        notifications.Add(new Notification
        {
            Type = "Info",
            Title = "Formation",
            Message = "Formation sur les nouveaux verres progressifs le 15/02",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        return notifications;
    }

    #endregion
}
