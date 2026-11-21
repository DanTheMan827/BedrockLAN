// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan.Protocol.Definitions.Types;

// Small shared types from Mojang's bedrock-protocol-docs.

[ProtocolType]
[Doc("Vec2: two floats.")]
public sealed class Vec2
{
    [Field(0)]
    public float X { get; init; }

    [Field(1)]
    public float Y { get; init; }
}

[ProtocolType]
[Doc("Vec3: three floats.")]
public sealed class Vec3
{
    [Field(0)]
    public float X { get; init; }

    [Field(1)]
    public float Y { get; init; }

    [Field(2)]
    public float Z { get; init; }
}

[ProtocolType]
[Doc("A block position as StartGame's level settings use it: three zig-zag varints.")]
public sealed class BlockPos
{
    [Field(0, Wire = Wire.VarInt32)]
    public int X { get; init; }

    [Field(1, Wire = Wire.VarInt32)]
    public int Y { get; init; }

    [Field(2, Wire = Wire.VarInt32)]
    public int Z { get; init; }
}

[ProtocolType("UUID")]
[Doc("mce::UUID: the most significant 64 bits, then the least significant.")]
public sealed class Uuid
{
    [Field(0, Wire = Wire.UInt64LE)]
    public ulong MostSignificantBits { get; init; }

    [Field(1, Wire = Wire.UInt64LE)]
    public ulong LeastSignificantBits { get; init; }
}

[ProtocolType]
[Doc("ActorUniqueID: an entity's unique id.")]
public sealed class ActorUniqueID
{
    [Field(0, Wire = Wire.VarInt64)]
    public long Id { get; init; }
}

[ProtocolType]
[Doc("ActorRuntimeID: an entity's runtime id.")]
public sealed class ActorRuntimeID
{
    [Field(0, Wire = Wire.VarUInt64)]
    public ulong Id { get; init; }
}

[ProtocolType]
[Doc("One experiment and whether it's on.")]
public sealed class ExperimentToggle
{
    [Field(0)]
    public string Name { get; init; } = "";

    [Field(1)]
    public bool Enabled { get; init; }
}

[ProtocolType]
[Doc("Experiments: the toggles (count without size compression) and whether any was ever toggled.")]
public sealed class Experiments
{
    [Field(0)]
    [Count(Wire.UInt32LE)]
    public List<ExperimentToggle> Toggles { get; init; } = new();

    [Field(1)]
    public bool ExperimentsEverToggled { get; init; }
}
