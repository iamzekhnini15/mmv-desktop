using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

public class DbContextTests
{
    [Fact]
    public async Task ShouldCreateDatabaseAndSeedAdmin()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new OpticDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var admin = await context.Users.SingleOrDefaultAsync(u => u.UserId == 1);

        Assert.NotNull(admin);
        Assert.Equal("admin", admin!.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.IsActive);
    }
}
