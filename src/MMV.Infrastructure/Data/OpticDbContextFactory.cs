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
        
        // Localisation par défaut : %LOCALAPPDATA%\ManageMyVision\mmv.db
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ManageMyVision",
            "mmv.db"
        );

        // Créer le dossier s'il n'existe pas
        var directory = Path.GetDirectoryName(dbPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new OpticDbContext(optionsBuilder.Options);
    }
}
