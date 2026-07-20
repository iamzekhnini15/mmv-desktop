using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-8 — Garde-fous d'architecture du domaine Notifications, sur le modèle de
/// <c>SalesBusinessRulesArchitectureTests</c> (P3-7). Verrouille les invariants structurels établis par P3-8 contre
/// toute régression future.
/// </summary>
public sealed class NotificationsArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Notification).Assembly;

    // ------------------------------------------------------------------
    // 1. Modèle : deux axes, jamais un troisième champ redondant
    // ------------------------------------------------------------------

    [Fact]
    public void Notification_PorteResolvedAt_MaisPasDeChampIsResolvedPersiste()
    {
        var properties = typeof(Notification).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        properties.Should().Contain("ResolvedAt");
        properties.Should().Contain("IsRead", "l'axe de lecture reste, il n'est pas remplacé mais séparé");

        // Deux colonnes pour un même état finiraient par diverger. ResolvedAt porte à lui seul « résolue ou non »
        // ET la date, sans redondance possible.
        properties.Should().NotContain("IsResolved");
        typeof(Notification).GetProperty("ResolvedAt")!.PropertyType.Should().Be(typeof(DateTime?));
    }

    [Fact]
    public void Notification_NAcquiertNiCleEtrangereNiPrioriteNiDestinataire()
    {
        // Périmètre P3-8 strictement tenu : les reports (priorité, destinataire utilisateur, navigation) ne doivent
        // pas s'inviter par glissement.
        var properties = typeof(Notification).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        properties.Should().NotContain("Priority");
        properties.Should().NotContain("UserId");
        properties.Should().NotContain("Product");
        properties.Should().NotContain("Order");
    }

    // ------------------------------------------------------------------
    // 2. Constantes centralisées
    // ------------------------------------------------------------------

    [Fact]
    public void LesTypesCanoniques_SontDesConstantesDomain_EtPasUnEnum()
    {
        // Constantes string délibérément, pas un enum : la colonne Type est un TEXT historique et sa conversion
        // exigerait une migration de données hors périmètre (report R9).
        typeof(NotificationTypes).IsAbstract.Should().BeTrue();
        typeof(NotificationTypes).IsSealed.Should().BeTrue("une classe statique");
        typeof(NotificationTypes).IsEnum.Should().BeFalse();
        typeof(NotificationEntityTypes).IsEnum.Should().BeFalse();

        DomainAssembly.GetTypes().Where(t => t.IsEnum)
            .Should().NotContain(t => t.Name.Contains("Notification", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // 3. Aucun littéral runtime dupliqué pour les types canoniques
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("LowStock")]
    [InlineData("StockOut")]
    [InlineData("OrderStatusChanged")]
    [InlineData("PaymentReceived")]
    public void AucunLitteralDeTypeCanonique_NeSubsisteDansLeCodeRuntime(string canonicalType)
    {
        // Toute faute de frappe sur ces chaînes casserait silencieusement l'anti-doublon ET le filtre de l'index :
        // la seule barrière est qu'elles n'apparaissent plus qu'à UN endroit, la constante Domain.
        var offenders = RuntimeSourceFiles()
            .Where(file => !IsConstantsDeclaration(file))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), $"\"{canonicalType}\""))
            .Select(Relative)
            .ToList();

        offenders.Should().BeEmpty(
            $"« {canonicalType} » ne doit exister qu'en constante Domain, jamais recopié dans le code runtime");
    }

    // ------------------------------------------------------------------
    // 4. Pureté des couches
    // ------------------------------------------------------------------

    [Fact]
    public void LeDomaine_NeReferenceNiEfCoreNiSqlite()
    {
        // La garantie symétrique côté Application est vérifiée par NotificationsApplicationArchitectureTests, seul
        // projet de test à référencer cet assembly.
        var referenced = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

        referenced.Should().NotContain(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        referenced.Should().NotContain(n => n.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void LeContratDuRepository_NExposeAucunSqlNiAucuneNotionEf()
    {
        // La primitive d'insertion atomique est spécialisée mais reste PROVIDER-NEUTRE : « ON CONFLICT DO NOTHING »
        // est un détail d'implémentation SQLite qui ne doit jamais transparaître dans le port.
        var signatures = typeof(INotificationRepository)
            .GetMethods()
            .Select(m => m.Name + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)))
            .ToList();

        signatures.Should().NotContain(s => s.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        signatures.Should().NotContain(s => s.Contains("EntityFrameworkCore", StringComparison.Ordinal));
        signatures.Should().NotContain(s => s.Contains("Sql", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // 5. Code mort supprimé, aucun chemin individuel réintroduit
    // ------------------------------------------------------------------

    [Fact]
    public void LesMethodesMortesDeMarquageIndividuel_NExistentPlus()
    {
        // MarkAsReadAsync était mort ET latent-défectueux (UpdateAsync sans SaveChangesAsync : il ne persistait
        // rien). GetUnreadNotificationsAsync était mort. Seul le marquage ENSEMBLISTE subsiste.
        var methods = typeof(INotificationRepository).GetMethods().Select(m => m.Name).ToList();

        methods.Should().NotContain("MarkAsReadAsync");
        methods.Should().NotContain("GetUnreadNotificationsAsync");
        methods.Should().Contain("MarkAllAsReadAsync");
    }

    // ------------------------------------------------------------------
    // 6. Aucune notification atelier / QC inventée
    // ------------------------------------------------------------------

    [Fact]
    public void AucuneNotificationAtelierOuControleQualite_NEstIntroduite()
    {
        // L'audit a établi qu'aucune preuve métier ne justifie ces notifications : les inventer serait du périmètre
        // non demandé.
        NotificationTypes.LowStock.Should().NotContain("Workshop");

        var constants = typeof(NotificationTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        constants.Should().BeEquivalentTo(new[]
        {
            "LowStock", "StockOut", "OrderStatusChanged", "PaymentReceived", "Info",
        }, "aucun type atelier/QC n'est ajouté en P3-8");

        typeof(NotificationEntityTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!)
            .Should().BeEquivalentTo(new[] { "Product", "Order" });
    }

    // ------------------------------------------------------------------
    // Harnais de lecture des sources
    // ------------------------------------------------------------------

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null,
            $"Le fichier solution MMV.sln doit être trouvable en remontant depuis {AppContext.BaseDirectory}.");

        return dir!.FullName;
    }

    /// <summary>
    /// Fichiers source de production, hors artefacts de build et hors migrations — une migration est un instantané
    /// historique figé : ses littéraux SQL sont la trace du schéma au moment où elle a été écrite et ne doivent
    /// jamais être réécrits pour suivre une constante.
    /// </summary>
    private static IEnumerable<string> RuntimeSourceFiles()
    {
        var root = RepositoryRoot();

        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool IsConstantsDeclaration(string file)
    {
        var name = Path.GetFileName(file);
        return name is "NotificationTypes.cs" or "NotificationEntityTypes.cs";
    }

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot(), file);
}
