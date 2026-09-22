using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-3 — Sélection du fournisseur de base de données par configuration (ADR-PROD-DB-002).
///
/// Vérifie le défaut SQLite (comportement historique préservé), les alias PostgreSQL, le blocage
/// explicite d'une configuration invalide (faute de frappe, chaîne de connexion serveur manquante)
/// et la configuration EF effectivement produite pour chaque fournisseur.
///
/// <para>
/// <b>Aucun test n'ouvre de connexion PostgreSQL réelle</b> : seule la <i>sélection</i> du fournisseur
/// EF est vérifiée. Aucun serveur ni conteneur n'est requis. L'environnement est injecté par
/// dictionnaire — l'environnement du processus n'est jamais modifié (déterminisme, parallélisme xUnit).
/// </para>
/// </summary>
public sealed class DatabaseProviderResolverTests
{
    /// <summary>Nom du fournisseur EF SQLite (valeur retournée par <c>Database.ProviderName</c>).</summary>
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>Nom du fournisseur EF PostgreSQL (valeur retournée par <c>Database.ProviderName</c>).</summary>
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Chaîne de connexion FACTICE, volontairement sans identifiants : elle sert uniquement à prouver
    /// la sélection du fournisseur EF. Aucun secret ni exemple exploitable n'est écrit dans le dépôt.
    /// </summary>
    private const string FakeServerConnectionString = "Host=localhost;Database=mmv-p4-3-fake";

    private static Dictionary<string, string?> Env(params (string Key, string? Value)[] entries)
    {
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    // ---------------------------------------------------------------------------------------------
    // 1. Défaut SQLite — l'absence de configuration ne change rien au comportement historique.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_WhenProviderVariableIsAbsent_DefaultsToSqlite()
    {
        var options = DatabaseProviderResolver.Resolve(Env());

        options.Provider.Should().Be(DatabaseProvider.Sqlite);
        options.ConnectionString.Should().BeNull();
        options.HasConnectionString.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WhenProviderVariableIsBlank_DefaultsToSqlite(string value)
    {
        var options = DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, value)));

        options.Provider.Should().Be(DatabaseProvider.Sqlite);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. Valeurs reconnues — casse et espaces tolérés.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("sqlite")]
    [InlineData("SQLite")]
    [InlineData("  SqLiTe  ")]
    public void Resolve_WithSqliteValue_SelectsSqlite(string value)
    {
        var options = DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, value)));

        options.Provider.Should().Be(DatabaseProvider.Sqlite);
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("postgres")]
    [InlineData("npgsql")]
    [InlineData("PostgreSQL")]
    [InlineData("  Postgres  ")]
    public void Resolve_WithPostgreSqlValueOrAlias_SelectsPostgreSql(string value)
    {
        var options = DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, value),
            (DatabaseProviderResolver.ConnectionStringVariableName, FakeServerConnectionString)));

        options.Provider.Should().Be(DatabaseProvider.PostgreSql);
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Configuration invalide bloquée — une faute de frappe ne retombe JAMAIS sur SQLite.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("sqlserver")]     // rejeté pour la V1 par l'ADR : non exposé
    [InlineData("mysql")]
    [InlineData("postgrse")]      // faute de frappe plausible, non sous-chaîne de "postgresql"
    public void Resolve_WithUnknownProvider_Throws_AndNeverFallsBackToSqlite(string value)
    {
        var act = () => DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, value)));

        // Le message doit nommer la variable fautive (diagnostic) sans restituer la valeur reçue :
        // celle-ci peut porter par erreur une chaîne de connexion ou un identifiant.
        var exception = act.Should()
            .Throw<DatabaseConfigurationException>()
            .Which;

        exception.Message.Should()
            .Contain(DatabaseProviderResolver.ProviderVariableName)
            .And.NotContain(value);
    }

    [Fact]
    public void Resolve_PostgreSqlWithoutConnectionString_Throws()
    {
        var act = () => DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql")));

        act.Should().Throw<DatabaseConfigurationException>()
            .Which.Message.Should().Contain(DatabaseProviderResolver.ConnectionStringVariableName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_PostgreSqlWithBlankConnectionString_Throws(string connectionString)
    {
        var act = () => DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, connectionString)));

        act.Should().Throw<DatabaseConfigurationException>();
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Options résolues.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_PostgreSqlWithConnectionString_ReturnsOptions_WithoutConnecting()
    {
        var options = DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, $"  {FakeServerConnectionString}  ")));

        options.Provider.Should().Be(DatabaseProvider.PostgreSql);
        options.HasConnectionString.Should().BeTrue();
        options.ConnectionString.Should().Be(FakeServerConnectionString);
    }

    [Fact]
    public void Resolve_Sqlite_IgnoresServerConnectionString_AndLeavesSqlitePathToItsOwnResolver()
    {
        // Le chemin SQLite reste la propriété de SqliteDatabasePathResolver / MMV_DATABASE_PATH :
        // P4-3 n'introduit aucune chaîne de connexion serveur pour SQLite.
        var options = DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, "sqlite"),
            (DatabaseProviderResolver.ConnectionStringVariableName, FakeServerConnectionString)));

        options.Provider.Should().Be(DatabaseProvider.Sqlite);
        options.ConnectionString.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------
    // 5. Configuration EF effectivement produite.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Configure_Sqlite_SelectsSqliteEfProvider()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();

        DatabaseProviderResolver.Configure(builder, DatabaseProviderOptions.Sqlite, InMemorySqlitePath());

        using var context = new OpticDbContext(builder.Options);
        context.Database.ProviderName.Should().Be(SqliteProviderName);
    }

    [Fact]
    public void Configure_Sqlite_UsesProvidedPath()
    {
        var path = InMemorySqlitePath();
        var builder = new DbContextOptionsBuilder<OpticDbContext>();

        DatabaseProviderResolver.Configure(builder, DatabaseProviderOptions.Sqlite, path);

        using var context = new OpticDbContext(builder.Options);
        context.Database.GetConnectionString().Should().Be($"Data Source={path}");
    }

    [Fact]
    public void Configure_PostgreSql_SelectsNpgsqlEfProvider()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        var options = new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = FakeServerConnectionString
        };

        DatabaseProviderResolver.Configure(builder, options);

        using var context = new OpticDbContext(builder.Options);
        context.Database.ProviderName.Should().Be(PostgreSqlProviderName);
    }

    [Fact]
    public void Configure_PostgreSqlWithoutConnectionString_Throws()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        var options = new DatabaseProviderOptions { Provider = DatabaseProvider.PostgreSql };

        var act = () => DatabaseProviderResolver.Configure(builder, options);

        act.Should().Throw<DatabaseConfigurationException>();
    }

    // ---------------------------------------------------------------------------------------------
    // 6. Non-régression — des options déjà configurées ne sont pas écrasées par OnConfiguring.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OnConfiguring_DoesNotOverride_OptionsAlreadyConfiguredByCaller()
    {
        // Contrat des tests existants : une connexion SQLite en mémoire fournie par l'appelant doit
        // survivre intacte. Si OnConfiguring reconfigurait le contexte, la connexion serait remplacée
        // par le fichier de base par défaut (Data Source=...\mmv.db).
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new OpticDbContext(options);

        context.Database.ProviderName.Should().Be(SqliteProviderName);
        context.Database.GetConnectionString().Should().Be(connection.ConnectionString);
        context.Database.GetConnectionString().Should().NotContain(SqliteDatabasePathResolver.DefaultFileName);
    }

    // ---------------------------------------------------------------------------------------------
    // 7. P4-5E — assembly de migrations sélectionnée au même endroit que le fournisseur (D-01, D-06).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Configure_Sqlite_DeclaresNoExplicitMigrationsAssembly()
    {
        // D-06.1 : la branche SQLite garde l'assembly implicite du contexte. Un MigrationsAssembly explicite
        // ne changerait rien au runtime, mais serait une différence de plus à justifier sur la chaîne SQLite.
        var builder = new DbContextOptionsBuilder<OpticDbContext>();

        DatabaseProviderResolver.Configure(builder, DatabaseProviderOptions.Sqlite, InMemorySqlitePath());

        RelationalOptionsExtension.Extract(builder.Options).MigrationsAssembly.Should().BeNull();
    }

    [Fact]
    public void Configure_PostgreSql_DeclaresTheDedicatedMigrationsAssembly()
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        var options = new DatabaseProviderOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = FakeServerConnectionString
        };

        DatabaseProviderResolver.Configure(builder, options);

        RelationalOptionsExtension.Extract(builder.Options).MigrationsAssembly
            .Should().Be(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName);
    }

    // ---------------------------------------------------------------------------------------------
    // 8. P4-5E — étanchéité runtime : la variable design-time n'influence jamais le runtime (D-05.2).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_IgnoresTheDesignTimeProviderVariable()
    {
        var options = DatabaseProviderResolver.Resolve(Env(
            (OpticDbContextFactory.DesignTimeProviderVariableName, "postgresql")));

        options.Provider.Should().Be(DatabaseProvider.Sqlite,
            "MMV_DESIGNTIME_DATABASE_PROVIDER ne sert qu'à dotnet ef : un poste ne bascule jamais sur un " +
            "serveur parce qu'un développeur a laissé cette variable dans son shell");
    }

    [Fact]
    public void Resolve_WithInvalidDesignTimeVariable_DoesNotThrow()
    {
        // Une valeur design-time invalide bloque dotnet ef (OpticDbContextFactory), jamais le démarrage.
        var act = () => DatabaseProviderResolver.Resolve(Env(
            (OpticDbContextFactory.DesignTimeProviderVariableName, "postgrse")));

        act.Should().NotThrow();
    }

    /// <summary>
    /// Chemin de fichier SQLite déterministe et jamais créé : les tests de cette classe ne configurent
    /// que des options EF, sans ouvrir ni matérialiser de base.
    /// </summary>
    private static string InMemorySqlitePath()
        => Path.Combine(Path.GetTempPath(), "mmv-p4-3-" + Guid.NewGuid().ToString("N") + ".db");
}
