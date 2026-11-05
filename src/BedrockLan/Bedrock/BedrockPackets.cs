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

    /// <summary>
    /// StartGame layouts are version-specific; only protocol 2225 (1.26.60) is encoded here, following
    /// gophertunnel / CloudburstMC v2225. Other protocols fall back to transferring before StartGame.
    /// </summary>
    public static bool SupportsSpawnStage(int protocol) => protocol == 2225;

    /// <summary>Whether this library knows the resource pack packet layouts for the given protocol.</summary>
    public static bool SupportsResourcePackStage(int protocol) => protocol >= 729;
}
