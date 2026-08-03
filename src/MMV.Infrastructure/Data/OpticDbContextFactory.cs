using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MMV.Infrastructure.Configuration;

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

        // P4-3 : le design-time reste VOLONTAIREMENT figé sur SQLite et ne lit ni MMV_DATABASE_PROVIDER
        // ni MMV_DATABASE_CONNECTION_STRING. La chaîne de migrations existante est SQLite, et la CI exécute
        // `has-pending-model-changes` sans configuration serveur. Le passage design-time à PostgreSQL est
        // reporté à la chaîne de migrations serveur (P4-5).
        DatabaseProviderResolver.Configure(optionsBuilder, DatabaseProviderOptions.Sqlite, dbPath);

        return new OpticDbContext(optionsBuilder.Options);
    }
}
