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
    [InlineData(2168, 2)] // + gathering join info
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
        var bytes = new StartGame { WorldName = "Joining server" }.Build();

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
        var withName = new StartGame { WorldName = name }.Build();
        var empty = new StartGame { WorldName = "" }.Build();

        var encoded = new byte[] { (byte)name.Length }.Concat(Encoding.UTF8.GetBytes(name)).ToArray();
        Assert.True(withName.AsSpan().IndexOf(encoded.AsSpan()) > 0, "world name not found");
        Assert.Equal(empty.Length + name.Length, withName.Length);
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
