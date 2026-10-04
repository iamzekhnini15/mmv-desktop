using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Journal;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// P4-6B — fenêtre N-1 et minimum calculé, de bout en bout sur un vrai serveur (S-4 ; H14, H17, Q-22).
/// I-7 (N-1 écrit, N-2 et plus récent bloqués), I-9 (exemple de référence du §4.3), I-10 (adoption).
/// </summary>
public sealed class LifecycleWindowTests : LifecycleTestBase
{
    private async Task<ServerSchemaVerdict> WorkstationVerdictAsync<TChain>(string version) where TChain : ILifecycleChain, new()
    {
        await using var context = Db.Context<TChain>(Db.AppConnectionString);
        return await ServerSchemaCompatibilityGuard.EvaluateAsync(
            context.Database.GetDbConnection(), context.Database.GetMigrations(), version);
    }

    private async Task<(string? Schema, string? Minimum)> VersionsAsync()
    {
        var row = await Db.CompatibilityRowAsync();
        return (row.Schema, row.Minimum);
    }

    [PostgreSqlFact]
    public async Task I7_N_minus_1_writes_on_schema_N_while_N_minus_2_and_newer_clients_are_blocked()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.GrantApplicationDataAccessAsync();

        // Schéma S2 (N = 1.1.0, minimum = 1.0.0) : le poste 1.0.0, qui ne connaît que S1, est N-1.
        var nMinus1 = await WorkstationVerdictAsync<ChainS1>("1.0.0");
        nMinus1.State.Should().Be(ServerSchemaState.E3a);
        nMinus1.CanStart.Should().BeTrue();

        await using (var workstation = Db.Context<ChainS1>(Db.AppConnectionString))
        {
            workstation.Customers.Add(new Customer { FirstName = "Fenêtre", LastName = "N-1" });
            await workstation.SaveChangesAsync();
        }

        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"Customers\" WHERE \"LastName\" = 'N-1'"))
            .Should().Be(1, "un poste N-1 travaille normalement, ni dégradé ni en lecture seule");

        (await Db.RunAsync<ChainS3>("1.3.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.GrantApplicationDataAccessAsync();
        (await VersionsAsync()).Should().Be(("1.3.0", "1.1.0"));

        var nMinus2 = await WorkstationVerdictAsync<ChainS1>("1.0.0");
        nMinus2.State.Should().Be(ServerSchemaState.E3b);
        nMinus2.CanStart.Should().BeFalse("N-2 est bloqué : la fenêtre vaut un cran");

        var newer = await WorkstationVerdictAsync<ChainS4>("1.4.0");
        newer.State.Should().Be(ServerSchemaState.E2);
        newer.CanStart.Should().BeFalse("aucune fenêtre pour un poste plus récent que la base");

        var current = await WorkstationVerdictAsync<ChainS3>("1.3.0");
        current.State.Should().Be(ServerSchemaState.E1);

        var guardAppliedSet = await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\"");
        await using var reference = Db.Context<ChainS3>(Db.AdminConnectionString);
        (await reference.Database.GetAppliedMigrationsAsync()).Should().HaveCount((int)guardAppliedSet,
            "la lecture brute de la garde et GetAppliedMigrations voient le même historique");
    }

    [PostgreSqlFact]
    public async Task I9_reference_example_end_to_end()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.0.0", "1.0.0"));

        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"));

        (await Db.RunAsync<ChainS2>("1.2.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"), "une release sans migration ne consomme aucun cran");

        // Variante : un échec entre 1.1.0 et 1.3.0 ne change pas la ligne.
        var failed = await Db.RunAsync<ChainS2ThenFail>("1.3.0");
        failed.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"), "un échec n'est jamais une ancre");

        (await Db.RunAsync<ChainS3>("1.3.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.3.0", "1.1.0"));

        // Lecture par la garde (§4.3) : sur S3, un poste 1.2.0 (qui connaît S2) est en E3a, un poste 1.0.0 en E3b.
        await Db.GrantApplicationDataAccessAsync();
        (await WorkstationVerdictAsync<ChainS2>("1.2.0")).State.Should().Be(ServerSchemaState.E3a);
        (await WorkstationVerdictAsync<ChainS1>("1.0.0")).State.Should().Be(ServerSchemaState.E3b);
    }

    [PostgreSqlFact]
    public async Task Scenario3_relaunch_by_a_newer_release_without_migration_keeps_the_producer_version()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.SetParameterAsync("mmv_it.app_role", Db.AppRole);
        (await Db.RunAsync<ChainS1ThenRevoke>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.VerificationFailed);

        // Relance par la release 1.2.0, dont la chaîne ne contient aucune migration nouvelle.
        var relaunched = await Db.RunAsync<ChainS1ThenRevoke>("1.2.0");

        relaunched.ExitCode.Should().Be(MigrationExitCode.Success, relaunched.Message);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"),
            "schema_version est la release qui a produit le schéma physique (1.1.0), jamais la version courante");
    }

    [PostgreSqlFact]
    public async Task Scenario5_N_then_failure_then_new_release_keeps_N_minus_2_blocked()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0")); // N = 1.1.0

        // Release 1.2.0 : sa migration s'applique, la vérification échoue (droit retiré) — état non vérifié.
        await Db.SetParameterAsync("mmv_it.app_role", Db.AppRole);
        (await Db.RunAsync<ChainS2ThenRevoke>("1.2.0")).ExitCode.Should().Be(MigrationExitCode.VerificationFailed);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"), "aucune ancre pour un état non vérifié");

        // Nouvelle release 1.3.0 : migration réussie et vérifiée.
        var next = await Db.RunAsync<ChainS2RevokeThenStep2>("1.3.0");
        next.ExitCode.Should().Be(MigrationExitCode.Success, next.Message);
        (await VersionsAsync()).Should().Be(("1.3.0", "1.2.0"),
            "le schéma immédiatement précédent est celui que 1.2.0 a physiquement produit");

        await Db.GrantApplicationDataAccessAsync();
        var nMinus2 = await WorkstationVerdictAsync<ChainS2>("1.1.0");
        nMinus2.State.Should().Be(ServerSchemaState.E3b, "N-2 reste bloqué");
        (await WorkstationVerdictAsync<ChainS1>("1.0.0")).CanStart.Should().BeFalse();
        (await WorkstationVerdictAsync<ChainS2ThenRevoke>("1.2.0")).State.Should().Be(ServerSchemaState.E3a, "N-1");
        (await WorkstationVerdictAsync<ChainS2RevokeThenStep2>("1.3.0")).State.Should().Be(ServerSchemaState.E1);
    }

    [PostgreSqlFact]
    public async Task I10_adopt_compatibility_initializes_a_base_migrated_before_the_journal()
    {
        await using (var legacy = Db.Context<ChainS1>(Db.MigratorConnectionString))
        {
            await legacy.Database.MigrateAsync(); // base migrée sans journal ni métadonnée
        }

        var refused = await Db.RunAsync<ChainS1>("1.0.0");
        refused.ExitCode.Should().Be(MigrationExitCode.MetadataInconsistent);
        refused.Message.Should().Contain("adopt-compatibility");
        (await Db.AdminScalarAsync<bool>("SELECT to_regnamespace('mmv_meta') IS NULL")).Should().BeTrue(
            "le refus précède toute écriture : pas même le schéma de métadonnée");

        var adopted = await Db.RunAsync<ChainS1>("1.0.0", MigrationRunKind.Adopt);
        adopted.ExitCode.Should().Be(MigrationExitCode.Success, adopted.Message);
        (await VersionsAsync()).Should().Be(("1.0.0", "1.0.0"), "la valeur la plus stricte : aucun poste plus ancien");
        (await Db.AdminScalarAsync<string>("SELECT run_kind FROM mmv_meta.migration_run")).Should().Be("adopt");

        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await VersionsAsync()).Should().Be(("1.1.0", "1.0.0"), "l'adoption est l'ancre du calcul suivant");

        (await Db.RunAsync<ChainS2>("1.1.0", MigrationRunKind.Adopt)).ExitCode
            .Should().Be(MigrationExitCode.MetadataInconsistent, "une métadonnée initialisée ne se réadopte pas");
    }
}
