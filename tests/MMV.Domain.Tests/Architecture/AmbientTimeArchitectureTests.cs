using FluentAssertions;
using Xunit;

namespace MMV.Domain.Tests.Architecture;

/// <summary>
/// P4-5D — test d'architecture <b>bloquant</b> : le temps ambiant dépendant du fuseau du poste est
/// interdit dans <c>src/**</c> (ADR-PROD-DB-004 §5, décision 5, obligation T4).
///
/// <para>
/// <b>Pourquoi un test EN PLUS du convertisseur validant.</b> Les deux mécanismes attrapent des choses
/// différentes, et l'ADR les a explicitement retenus ensemble (options D2 <i>et</i> D3). Le convertisseur
/// ne voit que ce qui <b>franchit la frontière de persistance</b> : il n'aurait jamais attrapé
/// <c>OrderDetailViewModel</c>, où <c>DateTime.Now</c> servait à <b>soustraire</b> une heure locale d'un
/// instant UTC relu — un calcul faux qui ne touche aucune colonne. Ce test, lui, attrape l'appel dès son
/// écriture, y compris sur un chemin qu'aucun test fonctionnel n'exerce.
/// </para>
///
/// <para>
/// <b>L'échec survient au test, jamais en production</b> — c'est toute la valeur de la garde.
/// </para>
/// </summary>
public sealed class AmbientTimeArchitectureTests
{
    /// <summary>
    /// Les trois lectures d'horloge dépendantes du fuseau du poste, interdites par la décision 5.
    ///
    /// <para>
    /// <c>DateTime.UtcNow</c> n'y figure PAS : il produit un <c>Kind = Utc</c>, donc persistable et
    /// comparable entre postes. L'ADR demande de faire disparaître les <c>DateTime.Now</c>, pas de
    /// convertir les 61 <c>UtcNow</c> existants — ce serait un autre lot, au bénéfice bien moindre.
    /// </para>
    /// </summary>
    private static readonly string[] ForbiddenExpressions =
    {
        "DateTime.Now",
        "DateTime.Today",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
    };

    /// <summary>
    /// <b>Liste blanche — une seule entrée, et elle est justifiée.</b>
    ///
    /// <para>
    /// <c>SystemClock</c> est l'implémentation de l'horloge : quelqu'un doit bien lire l'horloge du
    /// système d'exploitation, et c'est précisément la décision 2 (« <c>DateTime.UtcNow</c> n'est plus
    /// appelé que dans l'implémentation »). L'appel y sert à produire la <b>date civile</b> du poste,
    /// laquelle n'a de sens que dans le calendrier de l'opérateur.
    /// </para>
    ///
    /// <para>
    /// L'ADR autorisait une liste blanche « limitée à la frontière d'affichage UI ». P4-5D n'en a
    /// finalement <b>ajouté aucune</b> : l'audit des trois sites UI a montré qu'aucun n'était de
    /// l'affichage — deux produisaient une valeur persistée, le troisième un calcul de retard. Les trois
    /// ont été corrigés au lieu d'être exemptés.
    /// </para>
    /// </summary>
    private static readonly string[] WhitelistedFiles =
    {
        Path.Combine("src", "MMV.Infrastructure", "Services", "SystemClock.cs"),
    };

    [Fact]
    public void AucunTempsAmbiant_DansSrc()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, file);
            if (WhitelistedFiles.Any(w => relative.Equals(w, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComment(lines[i]);
                foreach (var expression in ForbiddenExpressions)
                {
                    if (code.Contains(expression, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}:{i + 1} — {expression}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "le temps ambiant dépend du fuseau du poste : dans une base centrale partagée il produit un " +
            "ordre chronologique faux, et Npgsql refuse de l'écrire (ADR-PROD-DB-004 §2.1, §2.3). " +
            "Injecter IClock et lire IClock.UtcNow — ou IClock.LocalToday pour une DATE CIVILE. " +
            "Trouvé :" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void LaListeBlanche_NeContientQueSystemClock()
    {
        // La liste blanche est un point de dette : elle doit rester visible et minuscule. Ce test rend
        // impossible de l'élargir en silence — l'élargir exige de modifier CE test, donc de l'assumer.
        WhitelistedFiles.Should().ContainSingle()
            .Which.Should().EndWith("SystemClock.cs");
    }

    [Fact]
    public void ChaqueFichierDeLaListeBlanche_ExisteReellement()
    {
        // Une liste blanche dont une entrée pointe un fichier disparu ne protège plus rien : elle
        // laisserait croire à une exemption maîtrisée là où il n'y a plus qu'un vestige.
        var root = RepositoryRoot();

        foreach (var whitelisted in WhitelistedFiles)
        {
            File.Exists(Path.Combine(root, whitelisted))
                .Should().BeTrue($"l'entrée de liste blanche « {whitelisted} » doit désigner un fichier réel");
        }
    }

    [Fact]
    public void SystemClock_EstLeSeulFichierDeSrcAAppelerLHorlogeLocale()
    {
        // Formulation complémentaire du test principal, prise par l'autre bout : au lieu de vérifier que
        // personne d'autre n'appelle DateTime.Now, elle vérifie que SystemClock, lui, l'appelle bien.
        // Une implémentation qui aurait « corrigé » LocalToday en le dérivant d'UtcNow rendrait le test
        // principal vert tout en réintroduisant le décalage de jour que la décision 7 supprime.
        var root = RepositoryRoot();
        var systemClock = File.ReadAllText(Path.Combine(root, WhitelistedFiles[0]));

        systemClock.Should().Contain("DateTime.Now",
            "la date civile du poste ne peut être lue que depuis l'horloge locale");
        systemClock.Should().Contain("DateTime.UtcNow",
            "l'instant courant ne peut être lu que depuis l'horloge UTC");
    }

    [Fact]
    public void LesMigrations_NeSontPasAuditees()
    {
        // Constat explicite, pas un oubli : les migrations sont un journal historique immuable. P4-5D a
        // l'interdiction formelle d'y toucher, et les y inclure rendrait le test rouge pour des lignes
        // que personne n'a le droit de corriger.
        var root = RepositoryRoot();

        SourceFiles(root).Should().NotContain(f => f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));
    }

    private static IEnumerable<string> SourceFiles(string root)
        => Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Retire la partie commentaire d'une ligne.
    /// </summary>
    /// <remarks>
    /// Sans cela, le test serait rouge à cause de <c>DocumentSequenceConfiguration.cs</c>, dont un
    /// commentaire cite <c>DateTime.Now</c> pour expliquer pourquoi le seed ne l'utilise PAS — et rouge
    /// aussi à cause des commentaires posés par P4-5D lui-même pour documenter chaque remplacement.
    /// Interdire le mot jusque dans la prose interdirait d'expliquer la règle.
    /// </remarks>
    private static string StripComment(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MMV.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull(
            $"le fichier solution MMV.sln doit être trouvable en remontant depuis {AppContext.BaseDirectory}");
        return directory!.FullName;
    }
}
