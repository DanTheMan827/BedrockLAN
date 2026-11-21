// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan.Bedrock;

/// <summary>
/// Protocol cut-offs the transport needs. Packet layouts and the conversation itself (including which
/// protocols get the resource pack and spawn stages) are data: see <see cref="ProtocolConfig"/>.
/// </summary>
internal static class BedrockPackets
{
    /// <summary>1.19.30: RequestNetworkSettings/NetworkSettings handshake introduced.</summary>
    public const int ProtocolNetworkSettings = 554;

    /// <summary>
    /// 1.20.60: batches carry a leading compression-algorithm byte. (The session takes the cut-off from its
    /// protocol config's "compressionHeaderSince"; this is the built-in value.)
    /// </summary>
    public const int ProtocolCompressionHeader = 649;
}
