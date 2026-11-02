using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;

namespace BedrockLan.RakNet;

/// <summary>Queries a RakNet Bedrock server with an unconnected ping.</summary>
public static class RakNetPinger
{
    public static async Task<BedrockMotd?> PingAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var address = await UdpSockets.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        var target = new IPEndPoint(address, port);
        using var socket = UdpSockets.Create(UdpSockets.AnyEndPoint(address.AddressFamily), reuseAddress: false, broadcast: false);

        var clientGuid = Random.Shared.NextInt64();
        var buffer = new byte[2048];

        // Two attempts within the timeout to ride out a single dropped datagram.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var writer = new ByteWriter(33);
            writer.Byte(RakNetProtocol.UnconnectedPing);
            writer.Int64BE(RakNetProtocol.Timestamp());
            writer.Bytes(RakNetProtocol.Magic);
            writer.Int64BE(clientGuid);
            await socket.SendToAsync(writer.ToArray(), SocketFlags.None, target, cancellationToken).ConfigureAwait(false);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout / 2);
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, UdpSockets.AnyEndPoint(address.AddressFamily), attemptCts.Token).ConfigureAwait(false);
                    var motd = TryParsePong(buffer, result.ReceivedBytes);
                    if (motd is not null)
                    {
                        return motd;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Attempt timed out; retry.
            }
            catch (SocketException)
            {
                // e.g. ICMP unreachable on some platforms; retry.
            }
        }

        return null;
    }

    internal static BedrockMotd? TryParsePong(byte[] data, int length)
    {
        // id(1) + time(8) + guid(8) + magic(16) + strlen(2)
        if (length < 35 || data[0] != RakNetProtocol.UnconnectedPong || !RakNetProtocol.HasMagicAt(data.AsSpan(0, length), 17))
        {
            return null;
        }

        try
        {
            var reader = new ByteReader(data, 33, length - 33);
            return BedrockMotd.TryParse(reader.ShortStringBE(), out var motd) ? motd : null;
        }
        catch (PacketFormatException)
        {
            return null;
        }
    }
}
