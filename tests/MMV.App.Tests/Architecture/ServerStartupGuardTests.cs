using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MMV.App.Tests.Architecture;

/// <summary>
/// P4-5E — Non-régression du garde-fou de démarrage serveur (risque R-E16, ADR-PROD-DB-005).
///
/// <para>
/// P4-5E ajoute une chaîne de migrations PostgreSQL et modifie <c>DatabaseProviderResolver.Configure</c>,
/// mais <b>ne lève pas</b> le garde-fou : la préparation d'une base serveur n'existe pas encore (P4-5G,
/// après P4-5F). Tant qu'il est actif, un poste configuré sur PostgreSQL refuse de démarrer au lieu
/// d'exécuter le cycle de vie SQLite (sauvegarde, migrations, adoption) contre un serveur.
/// </para>
///
/// <para>
/// <b>Preuve statique, volontairement.</b> Le garde-fou vit dans la composition privée de
/// <c>App.axaml.cs</c>, que P4-5E n'a pas le droit de modifier ; l'exécuter exigerait de muter
/// l'environnement du processus, partagé par les tests parallèles. Ce test lit donc le source et vérifie
/// que le blocage existe et qu'il précède toute préparation de base. Il doit être <b>mis à jour
/// délibérément</b> par le lot qui lèvera le garde-fou (P4-5G), jamais contourné.
/// </para>
/// </summary>
public sealed class ServerStartupGuardTests
{
    private const string Guard = "if (!usesSqlite)";

    [Fact]
    public void ProviderSelection_ComesFromTheRuntimeResolver()
    {
        var source = AppCompositionSource();

        Assert.Contains("var databaseProviderOptions = DatabaseProviderResolver.Resolve();", source);
        Assert.Contains(
            "var usesSqlite = databaseProviderOptions.Provider == DatabaseProvider.Sqlite;", source);
    }

    [Fact]
    public void ServerProvider_IsBlocked_BeforeAnyDatabasePreparation()
    {
        var source = AppCompositionSource();

        var guard = source.IndexOf(Guard, StringComparison.Ordinal);
        Assert.True(guard >= 0, "Le garde-fou de démarrage serveur a disparu de App.axaml.cs.");

        var block = source.Substring(guard, source.IndexOf('}', guard) - guard);
        Assert.Contains("throw new DatabaseConfigurationException(", block);

        // Le blocage n'est enveloppé dans aucun try : aucun catch générique ne peut le masquer.
        var firstTry = Regex.Match(source, @"^\s*try\s*$", RegexOptions.Multiline);
        Assert.True(firstTry.Success && firstTry.Index > guard,
            "Le garde-fou serveur doit précéder le premier bloc try de App.axaml.cs.");

        // Et il précède toute étape du cycle de vie SQLite.
        foreach (var sqliteStep in new[] { "new SqliteDatabaseManager(", ".PrepareDatabase(", ".Seed(" })
        {
            var position = source.IndexOf(sqliteStep, StringComparison.Ordinal);
            Assert.True(position > guard, $"« {sqliteStep} » doit suivre le garde-fou serveur.");
        }
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
