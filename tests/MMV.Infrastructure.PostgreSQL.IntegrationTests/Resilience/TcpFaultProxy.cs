using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Resilience;

/// <summary>
/// Proxy TCP de test entre un client Npgsql et le serveur PostgreSQL de test (P4-10) : simule une <b>perte réseau</b>
/// réelle — sockets coupées, connexions nouvelles refusées — sans toucher au serveur. Une coupure peut être armée
/// pour survenir juste après qu'un motif (ex. <c>COMMIT</c>) a été <b>transmis au serveur</b> : le serveur exécute
/// l'instruction, le client ne reçoit jamais la réponse (issue inconnue).
/// </summary>
public sealed class TcpFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly List<(TcpClient Client, TcpClient Server)> _links = new();
    private readonly CancellationTokenSource _stop = new();
    private volatile bool _down;
    private byte[]? _cutAfter;

    public TcpFaultProxy(string targetConnectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(targetConnectionString);
        _targetHost = target.Host!;
        _targetPort = target.Port;
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Connexions acceptées depuis la création (chaque ouverture physique d'une connexion Npgsql).</summary>
    public int Accepted { get; private set; }

    /// <summary>Même chaîne, routée par le proxy (serveur de test sans TLS).</summary>
    public string Route(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Host = "127.0.0.1", Port = Port }.ConnectionString;

    /// <summary>Perte réseau : toutes les connexions coupées, les nouvelles refusées jusqu'à <see cref="Restore"/>.</summary>
    public void Down()
    {
        _down = true;
        CutAll();
    }

    /// <summary>Réseau rétabli : les nouvelles connexions passent.</summary>
    public void Restore() => _down = false;

    /// <summary>
    /// Arme une coupure : dès que le client envoie <paramref name="pattern"/>, le paquet est transmis au serveur, la
    /// réponse n'est jamais relayée, puis tout est coupé et le réseau reste coupé.
    /// </summary>
    public void CutAfterClientSends(string pattern) => _cutAfter = Encoding.ASCII.GetBytes(pattern);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        CutAll();
        await Task.Yield();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            if (_down)
            {
                client.Client.Close(0);
                continue;
            }

            Accepted++;
            var server = new TcpClient();
            try
            {
                await server.ConnectAsync(_targetHost, _targetPort);
            }
            catch (SocketException)
            {
                client.Client.Close(0);
                continue;
            }

            lock (_links)
            {
                _links.Add((client, server));
            }

            var link = new Link();
            _ = PumpAsync(client, server, toServer: true, link);
            _ = PumpAsync(server, client, toServer: false, link);
        }
    }

    private sealed class Link
    {
        public volatile bool Muted;
    }

    private async Task PumpAsync(TcpClient from, TcpClient to, bool toServer, Link link)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            var input = from.GetStream();
            var output = to.GetStream();
            while (true)
            {
                var read = await input.ReadAsync(buffer, _stop.Token);
                if (read == 0)
                {
                    break;
                }

                if (!toServer && link.Muted)
                {
                    continue;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), _stop.Token);
                if (toServer && _cutAfter is { } pattern && buffer.AsSpan(0, read).IndexOf(pattern) >= 0)
                {
                    _cutAfter = null;
                    link.Muted = true;
                    _down = true;
                    await Task.Delay(500);
                    CutAll();
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Coupure : la fin de l'une des deux sockets arrête le relais.
        }

        from.Client.Close(0);
        to.Client.Close(0);
    }

    private void CutAll()
    {
        lock (_links)
        {
            foreach (var (client, server) in _links)
            {
                client.Client.Close(0);
                server.Client.Close(0);
            }

            _links.Clear();
        }
    }
}
