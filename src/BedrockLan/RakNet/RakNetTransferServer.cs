using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BedrockLan.Bedrock;
using BedrockLan.Internal;

namespace BedrockLan.RakNet;

/// <summary>
/// A minimal RakNet endpoint for one advertised server. It answers pings with the forwarded status and,
/// when a (pre-26.60) client connects, performs the Bedrock login just far enough to send a Transfer packet
/// pointing at the real server. Game traffic is never proxied.
/// </summary>
public sealed class RakNetTransferServer : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly ConcurrentDictionary<IPEndPoint, RakNetConnection> _connections = new();
    private readonly string _name;
    private readonly Func<string?> _pongProvider;
    private readonly Func<TransferTarget?> _targetProvider;
    private readonly TransferOptions _options;
    private readonly LanLogger? _log;
    private readonly Action<IPEndPoint, TransferTarget, int>? _transferred;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _tickTask;

    /// <param name="bind">Local endpoint (its port is the one advertised to clients).</param>
    /// <param name="name">Name used in logs.</param>
    /// <param name="pongProvider">Returns the MOTD string to answer pings with, or null to stay silent.</param>
    /// <param name="targetProvider">Returns the transfer destination, or null if unknown/offline.</param>
    /// <param name="options">Transfer behaviour.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="transferred">Called after a client was sent a Transfer (client endpoint, target, client protocol). Keep it short.</param>
    public RakNetTransferServer(IPEndPoint bind, string name, Func<string?> pongProvider, Func<TransferTarget?> targetProvider, TransferOptions options, LanLogger? log, Action<IPEndPoint, TransferTarget, int>? transferred = null)
    {
        _transferred = transferred;
        _socket = UdpSockets.Create(bind, reuseAddress: false, broadcast: false);
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        Guid = Random.Shared.NextInt64(1, long.MaxValue);
        _name = name;
        _pongProvider = pongProvider;
        _targetProvider = targetProvider;
        _options = options;
        _log = log;
    }

    /// <summary>RakNet server GUID used in pongs and the handshake.</summary>
    public long Guid { get; }

    /// <summary>Bound UDP port.</summary>
    public int Port { get; }

    public int ActiveConnections => _connections.Count;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(token));
        _tickTask = Task.Run(() => TickLoopAsync(token));
    }

    /// <summary>Sends an unconnected pong from this server's socket (used for LAN broadcast discovery too).</summary>
    public void SendPong(IPEndPoint to, long pingTime)
    {
        var motd = _pongProvider();
        if (motd is null)
        {
            return;
        }

        var writer = new ByteWriter(64 + motd.Length * 2);
        writer.Byte(RakNetProtocol.UnconnectedPong);
        writer.Int64BE(pingTime);
        writer.Int64BE(Guid);
        writer.Bytes(RakNetProtocol.Magic);
        writer.ShortStringBE(motd);
        Send(writer.ToArray(), to);
    }

    private void Send(byte[] data, IPEndPoint to) => UdpSockets.TrySend(_socket, data, to, _log);

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[RakNetProtocol.MaxMtu + 64];
        var any = UdpSockets.AnyEndPoint(_socket.AddressFamily);
        while (!token.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                _log.Debug($"[{_name}] RakNet receive error: {ex.SocketErrorCode}");
                continue;
            }

            if (result.ReceivedBytes == 0)
            {
                continue;
            }

            var remote = (IPEndPoint)result.RemoteEndPoint;
            try
            {
                Handle(buffer.AsSpan(0, result.ReceivedBytes).ToArray(), remote);
            }
            catch (Exception ex)
            {
                _log.Debug($"[{_name}] Bad RakNet packet from {remote}: {ex.Message}");
            }
        }
    }

    private void Handle(byte[] data, IPEndPoint remote)
    {
        var id = data[0];
        if ((id & 0x80) != 0)
        {
            if (_connections.TryGetValue(remote, out var connection))
            {
                connection.HandleDatagram(data);
            }

            return;
        }

        switch (id)
        {
            case RakNetProtocol.UnconnectedPing:
            case RakNetProtocol.UnconnectedPingOpenConnections:
                if (data.Length >= 25 && RakNetProtocol.HasMagicAt(data, 9))
                {
                    SendPong(remote, new ByteReader(data, 1, 8).Int64BE());
                }

                break;
            case RakNetProtocol.OpenConnectionRequest1:
                HandleOpenConnectionRequest1(data, remote);
                break;
            case RakNetProtocol.OpenConnectionRequest2:
                HandleOpenConnectionRequest2(data, remote);
                break;
        }
    }

    private void HandleOpenConnectionRequest1(byte[] data, IPEndPoint remote)
    {
        if (data.Length < 18 || !RakNetProtocol.HasMagicAt(data, 1))
        {
            return;
        }

        var protocol = data[17];
        if (protocol != RakNetProtocol.ProtocolVersion)
        {
            var reject = new ByteWriter(26);
            reject.Byte(RakNetProtocol.IncompatibleProtocolVersion);
            reject.Byte(RakNetProtocol.ProtocolVersion);
            reject.Bytes(RakNetProtocol.Magic);
            reject.Int64BE(Guid);
            Send(reject.ToArray(), remote);
            _log.Debug($"[{_name}] {remote} uses RakNet protocol {protocol}; rejected.");
            return;
        }

        // The request is padded to the MTU the client is probing (minus 28 bytes of IP/UDP headers).
        var mtu = Math.Clamp(data.Length + 28, RakNetProtocol.MinMtu, RakNetProtocol.MaxMtu);
        var reply = new ByteWriter(28);
        reply.Byte(RakNetProtocol.OpenConnectionReply1);
        reply.Bytes(RakNetProtocol.Magic);
        reply.Int64BE(Guid);
        reply.Bool(false); // no security/cookie
        reply.UInt16BE((ushort)mtu);
        Send(reply.ToArray(), remote);
    }

    private void HandleOpenConnectionRequest2(byte[] data, IPEndPoint remote)
    {
        if (data.Length < 17 || !RakNetProtocol.HasMagicAt(data, 1))
        {
            return;
        }

        var reader = new ByteReader(data, 17, data.Length - 17);
        RakNetProtocol.ReadAddress(reader); // server address as seen by the client
        var mtu = Math.Clamp((int)reader.UInt16BE(), RakNetProtocol.MinMtu, RakNetProtocol.MaxMtu);
        var clientGuid = reader.Int64BE();

        var reply = new ByteWriter(64);
        reply.Byte(RakNetProtocol.OpenConnectionReply2);
        reply.Bytes(RakNetProtocol.Magic);
        reply.Int64BE(Guid);
        RakNetProtocol.WriteAddress(reply, remote);
        reply.UInt16BE((ushort)mtu);
        reply.Bool(false); // encryption
        Send(reply.ToArray(), remote);

        if (_connections.TryGetValue(remote, out var existing) && existing.ClientGuid == clientGuid && !existing.IsClosed)
        {
            return; // retransmitted request; the reply above is enough
        }

        var connection = new RakNetConnection(remote, mtu, clientGuid, Guid, Send, _log);
        var session = new TransferSession(
            $"[{_name}] RakNet {remote}",
            _targetProvider,
            _options,
            connection.SendGamePayload,
            delay => _ = CloseLaterAsync(connection, delay),
            _log,
            _transferred is null ? null : (target, protocol) => _transferred(remote, target, protocol));
        connection.GamePacketReceived = session.HandleBatch;
        connection.Closed += () => _connections.TryRemove(new KeyValuePair<IPEndPoint, RakNetConnection>(remote, connection));

        if (_connections.TryRemove(remote, out var replaced))
        {
            replaced.Disconnect();
        }

        _connections[remote] = connection;
        _log.Debug($"[{_name}] RakNet connection from {remote} (MTU {mtu}).");
    }

    private static async Task CloseLaterAsync(RakNetConnection connection, TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
        else
        {
            await Task.Yield();
        }

        connection.Disconnect();
    }

    private async Task TickLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;
                foreach (var (_, connection) in _connections)
                {
                    connection.Tick(now);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        foreach (var (_, connection) in _connections)
        {
            connection.Disconnect();
        }

        _socket.Dispose();
        foreach (var task in new[] { _receiveTask, _tickTask })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Shutting down.
            }
        }

        _cts?.Dispose();
    }
}
