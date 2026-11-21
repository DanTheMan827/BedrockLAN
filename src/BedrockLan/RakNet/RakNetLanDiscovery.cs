// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;

namespace BedrockLan.RakNet;

/// <summary>
/// Listens on the RakNet LAN discovery port (19132) for broadcast unconnected pings and makes every
/// advertised <see cref="RakNetTransferServer"/> answer from its own socket, so each one shows up as a
/// separate LAN entry at its own port.
/// </summary>
public sealed class RakNetLanDiscovery : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly Func<IEnumerable<RakNetTransferServer>> _servers;
    private readonly LanLogger? _log;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public RakNetLanDiscovery(IPEndPoint bind, Func<IEnumerable<RakNetTransferServer>> servers, LanLogger? log)
    {
        // ReuseAddress so we can coexist with a real server on the same machine (broadcasts reach both).
        _socket = UdpSockets.Create(bind, reuseAddress: true, broadcast: true);
        _servers = servers;
        _log = log;
    }

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
        var buffer = new byte[2048];
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

            if (result.ReceivedBytes < 25)
            {
                continue;
            }

            var id = buffer[0];
            if ((id != RakNetProtocol.UnconnectedPing && id != RakNetProtocol.UnconnectedPingOpenConnections)
                || !RakNetProtocol.HasMagicAt(buffer.AsSpan(0, result.ReceivedBytes), 9))
            {
                continue;
            }

            var pingTime = new ByteReader(buffer, 1, 8).Int64BE();
            var remote = (IPEndPoint)result.RemoteEndPoint;
            try
            {
                foreach (var server in _servers())
                {
                    server.SendPong(remote, pingTime);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"RakNet LAN discovery: failed to answer {remote}: {ex.Message}");
            }
        }
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
        _log.Debug("RakNet LAN discovery stopped.");
    }
}
