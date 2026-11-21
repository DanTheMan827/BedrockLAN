// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan.Protocol.Definitions.Types;

// Resource packs, items and voxel shapes (Mojang's bedrock-protocol-docs; layouts as of 2168-2259).

[ProtocolType]
[Doc("A pack's version.")]
public sealed class PackVersion
{
    [Field(0)]
    public string Version { get; init; } = "";
}

[ProtocolType]
[Doc("A pack's UUID and version.")]
public sealed class PackIdVersion
{
    [Field(0)]
    public Uuid PackUUID { get; init; } = new();

    [Field(1)]
    public PackVersion PackVersion { get; init; } = new();
}

[ProtocolType]
[Doc("A pack's content identity.")]
public sealed class ContentIdentity
{
    [Field(0)]
    public string Identity { get; init; } = "";
}

[ProtocolType]
[Doc("A resource pack offered in ResourcePacksInfo.")]
public sealed class PackInfoData
{
    [Field(0)]
    public PackIdVersion PackIdVersion { get; init; } = new();

    [Field(1, Wire = Wire.UInt64LE)]
    public ulong PackSize { get; init; }

    [Field(2)]
    public string ContentKey { get; init; } = "";

    [Field(3)]
    public string SubpackName { get; init; } = "";

    [Field(4)]
    public ContentIdentity ContentIdentity { get; init; } = new();

    [Field(5)]
    public bool HasScripts { get; init; }

    [Field(6)]
    public bool IsAddonPack { get; init; }

    [Field(7)]
    public bool IsRayTracingCapable { get; init; }

    [Field(8)]
    public string CdnUrl { get; init; } = "";
}

[ProtocolType]
[Doc("A pack's CDN address (ResourcePacksInfo before 748).")]
public sealed class PackCdnUrl
{
    [Field(0)]
    public string PackId { get; init; } = "";

    [Field(1)]
    public string CdnUrl { get; init; } = "";
}

[ProtocolType]
[Doc("A pack in ResourcePackStack.")]
public sealed class PackInstanceId
{
    [Field(0)]
    public string PackId { get; init; } = "";

    [Field(1)]
    public string Version { get; init; } = "";

    [Field(2)]
    public string SubPackName { get; init; } = "";
}

[ProtocolType]
[Doc("An item in the item registry.")]
public sealed class ItemData
{
    [Field(0)]
    public string ItemName { get; init; } = "";

    [Field(1, Wire = Wire.Int16LE)]
    public short ItemId { get; init; }

    [Field(2)]
    public bool IsComponentBased { get; init; }

    [Field(3, Wire = Wire.VarInt32)]
    public int ItemVersion { get; init; }

    [Field(4)]
    public NbtCompound ItemComponentData { get; init; } = new();
}

[ProtocolType]
[Doc("NetworkItemInstanceDescriptor: an item stack.")]
public sealed class NetworkItemInstanceDescriptor
{
    [Field(0, Wire = Wire.VarInt32)]
    public int Id { get; init; }

    [Field(1, Wire = Wire.UInt16LE)]
    public ushort StackSize { get; init; }

    [Field(2, Wire = Wire.VarUInt32)]
    public uint AuxValue { get; init; }

    [Field(3, Wire = Wire.VarInt32)]
    public int BlockRuntimeId { get; init; }

    [Field(4)]
    public string UserDataBuffer { get; init; } = "";
}

[ProtocolType]
[Doc("A creative inventory group.")]
public sealed class CreativeGroupInfo
{
    [Field(0, Wire = Wire.Byte)]
    [Doc("CreativeItemCategory")]
    public byte CreativeCategory { get; init; }

    [Field(1)]
    public string Name { get; init; } = "";

    [Field(2)]
    public NetworkItemInstanceDescriptor GroupIconItem { get; init; } = new();
}

[ProtocolType]
[Doc("A creative inventory item.")]
public sealed class CreativeItemEntry
{
    [Field(0, Wire = Wire.VarUInt32)]
    public uint CreativeNetId { get; init; }

    [Field(1)]
    public NetworkItemInstanceDescriptor ItemInstance { get; init; } = new();

    [Field(2, Wire = Wire.VarUInt32)]
    public uint GroupIndex { get; init; }
}

[ProtocolType]
[Doc("VoxelShapes::SerializableCells: a shape's cell grid.")]
public sealed class SerializableCells
{
    [Field(0, Wire = Wire.Byte)]
    public byte XSize { get; init; }

    [Field(1, Wire = Wire.Byte)]
    public byte YSize { get; init; }

    [Field(2, Wire = Wire.Byte)]
    public byte ZSize { get; init; }

    [Field(3, Wire = Wire.Byte)]
    public List<byte> Storage { get; init; } = new();
}

[ProtocolType]
[Doc("VoxelShapes::SerializableVoxelShape: a custom collision / selection shape.")]
public sealed class SerializableVoxelShape
{
    [Field(0)]
    public SerializableCells Cells { get; init; } = new();

    [Field(1)]
    public List<float> XCoordinates { get; init; } = new();

    [Field(2)]
    public List<float> YCoordinates { get; init; } = new();

    [Field(3)]
    public List<float> ZCoordinates { get; init; } = new();
}

[ProtocolType]
[Doc("A voxel shape name and its registry handle.")]
public sealed class VoxelShapeName
{
    [Field(0)]
    [Doc("hashed_string")]
    public string Key { get; init; } = "";

    [Field(1, Wire = Wire.UInt16LE)]
    [Doc("VoxelShapes::RegistryHandle")]
    public ushort Value { get; init; }
}
