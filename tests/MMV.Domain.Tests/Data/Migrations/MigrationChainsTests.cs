using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MMV.Domain.Entities;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data.Migrations;

/// <summary>
/// P4-5E — Deux chaînes de migrations, un seul modèle (ADR-PROD-DB-005, décisions D-01 et D-06).
///
/// <para>
/// Vérifie, <b>sans serveur</b>, que chaque provider découvre sa propre chaîne et elle seule : la chaîne
/// SQLite reste exactement les 14 migrations historiques, sans dérive, dans <c>MMV.Infrastructure</c> ; la
/// chaîne PostgreSQL vit dans son assembly dédiée et ne voit jamais une migration SQLite (R-E1).
/// </para>
///
/// <para>
/// <b>Ce que ces tests NE prouvent PAS</b> : qu'une migration PostgreSQL s'applique sur un vrai serveur.
/// Cette preuve appartient à P4-5F (N1, N2, G5).
/// </para>
/// </summary>
public sealed class MigrationChainsTests
{
    /// <summary>
    /// Les 14 migrations SQLite, dans l'ordre. Gelées depuis P3-10 (<c>8d4bf49</c>) : toute évolution passe
    /// par une NOUVELLE migration, ajoutée en fin de liste ; aucun identifiant existant ne change (G4).
    /// </summary>
    private static readonly string[] SqliteMigrationIds =
    [
        "20260127184542_InitialCreate",
        "20260129192001_AddProductEntryDate",
        "20260201181136_ProductSchemaRefactoring",
        "20260202005050_AddNotifications",
        "20260212164646_AddCounterSaleFieldsToOrder",
        "20260212173313_AddDepositAndRemainingAmountToOrder",
        "20260212220902_RestoreSaleOrderSeparation",
        "20260611114307_FixDateTimeDefaultValues",
        "20260612071633_AddDocumentSequences",
        "20260713215132_AddCustomerArchivingAndProtectHistory",
        "20260717183027_AddProductNormalizedReferenceAndProtectHistory",
        "20260719003826_AddWorkshopSheets",
        "20260720204330_AddNotificationResolution",
        "20260721134634_AddNormalizedUsernameAndSecureLocalUsers"
    ];

    /// <summary>
    /// Baseline PostgreSQL unique (D-03), identifiant fixé par <c>dotnet ef</c> à la génération. Une
    /// régénération avant fusion (D-03.5) change l'horodatage et impose de mettre ce test à jour ; après
    /// fusion, l'identifiant est gelé comme ceux de la chaîne SQLite.
    /// </summary>
    private const string PostgreSqlBaselineId = "20260922001219_InitialPostgreSqlBaseline";

    /// <summary>Seule exception à D-01.2 (P4-5E-C, Q1 option A), verrouillée par <c>OpticDbContextDesignTimeFactoryTests</c>.</summary>
    internal const string DesignTimeFactoryTypeName =
        "MMV.Infrastructure.PostgreSQL.Migrations.OpticDbContextDesignTimeFactory";

    // Aucune connexion n'est ouverte : GetMigrations et le modèle se lisent par réflexion.
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

    /// <summary>
    /// Équivalent EF Core 8 de <c>dotnet ef migrations has-pending-model-changes</c> : compare le modèle
    /// courant à l'instantané de l'assembly de migrations du contexte. Liste vide ⇒ aucune dérive.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> PendingModelChanges(OpticDbContext context)
    {
        var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }

        if (snapshotModel is not null)
        {
            snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);
        }

        return context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel?.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());
    }

    // ---------------------------------------------------------------- chaîne SQLite (D-06, G4)

    [Fact]
    public void SqliteChain_IsExactlyThe14HistoricalMigrations_InOrder()
    {
        using var context = SqliteContext();

        context.Database.GetMigrations().Should().Equal(SqliteMigrationIds);
    }

    [Fact]
    public void SqliteChain_IsReadFromTheContextAssembly()
    {
        using var context = SqliteContext();

        context.GetService<IMigrationsAssembly>().Assembly.Should().BeSameAs(typeof(OpticDbContext).Assembly,
            "l'adoption par suffixe et l'écriture manuelle de l'historique (SqliteDatabaseManager) supposent " +
            "la chaîne SQLite dans MMV.Infrastructure");
    }

    [Fact]
    public void SqliteChain_HasNoPendingModelChanges()
    {
        using var context = SqliteContext();

        PendingModelChanges(context).Should().BeEmpty(
            "le modèle SQLite doit rester strictement aligné sur son instantané (même contrôle que la CI)");
    }

    // ---------------------------------------------------------------- chaîne PostgreSQL (D-01)

    [Fact]
    public void PostgreSqlChain_IsReadFromItsDedicatedAssembly()
    {
        using var context = PostgreSqlContext();

        // EF charge l'assembly PAR SON NOM : ce test échoue si la DLL est absente ou si le nom est faux.
        context.GetService<IMigrationsAssembly>().Assembly.GetName().Name
            .Should().Be(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);
    }

    [Fact]
    public void PostgreSqlChain_IsExactlyTheBaseline()
    {
        using var context = PostgreSqlContext();

        context.Database.GetMigrations().Should().Equal(new[] { PostgreSqlBaselineId },
            "D-03 : une baseline unique, aucune migration PostgreSQL historique artificielle");
    }

    [Fact]
    public void PostgreSqlChain_HasNoPendingModelChanges()
    {
        using var context = PostgreSqlContext();

        PendingModelChanges(context).Should().BeEmpty(
            "le modèle PostgreSQL doit rester aligné sur l'instantané de sa propre chaîne (D-03.4)");
    }

    [Fact]
    public void EachChain_HasItsOwnSnapshot_InItsOwnAssembly()
    {
        // D-01.4 : un instantané par assembly, chacun ne décrit que sa propre chaîne.
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        var sqliteSnapshot = sqlite.GetService<IMigrationsAssembly>().ModelSnapshot;
        var postgresSnapshot = postgres.GetService<IMigrationsAssembly>().ModelSnapshot;

        sqliteSnapshot.Should().NotBeNull();
        postgresSnapshot.Should().NotBeNull();
        sqliteSnapshot!.GetType().Assembly.Should().BeSameAs(typeof(OpticDbContext).Assembly);
        postgresSnapshot!.GetType().Assembly.Should().BeSameAs(PostgreSqlMigrationsAssembly());
    }

    [Fact]
    public void PostgreSqlChain_NeverSeesASqliteMigration()
    {
        using var context = PostgreSqlContext();

        context.Database.GetMigrations().Should().NotIntersectWith(SqliteMigrationIds,
            "deux chaînes mélangées feraient rejouer l'historique SQLite contre un serveur (R-E1)");
    }

    [Fact]
    public void BothChains_CoexistInTheSameProcess_EachWithItsOwnAssembly()
    {
        // Même ordre que la garde de cache de modèle (D-07) : SQLite d'abord, PostgreSQL ensuite.
        using var sqlite = SqliteContext();
        using var postgres = PostgreSqlContext();

        var sqliteAssembly = sqlite.GetService<IMigrationsAssembly>().Assembly;
        var postgresAssembly = postgres.GetService<IMigrationsAssembly>().Assembly;

        postgresAssembly.Should().NotBeSameAs(sqliteAssembly);
        sqlite.Database.GetMigrations().Should().Equal(SqliteMigrationIds);
    }

    // ---------------------------------------------------------------- assembly PostgreSQL (D-01.2, D-01.6)

    [Fact]
    public void PostgreSqlMigrationsAssemblyName_IsTheRealAssemblyName()
    {
        // D-01.6 : comparaison ORDINALE. Le chargement par nom tolère la casse, mais EF refuse une génération
        // dont le projet cible ne correspond pas exactement à l'assembly configurée.
        var assembly = Assembly.Load(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);

        assembly.GetName().Name.Should().Be(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);
    }

    [Fact]
    public void PostgreSqlMigrationsAssembly_ContainsOnlyMigrationsSnapshotAndTheDelegatingFactory()
    {
        // D-01.2 : ni configuration d'entité, ni repository, ni service. Une seule exception nommée : la
        // factory de découverte (Q1 option A), dont le contenu est verrouillé à part.
        var types = PostgreSqlMigrationsAssembly().GetTypes().Where(t => !IsCompilerGenerated(t)).ToArray();

        types.Where(t => !typeof(Migration).IsAssignableFrom(t) && !typeof(ModelSnapshot).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .Should().Equal(DesignTimeFactoryTypeName);
        types.Count(t => typeof(ModelSnapshot).IsAssignableFrom(t)).Should().Be(1, "D-01.4 : un instantané");
        types.Should().Contain(t => typeof(Migration).IsAssignableFrom(t), "la baseline est présente");
    }

    [Fact]
    public void PostgreSqlMigrationsAssembly_DependsOnNeitherTheUiNorTheApplicationLayer()
    {
        // D-04 : aucune dépendance inverse. L'assembly de migrations appartient à l'anneau Infrastructure.
        var references = PostgreSqlMigrationsAssembly().GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        references.Should().NotContain(n => n == "MMV.App" || n == "MMV.Application" || n.StartsWith("Avalonia"));
    }

    [Fact]
    public void DomainAndInfrastructure_DoNotReferenceThePostgreSqlMigrationsAssembly()
    {
        // Domain : provider-neutre (O14). Infrastructure : sens unique, sinon référence circulaire.
        // Application est déjà couverte par ApplicationArchitectureTests (préfixe « MMV.Infrastructure »).
        foreach (var assembly in new[] { typeof(Customer).Assembly, typeof(OpticDbContext).Assembly })
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name)
                .Should().NotContain(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName,
                    $"{assembly.GetName().Name} ne dépend jamais de la chaîne de migrations PostgreSQL");
        }
    }

    private static Assembly PostgreSqlMigrationsAssembly()
        => Assembly.Load(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);

    private static bool IsCompilerGenerated(Type type)
        => type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
           || type.Namespace is "Microsoft.CodeAnalysis" or "System.Runtime.CompilerServices";
}
