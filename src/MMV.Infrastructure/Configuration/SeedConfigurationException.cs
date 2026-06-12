namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Levée lorsqu'une configuration de seeding/environnement est explicitement <b>invalide</b> et
/// doit bloquer le démarrage (P2A-1F : « configuration invalide bloquée »).
///
/// À distinguer d'une configuration <b>absente ou ambiguë</b> : celle-ci ne lève pas d'exception mais
/// retombe sur le défaut sûr (<see cref="ApplicationEnvironment.Production"/> sans seed). Seule une
/// valeur sensible fournie mais invalide (ex. mot de passe bootstrap trop faible) bloque.
/// </summary>
public sealed class SeedConfigurationException : Exception
{
    public SeedConfigurationException(string message) : base(message)
    {
    }

    public SeedConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
