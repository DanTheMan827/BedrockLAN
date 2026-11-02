using System.Buffers.Binary;
using System.Text;

namespace BedrockLan.Internal;

/// <summary>Thrown when a received packet is truncated or malformed.</summary>
public sealed class PacketFormatException : Exception
{
    public PacketFormatException(string message) : base(message)
    {
    }
}

/// <summary>Bounds-checked reader over a byte array segment.</summary>
internal sealed class ByteReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _position;

    public ByteReader(byte[] data) : this(data, 0, data.Length)
    {
    }

    public ByteReader(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _data = data;
        _position = offset;
        _end = offset + count;
    }

    public int Remaining => _end - _position;

    private void Need(int count)
    {
        if (count < 0 || _end - _position < count)
        {
            throw new PacketFormatException($"Packet truncated: needed {count} byte(s), {Remaining} remaining.");
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        Need(count);
        var span = _data.AsSpan(_position, count);
        _position += count;
        return span;
    }

    public byte Byte()
    {
        Need(1);
        return _data[_position++];
    }

    public bool Bool() => Byte() != 0;

    public byte[] Bytes(int count) => Take(count).ToArray();

    public void Skip(int count) => Take(count);

    public byte[] RemainingBytes() => Take(Remaining).ToArray();

    public ushort UInt16BE() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

    public ushort UInt16LE() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public int Int32BE() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    public int Int32LE() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint UInt32LE() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public long Int64BE() => BinaryPrimitives.ReadInt64BigEndian(Take(8));

    public ulong UInt64LE() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public int UInt24LE()
    {
        var span = Take(3);
        return span[0] | (span[1] << 8) | (span[2] << 16);
    }

    public uint VarUInt32()
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = Byte();
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new PacketFormatException("VarUInt32 is longer than 5 bytes.");
    }

    public int VarInt32()
    {
        var raw = VarUInt32();
        return (int)(raw >> 1) ^ -(int)(raw & 1);
    }

    public string String()
    {
        var length = VarUInt32();
        if (length > (uint)Remaining)
        {
            throw new PacketFormatException($"String length {length} exceeds remaining {Remaining} byte(s).");
        }

        return Encoding.UTF8.GetString(Take((int)length));
    }

    public string ShortStringBE()
    {
        int length = UInt16BE();
        return Encoding.UTF8.GetString(Take(length));
    }
}
