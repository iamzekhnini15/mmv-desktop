using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace MMV.App.Tests.Architecture;

/// <summary>
/// P4-6B, C15 — <b>H11</b> (U-D1) : <c>MMV.App</c> ne contient aucun chemin d'exécution de DDL PostgreSQL.
///
/// <para>
/// Preuve par réflexion sur les assemblys : (1) aucune assembly atteignable depuis <c>MMV.App</c> n'est
/// <c>MMV.DatabaseManager</c>, et l'outil n'est pas copié à côté de l'application ; (2) aucune de ces assemblys
/// ne porte, dans son tas de chaînes utilisateur (le SQL littéral compilé), un marqueur de DDL serveur, de
/// verrou de migration ou d'écriture de la métadonnée. Le verrou, le journal et l'écriture de la métadonnée
/// n'existent que dans l'outil (DP-1).
/// </para>
/// </summary>
public sealed class NoServerDdlInApplicationTests
{
    private const string ToolAssemblyName = "MMV.DatabaseManager";

    /// <summary>Marqueurs de SQL serveur réservés à l'outil.</summary>
    private static readonly string[] ToolOnlySqlMarkers =
    [
        "pg_advisory", "pg_try_advisory", "lock_timeout", "CREATE SCHEMA", "GRANT ", "REVOKE ",
        "ALTER DEFAULT PRIVILEGES", "migration_run", "INSERT INTO mmv_meta", "UPDATE mmv_meta", "DELETE FROM mmv_meta"
    ];

    [Fact]
    public void App_does_not_reference_the_database_manager_directly_or_transitively()
    {
        var reachable = ReachableMmvAssemblies();

        Assert.Contains("MMV.App", reachable.Keys);
        Assert.Contains("MMV.Infrastructure", reachable.Keys);
        Assert.DoesNotContain(ToolAssemblyName, reachable.Keys);
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, ToolAssemblyName + ".dll")),
            "MMV.DatabaseManager.dll ne doit pas être copié à côté de l'application.");
    }

    [Fact]
    public void App_project_file_does_not_mention_the_database_manager()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var csproj = File.ReadAllText(Path.Combine(dir!.FullName, "src", "MMV.App", "MMV.App.csproj"));
        Assert.DoesNotContain(ToolAssemblyName, csproj);
    }

    [Fact]
    public void App_reachable_assemblies_carry_no_server_ddl_lock_or_metadata_write_sql()
    {
        var offenders = new List<string>();
        foreach (var (name, path) in ReachableMmvAssemblies())
        {
            foreach (var literal in UserStrings(path))
            {
                offenders.AddRange(ToolOnlySqlMarkers
                    .Where(marker => literal.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    .Select(marker => $"{name} : « {marker} » dans « {Truncate(literal)} »"));
            }
        }

        Assert.True(offenders.Count == 0,
            "Chemin de DDL serveur atteignable depuis l'application (H11) :\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void User_string_scan_is_not_vacuous()
    {
        // Garde anti-faux-vert : le scanner doit voir le SQL de lecture de la garde (Infrastructure).
        var infrastructure = ReachableMmvAssemblies()["MMV.Infrastructure"];

        Assert.Contains(UserStrings(infrastructure), s => s.Contains("mmv_meta.schema_compatibility", StringComparison.Ordinal));
    }

    /// <summary>Assemblys <c>MMV.*</c> atteignables depuis <c>MMV.App</c>, résolues dans le dossier de sortie des tests.</summary>
    private static Dictionary<string, string> ReachableMmvAssemblies()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>(["MMV.App"]);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (found.ContainsKey(name))
                continue;

            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            Assert.True(File.Exists(path), $"Assembly atteignable introuvable : {path}");
            found[name] = path;

            foreach (var reference in ReferencedNames(path))
            {
                if (reference.StartsWith("MMV.", StringComparison.Ordinal))
                    pending.Enqueue(reference);
            }
        }

        return found;
    }

    private static IEnumerable<string> ReferencedNames(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences
            .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
            .ToList();
    }

    private static List<string> UserStrings(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var strings = new List<string>();
        var size = reader.GetHeapSize(HeapIndex.UserString);
        var handle = MetadataTokens.UserStringHandle(1);
        while (!handle.IsNil && MetadataTokens.GetHeapOffset(handle) < size)
        {
            strings.Add(reader.GetUserString(handle));
            handle = reader.GetNextHandle(handle);
        }

        return strings;
    }

    private static string Truncate(string value) => value.Length <= 80 ? value : value[..80] + "…";
}
