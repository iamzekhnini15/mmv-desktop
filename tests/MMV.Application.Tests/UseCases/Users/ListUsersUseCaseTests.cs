using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Users.ListUsers;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Users;

/// <summary>
/// P2D-1 — Query use case « Lister les utilisateurs ». Vérifie, sur un <b>vrai SQLite temporaire</b>, la projection
/// entité → <see cref="UserListItemDto"/> (aucune entité EF renvoyée), le cas vide et les gardes.
/// </summary>
public sealed class ListUsersUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListUsersUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d-listusers-" + Guid.NewGuid().ToString("N"));
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
    public async Task Execute_ReturnsProjectedDtos_ForAllUsers()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await new UserRepository(context).CreateAsync(new User { Username = "jdupont", NormalizedUsername = UserIdentityPolicy.NormalizeUsername("jdupont"), FirstName = "Jean", LastName = "Dupont", Role = UserRole.Optician, IsActive = true, PasswordHash = "h" });
            await new UserRepository(context).CreateAsync(new User { Username = "amartin", NormalizedUsername = UserIdentityPolicy.NormalizeUsername("amartin"), FirstName = "Alice", LastName = "Martin", Role = UserRole.Admin, IsActive = false, PasswordHash = "h" });
            await new UnitOfWork(context).SaveChangesAsync();
        }

        IReadOnlyList<UserListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListUsersUseCase(new UserRepository(context));
            result = await useCase.ExecuteAsync(new ListUsersQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<UserListItemDto>();
        result.Should().ContainSingle(u => u.Username == "jdupont" && u.FirstName == "Jean" && u.Role == UserRole.Optician && u.IsActive);
        result.Should().ContainSingle(u => u.Username == "amartin" && !u.IsActive);
    }

    [Fact]
    public async Task Execute_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new ListUsersUseCase(new UserRepository(context));
        var result = await useCase.ExecuteAsync(new ListUsersQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new ListUsersUseCase(new UserRepository(context));
        await ((Func<Task>)(() => useCase.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListUsersUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
