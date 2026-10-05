using System;
using System.IO;
using System.Text.RegularExpressions;
using MMV.App.ViewModels;
using Xunit;

namespace MMV.App.Tests.Architecture;

/// <summary>
/// P4-6C — démarrage d'un poste PostgreSQL (ADR-PROD-DB-009 DP-1, DP-4 ; ADR-PROD-DB-010 D-12.3, D-15). Remplace la
/// preuve du garde-fou P4-3 / P4-5E, levé délibérément par ce lot.
///
/// <para>
/// <b>Preuve statique, volontairement</b> (même motif qu'en P4-5E) : la composition privée de <c>App.axaml.cs</c>
/// dépend de l'environnement du processus, partagé par les tests parallèles. Le comportement du contrôle lui-même
/// (<c>ServerStartupCheck</c>) est prouvé contre un vrai PostgreSQL par la suite d'intégration.
/// </para>
/// </summary>
public sealed class ServerStartupGuardTests
{
    private const string ServerBranch = "if (!usesSqlite)";

    [Fact]
    public void ProviderSelection_ComesFromTheRuntimeResolver()
    {
        var source = AppCompositionSource();

        Assert.Contains("var databaseProviderOptions = DatabaseProviderResolver.Resolve();", source);
        Assert.Contains(
            "var usesSqlite = databaseProviderOptions.Provider == DatabaseProvider.Sqlite;", source);
    }

    [Fact]
    public void ServerProvider_RunsTheStartupCheck_OutsideAnyTry_AndNeverReachesTheSqliteLifecycle()
    {
        var source = AppCompositionSource();

        var branch = source.IndexOf(ServerBranch, StringComparison.Ordinal);
        Assert.True(branch >= 0, "La branche serveur de App.axaml.cs a disparu.");
        var block = source.Substring(branch, source.IndexOf("return serviceProvider;", branch, StringComparison.Ordinal) - branch);

        Assert.Contains("ServerStartupCheck.Run(", block);
        Assert.DoesNotContain("throw new DatabaseConfigurationException(", block);

        // Hors de tout try : aucun catch générique ne peut avaler un blocage (K-13).
        var firstTry = Regex.Match(source, @"^\s*try\s*$", RegexOptions.Multiline);
        Assert.True(firstTry.Success && firstTry.Index > branch, "Le contrôle serveur doit précéder le premier bloc try.");

        // La branche serveur se termine avant tout le cycle de vie SQLite et tout seed (DP-1, D-12.3).
        var exit = source.IndexOf("return serviceProvider;", branch, StringComparison.Ordinal);
        foreach (var sqliteStep in new[] { "new SqliteDatabaseManager(", ".PrepareDatabase(", ".Seed(", "LegacyDatabaseRecoveryService" })
        {
            var position = source.IndexOf(sqliteStep, StringComparison.Ordinal);
            Assert.True(position > exit, $"« {sqliteStep} » ne doit être atteint que par la branche SQLite.");
        }

        foreach (var forbidden in new[] { "Migrate(", "EnsureCreated(", "DatabaseSeeder" })
        {
            Assert.DoesNotContain(forbidden, block);
        }
    }

    [Fact]
    public void BlockedStartup_ShowsTheBlockingWindow_InsteadOfTheLogin()
    {
        var source = AppCompositionSource();

        Assert.Matches(new Regex(@"if \(_startupBlock is not null\)\s*\{\s*ShowBlockingWindow\(desktop, _startupBlock\);\s*\}\s*else\s*\{\s*ShowLoginWindow\(desktop\);"), source);
        Assert.Contains("new DatabaseBlockedView", source);
    }

    [Fact]
    public void BlockingScreen_OnlyOffersToQuit()
    {
        var quitCalls = 0;
        var viewModel = new DatabaseBlockedViewModel("Titre", "Constat", "Action", () => quitCalls++);

        viewModel.QuitCommand.Execute(null);

        Assert.Equal(1, quitCalls);
        Assert.Equal(("Titre", "Constat", "Action"), (viewModel.Title, viewModel.Message, viewModel.Action));
        Assert.DoesNotContain(typeof(DatabaseBlockedViewModel).GetProperties(), p => p.Name.Contains("Retry", StringComparison.Ordinal));
    }

    private static string AppCompositionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null,
            $"Le fichier solution MMV.sln doit être trouvable en remontant depuis {AppContext.BaseDirectory}.");

        var path = Path.Combine(dir!.FullName, "src", "MMV.App", "App.axaml.cs");
        Assert.True(File.Exists(path), $"Source de composition introuvable : {path}");

        return File.ReadAllText(path);
    }
}
