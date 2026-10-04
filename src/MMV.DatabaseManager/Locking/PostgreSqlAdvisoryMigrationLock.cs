using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

namespace MMV.DatabaseManager.Locking;

/// <summary>
/// Verrou consultatif PostgreSQL de <b>session</b> (P4-6B, LK-1 ; §3.3 du plan). Tenu par une connexion
/// <b>dédiée</b>, ouverte pour toute la durée de l'opération : il couvre la vérification de cohérence, le DDL
/// de métadonnée, <c>Migrate()</c>, la vérification, l'avancement de la métadonnée et le journal — quel que
/// soit le nombre de migrations — et il fonctionne sur une base <b>vide</b> (la clé n'est liée à aucun objet).
///
/// <para>
/// Le verrou natif d'EF Core (chez Npgsql : <c>LOCK TABLE</c> sur l'historique) reste actif dessous, sans être
/// désactivé ni pris pour référence. Acquisition par <c>pg_try_advisory_lock</c> dans une boucle bornée ;
/// libération explicite par <c>pg_advisory_unlock</c> — la fin de session n'est qu'un filet.
/// </para>
/// </summary>
public sealed class PostgreSqlAdvisoryMigrationLock : IMigrationLock
{
    /// <summary>
    /// Clé du verrou : « MMVMIGR1 » en ASCII. Déclarée en ce seul point et <b>ne change jamais</b> — deux
    /// versions de l'outil doivent s'exclure mutuellement. Un test la fige.
    /// </summary>
    public const long LockKey = 0x4D4D564D49475231;

    /// <summary>Moitié haute de la clé, telle que <c>pg_locks.classid</c> l'expose.</summary>
    public const long KeyHigh = LockKey >> 32;

    /// <summary>Moitié basse de la clé, telle que <c>pg_locks.objid</c> l'expose (<c>objsubid = 1</c>).</summary>
    public const long KeyLow = LockKey & 0xFFFFFFFF;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DbConnection _connection;
    private readonly TimeSpan _pollInterval;
    private bool _held;

    /// <param name="connection">Connexion dédiée au verrou, distincte de celle qui migre.</param>
    /// <param name="pollInterval">Intervalle entre deux tentatives pendant l'attente bornée.</param>
    public PostgreSqlAdvisoryMigrationLock(DbConnection connection, TimeSpan? pollInterval = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public async Task<bool> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken = default)
    {
        if (_held)
        {
            throw new InvalidOperationException("Le verrou de migration est déjà détenu par cette session.");
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (await ServerCommand.ScalarAsync(_connection, "SELECT pg_try_advisory_lock(@key)",
                    cancellationToken, ("key", LockKey)) is true)
            {
                _held = true;
                return true;
            }

            var remaining = wait - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken);
        }
    }

    public async Task<string?> DescribeHolderAsync(CancellationToken cancellationToken = default)
    {
        await using var command = ServerCommand.Create(_connection,
            "SELECT a.pid, a.usename, a.application_name, a.backend_start " +
            "FROM pg_catalog.pg_locks l JOIN pg_catalog.pg_stat_activity a ON a.pid = l.pid " +
            "WHERE l.locktype = 'advisory' AND l.granted " +
            // Les verrous consultatifs sont propres à chaque base : seul un détenteur de CETTE base compte.
            "AND l.database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) " +
            "AND l.classid::bigint = @high AND l.objid::bigint = @low AND l.objsubid = 1 LIMIT 1",
            ("high", KeyHigh), ("low", KeyLow));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"pid {reader.GetValue(0)}, rôle {reader.GetValue(1)}, application '{reader.GetValue(2)}', session ouverte depuis {reader.GetValue(3)}");
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_held)
            {
                _held = false;
                await ServerCommand.ScalarAsync(_connection, "SELECT pg_advisory_unlock(@key)",
                    cancellationToken, ("key", LockKey));
            }
        }
        finally
        {
            await _connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseAsync();
        }
        catch (DbException)
        {
            // Session perdue : le serveur a déjà libéré le verrou en fin de session.
        }
        catch (InvalidOperationException)
        {
            // Connexion déjà fermée ou perdue : même conséquence.
        }
    }
}
