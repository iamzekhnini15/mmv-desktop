using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Fabrique de connexions dérivées, pour les expérimentations de PANNE (E13, E14). Les chaînes
/// proviennent toujours de l'environnement : rien n'est écrit en dur, aucun secret n'est journalisé.
/// </summary>
public static class SpikeConnections
{
    /// <summary>Port volontairement fermé, pour mesurer « connexion refusée ».</summary>
    private const int UnreachablePort = 59999;

    public static DbConnection Create(ProviderKind provider, string connectionString)
        => provider == ProviderKind.Postgres
            ? new NpgsqlConnection(connectionString)
            : new SqlConnection(connectionString);

    /// <summary>
    /// Même chaîne, mais pointant un port fermé sur la boucle locale. Le délai d'attente est
    /// volontairement court pour que la mesure reste bornée.
    /// </summary>
    public static string WithUnreachablePort(ProviderKind provider)
    {
        var baseConnection = SpikeEnvironment.For(provider)
            ?? throw new InvalidOperationException(SpikeEnvironment.SkipReason(provider));

        if (provider == ProviderKind.Postgres)
        {
            return new NpgsqlConnectionStringBuilder(baseConnection)
            {
                Port = UnreachablePort,
                Timeout = 5
            }.ConnectionString;
        }

        return new SqlConnectionStringBuilder(baseConnection)
        {
            DataSource = $"127.0.0.1,{UnreachablePort}",
            ConnectTimeout = 5
        }.ConnectionString;
    }

    /// <summary>
    /// Même chaîne, avec un délai de connexion court. Indispensable pour E14 : après l'arrêt du
    /// conteneur, un <c>Connect Timeout</c> de 60 s rendrait la mesure interminable.
    /// </summary>
    public static string WithShortConnectTimeout(ProviderKind provider, int seconds)
    {
        var baseConnection = SpikeEnvironment.For(provider)
            ?? throw new InvalidOperationException(SpikeEnvironment.SkipReason(provider));

        if (provider == ProviderKind.Postgres)
        {
            return new NpgsqlConnectionStringBuilder(baseConnection) { Timeout = seconds }.ConnectionString;
        }

        return new SqlConnectionStringBuilder(baseConnection) { ConnectTimeout = seconds }.ConnectionString;
    }

    /// <summary>Purge du pool : sans elle, une connexion morte est rendue par le pool après redémarrage.</summary>
    public static void ClearPools(ProviderKind provider)
    {
        if (provider == ProviderKind.Postgres)
        {
            NpgsqlConnection.ClearAllPools();
        }
        else
        {
            SqlConnection.ClearAllPools();
        }
    }
}
