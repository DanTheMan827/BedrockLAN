// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.Bedrock.Packets;
using BedrockLan.Protocol.Definitions.Types;

namespace BedrockLan.Protocol.Definitions.Packets;

// The packets the transfer server sends, laid out as in Mojang's bedrock-protocol-docs (checked for 2168-2259) and,
// for older protocols, CloudburstMC/Protocol. Property initializers are the values sent; [Variable] fields are filled
// in by the session at send time.

[Packet((int)PacketId.NetworkSettingsPacket)]
[Doc("NetworkSettings: compress batches over 256 bytes with zlib.")]
public sealed class NetworkSettingsPacket
{
    [Field(0, Wire = Wire.UInt16LE)]
    [Doc("compression threshold")]
    public ushort CompressionThreshold { get; init; } = 256;

    [Field(1, Wire = Wire.UInt16LE)]
    [Doc("compression algorithm: ZLib (flate)")]
    public ushort CompressionAlgorithm { get; init; }

    [Field(2)]
    [Doc("client throttle enabled")]
    public bool ClientThrottleEnabled { get; init; }

    [Field(3)]
    [Doc("client throttle threshold")]
    public byte ClientThrottleThreshold { get; init; }

    [Field(4)]
    [Doc("client throttle scalar")]
    public float ClientThrottleScalar { get; init; }

    [Field(5, Wire = Wire.Byte)]
    [Since(2259)]
    [Doc("SCTP recovery profile: 0 default (1 fast, 2 balanced)")]
    public byte SctpRecoveryProfile { get; init; }
}

[Packet((int)PacketId.PlayStatusPacket)]
public sealed class PlayStatusPacket
{
    /// <summary>0 LoginSuccess, 3 PlayerSpawn.</summary>
    [Field(0, Wire = Wire.Int32BE)]
    [Doc("status: 0 login success, 3 player spawn")]
    public int Status { get; init; }
}

[Packet((int)PacketId.ResourcePacksInfoPacket)]
[Doc("An empty ResourcePacksInfo: no behaviour or resource packs.")]
public sealed class ResourcePacksInfoPacket
{
    [Field(0)]
    [Doc("resource pack required")]
    public bool ResourcePackRequired { get; init; }

    [Field(1)]
    [Doc("has addon packs")]
    public bool HasAddonPacks { get; init; }

    [Field(2)]
    [Doc("has scripts")]
    public bool HasScripts { get; init; }

    [Field(3)]
    [Since(818)]
    [Doc("force disable vibrant visuals")]
    public bool ForceDisableVibrantVisuals { get; init; }

    [Field(4)]
    [Since(766)]
    [Doc("world template id and version")]
    public PackIdVersion WorldTemplateIdAndVersion { get; init; } = new();

    [Field(5)]
    [Since(2168)]
    [Doc("resource packs")]
    public List<PackInfoData> ResourcePacks { get; init; } = new();

    [Field(6)]
    [Until(2168)]
    [Count(Wire.UInt16LE)]
    [Doc("resource packs (older layout: count is a UInt16LE; only ever empty here)")]
    public List<PackInfoData> ResourcePacksLegacy { get; init; } = new();

    [Field(7)]
    [Until(748)]
    [Doc("CDN URLs (before 748)")]
    public List<PackCdnUrl> CdnUrls { get; init; } = new();
}

[Packet((int)PacketId.ResourcePackStackPacket)]
[Doc("An empty ResourcePackStack.")]
public sealed class ResourcePackStackPacket
{
    [Field(0)]
    [Doc("texture pack required")]
    public bool TexturePackRequired { get; init; }

    [Field(1)]
    [Until(898)]
    [Doc("behaviour packs (before 898)")]
    public List<PackInstanceId> BehaviorPackList { get; init; } = new();

    [Field(2)]
    [Doc("resource packs")]
    public List<PackInstanceId> TexturePackList { get; init; } = new();

    [Field(3)]
    [Doc("base game version")]
    public string BaseGameVersion { get; init; } = "*";

    [Field(4)]
    public Experiments Experiments { get; init; } = new();

    [Field(5)]
    [Until(2239)]
    [Doc("include editor packs")]
    public bool IncludeEditorPacks { get; init; }

    [Field(6)]
    [Since(2239)]
    [Doc("editor pack types: 0 none")]
    public byte EditorPackTypes { get; init; }
}

[Packet((int)PacketId.JigsawStructureDataPacket)]
[Doc("JigsawStructureData with the four empty lists vanilla sends.")]
public sealed class JigsawStructureDataPacket
{
    [Field(0)]
    [Doc("jigsaw structure rules")]
    public NbtCompound StructureData { get; init; } = new(
        ("processors", new NbtList(NbtKind.Compound)),
        ("template_pools", new NbtList(NbtKind.Compound)),
        ("jigsaws", new NbtList(NbtKind.Compound)),
        ("structure_sets", new NbtList(NbtKind.Compound)));
}

[Packet((int)PacketId.VoxelShapesPacket)]
[Doc("No custom voxel shapes.")]
public sealed class VoxelShapesPacket
{
    [Field(0)]
    [Doc("shapes")]
    public List<SerializableVoxelShape> Shapes { get; init; } = new();

    [Field(1)]
    [Doc("name map")]
    public List<VoxelShapeName> NameMap { get; init; } = new();

    [Field(2, Wire = Wire.UInt16LE)]
    [Doc("custom shape count")]
    public ushort CustomShapeCount { get; init; }
}

[Packet((int)PacketId.StartGamePacket)]
[Doc("""
    A minimal StartGame: empty world, player floating at y=100 in adventure mode. Checked against Mojang's
    docs for 2168-2259; the only wire change in that range is the migration version byte from 2216.
    """)]
public sealed class StartGamePacket
{
    [Field(0)]
    [Doc("entity id")]
    public ActorUniqueID EntityId { get; init; } = new() { Id = 1 };

    [Field(1)]
    [Doc("runtime id")]
    public ActorRuntimeID RuntimeId { get; init; } = new() { Id = 1 };

    [Field(2, Wire = Wire.VarInt32)]
    [Doc("player game type: 2 adventure")]
    public int GameType { get; init; } = 2;

    [Field(3)]
    [Doc("position")]
    public Vec3 Position { get; init; } = new() { Y = 100 };

    [Field(4)]
    [Doc("rotation (pitch, yaw)")]
    public Vec2 Rotation { get; init; } = new();

    [Field(5)]
    [Doc("level settings")]
    public LevelSettings Settings { get; init; } = new();

    [Field(6)]
    [Doc("level id")]
    public string LevelId { get; init; } = "";

    [Field(7)]
    [Variable("worldName")]
    [Doc("level name")]
    public string LevelName { get; init; } = "";

    [Field(8)]
    [Doc("template content identity")]
    public string TemplateContentIdentity { get; init; } = "";

    [Field(9)]
    [Doc("trial")]
    public bool IsTrial { get; init; }

    [Field(10)]
    [Doc("movement settings")]
    public SyncedPlayerMovementSettings MovementSettings { get; init; } = new();

    [Field(11, Wire = Wire.UInt64LE)]
    [Doc("level current time")]
    public ulong LevelCurrentTime { get; init; }

    [Field(12, Wire = Wire.VarInt32)]
    [Doc("enchantment seed")]
    public int EnchantmentSeed { get; init; }

    [Field(13)]
    [Doc("custom block properties")]
    public List<ServerBlockProperty> BlockProperties { get; init; } = new();

    [Field(14)]
    [Variable("uuid.random")]
    [Doc("multiplayer correlation id")]
    public string MultiplayerCorrelationId { get; init; } = "";

    [Field(15)]
    [Doc("server authoritative inventory")]
    public bool EnableItemStackNetManager { get; init; } = true;

    [Field(16)]
    [Doc("server version")]
    public string ServerVersion { get; init; } = "*";

    [Field(17)]
    [Doc("player property data: empty")]
    public NbtCompound PlayerPropertyData { get; init; } = new();

    [Field(18, Wire = Wire.UInt64LE)]
    [Doc("server block type registry checksum")]
    public ulong ServerBlockTypeRegistryChecksum { get; init; }

    [Field(19)]
    [Doc("world template id")]
    public Uuid WorldTemplateId { get; init; } = new();

    [Field(20)]
    [Doc("client side generation")]
    public bool ServerEnabledClientSideGeneration { get; init; }

    [Field(21)]
    [Doc("block network ids are hashes")]
    public bool BlockNetworkIdsAreHashes { get; init; }

    [Field(22)]
    [Doc("network permissions")]
    public NetworkPermissions NetworkPermissions { get; init; } = new();

    [Field(23)]
    [Optional]
    [Doc("server configuration join info: absent")]
    public ServerConfig? ServerConfigurationJoinInfo { get; init; }

    [Field(24)]
    [Doc("server telemetry data")]
    public ServerTelemetryData ServerTelemetryData { get; init; } = new();
}

[Packet((int)PacketId.ItemRegistryPacket)]
[Doc("No items.")]
public sealed class ItemRegistryPacket
{
    [Field(0)]
    [Doc("item data")]
    public List<ItemData> Items { get; init; } = new();
}

[Packet((int)PacketId.ChunkRadiusUpdatedPacket)]
public sealed class ChunkRadiusUpdatedPacket
{
    [Field(0, Wire = Wire.VarInt32)]
    [Variable("radius")]
    [Doc("chunk radius: the client's request, clamped")]
    public int ChunkRadius { get; init; }
}

[Packet((int)PacketId.CreativeContentPacket)]
[Doc("No groups, no items.")]
public sealed class CreativeContentPacket
{
    [Field(0)]
    [Doc("groups")]
    public List<CreativeGroupInfo> Groups { get; init; } = new();

    [Field(1)]
    [Doc("entries")]
    public List<CreativeItemEntry> Entries { get; init; } = new();
}

[Packet((int)PacketId.TransferPacket)]
public sealed class TransferPacket
{
    [Field(0)]
    [Variable("target.host")]
    [Doc("server address")]
    public string ServerAddress { get; init; } = "";

    [Field(1, Wire = Wire.UInt16LE)]
    [Variable("target.port")]
    [Doc("server port")]
    public ushort ServerPort { get; init; }

    [Field(2)]
    [Since(729)]
    [Doc("reload world")]
    public bool ReloadWorld { get; init; }

    [Field(3)]
    [Since(2168)]
    [Optional]
    [Doc("gatherings configuration: absent")]
    public GatheringsConfig? GatheringsConfiguration { get; init; }

    [Field(4)]
    [Since(2250)]
    [Optional]
    [Doc("server transfer settings: absent")]
    public TransferPlayerUIOptions? ServerTransferSettings { get; init; }
}

[Packet((int)PacketId.DisconnectPacket)]
[Doc("Sent instead of Transfer when the destination is offline.")]
public sealed class DisconnectPacket
{
    [Field(0, Wire = Wire.VarInt32)]
    [Since(622)]
    [Doc("reason: 0 unknown")]
    public int Reason { get; init; }

    [Field(1)]
    [Doc("skip message: false, so the messages follow")]
    public bool SkipMessage { get; init; }

    [Field(2)]
    [Doc("messages")]
    public DisconnectPacketMessages Messages { get; init; } = new();
}
