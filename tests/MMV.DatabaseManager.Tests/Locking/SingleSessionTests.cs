using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager.Locking;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests.Locking;

/// <summary>
/// P4-6B — décision d'architecte du 04/10/2026 : le verrou consultatif et <c>Migrate()</c> partagent
/// <b>une seule</b> session PostgreSQL. Si elle meurt, la migration meurt avec elle ; EF ne peut jamais la
/// remplacer silencieusement par une session sans verrou. Sans serveur : hôte du domaine réservé <c>.invalid</c>.
/// </summary>
public sealed class SingleSessionTests
{
    private static void Configure(DbContextOptionsBuilder<OpticDbContext> builder) =>
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = "Host=mmv-single-session.invalid;Database=mmv;Username=x;Password=y;Timeout=2"
        });

    [Fact]
    public async Task Session_builds_exactly_one_context_for_lock_metadata_journal_and_migration()
    {
        var configured = 0;

        await using var session = PostgreSqlMigrationSession.Create(b => { configured++; Configure(b); },
            MigrationRunner.DefaultLockTimeout);

        configured.Should().Be(1, "une seule session PostgreSQL porte toute la séquence");
    }

    [Fact]
    public async Task Migrator_never_opens_the_session_itself()
    {
        await using var session = PostgreSqlMigrationSession.Create(Configure, MigrationRunner.DefaultLockTimeout);

        var act = () => session.Ports.Migrator.GetAppliedAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("verrou", "la session est ouverte par le verrou, jamais par EF");
    }

    [Fact]
    public async Task EF_initiated_open_is_refused_before_any_network_contact()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        Configure(builder);
        builder.AddInterceptors(new SingleSessionGuard());
        await using var context = new OpticDbContext(builder.Options);

        var act = () => context.Database.OpenConnectionAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("verrou");
    }
}
