using FluentAssertions;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;
using MMV.Infrastructure.Configuration;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-9 — verbes <c>backup</c> et <c>verify-backup</c> sans serveur : options strictes, secrets jamais en argument
/// ni restitués, contrôle structurel avant tout contact serveur, environnement des outils clients durci.
/// </summary>
public sealed class BackupCommandTests
{
    private const string Secret = "unit-Backup-Secret-0123456789abcd";

    private static async Task<(int Code, string All)> Run(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-dbm-backup-{Guid.NewGuid():N}.log");
        try
        {
            var code = await Program.RunAsync(args, new Dictionary<string, string?>(), output, error, trace, new StringReader(stdin));
            var traced = File.Exists(trace) ? await File.ReadAllTextAsync(trace) : string.Empty;
            return (code, output + "\n" + error + "\n" + traced);
        }
        finally
        {
            File.Delete(trace);
        }
    }

    private static string[] Backup(string outputDirectory) =>
    [
        "backup", "--host", "mmv-server.invalid", "--database", "mmv", "--username", "mmv_backup",
        "--output-directory", outputDirectory, "--operator", "OP"
    ];

    [Fact]
    public async Task Backup_without_its_secret_on_stdin_exits_10()
    {
        var (code, all) = await Run(string.Empty, Backup(Path.GetTempPath()));

        code.Should().Be(10);
        all.Should().Contain("Secret manquant");
    }

    [Fact]
    public async Task Backup_to_an_unreachable_server_exits_20_without_any_file_and_without_echoing_the_secret()
    {
        var directory = Directory.CreateTempSubdirectory("mmv-backup-unreachable-").FullName;
        try
        {
            var (code, all) = await Run(Secret, Backup(directory));

            code.Should().Be(20);
            all.Should().NotContain(Secret);
            Directory.GetFiles(directory).Should().BeEmpty("un échec ne laisse aucun fichier");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_into_a_missing_directory_exits_10()
    {
        (await Run(Secret, Backup(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}")))).Code.Should().Be(10);
    }

    [Theory]
    [InlineData("--output-directory", "relative/dir")]
    [InlineData("--pg-bin", "bin")]
    public async Task Relative_paths_are_refused(string option, string value)
    {
        var args = Backup(Path.GetTempPath()).ToList();
        if (option == "--output-directory")
        {
            args[args.IndexOf("--output-directory") + 1] = value;
        }
        else
        {
            args.AddRange([option, value]);
        }

        var (code, all) = await Run(Secret, args.ToArray());

        code.Should().Be(10);
        all.Should().Contain("chemin absolu");
    }

    [Fact]
    public async Task Secret_is_never_accepted_as_an_argument()
    {
        var (code, _) = await Run(Secret, [.. Backup(Path.GetTempPath()), "--password", Secret]);

        code.Should().Be(10);
    }

    [Fact]
    public async Task Verify_of_an_absent_manifest_exits_22_before_any_server_contact()
    {
        // Hôte .invalid : un contact donnerait 20. Le code 22 prouve le contrôle structurel d'abord.
        var (code, all) = await Run(Secret,
            "verify-backup", "--host", "mmv-server.invalid", "--admin-user", "postgres",
            "--manifest", Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}{BackupFiles.ManifestSuffix}"),
            "--operator", "OP");

        code.Should().Be(22);
        all.Should().Contain("NON vérifiée").And.NotContain(Secret);
    }

    [Fact]
    public async Task Verify_of_a_corrupted_backup_exits_22_before_any_server_contact_and_its_old_proof_is_worthless()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await File.AppendAllTextAsync(backup.DumpPath, "x");

        var (code, all) = await Run(Secret,
            "verify-backup", "--host", "mmv-server.invalid", "--admin-user", "postgres",
            "--manifest", backup.ManifestPath, "--operator", "OP");

        code.Should().Be(22);
        all.Should().Contain("altéré ou incomplet");
        (await new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration)
                .VerifyAsync(backup.ManifestPath, CancellationToken.None))
            .IsVerified.Should().BeFalse("la preuve antérieure désigne un autre fichier : migrate la refuse");
    }

    [Theory]
    [InlineData("mmv", "jamais la base sauvegardée")]
    [InlineData("postgres", "jamais la base sauvegardée")]
    [InlineData("template1", "jamais la base sauvegardée")]
    [InlineData("Upper", "minuscules")]
    [InlineData("1abc", "minuscules")]
    [InlineData("a-b", "minuscules")]
    public async Task Invalid_scratch_database_is_refused_before_any_server_contact(string scratch, string because)
    {
        using var backup = await TemporaryBackup.CreateAsync();

        var (code, all) = await Run(Secret,
            "verify-backup", "--host", "mmv-server.invalid", "--admin-user", "postgres",
            "--manifest", backup.ManifestPath, "--operator", "OP", "--scratch-database", scratch);

        code.Should().Be(10);
        all.Should().Contain(because);
    }

    [Theory]
    [InlineData("--host", "h")]
    [InlineData("--database", "d")]
    [InlineData("--port", "5432")]
    public async Task Backup_with_a_partial_explicit_connection_is_refused(string option, string value)
    {
        var (code, all) = await Run(Secret, "backup", "--output-directory", Path.GetTempPath(), "--operator", "OP", option, value);

        code.Should().Be(10);
        all.Should().Contain("vont ensemble");
    }

    [Fact]
    public async Task Restore_of_a_backup_without_proof_exits_22_before_any_server_contact_and_creates_nothing()
    {
        using var backup = await TemporaryBackup.CreateAsync(withProof: false);

        var (code, all) = await Run(Secret,
            "restore-backup", "--host", "mmv-server.invalid", "--admin-user", "postgres", "--manifest", backup.ManifestPath,
            "--target-database", "mmv_restored", "--migrator-role", "mmv_migrator", "--operator", "OP");

        code.Should().Be(22);
        all.Should().Contain("seule une sauvegarde vérifiée est restaurée").And.NotContain(Secret);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("template0")]
    [InlineData("Mmv")]
    public async Task Restore_target_must_be_a_new_ordinary_database_name(string target)
    {
        using var backup = await TemporaryBackup.CreateAsync();

        var (code, _) = await Run(Secret,
            "restore-backup", "--host", "mmv-server.invalid", "--admin-user", "postgres", "--manifest", backup.ManifestPath,
            "--target-database", target, "--migrator-role", "mmv_migrator", "--operator", "OP");

        code.Should().Be(10);
    }

    [Fact]
    public void Restore_requires_a_target_and_the_migrator_owner()
    {
        AdministrationOptions.Parse(["restore-backup", "--host", "h", "--admin-user", "u", "--manifest", "C:\\m.manifest.json", "--operator", "o"])
            .Error.Should().Contain("--target-database").And.Contain("--migrator-role");
    }

    [Fact]
    public void Parsing_requires_every_backup_option()
    {
        AdministrationOptions.Parse(["backup", "--host", "h", "--database", "d", "--username", "u", "--operator", "o"])
            .Error.Should().Contain("--output-directory");
        AdministrationOptions.Parse(["verify-backup", "--host", "h", "--admin-user", "u", "--operator", "o"])
            .Error.Should().Contain("--manifest");
    }

    [Fact]
    public void Client_tools_get_a_hardened_environment_and_never_the_secret_as_an_argument()
    {
        var previous = (Environment.GetEnvironmentVariable("PGSSLMODE"), Environment.GetEnvironmentVariable("PGSERVICE"),
            Environment.GetEnvironmentVariable("PGPASSFILE"));
        Environment.SetEnvironmentVariable("PGSSLMODE", "disable");
        Environment.SetEnvironmentVariable("PGSERVICE", "weak");
        Environment.SetEnvironmentVariable("PGPASSFILE", "C:\\pgpass");
        try
        {
            var info = PostgreSqlClientTools.StartInfo("pg_dump", ["--format=custom"],
                new PostgreSqlConnectionSettings("db.example", 5432, "mmv", "mmv_backup", Secret, null));

            info.UseShellExecute.Should().BeFalse();
            info.ArgumentList.Should().Equal("--format=custom");
            info.Environment["PGSSLMODE"].Should().Be("verify-full");
            info.Environment["PGSSLROOTCERT"].Should().Be("system");
            info.Environment["PGREQUIREAUTH"].Should().Be("scram-sha-256");
            info.Environment.Should().NotContainKey("PGSERVICE").And.NotContainKey("PGPASSFILE");
            info.Environment["PGPASSWORD"].Should().Be(Secret, "le secret ne passe que par l'environnement du processus enfant");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGSSLMODE", previous.Item1);
            Environment.SetEnvironmentVariable("PGSERVICE", previous.Item2);
            Environment.SetEnvironmentVariable("PGPASSFILE", previous.Item3);
        }
    }

    [Fact]
    public void Client_tools_require_an_absolute_binary_directory()
    {
        var act = () => new PostgreSqlClientTools("bin");
        act.Should().Throw<ArgumentException>();
        new PostgreSqlClientTools(null).Executable("pg_dump").Should().Be("pg_dump");
    }
}
