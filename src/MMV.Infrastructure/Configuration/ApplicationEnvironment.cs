namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Environnement d'exécution de l'application (P2A-1F).
/// Sépare explicitement production, développement, démonstration et test afin de gouverner
/// le seeding (données de démonstration, compte bootstrap). Le défaut sûr est
/// <see cref="Production"/> : aucune donnée de démonstration, aucun compte faible.
/// </summary>
public enum ApplicationEnvironment
{
    /// <summary>
    /// Production : environnement réel. Aucune donnée de démonstration n'est créée automatiquement ;
    /// aucun compte <c>admin/admin</c> n'est laissé actif. Comportement par défaut (le plus restrictif).
    /// </summary>
    Production,

    /// <summary>
    /// Développement : le jeu de démonstration peut être seedé, mais uniquement de façon explicite
    /// (<see cref="SeedOptions.EnableDemoSeed"/>).
    /// </summary>
    Development,

    /// <summary>
    /// Démonstration : environnement de présentation. Le jeu de démonstration peut être seedé, mais
    /// uniquement de façon explicite (<see cref="SeedOptions.EnableDemoSeed"/>).
    /// </summary>
    Demonstration,

    /// <summary>
    /// Test : le seeding est entièrement contrôlé par les tests (aucun seeding implicite au démarrage).
    /// </summary>
    Test
}
