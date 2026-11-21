// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;
using BedrockLan.Internal;
using BedrockLan.NetherNet;
using BedrockLan.RakNet;

namespace BedrockLan;

/// <summary>
/// Advertises Minecraft: Bedrock servers on the local network without proxying game traffic.
/// <code>
/// await using var relay = new BedrockRelay();
/// var server = relay.AddServer("play.example.com:19132");
/// server.Transferred += (_, e) => Console.WriteLine($"{e.Client} -> {e.Target}");
/// // ...
/// await server.DisposeAsync();   // stop advertising just this one
/// </code>
/// <para>
/// Each server is queried to detect its type (NetherNet HTTP signaling, NetherNet LAN host or RakNet) and its live
/// details are forwarded to LAN clients over RakNet (UDP 19132) and NetherNet (UDP 7551) discovery. Joining clients
/// are sent on with a Transfer packet or, in relay mode, by relaying WebRTC signaling.
/// </para>
/// <para>
/// Thread-safe: every member may be called concurrently from any thread. Events are raised one at a time, in
/// order, on a dedicated event thread (never under internal locks), so handlers may call back into the relay.
/// Disposing the relay stops and removes every server.
/// </para>
/// </summary>
public sealed class BedrockRelay : IDisposable, IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly List<RelayServer> _servers = new();
    private readonly HashSet<int> _usedRakNetPorts = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly EventDispatcher _events;
    private readonly LanLogger _log;
    private readonly RakNetLanDiscovery? _rakNetDiscovery;
    private readonly NetherNetLanDiscovery? _netherNetDiscovery;
    private readonly Task _pollLoop;
    private readonly Func<NetherNetHandlerContext, INetherNetSignalHandler>? _transferFactory;
    private int _disposed;

    /// <summary>Creates a relay with default options and starts it.</summary>
    public BedrockRelay()
        : this(new BedrockRelayOptions())
    {
    }

    /// <summary>Creates a relay and starts it: discovery sockets are bound and polling begins immediately.</summary>
    public BedrockRelay(BedrockRelayOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _log = (level, message) => Options.Logger?.Invoke(level, message);
        _events = new EventDispatcher(() => _log);
        _transferFactory = options.TransferHandlerFactory ?? (options.AutoEnableWebRtcTransfer ? TryLoadWebRtcTransfer() : null);
        _log.Info(_transferFactory is null
            ? "WebRTC transfer server not available: NetherNet joins will be relayed (authenticated servers reject that)."
            : "WebRTC transfer server enabled for NetherNet joins.");

        if (options.EnableRakNet)
        {
            try
            {
                _rakNetDiscovery = new RakNetLanDiscovery(
                    new IPEndPoint(options.BindAddress, options.RakNetDiscoveryPort),
                    RakNetResponders,
                    _log);
                _rakNetDiscovery.Start();
                _log.Info($"RakNet LAN discovery listening on UDP {options.RakNetDiscoveryPort}.");
            }
            catch (SocketException ex)
            {
                _log.Warn($"RakNet LAN discovery: cannot bind UDP {options.RakNetDiscoveryPort} ({ex.Message}). Servers still answer direct pings on their own ports.");
            }
        }

        if (options.EnableNetherNet)
        {
            try
            {
                _netherNetDiscovery = new NetherNetLanDiscovery(new IPEndPoint(options.BindAddress, options.NetherNetDiscoveryPort), _log);
                _netherNetDiscovery.Start();
                _log.Info($"NetherNet LAN discovery listening on UDP {options.NetherNetDiscoveryPort}.");
            }
            catch (SocketException ex)
            {
                _log.Error($"NetherNet LAN discovery: cannot bind UDP {options.NetherNetDiscoveryPort} ({ex.Message}). NetherNet advertising disabled.");
            }
        }

        Options.PollIntervalChanged += WakePollLoop;
        _pollLoop = Task.Run(() => PollLoopAsync(_shutdown.Token));
    }

    /// <summary>Live options; see <see cref="BedrockRelayOptions"/> for which ones can change at runtime.</summary>
    public BedrockRelayOptions Options { get; }

    /// <summary>Snapshot of the servers currently in the relay.</summary>
    public IReadOnlyList<RelayServer> Servers
    {
        get
        {
            lock (_lock)
            {
                return _servers.ToArray();
            }
        }
    }

    /// <summary>True if NetherNet joins can be answered with a Transfer (a WebRTC transfer handler is configured or was found).</summary>
    public bool WebRtcTransferEnabled => _transferFactory is not null;

    /// <summary>True if the RakNet LAN discovery socket (UDP 19132) is bound.</summary>
    public bool RakNetActive => _rakNetDiscovery is not null;

    /// <summary>True if the NetherNet LAN discovery socket (UDP 7551) is bound.</summary>
    public bool NetherNetActive => _netherNetDiscovery is not null;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    // ---- Events (also raised per server on RelayServer) -----------------------------------------

    public event EventHandler<RelayServerEventArgs>? ServerAdded;

    public event EventHandler<RelayServerEventArgs>? ServerRemoved;

    /// <summary>A LAN client was sent on to one of the servers.</summary>
    public event EventHandler<TransferEventArgs>? Transferred;

    public event EventHandler<ServerStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<OnlineChangedEventArgs>? OnlineChanged;

    public event EventHandler<KindDetectedEventArgs>? KindDetected;

    // ---- Servers --------------------------------------------------------------------------------

    /// <summary>
    /// Starts advertising a server. <paramref name="address"/> is <c>host</c>, <c>host:port</c> or <c>[ipv6]:port</c>
    /// (port 19132 by default). Returns immediately; the type and details are queried in the background
    /// (await <see cref="RelayServer.RefreshAsync"/> or use <see cref="AddServerAsync"/> to wait for them).
    /// </summary>
    public RelayServer AddServer(string address, RelayServerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var (host, port) = ParseAddress(address);
        options ??= new RelayServerOptions();
        var server = new RelayServer(this, address.Trim(), host, port, options);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _servers.Add(server);
            if (Options.EnableRakNet && options.AdvertiseRakNet && options.Kind != UpstreamKind.NetherNetLan)
            {
                StartRakNetEndpoint(server);
            }

            if (_netherNetDiscovery is { } hub && options.AdvertiseNetherNet)
            {
                var handler = new DetectedKindSignalHandler(this, server, hub);
                server.SignalHandler = handler;
                hub.AddAdvertisement(
                    server.NetherNetId,
                    server.Address,
                    () => AdvertisesNetherNet(server) ? server.BuildNetherNetServerData() : null,
                    handler);
            }
        }

        _log.Info($"[{server.Address}] added{(server.RakNetPort is { } p ? $" (RakNet endpoint UDP {p})" : "")}; querying...");
        var added = new RelayServerEventArgs(server);
        _events.Post(() => ServerAdded?.Invoke(this, added));
        _ = PollAsync(server, wait: false, CancellationToken.None);
        return server;
    }

    /// <summary>Adds a server and waits for its first query to finish (it may still be offline afterwards).</summary>
    public async Task<RelayServer> AddServerAsync(string address, RelayServerOptions? options = null, CancellationToken cancellationToken = default)
    {
        var server = AddServer(address, options);
        await server.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return server;
    }

    /// <summary>Parses <c>host</c>, <c>host:port</c>, <c>[v6]</c> or <c>[v6]:port</c>. A bare IPv6 address is left whole.</summary>
    public static (string Host, int? Port) ParseAddress(string address)
    {
        var value = address.Trim();
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close > 0)
            {
                var host = value[1..close];
                var rest = value[(close + 1)..];
                return rest.StartsWith(':') ? (host, ParsePort(rest[1..], address)) : (host, null);
            }
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            return (value[..colon], ParsePort(value[(colon + 1)..], address));
        }

        return (value, null);
    }

    private static int ParsePort(string text, string original) =>
        int.TryParse(text, out var port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException($"Invalid port in '{original}'.");

    // ---- Internals used by RelayServer ----------------------------------------------------------

    internal bool AdvertisesRakNet(RelayServer server) =>
        server.Options.AdvertiseRakNet
        && server.Kind is UpstreamKind.RakNet or UpstreamKind.NetherNetHttp
        && ShouldAdvertise(server);

    internal bool AdvertisesNetherNet(RelayServer server) =>
        server.Options.AdvertiseNetherNet
        && server.Kind switch
        {
            UpstreamKind.NetherNetHttp or UpstreamKind.NetherNetLan => true,
            UpstreamKind.RakNet => _transferFactory is not null,
            _ => false,
        }
        && ShouldAdvertise(server);

    internal async ValueTask RemoveAsync(RelayServer server)
    {
        if (!server.TryMarkDisposed())
        {
            return;
        }

        RakNetTransferServer? rakNet;
        INetherNetSignalHandler? handler;
        lock (_lock)
        {
            _servers.Remove(server);
            rakNet = server.RakNetServer;
            server.RakNetServer = null;
            if (rakNet is not null)
            {
                _usedRakNetPorts.Remove(rakNet.Port);
            }

            handler = server.SignalHandler;
            server.SignalHandler = null;
        }

        _netherNetDiscovery?.RemoveAdvertisement(server.NetherNetId);
        (handler as IDisposable)?.Dispose();
        if (rakNet is not null)
        {
            await rakNet.DisposeAsync().ConfigureAwait(false);
        }

        server.ReleaseResources();
        _log.Info($"[{server.Address}] removed.");
        var removed = new RelayServerEventArgs(server);
        _events.Post(() =>
        {
            server.RaiseRemoved(removed);
            ServerRemoved?.Invoke(this, removed);
        });
    }

    internal async Task PollAsync(RelayServer server, bool wait, CancellationToken cancellationToken)
    {
        if (server.IsDisposed || IsDisposed)
        {
            return;
        }

        if (wait)
        {
            await server.PollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await server.PollGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return; // a query is already running
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, server.Lifetime, _shutdown.Token);
            await PollCoreAsync(server, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (server.IsDisposed || IsDisposed)
        {
        }
        finally
        {
            server.PollGate.Release();
        }
    }

    // ---- Implementation -------------------------------------------------------------------------

    private bool ShouldAdvertise(RelayServer server) =>
        !server.IsDisposed && (!Options.HideOfflineServers || server.IsOnline);

    private Func<TransferTarget?> TargetFor(RelayServer server) =>
        () => server.IsOnline ? server.TransferTarget : null;

    private IEnumerable<RakNetTransferServer> RakNetResponders()
    {
        foreach (var server in Servers)
        {
            if (server.RakNetServer is { } rakNet && AdvertisesRakNet(server))
            {
                yield return rakNet;
            }
        }
    }

    /// <summary>Binds the server's minimal RakNet endpoint. Caller holds <see cref="_lock"/>.</summary>
    private void StartRakNetEndpoint(RelayServer server)
    {
        var fixedPort = server.Options.RakNetLocalPort;
        var candidate = Options.RakNetFirstServerPort;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            int port;
            if (fixedPort is { } f)
            {
                port = f;
            }
            else if (Options.RakNetFirstServerPort == 0)
            {
                port = 0; // ephemeral ports requested
            }
            else
            {
                while (_usedRakNetPorts.Contains(candidate) || candidate == Options.RakNetDiscoveryPort)
                {
                    candidate++;
                }

                port = candidate++;
            }

            try
            {
                RakNetTransferServer? rakNet = null;
                rakNet = new RakNetTransferServer(
                    new IPEndPoint(Options.BindAddress, port),
                    server.Address,
                    () => rakNet is not null && AdvertisesRakNet(server) ? server.BuildRakNetMotd(rakNet.Guid, rakNet.Port) : null,
                    TargetFor(server),
                    Options.Transfer,
                    _log,
                    (client, target, protocol) => OnRedirected(server, client, RelayTransport.RakNet, RedirectMethod.TransferPacket, target, protocol));
                rakNet.Start();
                _usedRakNetPorts.Add(rakNet.Port);
                server.RakNetServer = rakNet;
                return;
            }
            catch (SocketException ex)
            {
                if (fixedPort is not null)
                {
                    _log.Error($"[{server.Address}] RakNet: cannot bind UDP {port}: {ex.Message}");
                    return;
                }

                _usedRakNetPorts.Add(port); // in use by something else; skip it from now on
            }
        }

        _log.Error($"[{server.Address}] RakNet: no free UDP port found from {Options.RakNetFirstServerPort}.");
    }

    private void OnRedirected(RelayServer server, IPEndPoint client, RelayTransport transport, RedirectMethod method, TransferTarget? target, int? protocol)
    {
        server.CountTransfer();
        var args = new TransferEventArgs(server, client, transport, method, target, protocol);
        _events.Post(() =>
        {
            server.RaiseTransferred(args);
            Transferred?.Invoke(this, args);
        });
    }

    private INetherNetSignalHandler? CreateNetherNetHandler(RelayServer server, UpstreamKind kind, NetherNetLanDiscovery hub)
    {
        var mode = kind == UpstreamKind.RakNet ? NetherNetConnectMode.Transfer : server.Options.NetherNetMode;
        var factory = _transferFactory;
        Action<IPEndPoint> relayed = client => OnRedirected(server, client, RelayTransport.NetherNet, RedirectMethod.SignalingRelay, null, null);
        HttpSignalingBridge HttpRelay(Func<NetherNetClientMessage, ulong, Exception, bool>? failed = null, TimeSpan? joinTimeout = null) =>
            new(server.SignalingClients, server.Address, () => Options.CandidateWindow, () => Options.TrickleAnswerCandidates, _log, relayed,
                server.PreferSignalingClient, failed, joinTimeout);
        INetherNetSignalHandler Transfer() => factory!(new NetherNetHandlerContext(
            server,
            hub,
            TargetFor(server),
            Options.Transfer,
            _log,
            (client, target, protocol) => OnRedirected(server, client, RelayTransport.NetherNet, RedirectMethod.TransferPacket, target, protocol)));

        if (mode == NetherNetConnectMode.Auto)
        {
            // Transfer where possible: a relayed LAN offer can be accepted by the server (it answers) and still
            // fail on the client, which we can't see from here. LAN hosts have no host:port, so they're relayed.
            mode = kind != UpstreamKind.NetherNetLan && factory is not null ? NetherNetConnectMode.Transfer : NetherNetConnectMode.Relay;
        }

        if (mode == NetherNetConnectMode.Relay && kind == UpstreamKind.NetherNetHttp && factory is not null)
        {
            // Opt-in: forward the join; if the server refuses the LAN offer, answer it ourselves and Transfer.
            _log.Info($"[{server.Name}] NetherNet joins: relaying signaling to {server.HttpClient?.BaseUri}, WebRTC transfer to {server.TransferTarget} if the server refuses it.");
            return new RelayThenTransferHandler(failed => HttpRelay(failed, RelayJoinTimeout), Transfer(), server.Address, _log);
        }

        if (mode == NetherNetConnectMode.Transfer && kind != UpstreamKind.NetherNetLan)
        {
            if (factory is not null)
            {
                _log.Info($"[{server.Name}] NetherNet joins: WebRTC transfer to {server.TransferTarget}.");
                return Transfer();
            }

            _log.Warn($"[{server.Name}] Transfer needs a WebRTC transfer handler (BedrockLan.WebRtc); falling back to signaling relay, which authenticated servers reject.");
        }

        switch (kind)
        {
            case UpstreamKind.NetherNetHttp:
                _log.Info($"[{server.Name}] NetherNet joins: relaying signaling to {server.HttpClient?.BaseUri}.");
                return HttpRelay();
            case UpstreamKind.NetherNetLan:
                _log.Info($"[{server.Name}] NetherNet joins: relaying LAN signaling to {server.Host}.");
                return new LanSignalingBridge(hub, () => server.LanUpstream, server.Address, _log, relayed);
            default:
                return null;
        }
    }

    /// <summary>Auto mode: how long a relayed join may take before the transfer server answers instead.</summary>
    private static readonly TimeSpan RelayJoinTimeout = TimeSpan.FromSeconds(4);

    private Func<NetherNetHandlerContext, INetherNetSignalHandler>? TryLoadWebRtcTransfer()
    {
        try
        {
            var type = Type.GetType("BedrockLan.WebRtc.WebRtcTransfer, BedrockLan.WebRtc", throwOnError: false);
            var method = type?.GetMethod("CreateFactory", new[] { typeof(string), typeof(LanLogger) });
            return method?.Invoke(null, new object?[] { Options.PersistDirectory, _log }) as Func<NetherNetHandlerContext, INetherNetSignalHandler>;
        }
        catch (Exception ex)
        {
            _log.Warn($"BedrockLan.WebRtc found but could not be initialised: {(ex.InnerException ?? ex).Message}");
            return null;
        }
    }

    private void WakePollLoop()
    {
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAll(Servers.Select(s => PollAsync(s, wait: false, token))).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error($"Status polling error: {ex.Message}");
            }

            try
            {
                await _wake.WaitAsync(Options.PollInterval, token).ConfigureAwait(false);
                while (_wake.CurrentCount > 0)
                {
                    _wake.Wait(0);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollCoreAsync(RelayServer server, CancellationToken token)
    {
        server.MarkAttempt();
        var timeout = Options.QueryTimeout;
        ServerStatus? status = null;
        UpstreamKind? detected = null;

        try
        {
            if (server.ConfiguredKind == UpstreamKind.Auto)
            {
                // Auto: probe every transport on every poll, not just the one that answered first. A server can be
                // reachable over some transports and not others (for example only its UDP port is open, so NetherNet
                // HTTP signaling over TCP fails while a RakNet ping on the same port answers), and which ones
                // answer can change. The best transport that answers right now decides how players are sent there.
                var result = await UpstreamDetector.DetectAsync(server, timeout, token).ConfigureAwait(false);
                if (result is null)
                {
                    server.SetError($"no Bedrock server answered (tried NetherNet HTTP/HTTPS on TCP {server.Port}, " +
                        $"RakNet on UDP {server.Port} and NetherNet LAN discovery on UDP {DiscoveryPacketCodec.DefaultPort})");
                }
                else
                {
                    if (server.Kind != result.Kind)
                    {
                        detected = result.Kind; // first detection, or the reachable transport changed
                    }

                    server.ApplyDetection(result.Kind, result.Http);
                    if (result.Lan is { } lan)
                    {
                        server.LanUpstream = (lan.NetworkId, lan.EndPoint);
                    }

                    status = result.Status;
                }
            }
            else if (server.Kind is { } kind)
            {
                status = await QueryAsync(server, kind, timeout, token).ConfigureAwait(false);
                if (status is null && server.LastError is null)
                {
                    server.SetError("no response");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            server.SetError(ex.Message);
        }

        if (server.IsDisposed)
        {
            return;
        }

        if (status is not null)
        {
            var previous = server.Update(status);
            if (detected is { } detectedKind)
            {
                _log.Info($"[{server.Address}] reachable as {UpstreamDetector.Describe(detectedKind, server)}.");
                var args = new KindDetectedEventArgs(server, detectedKind);
                _events.Post(() =>
                {
                    server.RaiseKindDetected(args);
                    KindDetected?.Invoke(this, args);
                });
            }

            if (status.DiffersFrom(previous))
            {
                var args = new ServerStatusChangedEventArgs(server, previous, status);
                _events.Post(() =>
                {
                    server.RaiseStatusChanged(args);
                    StatusChanged?.Invoke(this, args);
                });
            }
        }
        else if (server.ConfiguredKind == UpstreamKind.Auto && server.Kind is not null && !server.IsOnline)
        {
            server.ResetDetection(); // offline: forget the type until something answers again
            _log.Debug($"[{server.Address}] offline; its type will be detected again when it answers.");
        }

        if (server.ConsumeOnlineChange(out var online))
        {
            if (online)
            {
                _log.Info($"[{server.Address}] online: {server.Status}");
            }
            else
            {
                _log.Warn($"[{server.Address}] offline{(server.LastError is { } e ? ": " + e : "")}");
            }

            var args = new OnlineChangedEventArgs(server, online, online ? null : server.LastError);
            _events.Post(() =>
            {
                server.RaiseOnlineChanged(args);
                OnlineChanged?.Invoke(this, args);
            });
        }
    }

    private static async Task<ServerStatus?> QueryAsync(RelayServer server, UpstreamKind kind, TimeSpan timeout, CancellationToken token)
    {
        switch (kind)
        {
            case UpstreamKind.RakNet:
            {
                var motd = await RakNetPinger.PingAsync(server.Host, server.Port, timeout, token).ConfigureAwait(false);
                return motd is null ? null : ServerStatus.FromMotd(motd);
            }

            case UpstreamKind.NetherNetHttp:
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeout + TimeSpan.FromSeconds(3));
                var client = server.HttpClient ?? throw new InvalidOperationException("no HTTP signaling client");
                var info = await client.GetInfoAsync(cts.Token).ConfigureAwait(false);
                if (info is null)
                {
                    server.SetError("GET /v1/join did not return 2xx");
                    return null;
                }

                return ServerStatus.FromHttp(info);
            }

            case UpstreamKind.NetherNetLan:
            {
                var port = server.ConfiguredKind == UpstreamKind.NetherNetLan ? server.Port : DiscoveryPacketCodec.DefaultPort;
                var address = await UdpSockets.ResolveAsync(server.Host, token).ConfigureAwait(false);
                var result = await NetherNetLanQuery.QueryAsync(new IPEndPoint(address, port), timeout, token).ConfigureAwait(false);
                if (result is null)
                {
                    return null;
                }

                server.LanUpstream = (result.NetworkId, result.EndPoint);
                return ServerStatus.FromLan(result.ServerData);
            }

            default:
                return null;
        }
    }

    /// <summary>Stops and removes every server, then closes the discovery sockets.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Options.PollIntervalChanged -= WakePollLoop;
        _shutdown.Cancel();

        foreach (var server in Servers)
        {
            await RemoveAsync(server).ConfigureAwait(false);
        }

        if (_rakNetDiscovery is not null)
        {
            await _rakNetDiscovery.DisposeAsync().ConfigureAwait(false);
        }

        if (_netherNetDiscovery is not null)
        {
            await _netherNetDiscovery.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            await _pollLoop.ConfigureAwait(false);
        }
        catch
        {
            // Shutting down.
        }

        await _events.CompleteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc cref="DisposeAsync"/>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Advertisement handler that dispatches to the relay/transfer handler for the server's current type,
    /// creating it on first use so detected (and re-detected) servers need no restart.
    /// </summary>
    private sealed class DetectedKindSignalHandler : INetherNetSignalHandler, IDisposable
    {
        private readonly BedrockRelay _relay;
        private readonly RelayServer _server;
        private readonly NetherNetLanDiscovery _hub;
        private readonly Dictionary<UpstreamKind, INetherNetSignalHandler?> _handlers = new();
        private bool _disposed;

        public DetectedKindSignalHandler(BedrockRelay relay, RelayServer server, NetherNetLanDiscovery hub)
        {
            _relay = relay;
            _server = server;
            _hub = hub;
        }

        public void OnClientMessage(NetherNetClientMessage message)
        {
            if (_server.Kind is not { } kind)
            {
                return;
            }

            INetherNetSignalHandler? handler;
            lock (_handlers)
            {
                if (_disposed)
                {
                    return;
                }

                if (!_handlers.TryGetValue(kind, out handler))
                {
                    handler = _relay.CreateNetherNetHandler(_server, kind, _hub);
                    _handlers[kind] = handler;
                }
            }

            handler?.OnClientMessage(message);
        }

        public void Dispose()
        {
            List<INetherNetSignalHandler?> handlers;
            lock (_handlers)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                handlers = _handlers.Values.ToList();
                _handlers.Clear();
            }

            foreach (var handler in handlers)
            {
                (handler as IDisposable)?.Dispose();
            }
        }
    }
}
