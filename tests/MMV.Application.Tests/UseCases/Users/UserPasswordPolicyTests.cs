using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.UpdateUser;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Users;

/// <summary>
/// P3-10 — La politique de mot de passe est réellement appliquée sur les chemins runtime d'écriture
/// <b>Application</b> (Create / Update), <b>avant</b> tout hachage et avant toute écriture.
/// </summary>
/// <remarks>
/// <para>
/// Avant P3-10, seuls <c>UserFormViewModel</c> et <c>UserProfileViewModel</c> appliquaient la politique : tout
/// appelant entrant par la couche Application persistait un mot de passe faible (audit §8, R1/R7). Ces tests
/// exercent la couche Application <b>sans UI</b> — c'est exactement le chemin de contournement d'alors.
/// </para>
/// <para>
/// Les branches internes de la politique restent couvertes par <c>UserValidatorTests</c> (Domain) ; on ne les
/// duplique pas. On vérifie ici qu'elles sont <b>invoquées</b>, et surtout ce que le Domain seul ne peut pas
/// prouver : qu'un refus ne laisse aucune trace en base.
/// </para>
/// </remarks>
public sealed class UserPasswordPolicyTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>
    /// Fake d'<see cref="IAuthenticationService"/> qui <b>compte</b> les hachages. Le compteur permet de prouver
    /// une propriété autrement invisible : le refus survient AVANT le hachage, et non après (auquel cas le mot de
    /// passe faible aurait tout de même transité par BCrypt).
    /// </summary>
    private sealed class CountingAuth : IAuthenticationService
    {
        public int HashCallCount { get; private set; }

        public string HashPassword(string password)
        {
            HashCallCount++;
            return "HASHED::" + password;
        }

        public bool ValidatePassword(string password, string hash) => hash == HashPassword(password);
        public Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task UpdateLastLoginAsync(long userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default) => Task.FromResult(true);
    }

    public UserPasswordPolicyTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p310-pwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options);

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static CreateUserCommand ValidCreate(string username, string password) => new()
    {
        Username = username,
        FirstName = "Jean",
        LastName = "Dupont",
        Role = UserRole.Optician,
        IsActive = true,
        Password = password,
    };

    /// <summary>
    /// Chaque branche de refus de la politique (longueur, majuscule, minuscule, chiffre, vide) doit bloquer la
    /// création : aucun utilisateur en base, aucun appel à BCrypt.
    /// </summary>
    [Theory]
    [InlineData("")]                 // vide
    [InlineData("   ")]              // blanc
    [InlineData("Ab1")]              // trop court
    [InlineData("abcdefg1")]         // pas de majuscule
    [InlineData("ABCDEFG1")]         // pas de minuscule
    [InlineData("Abcdefgh")]         // pas de chiffre
    public async Task Create_WeakPassword_IsRejected_NothingWritten_NoHashing(string weakPassword)
    {
        var dbPath = PathFor($"create-weak-{Math.Abs(weakPassword.GetHashCode())}.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var auth = new CountingAuth();
        var useCase = new CreateUserUseCase(
            new UserRepository(context), new UnitOfWork(context), auth, new EfTransactionRunner(context));

        var result = await useCase.ExecuteAsync(ValidCreate("jdupont", weakPassword));

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(User.PasswordHash));
        result.UserId.Should().Be(0);

        auth.HashCallCount.Should().Be(0, "le refus doit précéder le hachage, pas le suivre");
        context.Users.AsNoTracking().Should().BeEmpty("aucune écriture pour un mot de passe faible");
    }

    [Fact]
    public async Task Create_StrongPassword_IsAccepted_AndClearTextNeverPersisted()
    {
        var dbPath = PathFor("create-strong.db");
        EnsureSchema(dbPath);

        const string clearPassword = "S3cretPass1";

        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateUserUseCase(
                new UserRepository(context), new UnitOfWork(context), new CountingAuth(), new EfTransactionRunner(context));
            var result = await useCase.ExecuteAsync(ValidCreate("jdupont", clearPassword));

            result.IsValid.Should().BeTrue();
            result.UserId.Should().BeGreaterThan(0);
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.PasswordHash.Should().NotBe(clearPassword, "le clair ne doit jamais être persisté");
        user.PasswordHash.Should().Contain("HASHED::");
        user.NormalizedUsername.Should().Be("jdupont");
    }

    /// <summary>
    /// Mot de passe faible à la modification : refus, et <b>aucune</b> propriété de l'entité suivie n'est
    /// modifiée — ni le hash, ni les champs de profil fournis dans la même commande.
    /// </summary>
    [Fact]
    public async Task Update_WeakPassword_IsRejected_NoPropertyChanged()
    {
        var dbPath = PathFor("update-weak.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new UserRepository(context).CreateAsync(new User
            {
                Username = "old",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("old"),
                FirstName = "O", LastName = "L", PasswordHash = "KEEP", Role = UserRole.Technician, IsActive = true,
            });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var auth = new CountingAuth();
            var useCase = new UpdateUserUseCase(
                new UserRepository(context), new UnitOfWork(context), auth, new EfTransactionRunner(context));

            var result = await useCase.ExecuteAsync(new UpdateUserCommand
            {
                UserId = id, Username = "renamed", FirstName = "N", LastName = "W",
                Role = UserRole.Admin, IsActive = false, Password = "weak",
            });

            result.UserFound.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            auth.HashCallCount.Should().Be(0);
        }

        // Vérification dans un NOUVEAU contexte : rien n'a été persisté, y compris les champs de profil qui
        // accompagnaient le mot de passe faible dans la même commande.
        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.PasswordHash.Should().Be("KEEP");
        user.Username.Should().Be("old");
        user.FirstName.Should().Be("O");
        user.Role.Should().Be(UserRole.Technician);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Update_NoPassword_KeepsExistingHash()
    {
        var dbPath = PathFor("update-nopwd.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new UserRepository(context).CreateAsync(new User
            {
                Username = "old",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("old"),
                FirstName = "O", LastName = "L", PasswordHash = "KEEP", Role = UserRole.Technician,
            });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var auth = new CountingAuth();
            var useCase = new UpdateUserUseCase(
                new UserRepository(context), new UnitOfWork(context), auth, new EfTransactionRunner(context));

            var result = await useCase.ExecuteAsync(new UpdateUserCommand
            {
                UserId = id, Username = "old", FirstName = "N", LastName = "W",
                Role = UserRole.Optician, IsActive = true, Password = null,
            });

            result.IsValid.Should().BeTrue();
            auth.HashCallCount.Should().Be(0, "aucun mot de passe fourni ⇒ aucun re-hachage");
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.PasswordHash.Should().Be("KEEP");
        user.FirstName.Should().Be("N", "les autres champs sont bien modifiés");
    }

    [Fact]
    public async Task Update_StrongPassword_ProducesNewHash()
    {
        var dbPath = PathFor("update-strong.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new UserRepository(context).CreateAsync(new User
            {
                Username = "old",
                NormalizedUsername = UserIdentityPolicy.NormalizeUsername("old"),
                FirstName = "O", LastName = "L", PasswordHash = "KEEP", Role = UserRole.Technician,
            });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new UpdateUserUseCase(
                new UserRepository(context), new UnitOfWork(context), new CountingAuth(), new EfTransactionRunner(context));

            var result = await useCase.ExecuteAsync(new UpdateUserCommand
            {
                UserId = id, Username = "old", FirstName = "O", LastName = "L",
                Role = UserRole.Technician, IsActive = true, Password = "N3wStrongPass",
            });

            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().PasswordHash.Should().Be("HASHED::N3wStrongPass");
    }
}
