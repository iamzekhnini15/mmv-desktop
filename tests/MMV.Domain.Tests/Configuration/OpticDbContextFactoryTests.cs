using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Npgsql;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-5E — Factory design-time sélective (ADR-PROD-DB-005 G3, décision D-05).
///
/// Prouve que <c>dotnet ef</c> vise la chaîne SQLite par défaut, au caractère près comme avant P4-5E, et la
/// chaîne PostgreSQL <b>uniquement</b> sur <see cref="OpticDbContextFactory.DesignTimeProviderVariableName"/>.
/// Les variables runtime ne sont jamais consultées.
///
/// <para>
/// <b>Aucune connexion n'est ouverte</b> : seules les options EF sont inspectées. L'environnement est injecté
/// par dictionnaire ; celui du processus n'est jamais modifié (déterminisme, parallélisme xUnit).
/// </para>
/// </summary>
public sealed class OpticDbContextFactoryTests
{
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Chaîne runtime FACTICE, reconnaissable : elle ne doit jamais atteindre le design-time.</summary>
    private const string RuntimeConnectionString = "Host=runtime-only.invalid;Database=mmv-p4-5e-runtime";

    private static Dictionary<string, string?> Env(params (string Key, string? Value)[] entries)
    {
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Chemin SQLite déterministe sous le dossier temporaire : aucun effet de bord dans le profil.
            [SqliteDatabasePathResolver.EnvironmentVariableName] = TempSqlitePath()
        };

        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    private static OpticDbContext Create(Dictionary<string, string?> environment)
        => new OpticDbContextFactory().CreateDbContext(environment);

    private static string? MigrationsAssembly(OpticDbContext context)
        => RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).MigrationsAssembly;

    // ---------------------------------------------------------------------------------------------
    // 1. Défaut SQLite — comportement historique inchangé.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutDesignTimeVariable_TargetsSqlite_WithTheHistoricalConnectionString()
    {
        var environment = Env();

        using var context = Create(environment);

        context.Database.ProviderName.Should().Be(SqliteProviderName);
        context.Database.GetConnectionString().Should().Be(
            SqliteDatabasePathResolver.GetConnectionString(environment: environment),
            "même résolution de chemin que le runtime et qu'avant P4-5E (P2A-1A)");
        MigrationsAssembly(context).Should().BeNull("la chaîne SQLite garde l'assembly implicite du contexte");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sqlite")]
    [InlineData("  SQLite  ")]
    public void WithBlankOrSqliteValue_TargetsSqlite(string value)
    {
        using var context = Create(Env((OpticDbContextFactory.DesignTimeProviderVariableName, value)));

        context.Database.ProviderName.Should().Be(SqliteProviderName);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. Variables runtime ignorées (D-05.1).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RuntimeProviderVariables_NeverSelectPostgreSql()
    {
        using var context = Create(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, RuntimeConnectionString)));

        context.Database.ProviderName.Should().Be(SqliteProviderName,
            "générer une migration ne dépend jamais de l'environnement runtime du poste (C-2, R-E2)");
    }

    [Fact]
    public void RuntimeConnectionString_NeverReachesThePostgreSqlDesignTimeContext()
    {
        using var context = Create(Env(
            (OpticDbContextFactory.DesignTimeProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, RuntimeConnectionString)));

        context.Database.GetConnectionString().Should()
            .Be(OpticDbContextFactory.DesignTimePostgreSqlConnectionString,
                "aucun secret runtime n'est lu pour générer (S-8)");
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Chaîne PostgreSQL.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("postgresql")]
    [InlineData("postgres")]
    [InlineData("npgsql")]
    [InlineData("  PostgreSQL  ")]
    public void WithPostgreSqlValueOrAlias_TargetsNpgsql_AndThePostgreSqlMigrationsAssembly(string value)
    {
        using var context = Create(Env((OpticDbContextFactory.DesignTimeProviderVariableName, value)));

        context.Database.ProviderName.Should().Be(PostgreSqlProviderName);
        MigrationsAssembly(context).Should().Be(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);
        context.Database.GetConnectionString().Should()
            .Be(OpticDbContextFactory.DesignTimePostgreSqlConnectionString);
    }

    [Fact]
    public void PostgreSqlBranch_ResolvesNoSqliteFile_AndCreatesNoDirectory()
    {
        // D-05.5 : l'effet de bord historique (création du dossier de la base SQLite) reste confiné à la
        // branche SQLite. Le dossier pointé ici n'existe pas et ne doit pas être créé.
        var missingDirectory = Path.Combine(Path.GetTempPath(), "mmv-p4-5e-" + Guid.NewGuid().ToString("N"));
        var environment = Env(
            (OpticDbContextFactory.DesignTimeProviderVariableName, "postgresql"),
            (SqliteDatabasePathResolver.EnvironmentVariableName, Path.Combine(missingDirectory, "mmv.db")));

        using var context = Create(environment);

        context.Database.ProviderName.Should().Be(PostgreSqlProviderName);
        Directory.Exists(missingDirectory).Should().BeFalse();
    }

    [Fact]
    public void DesignTimePostgreSqlConnectionString_IsFake_AndCarriesNoCredential()
    {
        // D-05.4 : chaîne constante, non secrète, désignant un serveur jamais contacté.
        var builder = new NpgsqlConnectionStringBuilder(OpticDbContextFactory.DesignTimePostgreSqlConnectionString);

        builder.Password.Should().BeNullOrEmpty();
        builder.Username.Should().BeNullOrEmpty();
        builder.Host.Should().EndWith(".invalid", "domaine réservé (RFC 2606) : il ne se résout jamais");
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Valeur invalide bloquée — jamais de repli silencieux sur SQLite (D-05.3).
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("postgrse")]
    [InlineData("sqlserver")]
    [InlineData("Host=db;Password=secret")]
    public void WithUnknownValue_Throws_NamingTheDesignTimeVariable_WithoutEchoingTheValue(string value)
    {
        var act = () => Create(Env((OpticDbContextFactory.DesignTimeProviderVariableName, value)));

        var exception = act.Should().Throw<DatabaseConfigurationException>().Which;

        exception.Message.Should()
            .Contain(OpticDbContextFactory.DesignTimeProviderVariableName)
            .And.NotContain(DatabaseProviderResolver.ProviderVariableName)
            .And.NotContain(value);
    }

    private static string TempSqlitePath()
        => Path.Combine(Path.GetTempPath(), "mmv-p4-5e-" + Guid.NewGuid().ToString("N") + ".db");
}
