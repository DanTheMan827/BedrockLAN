using Xunit;
using Definitions = BedrockLan.Protocol.Definitions;

namespace BedrockLan.Tests;

/// <summary>
/// Known-good bytes for every packet the transfer server sends, checked against both serializers: the protocol
/// config (ProtocolConfig.Default, what the server actually sends) and the C# definitions it is generated from
/// (DefinitionSerializer). The expected bytes are fixed data: they're what the hand-written packet classes wrote
/// before packets became data, which were checked against Mojang's bedrock-protocol-docs for 2168-2250 and
/// against real clients. A change to the definitions that alters the wire format fails here.
/// </summary>
public class GoldenPacketTests
{
    /// <summary>Fixed session variables the expected bytes were written with.</summary>
    private static readonly Dictionary<string, object?> Variables = new()
    {
        ["target.host"] = "play.example.com",
        ["target.port"] = (ushort)19132,
        ["worldName"] = "Joining server...",
        ["offlineMessage"] = "Offline",
        ["uuid.random"] = "0b1d4c2e-0000-4000-8000-0123456789ab",
        ["radius"] = 6L,
    };

    /// <summary>Each protocol cut-off, the protocol just before it, and one far newer than any known.</summary>
    private static readonly int[] Protocols =
    {
        554, 621, 622, 711, 712, 728, 729, 747, 748, 765, 766, 817, 818, 897, 898, 2167, 2168, 2191, 2192, 2215, 2216,
        2234, 2235, 2238, 2239, 2249, 2250, 2251, 9999,
    };

    /// <summary>Packet name → (first protocol, bytes from then until the next entry), oldest first.</summary>
    private static readonly Dictionary<string, List<(int Since, string Hex)>> Golden = BuildGolden();

    public static TheoryData<string, int> Cases()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in Golden.Keys)
        {
            foreach (var protocol in Protocols)
            {
                data.Add(name, protocol);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Config_WritesTheKnownBytes(string name, int protocol)
    {
        var actual = Convert.ToHexString(ProtocolConfig.Default.BuildPacket(name, protocol, Variables));
        AssertHex(Expected(name, protocol), actual, $"config: {name} at protocol {protocol}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Definitions_WriteTheKnownBytes(string name, int protocol)
    {
        var packet = Definitions.DefaultProtocol.Definition.Packets.Single(p => p.Name == name).Packet;
        var actual = Convert.ToHexString(Definitions.DefinitionSerializer.Serialize(packet, protocol, Variables));
        AssertHex(Expected(name, protocol), actual, $"definitions: {name} at protocol {protocol}");
    }

    [Fact]
    public void EveryPacketHasKnownBytes()
    {
        var defined = Definitions.DefaultProtocol.Definition.Packets.Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(defined, Golden.Keys.OrderBy(n => n));
        Assert.Equal(ProtocolConfig.Default.PacketNames.OrderBy(n => n), Golden.Keys.OrderBy(n => n));
    }

    private static string Expected(string name, int protocol) =>
        Golden[name].Last(e => e.Since <= protocol).Hex;

    private static void AssertHex(string expected, string actual, string what)
    {
        if (expected == actual)
        {
            return;
        }

        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
        {
            at++;
        }

        Assert.Fail($"{what}: bytes differ from byte {at / 2}\n  expected {expected}\n  actual   {actual}");
    }

    private static Dictionary<string, List<(int Since, string Hex)>> BuildGolden()
    {
        var golden = new Dictionary<string, List<(int, string)>>();
        void Add(string name, int since, string hex)
        {
            if (!golden.TryGetValue(name, out var list))
            {
                golden[name] = list = new List<(int, string)>();
            }

            list.Add((since, hex));
        }

        Add("NetworkSettings", 554,
            "8F0100010000000000000000");
        Add("PlayStatusLoginSuccess", 554,
            "0200000000");
        Add("PlayStatusPlayerSpawn", 554,
            "0200000003");
        Add("ResourcePacksInfo", 554,
            "06000000000000");
        Add("ResourcePacksInfo", 748,
            "060000000000");
        Add("ResourcePacksInfo", 766,
            "0600000000000000000000000000000000000000000000");
        Add("ResourcePacksInfo", 818,
            "060000000000000000000000000000000000000000000000");
        Add("ResourcePacksInfo", 2168,
            "0600000000000000000000000000000000000000000000");
        Add("ResourcePackStack", 554,
            "07000000012A000000000000");
        Add("ResourcePackStack", 898,
            "070000012A000000000000");
        Add("JigsawStructureData", 554,
            "B9020A00090A70726F636573736F72730A00090E74656D706C6174655F706F6F6C730A0009076A6967736177730A0009"
            + "0E7374727563747572655F736574730A0000");
        Add("VoxelShapes", 554,
            "D10200000000");
        Add("StartGame", 554,
            "0B020104000000000000C8420000000000000000000000000000000000000000000000000404000200C8010001000000"
            + "000000000000000000000000000101000000000000000000000000010400000000000000000000000000012A00000000"
            + "00000000010000000000000000114A6F696E696E67207365727665722E2E2E0000000000000000000000000000243062"
            + "3164346332652D303030302D343030302D383030302D30313233343536373839616201012A0A00000000000000000000"
            + "000000000000000000000000000000000000000000000000");
        Add("StartGame", 2216,
            "0B020104000000000000C8420000000000000000000000000000000000000000000000000404000200C8010001000000"
            + "00000000000000000000000000000101000000000000000000000000010400000000000000000000000000012A000000"
            + "0000000000010000000000000000114A6F696E696E67207365727665722E2E2E00000000000000000000000000002430"
            + "623164346332652D303030302D343030302D383030302D30313233343536373839616201012A0A000000000000000000"
            + "00000000000000000000000000000000000000000000000000");
        Add("ItemRegistry", 554,
            "A20100");
        Add("ChunkRadiusUpdated", 554,
            "460C");
        Add("CreativeContent", 554,
            "91010000");
        Add("Transfer", 554,
            "5510706C61792E6578616D706C652E636F6DBC4A");
        Add("Transfer", 729,
            "5510706C61792E6578616D706C652E636F6DBC4A00");
        Add("Transfer", 2168,
            "5510706C61792E6578616D706C652E636F6DBC4A0000");
        Add("Transfer", 2250,
            "5510706C61792E6578616D706C652E636F6DBC4A000000");
        Add("Disconnect", 554,
            "0500074F66666C696E65");
        Add("Disconnect", 622,
            "050000074F66666C696E65");
        Add("Disconnect", 712,
            "050000074F66666C696E65074F66666C696E65");

        return golden;
    }
}
