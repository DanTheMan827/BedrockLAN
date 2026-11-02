using BedrockLan.Internal;

namespace BedrockLan.NetherNet;

/// <summary>
/// Application data carried (hex encoded) in a NetherNet LAN discovery response, format version 7.
/// </summary>
public sealed class NetherNetServerData
{
    public const byte FormatVersion = 7;

    public string ServerName { get; set; } = "";
    public int Protocol { get; set; }
    public string Version { get; set; } = "";
    public string LevelName { get; set; } = "";
    public int PlayerCount { get; set; }
    public int MaxPlayerCount { get; set; }
    public int GameType { get; set; }
    public bool EditorWorld { get; set; }
    public bool Hardcore { get; set; }
    public bool AcceptsOnlineAuth { get; set; } = true;
    public bool AcceptsSelfSignedAuth { get; set; }
    public string Nonce { get; set; } = "";
    public int ConnectionType { get; set; } = 4;

    public byte[] ToBytes()
    {
        var writer = new ByteWriter(64);
        writer.Byte(FormatVersion);
        writer.String(ServerName);
        writer.VarInt32(Protocol);
        writer.String(Version);
        writer.String(LevelName);
        writer.VarInt32(PlayerCount);
        writer.VarInt32(MaxPlayerCount);
        writer.VarInt32(GameType);
        writer.Bool(EditorWorld);
        writer.Bool(Hardcore);
        writer.Bool(AcceptsOnlineAuth);
        writer.Bool(AcceptsSelfSignedAuth);
        writer.String(Nonce);
        writer.VarInt32(ConnectionType);
        return writer.ToArray();
    }

    /// <summary>Parses version-7 data. Returns false for other versions or malformed data.</summary>
    public static bool TryParse(byte[] data, out NetherNetServerData? result)
    {
        result = null;
        try
        {
            var reader = new ByteReader(data);
            if (reader.Byte() != FormatVersion)
            {
                return false;
            }

            result = new NetherNetServerData
            {
                ServerName = reader.String(),
                Protocol = reader.VarInt32(),
                Version = reader.String(),
                LevelName = reader.String(),
                PlayerCount = reader.VarInt32(),
                MaxPlayerCount = reader.VarInt32(),
                GameType = reader.VarInt32(),
                EditorWorld = reader.Bool(),
                Hardcore = reader.Bool(),
                AcceptsOnlineAuth = reader.Bool(),
                AcceptsSelfSignedAuth = reader.Bool(),
                Nonce = reader.String(),
                ConnectionType = reader.VarInt32(),
            };
            return true;
        }
        catch (PacketFormatException)
        {
            result = null;
            return false;
        }
    }

    public NetherNetServerData Clone() => (NetherNetServerData)MemberwiseClone();
}
