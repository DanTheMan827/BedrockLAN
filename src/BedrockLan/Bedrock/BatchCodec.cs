// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO.Compression;
using BedrockLan.Internal;

namespace BedrockLan.Bedrock;

/// <summary>Bedrock packet batches: a sequence of varuint32-length-prefixed packets, optionally DEFLATE compressed.</summary>
internal static class BatchCodec
{
    public const int MaxDecompressedSize = 8 * 1024 * 1024;

    public static List<byte[]> Split(byte[] payload)
    {
        var reader = new ByteReader(payload);
        var packets = new List<byte[]>();
        while (reader.Remaining > 0)
        {
            var length = reader.VarUInt32();
            if (length > (uint)reader.Remaining)
            {
                throw new PacketFormatException($"Batch entry length {length} exceeds remaining {reader.Remaining} byte(s).");
            }

            packets.Add(reader.Bytes((int)length));
        }

        return packets;
    }

    public static byte[] Join(params byte[][] packets)
    {
        var writer = new ByteWriter();
        foreach (var packet in packets)
        {
            writer.VarUInt32((uint)packet.Length);
            writer.Bytes(packet);
        }

        return writer.ToArray();
    }

    /// <summary>Raw DEFLATE (no zlib header), which is what Bedrock calls "zlib"/flate compression.</summary>
    public static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    public static byte[] Inflate(ReadOnlySpan<byte> data, int maxSize = MaxDecompressedSize)
    {
        using var input = new MemoryStream(data.ToArray());
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maxSize)
            {
                throw new PacketFormatException($"Decompressed batch exceeds {maxSize} bytes.");
            }
        }

        return output.ToArray();
    }
}
