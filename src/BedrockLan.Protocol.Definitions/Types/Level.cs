namespace BedrockLan.Protocol.Definitions.Types;

// StartGame's nested types (Mojang's bedrock-protocol-docs, checked for protocols 2168-2259).

[ProtocolType]
[Doc("SpawnSettings: biome and dimension the player spawns in.")]
public sealed class SpawnSettings
{
    [Field(0, Wire = Wire.Int16LE)]
    [Doc("SpawnBiomeType enum: 0 default")]
    public short SpawnBiomeType { get; init; }

    [Field(1)]
    public string UserDefinedBiomeName { get; init; } = "";

    [Field(2, Wire = Wire.VarInt32)]
    [Doc("0 overworld")]
    public int Dimension { get; init; }
}

[ProtocolType]
[Doc("GameRule. Only boolean rules can be described: the value is a union (bool, int or float) chosen by its type id.")]
public sealed class GameRule
{
    [Field(0)]
    public string RuleName { get; init; } = "";

    [Field(1)]
    public bool RuleCanBeModified { get; init; }

    [Field(2, Wire = Wire.VarUInt32)]
    [Doc("value type: 1 bool")]
    public uint RuleValueType { get; init; } = 1;

    [Field(3)]
    public bool RuleValue { get; init; }
}

[ProtocolType]
[Doc("GameRulesChangedPacketData: the game rules.")]
public sealed class GameRulesChangedPacketData
{
    [Field(0)]
    public List<GameRule> RulesList { get; init; } = new();
}

[ProtocolType]
[Doc("EduSharedUriResource: an education edition link button.")]
public sealed class EduSharedUriResource
{
    [Field(0)]
    public string ButtonName { get; init; } = "";

    [Field(1)]
    public string LinkUri { get; init; } = "";
}

[ProtocolType]
[Doc("LevelSettings: the world's settings.")]
public sealed class LevelSettings
{
    [Field(0, Wire = Wire.UInt64LE)]
    public ulong Seed { get; init; }

    [Field(1)]
    public SpawnSettings SpawnSettings { get; init; } = new();

    [Field(2, Wire = Wire.VarInt32)]
    [Doc("GeneratorType: 2 flat")]
    public int GeneratorType { get; init; } = 2;

    [Field(3, Wire = Wire.VarInt32)]
    [Doc("GameType: 2 adventure")]
    public int GameType { get; init; } = 2;

    [Field(4)]
    public bool IsHardcore { get; init; }

    [Field(5, Wire = Wire.VarInt32)]
    [Doc("Difficulty: 1 easy")]
    public int GameDifficulty { get; init; } = 1;

    [Field(6)]
    public BlockPos DefaultSpawnBlockPosition { get; init; } = new() { Y = 100 };

    [Field(7)]
    public bool AchievementsDisabled { get; init; } = true;

    [Field(8, Wire = Wire.VarInt32)]
    public int EditorWorldType { get; init; }

    [Field(9)]
    public bool IsCreatedInEditor { get; init; }

    [Field(10)]
    public bool IsExportedFromEditor { get; init; }

    [Field(11, Wire = Wire.Byte)]
    [Since(2216)]
    [Doc("EditorLevelMigrationVersion: 0 legacy")]
    public byte EditorLevelMigrationVersion { get; init; }

    [Field(12, Wire = Wire.VarInt32)]
    public int DayCycleStopTime { get; init; }

    [Field(13, Wire = Wire.VarUInt32)]
    public uint EducationEditionOffer { get; init; }

    [Field(14)]
    public bool EducationFeaturesEnabled { get; init; }

    [Field(15)]
    public string EducationProductId { get; init; } = "";

    [Field(16)]
    public float RainLevel { get; init; }

    [Field(17)]
    public float LightningLevel { get; init; }

    [Field(18)]
    public bool HasConfirmedPlatformLockedContent { get; init; }

    [Field(19)]
    public bool MultiplayerGameIntent { get; init; } = true;

    [Field(20)]
    public bool LanBroadcastIntent { get; init; } = true;

    [Field(21, Wire = Wire.VarInt32)]
    public int XboxLiveBroadcastSetting { get; init; }

    [Field(22, Wire = Wire.VarInt32)]
    public int PlatformBroadcastSetting { get; init; }

    [Field(23)]
    public bool CommandsEnabled { get; init; }

    [Field(24)]
    public bool TexturePacksRequired { get; init; }

    [Field(25)]
    public GameRulesChangedPacketData RuleData { get; init; } = new();

    [Field(26)]
    public Experiments Experiments { get; init; } = new();

    [Field(27)]
    public bool HasBonusChestEnabled { get; init; }

    [Field(28)]
    public bool StartWithMapEnabled { get; init; }

    [Field(29, Wire = Wire.Byte)]
    [Doc("PlayerPermissionLevel: 1 member")]
    public byte PlayerPermissions { get; init; } = 1;

    [Field(30, Wire = Wire.Int32LE)]
    public int ServerChunkTickRange { get; init; } = 4;

    [Field(31)]
    public bool HasLockedBehaviorPack { get; init; }

    [Field(32)]
    public bool HasLockedResourcePack { get; init; }

    [Field(33)]
    public bool IsFromLockedTemplate { get; init; }

    [Field(34)]
    public bool UseMsaGamertagsOnly { get; init; }

    [Field(35)]
    public bool IsFromWorldTemplate { get; init; }

    [Field(36)]
    public bool IsWorldTemplateOptionLocked { get; init; }

    [Field(37)]
    public bool OnlySpawnV1Villagers { get; init; }

    [Field(38)]
    public bool PersonaDisabled { get; init; }

    [Field(39)]
    public bool CustomSkinsDisabled { get; init; }

    [Field(40)]
    public bool EmoteChatMuted { get; init; }

    [Field(41)]
    public string BaseGameVersion { get; init; } = "*";

    [Field(42, Wire = Wire.Int32LE)]
    public int LimitedWorldWidth { get; init; }

    [Field(43, Wire = Wire.Int32LE)]
    public int LimitedWorldDepth { get; init; }

    [Field(44)]
    [Doc("new nether")]
    public bool NetherType { get; init; } = true;

    [Field(45)]
    public EduSharedUriResource EduSharedUriResource { get; init; } = new();

    [Field(46)]
    [Optional]
    public bool? OverrideForceExperimentalGameplay { get; init; }

    [Field(47, Wire = Wire.Byte)]
    public byte ChatRestrictionLevel { get; init; }

    [Field(48)]
    public bool DisablePlayerInteractions { get; init; }

    [Field(49, Wire = Wire.VarInt32)]
    public int ServerEditorConnectionPolicy { get; init; }

    [Field(50)]
    public bool AllowAnonymousBlockDropsInEditorWorlds { get; init; }
}

[ProtocolType]
[Doc("SyncedPlayerMovementSettings: how movement is synchronised.")]
public sealed class SyncedPlayerMovementSettings
{
    [Field(0, Wire = Wire.VarInt32)]
    public int RewindHistorySize { get; init; }

    [Field(1)]
    public bool ServerAuthoritativeBlockBreaking { get; init; }
}

[ProtocolType]
[Doc("ServerBlockProperty: a custom block's name and definition.")]
public sealed class ServerBlockProperty
{
    [Field(0)]
    public string BlockName { get; init; } = "";

    [Field(1)]
    [Doc("map of block states (can be left empty)")]
    public NbtCompound BlockDefinition { get; init; } = new();
}

[ProtocolType]
[Doc("NetworkPermissions: what the server is authoritative for.")]
public sealed class NetworkPermissions
{
    [Field(0)]
    public bool ServerAuthSoundEnabled { get; init; }
}

[ProtocolType]
[Doc("ServerTelemetryData: ids for the client's telemetry.")]
public sealed class ServerTelemetryData
{
    [Field(0)]
    public string ServerId { get; init; } = "";

    [Field(1)]
    public string ScenarioId { get; init; } = "";

    [Field(2)]
    public string WorldId { get; init; } = "";

    [Field(3)]
    public string OwnerId { get; init; } = "";
}
