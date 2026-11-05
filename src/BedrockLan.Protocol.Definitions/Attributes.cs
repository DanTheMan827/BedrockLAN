namespace BedrockLan.Protocol.Definitions;

/// <summary>Wire encodings, matching the config's primitive types.</summary>
public enum Wire
{
    /// <summary>Inferred from the C# type: bool → Bool, float → FloatLE, string → String, byte → Byte, byte[] → Bytes.</summary>
    Auto,
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

/// <summary>A packet: the class's [Field] properties in order make its body.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PacketAttribute : Attribute
{
    public PacketAttribute(int id)
    {
        Id = id;
    }

    public int Id { get; }
}

/// <summary>A type declared in the config's "types" section (Vec3, LevelSettings, ...): its [Field] properties in order.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProtocolTypeAttribute : Attribute
{
    public ProtocolTypeAttribute(string? name = null)
    {
        Name = name;
    }

    /// <summary>The name in the config (default: the class name).</summary>
    public string? Name { get; }
}

/// <summary>
/// A field, written in <see cref="Order"/>. Integers need <see cref="Wire"/>; for a list of primitives it's the
/// element's encoding.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FieldAttribute : Attribute
{
    public FieldAttribute(int order)
    {
        Order = order;
    }

    public int Order { get; }

    public Wire Wire { get; set; } = Wire.Auto;

    /// <summary>
    /// The field's name in a declared type (default: the property name in camelCase). Two fields may share a name
    /// when their protocol ranges don't overlap, e.g. a field that became optional in a later version.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>Only written from this protocol on (inclusive).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SinceAttribute : Attribute
{
    public SinceAttribute(int protocol)
    {
        Protocol = protocol;
    }

    public int Protocol { get; }
}

/// <summary>Only written before this protocol (exclusive).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UntilAttribute : Attribute
{
    public UntilAttribute(int protocol)
    {
        Protocol = protocol;
    }

    public int Protocol { get; }
}

/// <summary>[OPT] in Mojang's docs: a presence bool, then the value if present (null = absent).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class OptionalAttribute : Attribute
{
}

/// <summary>A list's count encoding (default VarUInt32; UInt32LE for the docs' "No size compression").</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CountAttribute : Attribute
{
    public CountAttribute(Wire wire)
    {
        Wire = wire;
    }

    public Wire Wire { get; }
}

/// <summary>The value comes from a session variable at send time ($target.host, $worldName, ...), not the property.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VariableAttribute : Attribute
{
    public VariableAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>A comment for the generated config (on a packet, a type, or a field).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class DocAttribute : Attribute
{
    public DocAttribute(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
