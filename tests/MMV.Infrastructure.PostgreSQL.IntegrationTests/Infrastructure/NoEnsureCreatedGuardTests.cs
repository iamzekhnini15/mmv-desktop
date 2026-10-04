using System.Text.RegularExpressions;
using FluentAssertions;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// ADR-PROD-DB-007 S5 / ADR-PROD-DB-008 §5.5 : aucun test d'intégration PostgreSQL ne construit son schéma
/// par <c>EnsureCreated</c>. Ne requiert aucun serveur.
/// </summary>
public class NoEnsureCreatedGuardTests
{
    // Un APPEL de EnsureCreated / EnsureDeleted (synchrone ou Async) ; une simple mention n'en est pas un.
    private static readonly Regex ForbiddenCall = new(@"\.Ensure(Created|Deleted)(Async)?\s*\(");

    [Fact]
    public void No_integration_source_file_uses_EnsureCreated()
    {
        var projectDirectory = FindProjectDirectory();

        var offenders = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => ForbiddenCall.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(projectDirectory, path))
            .ToList();

        offenders.Should().BeEmpty("le schéma de test est créé par migrations, jamais par le modèle (S5)");
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MMV.Infrastructure.PostgreSQL.IntegrationTests.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Répertoire du projet d'intégration introuvable depuis " + AppContext.BaseDirectory);
    }
}
