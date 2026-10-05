#pragma warning disable EF1001 // MigrationsAssembly est une API interne d'EF : usage confiné à ce projet de tests.
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using MMV.DatabaseManager.Locking;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// Chaînes de migrations <b>de test</b> (P4-6B, §7.3 : I-3, I-9 … I-12) : la baseline PostgreSQL de production,
/// suivie de migrations écrites à la main qui n'existent <b>que</b> dans ce projet de tests. Elles ne touchent
/// ni le modèle EF, ni le snapshot, ni les chaînes de production (<c>MigrationChainsTests</c> inchangé) : elles
/// simulent les releases successives qu'aucune chaîne réelle ne contient encore (une seule migration
/// PostgreSQL à ce jour).
/// </summary>
public interface ILifecycleChain
{
    /// <summary>Migrations de test ajoutées après la baseline, dans l'ordre.</summary>
    Type[] Extra { get; }
}

/// <summary>Release 1.0.0 : baseline seule (S1).</summary>
public sealed class ChainS1 : ILifecycleChain { public Type[] Extra => []; }

/// <summary>Release 1.1.0 / 1.2.0 : S1 + étape 1 (S2).</summary>
public sealed class ChainS2 : ILifecycleChain { public Type[] Extra => [typeof(ItStep1)]; }

/// <summary>Release 1.3.0 : S2 + étape 2 (S3).</summary>
public sealed class ChainS3 : ILifecycleChain { public Type[] Extra => [typeof(ItStep1), typeof(ItStep2)]; }

/// <summary>Release 1.4.0 : S3 + étape 3 (S4).</summary>
public sealed class ChainS4 : ILifecycleChain { public Type[] Extra => [typeof(ItStep1), typeof(ItStep2), typeof(ItStep3)]; }

/// <summary>S2 puis une migration qui échoue (division par zéro).</summary>
public sealed class ChainS2ThenFail : ILifecycleChain { public Type[] Extra => [typeof(ItStep1), typeof(ItFail)]; }

/// <summary>S2 puis une migration lente (<c>pg_sleep</c>, durée réglée par la base).</summary>
public sealed class ChainS2ThenSlow : ILifecycleChain { public Type[] Extra => [typeof(ItStep1), typeof(ItSlow)]; }

/// <summary>Baseline puis une migration lente.</summary>
public sealed class ChainS1ThenSlow : ILifecycleChain { public Type[] Extra => [typeof(ItSlow)]; }

/// <summary>S1 puis un <c>ALTER TABLE "Customers"</c> (I-11, <c>lock_timeout</c>).</summary>
public sealed class ChainS1ThenAlter : ILifecycleChain { public Type[] Extra => [typeof(ItAlterCustomers)]; }

/// <summary>S1 puis une migration qui retire au rôle applicatif son droit de lecture (I-12).</summary>
public sealed class ChainS1ThenRevoke : ILifecycleChain { public Type[] Extra => [typeof(ItRevokeAppRole)]; }

/// <summary>S2 puis un retrait du droit applicatif entre l'étape 1 et l'étape 2 (scénario 5, release 1.2.0).</summary>
public sealed class ChainS2ThenRevoke : ILifecycleChain { public Type[] Extra => [typeof(ItStep1), typeof(ItRevokeAfterStep1)]; }

/// <summary>Chaîne précédente puis l'étape 2 (scénario 5, release 1.3.0).</summary>
public sealed class ChainS2RevokeThenStep2 : ILifecycleChain
{
    public Type[] Extra => [typeof(ItStep1), typeof(ItRevokeAfterStep1), typeof(ItStep2)];
}

/// <summary>Assembly de migrations = production + migrations de test de <typeparamref name="TChain"/>.</summary>
public sealed class LifecycleMigrationsAssembly<TChain> : MigrationsAssembly where TChain : ILifecycleChain, new()
{
    private IReadOnlyDictionary<string, TypeInfo>? _migrations;

    public LifecycleMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
    }

    public override IReadOnlyDictionary<string, TypeInfo> Migrations =>
        _migrations ??= base.Migrations
            .Concat(new TChain().Extra.Select(t => new KeyValuePair<string, TypeInfo>(
                t.GetCustomAttribute<MigrationAttribute>()!.Id, t.GetTypeInfo())))
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToDictionary(m => m.Key, m => m.Value);
}

/// <summary>SQL des sondes : la session qui exécute la migration détient-elle elle-même le verrou de l'outil ?</summary>
internal static class Probe
{
    public static string Insert(string step) =>
        "INSERT INTO it_lifecycle_probe (step, advisory_holders) " +
        $"SELECT '{step}', count(*) FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND granted " +
        "AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) " +
        $"AND classid::bigint = {PostgreSqlAdvisoryMigrationLock.KeyHigh} " +
        $"AND objid::bigint = {PostgreSqlAdvisoryMigrationLock.KeyLow} AND objsubid = 1 AND pid = pg_backend_pid()";
}

[Migration(Id)]
public sealed class ItStep1 : Migration
{
    public const string Id = "29990101000001_ItLifecycleStep1";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE TABLE it_lifecycle_probe (step text NOT NULL, advisory_holders bigint NOT NULL)");
        migrationBuilder.Sql(Probe.Insert("step1"));
    }
}

[Migration(Id)]
public sealed class ItStep2 : Migration
{
    public const string Id = "29990101000002_ItLifecycleStep2";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Probe.Insert("step2"));
}

[Migration(Id)]
public sealed class ItStep3 : Migration
{
    public const string Id = "29990101000003_ItLifecycleStep3";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Probe.Insert("step3"));
}

[Migration(Id)]
public sealed class ItFail : Migration
{
    public const string Id = "29990101000090_ItLifecycleFail";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("SELECT 1 / 0");
}

[Migration(Id)]
public sealed class ItSlow : Migration
{
    public const string Id = "29990101000091_ItLifecycleSlow";

    /// <summary>Durée lue dans le paramètre de base <c>mmv_it.sleep_seconds</c> (posé par le test).</summary>
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("SELECT pg_sleep(current_setting('mmv_it.sleep_seconds')::float8)");
}

[Migration(Id)]
public sealed class ItAlterCustomers : Migration
{
    public const string Id = "29990101000092_ItLifecycleAlterCustomers";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE \"Customers\" ADD COLUMN it_lock_probe integer NULL");
}

/// <summary>P4-10 : S1 puis un <c>ALTER TABLE "Products"</c> additif, appliqué pendant que des postes vendent.</summary>
public sealed class ChainS1ThenAlterProducts : ILifecycleChain { public Type[] Extra => [typeof(ItAlterProducts)]; }

[Migration(Id)]
public sealed class ItAlterProducts : Migration
{
    public const string Id = "29990101000095_ItLifecycleAlterProducts";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE \"Products\" ADD COLUMN it_expand_probe integer NULL");
}

[Migration(Id)]
public sealed class ItRevokeAppRole : Migration
{
    public const string Id = "29990101000093_ItLifecycleRevokeAppRole";

    /// <summary>Rôle lu dans le paramètre de base <c>mmv_it.app_role</c> (posé par le test), cité par le serveur.</summary>
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $$ BEGIN EXECUTE format('REVOKE SELECT ON mmv_meta.schema_compatibility FROM %I', " +
                             "current_setting('mmv_it.app_role')); END $$");
}

/// <summary>
/// Même retrait que <see cref="ItRevokeAppRole"/>, ordonné entre l'étape 1 et l'étape 2 (l'identifiant trie
/// après <c>…000001_ItLifecycleStep1</c> et avant <c>…000002</c>).
/// </summary>
[Migration(Id)]
public sealed class ItRevokeAfterStep1 : Migration
{
    public const string Id = "29990101000001_ItLifecycleStep1Revoke";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $$ BEGIN EXECUTE format('REVOKE SELECT ON mmv_meta.schema_compatibility FROM %I', " +
                             "current_setting('mmv_it.app_role')); END $$");
}
