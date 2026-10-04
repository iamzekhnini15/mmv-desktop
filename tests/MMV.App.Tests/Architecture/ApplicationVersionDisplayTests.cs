using System;
using System.IO;
using System.Text.RegularExpressions;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Infrastructure.Configuration;
using Moq;
using Xunit;

namespace MMV.App.Tests.Architecture;

/// <summary>
/// P4-6B tranche A, U-A3 (DP-7.3) — la version affichée vient de la source unique
/// (<see cref="ApplicationVersion"/>), jamais d'un littéral dans la vue.
/// </summary>
public sealed class ApplicationVersionDisplayTests
{
    [Fact]
    public void SettingsView_contains_no_version_literal_and_binds_Version()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "MMV.App", "Views", "SettingsView.axaml");
        var axaml = File.ReadAllText(path);

        Assert.False(Regex.IsMatch(axaml, @"""\s*v?\d+\.\d+\.\d+\s*"""),
            "SettingsView.axaml ne doit contenir aucun littéral de version (DP-7.3).");
        Assert.Contains("{Binding Version}", axaml);
    }

    [Fact]
    public void SettingsViewModel_Version_is_the_single_source()
    {
        var viewModel = new SettingsViewModel(Mock.Of<IThemeService>());

        Assert.Equal(ApplicationVersion.Current, viewModel.Version);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null,
            $"Le fichier solution MMV.sln doit être trouvable en remontant depuis {AppContext.BaseDirectory}.");
        return dir!.FullName;
    }
}
