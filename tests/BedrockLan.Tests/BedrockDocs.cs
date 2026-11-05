using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using Xunit;

namespace BedrockLan.Tests;

/// <summary>
/// Readers written from Mojang's bedrock-protocol-docs (https://mojang.github.io/bedrock-protocol-docs/) field
/// lists, independently of the library's serializers. Each one reads every documented field for the given
/// protocol and fails if anything is missing, malformed or left over.
/// </summary>
/// <remarks>
/// Wire notes from the docs: [OPT] fields are a presence bool followed by the value; arrays carry a VarUInt32
/// count unless marked "No size compression" (uint32 LE); "Compression" on int32/int64 is a zig-zag varint and
/// on uint32/uint64 an unsigned varint (on 8-bit fields it changes nothing: one byte, as gophertunnel writes them).
/// </remarks>
internal static class BedrockDocs
{
    /// <summary>Every protocol published in the docs from 1.26.40 (2168) to 1.26.70-preview.23 (2250).</summary>
    public static readonly int[] DocumentedProtocols =
    {
        2168, // 1.26.40 - 1.26.44
        2169, // 1.26.45
        2177, // 1.26.50-preview.22
        2181, // 1.26.50-preview.24
        2187, // 1.26.50-preview.25
        2192, // 1.26.50-preview.26
        2193, // 1.26.50, 1.26.51
        2207, // 1.26.60-preview.21
        2208, // 1.26.60-preview.23
        2211, // 1.26.60-preview.25
        2216, // 1.26.60-preview.28
        2223, // 1.26.60-preview.29
        2225, // 1.26.60 - 1.26.62
        2235, // 1.26.70-preview.20
        2238, // 1.26.70-preview.21
        2239, // 1.26.70-preview.22
        2250, // 1.26.70-preview.23
    };

    /// <summary>Newest documented protocol; anything newer is expected to be encoded exactly like it.</summary>
    public const int NewestKnown = 2250;

    /// <summary>Undocumented protocols newer than <see cref="NewestKnown"/>, standing in for future versions.</summary>
    public static readonly int[] NewerProtocols = { 2251, 2300, 9999 };

    public static TheoryData<int> Protocols()
    {
        var data = new TheoryData<int>();
        foreach (var p in DocumentedProtocols)
        {
            data.Add(p);
        }

        return data;
    }

    public static TheoryData<int> NewerThanKnown()
    {
        var data = new TheoryData<int>();
        foreach (var p in NewerProtocols)
        {
            data.Add(p);
        }

        return data;
    }

    /// <summary>Documented protocols plus the newer stand-ins, each with every transfer timing.</summary>
    public static TheoryData<int, TransferTiming> ProtocolsAndTimings()
    {
        var data = new TheoryData<int, TransferTiming>();
        foreach (var p in DocumentedProtocols.Concat(NewerProtocols))
        {
            foreach (var timing in new[] { TransferTiming.Immediate, TransferTiming.AfterResourcePacks, TransferTiming.AfterSpawn })
            {
                data.Add(p, timing);
            }
        }

        return data;
    }

    /// <summary>Reads any packet this library sends, by id, per the docs for <paramref name="protocol"/>.</summary>
    public static void ReadAny(byte[] packet, int protocol)
    {
        var id = (PacketId)(new ByteReader(packet).VarUInt32() & 0x3FF);
        switch (id)
        {
            case PacketId.NetworkSettingsPacket: ReadNetworkSettings(packet); break;
            case PacketId.PlayStatusPacket: ReadPlayStatus(packet); break;
            case PacketId.ResourcePacksInfoPacket: ReadResourcePacksInfo(packet, protocol); break;
            case PacketId.ResourcePackStackPacket: ReadResourcePackStack(packet, protocol); break;
            case PacketId.JigsawStructureDataPacket: ReadJigsawStructureData(packet); break;
            case PacketId.VoxelShapesPacket: ReadVoxelShapes(packet); break;
            case PacketId.StartGamePacket: ReadStartGame(packet, protocol); break;
            case PacketId.ItemRegistryPacket: ReadItemRegistry(packet); break;
            case PacketId.ChunkRadiusUpdatedPacket: ReadChunkRadiusUpdated(packet); break;
            case PacketId.CreativeContentPacket: ReadCreativeContent(packet); break;
            case PacketId.TransferPacket: ReadTransfer(packet, protocol); break;
            case PacketId.DisconnectPacket: ReadDisconnect(packet); break;
            default: Assert.Fail($"No documented reader for {id}."); break;
        }
    }

    // NetworkSettings: uint16 threshold, uint16 algorithm (ZLib = 0), bool throttle, uint8 threshold, float scalar.
    public static ushort ReadNetworkSettings(byte[] packet)
    {
        var r = Open(packet, PacketId.NetworkSettingsPacket);
        var threshold = r.UInt16LE();
        Assert.Equal(0, r.UInt16LE()); // ZLib
        r.Bool();
        r.Byte();
        r.UInt32LE();
        Done(r);
        return threshold;
    }

    // PlayStatus: int32 status (big-endian on the wire).
    public static int ReadPlayStatus(byte[] packet)
    {
        var r = Open(packet, PacketId.PlayStatusPacket);
        var status = r.Int32BE();
        Assert.InRange(status, 0, 9); // LoginSuccess .. LoginFailed_EditorMismatchVanillaToEditor
        Done(r);
        return status;
    }

    // ResourcePacksInfo (2168-2250): 4 bools, world template { UUID, version string }, packs array.
    public static void ReadResourcePacksInfo(byte[] packet, int protocol)
    {
        RequireDocumented(protocol);
        var r = Open(packet, PacketId.ResourcePacksInfoPacket);
        r.Bool(); // resource pack required
        r.Bool(); // has addon packs
        r.Bool(); // has scripts
        r.Bool(); // force disable vibrant visuals
        r.Skip(16); // world template UUID
        r.String(); // world template version
        Assert.Equal(0u, r.VarUInt32()); // resource packs: none
        Done(r);
    }

    // ResourcePackStack (2168-2250): bool, texture pack list, base game version, experiments { uint32 count,
    // bool }, then "Include Editor Packs" (bool, < 2239) or "Editor Pack Types" (uint8 enum, 2239+).
    public static void ReadResourcePackStack(byte[] packet, int protocol)
    {
        RequireDocumented(protocol);
        var r = Open(packet, PacketId.ResourcePackStackPacket);
        r.Bool(); // texture pack required
        Assert.Equal(0u, r.VarUInt32()); // texture packs: none
        Assert.False(string.IsNullOrEmpty(r.String())); // base game version
        Assert.Equal(0u, r.UInt32LE()); // experiments (no size compression)
        r.Bool(); // experiments ever toggled
        var editor = r.Byte();
        Assert.InRange(editor, 0, protocol >= 2239 ? 2 : 1); // bool, or None/Core/Annotation
        Done(r);
    }

    // Transfer: string, uint16, bool reload world, [OPT] gatherings configuration, [OPT] (2250+) server
    // transfer settings.
    public static (string Address, ushort Port) ReadTransfer(byte[] packet, int protocol)
    {
        RequireDocumented(protocol);
        var r = Open(packet, PacketId.TransferPacket);
        var address = r.String();
        var port = r.UInt16LE();
        r.Bool(); // reload world
        Assert.False(r.Bool()); // gatherings configuration: absent
        if (protocol >= 2250)
        {
            Assert.False(r.Bool()); // server transfer settings: absent
        }

        Done(r);
        return (address, port);
    }

    // Disconnect: varint reason, then Messages (oneOf { message, filtered message } | null).
    public static string ReadDisconnect(byte[] packet)
    {
        var r = Open(packet, PacketId.DisconnectPacket);
        r.VarInt32(); // reason
        Assert.Equal(0, r.Byte()); // first alternative: messages present
        var message = r.String();
        Assert.Equal(message, r.String()); // filtered message
        Done(r);
        return message;
    }

    // JigsawStructureData: one network NBT tag.
    public static void ReadJigsawStructureData(byte[] packet)
    {
        var r = Open(packet, PacketId.JigsawStructureDataPacket);
        SkipNbt(r);
        Done(r);
    }

    // VoxelShapes: shapes array, name map (count), uint16 custom shape count.
    public static void ReadVoxelShapes(byte[] packet)
    {
        var r = Open(packet, PacketId.VoxelShapesPacket);
        Assert.Equal(0u, r.VarUInt32());
        Assert.Equal(0u, r.VarUInt32());
        r.UInt16LE();
        Done(r);
    }

    // ItemRegistry: item data array.
    public static void ReadItemRegistry(byte[] packet)
    {
        var r = Open(packet, PacketId.ItemRegistryPacket);
        Assert.Equal(0u, r.VarUInt32());
        Done(r);
    }

    // ChunkRadiusUpdated: int32 varint.
    public static int ReadChunkRadiusUpdated(byte[] packet)
    {
        var r = Open(packet, PacketId.ChunkRadiusUpdatedPacket);
        var radius = r.VarInt32();
        Done(r);
        return radius;
    }

    // CreativeContent: groups array, entries array.
    public static void ReadCreativeContent(byte[] packet)
    {
        var r = Open(packet, PacketId.CreativeContentPacket);
        Assert.Equal(0u, r.VarUInt32());
        Assert.Equal(0u, r.VarUInt32());
        Done(r);
    }

    /// <summary>StartGame, field by field per the docs (2168-2250; level settings gained a field in 2216).</summary>
    public static string ReadStartGame(byte[] packet, int protocol)
    {
        RequireDocumented(protocol);
        var r = Open(packet, PacketId.StartGamePacket);
        Assert.Equal(1L, VarInt64(r));   // 0 entity id
        Assert.Equal(1UL, VarUInt64(r)); // 1 runtime id
        Assert.Equal(2, r.VarInt32());   // 2 game type: adventure
        r.Skip(3 * 4);                   // 3 position
        r.Skip(2 * 4);                   // 4 rotation

        // 5 level settings
        r.UInt64LE();      // seed
        r.UInt16LE();      // spawn biome type (int16)
        r.String();        // user defined biome name
        r.VarInt32();      // dimension
        r.VarInt32();      // generator type
        r.VarInt32();      // game type
        r.Bool();          // hardcore
        r.VarInt32();      // difficulty
        r.VarInt32();      // default spawn block position x
        r.VarInt32();      // y
        r.VarInt32();      // z
        r.Bool();          // achievements disabled
        r.VarInt32();      // editor world type
        r.Bool();          // created in editor
        r.Bool();          // exported from editor
        if (protocol >= 2216)
        {
            r.Byte();      // editor level migration version (int8)
        }

        r.VarInt32();      // day cycle stop time
        r.VarUInt32();     // education edition offer
        r.Bool();          // education features enabled
        r.String();        // education product id
        r.UInt32LE();      // rain level
        r.UInt32LE();      // lightning level
        r.Bool();          // confirmed platform locked content
        r.Bool();          // multiplayer game intent
        r.Bool();          // LAN broadcast intent
        r.VarInt32();      // Xbox Live broadcast setting
        r.VarInt32();      // platform broadcast setting
        r.Bool();          // commands enabled
        r.Bool();          // texture packs required
        Assert.Equal(0u, r.VarUInt32()); // rule data: no rules
        Assert.Equal(0u, r.UInt32LE());  // experiments: no toggles (no size compression)
        r.Bool();          // experiments ever toggled
        r.Bool();          // bonus chest
        r.Bool();          // start with map
        Assert.InRange(r.Byte(), 0, 3); // player permissions: visitor/member/operator/custom
        r.Int32LE();       // server chunk tick range
        for (var i = 0; i < 10; i++)
        {
            r.Bool();      // locked behaviour pack .. emote chat muted
        }

        r.String();        // base game version
        r.Int32LE();       // limited world width
        r.Int32LE();       // limited world depth
        r.Bool();          // nether type
        r.String();        // edu shared uri resource: button name
        r.String();        // edu shared uri resource: link
        Assert.False(r.Bool()); // [OPT] override force experimental gameplay: absent
        r.Byte();          // chat restriction level
        r.Bool();          // disable player interactions
        r.VarInt32();      // server editor connection policy
        r.Bool();          // allow anonymous block drops in editor worlds

        r.String();                   // 6 level id
        var levelName = r.String();   // 7 level name
        r.String();                   // 8 template content identity
        r.Bool();                     // 9 trial
        r.VarInt32();                 // 10 movement: rewind history size
        r.Bool();                     //    server authoritative block breaking
        r.UInt64LE();                 // 11 level current time
        r.VarInt32();                 // 12 enchantment seed
        Assert.Equal(0u, r.VarUInt32()); // 13 block properties: none
        Assert.True(Guid.TryParse(r.String(), out _)); // 14 multiplayer correlation id
        r.Bool();                     // 15 item stack net manager
        r.String();                   // 16 server version
        SkipNbt(r);                   // 17 player property data
        r.UInt64LE();                 // 18 block type registry checksum
        r.Skip(16);                   // 19 world template id
        r.Bool();                     // 20 client side generation
        r.Bool();                     // 21 block network ids are hashes
        r.Bool();                     // 22 network permissions: server auth sound
        Assert.False(r.Bool());       // 23 [OPT] server configuration join info: absent
        r.String();                   // 24 telemetry: server id
        r.String();                   //    scenario id
        r.String();                   //    world id
        r.String();                   //    owner id
        Done(r);
        return levelName;
    }

    public static ulong VarUInt64(ByteReader r)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var b = r.Byte();
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new PacketFormatException("VarUInt64 is longer than 10 bytes.");
    }

    public static long VarInt64(ByteReader r)
    {
        var raw = VarUInt64(r);
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    /// <summary>Skips one network little-endian NBT tag (only the tag types this library writes).</summary>
    private static void SkipNbt(ByteReader r)
    {
        var type = r.Byte();
        Assert.Equal(0x0A, type); // root compound
        r.String();               // root name
        SkipCompoundBody(r);
    }

    private static void SkipCompoundBody(ByteReader r)
    {
        while (true)
        {
            var type = r.Byte();
            if (type == 0x00)
            {
                return;
            }

            r.String(); // name
            SkipPayload(r, type);
        }
    }

    private static void SkipPayload(ByteReader r, byte type)
    {
        switch (type)
        {
            case 0x09: // list: element type, varint length, elements
                var elementType = r.Byte();
                var length = r.VarInt32();
                Assert.InRange(length, 0, 1 << 20);
                for (var i = 0; i < length; i++)
                {
                    SkipPayload(r, elementType);
                }

                break;
            case 0x0A:
                SkipCompoundBody(r);
                break;
            case 0x08:
                r.String();
                break;
            case 0x01:
                r.Byte();
                break;
            case 0x03:
                r.VarInt32();
                break;
            default:
                Assert.Fail($"Unexpected NBT tag type 0x{type:X2}.");
                break;
        }
    }

    private static ByteReader Open(byte[] packet, PacketId expected)
    {
        var r = new ByteReader(packet);
        Assert.Equal((uint)expected, r.VarUInt32());
        return r;
    }

    private static void Done(ByteReader r) => Assert.True(r.Remaining == 0, $"{r.Remaining} byte(s) left over.");

    /// <summary>Protocols newer than the docs are read with the newest known layout.</summary>
    private static void RequireDocumented(int protocol) =>
        Assert.True(protocol >= 2168, $"Protocol {protocol} is older than the documented range.");
}
