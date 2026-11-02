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
