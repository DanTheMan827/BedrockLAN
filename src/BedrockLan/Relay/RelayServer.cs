using BedrockLan.Internal;
using BedrockLan.NetherNet;
using BedrockLan.RakNet;
using System.Net;

namespace BedrockLan;

/// <summary>
/// One server advertised by a <see cref="BedrockRelay"/>. Returned by <see cref="BedrockRelay.AddServer(string, RelayServerOptions?)"/>.
/// Disposing it stops advertising it and removes it from the relay.
/// <para>
/// Thread-safe: getters return consistent, immutable values (<see cref="Status"/> is replaced as a whole on
/// every successful query); the settable overrides may be changed from any thread and apply to the next
/// LAN ping or transfer. Events are raised in order on the relay's event thread.
/// </para>
/// </summary>
public sealed class RelayServer : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly string _nonce = Guid.NewGuid().ToString();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);

    private ServerStatus? _status;
    private long _lastSuccessTicks;
    private long _lastAttemptTicks;
    private int _detectedKind = -1;
    private string? _lastError;
    private string? _displayName;
    private string? _levelName;
    private string? _namePrefix;
    private string? _transferHost;
    private int _transferPort;
    private long _transferCount;
    private int _disposed;
    private bool _reportedOnline;

    private HttpSignalingClient? _httpClient;
    private HttpSignalingClient? _plainHttpClient;
    private HttpSignalingClient? _secureHttpClient;
    private UpstreamEndPoint? _lanUpstream;
    private RakNetTransferServer? _rakNetServer;

    internal RelayServer(BedrockRelay relay, string address, string host, int? port, RelayServerOptions options)
    {
        Relay = relay;
        Address = address;
        Host = host;
        ConfiguredKind = options.Kind;
        Port = port ?? (options.Kind == UpstreamKind.NetherNetLan ? DiscoveryPacketCodec.DefaultPort : 19132);
        PortSpecified = port is not null;
        Options = options;
        _displayName = options.DisplayName;
        _levelName = options.LevelName;
        _namePrefix = options.NamePrefix;
        _transferHost = options.TransferHost;
        _transferPort = options.TransferPort ?? 0;
        NetherNetId = UdpSockets.RandomId();
        AddedAt = DateTime.UtcNow;
        if (options.Kind == UpstreamKind.NetherNetHttp)
        {
            _httpClient = GetHttpClient(options.UseHttps);
        }
    }

    // ---- Identity ---------------------------------------------------------------------------

    /// <summary>The relay this server belongs to.</summary>
    public BedrockRelay Relay
    {
        get;
    }

    /// <summary>The address as passed to <see cref="BedrockRelay.AddServer(string, RelayServerOptions?)"/>.</summary>
    public string Address
    {
        get;
    }

    /// <summary>Upstream host name or IP.</summary>
    public string Host
    {
        get;
    }

    /// <summary>Upstream port (19132 unless given; 7551 for an explicit NetherNet LAN host).</summary>
    public int Port
    {
        get;
    }

    /// <summary>The kind requested when adding (usually <see cref="UpstreamKind.Auto"/>).</summary>
    public UpstreamKind ConfiguredKind
    {
        get;
    }

    /// <summary>The kind in use: configured, or detected for Auto (null until detected).</summary>
    public UpstreamKind? Kind
    {
        get
        {
            if (ConfiguredKind != UpstreamKind.Auto)
            {
                return ConfiguredKind;
            }

            var detected = Volatile.Read(ref _detectedKind);
            return detected < 0 ? null : (UpstreamKind)detected;
        }
    }

    /// <summary>When the server was added (UTC).</summary>
    public DateTime AddedAt
    {
        get;
    }

    /// <summary>Network id this server is advertised under on NetherNet LAN discovery.</summary>
    public ulong NetherNetId
    {
        get;
    }

    // ---- Live overrides -------------------------------------------------------------------------

    /// <summary>Name shown in the LAN list instead of the upstream's. Null = forward the upstream's.</summary>
    public string? DisplayName
    {
        get => Volatile.Read(ref _displayName);
        set => Volatile.Write(ref _displayName, value);
    }

    /// <summary>
    /// Text put in front of the advertised name (after formatting codes are stripped from the name, so it may hold
    /// characters that stripping would remove, such as control characters). Null or empty = no prefix.
    /// </summary>
    public string? NamePrefix
    {
        get => Volatile.Read(ref _namePrefix);
        set => Volatile.Write(ref _namePrefix, value);
    }

    /// <summary>Level/sub-MOTD shown instead of the upstream's. Null = forward the upstream's.</summary>
    public string? LevelName
    {
        get => Volatile.Read(ref _levelName);
        set => Volatile.Write(ref _levelName, value);
    }

    /// <summary>Host used in Transfer packets. Null = <see cref="Host"/>.</summary>
    public string? TransferHost
    {
        get => Volatile.Read(ref _transferHost);
        set => Volatile.Write(ref _transferHost, value);
    }

    /// <summary>Port used in Transfer packets. Null = <see cref="Port"/>.</summary>
    public int? TransferPort
    {
        get
        {
            var port = Volatile.Read(ref _transferPort);
            return port == 0 ? null : port;
        }
        set
        {
            if (value is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Port must be 1-65535.");
            }

            Volatile.Write(ref _transferPort, value ?? 0);
        }
    }

    // ---- Status ---------------------------------------------------------------------------------

    /// <summary>Latest successfully queried status (immutable snapshot; may be stale, see <see cref="LastUpdated"/>).</summary>
    public ServerStatus? Status => Volatile.Read(ref _status);

    /// <summary>The name currently advertised: <see cref="DisplayName"/>, else the upstream's, else the address.</summary>
    public string Name => DisplayName ?? NonEmpty(Status?.Name) ?? Address;

    /// <summary>The level/sub-MOTD currently advertised.</summary>
    public string Level => LevelName ?? Status?.Level ?? "";

    public int Players => Status?.Players ?? 0;

    public int MaxPlayers => Status?.MaxPlayers ?? 0;

    public string Version => Status?.Version ?? "";

    public int Protocol => Status?.Protocol ?? 0;

    /// <summary>True if the server answered within <see cref="BedrockRelayOptions.OfflineAfter"/>.</summary>
    public bool IsOnline => LastUpdated is { } t && DateTime.UtcNow - t < Relay.Options.OfflineAfter;

    /// <summary>Time of the last successful query (UTC).</summary>
    public DateTime? LastUpdated => TicksToTime(Interlocked.Read(ref _lastSuccessTicks));

    /// <summary>Time of the last query attempt (UTC).</summary>
    public DateTime? LastQueried => TicksToTime(Interlocked.Read(ref _lastAttemptTicks));

    /// <summary>Why the last query failed, if it did.</summary>
    public string? LastError => Volatile.Read(ref _lastError);

    /// <summary>Destination clients are transferred to.</summary>
    public TransferTarget TransferTarget => new(TransferHost ?? Host, (ushort)(TransferPort ?? Port));

    /// <summary>Number of clients sent on to this server (transfers and relayed joins).</summary>
    public long TransferCount => Interlocked.Read(ref _transferCount);

    /// <summary>Local UDP port of this server's minimal RakNet endpoint, if it has one.</summary>
    public int? RakNetPort => Volatile.Read(ref _rakNetServer)?.Port;

    /// <summary>True if RakNet LAN pings for this server are currently answered.</summary>
    public bool IsAdvertisedOnRakNet => RakNetPort is not null && Relay.AdvertisesRakNet(this);

    /// <summary>True if NetherNet LAN discovery requests for this server are currently answered.</summary>
    public bool IsAdvertisedOnNetherNet => Relay.NetherNetActive && Relay.AdvertisesNetherNet(this);

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    // ---- Events ---------------------------------------------------------------------------------

    /// <summary>A LAN client was sent on to this server.</summary>
    public event EventHandler<TransferEventArgs>? Transferred;

    /// <summary>Name, level, players, version... changed (also raised on the first successful query).</summary>
    public event EventHandler<ServerStatusChangedEventArgs>? StatusChanged;

    /// <summary>The server came online or went offline.</summary>
    public event EventHandler<OnlineChangedEventArgs>? OnlineChanged;

    /// <summary>The server type was detected (Auto only; raised again after a re-detection).</summary>
    public event EventHandler<KindDetectedEventArgs>? KindDetected;

    /// <summary>The server was removed from the relay (disposed, or the relay was disposed).</summary>
    public event EventHandler<RelayServerEventArgs>? Removed;

    // ---- Methods --------------------------------------------------------------------------------

    /// <summary>Queries the upstream now (waits for an in-progress query instead of starting a second one).</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Relay.PollAsync(this, wait: true, cancellationToken);

    /// <summary>Stops advertising this server and removes it from the relay.</summary>
    public ValueTask DisposeAsync() => Relay.RemoveAsync(this);

    /// <inheritdoc cref="DisposeAsync"/>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public override string ToString() => $"{Name} ({Address})";

    // ---- Internals used by the relay ------------------------------------------------------------

    internal RelayServerOptions Options
    {
        get;
    }

    internal bool PortSpecified
    {
        get;
    }

    internal CancellationToken Lifetime => _lifetime.Token;

    internal SemaphoreSlim PollGate => _pollGate;

    internal RakNetTransferServer? RakNetServer
    {
        get => Volatile.Read(ref _rakNetServer);
        set => Volatile.Write(ref _rakNetServer, value);
    }

    internal HttpSignalingClient? HttpClient => Volatile.Read(ref _httpClient);

    /// <summary>NetherNet advertisement handler; guarded by the relay's lock.</summary>
    internal INetherNetSignalHandler? SignalHandler
    {
        get; set;
    }

    internal (ulong NetworkId, IPEndPoint EndPoint)? LanUpstream
    {
        get
        {
            var upstream = Volatile.Read(ref _lanUpstream);
            return upstream is null ? null : (upstream.NetworkId, upstream.EndPoint);
        }
        set => Volatile.Write(ref _lanUpstream, value is { } v ? new UpstreamEndPoint(v.NetworkId, v.EndPoint) : null);
    }

    /// <summary>Marks the server disposed; returns false if it already was.</summary>
    internal bool TryMarkDisposed()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return false;
        }

        _lifetime.Cancel();
        return true;
    }

    internal void ReleaseResources()
    {
        lock (_sync)
        {
            _plainHttpClient?.Dispose();
            _secureHttpClient?.Dispose();
            _plainHttpClient = null;
            _secureHttpClient = null;
        }

        // _lifetime is intentionally not disposed: in-flight queries may still hold its token.
    }

    /// <summary>One cached client per scheme, so re-detection never disposes a client mid-request.</summary>
    internal HttpSignalingClient GetHttpClient(bool https)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ref var slot = ref https ? ref _secureHttpClient : ref _plainHttpClient;
            return slot ??= new HttpSignalingClient(
                HttpSignalingClient.BuildBaseUri(Host, Port, https),
                allowInvalidCertificate: Options.AllowInvalidCertificate || ConfiguredKind == UpstreamKind.Auto);
        }
    }

    internal void ApplyDetection(UpstreamKind kind, HttpSignalingClient? http)
    {
        if (http is not null)
        {
            Volatile.Write(ref _httpClient, http);
        }

        Volatile.Write(ref _detectedKind, (int)kind);
    }

    internal void ResetDetection()
    {
        if (ConfiguredKind == UpstreamKind.Auto)
        {
            Volatile.Write(ref _detectedKind, -1);
        }
    }

    internal void MarkAttempt() => Interlocked.Exchange(ref _lastAttemptTicks, DateTime.UtcNow.Ticks);

    internal void SetError(string? error) => Volatile.Write(ref _lastError, error);

    /// <summary>Stores a successful status; returns the previous one.</summary>
    internal ServerStatus? Update(ServerStatus status)
    {
        var previous = Interlocked.Exchange(ref _status, status);
        Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
        Volatile.Write(ref _lastError, null);
        return previous;
    }

    /// <summary>Called only by the (serialised) poller: returns true if the online state flipped since last reported.</summary>
    internal bool ConsumeOnlineChange(out bool online)
    {
        online = IsOnline;
        if (online == _reportedOnline)
        {
            return false;
        }

        _reportedOnline = online;
        return true;
    }

    internal void CountTransfer() => Interlocked.Increment(ref _transferCount);

    internal void RaiseTransferred(TransferEventArgs e) => Transferred?.Invoke(this, e);

    internal void RaiseStatusChanged(ServerStatusChangedEventArgs e) => StatusChanged?.Invoke(this, e);

    internal void RaiseOnlineChanged(OnlineChangedEventArgs e) => OnlineChanged?.Invoke(this, e);

    internal void RaiseKindDetected(KindDetectedEventArgs e) => KindDetected?.Invoke(this, e);

    internal void RaiseRemoved(RelayServerEventArgs e) => Removed?.Invoke(this, e);

    /// <summary>The MOTD to answer RakNet pings with, rewritten to point at the local minimal server.</summary>
    internal string BuildRakNetMotd(long guid, int localPort)
    {
        var status = Status;
        BedrockMotd motd;
        if (status?.RakNetMotd is { } upstream)
        {
            motd = upstream.Clone();
        }
        else
        {
            motd = new BedrockMotd
            {
                Motd = status?.Name ?? Address,
                Protocol = status?.Protocol ?? 0,
                Version = status?.Version ?? "",
                Players = status?.Players ?? 0,
                MaxPlayers = status?.MaxPlayers ?? 0,
                SubMotd = status?.Level ?? "",
                GameMode = BedrockMotd.GameModeName(status?.GameMode ?? 0),
                GameModeNumeric = 1,
            };
        }

        motd.Motd = NamePrefix + BedrockText.StripFormatting(DisplayName ?? motd.Motd);
        motd.SubMotd = BedrockText.StripFormatting(LevelName ?? motd.SubMotd);

        motd.ServerGuid = guid;
        motd.PortV4 = localPort;
        motd.PortV6 = localPort;
        return motd.ToString();
    }

    /// <summary>
    /// The NetherNet LAN discovery application data for this server. The LAN list shows the level name as the
    /// title and the server name underneath, so the server's name goes in the level-name field and its
    /// second line (level/sub-MOTD) in the server-name field. <see cref="DisplayName"/> is always the title.
    /// </summary>
    internal byte[] BuildNetherNetServerData()
    {
        var status = Status;
        var displayName = DisplayName;
        var levelName = LevelName;
        var prefix = NamePrefix;
        if (status?.NetherNetData is { } upstream)
        {
            // A real LAN world already uses the fields the way the client expects; only apply overrides.
            var data = upstream.Clone();
            data.LevelName = prefix + BedrockText.StripFormatting(displayName ?? data.LevelName);
            data.ServerName = BedrockText.StripFormatting(levelName ?? data.ServerName);
            return data.ToBytes();
        }

        if (status?.NetherNetRaw is { } raw)
        {
            return raw; // unknown format version: forward untouched
        }

        return new NetherNetServerData
        {
            LevelName = prefix + BedrockText.StripFormatting(displayName ?? NonEmpty(status?.Name) ?? Address),
            ServerName = BedrockText.StripFormatting(levelName ?? status?.Level ?? ""),
            Protocol = status?.Protocol ?? 0,
            Version = status?.Version ?? "",
            PlayerCount = status?.Players ?? 0,
            MaxPlayerCount = status?.MaxPlayers ?? 0,
            GameType = status?.GameMode ?? 0,
            AcceptsOnlineAuth = true,
            Nonce = _nonce,
            ConnectionType = 4,
        }.ToBytes();
    }

    private static DateTime? TicksToTime(long ticks) => ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record UpstreamEndPoint(ulong NetworkId, IPEndPoint EndPoint);
}
