using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Text;

namespace BedrockLan.Protocol.Definitions;

/// <summary>
/// Serializes a definition instance straight to bytes, reading the same attributes the generator does. Tests use
/// it to check that the config built from the definitions writes exactly what the definitions describe.
/// </summary>
public static class DefinitionSerializer
{
    /// <summary>A packet (id + body) for a protocol, with session variables for [Variable] fields (unset: 0 / "").</summary>
    public static byte[] Serialize(object packet, int protocol, IReadOnlyDictionary<string, object?> variables)
    {
        var w = new MemoryStream();
        VarUInt(w, (uint)Metadata.PacketId(packet.GetType()));
        WriteFields(w, packet, protocol, variables);
        return w.ToArray();
    }

    private static void WriteFields(Stream w, object value, int protocol, IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var field in Metadata.Fields(value.GetType()))
        {
            if (!field.AppliesTo(protocol))
            {
                continue;
            }

            var v = field.Variable is { } name
                ? (name == "protocol" ? protocol : variables.GetValueOrDefault(name))
                : field.Property.GetValue(value);
            if (field.Optional)
            {
                w.WriteByte(v is null ? (byte)0 : (byte)1);
                if (v is null)
                {
                    continue;
                }
            }

            WriteValue(w, field, v, protocol, variables);
        }
    }

    private static void WriteValue(Stream w, FieldMeta field, object? value, int protocol, IReadOnlyDictionary<string, object?> variables)
    {
        switch (field.Kind)
        {
            case FieldKind.Primitive:
                Primitive(w, field.Wire, value);
                break;
            case FieldKind.Nbt:
                w.WriteByte(10);
                String(w, "");
                NbtPayload(w, (NbtTag)value!);
                break;
            case FieldKind.Struct:
                WriteFields(w, value!, protocol, variables);
                break;
            case FieldKind.Array:
                var items = ((IEnumerable?)value)?.Cast<object?>().ToList() ?? new List<object?>();
                Primitive(w, field.Count ?? Wire.VarUInt32, (long)items.Count);
                foreach (var item in items)
                {
                    if (Metadata.IsDeclaredType(field.ElementType!))
                    {
                        WriteFields(w, item!, protocol, variables);
                    }
                    else
                    {
                        Primitive(w, field.Wire, item);
                    }
                }

                break;
        }
    }

    private static void Primitive(Stream w, Wire wire, object? value)
    {
        Span<byte> buffer = stackalloc byte[8];
        switch (wire)
        {
            case Wire.Byte: w.WriteByte(unchecked((byte)Long(value))); break;
            case Wire.Bool: w.WriteByte(Bool(value) ? (byte)1 : (byte)0); break;
            case Wire.Int16LE: BinaryPrimitives.WriteInt16LittleEndian(buffer, unchecked((short)Long(value))); w.Write(buffer[..2]); break;
            case Wire.UInt16LE: BinaryPrimitives.WriteUInt16LittleEndian(buffer, unchecked((ushort)Long(value))); w.Write(buffer[..2]); break;
            case Wire.Int32LE: BinaryPrimitives.WriteInt32LittleEndian(buffer, unchecked((int)Long(value))); w.Write(buffer[..4]); break;
            case Wire.Int32BE: BinaryPrimitives.WriteInt32BigEndian(buffer, unchecked((int)Long(value))); w.Write(buffer[..4]); break;
            case Wire.UInt32LE: BinaryPrimitives.WriteUInt32LittleEndian(buffer, unchecked((uint)Long(value))); w.Write(buffer[..4]); break;
            case Wire.Int64LE: BinaryPrimitives.WriteInt64LittleEndian(buffer, Long(value)); w.Write(buffer); break;
            case Wire.UInt64LE: BinaryPrimitives.WriteUInt64LittleEndian(buffer, unchecked((ulong)Long(value))); w.Write(buffer); break;
            case Wire.FloatLE: BinaryPrimitives.WriteSingleLittleEndian(buffer, (float)Double(value)); w.Write(buffer[..4]); break;
            case Wire.VarInt32: var i = unchecked((int)Long(value)); VarUInt(w, (uint)((i << 1) ^ (i >> 31))); break;
            case Wire.VarUInt32: VarUInt(w, unchecked((uint)Long(value))); break;
            case Wire.VarInt64: var l = Long(value); VarUInt(w, (ulong)((l << 1) ^ (l >> 63))); break;
            case Wire.VarUInt64: VarUInt(w, unchecked((ulong)Long(value))); break;
            case Wire.String: String(w, Text(value)); break;
            case Wire.Bytes: w.Write((byte[]?)value ?? System.Array.Empty<byte>()); break;
            default: throw new InvalidOperationException($"Can't write {wire} here.");
        }
    }

    private static void NbtPayload(Stream w, NbtTag tag)
    {
        Span<byte> buffer = stackalloc byte[4];
        switch (tag)
        {
            case NbtCompound c:
                foreach (var (name, child) in c.Entries)
                {
                    w.WriteByte((byte)child.Kind);
                    String(w, name);
                    NbtPayload(w, child);
                }

                w.WriteByte(0);
                break;
            case NbtList list:
                w.WriteByte((byte)list.Of);
                Primitive(w, Wire.VarInt32, (long)list.Items.Count);
                foreach (var item in list.Items)
                {
                    NbtPayload(w, item);
                }

                break;
            case NbtString s: String(w, s.Value); break;
            case NbtByte b: w.WriteByte(unchecked((byte)b.Value)); break;
            case NbtShort s: Primitive(w, Wire.Int16LE, (long)s.Value); break;
            case NbtInt i: Primitive(w, Wire.VarInt32, (long)i.Value); break;
            case NbtLong l: Primitive(w, Wire.VarInt64, l.Value); break;
            case NbtFloat f: BinaryPrimitives.WriteSingleLittleEndian(buffer, f.Value); w.Write(buffer); break;
        }
    }

    private static void VarUInt(Stream w, ulong value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            w.WriteByte(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
    }

    private static void String(Stream w, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        VarUInt(w, (uint)bytes.Length);
        w.Write(bytes);
    }

    private static long Long(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture),
        ulong u => unchecked((long)u),
        IConvertible c => c.ToInt64(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static double Double(object? value) => value switch
    {
        null => 0,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static bool Bool(object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => Long(value) != 0,
    };

    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
