using System.Reflection;
using System.Text.RegularExpressions;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Version applicative de MMV, lue à l'exécution (P4-6B, ADR-PROD-DB-009 DP-7, H7).
///
/// <para>
/// Source unique : la propriété <c>Version</c> de <c>Directory.Build.props</c>. Elle est lue sur
/// l'assembly qui porte <b>ce type</b> (<c>MMV.Infrastructure</c>), jamais sur l'assembly d'entrée :
/// <c>MMV.App</c> et <c>MMV.DatabaseManager</c> partagent Infrastructure et annoncent donc la même version
/// (DI-5.2).
/// </para>
///
/// <para>
/// Format : <c>Major.Minor.Patch</c> uniquement. Le suffixe <c>+&lt;sha&gt;</c> que le SDK ajoute à la version
/// informative est retiré ; une étiquette de pré-version (<c>-rc.1</c>) est refusée, car son ordre SemVer ne se
/// lit pas avec <see cref="System.Version"/> et aucune release de production n'en porte (§5.4 du plan).
/// </para>
/// </summary>
public static class ApplicationVersion
{
    private static readonly Regex SemVerCore = new(@"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant);

    /// <summary>Version de cette build, <c>Major.Minor.Patch</c>.</summary>
    public static string Current { get; } = Normalize(
        typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty);

    /// <summary>
    /// Retire le suffixe de révision source (tout ce qui suit le premier <c>+</c>) et exige le format
    /// <c>Major.Minor.Patch</c>.
    /// </summary>
    /// <exception cref="FormatException">Si la valeur n'est pas un triplet SemVer sans pré-version.</exception>
    public static string Normalize(string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);

        var plus = informationalVersion.IndexOf('+');
        var core = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();

        if (!SemVerCore.IsMatch(core))
        {
            throw new FormatException(
                $"Version applicative invalide '{core}' : format attendu Major.Minor.Patch, sans pré-version.");
        }

        return core;
    }

    /// <summary>
    /// Analyse une version <c>Major.Minor.Patch</c> pour comparaison numérique. Toute autre forme (pré-version,
    /// deux ou quatre composantes, vide) est refusée.
    /// </summary>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (value is null || !SemVerCore.IsMatch(value))
        {
            return false;
        }

        return Version.TryParse(value, out version!);
    }
}
