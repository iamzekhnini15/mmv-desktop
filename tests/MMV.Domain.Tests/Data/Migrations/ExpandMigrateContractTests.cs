using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data.Migrations;

/// <summary>
/// P4-6B, U-E1 — contrôle d'« étendre → migrer → contracter » (Q-21, H15 ; DP-3 §5.2.3.4).
///
/// <para>
/// Énumère les opérations de <b>contraction</b> des <c>UpOperations</c> de chaque migration, sur les deux
/// chaînes — suppression de colonne ou de table, renommage de colonne ou de table, resserrement de nullabilité —
/// et les compare à une <b>liste d'autorisation explicite</b>. Une contraction nouvelle casse ce test jusqu'à
/// ce que son auteur la déclare ici : elle devient visible en revue, sans être interdite à la release qui suit
/// l'expand. Même idiome que <see cref="MigrationChainsTests"/>. Une contraction casse un poste N-1 : elle ne
/// peut jamais partir dans la release qui étend (CONTRIBUTING.md, « Étendre → migrer → contracter »).
/// </para>
/// </summary>
public sealed class ExpandMigrateContractTests
{
    /// <summary>
    /// Contractions déclarées, <c>migration|opération</c>. Historique figé : toutes antérieures à la V1
    /// multi-poste (aucun poste N-1 n'existait). Toute entrée nouvelle est un acte de revue.
    /// </summary>
    private static readonly string[] DeclaredContractions =
    [
        // Chaîne SQLite (P2A/P2B, mono-poste) — gelée depuis P3-10.
        "20260201181136_ProductSchemaRefactoring|TightenNullability Products.SupplierId",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.CustomerId",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.DepositAmount",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.DiscountAmount",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.FinalAmount",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.PaymentMethod",
        "20260212220902_RestoreSaleOrderSeparation|DropColumn Orders.TotalAmount",
        "20260212220902_RestoreSaleOrderSeparation|RenameColumn Orders.IsCounterSale->SaleId",
        "20260212220902_RestoreSaleOrderSeparation|RenameColumn Orders.RemainingAmount->ReceivedDate",
        "20260212220902_RestoreSaleOrderSeparation|RenameColumn Orders.StaffId->SupplierId"
        // Chaîne PostgreSQL : baseline unique, aucune contraction.
    ];

    [Fact]
    public void Every_contraction_is_declared_in_the_allow_list()
    {
        var actual = Contractions(SqliteContext()).Concat(Contractions(PostgreSqlContext())).ToArray();

        actual.Should().BeEquivalentTo(DeclaredContractions,
            "toute contraction (DropColumn, DropTable, RenameColumn, RenameTable, nullabilité resserrée) doit être " +
            "déclarée dans la liste d'autorisation, et toute entrée déclarée doit correspondre à une contraction réelle");
    }

    [Fact]
    public void Detector_sees_every_contraction_kind()
    {
        // Garde anti-faux-vert : le détecteur reconnaît chaque forme de contraction.
        var operations = new MigrationOperation[]
        {
            new DropColumnOperation { Table = "T", Name = "c" },
            new DropTableOperation { Name = "T" },
            new RenameColumnOperation { Table = "T", Name = "a", NewName = "b" },
            new RenameTableOperation { Name = "T", NewName = "U" },
            new AlterColumnOperation { Table = "T", Name = "n", IsNullable = false, OldColumn = new AddColumnOperation { IsNullable = true } },
            new AlterColumnOperation { Table = "T", Name = "w", IsNullable = true, OldColumn = new AddColumnOperation { IsNullable = false } },
            new AddColumnOperation { Table = "T", Name = "x" },
            new CreateTableOperation { Name = "V" }
        };

        operations.Select(Describe).Where(d => d is not null).Should().Equal(
            "DropColumn T.c", "DropTable T", "RenameColumn T.a->b", "RenameTable T->U", "TightenNullability T.n");
    }

    private static IEnumerable<string> Contractions(OpticDbContext context)
    {
        using (context)
        {
            var migrations = context.GetService<IMigrationsAssembly>();
            var provider = context.Database.ProviderName!;
            foreach (var (id, type) in migrations.Migrations.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                var migration = migrations.CreateMigration(type, provider);
                foreach (var description in migration.UpOperations.Select(Describe).OfType<string>())
                {
                    yield return $"{id}|{description}";
                }
            }
        }
    }

    private static string? Describe(MigrationOperation operation) => operation switch
    {
        DropColumnOperation drop => $"DropColumn {drop.Table}.{drop.Name}",
        DropTableOperation drop => $"DropTable {drop.Name}",
        RenameColumnOperation rename => $"RenameColumn {rename.Table}.{rename.Name}->{rename.NewName}",
        RenameTableOperation rename => $"RenameTable {rename.Name}->{rename.NewName}",
        AlterColumnOperation alter when alter.OldColumn.IsNullable && !alter.IsNullable =>
            $"TightenNullability {alter.Table}.{alter.Name}",
        _ => null
    };

    private static OpticDbContext SqliteContext() => new(
        new DbContextOptionsBuilder<OpticDbContext>().UseSqlite("Data Source=:memory:").Options);

    private static OpticDbContext PostgreSqlContext()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = OpticDbContextFactory.DesignTimePostgreSqlConnectionString
        });
        return new OpticDbContext(builder.Options);
    }
}
