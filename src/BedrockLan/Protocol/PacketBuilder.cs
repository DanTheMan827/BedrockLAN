using System.Globalization;
using BedrockLan.Internal;

namespace BedrockLan.Protocol;

/// <summary>Writes a configured packet: its id, then each body field that applies to the protocol.</summary>
internal static class PacketBuilder
{
    /// <param name="packet">The packet.</param>
    /// <param name="protocol">The client's protocol (picks the fields by their since/until, at every level).</param>
    /// <param name="resolve">Value of a variable (null when unset: written as 0, false or "").</param>
    public static byte[] Build(PacketSpec packet, int protocol, Func<string, object?> resolve)
    {
        var w = new ByteWriter(64);
        w.VarUInt32((uint)packet.Id);
        foreach (var field in packet.Body)
        {
            if (field.Range.Contains(protocol))
            {
                WriteField(w, field.Type, field.Value, field.Optional, protocol, resolve);
            }
        }

        return w.ToArray();
    }

    /// <summary>An optional field is a presence bool, then the value only if present.</summary>
    private static void WriteField(ByteWriter w, TypeRef type, ValueNode value, bool optional, int protocol, Func<string, object?> resolve)
    {
        if (optional)
        {
            var present = value is not AbsentValue;
            w.Bool(present);
            if (!present)
            {
                return;
            }
        }

        WriteValue(w, type, value, protocol, resolve);
    }

    private static void WriteValue(ByteWriter w, TypeRef type, ValueNode value, int protocol, Func<string, object?> resolve)
    {
        switch (type)
        {
            case PrimitiveType p:
                var scalar = (value as ScalarValue)?.Value;
                Write(w, p.Kind, scalar?.Variable is { } name ? resolve(name) : scalar?.Literal);
                break;

            case StructType s:
                var fields = (value as StructValue)?.Fields;
                for (var i = 0; i < s.Spec.Fields.Count; i++)
                {
                    var field = s.Spec.Fields[i];
                    if (field.Range.Contains(protocol))
                    {
                        var child = fields is not null && i < fields.Count ? fields[i] : AbsentValue.Instance;
                        WriteField(w, field.Type, child, field.Optional, protocol, resolve);
                    }
                }

                break;

            case ArrayType array:
                var items = (value as ArrayValue)?.Items ?? System.Array.Empty<ValueNode>();
                Write(w, array.CountType, (long)items.Count);
                foreach (var item in items)
                {
                    WriteValue(w, array.Element, item, protocol, resolve);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    private static void Write(ByteWriter w, FieldType type, object? value)
    {
        switch (type)
        {
            case FieldType.Byte: w.Byte(unchecked((byte)Long(value))); break;
            case FieldType.Bool: w.Bool(Bool(value)); break;
            case FieldType.Int16LE: w.Int16LE(unchecked((short)Long(value))); break;
            case FieldType.UInt16LE: w.UInt16LE(unchecked((ushort)Long(value))); break;
            case FieldType.Int32LE: w.Int32LE(unchecked((int)Long(value))); break;
            case FieldType.Int32BE: w.Int32BE(unchecked((int)Long(value))); break;
            case FieldType.UInt32LE: w.UInt32LE(unchecked((uint)Long(value))); break;
            case FieldType.Int64LE: w.Int64LE(Long(value)); break;
            case FieldType.UInt64LE: w.UInt64LE(unchecked((ulong)Long(value))); break;
            case FieldType.FloatLE: w.FloatLE((float)Double(value)); break;
            case FieldType.VarInt32: w.VarInt32(unchecked((int)Long(value))); break;
            case FieldType.VarUInt32: w.VarUInt32(unchecked((uint)Long(value))); break;
            case FieldType.VarInt64: w.VarInt64(Long(value)); break;
            case FieldType.VarUInt64: w.VarUInt64(unchecked((ulong)Long(value))); break;
            case FieldType.String: w.String(Text(value)); break;
            case FieldType.Bytes: w.Bytes((byte[]?)value ?? System.Array.Empty<byte>()); break;
            case FieldType.NetworkNbt: WriteNbt(w, (NbtRoot)value!); break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    // ---- Network little-endian NBT ----

    private static void WriteNbt(ByteWriter w, NbtRoot root)
    {
        w.Byte(10);
        w.String(root.Name);
        WritePayload(w, root.Compound);
    }

    private static void WritePayload(ByteWriter w, NbtTag tag)
    {
        switch (tag)
        {
            case NbtByte b: w.Byte(unchecked((byte)b.Value)); break;
            case NbtShort s: w.Int16LE(s.Value); break;
            case NbtInt i: w.VarInt32(i.Value); break;
            case NbtLong l: w.VarInt64(l.Value); break;
            case NbtFloat f: w.FloatLE(f.Value); break;
            case NbtString s: w.String(s.Value); break;
            case NbtList list:
                w.Byte(list.ElementType);
                w.VarInt32(list.Items.Count);
                foreach (var item in list.Items)
                {
                    WritePayload(w, item);
                }

                break;
            case NbtCompound compound:
                foreach (var (name, child) in compound.Entries)
                {
                    w.Byte(child.TypeId);
                    w.String(name);
                    WritePayload(w, child);
                }

                w.Byte(0); // end
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tag), tag, null);
        }
    }

    // ---- Value conversion (literals are long, double, bool or string; variables can be any of those) ----

    private static long Long(object? value) => value switch
    {
        null => 0,
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        double d => (long)d,
        string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0,
        IConvertible c => c.ToInt64(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static double Double(object? value) => value switch
    {
        null => 0,
        double d => d,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static bool Bool(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0 && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
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
