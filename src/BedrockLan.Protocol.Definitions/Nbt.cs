namespace BedrockLan.Protocol.Definitions;

/// <summary>NBT tag types (network little-endian NBT, as used inside packets).</summary>
public enum NbtKind : byte
{
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    String = 8,
    List = 9,
    Compound = 10,
}

/// <summary>An NBT tag in a definition (a NetworkNbt field's value is a root <see cref="NbtCompound"/>).</summary>
public abstract class NbtTag
{
    public abstract NbtKind Kind { get; }
}

public sealed class NbtCompound : NbtTag
{
    public NbtCompound(params (string Name, NbtTag Tag)[] entries)
    {
        Entries = entries;
    }

    public override NbtKind Kind => NbtKind.Compound;

    public IReadOnlyList<(string Name, NbtTag Tag)> Entries { get; }
}

public sealed class NbtList : NbtTag
{
    public NbtList(NbtKind of, params NbtTag[] items)
    {
        Of = of;
        Items = items;
    }

    public override NbtKind Kind => NbtKind.List;

    public NbtKind Of { get; }

    public IReadOnlyList<NbtTag> Items { get; }
}

public sealed class NbtString(string value) : NbtTag
{
    public override NbtKind Kind => NbtKind.String;

    public string Value { get; } = value;
}

public sealed class NbtByte(sbyte value) : NbtTag
{
    public override NbtKind Kind => NbtKind.Byte;

    public sbyte Value { get; } = value;
}

public sealed class NbtShort(short value) : NbtTag
{
    public override NbtKind Kind => NbtKind.Short;

    public short Value { get; } = value;
}

public sealed class NbtInt(int value) : NbtTag
{
    public override NbtKind Kind => NbtKind.Int;

    public int Value { get; } = value;
}

public sealed class NbtLong(long value) : NbtTag
{
    public override NbtKind Kind => NbtKind.Long;

    public long Value { get; } = value;
}

public sealed class NbtFloat(float value) : NbtTag
{
    public override NbtKind Kind => NbtKind.Float;

    public float Value { get; } = value;
}
