using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.ListUsers;
using MMV.Application.UseCases.Users.UpdateUser;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Application.Tests.Architecture;

/// <summary>
/// P3-10 — Garde-fous structurels du domaine Utilisateurs : verrouillent contre toute régression future les
/// invariants établis par cette étape, ainsi que les <b>non-décisions</b> assumées (aucune suppression, aucune
/// autorisation par acteur, aucune permission fine).
/// </summary>
public sealed class UsersApplicationArchitectureTests : IDisposable
{
    private readonly string _workDirectory;

    public UsersApplicationArchitectureTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p310-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
    }

    private static Assembly ApplicationAssembly => typeof(CreateUserUseCase).Assembly;
    private static Assembly DomainAssembly => typeof(User).Assembly;

    private OpticDbContext CreateContext(string fileName)
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_workDirectory, fileName)};Pooling=False").Options);

    // ================================================================== Frontières de couches

    [Fact]
    public void DomainAndApplication_ReferenceNeitherEfNorSqlite()
    {
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly })
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

            referenced.Should().NotContain(n => n != null && n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal),
                $"{assembly.GetName().Name} ne doit dépendre d'aucun ORM");
            referenced.Should().NotContain(n => n != null && n.Contains("Sqlite", StringComparison.OrdinalIgnoreCase),
                $"{assembly.GetName().Name} ne doit dépendre d'aucun provider");
            referenced.Should().NotContain(n => n != null && n.Contains("BCrypt", StringComparison.OrdinalIgnoreCase),
                $"{assembly.GetName().Name} ne doit pas embarquer l'implémentation de hachage");
        }
    }

    [Fact]
    public void ApplicationAssembly_ReferencesOnlyDomain_AmongProjectAssemblies()
    {
        var projectReferences = ApplicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n != null && n.StartsWith("MMV.", StringComparison.Ordinal))
            .ToList();

        projectReferences.Should().Equal(new[] { "MMV.Domain" });
    }

    /// <summary>
    /// Le port utilisateur reste <b>provider-neutre</b> : aucune signature n'expose <c>IQueryable</c>, EF ou
    /// SQLite. Sans cette garde, une requête pourrait fuir dans la couche Application et y réintroduire une
    /// dépendance de persistance.
    /// </summary>
    [Fact]
    public void UserRepositoryPort_IsProviderNeutral()
    {
        foreach (var method in typeof(IUserRepository).GetMethods())
        {
            var types = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType);

            foreach (var type in types)
            {
                var name = type.FullName ?? type.Name;
                name.Should().NotContain("IQueryable");
                name.Should().NotContain("EntityFrameworkCore");
                name.Should().NotContain("Sqlite");
            }
        }
    }

    // ================================================================== Normalisation : un seul propriétaire

    /// <summary>
    /// <c>UserIdentityPolicy</c> est le <b>seul</b> propriétaire de la normalisation : aucun chemin runtime User
    /// ne recopie <c>Trim().ToLowerInvariant()</c>. Quatre copies pouvant diverger recréeraient l'ambiguïté que
    /// P3-10 corrige.
    /// </summary>
    [Fact]
    public void NormalizationRule_HasASingleOwner_InRuntimeUserPaths()
    {
        var repositoryRoot = FindRepositoryRoot();

        var runtimeUserFiles = new[]
        {
            "src/MMV.Application/UseCases/Users/CreateUser/CreateUserUseCase.cs",
            "src/MMV.Application/UseCases/Users/UpdateUser/UpdateUserUseCase.cs",
            "src/MMV.Application/UseCases/Users/Common/UserCommandGuards.cs",
            "src/MMV.Infrastructure/Repositories/UserRepository.cs",
            "src/MMV.Infrastructure/Services/AuthenticationService.cs",
            "src/MMV.Infrastructure/Configuration/DatabaseSeeder.cs",
        };

        foreach (var relativePath in runtimeUserFiles)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath);
            File.Exists(fullPath).Should().BeTrue($"{relativePath} doit exister");

            var source = File.ReadAllText(fullPath);
            source.Should().NotContain("ToLowerInvariant()",
                $"{relativePath} doit déléguer à UserIdentityPolicy.NormalizeUsername, jamais recopier la règle");
        }
    }

    [Fact]
    public void UserIdentityPolicy_LivesInDomain_AndIsStateless()
    {
        var type = typeof(UserIdentityPolicy);

        type.Assembly.Should().BeSameAs(DomainAssembly, "la règle d'identité appartient au Domain");
        type.IsAbstract.Should().BeTrue();
        type.IsSealed.Should().BeTrue("classe statique : aucun état, aucune instance");
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Should().BeEmpty("aucune dépendance ni état mutable");
    }

    // ================================================================== Données sensibles

    [Fact]
    public void UserEntity_CarriesNoClearTextPassword()
    {
        var properties = typeof(User).GetProperties().Select(p => p.Name).ToList();

        properties.Should().Contain("PasswordHash");
        properties.Should().NotContain(p =>
            p.Equals("Password", StringComparison.OrdinalIgnoreCase)
            || p.Equals("ClearPassword", StringComparison.OrdinalIgnoreCase)
            || p.Equals("PlainPassword", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UserListItemDto_ExposesNeitherHashNorSecret()
    {
        var properties = typeof(UserListItemDto).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain(p =>
            p.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Hash", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Salt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SeedResult_ExposesNoBootstrapSecret()
    {
        var properties = typeof(SeedResult).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain(p =>
            p.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================== Non-décisions assumées

    /// <summary>
    /// Aucune suppression d'utilisateur n'est exposée : la désactivation reste le seul mécanisme de retrait.
    /// Les FK historiques étant <c>SetNull</c>, une suppression anonymiserait ventes et mouvements de stock.
    /// </summary>
    [Fact]
    public void NoDeleteUserUseCase_Exists()
    {
        var deleteUserTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.Name.Contains("DeleteUser", StringComparison.Ordinal))
            .ToList();

        deleteUserTypes.Should().BeEmpty("P3-10 conserve la désactivation, pas la suppression");
    }

    /// <summary>
    /// Aucune autorisation par acteur n'est introduite : P3-10 valide la VALEUR du rôle, pas le DROIT de le
    /// demander. Cette limite est un report explicite — la verrouiller évite de croire, plus tard, qu'elle est
    /// couverte.
    /// </summary>
    [Fact]
    public void NoActorBasedAuthorization_IsIntroduced()
    {
        var actorTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.Name is "ICurrentUser" or "CurrentUser" or "IPermissionService" or "IAuthorizationService")
            .ToList();

        actorTypes.Should().BeEmpty(
            "l'autorisation par acteur reste hors périmètre P3-10 (report explicite)");
    }

    /// <summary>Aucune notion de permission fine n'est inventée : les rôles restent les trois valeurs connues.</summary>
    [Fact]
    public void UserRole_KeepsExactlyTheThreeKnownValues()
        => Enum.GetNames<UserRole>().Should().BeEquivalentTo(new[] { "Admin", "Optician", "Technician" });

    // ================================================================== Schéma

    [Fact]
    public void NormalizedUsername_IsRequired_AndUniquelyIndexed_InTheModel()
    {
        using var context = CreateContext("arch-model.db");
        var entity = context.Model.FindEntityType(typeof(User))!;

        var normalized = entity.FindProperty(nameof(User.NormalizedUsername))!;
        normalized.IsNullable.Should().BeFalse();
        normalized.GetMaxLength().Should().Be(UserIdentityPolicy.UsernameMaxLength);

        var uniqueIndexes = entity.GetIndexes().Where(i => i.IsUnique).ToList();
        uniqueIndexes.Should().ContainSingle("une seule clé métier d'unicité sur Users")
            .Which.Properties.Select(p => p.Name).Should().Equal(new[] { nameof(User.NormalizedUsername) });

        entity.FindProperty(nameof(User.Username))!.IsNullable.Should().BeFalse();
    }

    /// <summary>
    /// Les FK historiques restent <c>SetNull</c> et nullables : P3-10 ne les passe pas en <c>Restrict</c>, car
    /// aucune suppression n'est exposée — il n'y a rien à empêcher.
    /// </summary>
    [Fact]
    public void HistoricalForeignKeys_RemainUnchanged()
    {
        using var context = CreateContext("arch-fk.db");

        foreach (var entityType in new[] { typeof(Sale), typeof(StockMovement) })
        {
            var foreignKey = context.Model.FindEntityType(entityType)!
                .GetForeignKeys()
                .Single(fk => fk.PrincipalEntityType.ClrType == typeof(User));

            foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.SetNull);
            foreignKey.Properties.Should().OnlyContain(p => p.IsNullable);
        }
    }

    [Fact]
    public void RoleIsOptionalOnCommands_SoOmissionCannotMeanAdmin()
    {
        typeof(CreateUserCommand).GetProperty(nameof(CreateUserCommand.Role))!
            .PropertyType.Should().Be(typeof(UserRole?));
        typeof(UpdateUserCommand).GetProperty(nameof(UpdateUserCommand.Role))!
            .PropertyType.Should().Be(typeof(UserRole?));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MMV.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("la racine du dépôt (MMV.sln) doit être trouvable depuis les tests");
        return directory!.FullName;
    }
}
