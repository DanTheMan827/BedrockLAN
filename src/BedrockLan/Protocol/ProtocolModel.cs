// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan.Protocol;

// The parsed form of a protocol config (see default-protocol.jsonc for the format). Immutable once parsed.

/// <summary>Wire types a packet field can be written as.</summary>
internal enum FieldType
{
    Byte,
    Bool,
    Int16LE,
    UInt16LE,
    Int32LE,
    Int32BE,
    UInt32LE,
    Int64LE,
    UInt64LE,
    FloatLE,
    VarInt32,
    VarUInt32,
    VarInt64,
    VarUInt64,
    String,
    Bytes,
    NetworkNbt,
}

/// <summary>A scalar value: a literal (long, double, bool, string, byte[] or <see cref="NbtRoot"/>) or a variable.</summary>
internal sealed record ValueSpec(object? Literal, string? Variable)
{
    public static ValueSpec Of(object? literal) => new(literal, null);

    public static ValueSpec Var(string name) => new(null, name);
}

// ---- Types: wire primitives, and what a config builds from them ----

/// <summary>A field's type: a wire primitive, a type declared in the config's "types", or Array&lt;T&gt;.</summary>
internal abstract record TypeRef
{
    public abstract string Name { get; }
}

internal sealed record PrimitiveType(FieldType Kind) : TypeRef
{
    public override string Name => Kind.ToString();
}

/// <summary>A type declared in the config ("types"): an ordered list of named fields.</summary>
internal sealed record StructType(StructSpec Spec) : TypeRef
{
    public override string Name => Spec.Name;
}

/// <summary>A count, then that many elements. The count's own type defaults to VarUInt32.</summary>
internal sealed record ArrayType(TypeRef Element, FieldType CountType) : TypeRef
{
    public override string Name => $"Array<{Element.Name}>";
}

/// <summary>A declared type. Its fields are filled in once every type name is known (types can refer to each other).</summary>
internal sealed class StructSpec
{
    public StructSpec(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<StructFieldSpec> Fields { get; } = new();
}

/// <summary>One field of a declared type; like a packet field, it can depend on the protocol and be optional.</summary>
internal sealed record StructFieldSpec(string Name, TypeRef Type, ProtocolRange Range, bool Optional);

/// <summary>
/// A declared type's value: one node per field of its <see cref="StructSpec"/>, in the same order. Fields that share a
/// name (versioned variants with disjoint ranges) all take the value given for that name.
/// </summary>

/// <summary>A value as written in a packet body: scalar, a declared type's fields, an array's items, or absent.</summary>
internal abstract record ValueNode;

internal sealed record ScalarValue(ValueSpec Value) : ValueNode;

internal sealed record StructValue(IReadOnlyList<ValueNode> Fields) : ValueNode;

internal sealed record ArrayValue(IReadOnlyList<ValueNode> Items) : ValueNode;

/// <summary>An optional field that's absent (written as a false presence flag), or a field left out of a value.</summary>
internal sealed record AbsentValue : ValueNode
{
    public static readonly AbsentValue Instance = new();
}

/// <summary>Protocol range a field or read applies to: since is inclusive, until exclusive.</summary>
internal readonly record struct ProtocolRange(int? Since, int? Until)
{
    public bool Contains(int protocol) => (Since is null || protocol >= Since) && (Until is null || protocol < Until);

    /// <summary>Whether some protocol is in both ranges.</summary>
    public bool Overlaps(ProtocolRange other) =>
        Math.Max(Since ?? int.MinValue, other.Since ?? int.MinValue) < Math.Min(Until ?? int.MaxValue, other.Until ?? int.MaxValue);
}

/// <summary>A packet body field. Optional fields ([OPT] in Mojang's docs) are a presence bool, then the value if present.</summary>
internal sealed record FieldSpec(TypeRef Type, ValueNode Value, ProtocolRange Range, bool Optional);

/// <summary>
/// One value read from a client packet: stored in <see cref="Variable"/> (optionally clamped, or only if unset),
/// or compared with <see cref="Match"/> (the packet is ignored when it isn't one of them).
/// </summary>
internal sealed record ReadSpec(
    FieldType Type,
    string? Variable,
    IReadOnlyList<object>? Match,
    bool Keep,
    long? ClampMin,
    long? ClampMax,
    ProtocolRange Range);

internal sealed record PacketSpec(string Name, int Id, IReadOnlyList<FieldSpec> Body);

/// <summary>What a condition can look at.</summary>
internal sealed record ConditionContext(string Timing, int Protocol, Func<bool> TargetOnline, Func<string, object?> Variable);

internal abstract record Condition
{
    public abstract bool Evaluate(ConditionContext context);
}

internal sealed record TimingCondition(IReadOnlySet<string> Timings) : Condition
{
    public override bool Evaluate(ConditionContext context) => Timings.Contains(context.Timing);
}

internal sealed record ProtocolCondition(int? Min, int? Max) : Condition
{
    public override bool Evaluate(ConditionContext context) =>
        (Min is null || context.Protocol >= Min) && (Max is null || context.Protocol <= Max);
}

internal sealed record TargetOnlineCondition(bool Online) : Condition
{
    public override bool Evaluate(ConditionContext context) => context.TargetOnline() == Online;
}

/// <summary>A variable's current value is one of <see cref="Values"/> (numbers compare as integers).</summary>
internal sealed record VariableCondition(string Name, IReadOnlyList<object> Values) : Condition
{
    public override bool Evaluate(ConditionContext context)
    {
        var value = context.Variable(Name);
        return Values.Any(v => Same(v, value));
    }

    internal static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (bool a, bool b) => a == b,
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (null, _) or (_, null) => false,
        (bool, _) or (_, bool) or (string, _) or (_, string) => false,
        (IConvertible a, IConvertible b) => a.ToInt64(System.Globalization.CultureInfo.InvariantCulture) == b.ToInt64(System.Globalization.CultureInfo.InvariantCulture),
        _ => false,
    };
}

internal sealed record AllCondition(IReadOnlyList<Condition> Conditions) : Condition
{
    public override bool Evaluate(ConditionContext context) => Conditions.All(c => c.Evaluate(context));
}

internal sealed record AnyCondition(IReadOnlyList<Condition> Conditions) : Condition
{
    public override bool Evaluate(ConditionContext context) => Conditions.Any(c => c.Evaluate(context));
}

internal sealed record NotCondition(Condition Condition) : Condition
{
    public override bool Evaluate(ConditionContext context) => !Condition.Evaluate(context);
}

internal enum StepEnding
{
    None,
    Goto,
    Transfer,
    Close,
}

internal sealed record StepSpec(Condition? If, string? Log, IReadOnlyList<string> Send, bool Compression, StepEnding Ending, string? Goto);

internal sealed record TimeoutSpec(ValueSpec Seconds, IReadOnlyList<StepSpec> Steps);

internal sealed record StageSpec(string Name, int ExpectPacket, IReadOnlyList<ReadSpec> Read, IReadOnlyList<StepSpec> Steps, TimeoutSpec? Timeout);

internal sealed record SessionSpec(
    string Start,
    string TransferPacket,
    string OfflinePacket,
    int CompressionHeaderSince,
    IReadOnlyDictionary<string, StageSpec> Stages);

// ---- Network little-endian NBT (as used inside packets) ----

internal abstract record NbtTag(byte TypeId);

internal sealed record NbtByte(sbyte Value) : NbtTag(1);

internal sealed record NbtShort(short Value) : NbtTag(2);

internal sealed record NbtInt(int Value) : NbtTag(3);

internal sealed record NbtLong(long Value) : NbtTag(4);

internal sealed record NbtFloat(float Value) : NbtTag(5);

internal sealed record NbtString(string Value) : NbtTag(8);

internal sealed record NbtList(byte ElementType, IReadOnlyList<NbtTag> Items) : NbtTag(9);

internal sealed record NbtCompound(IReadOnlyList<KeyValuePair<string, NbtTag>> Entries) : NbtTag(10);

/// <summary>A root tag: always a named compound.</summary>
internal sealed record NbtRoot(string Name, NbtCompound Compound);
