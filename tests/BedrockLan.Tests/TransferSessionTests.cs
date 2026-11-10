using BedrockLan.Bedrock;
using BedrockLan.Bedrock.Packets;
using BedrockLan.Internal;
using Xunit;

namespace BedrockLan.Tests;

/// <summary>The whole login → transfer conversation, driven the way a client would.</summary>
public class TransferSessionTests
{
    private static readonly TransferTarget Target = new("play.example.com", 19132);

    [Theory]
    [InlineData(2225, TransferTiming.AfterSpawn)]
    [InlineData(2225, TransferTiming.AfterResourcePacks)]
    [InlineData(800, TransferTiming.AfterSpawn)]
    [InlineData(800, TransferTiming.AfterResourcePacks)]
    [InlineData(700, TransferTiming.Immediate)]
    public void Session_EndsWithTransfer(int protocol, TransferTiming timing)
    {
        var client = new FakeClient(protocol, timing);

        client.RequestNetworkSettings();
        Assert.Equal((int)PacketId.NetworkSettingsPacket, client.LastIds()[0]);

        client.Login();
        if (timing != TransferTiming.Immediate)
        {
            client.ResourcePackResponse(3); // have all packs
            client.ResourcePackResponse(4); // completed
        }

        if (timing == TransferTiming.AfterSpawn && protocol == 2225)
        {
            Assert.Equal((int)PacketId.ItemRegistryPacket, client.LastIds()[^1]);
            client.RequestChunkRadius(12);
            Assert.False(client.Session.IsFinished);
            client.Initialised();
        }

        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
        Assert.True(client.Session.IsFinished);
        Assert.NotNull(client.CloseDelay);
    }

    /// <summary>
    /// The full conversation for every documented protocol (plus newer stand-ins) and timing: the expected packets in order, each one
    /// read back with the docs-derived readers for that protocol.
    /// </summary>
    [Theory]
    [MemberData(nameof(BedrockDocs.ProtocolsAndTimings), MemberType = typeof(BedrockDocs))]
    public void Protocol_FollowsTheHandshake(int protocol, TransferTiming timing)
    {
        var spawns = timing == TransferTiming.AfterSpawn && protocol >= 2207; // 1.26.60+ clients spawn first
        var packs = timing != TransferTiming.Immediate;

        var client = new FakeClient(protocol, timing);
        client.RequestNetworkSettings();
        client.Login();
        if (packs)
        {
            client.ResourcePackResponse(3);
            client.ResourcePackResponse(4);
        }

        if (spawns)
        {
            Assert.False(client.Session.IsFinished);
            client.RequestChunkRadius(6);
            client.Initialised();
        }

        var expected = new List<PacketId> { PacketId.NetworkSettingsPacket, PacketId.PlayStatusPacket };
        if (packs)
        {
            expected.AddRange(new[] { PacketId.ResourcePacksInfoPacket, PacketId.ResourcePackStackPacket });
        }

        if (spawns)
        {
            expected.AddRange(new[]
            {
                PacketId.JigsawStructureDataPacket, PacketId.VoxelShapesPacket, PacketId.StartGamePacket,
                PacketId.ItemRegistryPacket, PacketId.ChunkRadiusUpdatedPacket, PacketId.PlayStatusPacket,
                PacketId.CreativeContentPacket,
            });
        }

        expected.Add(PacketId.TransferPacket);
        Assert.Equal(expected.Select(id => (int)id), client.AllIds());
        Assert.True(client.Session.IsFinished);
        Assert.Equal(protocol, client.Session.ClientProtocol);

        foreach (var packet in client.DecodedPackets())
        {
            BedrockDocs.ReadAny(packet, protocol);
        }

        Assert.Equal((Target.Host, Target.Port), BedrockDocs.ReadTransfer(client.LastPackets()[^1], protocol));
    }

    [Theory]
    [InlineData(2193)] // 1.26.50: accepts a transfer before spawning
    public void AfterSpawn_BeforeTheSpawnRange_TransfersAfterResourcePacks(int protocol)
    {
        var client = new FakeClient(protocol, TransferTiming.AfterSpawn);
        client.RequestNetworkSettings();
        client.Login();
        client.ResourcePackResponse(3);
        client.ResourcePackResponse(4);

        Assert.DoesNotContain((int)PacketId.StartGamePacket, client.AllIds());
        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
        Assert.True(client.Session.IsFinished);
    }

    /// <summary>A protocol newer than the docs gets exactly the newest known protocol's conversation.</summary>
    [Theory]
    [MemberData(nameof(BedrockDocs.NewerThanKnown), MemberType = typeof(BedrockDocs))]
    public void NewerProtocol_SendsTheSameBytesAsTheNewestKnown(int protocol)
    {
        var newer = Run(protocol);
        var known = Run(BedrockDocs.NewestKnown);

        Assert.Equal(known.Count, newer.Count);
        for (var i = 0; i < known.Count; i++)
        {
            if ((PacketId)new ByteReader(known[i]).VarUInt32() == PacketId.StartGamePacket)
            {
                Assert.Equal(known[i].Length, newer[i].Length); // differs only in its random correlation id
            }
            else
            {
                Assert.Equal(known[i], newer[i]);
            }
        }

        static List<byte[]> Run(int protocol)
        {
            var client = new FakeClient(protocol, TransferTiming.AfterSpawn);
            client.RequestNetworkSettings();
            client.Login();
            client.ResourcePackResponse(3);
            client.ResourcePackResponse(4);
            client.RequestChunkRadius(6);
            client.Initialised();
            Assert.True(client.Session.IsFinished);
            return client.DecodedPackets();
        }
    }

    [Fact]
    public void ResourcePacks_FirstResponseSendsStackSecondAdvances()
    {
        // No packs are offered, so the client's response value doesn't matter: the first response sends the stack,
        // the next one advances. (A 1.26.60 client reports DownloadingFinished as 2 and ResourcePackStackFinished as 3.)
        var client = new FakeClient(2225, TransferTiming.AfterResourcePacks);
        client.RequestNetworkSettings();
        client.Login();

        var sent = client.Packets.Count;
        client.ResourcePackResponse(2); // downloadingfinished on 2225
        Assert.Equal(sent + 1, client.Packets.Count);
        Assert.Equal((int)PacketId.ResourcePackStackPacket, client.LastIds()[^1]);
        Assert.False(client.Session.IsFinished);

        client.ResourcePackResponse(3); // resourcepackstackfinished on 2225 (not 4)
        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
        Assert.True(client.Session.IsFinished);
    }

    [Fact]
    public void ResourcePacks_CancelStillGetsTheStack()
    {
        var client = new FakeClient(2225, TransferTiming.AfterResourcePacks);
        client.RequestNetworkSettings();
        client.Login();

        client.ResourcePackResponse(1); // cancel: packs aren't required
        Assert.Equal((int)PacketId.ResourcePackStackPacket, client.LastIds()[^1]);

        client.ResourcePackResponse(3);
        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
    }

    /// <summary>
    /// The real 1.26.60 (protocol 2225) sequence from the log: DownloadingFinished (2), then
    /// ResourcePackStackFinished (3). The stack is sent exactly once, then the temporary game starts.
    /// </summary>
    [Fact]
    public void ResourcePacks_SendsTheStackOnce()
    {
        var client = new FakeClient(2225, TransferTiming.AfterSpawn);
        client.RequestNetworkSettings();
        client.Login();

        client.ResourcePackResponse(2);
        client.ResourcePackResponse(3);

        Assert.Equal((int)PacketId.ItemRegistryPacket, client.LastIds()[^1]); // on to the temporary game
        var stacks = client.AllIds().Count(id => id == (int)PacketId.ResourcePackStackPacket);
        Assert.Equal(1, stacks);
    }

    [Fact]
    public void AfterSpawn_SendsPacketsInOrder()
    {
        var client = new FakeClient(2225, TransferTiming.AfterSpawn);
        client.RequestNetworkSettings();
        client.Login();
        client.ResourcePackResponse(3);
        client.ResourcePackResponse(4);
        client.RequestChunkRadius(4);
        client.Initialised();

        Assert.Equal(
            new[]
            {
                PacketId.NetworkSettingsPacket,
                PacketId.PlayStatusPacket,
                PacketId.ResourcePacksInfoPacket,
                PacketId.ResourcePackStackPacket,
                PacketId.JigsawStructureDataPacket,
                PacketId.VoxelShapesPacket,
                PacketId.StartGamePacket,
                PacketId.ItemRegistryPacket,
                PacketId.ChunkRadiusUpdatedPacket,
                PacketId.PlayStatusPacket,
                PacketId.CreativeContentPacket,
                PacketId.TransferPacket,
            }.Select(id => (int)id),
            client.AllIds());
    }

    [Fact]
    public void Transfer_CarriesTheTargetAddress()
    {
        TransferTarget? reported = null;
        var client = new FakeClient(800, TransferTiming.Immediate, transferred: (t, _) => reported = t);
        client.RequestNetworkSettings();
        client.Login();

        var r = new ByteReader(client.LastPackets()[^1]);
        Assert.Equal((uint)PacketId.TransferPacket, r.VarUInt32());
        Assert.Equal(Target.Host, r.String());
        Assert.Equal(Target.Port, r.UInt16LE());
        Assert.Equal(Target, reported);
    }

    [Fact]
    public void OfflineTarget_SendsDisconnectWithMessage()
    {
        var client = new FakeClient(800, TransferTiming.Immediate, target: () => null);
        client.RequestNetworkSettings();
        client.Login();

        var r = new ByteReader(client.LastPackets()[^1]);
        Assert.Equal((uint)PacketId.DisconnectPacket, r.VarUInt32());
        r.VarInt32(); // reason
        r.Bool();     // skip message
        Assert.Equal(new TransferOptions().OfflineMessage, r.String());
        Assert.True(client.Session.IsFinished);
    }

    [Fact]
    public void ProtocolWithoutResourcePackLayouts_TransfersRightAfterLogin()
    {
        var client = new FakeClient(700, TransferTiming.AfterResourcePacks);
        client.RequestNetworkSettings();
        client.Login();

        Assert.Equal(new[] { (int)PacketId.PlayStatusPacket, (int)PacketId.TransferPacket }, client.AllIds().Skip(1));
        Assert.True(client.Session.IsFinished);
    }

    [Fact]
    public void ChunkRadius_IsClampedToEight()
    {
        var client = new FakeClient(2225, TransferTiming.AfterSpawn);
        client.RequestNetworkSettings();
        client.Login();
        client.ResourcePackResponse(3);
        client.ResourcePackResponse(4);
        client.RequestChunkRadius(32);

        var packet = client.DecodedPackets()
            .First(p => new ByteReader(p).VarUInt32() == (uint)PacketId.ChunkRadiusUpdatedPacket);
        var r = new ByteReader(packet);
        r.VarUInt32();
        Assert.Equal(8, r.VarInt32());
    }

    [Fact]
    public async Task MissingSpawnConfirmation_TransfersAfterTimeout()
    {
        var client = new FakeClient(2225, TransferTiming.AfterSpawn, new TransferOptions
        {
            Timing = TransferTiming.AfterSpawn,
            SpawnTimeout = TimeSpan.FromMilliseconds(50),
        });
        client.RequestNetworkSettings();
        client.Login();
        client.ResourcePackResponse(3);
        client.ResourcePackResponse(4);
        client.RequestChunkRadius(4);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!client.Session.IsFinished && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(client.Session.IsFinished);
        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
    }

    [Fact]
    public void PacketsAfterTransfer_AreIgnored()
    {
        var client = new FakeClient(800, TransferTiming.Immediate);
        client.RequestNetworkSettings();
        client.Login();
        var sent = client.Packets.Count;

        client.ResourcePackResponse(4);
        client.Initialised();

        Assert.Equal(sent, client.Packets.Count);
    }

    [Fact]
    public void UnreadableBatch_EndsSessionAndClosesNow()
    {
        var client = new FakeClient(800, TransferTiming.AfterResourcePacks);
        client.RequestNetworkSettings();

        client.Session.HandleBatch(new byte[] { 0x05, 0x01, 0x02 }); // unknown compression algorithm

        Assert.True(client.Session.IsFinished);
        Assert.Equal(TimeSpan.Zero, client.CloseDelay);
    }

    [Theory]
    [InlineData(600)]  // raw deflate
    [InlineData(800)]  // algorithm byte + deflate
    public void NetworkSettings_IsUncompressed_ThenEverythingIsCompressed(int protocol)
    {
        var client = new FakeClient(protocol, TransferTiming.Immediate);
        client.RequestNetworkSettings();
        Assert.Equal((uint)PacketId.NetworkSettingsPacket, new ByteReader(BatchCodec.Split(client.Packets[0])[0]).VarUInt32());

        client.Login();
        if (protocol >= BedrockPackets.ProtocolCompressionHeader)
        {
            Assert.Equal(0x00, client.Packets[^1][0]);
        }

        Assert.Equal((int)PacketId.TransferPacket, client.LastIds()[^1]);
    }

    private static List<byte[]> Decode(byte[] wire, int index, int protocol)
    {
        if (index == 0)
        {
            return BatchCodec.Split(wire).ToList(); // NetworkSettings goes out uncompressed
        }

        var batch = protocol >= BedrockPackets.ProtocolCompressionHeader ? BatchCodec.Inflate(wire.AsSpan(1)) : BatchCodec.Inflate(wire);
        return BatchCodec.Split(batch).ToList();
    }

    /// <summary>Plays the client side of the handshake against a <see cref="TransferSession"/>.</summary>
    private sealed class FakeClient
    {
        private readonly int _protocol;

        public FakeClient(int protocol, TransferTiming timing, TransferOptions? options = null,
            Func<TransferTarget?>? target = null, Action<TransferTarget, int>? transferred = null)
        {
            _protocol = protocol;
            Session = new TransferSession("test", target ?? (() => Target), options ?? new TransferOptions { Timing = timing },
                Packets.Add, d => CloseDelay = d, null, transferred);
        }

        public TransferSession Session { get; }

        /// <summary>Raw batches the session sent, in order.</summary>
        public List<byte[]> Packets { get; } = new();

        public TimeSpan? CloseDelay { get; private set; }

        public void RequestNetworkSettings() =>
            Session.HandleBatch(BatchCodec.Join(Build(PacketId.RequestNetworkSettingsPacket, w => w.Int32BE(_protocol))));

        public void Login() => SendCompressed(PacketId.LoginPacket, w => w.Int32BE(_protocol));

        public void ResourcePackResponse(byte status) => SendCompressed(PacketId.ResourcePackClientResponsePacket, w => w.Byte(status));

        public void RequestChunkRadius(int radius) => SendCompressed(PacketId.RequestChunkRadiusPacket, w => { w.VarInt32(radius); w.Byte((byte)radius); });

        public void Initialised() => SendCompressed(PacketId.SetLocalPlayerAsInitializedPacket, w => w.VarUInt32(1));

        /// <summary>Every packet the session sent, decoded from all batches.</summary>
        public List<byte[]> DecodedPackets() => Packets.SelectMany((wire, i) => TransferSessionTests.Decode(wire, i, _protocol)).ToList();

        /// <summary>The packets in the most recent batch.</summary>
        public List<byte[]> LastPackets() => TransferSessionTests.Decode(Packets[^1], Packets.Count - 1, _protocol);

        public List<int> AllIds() => DecodedPackets().Select(IdOf).ToList();

        public List<int> LastIds() => LastPackets().Select(IdOf).ToList();

        private static int IdOf(byte[] packet) => (int)(new ByteReader(packet).VarUInt32() & 0x3FF);

        private void SendCompressed(PacketId id, Action<ByteWriter> body)
        {
            var batch = BatchCodec.Join(Build(id, body));
            var deflated = BatchCodec.Deflate(batch);
            Session.HandleBatch(_protocol >= BedrockPackets.ProtocolCompressionHeader
                ? new byte[] { 0x00 }.Concat(deflated).ToArray()
                : deflated);
        }

        private static byte[] Build(PacketId id, Action<ByteWriter> body)
        {
            var writer = new ByteWriter();
            writer.VarUInt32((uint)id);
            body(writer);
            return writer.ToArray();
        }
    }
}
