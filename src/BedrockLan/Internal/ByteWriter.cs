// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.Text;

namespace BedrockLan.Internal;

/// <summary>Growable little helper for writing binary protocol data.</summary>
internal sealed class ByteWriter
{
    private byte[] _buffer;
    private int _length;

    public ByteWriter(int capacity = 256)
    {
        _buffer = new byte[Math.Max(16, capacity)];
    }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    private Span<byte> Reserve(int count)
    {
        if (_length + count > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        }

        var span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }

    public void Byte(byte value) => Reserve(1)[0] = value;

    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public void Bytes(ReadOnlySpan<byte> value) => value.CopyTo(Reserve(value.Length));

    public void UInt16BE(ushort value) => BinaryPrimitives.WriteUInt16BigEndian(Reserve(2), value);

    public void UInt16LE(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);

    public void Int32BE(int value) => BinaryPrimitives.WriteInt32BigEndian(Reserve(4), value);

    public void Int32LE(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

    public void UInt32LE(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);

    public void Int64BE(long value) => BinaryPrimitives.WriteInt64BigEndian(Reserve(8), value);

    public void Int64LE(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);

    public void Int16LE(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);

    public void UInt64LE(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);

    public void FloatLE(float value) => BinaryPrimitives.WriteSingleLittleEndian(Reserve(4), value);

    public void UInt24LE(int value)
    {
        var span = Reserve(3);
        span[0] = (byte)value;
        span[1] = (byte)(value >> 8);
        span[2] = (byte)(value >> 16);
    }

    public void VarUInt32(uint value)
    {
        while (value >= 0x80)
        {
            Byte((byte)(value | 0x80));
            value >>= 7;
        }

        Byte((byte)value);
    }

    public void VarUInt64(ulong value)
    {
        while (value >= 0x80)
        {
            Byte((byte)(value | 0x80));
            value >>= 7;
        }

        Byte((byte)value);
    }

    public void VarInt64(long value) => VarUInt64((ulong)((value << 1) ^ (value >> 63)));

    /// <summary>ZigZag-encoded signed varint.</summary>
    public void VarInt32(int value) => VarUInt32((uint)((value << 1) ^ (value >> 31)));

    /// <summary>Bedrock string: varuint32 byte length followed by UTF-8 bytes.</summary>
    public void String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        VarUInt32((uint)bytes.Length);
        Bytes(bytes);
    }

    /// <summary>RakNet string: big-endian uint16 byte length followed by UTF-8 bytes.</summary>
    public void ShortStringBE(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("String is too long for a uint16 length prefix.", nameof(value));
        }

        UInt16BE((ushort)bytes.Length);
        Bytes(bytes);
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}
