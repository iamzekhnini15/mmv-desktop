using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Import;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests.Import;

/// <summary>P4-7 — options de <c>import-sqlite</c> et plan d'import tiré du modèle EF PostgreSQL réel, sans serveur.</summary>
public sealed class ImportOptionsAndPlanTests
{
    private static readonly string[] Valid =
    [
        "import-sqlite", "--source", "mmv.db", "--source-time-zone", "Africa/Casablanca", "--operator", "OP-1",
        "--backup-ref", "b.manifest.json", "--report", "r.json"
    ];

    internal static ImportPlan PostgreSqlPlan()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = "Host=mmv-plan.invalid;Database=plan"
        });
        using var context = new OpticDbContext(builder.Options);
        return ImportPlan.From(context.GetService<IDesignTimeModel>().Model);
    }

    [Fact]
    public void Valid_arguments_are_parsed_with_safe_defaults()
    {
        var parsed = ImportOptions.Parse(Valid);

        parsed.IsValid.Should().BeTrue(parsed.Error);
        parsed.Options.Should().Be(new ImportOptions("mmv.db", "Africa/Casablanca", "OP-1", "b.manifest.json", "r.json", false,
            MigrationToolOptions.DefaultWait));
        ImportOptions.Parse([.. Valid, "--dry-run", "--wait", "30"]).Options!.Should()
            .Match<ImportOptions>(o => o.DryRun && o.Wait == TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--source-time-zone")]
    [InlineData("--operator")]
    [InlineData("--backup-ref")]
    [InlineData("--report")]
    public void Every_required_option_is_mandatory_including_the_time_zone(string option)
    {
        var index = Array.IndexOf(Valid, option);
        var args = Valid.Where((_, i) => i != index && i != index + 1).ToArray();

        ImportOptions.Parse(args).Error.Should().Contain(option);
    }

    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--operator", "OP-2")]
    [InlineData("--wait", "-1")]
    [InlineData("--wait", "999999")]
    [InlineData("--dry-run", "--dry-run")]
    public void Unknown_repeated_or_out_of_range_options_are_refused(string name, string value) =>
        ImportOptions.Parse([.. Valid, name, value]).IsValid.Should().BeFalse();

    [Fact]
    public void No_import_option_relaxes_the_backup_gate_or_targets_another_role()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "MMV.DatabaseManager", "CommandLine", "ImportOptions.cs"));

        source.Should().NotMatchRegex(@"""--[a-z-]*(age|old|stale|skip|force|password|connection)",
            "aucune option ne relâche la sauvegarde vérifiée ni ne porte de secret");
        ImportOptions.Parse([.. Valid, "--skip-backup", "x"]).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Option_without_value_or_with_a_control_character_is_refused()
    {
        ImportOptions.Parse([.. Valid[..^1]]).Error.Should().Contain("attend une valeur");
        ImportOptions.Parse(Valid.Select(a => a == "OP-1" ? "OP\n1" : a).ToArray()).Error.Should().Contain("contrôle");
    }

    [Fact]
    public void Plan_covers_every_application_table_and_never_the_ef_history()
    {
        var plan = PostgreSqlPlan();

        plan.Tables.Should().HaveCount(21);
        plan.Tables.Select(t => t.Name).Should().NotContain("__EFMigrationsHistory").And.OnlyHaveUniqueItems();
        plan.Tables.Should().OnlyContain(t => t.KeyOrdinals.Count > 0 && t.KeyOrdinals.All(i => i >= 0));
        plan.Tables.Single(t => t.Name == "GlassSupplements").KeyOrdinals.Should().HaveCount(2, "clé composite N-N");
    }

    [Fact]
    public void Plan_orders_every_parent_before_its_children()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = "Host=mmv-plan.invalid;Database=plan"
        });
        using var context = new OpticDbContext(builder.Options);
        var relational = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var order = PostgreSqlPlan().Tables.Select(t => t.Name).ToList();

        var edges = relational.Tables.SelectMany(t => t.ForeignKeyConstraints.Select(fk => (Child: t.Name, Parent: fk.PrincipalTable.Name))).ToList();

        edges.Should().HaveCountGreaterThan(15);
        edges.Should().OnlyContain(e => order.IndexOf(e.Parent) < order.IndexOf(e.Child));
    }

    [Fact]
    public void Plan_knows_the_seed_rows_and_the_exact_store_types()
    {
        var plan = PostgreSqlPlan();

        plan.Tables.Single(t => t.Name == "DocumentSequences").SeedKeys.Should().BeEquivalentTo("ORDER", "SALE");
        plan.Tables.Where(t => t.Name != "DocumentSequences").Should().OnlyContain(t => t.SeedKeys.Count == 0);

        var columns = plan.Tables.SelectMany(t => t.Columns).ToList();
        columns.Count(c => c.StoreType == "numeric(12,2)").Should().Be(14, "les 14 colonnes monétaires d'ADR-003");
        columns.Count(c => c.Kind == ImportColumnKind.Instant).Should().Be(19);
        columns.Count(c => c.Kind == ImportColumnKind.CivilDate).Should().Be(2);
        columns.Where(c => c.EnumType is not null).Should().HaveCountGreaterThan(15).And.OnlyContain(c => c.Kind == ImportColumnKind.Text);
    }
}
