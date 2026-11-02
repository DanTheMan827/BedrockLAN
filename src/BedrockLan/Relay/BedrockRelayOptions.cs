using System.Net;
using BedrockLan.NetherNet;

namespace BedrockLan;

/// <summary>
/// Settings for a <see cref="BedrockRelay"/>.
/// <para>
/// Properties with <c>init</c> accessors (sockets, ports, transfer handler) are fixed once the relay is created.
/// Everything with a <c>set</c> accessor can be changed at any time from any thread while the relay runs;
/// for example changing <see cref="PollInterval"/> takes effect immediately.
/// </para>
/// </summary>
public sealed class BedrockRelayOptions
{
    private long _pollIntervalTicks = TimeSpan.FromSeconds(5).Ticks;
    private long _queryTimeoutTicks = TimeSpan.FromSeconds(2).Ticks;
    private long _offlineAfterTicks = TimeSpan.FromSeconds(20).Ticks;
    private long _candidateWindowTicks = TimeSpan.FromMilliseconds(500).Ticks;
    private int _hideOfflineServers = 1;
    private int _trickleAnswerCandidates = 1;
    private LanLogger? _logger;

    public static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);

    // ---- Fixed at creation -------------------------------------------------------------

    /// <summary>Local address to bind every socket to.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>Advertise via RakNet LAN discovery (clients before 26.60).</summary>
    public bool EnableRakNet { get; init; } = true;

    public int RakNetDiscoveryPort { get; init; } = 19132;

    /// <summary>First UDP port handed to per-server minimal RakNet servers (0 = let the OS pick).</summary>
    public int RakNetFirstServerPort { get; init; } = 19140;

    /// <summary>Advertise via NetherNet LAN discovery (26.50+; the only kind 26.60+ clients use).</summary>
    public bool EnableNetherNet { get; init; } = true;

    public int NetherNetDiscoveryPort { get; init; } = DiscoveryPacketCodec.DefaultPort;

    /// <summary>
    /// Creates the handler that terminates WebRTC locally and issues a Transfer (see BedrockLan.WebRtc's
    /// <c>UseWebRtcTransfer()</c>). Without it, NetherNet joins can only be relayed, which authenticated servers reject.
    /// </summary>
    public Func<NetherNetHandlerContext, INetherNetSignalHandler>? TransferHandlerFactory { get; init; }

    /// <summary>
    /// When <see cref="TransferHandlerFactory"/> is null and the BedrockLan.WebRtc assembly is available, use its
    /// WebRTC transfer server automatically. Default true.
    /// </summary>
    public bool AutoEnableWebRtcTransfer { get; init; } = true;

    /// <summary>Where persistent data (the NetherNet operator key) is stored. Default: <c>./persist</c> in the working directory.</summary>
    public string PersistDirectory { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "persist");

    /// <summary>Transfer behaviour. The object is fixed, its properties are live.</summary>
    public TransferOptions Transfer { get; init; } = new();

    // ---- Live --------------------------------------------------------------------------

    /// <summary>How often every server is queried. Minimum 1 second. Takes effect immediately.</summary>
    public TimeSpan PollInterval
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _pollIntervalTicks));
        set
        {
            var ticks = Math.Max(MinimumPollInterval.Ticks, value.Ticks);
            if (Interlocked.Exchange(ref _pollIntervalTicks, ticks) != ticks)
            {
                PollIntervalChanged?.Invoke();
            }
        }
    }

    /// <summary>Per-query timeout for UDP probes (HTTP gets 3 s more).</summary>
    public TimeSpan QueryTimeout
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _queryTimeoutTicks));
        set => Interlocked.Exchange(ref _queryTimeoutTicks, Math.Max(TimeSpan.FromMilliseconds(200).Ticks, value.Ticks));
    }

    /// <summary>A server is considered offline when it has not answered for this long.</summary>
    public TimeSpan OfflineAfter
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _offlineAfterTicks));
        set => Interlocked.Exchange(ref _offlineAfterTicks, Math.Max(TimeSpan.FromSeconds(1).Ticks, value.Ticks));
    }

    /// <summary>Stop advertising servers that are offline (otherwise they are listed with their last known data).</summary>
    public bool HideOfflineServers
    {
        get => Volatile.Read(ref _hideOfflineServers) != 0;
        set => Volatile.Write(ref _hideOfflineServers, value ? 1 : 0);
    }

    /// <summary>Relay mode: how long to collect trickled ICE candidates before posting to HTTP signaling.</summary>
    public TimeSpan CandidateWindow
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _candidateWindowTicks));
        set => Interlocked.Exchange(ref _candidateWindowTicks, Math.Max(0, value.Ticks));
    }

    /// <summary>Relay mode: also send each answer candidate as CANDIDATEADD.</summary>
    public bool TrickleAnswerCandidates
    {
        get => Volatile.Read(ref _trickleAnswerCandidates) != 0;
        set => Volatile.Write(ref _trickleAnswerCandidates, value ? 1 : 0);
    }

    /// <summary>Log sink. May be replaced at runtime.</summary>
    public LanLogger? Logger
    {
        get => Volatile.Read(ref _logger);
        set => Volatile.Write(ref _logger, value);
    }

    internal event Action? PollIntervalChanged;
}
