using BedrockLan.Internal;

namespace BedrockLan.Bedrock;

/// <summary>
/// The handful of Bedrock game packets the minimal transfer server needs, encoded per protocol version.
/// Version cut-offs follow CloudburstMC/Protocol's per-version serializers.
/// </summary>
internal static class BedrockPackets
{
    public const int IdLogin = 0x01;
    public const int IdPlayStatus = 0x02;
    public const int IdDisconnect = 0x05;
    public const int IdResourcePacksInfo = 0x06;
    public const int IdResourcePackStack = 0x07;
    public const int IdResourcePackClientResponse = 0x08;
    public const int IdTransfer = 0x55;
    public const int IdNetworkSettings = 0x8F;
    public const int IdRequestNetworkSettings = 0xC1;
    public const int IdStartGame = 0x0B;
    public const int IdRequestChunkRadius = 0x45;
    public const int IdChunkRadiusUpdated = 0x46;
    public const int IdSetLocalPlayerAsInitialised = 0x71;
    public const int IdCreativeContent = 0x91;
    public const int IdItemRegistry = 0xA2;
    public const int IdJigsawStructureData = 313;
    public const int IdVoxelShapes = 337;

    /// <summary>Entity runtime/unique id given to the (never really spawned) player.</summary>
    public const long PlayerEntityId = 1;

    /// <summary>1.19.30: RequestNetworkSettings/NetworkSettings handshake introduced.</summary>
    public const int ProtocolNetworkSettings = 554;

    /// <summary>1.20.60: batches carry a leading compression-algorithm byte.</summary>
    public const int ProtocolCompressionHeader = 649;

    public const int PlayStatusLoginSuccess = 0;
    public const int PlayStatusPlayerSpawn = 3;

    /// <summary>
    /// StartGame layouts are version-specific; only protocol 2225 (1.26.60) is encoded here, following
    /// gophertunnel / CloudburstMC v2225. Other protocols fall back to transferring before StartGame.
    /// </summary>
    public static bool SupportsSpawnStage(int protocol) => protocol == 2225;

    /// <summary>JigsawStructureData with the four empty lists vanilla sends.</summary>
    public static byte[] JigsawStructureData() => Build(IdJigsawStructureData, w =>
    {
        // Network little-endian NBT: compound "" { processors, template_pools, jigsaws, structure_sets: [] of compound }
        w.Byte(0x0A);
        w.VarUInt32(0);
        foreach (var name in new[] { "processors", "template_pools", "jigsaws", "structure_sets" })
        {
            w.Byte(0x09);
            w.String(name);
            w.Byte(0x0A);   // element type: compound
            w.VarInt32(0);  // length
        }

        w.Byte(0x00);
    });

    public static byte[] VoxelShapes() => Build(IdVoxelShapes, w =>
    {
        w.VarUInt32(0); // shapes
        w.VarUInt32(0); // name map
        w.UInt16LE(0);  // custom shape count
    });

    /// <summary>A minimal StartGame for protocol 2225: empty world, player floating at y=100 in adventure mode.</summary>
    public static byte[] StartGame(string worldName) => Build(IdStartGame, w =>
    {
        w.VarInt64(PlayerEntityId);   // entity unique id
        w.VarUInt64(PlayerEntityId);  // entity runtime id
        w.VarInt32(2);                // player game mode: adventure
        w.FloatLE(0); w.FloatLE(100); w.FloatLE(0); // position
        w.FloatLE(0);                 // pitch
        w.FloatLE(0);                 // yaw

        // Level settings
        w.Int64LE(0);                 // seed
        w.Int16LE(0);                 // spawn biome type
        w.String("");                 // user defined biome name
        w.VarInt32(0);                // dimension: overworld
        w.VarInt32(2);                // generator: flat
        w.VarInt32(2);                // world game mode: adventure
        w.Bool(false);                // hardcore
        w.VarInt32(1);                // difficulty
        w.VarInt32(0); w.VarInt32(100); w.VarInt32(0); // world spawn
        w.Bool(true);                 // achievements disabled
        w.VarInt32(0);                // editor world type
        w.Bool(false);                // created in editor
        w.Bool(false);                // exported from editor
        w.Byte(0);                    // editor level migration version
        w.VarInt32(0);                // day cycle lock time
        w.VarUInt32(0);               // education edition offer
        w.Bool(false);                // education features
        w.String("");                 // education product id
        w.FloatLE(0);                 // rain level
        w.FloatLE(0);                 // lightning level
        w.Bool(false);                // confirmed platform locked content
        w.Bool(true);                 // multiplayer game
        w.Bool(true);                 // LAN broadcast
        w.VarInt32(0);                // XBL broadcast mode
        w.VarInt32(0);                // platform broadcast mode
        w.Bool(false);                // commands enabled
        w.Bool(false);                // texture packs required
        w.VarUInt32(0);               // game rules
        w.UInt32LE(0);                // experiments
        w.Bool(false);                // experiments previously toggled
        w.Bool(false);                // bonus chest
        w.Bool(false);                // start with map
        w.Byte(1);                    // default player permissions: member
        w.Int32LE(4);                 // server chunk tick radius
        w.Bool(false);                // locked behaviour pack
        w.Bool(false);                // locked texture pack
        w.Bool(false);                // from locked world template
        w.Bool(false);                // MSA gamertags only
        w.Bool(false);                // from world template
        w.Bool(false);                // world template settings locked
        w.Bool(false);                // only spawn v1 villagers
        w.Bool(false);                // persona disabled
        w.Bool(false);                // custom skins disabled
        w.Bool(false);                // emote chat muted
        w.String("*");                // base game version
        w.Int32LE(0);                 // limited world width
        w.Int32LE(0);                 // limited world depth
        w.Bool(true);                 // new nether
        w.String("");                 // education shared resource: button name
        w.String("");                 // education shared resource: link
        w.Bool(false);                // force experimental gameplay: absent
        w.Byte(0);                    // chat restriction level
        w.Bool(false);                // disable player interactions
        w.VarInt32(0);                // server editor connection policy
        w.Bool(false);                // allow anonymous block drops in editor worlds

        w.String("");                 // level id
        w.String(worldName);          // world name
        w.String("");                 // template content identity
        w.Bool(false);                // trial
        w.VarInt32(0);                // movement: rewind history size
        w.Bool(false);                // movement: server authoritative block breaking
        w.Int64LE(0);                 // time
        w.VarInt32(0);                // enchantment seed
        w.VarUInt32(0);               // custom blocks
        w.String(Guid.NewGuid().ToString()); // multiplayer correlation id
        w.Bool(true);                 // server authoritative inventory
        w.String("*");                // game version
        w.Byte(0x0A); w.VarUInt32(0); w.Byte(0x00); // property data: empty NBT compound
        w.UInt64LE(0);                // server block state checksum
        w.Bytes(new byte[16]);        // world template id
        w.Bool(false);                // client side generation
        w.Bool(false);                // use block network id hashes
        w.Bool(false);                // server authoritative sound
        w.Bool(false);                // server join information: absent
        w.String("");                 // server id
        w.String("");                 // scenario id
        w.String("");                 // world id
        w.String("");                 // owner id
    });

    public static byte[] ItemRegistry() => Build(IdItemRegistry, w => w.VarUInt32(0));

    public static byte[] ChunkRadiusUpdated(int radius) => Build(IdChunkRadiusUpdated, w => w.VarInt32(radius));

    public static byte[] CreativeContent() => Build(IdCreativeContent, w =>
    {
        w.VarUInt32(0); // groups
        w.VarUInt32(0); // items
    });

    private const int CompressionAlgorithmFlate = 0;

    public static byte[] NetworkSettings(ushort compressionThreshold) => Build(IdNetworkSettings, w =>
    {
        w.UInt16LE(compressionThreshold);
        w.UInt16LE(CompressionAlgorithmFlate);
        w.Bool(false); // client throttle enabled
        w.Byte(0);     // client throttle threshold
        w.FloatLE(0);  // client throttle scalar
    });

    public static byte[] PlayStatus(int status) => Build(IdPlayStatus, w => w.Int32BE(status));

    /// <summary>Whether this library knows the resource pack packet layouts for the given protocol.</summary>
    public static bool SupportsResourcePackStage(int protocol) => protocol >= 729;

    /// <summary>An empty ResourcePacksInfo (no behaviour or resource packs).</summary>
    public static byte[] ResourcePacksInfo(int protocol) => Build(IdResourcePacksInfo, w =>
    {
        w.Bool(false); // forced to accept
        w.Bool(false); // has addon packs
        w.Bool(false); // scripting enabled
        if (protocol >= 818)
        {
            w.Bool(false); // vibrant visuals force disabled
        }

        if (protocol >= 766)
        {
            w.Bytes(new byte[16]); // world template UUID
            w.String("");          // world template version
        }

        if (protocol >= 2168)
        {
            w.VarUInt32(0); // resource pack entries
        }
        else
        {
            w.UInt16LE(0); // resource pack entries
            if (protocol < 748)
            {
                w.VarUInt32(0); // CDN entries (729-747 only)
            }
        }
    });

    /// <summary>An empty ResourcePackStack.</summary>
    public static byte[] ResourcePackStack(int protocol) => Build(IdResourcePackStack, w =>
    {
        w.Bool(false); // forced to accept
        if (protocol < 898)
        {
            w.VarUInt32(0); // behaviour packs
        }

        w.VarUInt32(0); // resource packs
        w.String("*");  // base game version
        w.Int32LE(0);   // experiments
        w.Bool(false);  // experiments previously toggled
        w.Bool(false);  // has editor packs (671+)
    });

    public static byte[] Transfer(int protocol, string address, ushort port) => Build(IdTransfer, w =>
    {
        w.String(address);
        w.UInt16LE(port);
        if (protocol >= 729)
        {
            w.Bool(false); // reload world
        }

        if (protocol >= 2168)
        {
            w.Bool(false); // optional gathering join info: absent
        }
    });

    public static byte[] Disconnect(int protocol, string message) => Build(IdDisconnect, w =>
    {
        if (protocol >= 622)
        {
            w.VarInt32(0); // reason: unknown
        }

        w.Bool(false); // skip message (712+ / 975+ encode "present" the same way)
        w.String(message);
        if (protocol >= 712)
        {
            w.String(message); // filtered message
        }
    });

    private static byte[] Build(int id, Action<ByteWriter> body)
    {
        var writer = new ByteWriter(64);
        writer.VarUInt32((uint)id);
        body(writer);
        return writer.ToArray();
    }
}
