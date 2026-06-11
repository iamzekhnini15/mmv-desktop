using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

public class DbContextTests
{
    private static OpticDbContext CreateInMemoryContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connection)
            .Options;

        return new OpticDbContext(options);
    }

    /// <summary>
    /// EnsureCreated ne crée que le schéma : aucune donnée n'est seedée par le modèle EF
    /// (il n'y a pas de HasData pour les utilisateurs). Le seed applicatif vit dans
    /// DbInitializer.Initialize, testé séparément ci-dessous.
    /// </summary>
    [Fact]
    public async Task EnsureCreated_ShouldCreateSchema_WithoutSeedingUsers()
    {
        using var context = CreateInMemoryContext(out var connection);
        await using var _ = connection;

        await context.Database.EnsureCreatedAsync();

        // Le schéma est interrogeable...
        var userCount = await context.Users.CountAsync();
        // ...mais EnsureCreated ne seede aucun utilisateur (pas de HasData).
        Assert.Equal(0, userCount);
    }

    /// <summary>
    /// Comportement de DbInitializer.Initialize : le seed impératif crée l'administrateur
    /// comme premier utilisateur (UserId == 1). Ce test exerce explicitement l'initialiseur,
    /// sans présumer qu'EnsureCreated le ferait.
    /// </summary>
    [Fact]
    public async Task DbInitializer_Initialize_ShouldSeedAdminUser()
    {
        using var context = CreateInMemoryContext(out var connection);
        await using var _ = connection;

        DbInitializer.Initialize(context);

        var admin = await context.Users.SingleOrDefaultAsync(u => u.UserId == 1);

        Assert.NotNull(admin);
        Assert.Equal("admin", admin!.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
    }
}
