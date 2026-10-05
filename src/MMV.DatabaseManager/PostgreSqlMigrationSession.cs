using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Locking;
using MMV.DatabaseManager.Permissions;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>
/// Composition PostgreSQL des ports de <see cref="MigrationRunner"/> sur <b>une seule</b> session
/// (décision d'architecte du 04/10/2026, amendement de §3.3) : verrou consultatif, métadonnée, GRANT, marqueur,
/// journal, <c>Migrate()</c> et vérification passent par la même connexion, ouverte par l'acquisition du verrou
/// et tenue ouverte jusqu'à sa libération. Si cette session meurt, la migration meurt avec elle :
/// <see cref="SingleSessionGuard"/> interdit à EF de la rouvrir, et les composants de l'outil ne la rouvrent
/// jamais. Les écritures de journal restent hors des transactions de migration : elles s'exécutent avant et
/// après <c>Migrate()</c>, en validation automatique.
/// </summary>
public sealed class PostgreSqlMigrationSession : IAsyncDisposable
{
    private readonly OpticDbContext _context;
    private readonly DbConnection _connection;

    private PostgreSqlMigrationSession(OpticDbContext context, TimeSpan lockTimeout)
    {
        _context = context;
        _connection = context.Database.GetDbConnection();

        var migrator = new EfSchemaMigrator(context, lockTimeout);
        Ports = new MigrationPorts(
            new PostgreSqlAdvisoryMigrationLock(_connection),
            migrator,
            new CompatibilityMetadataWriter(_connection),
            new ServerMigrationJournal(_connection),
            new ApplicationRoleGrants(_connection),
            new ServerSchemaVerification(migrator, _connection),
            new DatabaseFingerprintReader(_connection));
    }

    public MigrationPorts Ports { get; }

    /// <summary>Connexion de contrôle de la session — celle qui tient le verrou (P4-7 : transaction d'import).</summary>
    public DbConnection Connection => _connection;

    /// <summary>Modèle de conception PostgreSQL (tables, types physiques, données <c>HasData</c>) — plan d'import P4-7.</summary>
    public IModel DesignTimeModel => _context.GetService<IDesignTimeModel>().Model;

    /// <param name="configure">Configuration PostgreSQL du contexte (chaîne du rôle migrateur).</param>
    /// <param name="lockTimeout"><c>lock_timeout</c> de la session (CX-1).</param>
    public static PostgreSqlMigrationSession Create(
        Action<DbContextOptionsBuilder<OpticDbContext>> configure, TimeSpan lockTimeout)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        configure(builder);
        builder.AddInterceptors(new SingleSessionGuard());

        var context = new OpticDbContext(builder.Options);
        try
        {
            return new PostgreSqlMigrationSession(context, lockTimeout);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>Ouvre la session pour le diagnostic <c>status</c>, sans prendre le verrou.</summary>
    public async Task OpenForStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Ports.Lock.DisposeAsync();
        await _context.DisposeAsync();
    }
}
