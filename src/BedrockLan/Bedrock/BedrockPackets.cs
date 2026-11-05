namespace BedrockLan.Bedrock;

/// <summary>
/// Packet ids and protocol cut-offs for the minimal transfer server. The packets themselves are the classes in the
/// Packets folder; version cut-offs follow CloudburstMC/Protocol's per-version serializers.
/// </summary>
internal static class BedrockPackets
{

    /// <summary>1.19.30: RequestNetworkSettings/NetworkSettings handshake introduced.</summary>
    public const int ProtocolNetworkSettings = 554;

    /// <summary>1.20.60: batches carry a leading compression-algorithm byte.</summary>
    public const int ProtocolCompressionHeader = 649;

    /// <summary>First protocol (1.26.60 previews) whose clients need to spawn before a Transfer is accepted.</summary>
    public const int ProtocolSpawnBeforeTransfer = 2207;

    /// <summary>
    /// Newest protocol checked against Mojang's protocol docs. Packets for newer protocols are encoded as for this
    /// one: every version cut-off is a "from this protocol on" check, so the last known layout carries forward.
    /// </summary>
    public const int ProtocolNewestKnown = 2250;

    /// <summary>
    /// Whether to run the temporary-game stage for this protocol: clients from 1.26.60 (2207) on, including ones
    /// newer than <see cref="ProtocolNewestKnown"/>. Older clients accept a Transfer before spawning.
    /// </summary>
    public static bool SupportsSpawnStage(int protocol) => protocol >= ProtocolSpawnBeforeTransfer;

    /// <summary>Whether this library knows the resource pack packet layouts for the given protocol.</summary>
    public static bool SupportsResourcePackStage(int protocol) => protocol >= 729;
}
