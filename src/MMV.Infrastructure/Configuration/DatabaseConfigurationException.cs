namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Levée lorsqu'une configuration de <b>fournisseur de base de données</b> est explicitement invalide,
/// ou lorsqu'un fournisseur correctement sélectionné n'est pas encore exploitable au démarrage (P4-3).
///
/// Suit la convention de <see cref="SeedConfigurationException"/> : une configuration <b>absente</b>
/// retombe sur le défaut sûr (<see cref="DatabaseProvider.Sqlite"/>), tandis qu'une valeur <b>fournie
/// mais invalide</b> bloque le démarrage. Une faute de frappe sur le nom du fournisseur ne doit jamais
/// retomber silencieusement sur SQLite.
///
/// <para>
/// <b>Secrets</b> : le message peut nommer la variable d'environnement fautive, mais ne doit
/// <b>jamais</b> en restituer la valeur (une chaîne de connexion contient des identifiants).
/// </para>
/// </summary>
public sealed class DatabaseConfigurationException : Exception
{
    public DatabaseConfigurationException(string message) : base(message)
    {
    }

    public DatabaseConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
