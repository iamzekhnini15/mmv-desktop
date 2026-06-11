using FluentAssertions;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1A — chemin de base unique, résolution configurable par environnement et testable.
/// </summary>
public class SqliteDatabasePathResolverTests
{
    [Fact]
    public void ResolveDatabasePath_Default_UsesLocalAppDataManageMyVision()
    {
        var path = SqliteDatabasePathResolver.ResolveDatabasePath(
            explicitPath: null,
            environment: new Dictionary<string, string?>());

        path.Should().EndWith(Path.Combine("ManageMyVision", "mmv.db"));
        path.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    [Fact]
    public void ResolveDatabasePath_EnvironmentVariable_OverridesDefault()
    {
        var custom = Path.Combine(Path.GetTempPath(), "mmv-env-test", "custom.db");
        var env = new Dictionary<string, string?>
        {
            [SqliteDatabasePathResolver.EnvironmentVariableName] = custom
        };

        var path = SqliteDatabasePathResolver.ResolveDatabasePath(environment: env);

        path.Should().Be(Path.GetFullPath(custom));
    }

    [Fact]
    public void ResolveDatabasePath_ExplicitPath_TakesPrecedenceOverEnvironment()
    {
        var explicitPath = Path.Combine(Path.GetTempPath(), "explicit.db");
        var env = new Dictionary<string, string?>
        {
            [SqliteDatabasePathResolver.EnvironmentVariableName] = Path.Combine(Path.GetTempPath(), "env.db")
        };

        var path = SqliteDatabasePathResolver.ResolveDatabasePath(explicitPath, env);

        path.Should().Be(Path.GetFullPath(explicitPath));
    }

    [Fact]
    public void ResolveDatabasePath_ConfiguredConnectionString_ExtractsDataSource()
    {
        var configured = @"Data Source=C:\data\optic.db";

        var path = SqliteDatabasePathResolver.ResolveDatabasePath(
            environment: new Dictionary<string, string?>(),
            configuredConnectionString: configured);

        path.Should().Be(Path.GetFullPath(@"C:\data\optic.db"));
    }

    [Fact]
    public void GetConnectionString_BuildsDataSourceConnectionString()
    {
        var explicitPath = Path.Combine(Path.GetTempPath(), "conn.db");

        var connectionString = SqliteDatabasePathResolver.GetConnectionString(explicitPath);

        connectionString.Should().Be($"Data Source={Path.GetFullPath(explicitPath)}");
    }

    [Fact]
    public void EnsureDirectoryExists_CreatesMissingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mmv-dir-test-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(directory, "mmv.db");
        try
        {
            Directory.Exists(directory).Should().BeFalse();

            SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);

            Directory.Exists(directory).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureDirectoryExists_InMemory_IsNoOp()
    {
        var act = () => SqliteDatabasePathResolver.EnsureDirectoryExists(":memory:");

        act.Should().NotThrow();
    }
}
