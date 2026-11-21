// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using System.Text;
using Xunit;

namespace BedrockLan.Tests;

/// <summary>
/// One test (or theory) per packet in the built-in protocol config: the id prefix and the exact body layout for each
/// protocol cut-off. Expected bytes are written out by hand, independently of the config and the definitions it is
/// generated from.
/// </summary>
public class PacketTests
{
    private const string Host = "play.example.com";
    private const ushort Port = 19132;
    private const string World = "Joining server";
    private const string Message = "Server offline";

    private static readonly string Uuid0 = new('0', 32); // 16 zero bytes as hex

    [Theory]
    [InlineData(2258, false)]
    [InlineData(2259, true)] // + SCTP recovery profile
    public void NetworkSettings_EncodesThresholdAndFlate(int protocol, bool hasRecoveryProfile)
    {
        var r = new ByteReader(Build("NetworkSettings", protocol));
        Assert.Equal((uint)PacketId.NetworkSettingsPacket, r.VarUInt32());
        Assert.Equal(256, r.UInt16LE());   // compression threshold
        Assert.Equal(0, r.UInt16LE());     // algorithm: flate
        Assert.False(r.Bool());            // client throttle enabled
        Assert.Equal(0, r.Byte());         // client throttle threshold
        Assert.Equal(0u, r.UInt32LE());    // client throttle scalar (0.0f)
        if (hasRecoveryProfile)
        {
            Assert.Equal(0, r.Byte());     // SCTP recovery profile: Default
        }

        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData("PlayStatusLoginSuccess", "0200000000")]
    [InlineData("PlayStatusPlayerSpawn", "0200000003")]
    public void PlayStatus_EncodesStatusBigEndian(string name, string expected)
    {
        AssertBytes(expected, Build(name));
    }

    [Theory]
    [InlineData(729, "06" + "000000" + "0000" + "00")]                      // + CDN entries
    [InlineData(748, "06" + "000000" + "0000")]                             // CDN entries dropped
    [InlineData(766, "06" + "000000" + "{uuid}" + "00" + "0000")]           // + world template id/version
    [InlineData(818, "06" + "00000000" + "{uuid}" + "00" + "0000")]         // + vibrant visuals flag
    [InlineData(2168, "06" + "00000000" + "{uuid}" + "00" + "00")]          // pack count becomes a VarUInt32
    public void ResourcePacksInfo_MatchesLayoutPerProtocol(int protocol, string expected)
    {
        AssertBytes(expected.Replace("{uuid}", Uuid0), Build("ResourcePacksInfo", protocol));
    }

    [Theory]
    [InlineData(897, "07" + "00" + "00" + "00" + "012A" + "00000000" + "00" + "00")] // with behaviour packs
    [InlineData(898, "07" + "00" + "00" + "012A" + "00000000" + "00" + "00")]        // behaviour packs dropped
    [InlineData(2239, "07" + "00" + "00" + "012A" + "00000000" + "00" + "00")]       // editor pack type (uint8) = None
    public void ResourcePackStack_MatchesLayoutPerProtocol(int protocol, string expected)
    {
        AssertBytes(expected, Build("ResourcePackStack", protocol));
    }

    [Theory]
    [InlineData(700, 0)]
    [InlineData(729, 1)]  // + reload world
    [InlineData(2168, 2)] // + gatherings configuration
    [InlineData(2250, 3)] // + server transfer settings
    public void Transfer_EncodesAddressPortAndVersionFlags(int protocol, int trailingFlags)
    {
        var r = new ByteReader(Build("Transfer", protocol));
        Assert.Equal((uint)PacketId.TransferPacket, r.VarUInt32());
        Assert.Equal(Host, r.String());
        Assert.Equal(Port, r.UInt16LE());
        for (var i = 0; i < trailingFlags; i++)
        {
            Assert.False(r.Bool());
        }

        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(600, false, false)]
    [InlineData(622, true, false)] // + reason
    [InlineData(712, true, true)]  // + filtered message
    public void Disconnect_MatchesLayoutPerProtocol(int protocol, bool hasReason, bool hasFiltered)
    {
        var r = new ByteReader(Build("Disconnect", protocol));
        Assert.Equal((uint)PacketId.DisconnectPacket, r.VarUInt32());
        if (hasReason)
        {
            Assert.Equal(0, r.VarInt32());
        }

        Assert.False(r.Bool()); // skip message
        Assert.Equal(Message, r.String());
        if (hasFiltered)
        {
            Assert.Equal(Message, r.String());
        }

        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void JigsawStructureData_IsCompoundOfFourEmptyLists()
    {
        AssertBytes(
            "B902" + "0A00"
            + "09" + "0A" + Ascii("processors") + "0A00"
            + "09" + "0E" + Ascii("template_pools") + "0A00"
            + "09" + "07" + Ascii("jigsaws") + "0A00"
            + "09" + "0E" + Ascii("structure_sets") + "0A00"
            + "00",
            Build("JigsawStructureData"));
    }

    [Fact]
    public void VoxelShapes_IsEmpty()
    {
        AssertBytes("D102" + "00" + "00" + "0000", Build("VoxelShapes"));
    }

    [Fact]
    public void ItemRegistry_IsEmpty()
    {
        AssertBytes("A201" + "00", Build("ItemRegistry"));
    }

    [Fact]
    public void CreativeContent_IsEmpty()
    {
        AssertBytes("9101" + "00" + "00", Build("CreativeContent"));
    }

    [Theory]
    [InlineData(1, "4602")]
    [InlineData(8, "4610")]
    [InlineData(-1, "4601")] // zig-zag
    public void ChunkRadiusUpdated_EncodesRadiusAsVarInt(int radius, string expected)
    {
        AssertBytes(expected, Build("ChunkRadiusUpdated", BedrockDocs.NewestKnown, radius: radius));
    }

    [Fact]
    public void StartGame_StartsWithPlayerAndEndsWithEmptyIds()
    {
        var bytes = Build("StartGame", 2225);

        var r = new ByteReader(bytes);
        Assert.Equal((uint)PacketId.StartGamePacket, r.VarUInt32());
        Assert.Equal(0x02, r.Byte()); // unique id 1 (zig-zag VarInt64)
        Assert.Equal(0x01, r.Byte()); // runtime id 1 (VarUInt64)
        Assert.Equal(2, r.VarInt32()); // adventure

        // Ends with: server join info absent, then server/scenario/world/owner ids all empty.
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, bytes[^5..]);
    }

    [Fact]
    public void StartGame_CarriesWorldName()
    {
        var withName = Build("StartGame", 2225, world: World);
        var empty = Build("StartGame", 2225, world: "");

        var encoded = new byte[] { (byte)World.Length }.Concat(Encoding.UTF8.GetBytes(World)).ToArray();
        Assert.True(withName.AsSpan().IndexOf(encoded.AsSpan()) > 0, "world name not found");
        Assert.Equal(empty.Length + World.Length, withName.Length);
    }

    [Fact]
    public void StartGame_AddsEditorLevelMigrationVersionFrom2216()
    {
        var before = Build("StartGame", 2211);
        var after = Build("StartGame", 2216);
        Assert.Equal(before.Length + 1, after.Length);
    }

    // ---- Every documented protocol (2168-2259), read back with readers written from Mojang's docs ----

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void NetworkSettings_MatchesDocs(int protocol)
    {
        Assert.Equal(256, BedrockDocs.ReadNetworkSettings(Build("NetworkSettings", protocol), protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void PlayStatus_MatchesDocs(int protocol)
    {
        Assert.Equal(0, BedrockDocs.ReadPlayStatus(Build("PlayStatusLoginSuccess", protocol)));
        Assert.Equal(3, BedrockDocs.ReadPlayStatus(Build("PlayStatusPlayerSpawn", protocol)));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ResourcePacksInfo_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadResourcePacksInfo(Build("ResourcePacksInfo", protocol), protocol);
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ResourcePackStack_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadResourcePackStack(Build("ResourcePackStack", protocol), protocol);
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void Transfer_MatchesDocs(int protocol)
    {
        Assert.Equal((Host, Port), BedrockDocs.ReadTransfer(Build("Transfer", protocol), protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void Disconnect_MatchesDocs(int protocol)
    {
        Assert.Equal(Message, BedrockDocs.ReadDisconnect(Build("Disconnect", protocol)));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void StartGame_MatchesDocs(int protocol)
    {
        Assert.Equal(World, BedrockDocs.ReadStartGame(Build("StartGame", protocol), protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void JigsawStructureData_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadJigsawStructureData(Build("JigsawStructureData", protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void VoxelShapes_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadVoxelShapes(Build("VoxelShapes", protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ItemRegistry_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadItemRegistry(Build("ItemRegistry", protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ChunkRadiusUpdated_MatchesDocs(int protocol)
    {
        Assert.Equal(6, BedrockDocs.ReadChunkRadiusUpdated(Build("ChunkRadiusUpdated", protocol, radius: 6)));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void CreativeContent_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadCreativeContent(Build("CreativeContent", protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.NewerThanKnown), MemberType = typeof(BedrockDocs))]
    public void NewerProtocol_EncodesLikeTheNewestKnown(int protocol)
    {
        var known = BedrockDocs.NewestKnown;
        foreach (var name in ProtocolConfig.Default.PacketNames)
        {
            Assert.Equal(Build(name, known), Build(name, protocol));
        }

        Assert.Equal(World, BedrockDocs.ReadStartGame(Build("StartGame", protocol), protocol));
    }

    [Fact]
    public void Build_PrefixesMultiByteIdsAsVarUInt32()
    {
        // 313 needs two VarUInt32 bytes; the body starts right after them.
        var r = new ByteReader(Build("JigsawStructureData"));
        Assert.Equal((uint)PacketId.JigsawStructureDataPacket, r.VarUInt32());
        Assert.Equal(0x0A, r.Byte());
    }

    /// <summary>A packet from the built-in config, with fixed session variables (and a fixed correlation id).</summary>
    private static byte[] Build(string name, int protocol = BedrockDocs.NewestKnown, string world = World, int radius = 6) =>
        ProtocolConfig.Default.BuildPacket(name, protocol, new Dictionary<string, object?>
        {
            ["target.host"] = Host,
            ["target.port"] = Port,
            ["worldName"] = world,
            ["offlineMessage"] = Message,
            ["uuid.random"] = "0b1d4c2e-0000-4000-8000-0123456789ab",
            ["radius"] = (long)radius,
        });

    private static string Ascii(string s) => Convert.ToHexString(Encoding.ASCII.GetBytes(s));

    private static void AssertBytes(string expectedHex, byte[] actual)
    {
        Assert.Equal(expectedHex.ToUpperInvariant(), Convert.ToHexString(actual));
    }
}
