using System.Reflection;
using FluentAssertions;
using MMV.DatabaseManager.Locking;
using MMV.Domain.Tests.TestDoubles;

namespace MMV.DatabaseManager.Tests.Locking;

/// <summary>
/// P4-6B (DP-2, LK-1 ; §3.3 du plan) — verrou consultatif de session. U-C6 : la clé est la constante attendue,
/// déclarée en un seul point ; l'attente est bornée ; la libération est explicite.
/// </summary>
public sealed class PostgreSqlAdvisoryMigrationLockTests
{
    /// <summary>« MMVMIGR1 » en ASCII. Ne change JAMAIS : deux versions de l'outil doivent s'exclure.</summary>
    private const long ExpectedKey = 0x4D4D564D49475231;

    [Fact]
    public void Lock_key_is_the_frozen_constant()
    {
        PostgreSqlAdvisoryMigrationLock.LockKey.Should().Be(ExpectedKey);
    }

    [Fact]
    public void Lock_key_is_declared_in_a_single_place()
    {
        var declarations = typeof(PostgreSqlAdvisoryMigrationLock).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.IsLiteral && f.FieldType == typeof(long) && (long)f.GetRawConstantValue()! == ExpectedKey)
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}");

        declarations.Should().Equal("PostgreSqlAdvisoryMigrationLock.LockKey");
    }

    [Fact]
    public async Task Acquisition_uses_try_lock_with_the_key_as_bound_parameter()
    {
        var connection = new ScriptedDbConnection(_ => true);
        await using var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection);

        (await migrationLock.TryAcquireAsync(TimeSpan.Zero)).Should().BeTrue();

        var command = connection.Executed.Single();
        command.Text.Should().Be("SELECT pg_try_advisory_lock(@key)");
        command.Parameters["key"].Should().Be(ExpectedKey);
        connection.OpenCount.Should().Be(1, "la connexion du verrou est dédiée et tenue ouverte");
    }

    [Fact]
    public async Task Busy_lock_is_retried_until_the_bounded_wait_then_refused()
    {
        var connection = new ScriptedDbConnection(c => c.Text.Contains("pg_try_advisory_lock") ? false : null);
        await using var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection, TimeSpan.FromMilliseconds(20));

        var acquired = await migrationLock.TryAcquireAsync(TimeSpan.FromMilliseconds(150));

        acquired.Should().BeFalse("jamais d'attente infinie : l'outil le dit, il ne se fige pas");
        connection.Executed.Count(c => c.Text.Contains("pg_try_advisory_lock")).Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Release_unlocks_explicitly_then_closes()
    {
        var connection = new ScriptedDbConnection(_ => true);
        var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection);
        await migrationLock.TryAcquireAsync(TimeSpan.Zero);

        await migrationLock.ReleaseAsync();

        connection.Executed.Last().Text.Should().Be("SELECT pg_advisory_unlock(@key)");
        connection.Executed.Last().Parameters["key"].Should().Be(ExpectedKey);
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task Dispose_releases_a_held_lock()
    {
        var connection = new ScriptedDbConnection(_ => true);
        await using (var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection))
        {
            await migrationLock.TryAcquireAsync(TimeSpan.Zero);
        }

        connection.Executed.Should().Contain(c => c.Text.Contains("pg_advisory_unlock"));
    }

    [Fact]
    public async Task Release_without_acquisition_issues_no_unlock()
    {
        var connection = new ScriptedDbConnection(_ => true);
        var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection);

        await migrationLock.ReleaseAsync();

        connection.Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task Holder_is_searched_in_the_current_database_only()
    {
        // Les verrous consultatifs sont propres à chaque base : un détenteur dans une autre base MMV du même
        // serveur n'est pas « l'autre exécution ».
        var connection = new ScriptedDbConnection(_ => new System.Data.DataTable());
        connection.Open();
        await using var migrationLock = new PostgreSqlAdvisoryMigrationLock(connection);

        (await migrationLock.DescribeHolderAsync()).Should().BeNull();

        connection.Executed.Single().Text.Should().Contain("l.database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())");
    }

    [Fact]
    public void Holder_query_targets_the_advisory_key_halves()
    {
        PostgreSqlAdvisoryMigrationLock.KeyHigh.Should().Be(0x4D4D564D);
        PostgreSqlAdvisoryMigrationLock.KeyLow.Should().Be(0x49475231);
    }
}
