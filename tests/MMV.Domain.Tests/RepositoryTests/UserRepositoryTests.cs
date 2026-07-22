using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using System.Linq;
using Xunit;

namespace MMV.Domain.Tests.RepositoryTests;

public class UserRepositoryTests
{
    private static OpticDbContext CreateContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new OpticDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistUser()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var repository = new UserRepository(context);

        var user = new User
        {
            Username = "optician",
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername("optician"),
            PasswordHash = "hash",
            FirstName = "Opti",
            LastName = "Cian",
            Role = UserRole.Optician,
            IsActive = true
        };

        await repository.CreateAsync(user);
        await context.SaveChangesAsync();

        var saved = await repository.GetByNormalizedUsernameAsync("optician");

        Assert.NotNull(saved);
        Assert.Equal(UserRole.Optician, saved!.Role);
    }

    [Fact]
    public async Task GetActiveUsersAsync_ShouldReturnOnlyActive()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var repository = new UserRepository(context);

        var active = new User
        {
            Username = "active",
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername("active"),
            PasswordHash = "hash",
            FirstName = "Active",
            LastName = "User",
            Role = UserRole.Optician,
            IsActive = true
        };

        var inactive = new User
        {
            Username = "inactive",
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername("inactive"),
            PasswordHash = "hash",
            FirstName = "Inactive",
            LastName = "User",
            Role = UserRole.Technician,
            IsActive = false
        };

        await repository.CreateAsync(active);
        await repository.CreateAsync(inactive);
        await context.SaveChangesAsync();

        var result = await repository.GetActiveUsersAsync();

        // CreateContext n'effectue qu'EnsureCreated (aucun seed) : seul l'utilisateur actif
        // créé explicitement par ce test doit être retourné.
        Assert.Single(result);
        Assert.All(result, u => Assert.True(u.IsActive));
        Assert.DoesNotContain(result, u => u.Username == "inactive");
    }

    [Fact]
    public async Task GetByRoleAsync_ShouldFilterByRole()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var repository = new UserRepository(context);

        var optician = new User
        {
            Username = "optician",
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername("optician"),
            PasswordHash = "hash",
            FirstName = "Opti",
            LastName = "Cian",
            Role = UserRole.Optician,
            IsActive = true
        };

        var technician = new User
        {
            Username = "technician",
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername("technician"),
            PasswordHash = "hash",
            FirstName = "Tech",
            LastName = "Guy",
            Role = UserRole.Technician,
            IsActive = true
        };

        await repository.CreateAsync(optician);
        await repository.CreateAsync(technician);
        await context.SaveChangesAsync();

        var result = await repository.GetByRoleAsync(UserRole.Optician);

        Assert.Single(result);
        Assert.Equal("optician", result.First().Username);
    }
}
