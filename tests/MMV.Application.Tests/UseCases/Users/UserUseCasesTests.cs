using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.CreateUser;
using MMV.Application.UseCases.Users.SetUserActive;
using MMV.Application.UseCases.Users.UpdateUser;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Users;

/// <summary>
/// P2C-GLOBAL — Use cases du module Utilisateurs (Create / Update / SetActive), extraits iso-fonctionnellement de
/// <c>UserFormViewModel.ExecuteSaveAsync</c> et <c>UsersListViewModel.ToggleActiveAsync</c>. Vérifie sur un
/// <b>vrai SQLite temporaire</b> le hachage du mot de passe, l'unicité du nom d'utilisateur, l'activation et les gardes.
/// </summary>
public sealed class UserUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    /// <summary>Fake d'<see cref="IAuthenticationService"/> : seul <see cref="HashPassword"/> est sollicité.</summary>
    private sealed class FakeAuth : IAuthenticationService
    {
        public string HashPassword(string password) => "HASHED::" + password;
        public bool ValidatePassword(string password, string hash) => hash == HashPassword(password);
        public Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task UpdateLastLoginAsync(long userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken ct = default) => Task.FromResult(true);
    }

    public UserUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2cg-user-" + Guid.NewGuid().ToString("N"));
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
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Create_HashesPasswordAndPersists()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);

        CreateUserResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateUserUseCase(new UserRepository(context), new UnitOfWork(context), new FakeAuth(), new EfTransactionRunner(context));
            result = await useCase.ExecuteAsync(new CreateUserCommand
            {
                Username = "jdupont", FirstName = "Jean", LastName = "Dupont",
                Role = UserRole.Optician, IsActive = true, Password = "S3cretPass1"
            });
        }

        result.UsernameTaken.Should().BeFalse();
        result.UserId.Should().BeGreaterThan(0);

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.Username.Should().Be("jdupont");
        user.PasswordHash.Should().Be("HASHED::S3cretPass1");
        user.NormalizedUsername.Should().Be("jdupont");
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_DuplicateUsername_ReturnsTaken_NoWrite()
    {
        var dbPath = PathFor("create-dup.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await new UserRepository(context).CreateAsync(new User { Username = "taken", NormalizedUsername = UserIdentityPolicy.NormalizeUsername("taken"), FirstName = "A", LastName = "B", PasswordHash = "x" });
            await new UnitOfWork(context).SaveChangesAsync();
        }

        using var ctx = CreateContext(dbPath);
        var useCase = new CreateUserUseCase(new UserRepository(ctx), new UnitOfWork(ctx), new FakeAuth(), new EfTransactionRunner(ctx));
        var result = await useCase.ExecuteAsync(new CreateUserCommand { Username = "taken", FirstName = "C", LastName = "D", Role = UserRole.Optician, Password = "S3cretPass1" });

        result.UsernameTaken.Should().BeTrue();
        ctx.Users.AsNoTracking().Count().Should().Be(1);
    }

    [Fact]
    public async Task Update_ChangesFields_AndRehashesOnlyWhenPasswordProvided()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new UserRepository(context).CreateAsync(new User { Username = "old", NormalizedUsername = UserIdentityPolicy.NormalizeUsername("old"), FirstName = "O", LastName = "L", PasswordHash = "KEEP", Role = UserRole.Technician });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new UpdateUserUseCase(new UserRepository(context), new UnitOfWork(context), new FakeAuth(), new EfTransactionRunner(context));
            var result = await useCase.ExecuteAsync(new UpdateUserCommand { UserId = id, Username = "new", FirstName = "N", LastName = "W", Role = UserRole.Admin, IsActive = false, Password = null });
            result.UserFound.Should().BeTrue();
            result.UsernameTaken.Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        var user = verify.Users.AsNoTracking().Single();
        user.Username.Should().Be("new");
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeFalse();
        user.PasswordHash.Should().Be("KEEP", "mot de passe non fourni ⇒ hash inchangé");
    }

    [Fact]
    public async Task Update_MissingUser_ReturnsNotFound()
    {
        var dbPath = PathFor("update-missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new UpdateUserUseCase(new UserRepository(context), new UnitOfWork(context), new FakeAuth(), new EfTransactionRunner(context));
        var result = await useCase.ExecuteAsync(new UpdateUserCommand { UserId = 404, Username = "xyz", FirstName = "x", LastName = "x", Role = UserRole.Optician });
        result.UserFound.Should().BeFalse();
    }

    [Fact]
    public async Task SetActive_TogglesState()
    {
        var dbPath = PathFor("setactive.db");
        EnsureSchema(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var created = await new UserRepository(context).CreateAsync(new User { Username = "usr", NormalizedUsername = UserIdentityPolicy.NormalizeUsername("usr"), FirstName = "F", LastName = "L", PasswordHash = "h", IsActive = true });
            await new UnitOfWork(context).SaveChangesAsync();
            id = created.UserId;
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new SetUserActiveUseCase(new UserRepository(context), new UnitOfWork(context));
            var result = await useCase.ExecuteAsync(new SetUserActiveCommand { UserId = id, IsActive = false });
            result.UserFound.Should().BeTrue();
            result.IsActive.Should().BeFalse();
        }

        using var verify = CreateContext(dbPath);
        verify.Users.AsNoTracking().Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SetActive_MissingUser_ReturnsNotFound()
    {
        var dbPath = PathFor("setactive-missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new SetUserActiveUseCase(new UserRepository(context), new UnitOfWork(context));
        var result = await useCase.ExecuteAsync(new SetUserActiveCommand { UserId = 77, IsActive = true });
        result.UserFound.Should().BeFalse();
    }

    [Fact]
    public async Task NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var create = new CreateUserUseCase(new UserRepository(context), new UnitOfWork(context), new FakeAuth(), new EfTransactionRunner(context));
        var update = new UpdateUserUseCase(new UserRepository(context), new UnitOfWork(context), new FakeAuth(), new EfTransactionRunner(context));
        var setActive = new SetUserActiveUseCase(new UserRepository(context), new UnitOfWork(context));

        await ((Func<Task>)(() => create.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => update.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => setActive.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullDependencies()
    {
        ((Action)(() => _ = new CreateUserUseCase(null!, null!, null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new UpdateUserUseCase(null!, null!, null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SetUserActiveUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
    }
}
