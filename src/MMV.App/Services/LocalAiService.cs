using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;

namespace MMV.App.Services;

/// <summary>
/// Service de communication avec Ollama (LLM local) pour la génération de requêtes SQL
/// à partir de questions en langage naturel (Text-to-SQL).
/// </summary>
public class LocalAiService
{
    private readonly HttpClient _httpClient;
    private const string OllamaEndpoint = "http://localhost:11434/api/generate";
    private const string DefaultModel = "llama3.2"; // Modèles recommandés: llama3.2, qwen2.5-coder:7b, deepseek-coder-v2

    public LocalAiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
    }

    public LocalAiService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Génère une requête SQL SELECT à partir d'une question en langage naturel.
    /// </summary>
    /// <param name="userQuestion">La question de l'utilisateur.</param>
    /// <param name="model">Le modèle Ollama à utiliser (par défaut "sqlcoder").</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    /// <returns>La requête SQL générée.</returns>
    public async Task<string> GenerateSqlQueryAsync(
        string userQuestion,
        string model = DefaultModel,
        CancellationToken cancellationToken = default)
    {
        var schema = BuildDatabaseSchema();
        var systemPrompt = BuildSystemPrompt(schema);

        var requestBody = new OllamaRequest
        {
            Model = model,
            Prompt = $"{systemPrompt}\n\nQuestion: {userQuestion}\n\nSQL:",
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.2,
                NumPredict = 200,
                TopP = 0.9
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(OllamaEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var rawResponse = ollamaResponse?.Response?.Trim() ?? string.Empty;

        // Extraire uniquement la requête SQL de la réponse
        return ExtractSqlFromResponse(rawResponse);
    }

    /// <summary>
    /// Génère une réponse textuelle en français à partir des résultats SQL avec streaming.
    /// </summary>
    /// <param name="userQuestion">Question de l'utilisateur.</param>
    /// <param name="sqlQuery">Requête SQL exécutée.</param>
    /// <param name="resultData">Données retournées (JSON).</param>
    /// <param name="onTokenReceived">Callback appelé pour chaque token reçu.</param>
    /// <param name="model">Modèle Ollama.</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    public async Task StreamTextResponseAsync(
        string userQuestion,
        string sqlQuery,
        string resultData,
        Action<string> onTokenReceived,
        string model = DefaultModel,
        CancellationToken cancellationToken = default)
    {
        var prompt = "Tu es un assistant intelligent pour un logiciel d'opticien.\n\n" +
            $"Question de l'utilisateur : {userQuestion}\n\n" +
            $"Requête SQL exécutée :\n{sqlQuery}\n\n" +
            $"Résultats :\n{resultData}\n\n" +
            "Ta tâche : Génère une réponse claire et naturelle en français qui explique les résultats.\n\n" +
            "📊 Si c'est une ANALYSE TEMPORELLE (groupes par date/mois) :\n" +
            "- Explique la tendance (croissance, stabilité, baisse)\n" +
            "- Mentionne les périodes clés\n" +
            "- Ex: \"📈 Vos inscriptions client sont en croissance ! Vous avez enregistré X clients en janvier 2026, Y en février...\"\n\n" +
            "💰 Si c'est une AGRÉGATION (somme, moyenne) :\n" +
            "- Donne le chiffre principal en gras\n" +
            "- Ajoute du contexte si pertinent\n" +
            "- Ex: \"💰 Votre chiffre d'affaires total est de 45 230€, avec une moyenne de 890€ par vente.\"\n\n" +
            "📋 Si c'est une LISTE ou TOP :\n" +
            "- Résume les 3-5 premiers éléments\n" +
            "- Ex: \"🏆 Vos produits stars sont : Lunettes Ray-Ban (145 ventes), Verres progressifs (98 ventes)...\"\n\n" +
            "⚠️ Si c'est un STOCK ou ALERTE :\n" +
            "- Sois direct et actionnable\n" +
            "- Ex: \"⚠️ 8 produits nécessitent un réapprovisionnement urgent.\"\n\n" +
            "✅ Règles :\n" +
            "- Maximum 4 phrases\n" +
            "- Utilise des emojis appropriés (📊📈💰✅⚠️🏆)\n" +
            "- Ton conversationnel et naturel\n" +
            "- NE répète PAS la question\n\n" +
            "Réponse :";

        var requestBody = new OllamaRequest
        {
            Model = model,
            Prompt = prompt,
            Stream = true, // ACTIVER LE STREAMING
            Options = new OllamaOptions
            {
                Temperature = 0.7,
                NumPredict = 150,
                TopP = 0.9
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        // Utiliser HttpRequestMessage pour le streaming
        var request = new HttpRequestMessage(HttpMethod.Post, OllamaEndpoint)
        {
            Content = content
        };
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Lire le stream ligne par ligne (NDJSON)
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var chunk = JsonSerializer.Deserialize<OllamaStreamResponse>(line, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (!string.IsNullOrEmpty(chunk?.Response))
                {
                    onTokenReceived(chunk.Response);
                }

                if (chunk?.Done == true)
                    break;
            }
            catch
            {
                // Ignorer les lignes mal formatées
            }
        }
    }

    /// <summary>
    /// Construit un schéma SIMPLIFIÉ (seulement tables principales et colonnes clés).
    /// </summary>
    private static string BuildDatabaseSchema()
    {
        // Schéma SQLite ultra-compact (seulement l'essentiel)
        return @"Customers (CustomerId, FirstName, LastName, Phone, Email, BirthDate, City, CreatedAt)
Products (ProductId, Reference, Name, Category, SupplierId, PurchasePrice, SalePrice, StockQuantity, StockAlertThreshold)
Suppliers (SupplierId, Name, ContactEmail, Phone)
ProductCategories (CategoryId, Name)
Prescriptions (PrescriptionId, CustomerId, IssueDate, DoctorName, OdSphere, OgSphere)
Orders (OrderId, OrderNumber, CustomerId, StaffId, OrderDate, EstimatedDelivery, TotalAmount, Status)
OrderItems (OrderItemId, OrderId, ProductId, Quantity, UnitPrice)
Sales (SaleId, SaleNumber, CustomerId, StaffId, SaleDate, TotalAmount, FinalAmount, PaymentMethod, PaymentStatus)
SaleItems (SaleItemId, SaleId, ProductId, Quantity, UnitPrice, TotalPrice)
Users (UserId, Username, FirstName, LastName, Role)
StockMovements (MovementId, ProductId, MovementType, Quantity, Reason, CreatedAt)
Notifications (NotificationId, Type, Title, Message, IsRead, CreatedAt)";
    }

    /// <summary>
    /// Construit le prompt système pour le LLM.
    /// </summary>
    private static string BuildSystemPrompt(string schema)
    {
        return $@"You are an intelligent SQLite assistant for an optician management system. You can handle SIMPLE queries and COMPLEX analytics.

SCHEMA:
{schema}

⚠️ CRITICAL DATE DISTINCTION:
- Customers.CreatedAt = Date d'INSCRIPTION du client (date d'ajout dans le système)
- Customers.BirthDate = Date de NAISSANCE du client (anniversaire)
- Sales.SaleDate = Date de la vente
- Orders.OrderDate = Date de la commande

📈 ADVANCED ANALYTICS CAPABILITIES:
- Temporal grouping: strftime('%Y-%m', dateColumn) for monthly, strftime('%Y', dateColumn) for yearly
- Aggregations: COUNT, SUM, AVG, MIN, MAX
- Multiple JOINs when needed for complex analysis
- Subqueries for advanced filtering

✅ CORRECT EXAMPLES:

Q: Combien de clients?
SQL: SELECT COUNT(*) as Total FROM Customers;

Q: Graphique des inscriptions clients dans le temps (par mois)
SQL: SELECT strftime('%Y-%m', CreatedAt) as Mois, COUNT(*) as Nouveaux FROM Customers GROUP BY Mois ORDER BY Mois LIMIT 100;

Q: Évolution du chiffre d'affaires mensuel
SQL: SELECT strftime('%Y-%m', SaleDate) as Mois, SUM(FinalAmount) as CA FROM Sales GROUP BY Mois ORDER BY Mois LIMIT 100;

Q: Top 10 produits vendus avec quantités
SQL: SELECT p.Name, SUM(si.Quantity) as Quantité FROM Products p JOIN SaleItems si ON p.ProductId=si.ProductId GROUP BY p.Name ORDER BY Quantité DESC LIMIT 10;

Q: Clients inscrits cette année
SQL: SELECT FirstName, LastName, CreatedAt FROM Customers WHERE strftime('%Y', CreatedAt)='2026' LIMIT 100;

Q: Répartition des clients par ville
SQL: SELECT City, COUNT(*) as Nombre FROM Customers WHERE City IS NOT NULL GROUP BY City ORDER BY Nombre DESC LIMIT 100;

Q: Produits jamais vendus
SQL: SELECT p.Name, p.Reference FROM Products p LEFT JOIN SaleItems si ON p.ProductId=si.ProductId WHERE si.SaleItemId IS NULL LIMIT 100;

Q: Clients les plus dépensiers (top 5)
SQL: SELECT c.FirstName || ' ' || c.LastName as Client, SUM(s.FinalAmount) as Total FROM Customers c JOIN Sales s ON c.CustomerId=s.CustomerId GROUP BY c.CustomerId ORDER BY Total DESC LIMIT 5;

Q: Ventes par méthode de paiement
SQL: SELECT PaymentMethod, COUNT(*) as Nombre, SUM(FinalAmount) as Total FROM Sales GROUP BY PaymentMethod LIMIT 100;

Q: Analyse du stock (bas vs normal vs sur-stock)
SQL: SELECT CASE WHEN StockQuantity <= StockAlertThreshold THEN 'Bas' WHEN StockQuantity > StockAlertThreshold * 3 THEN 'Sur-stock' ELSE 'Normal' END as Statut, COUNT(*) as Nombre FROM Products GROUP BY Statut;

❌ WRONG EXAMPLES:
Q: Graphique inscriptions clients
SQL: SELECT strftime('%Y', BirthDate) as Année, COUNT(*) FROM Customers...  ← WRONG! BirthDate is birth year, use CreatedAt!

Q: Combien de clients?
SQL: SELECT COUNT(*) FROM Customers c JOIN Orders o ON c.CustomerId=o.CustomerId  ← Too complex for simple count!

🎯 RULES:
1. Use CreatedAt for registration/inscription dates, NOT BirthDate
2. Use strftime for date grouping (SQLite doesn't have YEAR/MONTH functions)
3. Always add LIMIT (100 for lists, no limit for aggregations)
4. JOIN only when truly needed
5. For graphs: return time periods + metric columns
6. Use French aliases

Output ONLY the SQL query, nothing else.";
    }

    /// <summary>
    /// Extrait la requête SQL pure de la réponse du LLM.
    /// </summary>
    private static string ExtractSqlFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        // Supprimer les blocs de code markdown
        response = response.Replace("```sql", "").Replace("```", "").Trim();

        // Chercher la première instruction SELECT
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sqlLines = new List<string>();
        var foundSelect = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!foundSelect && trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                foundSelect = true;
            }

            if (foundSelect)
            {
                sqlLines.Add(trimmed);
                // Arrêter au point-virgule
                if (trimmed.EndsWith(";"))
                    break;
            }
        }

        var sql = string.Join("\n", sqlLines).TrimEnd(';').Trim();
        return string.IsNullOrEmpty(sql) ? response.Trim() : sql;
    }

    /// <summary>
    /// Récupère tous les types d'entités du domaine.
    /// </summary>
    private static List<Type> GetDomainEntityTypes()
    {
        var assembly = typeof(Customer).Assembly;
        return assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "MMV.Domain.Entities")
            .OrderBy(t => t.Name)
            .ToList();
    }

    /// <summary>
    /// Détermine le nom de la table (convention EF Core : pluriel du nom de la classe).
    /// </summary>
    private static string GetTableName(Type entityType)
    {
        var name = entityType.Name;
        // Correspondance avec les DbSet de OpticDbContext
        var tableMap = new Dictionary<string, string>
        {
            ["User"] = "Users",
            ["Supplier"] = "Suppliers",
            ["ProductCategory"] = "ProductCategories",
            ["Product"] = "Products",
            ["GlassDetail"] = "GlassDetails",
            ["LensDetail"] = "LensDetails",
            ["AccessoryDetail"] = "AccessoryDetails",
            ["Supplement"] = "Supplements",
            ["GlassSupplement"] = "GlassSupplements",
            ["GlassPricingTier"] = "GlassPricingTiers",
            ["Customer"] = "Customers",
            ["Prescription"] = "Prescriptions",
            ["Order"] = "Orders",
            ["OrderItem"] = "OrderItems",
            ["Sale"] = "Sales",
            ["SaleItem"] = "SaleItems",
            ["StockMovement"] = "StockMovements",
            ["Notification"] = "Notifications"
        };

        return tableMap.TryGetValue(name, out var tableName) ? tableName : name + "s";
    }

    /// <summary>
    /// Vérifie si une propriété est une clé primaire (convention : Id ou EntityNameId).
    /// </summary>
    private static bool IsPrimaryKey(PropertyInfo prop, Type entityType)
    {
        var name = prop.Name;
        return name == "Id"
            || name == entityType.Name + "Id"
            || name == entityType.Name.Replace("Detail", "") + "Id"
            || (entityType.Name.EndsWith("Detail") && name == "ProductId")
            || (entityType.Name == "GlassSupplement" && (name == "GlassId" || name == "SupplementId"))
            || (entityType.Name == "GlassPricingTier" && name == "TierId");
    }

    /// <summary>
    /// Vérifie si une propriété est une propriété de navigation (à exclure du schéma).
    /// </summary>
    private static bool IsNavigationProperty(PropertyInfo prop)
    {
        var type = prop.PropertyType;

        // Collections (ICollection<T>, IList<T>, List<T>, etc.)
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(ICollection<>) || genericDef == typeof(IList<>) ||
                genericDef == typeof(List<>) || genericDef == typeof(IEnumerable<>))
                return true;
        }

        // Références vers d'autres entités
        if (type.IsClass && type != typeof(string) && type.Namespace == "MMV.Domain.Entities")
            return true;

        return false;
    }

    /// <summary>
    /// Mappe un type .NET vers un type SQLite.
    /// </summary>
    private static string MapToSqliteType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(long) || underlying == typeof(int) || underlying == typeof(short) || underlying == typeof(byte))
            return "INTEGER";
        if (underlying == typeof(string))
            return "TEXT";
        if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
            return "REAL";
        if (underlying == typeof(bool))
            return "INTEGER";
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            return "TEXT";
        if (underlying.IsEnum)
            return "INTEGER";

        return "TEXT";
    }

    /// <summary>
    /// Vérifie si une propriété est nullable.
    /// </summary>
    private static bool IsNullable(PropertyInfo prop)
    {
        if (Nullable.GetUnderlyingType(prop.PropertyType) != null)
            return true;

        // Les types référence avec ? sont marqués nullable via NullabilityInfoContext en .NET 8
        if (!prop.PropertyType.IsValueType)
        {
            var context = new NullabilityInfoContext();
            var info = context.Create(prop);
            return info.ReadState == NullabilityState.Nullable;
        }

        return false;
    }

    // ====== DTOs Ollama ======

    private class OllamaRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        public double Temperature { get; set; }
        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
        [JsonPropertyName("top_p")]
        public double TopP { get; set; }
    }

    private class OllamaResponse
    {
        public string Response { get; set; } = string.Empty;
    }

    private class OllamaStreamResponse
    {
        public string Response { get; set; } = string.Empty;
        public bool Done { get; set; }
    }
}
