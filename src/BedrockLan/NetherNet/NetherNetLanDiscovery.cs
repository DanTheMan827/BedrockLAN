using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;

namespace BedrockLan.NetherNet;

/// <summary>A signaling message a LAN client sent to one of our advertisements.</summary>
public sealed record NetherNetClientMessage(
    NetherNetLanDiscovery Hub,
    ulong AdvertisementId,
    ulong ClientId,
    IPEndPoint ClientEndPoint,
    string Data);

/// <summary>A message received for a relay id registered with <see cref="NetherNetLanDiscovery.RegisterRelay"/>.</summary>
public readonly record struct NetherNetRelayMessage(ulong RelayId, ulong SenderId, IPEndPoint From, string Data);

/// <summary>Handles signaling for one advertised NetherNet entry.</summary>
public interface INetherNetSignalHandler
{
    /// <summary>Called on the discovery receive loop; implementations must not block.</summary>
    void OnClientMessage(NetherNetClientMessage message);
}

/// <summary>
/// The NetherNet LAN discovery socket (UDP 7551). Answers client discovery requests with one response
/// per advertisement (each under its own network id) and routes signaling messages to handlers.
/// </summary>
public sealed class NetherNetLanDiscovery : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly LanLogger? _log;
    private readonly ConcurrentDictionary<ulong, Advertisement> _advertisements = new();
    private readonly ConcurrentDictionary<ulong, Action<NetherNetRelayMessage>> _relays = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    public NetherNetLanDiscovery(IPEndPoint bind, LanLogger? log)
    {
        _socket = UdpSockets.Create(bind, reuseAddress: true, broadcast: true);
        _log = log;
    }

    public void AddAdvertisement(ulong networkId, string name, Func<byte[]?> serverData, INetherNetSignalHandler handler)
    {
        if (!_advertisements.TryAdd(networkId, new Advertisement(name, serverData, handler)))
        {
            throw new InvalidOperationException($"Network id {networkId} is already advertised.");
        }
    }

    public void RemoveAdvertisement(ulong networkId) => _advertisements.TryRemove(networkId, out _);

    /// <summary>Allocates a fresh network id whose incoming messages are delivered to <paramref name="handler"/>.</summary>
    public ulong RegisterRelay(Action<NetherNetRelayMessage> handler)
    {
        while (true)
        {
            var id = UdpSockets.RandomId();
            if (!_advertisements.ContainsKey(id) && _relays.TryAdd(id, handler))
            {
                return id;
            }
        }
    }

    public void UnregisterRelay(ulong relayId) => _relays.TryRemove(relayId, out _);

    public void SendMessage(ulong senderId, ulong recipientId, string data, IPEndPoint to) =>
        UdpSockets.TrySend(_socket, DiscoveryPacketCodec.EncodeMessage(senderId, recipientId, data), to, _log);

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => LoopAsync(token));
    }

    private async Task LoopAsync(CancellationToken token)
    {
        var buffer = new byte[65536];
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
            catch (SocketException)
            {
                continue;
            }

            var remote = (IPEndPoint)result.RemoteEndPoint;
            try
            {
                Handle(buffer.AsSpan(0, result.ReceivedBytes), remote);
            }
            catch (Exception ex)
            {
                _log.Debug($"NetherNet discovery: bad packet from {remote}: {ex.Message}");
            }
        }
    }

    private void Handle(ReadOnlySpan<byte> data, IPEndPoint remote)
    {
        if (!DiscoveryPacketCodec.TryDecode(data, out var packet) || packet is null)
        {
            return;
        }

        // Ignore our own broadcasts/echoes.
        if (_advertisements.ContainsKey(packet.SenderId) || _relays.ContainsKey(packet.SenderId))
        {
            return;
        }

        switch (packet.Type)
        {
            case DiscoveryPacketType.Request:
                foreach (var (id, advertisement) in _advertisements)
                {
                    var serverData = advertisement.ServerData();
                    if (serverData is not null)
                    {
                        UdpSockets.TrySend(_socket, DiscoveryPacketCodec.EncodeResponse(id, serverData), remote, _log);
                    }
                }

                break;

            case DiscoveryPacketType.Message:
            {
                var (recipient, text) = DiscoveryPacketCodec.DecodeMessageBody(packet.Body);
                if (_advertisements.TryGetValue(recipient, out var advertisement))
                {
                    if (text.Length > 0 && text != "Ping")
                    {
                        _log.Debug($"NetherNet [{advertisement.Name}] <- {packet.SenderId}@{remote}: {Summarize(text)}");
                    }

                    advertisement.Handler.OnClientMessage(new NetherNetClientMessage(this, recipient, packet.SenderId, remote, text));
                }
                else if (_relays.TryGetValue(recipient, out var relay))
                {
                    relay(new NetherNetRelayMessage(recipient, packet.SenderId, remote, text));
                }

                break;
            }
        }
    }

    private static string Summarize(string text)
    {
        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _socket.Dispose();
        if (_task is not null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch
            {
                // Shutting down.
            }
        }

        _cts?.Dispose();
    }

    private sealed record Advertisement(string Name, Func<byte[]?> ServerData, INetherNetSignalHandler Handler);
}
