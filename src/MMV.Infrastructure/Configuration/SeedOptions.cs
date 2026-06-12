namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Paramètres typés gouvernant le seeding au démarrage (P2A-1F).
/// Construits par <see cref="SeedOptionsResolver"/> à partir de l'environnement, ou fournis
/// directement par les tests. Le défaut sûr (constructeur sans configuration) cible la
/// <see cref="ApplicationEnvironment.Production"/> sans seed de démonstration ni mot de passe bootstrap.
/// </summary>
public sealed class SeedOptions
{
    /// <summary>Environnement d'exécution résolu. Défaut : <see cref="ApplicationEnvironment.Production"/>.</summary>
    public ApplicationEnvironment Environment { get; init; } = ApplicationEnvironment.Production;

    /// <summary>
    /// Active le jeu de données de démonstration (<see cref="Data.DbInitializer"/>). N'a d'effet qu'en
    /// <see cref="ApplicationEnvironment.Development"/> ou <see cref="ApplicationEnvironment.Demonstration"/>.
    /// En production, le seed de démonstration est toujours désactivé (défense en profondeur).
    /// </summary>
    public bool EnableDemoSeed { get; init; }

    /// <summary>Nom d'utilisateur du compte administrateur bootstrap. Défaut : <c>admin</c>.</summary>
    public string BootstrapAdminUsername { get; init; } = SeedOptionsResolver.DefaultBootstrapAdminUsername;

    /// <summary>
    /// Mot de passe du compte administrateur bootstrap (secret, hors dépôt). Lorsqu'il est fourni, il doit
    /// respecter la politique forte (<see cref="Domain.Validators.UserValidator.ValidatePasswordPolicy"/>).
    /// <c>null</c>/vide = aucun compte bootstrap n'est provisionné automatiquement et tout compte par défaut
    /// faible (<c>admin/admin</c>) est neutralisé.
    /// </summary>
    public string? BootstrapAdminPassword { get; init; }

    /// <summary>Indique si un mot de passe bootstrap exploitable a été fourni.</summary>
    public bool HasBootstrapAdminPassword => !string.IsNullOrWhiteSpace(BootstrapAdminPassword);

    /// <summary>
    /// Indique si le jeu de démonstration doit réellement être appliqué (explicite ET environnement autorisé).
    /// </summary>
    public bool ShouldSeedDemoData =>
        EnableDemoSeed &&
        Environment is ApplicationEnvironment.Development or ApplicationEnvironment.Demonstration;
}
