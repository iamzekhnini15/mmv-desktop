using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-6B tranche A (DP-7, H7) — version applicative unique, lue à l'exécution.
/// U-A1 : format <c>Major.Minor.Patch</c>, jamais de suffixe <c>+sha</c>.
/// U-A2 : la version est celle de l'assembly <b>Infrastructure</b>, jamais celle de l'assembly d'entrée
/// (DI-5.2 : <c>MMV.App</c> et <c>MMV.DatabaseManager</c> doivent annoncer la même version).
/// </summary>
public sealed class ApplicationVersionTests
{
    [Fact]
    public void Current_is_Major_Minor_Patch_without_source_revision_suffix()
    {
        ApplicationVersion.Current.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void Current_comes_from_the_Infrastructure_assembly()
    {
        var infrastructure = typeof(OpticDbContext).Assembly;
        var informational = infrastructure
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        ApplicationVersion.Current.Should().Be(informational.Split('+')[0]);
        typeof(ApplicationVersion).Assembly.Should().BeSameAs(infrastructure);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc123def", "1.2.3")]
    [InlineData(" 10.0.7+sha ", "10.0.7")]
    public void Normalize_strips_the_source_revision_suffix(string informational, string expected)
    {
        ApplicationVersion.Normalize(informational).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.x")]
    public void Normalize_refuses_anything_but_Major_Minor_Patch(string informational)
    {
        var act = () => ApplicationVersion.Normalize(informational);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("0.0.0", true)]
    [InlineData("1.2.3-rc.1", false)]
    [InlineData("1.2", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void TryParse_accepts_only_Major_Minor_Patch(string? value, bool expected)
    {
        ApplicationVersion.TryParse(value, out _).Should().Be(expected);
    }

    [Fact]
    public void TryParse_compares_numerically_not_lexically()
    {
        ApplicationVersion.TryParse("1.10.0", out var newer).Should().BeTrue();
        ApplicationVersion.TryParse("1.9.0", out var older).Should().BeTrue();

        newer.Should().BeGreaterThan(older);
    }

    [Fact]
    public void Source_never_reads_the_entry_assembly()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "MMV.Infrastructure", "Configuration", "ApplicationVersion.cs"));

        // Aucun appel réel (commentaires XML exclus) : seule la forme d'appel est interdite.
        Regex.IsMatch(source, @"GetEntryAssembly\s*\(").Should().BeFalse(
            "la version doit venir de l'assembly Infrastructure, partagée par les deux exécutables (DI-5.2)");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;

        dir.Should().NotBeNull("MMV.sln doit être trouvable en remontant depuis le dossier de sortie");
        return dir!.FullName;
    }
}
