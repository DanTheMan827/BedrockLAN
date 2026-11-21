// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using BedrockLan.Internal;

namespace BedrockLan.NetherNet;

public enum DiscoveryPacketType : ushort
{
    Request = 0,
    Response = 1,
    Message = 2,
}

/// <summary>A decoded NetherNet LAN discovery packet (UDP 7551).</summary>
public sealed record DiscoveryPacket(DiscoveryPacketType Type, ulong SenderId, byte[] Body);

/// <summary>
/// NetherNet LAN discovery framing:
/// <c>HMAC-SHA256(key, payload)[32] || AES-256-ECB-PKCS7(key, payload)</c> where
/// <c>payload = u16le length(incl. itself) | u16le type | u64le sender | 8 zero bytes | body</c>
/// and <c>key = SHA-256(u64le 0xDEADBEEF)</c>.
/// </summary>
public static class DiscoveryPacketCodec
{
    public const int DefaultPort = 7551;

    private static readonly byte[] Key = CreateKey();

    private static byte[] CreateKey()
    {
        var applicationId = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(applicationId, 0xDEADBEEFUL);
        return SHA256.HashData(applicationId);
    }

    public static byte[] Encode(DiscoveryPacketType type, ulong senderId, ReadOnlySpan<byte> body)
    {
        var writer = new ByteWriter(20 + body.Length);
        writer.UInt16LE((ushort)(20 + body.Length));
        writer.UInt16LE((ushort)type);
        writer.UInt64LE(senderId);
        writer.Bytes(stackalloc byte[8]);
        writer.Bytes(body);
        var payload = writer.ToArray();

        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.Key = Key;
            cipher = aes.EncryptEcb(payload, PaddingMode.PKCS7);
        }

        var mac = HMACSHA256.HashData(Key, payload);
        var result = new byte[mac.Length + cipher.Length];
        mac.CopyTo(result, 0);
        cipher.CopyTo(result, mac.Length);
        return result;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out DiscoveryPacket? packet)
    {
        packet = null;
        if (data.Length < 32 + 16 || (data.Length - 32) % 16 != 0)
        {
            return false;
        }

        byte[] payload;
        try
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            payload = aes.DecryptEcb(data[32..], PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var mac = HMACSHA256.HashData(Key, payload);
        if (!CryptographicOperations.FixedTimeEquals(mac, data[..32]))
        {
            return false;
        }

        try
        {
            var reader = new ByteReader(payload);
            reader.UInt16LE(); // length
            var type = (DiscoveryPacketType)reader.UInt16LE();
            var sender = reader.UInt64LE();
            reader.Skip(8);
            packet = new DiscoveryPacket(type, sender, reader.RemainingBytes());
            return true;
        }
        catch (PacketFormatException)
        {
            return false;
        }
    }

    public static byte[] EncodeRequest(ulong senderId) => Encode(DiscoveryPacketType.Request, senderId, ReadOnlySpan<byte>.Empty);

    public static byte[] EncodeResponse(ulong senderId, byte[] applicationData)
    {
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(applicationData).ToLowerInvariant());
        var writer = new ByteWriter(4 + hex.Length);
        writer.UInt32LE((uint)hex.Length);
        writer.Bytes(hex);
        return Encode(DiscoveryPacketType.Response, senderId, writer.WrittenSpan);
    }

    public static byte[] EncodeMessage(ulong senderId, ulong recipientId, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var writer = new ByteWriter(12 + bytes.Length);
        writer.UInt64LE(recipientId);
        writer.UInt32LE((uint)bytes.Length);
        writer.Bytes(bytes);
        return Encode(DiscoveryPacketType.Message, senderId, writer.WrittenSpan);
    }

    public static byte[] DecodeResponseBody(byte[] body)
    {
        var reader = new ByteReader(body);
        var length = reader.UInt32LE();
        if (length > (uint)reader.Remaining)
        {
            throw new PacketFormatException("Response data length exceeds packet.");
        }

        var hex = Encoding.ASCII.GetString(reader.Bytes((int)length));
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new PacketFormatException($"Response data is not hex: {ex.Message}");
        }
    }

    public static (ulong RecipientId, string Data) DecodeMessageBody(byte[] body)
    {
        var reader = new ByteReader(body);
        var recipient = reader.UInt64LE();
        var length = reader.UInt32LE();
        if (length > (uint)reader.Remaining)
        {
            throw new PacketFormatException("Message data length exceeds packet.");
        }

        var data = reader.Bytes((int)length);
        var trailing = reader.RemainingBytes(); // tolerated like the reference implementation
        return (recipient, Encoding.UTF8.GetString(data) + Encoding.UTF8.GetString(trailing));
    }
}
