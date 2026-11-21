// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using BedrockLan.NetherNet;

namespace BedrockLan;

/// <summary>Everything a custom NetherNet signal handler (such as the WebRTC transfer handler) needs.</summary>
/// <param name="Server">The server being joined.</param>
/// <param name="Hub">The NetherNet LAN discovery socket, for sending signaling replies.</param>
/// <param name="Target">Resolves the current transfer destination (null when offline).</param>
/// <param name="TransferOptions">Live transfer settings.</param>
/// <param name="Logger">Log sink.</param>
/// <param name="Transferred">Call after sending a Transfer: client endpoint, target, client protocol.</param>
public sealed record NetherNetHandlerContext(
    RelayServer Server,
    NetherNetLanDiscovery Hub,
    Func<TransferTarget?> Target,
    TransferOptions TransferOptions,
    LanLogger? Logger,
    Action<IPEndPoint, TransferTarget, int>? Transferred = null);
