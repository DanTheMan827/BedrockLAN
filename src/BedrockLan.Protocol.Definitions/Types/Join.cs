namespace BedrockLan.Protocol.Definitions.Types;

// Join information sent with StartGame and Transfer (Mojang's bedrock-protocol-docs).

[ProtocolType]
[Doc("gatheringsConfig: the experience a player joins through. Every field became optional in 2192.")]
public sealed class GatheringsConfig
{
    [Field(0, Name = "experienceId")]
    [Until(2192)]
    public Uuid ExperienceIdRequired { get; init; } = new();

    [Field(1, Name = "experienceId")]
    [Since(2192)]
    [Optional]
    public Uuid? ExperienceId { get; init; }

    [Field(2, Name = "experienceName")]
    [Until(2192)]
    public string ExperienceNameRequired { get; init; } = "";

    [Field(3, Name = "experienceName")]
    [Since(2192)]
    [Optional]
    public string? ExperienceName { get; init; }

    [Field(4, Name = "worldId")]
    [Until(2192)]
    public Uuid WorldIdRequired { get; init; } = new();

    [Field(5, Name = "worldId")]
    [Since(2192)]
    [Optional]
    public Uuid? WorldId { get; init; }

    [Field(6, Name = "worldName")]
    [Until(2192)]
    public string WorldNameRequired { get; init; } = "";

    [Field(7, Name = "worldName")]
    [Since(2192)]
    [Optional]
    public string? WorldName { get; init; }

    [Field(8, Name = "creatorId")]
    [Until(2192)]
    public string CreatorIdRequired { get; init; } = "";

    [Field(9, Name = "creatorId")]
    [Since(2192)]
    [Optional]
    public string? CreatorId { get; init; }

    [Field(10, Name = "targetId")]
    [Until(2192)]
    public Uuid TargetIdRequired { get; init; } = new();

    [Field(11, Name = "targetId")]
    [Since(2192)]
    [Optional]
    public Uuid? TargetId { get; init; }

    [Field(12, Name = "scenarioId")]
    [Until(2192)]
    public string ScenarioIdRequired { get; init; } = "";

    [Field(13, Name = "scenarioId")]
    [Since(2192)]
    [Optional]
    public string? ScenarioId { get; init; }

    [Field(14, Name = "serverId")]
    [Until(2192)]
    public string ServerIdRequired { get; init; } = "";

    [Field(15, Name = "serverId")]
    [Since(2192)]
    [Optional]
    public string? ServerId { get; init; }
}

[ProtocolType]
[Doc("A store entry point for the client's store button.")]
public sealed class ClientStoreEntryPoint
{
    [Field(0)]
    public string StoreId { get; init; } = "";

    [Field(1)]
    public string StoreName { get; init; } = "";
}

[ProtocolType]
[Doc("Rich presence for the join.")]
public sealed class Presence
{
    [Field(0)]
    [Optional]
    public string? RichPresenceId { get; init; }
}

[ProtocolType]
[Doc("Managed chat settings.")]
public sealed class ManagedChat
{
    [Field(0)]
    [Optional]
    public bool? Enabled { get; init; }
}

[ProtocolType]
[Doc("server_config: server configuration join info.")]
public sealed class ServerConfig
{
    [Field(0)]
    [Optional]
    public GatheringsConfig? Gathering { get; init; }

    [Field(1)]
    [Optional]
    public ClientStoreEntryPoint? ClientStoreEntryPoint { get; init; }

    [Field(2)]
    [Optional]
    public Presence? Presence { get; init; }

    [Field(3)]
    [Since(2235)]
    [Optional]
    public ManagedChat? ManagedChat { get; init; }
}

[ProtocolType]
[Doc("TransferPlayerUIOptions: what the client shows while it transfers.")]
public sealed class TransferPlayerUIOptions
{
    [Field(0)]
    [Optional]
    public string? TitleText { get; init; }

    [Field(1)]
    [Optional]
    public string? BodyText { get; init; }

    [Field(2)]
    [Optional]
    public bool? TitleVisible { get; init; }

    [Field(3)]
    [Optional]
    public bool? BodyVisible { get; init; }

    [Field(4)]
    [Optional]
    public string? CancelButtonText { get; init; }

    [Field(5)]
    [Optional]
    public bool? CancelButtonVisible { get; init; }

    [Field(6)]
    [Optional]
    public float? CancelButtonDelaySeconds { get; init; }
}

[ProtocolType]
[Doc("DisconnectPacketMessages: the message and its filtered copy.")]
public sealed class DisconnectPacketMessages
{
    [Field(0)]
    [Variable("offlineMessage")]
    public string Message { get; init; } = "";

    [Field(1)]
    [Since(712)]
    [Variable("offlineMessage")]
    public string FilteredMessage { get; init; } = "";
}
