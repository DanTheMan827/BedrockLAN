using BedrockLan.Bedrock.Packets;
using BedrockLan.Protocol.Definitions.Packets;

namespace BedrockLan.Protocol.Definitions;

/// <summary>
/// The protocol config the library ships (src/BedrockLan/Protocol/default-protocol.jsonc) and the update server hosts
/// (protocol-{formatVersion}.jsonc). tools/ProtocolGen writes it from this definition.
/// </summary>
public static class DefaultProtocol
{
    /// <summary>Bump when the packets or the session change, so clients can tell which copy they have.</summary>
    public const string ConfigVersion = "2026-11-05.7";

    public static ProtocolDefinition Definition { get; } = new()
    {
        FormatVersion = ProtocolFormat.Version,
        ConfigVersion = ConfigVersion,
        Packets = new (string, object)[]
        {
            ("NetworkSettings", new NetworkSettingsPacket()),
            ("PlayStatusLoginSuccess", new PlayStatusPacket { Status = 0 }),
            ("PlayStatusPlayerSpawn", new PlayStatusPacket { Status = 3 }),
            ("ResourcePacksInfo", new ResourcePacksInfoPacket()),
            ("ResourcePackStack", new ResourcePackStackPacket()),
            ("JigsawStructureData", new JigsawStructureDataPacket()),
            ("VoxelShapes", new VoxelShapesPacket()),
            ("StartGame", new StartGamePacket()),
            ("ItemRegistry", new ItemRegistryPacket()),
            ("ChunkRadiusUpdated", new ChunkRadiusUpdatedPacket()),
            ("CreativeContent", new CreativeContentPacket()),
            ("Transfer", new TransferPacket()),
            ("Disconnect", new DisconnectPacket()),
        },
        Session = BuildSession(),
    };

    private static SessionDefinition BuildSession()
    {
        var session = new SessionDefinition
        {
            Start = "awaitNetworkSettingsRequest",
            TransferPacket = "Transfer",
            OfflinePacket = "Disconnect",
            CompressionHeaderSince = 649,
        };

        session.Stages.Add(new StageDefinition("awaitNetworkSettingsRequest", PacketId.RequestNetworkSettingsPacket)
        {
            Doc = "RequestNetworkSettings: the client's protocol. Reply uncompressed, then compress everything after.",
            Read = { ReadDefinition.Into(Wire.Int32BE, "protocol") },
            Steps =
            {
                new StepDefinition { Send = { "NetworkSettings" }, Compression = true, Goto = "awaitLogin" },
            },
        });

        session.Stages.Add(new StageDefinition("awaitLogin", PacketId.LoginPacket)
        {
            Doc = "Login. Older clients (before RequestNetworkSettings) report their protocol here first.",
            Read = { ReadDefinition.Into(Wire.Int32BE, "protocol", keep: true) },
            Steps =
            {
                new StepDefinition { Send = { "PlayStatusLoginSuccess" } },
                new StepDefinition
                {
                    Doc = "No resource pack stage for Immediate, or for clients older than 1.21.30 (729).",
                    If = When.Any(When.Timing(TransferTiming.Immediate), When.Protocol(max: 728)),
                    Transfer = true,
                },
                new StepDefinition { Send = { "ResourcePacksInfo" }, Goto = "awaitPacksResponse" },
            },
        });

        session.Stages.Add(new StageDefinition("awaitPacksResponse", PacketId.ResourcePackClientResponsePacket)
        {
            Doc = """
                ResourcePackClientResponse. No packs are offered, so the client's first response (whatever it is:
                Cancel, Downloading, or DownloadingFinished) means it's done with the empty set. Send the stack.

                The response's enum value isn't relied on: it shifted between versions (a 1.26.60 client reports
                DownloadingFinished as 2 and ResourcePackStackFinished as 3, where older docs list 3 and 4), and the
                value doesn't matter when there are no packs. Waiting for one specific number is what made older
                builds loop here.
                """,
            Steps =
            {
                new StepDefinition { Send = { "ResourcePackStack" }, Goto = "awaitStackResponse" },
            },
        });

        var stackResponse = new StageDefinition("awaitStackResponse", PacketId.ResourcePackClientResponsePacket)
        {
            Doc = "The next ResourcePackClientResponse (ResourcePackStackFinished) acknowledges the stack; carry on.",
            Steps = { },
        };
        stackResponse.Steps.AddRange(AfterStack());
        session.Stages.Add(stackResponse);

        session.Stages.Add(new StageDefinition("awaitChunkRadiusRequest", PacketId.RequestChunkRadiusPacket)
        {
            Doc = "RequestChunkRadius: answer with the radius it asked for (clamped), spawn it, and send an empty creative inventory.",
            Read = { ReadDefinition.IntoClamped(Wire.VarInt32, "radius", 1, 8) },
            Steps =
            {
                new StepDefinition
                {
                    Send = { "ChunkRadiusUpdated", "PlayStatusPlayerSpawn", "CreativeContent" },
                    Goto = "awaitSpawned",
                },
            },
        });

        session.Stages.Add(new StageDefinition("awaitSpawned", PacketId.SetLocalPlayerAsInitializedPacket)
        {
            Doc = "SetLocalPlayerAsInitialized: the client has spawned. Transfer anyway if it doesn't say so in time.",
            Steps =
            {
                new StepDefinition { Log = "client spawned.", Transfer = true },
            },
            Timeout = new TimeoutDefinition
            {
                Seconds = "$spawnTimeout",
                Steps =
                {
                    new StepDefinition { Log = "no spawn confirmation in time; transferring anyway.", Transfer = true },
                },
            },
        });

        return session;
    }

    /// <summary>Once the client has the stack: a minimal game first for clients that need one, otherwise the Transfer.</summary>
    private static IEnumerable<StepDefinition> AfterStack() => new[]
    {
        new StepDefinition
        {
            Doc = "1.26.60+ clients (2207+) ignore a Transfer sent during login: start a minimal empty game first.",
            If = When.All(When.Timing(TransferTiming.AfterSpawn), When.Protocol(min: 2207)),
            Log = "starting temporary game.",
            Send = { "JigsawStructureData", "VoxelShapes", "StartGame", "ItemRegistry" },
            Goto = "awaitChunkRadiusRequest",
        },
        new StepDefinition { Transfer = true },
    };
}
