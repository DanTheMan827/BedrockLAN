// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;

namespace BedrockLan.NetherNet;

public sealed record NetherNetLanQueryResult(ulong NetworkId, IPEndPoint EndPoint, byte[] ServerData);

/// <summary>Queries a NetherNet LAN host (e.g. a world opened to LAN on another subnet) by unicast discovery request.</summary>
public static class NetherNetLanQuery
{
    public static async Task<NetherNetLanQueryResult?> QueryAsync(IPEndPoint target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var socket = UdpSockets.Create(UdpSockets.AnyEndPoint(target.AddressFamily), reuseAddress: false, broadcast: false);
        var request = DiscoveryPacketCodec.EncodeRequest(UdpSockets.RandomId());
        var buffer = new byte[65536];

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await socket.SendToAsync(request, SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout / 2);
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, UdpSockets.AnyEndPoint(target.AddressFamily), attemptCts.Token).ConfigureAwait(false);
                    if (!DiscoveryPacketCodec.TryDecode(buffer.AsSpan(0, result.ReceivedBytes), out var packet)
                        || packet is null
                        || packet.Type != DiscoveryPacketType.Response)
                    {
                        continue;
                    }

                    try
                    {
                        var data = DiscoveryPacketCodec.DecodeResponseBody(packet.Body);
                        return new NetherNetLanQueryResult(packet.SenderId, target, data);
                    }
                    catch (PacketFormatException)
                    {
                        // keep listening
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // retry
            }
            catch (SocketException)
            {
                // retry
            }
        }

        return null;
    }
}
