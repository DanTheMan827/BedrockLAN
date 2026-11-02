namespace BedrockLan;

/// <summary>
/// Optional per-server settings for <see cref="BedrockRelay.AddServer(string, RelayServerOptions?)"/>.
/// Values are copied when the server is added; <see cref="DisplayName"/>, <see cref="LevelName"/>,
/// <see cref="TransferHost"/> and <see cref="TransferPort"/> can later be changed on the <see cref="RelayServer"/>.
/// </summary>
public sealed class RelayServerOptions
{
    /// <summary>Server type. Default <see cref="UpstreamKind.Auto"/>: detected by querying the host.</summary>
    public UpstreamKind Kind { get; init; } = UpstreamKind.Auto;

    /// <summary>Overrides the forwarded server name / MOTD line 1.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Overrides the forwarded level name / MOTD line 2.</summary>
    public string? LevelName { get; init; }

    /// <summary>Address put in Transfer packets (defaults to the queried host).</summary>
    public string? TransferHost { get; init; }

    /// <summary>Port put in Transfer packets (defaults to the queried port).</summary>
    public int? TransferPort { get; init; }

    /// <summary>Set false to never advertise this server via RakNet LAN discovery.</summary>
    public bool AdvertiseRakNet { get; init; } = true;

    /// <summary>Set false to never advertise this server via NetherNet LAN discovery.</summary>
    public bool AdvertiseNetherNet { get; init; } = true;

    /// <summary>Fixed local UDP port for this server's minimal RakNet endpoint (otherwise taken from the pool).</summary>
    public int? RakNetLocalPort { get; init; }

    /// <summary>How NetherNet clients are sent to a NetherNet upstream.</summary>
    public NetherNetConnectMode NetherNetMode { get; init; } = NetherNetConnectMode.Auto;

    /// <summary>Explicit <see cref="UpstreamKind.NetherNetHttp"/> only: use https (auto-detection tries both).</summary>
    public bool UseHttps { get; init; }

    /// <summary>Accept invalid TLS certificates when querying status over https (always on for auto-detection).</summary>
    public bool AllowInvalidCertificate { get; init; }
}
