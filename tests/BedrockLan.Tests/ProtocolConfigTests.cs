using BedrockLan.Bedrock;
using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Definitions = BedrockLan.Protocol.Definitions;

namespace BedrockLan.Tests;

/// <summary>
/// The data-driven protocol: the built-in config must be the one generated from the C# definitions
/// (BedrockLan.Protocol.Definitions) and write exactly what they describe, for every protocol; configs are
/// validated up front; and a replaced config really changes what a session does.
/// </summary>
public class ProtocolConfigTests
{
    private const string Host = "play.example.com";
    private const ushort Port = 19132;
    private const string World = "Joining server...";
    private const string Message = "That server is offline right now.";
    private const string Uuid = "0b1d4c2e-0000-4000-8000-0123456789ab";
    private const int Radius = 6;

    /// <summary>Older than the docs, the documented range, and newer than it.</summary>
    public static TheoryData<int> AllProtocols()
    {
        var data = new TheoryData<int>();
        foreach (var p in new[] { 554, 600, 622, 649, 700, 712, 729, 747, 748, 765, 766, 800, 817, 818, 897, 898, 2167 }
                     .Concat(BedrockDocs.DocumentedProtocols)
                     .Concat(BedrockDocs.NewerProtocols))
        {
            data.Add(p);
        }

        return data;
    }

    [Fact]
    public void Default_ParsesAndDefinesEveryPacket()
    {
        var config = ProtocolConfig.Default;
        Assert.Equal(1, config.FormatVersion);
        Assert.False(string.IsNullOrEmpty(config.ConfigVersion));
        Assert.Equal(DefinitionNames.OrderBy(n => n), config.PacketNames.OrderBy(n => n));
        Assert.Same(config, new TransferOptions().Protocol);
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void Default_MatchesTheDefinitionsByteForByte(int protocol)
    {
        var variables = new Dictionary<string, object?>
        {
            ["target.host"] = Host,
            ["target.port"] = Port,
            ["worldName"] = World,
            ["offlineMessage"] = Message,
            ["uuid.random"] = Uuid,
            ["radius"] = (long)Radius,
        };

        foreach (var (name, packet) in Definitions.DefaultProtocol.Definition.Packets)
        {
            var expected = Convert.ToHexString(Definitions.DefinitionSerializer.Serialize(packet, protocol, variables));
            var actual = Convert.ToHexString(ProtocolConfig.Default.BuildPacket(name, protocol, variables));
            Assert.True(expected == actual, $"{name} at protocol {protocol}:\n  definition {expected}\n  config     {actual}");
        }
    }

    /// <summary>
    /// The committed default-protocol.jsonc is what tools/ProtocolGen writes from the definitions. Comments and
    /// formatting aside. Building BedrockLan regenerates it, so this fails only if it was built with
    /// -p:GenerateProtocolConfig=false or without tools/ProtocolGen.
    /// </summary>
    [Fact]
    public void Default_IsGeneratedFromTheDefinitions()
    {
        var generated = Definitions.JsoncGenerator.Generate(Definitions.DefaultProtocol.Definition);
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var expected = JsonNode.Parse(generated, documentOptions: options);
        var actual = JsonNode.Parse(ProtocolConfig.DefaultText, documentOptions: options);
        Assert.True(JsonNode.DeepEquals(expected, actual),
            "src/BedrockLan/Protocol/default-protocol.jsonc is out of date; build BedrockLan (or run: dotnet run --project tools/ProtocolGen) to regenerate it.");

        // ...and it parses to the same thing as the committed copy.
        var reparsed = ProtocolConfig.Parse(generated);
        Assert.Equal(ProtocolConfig.Default.ConfigVersion, reparsed.ConfigVersion);
    }

    [Fact]
    public void Default_TextRoundTrips()
    {
        var reparsed = ProtocolConfig.Parse(ProtocolConfig.DefaultText);
        Assert.Equal(ProtocolConfig.Default.ConfigVersion, reparsed.ConfigVersion);
        Assert.Equal(ProtocolConfig.Default.PacketNames.OrderBy(n => n), reparsed.PacketNames.OrderBy(n => n));
    }

    [Fact]
    public void Parse_AcceptsCommentsAndTrailingCommas()
    {
        var config = ProtocolConfig.Parse(Minimal);
        Assert.Equal("test.1", config.ConfigVersion);
    }

    [Theory]
    [InlineData("\"formatVersion\": 1", "\"formatVersion\": 2", "newer than")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"VarInt33\": 0 }", "unknown type")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"String\": \"$nope\" }", "unknown variable $nope")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"Byte\": 300 }", "out of range")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"Bytes\": \"0g\" }", "hex")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"Byte\": 0, \"since\": 10, \"until\": 5 }", "must be below")]
    [InlineData("{ \"String\": \"$target.host\" }", "{ \"Byte\": 0, \"Bool\": true }", "one type key")]
    [InlineData("{ \"transfer\": true }", "{ \"send\": \"Missing\", \"transfer\": true }", "no packet named \"Missing\"")]
    [InlineData("{ \"transfer\": true }", "{ \"goto\": \"nowhere\" }", "no stage named \"nowhere\"")]
    [InlineData("{ \"transfer\": true }", "{ \"goto\": \"login\", \"transfer\": true }", "only one of")]
    [InlineData("{ \"transfer\": true }", "{ \"if\": { \"timing\": \"Later\" }, \"transfer\": true }", "unknown timing")]
    [InlineData("\"expect\": { \"packet\": 1 }", "\"expect\": { \"packet\": 1, \"read\": [ { \"Int64LE\": \"$x\" } ] }", "can't be read")]
    [InlineData("\"expect\": { \"packet\": 1 }", "\"expect\": { \"packet\": 1, \"read\": [ { \"String\": \"$worldName\" } ] }", "provided by the session")]
    [InlineData("\"offlinePacket\": \"Disconnect\"", "\"offlinePacket\": \"Gone\"", "no packet named \"Gone\"")]
    [InlineData("{ \"transfer\": true }", "{ \"if\": { \"variable\": { \"$nope\": 1 } }, \"transfer\": true }", "unknown variable $nope")]
    [InlineData("{ \"transfer\": true }", "{ \"if\": { \"variable\": { \"nope\": 1 } }, \"transfer\": true }", "starting with $")]
    [InlineData("{ \"transfer\": true }", "{ \"if\": { \"variable\": { \"$protocol\": [] } }, \"transfer\": true }", "at least one value")]
    [InlineData("{ \"transfer\": true }", "{ \"if\": { \"variable\": { \"$protocol\": 1.5 } }, \"transfer\": true }", "expected an integer")]
    [InlineData("\"start\": \"login\"", "\"start\": \"later\"", "no stage named \"later\"")]
    [InlineData("\"id\": 85", "\"id\": 85, \"ids\": 2", "unknown key")]
    public void Parse_RejectsInvalidConfigs(string find, string replace, string expectedMessage)
    {
        Assert.Contains(find, Minimal);
        var text = Minimal.Replace(find, replace);
        var ex = Assert.Throws<ProtocolConfigException>(() => ProtocolConfig.Parse(text));
        Assert.Contains(expectedMessage, ex.Message);
        Assert.False(ProtocolConfig.TryParse(text, out var config, out var error));
        Assert.Null(config);
        Assert.Equal(ex.Message, error);
    }

    [Fact]
    public void Parse_RejectsNonJson()
    {
        var ex = Assert.Throws<ProtocolConfigException>(() => ProtocolConfig.Parse("{ not json"));
        Assert.Contains("Not valid JSON", ex.Message);
    }

    /// <summary>A replaced config changes what a session sends, with no code change: here Login transfers at once.</summary>
    [Fact]
    public void Session_FollowsAReplacedConfig()
    {
        var custom = ProtocolConfig.Parse(Minimal);
        var options = new TransferOptions { Protocol = custom };
        var sent = new List<byte[]>();
        var session = new TransferSession("test", () => new TransferTarget(Host, Port), options, sent.Add, _ => { }, null);

        // The minimal config starts at "login" (packet 1) with no compression.
        session.HandleBatch(BatchCodec.Join(Packet(1, w => w.Int32BE(2250))));

        Assert.True(session.IsFinished);
        var packet = BatchCodec.Split(sent.Single())[0];
        var r = new ByteReader(packet);
        Assert.Equal(85u, r.VarUInt32());
        Assert.Equal(Host, r.String());
        Assert.Equal(0x2A, r.Byte()); // the custom config's extra marker byte
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Session_KeepsTheConfigItStartedWith()
    {
        var options = new TransferOptions();
        var sent = new List<byte[]>();
        var session = new TransferSession("test", () => new TransferTarget(Host, Port), options, sent.Add, _ => { }, null);

        options.Protocol = ProtocolConfig.Parse(Minimal); // swapped mid-connection: only new sessions use it
        Assert.Same(ProtocolConfig.Default, session.Protocol);

        session.HandleBatch(BatchCodec.Join(Packet((int)PacketId.RequestNetworkSettingsPacket, w => w.Int32BE(2250))));
        Assert.Equal((uint)PacketId.NetworkSettingsPacket, new ByteReader(BatchCodec.Split(sent.Single())[0]).VarUInt32());
        Assert.False(session.IsFinished);
    }

    [Fact]
    public void Session_ReadsMatchesAndClampsAsConfigured()
    {
        // Wait for packet 9; it must start with 7, then a radius stored clamped to 2..3, echoed back in Transfer.
        var text = Minimal
            .Replace("\"expect\": { \"packet\": 1 }",
                "\"expect\": { \"packet\": 9, \"read\": [ { \"Byte\": [7] }, { \"VarInt32\": \"$radius\", \"clamp\": [2, 3] } ] }")
            .Replace("{ \"Byte\": 42 }", "{ \"VarInt32\": \"$radius\" }");
        var options = new TransferOptions { Protocol = ProtocolConfig.Parse(text) };
        var sent = new List<byte[]>();
        var session = new TransferSession("test", () => new TransferTarget(Host, Port), options, sent.Add, _ => { }, null);

        session.HandleBatch(BatchCodec.Join(Packet(9, w => { w.Byte(8); w.VarInt32(99); }))); // wrong first byte: ignored
        Assert.Empty(sent);
        session.HandleBatch(BatchCodec.Join(Packet(9, w => { w.Byte(7); w.VarInt32(99); })));

        var r = new ByteReader(BatchCodec.Split(sent.Single())[0]);
        r.VarUInt32();
        r.String();
        Assert.Equal(3, r.VarInt32()); // 99 clamped to 3
    }

    [Fact]
    public void Session_BranchesOnAReadVariable()
    {
        // Packet 9 carries a status: 1 or 2 closes without a word, anything else transfers.
        var text = Minimal
            .Replace("\"expect\": { \"packet\": 1 }", "\"expect\": { \"packet\": 9, \"read\": [ { \"Byte\": \"$status\" } ] }")
            .Replace("\"steps\": [ { \"transfer\": true } ]",
                "\"steps\": [ { \"if\": { \"variable\": { \"$status\": [1, 2] } }, \"close\": true }, { \"transfer\": true } ]");
        var config = ProtocolConfig.Parse(text);

        List<byte[]> Run(byte status)
        {
            var sent = new List<byte[]>();
            var session = new TransferSession("test", () => new TransferTarget(Host, Port), new TransferOptions { Protocol = config },
                sent.Add, _ => { }, null);
            session.HandleBatch(BatchCodec.Join(Packet(9, w => w.Byte(status))));
            return sent;
        }

        Assert.Empty(Run(2));
        Assert.Single(Run(3));
    }

    // ---- Declared types, arrays and optional fields ----

    private const string Types = """
        "Vec3": [ { "FloatLE": "x" }, { "FloatLE": "y" }, { "FloatLE": "z" } ],
        "Id": [ { "VarInt64": "id" } ],
        "Toggle": [ { "String": "name" }, { "Bool": "enabled" } ],
        "Experiments": [ { "Array<Toggle>": "toggles", "count": "UInt32LE" }, { "Bool": "ever" } ],
        "Versioned": [ { "Byte": "a" }, { "Byte": "b", "since": 2000 }, { "Id": "c", "optional": true } ],
        """;

    [Fact]
    public void DeclaredType_ObjectArrayAndShorthandWriteTheSameBytes()
    {
        var expected = Hex(w => { w.FloatLE(1); w.FloatLE(2); w.FloatLE(3); });
        Assert.Equal(expected, BuildBody("""{ "Vec3": { "x": 1, "y": 2, "z": 3 } }"""));
        Assert.Equal(expected, BuildBody("""{ "Vec3": [1, 2, 3] }"""));
        Assert.Equal(Hex(w => w.VarInt64(-5)), BuildBody("""{ "Id": -5 }"""));
        Assert.Equal(Hex(w => w.VarInt64(-5)), BuildBody("""{ "Id": { "id": -5 } }"""));
    }

    [Fact]
    public void NestedTypesAndArrays_WriteCountsAndItems()
    {
        var body = """{ "Experiments": { "toggles": [ { "name": "a", "enabled": true }, ["b", false] ], "ever": true } }""";
        var expected = Hex(w =>
        {
            w.UInt32LE(2); // "count": "UInt32LE"
            w.String("a");
            w.Bool(true);
            w.String("b");
            w.Bool(false);
            w.Bool(true);
        });
        Assert.Equal(expected, BuildBody(body));
        Assert.Equal(Hex(w => { w.VarUInt32(3); w.String("x"); w.String("y"); w.String("$z"); }),
            BuildBody("""{ "Array<String>": [ "x", "y", "$$z" ] }"""));
    }

    [Fact]
    public void OptionalFields_WriteAPresenceFlag()
    {
        Assert.Equal(Hex(w => w.Bool(false)), BuildBody("""{ "Vec3": null, "optional": true }"""));
        Assert.Equal(Hex(w => { w.Bool(true); w.FloatLE(0); w.FloatLE(1); w.FloatLE(0); }),
            BuildBody("""{ "Vec3": [0, 1, 0], "optional": true }"""));
    }

    [Fact]
    public void DeclaredTypeFields_FollowTheProtocol()
    {
        // "b" is since 2000; optional "c" left out is absent.
        Assert.Equal(Hex(w => { w.Byte(1); w.Bool(false); }), BuildBody("""{ "Versioned": { "a": 1, "b": 2 } }""", 1999));
        Assert.Equal(Hex(w => { w.Byte(1); w.Byte(2); w.Bool(false); }), BuildBody("""{ "Versioned": { "a": 1, "b": 2 } }""", 2000));
        Assert.Equal(Hex(w => { w.Byte(1); w.Byte(2); w.Bool(true); w.VarInt64(9); }),
            BuildBody("""{ "Versioned": { "a": 1, "b": 2, "c": 9 } }""", 2000));
    }

    [Fact]
    public void DeclaredTypeFields_TakeVariables()
    {
        var config = ProtocolConfig.Parse(Config(Types, """{ "Vec3": { "x": 0, "y": "$protocol", "z": 0 } }"""));
        var bytes = config.BuildPacket("P", 2250, new Dictionary<string, object?>());
        var r = new ByteReader(bytes);
        r.VarUInt32();
        r.UInt32LE();
        Assert.Equal(2250f, BitConverter.Int32BitsToSingle(r.Int32LE()));
    }

    [Theory]
    [InlineData("""{ "Vec4": [1, 2, 3, 4] }""", "unknown type")]
    [InlineData("""{ "Vec3": { "x": 1, "y": 2 } }""", "needs \"z\"")]
    [InlineData("""{ "Vec3": { "x": 1, "y": 2, "z": 3, "w": 4 } }""", "has no field \"w\"")]
    [InlineData("""{ "Vec3": [1, 2] }""", "3 field(s)")]
    [InlineData("""{ "Vec3": 1 }""", "takes an object of its fields")]
    [InlineData("""{ "Vec3": null }""", "only allowed for optional fields")]
    [InlineData("""{ "Vec3": [1, 2, 3], "count": "UInt32LE" }""", "only applies to Array")]
    [InlineData("""{ "Array<Vec3>": [], "count": "String" }""", "the count is one of")]
    [InlineData("""{ "Array<Nope>": [] }""", "unknown type")]
    [InlineData("""{ "Experiments": { "toggles": [ { "name": "a" } ], "ever": true } }""", "needs \"enabled\"")]
    public void DeclaredTypes_RejectBadValues(string body, string expectedMessage)
    {
        var ex = Assert.Throws<ProtocolConfigException>(() => ProtocolConfig.Parse(Config(Types, body)));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData(""" "Loop": [ { "Loop": "self" } ], """, "contains itself")]
    [InlineData(""" "A": [ { "Array<B>": "bs" } ], "B": [ { "A": "a" } ], """, "contains itself")]
    [InlineData(""" "Byte": [ { "Bool": "b" } ], """, "can't reuse a primitive name")]
    [InlineData(""" "Pair": [ { "Byte": "a" }, { "Bool": "a" } ], """, "already has a field \"a\"")]
    [InlineData(""" "Pair": [ { "Byte": "a", "until": 2000 }, { "Bool": "a", "since": 1999 } ], """, "already has a field \"a\"")]
    [InlineData(""" "Empty": [], """, "at least one field")]
    [InlineData(""" "Bad": [ { "Byte": 1 } ], """, "expected a string")]
    public void DeclaredTypes_RejectBadDeclarations(string types, string expectedMessage)
    {
        var ex = Assert.Throws<ProtocolConfigException>(() => ProtocolConfig.Parse(Config(types, """{ "Byte": 0 }""")));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void DeclaredTypeFields_CanRepeatANameForDisjointProtocols()
    {
        // "a" is a required Byte before 2000 and an optional UInt16LE from 2000: one value feeds both.
        const string types = """ "Pair": [ { "Byte": "a", "until": 2000 }, { "UInt16LE": "a", "since": 2000, "optional": true }, { "Bool": "b" } ], """;
        string Body(string field, int protocol)
        {
            var bytes = ProtocolConfig.Parse(Config(types, field)).BuildPacket("P", protocol, new Dictionary<string, object?>());
            var r = new ByteReader(bytes);
            r.VarUInt32();
            return Convert.ToHexString(r.RemainingBytes());
        }

        Assert.Equal(Hex(w => { w.Byte(5); w.Bool(true); }), Body("""{ "Pair": { "a": 5, "b": true } }""", 1999));
        Assert.Equal(Hex(w => { w.Bool(true); w.UInt16LE(5); w.Bool(true); }), Body("""{ "Pair": { "a": 5, "b": true } }""", 2000));

        // The array form lists every declared field, repeats included.
        Assert.Equal(Hex(w => { w.Byte(1); w.Bool(false); }), Body("""{ "Pair": [1, 300, false] }""", 1999));
        Assert.Equal(Hex(w => { w.Bool(true); w.UInt16LE(300); w.Bool(false); }), Body("""{ "Pair": [1, 300, false] }""", 2000));
    }

    /// <summary>Builds packet P (body = one field) from a config with the given types; returns its body as hex.</summary>
    private static string BuildBody(string field, int protocol = 2250)
    {
        var bytes = ProtocolConfig.Parse(Config(Types, field)).BuildPacket("P", protocol, new Dictionary<string, object?>());
        var r = new ByteReader(bytes);
        r.VarUInt32(); // id
        return Convert.ToHexString(r.RemainingBytes());
    }

    private static string Hex(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return Convert.ToHexString(w.ToArray());
    }

    private static string Config(string types, string field) => $$"""
        {
          "formatVersion": 1,
          "types": { {{types}} },
          "packets": {
            "P": { "id": 200, "body": [ {{field}} ] },
            "Transfer": { "id": 85, "body": [ { "String": "$target.host" } ] },
            "Disconnect": { "id": 5, "body": [] },
          },
          "session": {
            "start": "s", "transferPacket": "Transfer", "offlinePacket": "Disconnect",
            "stages": { "s": { "expect": { "packet": 1 }, "steps": [ { "send": "P", "transfer": true } ] } },
          },
        }
        """;

    // ---- Helpers ----

    /// <summary>A small valid config (with comments and trailing commas) that the parse tests edit.</summary>
    private const string Minimal = """
        // A comment
        {
          "formatVersion": 1,
          "configVersion": "test.1",
          "packets": {
            /* block comment */
            "Transfer": { "id": 85, "body": [ { "String": "$target.host" }, { "Byte": 42 }, ] },
            "Disconnect": { "id": 5, "body": [ { "String": "$offlineMessage" } ] },
          },
          "session": {
            "start": "login",
            "transferPacket": "Transfer",
            "offlinePacket": "Disconnect",
            "stages": {
              "login": { "expect": { "packet": 1 }, "steps": [ { "transfer": true } ] },
            },
          },
        }
        """;

    private static readonly string[] DefinitionNames =
        Definitions.DefaultProtocol.Definition.Packets.Select(p => p.Name).ToArray();

    private static byte[] Packet(int id, Action<ByteWriter> body)
    {
        var writer = new ByteWriter();
        writer.VarUInt32((uint)id);
        body(writer);
        return writer.ToArray();
    }
}
