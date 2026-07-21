using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Validators;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Suppliers;

/// <summary>
/// P3-9 — Règles métier Fournisseurs, prouvées sur de <b>vraies bases SQLite jetables</b> (jamais EF InMemory :
/// InMemory n'applique ni clé étrangère ni contrainte, donc il ne pourrait rien prouver ici).
/// </summary>
/// <remarks>
/// <b>Concurrence — protocole honnête.</b> SQLite sérialise les écritures : deux transactions ne s'entrelacent pas
/// réellement. Aucun test ci-dessous n'utilise <c>Thread.Sleep</c> ni de course temporelle, qui seraient
/// fragiles et ne prouveraient rien. Les invariants sont établis autrement : (1) en construisant l'<b>état
/// déterministe</b> que la course produirait — le produit existe déjà au moment de la tentative — et (2) en
/// exerçant directement la <b>primitive atomique</b>, dont c'est précisément le rôle de rendre l'entrelacement
/// impossible. Même protocole séquentiel qu'en P3-8 §22.
/// </remarks>
public sealed class SupplierBusinessRulesTests : IDisposable
{
    private readonly string _workDirectory;

    public SupplierBusinessRulesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p39-supplier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private string NewDatabase(string fileName)
    {
        var dbPath = PathFor(fileName);
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();
        return dbPath;
    }

    private static CreateSupplierUseCase CreateUseCase(OpticDbContext ctx)
        => new(new SupplierRepository(ctx), new UnitOfWork(ctx));

    private static UpdateSupplierUseCase UpdateUseCase(OpticDbContext ctx)
        => new(new SupplierRepository(ctx), new UnitOfWork(ctx));

    private static DeleteSupplierUseCase DeleteUseCase(OpticDbContext ctx)
        => new(new SupplierRepository(ctx), new EfTransactionRunner(ctx));

    private static async Task<long> SeedSupplierAsync(string dbPath, string name = "Essilor")
    {
        using var ctx = CreateContext(dbPath);
        var created = await new SupplierRepository(ctx).CreateAsync(new Supplier { Name = name });
        await new UnitOfWork(ctx).SaveChangesAsync();
        return created.SupplierId;
    }

    private static async Task<long> SeedProductAsync(string dbPath, long supplierId, string reference, bool isActive = true)
    {
        using var ctx = CreateContext(dbPath);
        var product = new Product
        {
            Reference = reference,
            Name = "Produit " + reference,
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplierId,
            SalePrice = 10m,
            IsActive = isActive,
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.ProductId;
    }

    // ==================================================================
    // CREATE
    // ==================================================================

    [Fact]
    public async Task Create_PersisteLesValeursNormalisees()
    {
        var dbPath = NewDatabase("create-normalise.db");

        using (var ctx = CreateContext(dbPath))
        {
            var result = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand
            {
                Name = "  Essilor France  ",
                ContactEmail = "  contact@essilor.fr  ",
                Phone = "  0102030405  ",
                Address = "  12 rue de la Paix  ",
                ReferenceCode = "  VP001  ",
            });

            result.IsValid.Should().BeTrue();
            result.Name.Should().Be("Essilor France", "le résultat renvoie l'écho de l'entrée NORMALISÉE");
        }

        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.Name.Should().Be("Essilor France");
        supplier.ContactEmail.Should().Be("contact@essilor.fr");
        supplier.Phone.Should().Be("0102030405");
        supplier.Address.Should().Be("12 rue de la Paix");
        supplier.ReferenceCode.Should().Be("VP001");
    }

    [Fact]
    public async Task Create_ChampsOptionnelsVides_SontPersistesEnNull()
    {
        // Une seule représentation de « non renseigné » subsiste en base : plus de coexistence NULL / "" (🟡-2).
        var dbPath = NewDatabase("create-null.db");

        using (var ctx = CreateContext(dbPath))
        {
            var result = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand
            {
                Name = "Minimal", ContactEmail = "", Phone = "   ", Address = "", ReferenceCode = "  ",
            });
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.ContactEmail.Should().BeNull();
        supplier.Phone.Should().BeNull();
        supplier.Address.Should().BeNull();
        supplier.ReferenceCode.Should().BeNull();
    }

    [Theory]
    [InlineData("", "nom vide")]
    [InlineData("   ", "nom en espaces seuls")]
    public async Task Create_NomAbsent_EstRefuse_SansAucuneEcriture(string name, string because)
    {
        var dbPath = NewDatabase($"create-nom-{name.Length}.db");

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand { Name = name });

        result.IsValid.Should().BeFalse(because);
        result.SupplierId.Should().Be(0);
        result.ValidationErrors.Should().Contain(e => e.Message == SupplierValidator.NameRequiredMessage);

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty("une commande invalide n'écrit RIEN");
    }

    [Fact]
    public async Task Create_EmailInvalide_EstRefuse_SansAucuneEcriture()
    {
        var dbPath = NewDatabase("create-email.db");

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand
        {
            Name = "Essilor", ContactEmail = "pas-un-email",
        });

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Message == SupplierValidator.EmailInvalidMessage);

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ChampTropLong_EstRefuseCoteApplication()
    {
        // Point capital : la BASE n'aurait rien refusé. HasMaxLength(200) ne génère aucune contrainte en SQLite
        // (colonne TEXT), et un nom de 5 000 caractères était réellement persisté avant P3-9 (audit §16).
        var dbPath = NewDatabase("create-long.db");

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand { Name = new string('N', 5000) });

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Message == SupplierValidator.NameTooLongMessage);

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_AucuneUniciteNEstImposee()
    {
        // Deux fournisseurs strictement identiques restent acceptés : aucune unicité n'a été inventée (R3).
        var dbPath = NewDatabase("create-doublon.db");

        using (var ctx = CreateContext(dbPath))
        {
            var first = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand { Name = "Essilor", ReferenceCode = "VP001" });
            first.IsValid.Should().BeTrue();
        }

        using (var ctx = CreateContext(dbPath))
        {
            var second = await CreateUseCase(ctx).ExecuteAsync(new CreateSupplierCommand { Name = "Essilor", ReferenceCode = "VP001" });
            second.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().HaveCount(2);
    }

    // ==================================================================
    // UPDATE
    // ==================================================================

    [Fact]
    public async Task Update_PersisteLesValeursNormalisees()
    {
        var dbPath = NewDatabase("update-normalise.db");
        var id = await SeedSupplierAsync(dbPath, "Ancien");

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateSupplierCommand
            {
                SupplierId = id, Name = "  Nouveau  ", ContactEmail = "  a@b.fr  ", ReferenceCode = "  NEW  ",
            });

            result.SupplierFound.Should().BeTrue();
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.Name.Should().Be("Nouveau");
        supplier.ContactEmail.Should().Be("a@b.fr");
        supplier.ReferenceCode.Should().Be("NEW");
    }

    [Fact]
    public async Task Update_ChampsOptionnelsVides_DeviennentNull_AucunPatchPartiel()
    {
        var dbPath = NewDatabase("update-efface.db");
        var id = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            await new SupplierRepository(ctx).UpdateAsync(new Supplier
            {
                SupplierId = id, Name = "Essilor", ContactEmail = "old@essilor.fr", Phone = "0102030405",
                Address = "Ancienne", ReferenceCode = "OLD",
            });
            await new UnitOfWork(ctx).SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateSupplierCommand { SupplierId = id, Name = "Essilor" });
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var supplier = verify.Suppliers.AsNoTracking().Single();
        supplier.ContactEmail.Should().BeNull("une commande complète remplace les cinq champs — pas de patch partiel");
        supplier.Phone.Should().BeNull();
        supplier.Address.Should().BeNull();
        supplier.ReferenceCode.Should().BeNull();
    }

    [Fact]
    public async Task Update_CommandeInvalide_NeModifieAucuneValeurExistante()
    {
        // L'entité suivie n'est JAMAIS mutée avant que la validation ait réussi : la validation porte sur un
        // candidat détaché. Sans cet ordre, un SaveChanges déclenché ailleurs dans la portée persisterait les
        // valeurs refusées.
        var dbPath = NewDatabase("update-invalide.db");
        var id = await SeedSupplierAsync(dbPath, "Intact");

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateSupplierCommand
            {
                SupplierId = id, Name = "", ContactEmail = "pas-un-email",
            });

            result.SupplierFound.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            result.ValidationErrors.Should().Contain(e => e.Message == SupplierValidator.NameRequiredMessage);

            // Même un SaveChanges opportuniste sur la MÊME portée ne peut rien persister : rien n'a été muté.
            await new UnitOfWork(ctx).SaveChangesAsync();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Single().Name.Should().Be("Intact");
    }

    [Fact]
    public async Task Update_FournisseurIntrouvable_ResteSupplierFoundFalse()
    {
        var dbPath = NewDatabase("update-absent.db");

        using var ctx = CreateContext(dbPath);
        var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateSupplierCommand { SupplierId = 999, Name = "X" });

        result.SupplierFound.Should().BeFalse();
        result.IsValid.Should().BeTrue("l'absence n'est pas une erreur de saisie : les deux axes restent distincts");
        ctx.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    // ==================================================================
    // DELETE — la règle métier centrale de P3-9
    // ==================================================================

    [Fact]
    public async Task Delete_FournisseurSansProduit_EstSupprime()
    {
        var dbPath = NewDatabase("delete-libre.db");
        var id = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            var result = await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = id });
            result.SupplierFound.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty("sans produit lié, le fournisseur ne porte aucun historique");
    }

    [Fact]
    public async Task Delete_FournisseurIntrouvable_ResteSupplierFoundFalse()
    {
        var dbPath = NewDatabase("delete-absent.db");

        using var ctx = CreateContext(dbPath);
        var result = await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = 12345 });

        result.SupplierFound.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "produit ACTIF")]
    [InlineData(false, "produit INACTIF")]
    public async Task Delete_FournisseurAvecProduit_EstRefuseAvecMessageMetierStable(bool productIsActive, string because)
    {
        // Le cas dangereux, qui n'était couvert par AUCUN test avant P3-9 — et le cœur du défaut 🔴-1.
        // Un produit INACTIF compte tout autant : il conserve sa ligne, donc sa clé étrangère.
        var dbPath = NewDatabase($"delete-lie-{productIsActive}.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, supplierId, "P-LIE", productIsActive);

        using (var ctx = CreateContext(dbPath))
        {
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId });

            var exception = (await act.Should().ThrowAsync<BusinessRuleException>(because)).Which;
            exception.Message.Should().Be(DeleteSupplierUseCase.SupplierHasProductsMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().ContainSingle().Which.SupplierId.Should().Be(supplierId);
        verify.Products.AsNoTracking().Should().ContainSingle().Which.ProductId.Should().Be(productId);
    }

    [Fact]
    public async Task Delete_Refuse_NExposeAucunMessageProvider()
    {
        // Avant P3-9, l'utilisateur lisait littéralement « An error occurred while saving the entity changes. »
        // — message anglais, technique, sans rapport avec la cause réelle. adr-application-boundaries l'interdit.
        var dbPath = NewDatabase("delete-message.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId, "P-MSG");

        using var ctx = CreateContext(dbPath);
        var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId });

        var exception = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;

        exception.Should().NotBeOfType<PersistenceException>();
        exception.Message.Should().NotContainEquivalentOf("SQLITE");
        exception.Message.Should().NotContainEquivalentOf("constraint");
        exception.Message.Should().NotContainEquivalentOf("entity changes");
        exception.Message.Should().NotContainEquivalentOf("inner exception");
        exception.Message.Should().Be(DeleteSupplierUseCase.SupplierHasProductsMessage);
    }

    [Fact]
    public async Task Delete_LUiExistanteRecevraitDesormaisLeMessageMetier()
    {
        // SuppliersViewModel fait déjà « catch (Exception ex) → ShowErrorAsync($"…{ex.Message}") ». L'écran passe
        // donc mécaniquement du message EF anglais au message métier français, SANS QU'AUCUN FICHIER UI SOIT
        // TOUCHÉ. C'est ce qui rend l'option A compatible avec l'interdiction de modifier l'UI en P3-9.
        var dbPath = NewDatabase("delete-ui.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId, "P-UI");

        using var ctx = CreateContext(dbPath);

        string displayedMessage;
        try
        {
            await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId });
            displayedMessage = "(aucune erreur)";
        }
        catch (Exception ex) // exactement le catch de SuppliersViewModel
        {
            displayedMessage = $"Impossible de supprimer le fournisseur :\n{ex.Message}";
        }

        displayedMessage.Should().Contain(DeleteSupplierUseCase.SupplierHasProductsMessage);
        displayedMessage.Should().NotContainEquivalentOf("entity changes");
    }

    [Fact]
    public async Task Delete_RejoueApresSucces_EstIdempotent()
    {
        var dbPath = NewDatabase("delete-rejeu.db");
        var id = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            (await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = id }))
                .SupplierFound.Should().BeTrue();
        }

        using (var ctx = CreateContext(dbPath))
        {
            // Second appel : introuvable, aucune écriture, AUCUNE exception. Rejouer est sûr.
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = id });

            var result = await act.Should().NotThrowAsync();
            result.Which.SupplierFound.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Delete_ApresProduitSupprime_RedevientPossible()
    {
        // Le refus n'est pas définitif : il décrit un état, pas une condamnation.
        var dbPath = NewDatabase("delete-apres-produit.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, supplierId, "P-TMP");

        using (var ctx = CreateContext(dbPath))
        {
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId });
            await act.Should().ThrowAsync<BusinessRuleException>();
        }

        using (var ctx = CreateContext(dbPath))
        {
            ctx.Products.Remove(await ctx.Products.SingleAsync(p => p.ProductId == productId));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbPath))
        {
            (await DeleteUseCase(ctx).ExecuteAsync(new DeleteSupplierCommand { SupplierId = supplierId }))
                .SupplierFound.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Delete_CommandeNulle_Leve()
    {
        var dbPath = NewDatabase("delete-null.db");
        using var ctx = CreateContext(dbPath);

        await ((Func<Task>)(() => DeleteUseCase(ctx).ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Delete_ConstructeurRejetteLesDependancesNulles()
    {
        ((Action)(() => _ = new DeleteSupplierUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
    }

    // ==================================================================
    // PRIMITIVE ATOMIQUE — testée directement
    // ==================================================================

    [Fact]
    public async Task TryDeleteIfUnused_SansProduit_RenvoieTrue_EtSupprimeLaLigne()
    {
        var dbPath = NewDatabase("prim-libre.db");
        var id = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(id)).Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryDeleteIfUnused_AvecProduit_RenvoieFalse_EtNeSupprimeRien(bool productIsActive)
    {
        var dbPath = NewDatabase($"prim-lie-{productIsActive}.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId, "P", productIsActive);

        using (var ctx = CreateContext(dbPath))
        {
            (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(supplierId)).Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().ContainSingle();
        verify.Products.AsNoTracking().Should().ContainSingle();
    }

    [Fact]
    public async Task TryDeleteIfUnused_FournisseurAbsent_RenvoieFalse()
    {
        var dbPath = NewDatabase("prim-absent.db");

        using var ctx = CreateContext(dbPath);
        (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task TryDeleteIfUnused_NeToucheAucunAutreFournisseur()
    {
        var dbPath = NewDatabase("prim-isole.db");
        var cible = await SeedSupplierAsync(dbPath, "Cible");
        var voisin = await SeedSupplierAsync(dbPath, "Voisin");
        await SeedProductAsync(dbPath, voisin, "P-VOISIN");

        using (var ctx = CreateContext(dbPath))
        {
            (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(cible)).Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().ContainSingle().Which.SupplierId.Should().Be(voisin);
        verify.Products.AsNoTracking().Should().ContainSingle("aucune suppression partielle ni collatérale");
    }

    [Fact]
    public async Task ExistsFresh_LitLaBase_EtNonLeChangeTracker()
    {
        // Distinction essentielle : ExistsAsync (hérité, FindAsync) renverrait l'entité SUIVIE sans requêter la
        // base — donc « existe » après une suppression concurrente. ExistsFreshAsync interroge réellement SQLite.
        var dbPath = NewDatabase("exists-fresh.db");
        var id = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var repository = new SupplierRepository(ctx);

        // Charge l'entité dans le change tracker de CETTE portée.
        (await repository.GetByIdAsync(id)).Should().NotBeNull();

        // Suppression par une AUTRE connexion — le tracker de ctx l'ignore.
        using (var other = CreateContext(dbPath))
        {
            (await new SupplierRepository(other).TryDeleteIfUnusedAsync(id)).Should().BeTrue();
        }

        (await repository.ExistsAsync(id)).Should().BeTrue("FindAsync sert l'entité suivie, désormais PÉRIMÉE");
        (await repository.ExistsFreshAsync(id)).Should().BeFalse("AnyAsync interroge la base réelle");
    }

    // ==================================================================
    // ENTRELACEMENT DÉTERMINISTE
    // ==================================================================

    [Fact]
    public async Task Course_ProduitCreeApresObservationDAbsence_LaTentativeAtomiqueEchoue()
    {
        // Reproduction déterministe de la course, sans délai : on OBSERVE d'abord l'absence de produit (ce que
        // ferait une garde « check-then-act »), PUIS un autre poste crée le produit, PUIS on tente la suppression.
        // Une garde HasProducts-puis-Delete aurait supprimé ; la primitive atomique refuse.
        var dbPath = NewDatabase("course-creation.db");
        var supplierId = await SeedSupplierAsync(dbPath);

        // 1. Observation périmée : aucun produit lié à cet instant.
        using (var observer = CreateContext(dbPath))
        {
            (await observer.Products.AsNoTracking().AnyAsync(p => p.SupplierId == supplierId)).Should().BeFalse();
        }

        // 2. Un autre poste crée le produit.
        await SeedProductAsync(dbPath, supplierId, "P-COURSE");

        // 3. La tentative atomique réévalue la condition AU MOMENT de l'écriture.
        using (var ctx = CreateContext(dbPath))
        {
            (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(supplierId)).Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().ContainSingle();
        verify.Products.AsNoTracking().Should().ContainSingle();
    }

    [Fact]
    public async Task Course_SuppressionPuisCreationDeProduit_EstRefuseeSansOrphelin()
    {
        // L'ordre inverse : la suppression atomique réussit, puis un poste tente de créer un produit pointant vers
        // le fournisseur disparu. La FK arbitre — jamais d'orphelin.
        var dbPath = NewDatabase("course-inverse.db");
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(supplierId)).Should().BeTrue();
        }

        using (var ctx = CreateContext(dbPath))
        {
            ctx.Products.Add(new Product
            {
                Reference = "ORPHELIN", Name = "Orphelin", Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplierId, SalePrice = 1m,
            });

            var act = async () => await ctx.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>("la FK RESTRICT refuse un fournisseur inexistant");
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty("aucun produit orphelin n'a pu être créé");
        verify.Suppliers.AsNoTracking().Should().BeEmpty();
    }
}
