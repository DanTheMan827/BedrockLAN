using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;

namespace BedrockLan.RakNet;

internal static class RakNetProtocol
{
    /// <summary>RakNet protocol version used by Minecraft: Bedrock Edition.</summary>
    public const byte ProtocolVersion = 11;

    public const int MinMtu = 400;
    public const int MaxMtu = 1492;

    public const byte ConnectedPing = 0x00;
    public const byte UnconnectedPing = 0x01;
    public const byte UnconnectedPingOpenConnections = 0x02;
    public const byte ConnectedPong = 0x03;
    public const byte OpenConnectionRequest1 = 0x05;
    public const byte OpenConnectionReply1 = 0x06;
    public const byte OpenConnectionRequest2 = 0x07;
    public const byte OpenConnectionReply2 = 0x08;
    public const byte ConnectionRequest = 0x09;
    public const byte ConnectionRequestAccepted = 0x10;
    public const byte NewIncomingConnection = 0x13;
    public const byte DisconnectNotification = 0x15;
    public const byte IncompatibleProtocolVersion = 0x19;
    public const byte UnconnectedPong = 0x1C;
    public const byte GamePacket = 0xFE;

    public static readonly byte[] Magic =
    {
        0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE,
        0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78,
    };

    public static bool HasMagicAt(ReadOnlySpan<byte> data, int offset) =>
        data.Length >= offset + Magic.Length && data.Slice(offset, Magic.Length).SequenceEqual(Magic);

    public static long Timestamp() => Environment.TickCount64;

    public static void WriteAddress(ByteWriter writer, IPEndPoint? endPoint)
    {
        if (endPoint is null)
        {
            // 0.0.0.0:0 (bytes are stored inverted).
            writer.Byte(4);
            writer.Bytes(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
            writer.UInt16BE(0);
            return;
        }

        var address = endPoint.Address;
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            writer.Byte(4);
            for (var i = 0; i < 4; i++)
            {
                writer.Byte((byte)~bytes[i]);
            }

            writer.UInt16BE((ushort)endPoint.Port);
        }
        else
        {
            writer.Byte(6);
            writer.UInt16LE(23); // AF_INET6 as defined on Windows, which is what RakNet sends.
            writer.UInt16BE((ushort)endPoint.Port);
            writer.Int32BE(0); // flow info
            writer.Bytes(address.GetAddressBytes());
            writer.Int32BE((int)address.ScopeId);
        }
    }

    public static IPEndPoint ReadAddress(ByteReader reader)
    {
        var version = reader.Byte();
        if (version == 6)
        {
            reader.Skip(2); // family
            int port = reader.UInt16BE();
            reader.Skip(4); // flow info
            var address = new IPAddress(reader.Bytes(16));
            reader.Skip(4); // scope id
            return new IPEndPoint(address, port);
        }

        var raw = reader.Bytes(4);
        for (var i = 0; i < 4; i++)
        {
            raw[i] = (byte)~raw[i];
        }

        return new IPEndPoint(new IPAddress(raw), reader.UInt16BE());
    }
}
