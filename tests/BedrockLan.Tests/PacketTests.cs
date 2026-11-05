using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using System.Text;
using Xunit;

namespace BedrockLan.Tests;

/// <summary>
/// One test (or theory) per packet class: the id prefix and the exact body layout for each protocol cut-off.
/// Expected bytes are written out by hand, independently of the serializers.
/// </summary>
public class PacketTests
{
    private static readonly string Uuid0 = new('0', 32); // 16 zero bytes as hex

    [Fact]
    public void NetworkSettings_EncodesThresholdAndFlate()
    {
        var bytes = new NetworkSettings { CompressionThreshold = 256 }.Build();

        var r = new ByteReader(bytes);
        Assert.Equal((uint)PacketId.NetworkSettingsPacket, r.VarUInt32());
        Assert.Equal(256, r.UInt16LE());   // compression threshold
        Assert.Equal(0, r.UInt16LE());     // algorithm: flate
        Assert.False(r.Bool());            // client throttle enabled
        Assert.Equal(0, r.Byte());         // client throttle threshold
        Assert.Equal(0u, r.UInt32LE());    // client throttle scalar (0.0f)
        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(PlayStatus.LoginSuccess, "0200000000")]
    [InlineData(PlayStatus.PlayerSpawn, "0200000003")]
    public void PlayStatus_EncodesStatusBigEndian(int status, string expected)
    {
        AssertBytes(expected, new PlayStatus { Status = status }.Build());
    }

    [Theory]
    [InlineData(729, "06" + "000000" + "0000" + "00")]                      // + CDN entries
    [InlineData(748, "06" + "000000" + "0000")]                             // CDN entries dropped
    [InlineData(766, "06" + "000000" + "{uuid}" + "00" + "0000")]           // + world template id/version
    [InlineData(818, "06" + "00000000" + "{uuid}" + "00" + "0000")]         // + vibrant visuals flag
    [InlineData(2168, "06" + "00000000" + "{uuid}" + "00" + "00")]          // pack count becomes a VarUInt32
    public void ResourcePacksInfo_MatchesLayoutPerProtocol(int protocol, string expected)
    {
        AssertBytes(expected.Replace("{uuid}", Uuid0), new ResourcePacksInfo { Protocol = protocol }.Build());
    }

    [Theory]
    [InlineData(897, "07" + "00" + "00" + "00" + "012A" + "00000000" + "00" + "00")] // with behaviour packs
    [InlineData(898, "07" + "00" + "00" + "012A" + "00000000" + "00" + "00")]        // behaviour packs dropped
    public void ResourcePackStack_MatchesLayoutPerProtocol(int protocol, string expected)
    {
        AssertBytes(expected, new ResourcePackStack { Protocol = protocol }.Build());
    }

    [Theory]
    [InlineData(700, 0)]
    [InlineData(729, 1)]  // + reload world
    [InlineData(2168, 2)] // + gatherings configuration
    [InlineData(2250, 3)] // + server transfer settings
    public void Transfer_EncodesAddressPortAndVersionFlags(int protocol, int trailingFlags)
    {
        var bytes = new Transfer { Protocol = protocol, Address = "play.example.com", Port = 19132 }.Build();

        var r = new ByteReader(bytes);
        Assert.Equal((uint)PacketId.TransferPacket, r.VarUInt32());
        Assert.Equal("play.example.com", r.String());
        Assert.Equal(19132, r.UInt16LE());
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
        var bytes = new Disconnect { Protocol = protocol, Message = "Server offline" }.Build();

        var r = new ByteReader(bytes);
        Assert.Equal((uint)PacketId.DisconnectPacket, r.VarUInt32());
        if (hasReason)
        {
            Assert.Equal(0, r.VarInt32());
        }

        Assert.False(r.Bool()); // skip message
        Assert.Equal("Server offline", r.String());
        if (hasFiltered)
        {
            Assert.Equal("Server offline", r.String());
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
            new JigsawStructureData().Build());
    }

    [Fact]
    public void VoxelShapes_IsEmpty()
    {
        AssertBytes("D102" + "00" + "00" + "0000", new VoxelShapes().Build());
    }

    [Fact]
    public void ItemRegistry_IsEmpty()
    {
        AssertBytes("A201" + "00", new ItemRegistry().Build());
    }

    [Fact]
    public void CreativeContent_IsEmpty()
    {
        AssertBytes("9101" + "00" + "00", new CreativeContent().Build());
    }

    [Theory]
    [InlineData(1, "4602")]
    [InlineData(8, "4610")]
    [InlineData(-1, "4601")] // zig-zag
    public void ChunkRadiusUpdated_EncodesRadiusAsVarInt(int radius, string expected)
    {
        AssertBytes(expected, new ChunkRadiusUpdated { Radius = radius }.Build());
    }

    [Fact]
    public void StartGame_StartsWithPlayerAndEndsWithEmptyIds()
    {
        var bytes = new StartGame { WorldName = "Joining server", Protocol = 2225 }.Build();

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
        var name = "Joining server";
        var withName = new StartGame { WorldName = name, Protocol = 2225 }.Build();
        var empty = new StartGame { WorldName = "", Protocol = 2225 }.Build();

        var encoded = new byte[] { (byte)name.Length }.Concat(Encoding.UTF8.GetBytes(name)).ToArray();
        Assert.True(withName.AsSpan().IndexOf(encoded.AsSpan()) > 0, "world name not found");
        Assert.Equal(empty.Length + name.Length, withName.Length);
    }

    [Fact]
    public void StartGame_AddsEditorLevelMigrationVersionFrom2216()
    {
        var before = new StartGame { WorldName = "w", Protocol = 2211 }.Build();
        var after = new StartGame { WorldName = "w", Protocol = 2216 }.Build();
        Assert.Equal(before.Length + 1, after.Length);
    }

    // ---- Every documented protocol (2168-2250), read back with readers written from Mojang's docs ----

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void NetworkSettings_MatchesDocs(int protocol)
    {
        _ = protocol; // same layout in every documented protocol
        Assert.Equal(256, BedrockDocs.ReadNetworkSettings(new NetworkSettings { CompressionThreshold = 256 }.Build()));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void PlayStatus_MatchesDocs(int protocol)
    {
        _ = protocol;
        Assert.Equal(PlayStatus.LoginSuccess, BedrockDocs.ReadPlayStatus(new PlayStatus { Status = PlayStatus.LoginSuccess }.Build()));
        Assert.Equal(PlayStatus.PlayerSpawn, BedrockDocs.ReadPlayStatus(new PlayStatus { Status = PlayStatus.PlayerSpawn }.Build()));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ResourcePacksInfo_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadResourcePacksInfo(new ResourcePacksInfo { Protocol = protocol }.Build(), protocol);
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ResourcePackStack_MatchesDocs(int protocol)
    {
        BedrockDocs.ReadResourcePackStack(new ResourcePackStack { Protocol = protocol }.Build(), protocol);
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void Transfer_MatchesDocs(int protocol)
    {
        var bytes = new Transfer { Protocol = protocol, Address = "play.example.com", Port = 19132 }.Build();
        Assert.Equal(("play.example.com", (ushort)19132), BedrockDocs.ReadTransfer(bytes, protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void Disconnect_MatchesDocs(int protocol)
    {
        Assert.Equal("Server offline", BedrockDocs.ReadDisconnect(new Disconnect { Protocol = protocol, Message = "Server offline" }.Build()));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void StartGame_MatchesDocs(int protocol)
    {
        var bytes = new StartGame { WorldName = "Joining server", Protocol = protocol }.Build();
        Assert.Equal("Joining server", BedrockDocs.ReadStartGame(bytes, protocol));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void JigsawStructureData_MatchesDocs(int protocol)
    {
        _ = protocol;
        BedrockDocs.ReadJigsawStructureData(new JigsawStructureData().Build());
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void VoxelShapes_MatchesDocs(int protocol)
    {
        _ = protocol;
        BedrockDocs.ReadVoxelShapes(new VoxelShapes().Build());
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ItemRegistry_MatchesDocs(int protocol)
    {
        _ = protocol;
        BedrockDocs.ReadItemRegistry(new ItemRegistry().Build());
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void ChunkRadiusUpdated_MatchesDocs(int protocol)
    {
        _ = protocol;
        Assert.Equal(6, BedrockDocs.ReadChunkRadiusUpdated(new ChunkRadiusUpdated { Radius = 6 }.Build()));
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.Protocols), MemberType = typeof(BedrockDocs))]
    public void CreativeContent_MatchesDocs(int protocol)
    {
        _ = protocol;
        BedrockDocs.ReadCreativeContent(new CreativeContent().Build());
    }

    [Theory]
    [MemberData(nameof(BedrockDocs.NewerThanKnown), MemberType = typeof(BedrockDocs))]
    public void NewerProtocol_EncodesLikeTheNewestKnown(int protocol)
    {
        var known = BedrockDocs.NewestKnown;
        Assert.Equal(new ResourcePacksInfo { Protocol = known }.Build(), new ResourcePacksInfo { Protocol = protocol }.Build());
        Assert.Equal(new ResourcePackStack { Protocol = known }.Build(), new ResourcePackStack { Protocol = protocol }.Build());
        Assert.Equal(new Transfer { Protocol = known, Address = "a", Port = 1 }.Build(), new Transfer { Protocol = protocol, Address = "a", Port = 1 }.Build());
        Assert.Equal(new Disconnect { Protocol = known, Message = "m" }.Build(), new Disconnect { Protocol = protocol, Message = "m" }.Build());
        Assert.Equal(new StartGame { Protocol = known, WorldName = "w" }.Build().Length, new StartGame { Protocol = protocol, WorldName = "w" }.Build().Length);
        Assert.Equal("w", BedrockDocs.ReadStartGame(new StartGame { Protocol = protocol, WorldName = "w" }.Build(), protocol));
    }

    [Fact]
    public void Build_PrefixesMultiByteIdsAsVarUInt32()
    {
        // 313 needs two VarUInt32 bytes; the body starts right after them.
        var bytes = new JigsawStructureData().Build();
        var r = new ByteReader(bytes);
        Assert.Equal((uint)PacketId.JigsawStructureDataPacket, r.VarUInt32());
        Assert.Equal(0x0A, r.Byte());
    }

    private static string Ascii(string s) => Convert.ToHexString(Encoding.ASCII.GetBytes(s));

    private static void AssertBytes(string expectedHex, byte[] actual)
    {
        Assert.Equal(expectedHex.ToUpperInvariant(), Convert.ToHexString(actual));
    }
}
