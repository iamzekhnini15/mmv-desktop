using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Provisioning;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

/// <summary>
/// P4-8 (D-12) — premier administrateur créé par la procédure d'installation, une seule fois, secret jamais en
/// clair ; puis authentifié par le service du produit sous le <b>rôle applicatif</b> d'un poste.
/// </summary>
public sealed class BootstrapAdministratorTests : IAsyncLifetime
{
    private const string InitialSecret = "Initial-Admin-2026";

    private readonly TlsServer _server = new();
    private ProvisioningRequest _request = null!;

    public async Task InitializeAsync()
    {
        _request = _server.Request(TlsServer.Suffix());
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    public Task DisposeAsync() => _server.DropAsync(_request);

    private string Migrator => _server.ConnectionString(_request.Database, _request.MigratorRole, _request.MigratorPassword);
    private string App => _server.ConnectionString(_request.Database, _request.AppRole, _request.AppPassword);

    private Task<AdministrationResult> BootstrapAsync(IMigrationJournal? trace = null, string username = "patron") =>
        new BootstrapAdministrator(trace ?? new MigrationJournal())
            .RunAsync(Migrator, username, InitialSecret, InitialSecret, "OP-IT");

    private async Task MigratedAsync() =>
        (await _server.MigrateAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);

    [PostgreSqlTlsFact]
    public async Task Refused_while_the_schema_is_not_initialized()
    {
        var result = await BootstrapAsync();

        result.ExitCode.Should().Be(MigrationExitCode.BootstrapRefused);
        result.Message.Should().Contain("migrate");
    }

    [PostgreSqlTlsFact]
    public async Task Creates_one_active_administrator_whose_secret_is_only_a_bcrypt_hash()
    {
        await MigratedAsync();
        var trace = new MigrationJournal();

        var result = await BootstrapAsync(trace);

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        await using var context = _server.Context(Migrator);
        var user = await context.Users.SingleAsync();
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeTrue();
        user.NormalizedUsername.Should().Be("patron");
        user.PasswordHash.Should().StartWith("$2").And.NotContain(InitialSecret);
        BCrypt.Net.BCrypt.Verify(InitialSecret, user.PasswordHash).Should().BeTrue();
        string.Join('\n', trace.Entries).Should().NotContain(InitialSecret);
        result.Message.Should().NotContain(InitialSecret);
    }

    [PostgreSqlTlsFact]
    public async Task Is_one_shot_a_second_run_is_refused_and_writes_nothing()
    {
        await MigratedAsync();
        (await BootstrapAsync()).ExitCode.Should().Be(MigrationExitCode.Success);

        var again = await BootstrapAsync(username: "intrus");

        again.ExitCode.Should().Be(MigrationExitCode.BootstrapRefused);
        await using var context = _server.Context(Migrator);
        (await context.Users.CountAsync()).Should().Be(1);
        (await context.Users.AnyAsync(u => u.NormalizedUsername == "intrus")).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Concurrent_runs_create_exactly_one_administrator()
    {
        await MigratedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => BootstrapAsync(username: $"admin{i}")));

        results.Count(r => r.ExitCode == MigrationExitCode.Success).Should().Be(1);
        results.Count(r => r.ExitCode == MigrationExitCode.BootstrapRefused).Should().Be(3);
        await using var context = _server.Context(Migrator);
        (await context.Users.CountAsync()).Should().Be(1);
    }

    [PostgreSqlTlsFact]
    public async Task Administrator_authenticates_from_a_workstation_through_the_application_role()
    {
        await MigratedAsync();
        (await BootstrapAsync()).ExitCode.Should().Be(MigrationExitCode.Success);

        await using var context = _server.Context(App);
        var authentication = new AuthenticationService(new UnitOfWork(context));

        (await authentication.AuthenticateAsync("PATRON", InitialSecret)).Should().NotBeNull();
        (await authentication.AuthenticateAsync("patron", "Wrong-Secret-1")).Should().BeNull();
    }

    [PostgreSqlTlsTheory]
    [InlineData("weak", "weak")]
    [InlineData(InitialSecret, "Different-Secret-1")]
    public async Task Weak_or_unconfirmed_secret_is_refused_before_any_write(string secret, string confirmation)
    {
        await MigratedAsync();

        var result = await new BootstrapAdministrator(new MigrationJournal())
            .RunAsync(Migrator, "patron", secret, confirmation, "OP-IT");

        result.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        await using var context = _server.Context(Migrator);
        (await context.Users.AnyAsync()).Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Through_the_real_entry_point_reads_the_secret_from_stdin_and_never_echoes_it()
    {
        await MigratedAsync();
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-it-bootstrap-{Guid.NewGuid():N}.log");

        int code;
        string traced;
        try
        {
            code = await Program.RunAsync(["bootstrap-admin", "--username", "patron", "--operator", "OP-IT"],
                new Dictionary<string, string?> { ["MMV_MIGRATOR_CONNECTION_STRING"] = Migrator },
                output, error, trace, new StringReader($"{InitialSecret}\n{InitialSecret}\n"));
            traced = await File.ReadAllTextAsync(trace);
        }
        finally
        {
            File.Delete(trace);
        }

        code.Should().Be(0, error.ToString());
        (output + error.ToString() + traced).Should().NotContain(InitialSecret).And.NotContain(_request.MigratorPassword);
    }
}
