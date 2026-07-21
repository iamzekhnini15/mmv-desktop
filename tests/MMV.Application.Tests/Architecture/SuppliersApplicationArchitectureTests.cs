using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.Architecture;

/// <summary>
/// P3-9 — Garde-fous structurels du domaine Fournisseurs : verrouillent contre toute régression future les
/// invariants établis par cette étape, ainsi que les <b>non-décisions</b> assumées (aucun cycle de vie, aucune
/// unicité, aucune migration).
/// </summary>
public sealed class SuppliersApplicationArchitectureTests : IDisposable
{
    private readonly string _workDirectory;

    public SuppliersApplicationArchitectureTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p39-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
    }

    private OpticDbContext CreateContext(string fileName)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_workDirectory, fileName)};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static IForeignKey SupplierForeignKey(OpticDbContext context)
        => context.Model.FindEntityType(typeof(Product))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Supplier));

    // ==================================================================
    // Relation Supplier ↔ Product
    // ==================================================================

    [Fact]
    public void LaFk_ProduitVersFournisseur_EstNonNullableEtRestrict()
    {
        using var context = CreateContext("fk-model.db");
        var foreignKey = SupplierForeignKey(context);

        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        foreignKey.IsRequired.Should().BeTrue();
        foreignKey.Properties.Single().ClrType.Should().Be(typeof(long), "SupplierId n'est PAS nullable");
    }

    [Fact]
    public void AucunSetNullNiCascade_NEstConfigureSurLaRelationFournisseur()
    {
        // Le DeleteBehavior.SetNull mort de SupplierConfiguration a été retiré en P3-9. Le réintroduire — ou pire,
        // passer en Cascade — détruirait l'historique produit ou créerait des orphelins, à rebours de P3-4B.
        // SetNull serait de surcroît inapplicable : il exige une FK nullable.
        using var context = CreateContext("fk-nosetnull.db");

        SupplierForeignKey(context).DeleteBehavior.Should()
            .NotBe(DeleteBehavior.SetNull).And.NotBe(DeleteBehavior.Cascade);
    }

    [Fact]
    public void LaRelationFournisseur_EstConfigureeDUnSeulCote()
    {
        // Deux configurations contradictoires de la MÊME relation (SupplierConfiguration vs ProductConfiguration)
        // rendaient le code trompeur : l'une décrivait un comportement inexistant. Une seule FK doit subsister.
        using var context = CreateContext("fk-unique.db");

        context.Model.FindEntityType(typeof(Product))!
            .GetForeignKeys()
            .Count(fk => fk.PrincipalEntityType.ClrType == typeof(Supplier))
            .Should().Be(1);
    }

    [Fact]
    public async Task LaBase_RefuseUneSuppressionForceeHorsUseCase()
    {
        // Le filet ultime : même en contournant totalement la couche Application (suppression directe via le
        // DbContext), la base refuse. Verrouille la protection contre une régression de configuration.
        using var context = CreateContext("fk-filet.db");
        await context.Database.EnsureCreatedAsync();

        var supplier = new Supplier { Name = "Lié" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        context.Products.Add(new Product
        {
            Reference = "P-FK", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId, SalePrice = 1m,
        });
        await context.SaveChangesAsync();

        // SQL brut : contourne totalement EF — ni change tracker, ni configuration de modèle, ni use case. Seule
        // la contrainte réellement présente dans le fichier SQLite peut encore refuser. C'est le filet ultime.
        var act = async () => await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Suppliers\" WHERE \"SupplierId\" = {0}", supplier.SupplierId);

        (await act.Should().ThrowAsync<SqliteException>()).Which
            .SqliteErrorCode.Should().Be(19, "SQLITE_CONSTRAINT : la base elle-même refuse");

        context.ChangeTracker.Clear();
        (await context.Suppliers.AsNoTracking().CountAsync()).Should().Be(1, "rien n'a été supprimé");
        (await context.Products.AsNoTracking().CountAsync()).Should().Be(1, "aucun produit orphelin ni détruit");
    }

    [Fact]
    public async Task EfLuiMeme_RefuseDeSeveerUneRelationRequise()
    {
        // Second rempart, en amont du SQL : la relation étant requise et non nullable, EF refuse de détacher un
        // produit de son fournisseur. Aucune mise à null silencieuse n'est possible — ce que le DeleteBehavior
        // .SetNull retiré en P3-9 laissait faussement croire.
        using var context = CreateContext("fk-severed.db");
        await context.Database.EnsureCreatedAsync();

        var supplier = new Supplier { Name = "Lié" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        context.Products.Add(new Product
        {
            Reference = "P-SEV", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId, SalePrice = 1m,
        });
        await context.SaveChangesAsync();

        ((Action)(() => context.Suppliers.Remove(supplier))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LesClesEtrangeres_SontEffectivementActivees()
    {
        using var context = CreateContext("fk-pragma.db");
        await context.Database.EnsureCreatedAsync();

        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";

        Convert.ToInt64(await command.ExecuteScalarAsync()).Should()
            .Be(1, "sans ce PRAGMA, SQLite n'appliquerait aucune FK et tout le filet serait fictif");
    }

    // ==================================================================
    // Neutralité du port
    // ==================================================================

    [Fact]
    public void LePortFournisseur_ResteProviderNeutre()
    {
        // Aucune signature n'expose EF, SQLite, IQueryable ni SQL : le Domain ne doit rien apprendre du provider.
        var forbidden = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "System.Linq.IQueryable" };

        var exposedTypes = typeof(ISupplierRepository).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .Select(t => t.FullName ?? string.Empty)
            .ToList();

        exposedTypes.Should().NotContain(n => forbidden.Any(f => n.StartsWith(f, StringComparison.Ordinal)));
    }

    [Fact]
    public void LesPrimitivesP39_SontDeclareesSurLePort()
    {
        var methods = typeof(ISupplierRepository).GetMethods().Select(m => m.Name).ToList();

        methods.Should().Contain("TryDeleteIfUnusedAsync", "la suppression conditionnelle est atomique par contrat");
        methods.Should().Contain("ExistsFreshAsync", "l'existence doit être lisible sans passer par le tracker");
    }

    [Fact]
    public void LesUseCasesProduit_DependentDuPortFournisseur()
    {
        // La garde « fournisseur obligatoire » est structurelle, pas décorative.
        foreach (var useCase in new[] { typeof(CreateProductUseCase), typeof(UpdateProductUseCase) })
        {
            useCase.GetConstructors().Single()
                .GetParameters().Select(p => p.ParameterType)
                .Should().Contain(typeof(ISupplierRepository), $"{useCase.Name} doit pouvoir vérifier le fournisseur");
        }
    }

    [Fact]
    public void LaSuppressionFournisseur_EstEnveloppeeDansUneFrontiereTransactionnelle()
    {
        // Sans le runner, aucune traduction de PersistenceException : un message EF brut atteindrait l'UI.
        typeof(DeleteSupplierUseCase).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType.Name)
            .Should().Contain("ITransactionRunner");
    }

    // ==================================================================
    // Non-décisions assumées
    // ==================================================================

    [Fact]
    public void LEntiteFournisseur_NaAucunEtatDeCycleDeVie()
    {
        // Aucun besoin métier d'archivage fournisseur n'est prouvé dans le dépôt : ni roadmap, ni document, ni
        // écran, ni commentaire. L'introduire serait inventer un besoin — exactement ce que P3-9 s'interdit pour
        // les doublons. Report explicite (R2).
        typeof(Supplier).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().NotContain("IsActive").And.NotContain("IsArchived");
    }

    [Fact]
    public void AucunUseCaseDArchivageOuDeDesactivationFournisseur_NExiste()
    {
        typeof(CreateSupplierUseCase).Assembly.GetTypes()
            .Where(t => t.Name.Contains("Supplier", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Should().NotContain(n => n.Contains("Archive", StringComparison.Ordinal)
                                      || n.Contains("Deactivate", StringComparison.Ordinal)
                                      || n.Contains("SetSupplierActive", StringComparison.Ordinal));
    }

    [Fact]
    public void AucunIndexUnique_NEstImposeSurLaTableFournisseurs()
    {
        // Aucune unicité fournisseur n'a été inventée (nom, e-mail, téléphone, code de référence) : R3.
        using var context = CreateContext("index-unique.db");

        context.Model.FindEntityType(typeof(Supplier))!.GetIndexes()
            .Should().NotContain(i => i.IsUnique);
    }

    [Fact]
    public void LesResultatsFournisseur_SuiventLaConventionP31()
    {
        foreach (var resultType in new[] { typeof(CreateSupplierResult), typeof(UpdateSupplierResult) })
        {
            var properties = resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).ToList();

            properties.Should().Contain("ValidationErrors", $"{resultType.Name} expose les erreurs de saisie");
            properties.Should().Contain("IsValid");
        }
    }

    [Fact]
    public void LesMessagesMetierFournisseur_SontDesConstantesPubliquesStables()
    {
        // Exposés en constantes pour que l'UI et les tests s'y réfèrent sans les dupliquer.
        DeleteSupplierUseCase.SupplierHasProductsMessage.Should()
            .Be("Ce fournisseur ne peut pas être supprimé car il est associé à un ou plusieurs produits.");
        CreateProductUseCase.SupplierRequiredMessage.Should().Be("Le fournisseur est obligatoire.");
        CreateProductUseCase.SupplierNotFoundMessage.Should().Be("Le fournisseur sélectionné est introuvable.");

        foreach (var message in new[]
                 {
                     DeleteSupplierUseCase.SupplierHasProductsMessage,
                     CreateProductUseCase.SupplierRequiredMessage,
                     CreateProductUseCase.SupplierNotFoundMessage,
                 })
        {
            message.Should().NotContainEquivalentOf("SQLITE");
            message.Should().NotContainEquivalentOf("constraint");
            message.Should().NotContainEquivalentOf("exception");
        }
    }

    [Fact]
    public void LeCodeMortDocumente_EstConserveEnLEtat()
    {
        // P3-9 ne supprime rien opportunément : ces membres sont une dette documentée (🟡-8, R14), pas la cause du
        // défaut corrigé. Les retirer élargirait le périmètre sans rien sécuriser.
        var methods = typeof(ISupplierRepository).GetMethods().Select(m => m.Name).ToList();

        methods.Should().Contain("GetByNameAsync");
        methods.Should().Contain("GetByReferenceCodeAsync");
        typeof(SupplierRepository).GetMethod("DeleteAsync", new[] { typeof(long), typeof(CancellationToken) })
            .Should().NotBeNull();
    }
}
