using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Factory pour la création du DbContext au moment du design (migrations).
/// </summary>
public class OpticDbContextFactory : IDesignTimeDbContextFactory<OpticDbContext>
{
    public OpticDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OpticDbContext>();

        // Chemin unique résolu par SqliteDatabasePathResolver (P2A-1A).
        var dbPath = SqliteDatabasePathResolver.ResolveDatabasePath();
        SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);
        optionsBuilder.UseSqlite(SqliteDatabasePathResolver.GetConnectionString(dbPath));

        return new OpticDbContext(optionsBuilder.Options);
    }
}
