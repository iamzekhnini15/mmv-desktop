using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Une base migrée neuve PAR TEST (xunit instancie la classe pour chaque test) : aucun test ne dépend de
/// l'ordre d'exécution ni d'un état laissé par un autre (ADR-PROD-DB-008 §5.6).
/// </summary>
public abstract class PostgreSqlTestBase : IAsyncLifetime
{
    protected PostgreSqlDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync() => Database = await PostgreSqlDatabase.CreateAsync();

    public async Task DisposeAsync() => await Database.DisposeAsync();

    /// <summary>
    /// Contexte neuf. Les primitives atomiques passent par <c>ExecuteUpdate</c>/SQL brut et ne mettent pas le
    /// change tracker à jour : toute relecture se fait donc dans un contexte neuf.
    /// </summary>
    protected OpticDbContext NewContext() => Database.CreateContext();
}
