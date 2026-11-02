using System.Net;
using System.Net.Sockets;

namespace BedrockLan.Internal;

internal static class UdpSockets
{
    // SIO_UDP_CONNRESET: stops Windows from surfacing ICMP "port unreachable" as receive errors.
    private const int SioUdpConnReset = unchecked((int)0x9800000C);

    public static Socket Create(IPEndPoint bind, bool reuseAddress, bool broadcast)
    {
        var socket = new Socket(bind.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (reuseAddress)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            if (broadcast)
            {
                socket.EnableBroadcast = true;
            }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    socket.IOControl(SioUdpConnReset, new byte[4], null);
                }
                catch (SocketException)
                {
                    // Not fatal; receive loops tolerate ConnectionReset anyway.
                }
            }

            socket.Bind(bind);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static IPEndPoint AnyEndPoint(AddressFamily family) =>
        new(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

    /// <summary>Sends a datagram, swallowing transient socket errors (UDP is best effort).</summary>
    public static void TrySend(Socket socket, byte[] data, EndPoint remote, LanLogger? log)
    {
        try
        {
            socket.SendTo(data, SocketFlags.None, remote);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException ex)
        {
            log.Debug($"UDP send to {remote} failed: {ex.SocketErrorCode}");
        }
    }

    public static async Task<IPAddress> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? throw new SocketException((int)SocketError.HostNotFound);
    }

    public static ulong RandomId()
    {
        ulong id;
        do
        {
            id = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        } while (id == 0);

        return id;
    }
}
