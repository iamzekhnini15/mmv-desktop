namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Résultat (non sensible) d'une exécution de <see cref="DatabaseSeeder"/> — destiné aux journaux de
/// démarrage et aux tests. Ne contient jamais de secret (« logs de démarrage non sensibles », P2A-1F).
/// </summary>
public sealed class SeedResult
{
    /// <summary>Environnement effectivement appliqué.</summary>
    public ApplicationEnvironment Environment { get; init; }

    /// <summary>Le jeu de données de démonstration a été appliqué.</summary>
    public bool DemoSeedApplied { get; init; }

    /// <summary>Un compte administrateur bootstrap a été provisionné/sécurisé à partir du secret fourni.</summary>
    public bool BootstrapAdminConfigured { get; init; }

    /// <summary>Un compte par défaut faible (<c>admin/admin</c>) a été neutralisé (désactivé).</summary>
    public bool WeakDefaultAdminNeutralized { get; init; }

    /// <summary>Note lisible décrivant la décision (non sensible).</summary>
    public string Notes { get; init; } = string.Empty;
}
