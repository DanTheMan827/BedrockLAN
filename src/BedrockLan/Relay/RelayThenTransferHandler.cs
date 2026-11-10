using System.Collections.Concurrent;
using BedrockLan.NetherNet;

namespace BedrockLan;

/// <summary>
/// NetherNet joins for an HTTP-signaling upstream in <see cref="NetherNetConnectMode.Auto"/>: forward the client's
/// offer to the server and send back its answer, so the client connects to it directly. If the server refuses the
/// relayed offer (authenticated servers do: the LAN offer's identity isn't one they accept) or can't be reached,
/// the same client messages are handed to the WebRTC transfer server instead, which answers and sends a Transfer.
/// A refusal is remembered for a while, so later joins skip the round trip.
/// </summary>
internal sealed class RelayThenTransferHandler : INetherNetSignalHandler, IDisposable
{
    private static readonly TimeSpan RefusalMemory = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromSeconds(60);

    private readonly HttpSignalingBridge _relay;
    private readonly INetherNetSignalHandler _transfer;
    private readonly string _name;
    private readonly LanLogger? _log;
    private readonly ConcurrentDictionary<(ulong ClientId, ulong ConnectionId), Connection> _connections = new();
    private long _refusedUntilTicks;

    /// <param name="createRelay">Creates the relay bridge, given the callback to call when a join can't be relayed.</param>
    /// <param name="transfer">The WebRTC transfer handler to fall back to.</param>
    public RelayThenTransferHandler(
        Func<Func<NetherNetClientMessage, ulong, Exception, bool>, HttpSignalingBridge> createRelay,
        INetherNetSignalHandler transfer,
        string name,
        LanLogger? log)
    {
        _relay = createRelay(OnRelayFailed);
        _transfer = transfer;
        _name = name;
        _log = log;
    }

    public void OnClientMessage(NetherNetClientMessage message)
    {
        if (!NetherNetSignal.TryParse(message.Data, out var signal))
        {
            _transfer.OnClientMessage(message); // keep-alives: the bridge ignores them anyway
            return;
        }

        var key = (message.ClientId, signal.ConnectionId);
        if (signal.Type == NetherNetSignal.Offer)
        {
            PruneStale();
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _refusedUntilTicks))
            {
                _connections.TryRemove(key, out _);
                _transfer.OnClientMessage(message);
                return;
            }

            var connection = new Connection();
            connection.Buffered.Add(message);
            _connections[key] = connection;
            _relay.OnClientMessage(message);
            return;
        }

        if (_connections.TryGetValue(key, out var existing))
        {
            lock (existing)
            {
                if (existing.FellBack)
                {
                    _transfer.OnClientMessage(message);
                }
                else
                {
                    existing.Buffered.Add(message);
                    _relay.OnClientMessage(message);
                }
            }

            return;
        }

        _transfer.OnClientMessage(message); // a join that went straight to the transfer server
    }

    /// <summary>The relay couldn't get an answer: replay the client's messages to the transfer server.</summary>
    private bool OnRelayFailed(NetherNetClientMessage message, ulong connectionId, Exception error)
    {
        if (HttpSignalingBridge.IsRejection(error))
        {
            Interlocked.Exchange(ref _refusedUntilTicks, (DateTime.UtcNow + RefusalMemory).Ticks);
            _log.Info($"[{_name}] the server refused the relayed join ({error.Message}); using the WebRTC transfer server for its joins for the next {RefusalMemory.TotalMinutes:0} minutes.");
        }

        if (!_connections.TryGetValue((message.ClientId, connectionId), out var connection))
        {
            return false;
        }

        lock (connection)
        {
            if (connection.FellBack)
            {
                return true;
            }

            connection.FellBack = true;
            _log.Info($"[{_name}] answering {message.ClientEndPoint} with the WebRTC transfer server instead.");
            foreach (var buffered in connection.Buffered)
            {
                _transfer.OnClientMessage(buffered);
            }

            connection.Buffered.Clear();
        }

        return true;
    }

    private void PruneStale()
    {
        var cutoff = DateTime.UtcNow - ConnectionLifetime;
        foreach (var entry in _connections)
        {
            if (entry.Value.Created < cutoff)
            {
                _connections.TryRemove(entry);
            }
        }
    }

    public void Dispose() => (_transfer as IDisposable)?.Dispose();

    private sealed class Connection
    {
        public List<NetherNetClientMessage> Buffered { get; } = new();

        public bool FellBack { get; set; }

        public DateTime Created { get; } = DateTime.UtcNow;
    }
}
