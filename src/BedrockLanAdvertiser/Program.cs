using System.Globalization;
using System.Runtime.InteropServices;
using BedrockLan;
using BedrockLanAdvertiser;

// Usage:
//   BedrockLanAdvertiser play.example.com 192.168.1.20:19133      servers as arguments, no config needed
//   BedrockLanAdvertiser --config servers.json [more servers...]  config file (plus optional extra servers)
//   BedrockLanAdvertiser                                          uses ./servers.json if present

string? configPath = null;
var extraServers = new List<string>();
double? pollOverride = null;
var verboseOverride = false;
var noRakNet = false;
var noNetherNet = false;
string? protocolOverride = null;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "-h" or "--help" or "/?":
                PrintUsage();
                return 0;
            case "-c" or "--config":
                configPath = NextArg(ref i, arg);
                break;
            case "-p" or "--poll":
                pollOverride = double.Parse(NextArg(ref i, arg), CultureInfo.InvariantCulture);
                break;
            case "-v" or "--verbose":
                verboseOverride = true;
                break;
            case "--no-raknet":
                noRakNet = true;
                break;
            case "--no-nethernet":
                noNetherNet = true;
                break;
            case "--protocol":
                protocolOverride = NextArg(ref i, arg);
                break;
            default:
                if (arg.StartsWith('-'))
                {
                    throw new ArgumentException($"Unknown option '{arg}'.");
                }

                if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && configPath is null)
                {
                    configPath = arg;
                }
                else
                {
                    extraServers.Add(arg);
                }

                break;
        }
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 1;
}

if (configPath is null && extraServers.Count == 0)
{
    configPath = FindDefaultConfig();
    if (configPath is null)
    {
        PrintUsage();
        return 1;
    }
}

AppConfig config;
var servers = new List<(string Address, RelayServerOptions Options)>();
BedrockRelayOptions options;
try
{
    config = configPath is null ? new AppConfig() : AppConfig.Load(configPath);
    foreach (var entry in config.Servers.Where(s => s.Enabled))
    {
        if (string.IsNullOrWhiteSpace(entry.Host))
        {
            throw new InvalidDataException("Every server needs a \"host\".");
        }

        servers.Add((entry.Address, entry.ToOptions()));
    }

    foreach (var address in extraServers)
    {
        BedrockRelay.ParseAddress(address); // validate early
        servers.Add((address, new RelayServerOptions()));
    }

    if (servers.Count == 0)
    {
        Console.Error.WriteLine("No servers given. Pass hosts as arguments or list them in servers.json.");
        return 1;
    }

    options = new BedrockRelayOptions
    {
        BindAddress = config.ParseBindAddress(),
        EnableRakNet = config.RakNet.Enabled && !noRakNet,
        RakNetDiscoveryPort = config.RakNet.DiscoveryPort,
        RakNetFirstServerPort = config.RakNet.FirstServerPort,
        EnableNetherNet = config.NetherNet.Enabled && !noNetherNet,
        NetherNetDiscoveryPort = config.NetherNet.DiscoveryPort,
        PersistDirectory = config.PersistDirectory is { } dir ? Path.GetFullPath(dir) : Path.Combine(Directory.GetCurrentDirectory(), "persist"),
        PollInterval = TimeSpan.FromSeconds(pollOverride ?? config.PollIntervalSeconds),
        OfflineAfter = TimeSpan.FromSeconds(config.OfflineAfterSeconds),
        HideOfflineServers = config.HideOfflineServers,
        CandidateWindow = TimeSpan.FromMilliseconds(config.NetherNet.CandidateWindowMilliseconds),
        TrickleAnswerCandidates = config.NetherNet.TrickleAnswerCandidates,
    };
    options.Transfer.Timing = config.TransferTiming;
    options.Transfer.DisconnectDelay = TimeSpan.FromSeconds(config.TransferDisconnectDelaySeconds);
}
catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Invalid configuration{(configPath is null ? "" : $" in {configPath}")}: {ex.Message}");
    return 1;
}

// Protocol config: published updates by default; a file, another URL, or "builtin" if configured.
string? protocolSource = (protocolOverride ?? config.Protocol ?? ProtocolConfig.DefaultUpdateUrl).Trim();
if (ProtocolSource.IsBuiltIn(protocolSource))
{
    protocolSource = null;
}
else if (!ProtocolSource.IsUrl(protocolSource))
{
    // A local file is part of the configuration: a broken one stops here.
    try
    {
        options.Transfer.Protocol = await ProtocolSource.LoadFileAsync(protocolSource, CancellationToken.None);
    }
    catch (Exception ex) when (ProtocolSource.IsLoadFailure(ex))
    {
        Console.Error.WriteLine($"Invalid protocol config {protocolSource}: {ex.Message}");
        return 1;
    }
}

var verbose = config.Verbose || verboseOverride;
var consoleLock = new object();
void Write(LanLogLevel level, string message)
{
    if (level == LanLogLevel.Debug && !verbose)
    {
        return;
    }

    lock (consoleLock)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            LanLogLevel.Debug => ConsoleColor.DarkGray,
            LanLogLevel.Warning => ConsoleColor.Yellow,
            LanLogLevel.Error => ConsoleColor.Red,
            _ => previous,
        };
        var label = level switch
        {
            LanLogLevel.Debug => "DBG",
            LanLogLevel.Warning => "WRN",
            LanLogLevel.Error => "ERR",
            _ => "INF",
        };
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {label} {message}");
        Console.ForegroundColor = previous;
    }
}

options.Logger = Write;

// A URL is only an update: if it can't be fetched (anything but HTTP 200) or doesn't validate, log it and keep
// the built-in config. Its caching headers are kept for the refreshes below.
ProtocolCacheInfo? protocolCache = null;
if (protocolSource is not null && ProtocolSource.IsUrl(protocolSource))
{
    try
    {
        var first = await ProtocolSource.CheckUrlAsync(protocolSource, null, CancellationToken.None);
        options.Transfer.Protocol = first.Config!;
        protocolCache = first.Cache;
    }
    catch (Exception ex) when (ProtocolSource.IsLoadFailure(ex))
    {
        Write(LanLogLevel.Warning, $"Protocol config from {protocolSource} not used ({ex.Message}); using the built-in {ProtocolConfig.Default.ConfigVersion}.");
    }
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true; // let the await-using cleanup run
    shutdown.Cancel();
});

await using var relay = new BedrockRelay(options);
relay.Transferred += (_, e) => Write(LanLogLevel.Info,
    e.Method == RedirectMethod.TransferPacket
        ? $"[{e.Server.Name}] sent {e.Client} to {e.Target} over {e.Transport} (total {e.Server.TransferCount})."
        : $"[{e.Server.Name}] relayed {e.Client}'s join over {e.Transport} (total {e.Server.TransferCount}).");

Write(LanLogLevel.Info, $"Adding {servers.Count} server(s){(configPath is null ? "" : $" from {Path.GetFullPath(configPath)}")}...");
try
{
    await Task.WhenAll(servers.Select(s => relay.AddServerAsync(s.Address, s.Options, shutdown.Token)));
}
catch (OperationCanceledException)
{
    return 0;
}

PrintSummary(relay);
Write(LanLogLevel.Info, $"Using {relay.Options.Transfer.Protocol}{(ReferenceEquals(relay.Options.Transfer.Protocol, ProtocolConfig.Default) ? " (built in)" : $" from {protocolSource}")}.");
Write(LanLogLevel.Info, "Advertising. Press Ctrl+C to stop.");

// A protocol config URL is checked again now and then, following its caching headers (an unchanged file isn't
// downloaded again); a failed check or a bad file keeps the config in use.
if (protocolSource is { } protocolUrl && ProtocolSource.IsUrl(protocolUrl) && config.ProtocolRefreshHours > 0)
{
    _ = Task.Run(async () =>
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(config.ProtocolRefreshHours), shutdown.Token);
                // The cache info describes the downloaded copy, so it's only sent while that copy is in use.
                var inUse = relay.Options.Transfer.Protocol;
                var result = await ProtocolSource.CheckUrlAsync(protocolUrl, ReferenceEquals(inUse, ProtocolConfig.Default) ? null : protocolCache, shutdown.Token);
                protocolCache = result.Cache;
                if (result.Outcome == ProtocolDownloadOutcome.Downloaded && result.Config!.Text != inUse.Text)
                {
                    relay.Options.Transfer.Protocol = result.Config;
                    Write(LanLogLevel.Info, $"Protocol config updated to {result.Config.ConfigVersion}.");
                }
                else
                {
                    Write(LanLogLevel.Debug, $"Protocol config {inUse.ConfigVersion} is current ({result.Outcome}).");
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ProtocolSource.IsLoadFailure(ex))
            {
                Write(LanLogLevel.Warning, $"Protocol config update failed ({ex.Message}); keeping {relay.Options.Transfer.Protocol.ConfigVersion}.");
            }
        }
    });
}

try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
}

Write(LanLogLevel.Info, "Stopping...");
return 0;

string NextArg(ref int index, string option)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"{option} needs a value.");
    }

    return args[++index];
}

static string? FindDefaultConfig()
{
    foreach (var candidate in new[] { "servers.json", Path.Combine(AppContext.BaseDirectory, "servers.json") })
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Advertises Minecraft: Bedrock servers on the LAN (no game traffic is proxied).

        Usage:
          BedrockLanAdvertiser [options] [host[:port] ...]

        Examples:
          BedrockLanAdvertiser play.example.com 192.168.1.20:19133
          BedrockLanAdvertiser --config servers.json
          BedrockLanAdvertiser                         (uses ./servers.json if it exists)

        Options:
          -c, --config <file>   Read settings and servers from a JSON file (servers given as
                                arguments are added to the ones in the file).
          -p, --poll <seconds>  How often to query servers (default 5).
          -v, --verbose         Debug logging.
              --no-raknet       Don't advertise over RakNet (pre-26.60 clients).
              --no-nethernet    Don't advertise over NetherNet (26.50+ clients).
              --protocol <file|url|builtin>
                                Protocol config (packets and transfer steps). Default:
                                the published one for this version, re-downloaded every
                                12 hours (protocolRefreshHours); falls back to the
                                built-in config if it can't be fetched.

        Each server's type is detected automatically. Persistent data (the NetherNet
        operator key) is stored in ./persist under the working directory.
        """);
}

static void PrintSummary(BedrockRelay relay)
{
    Console.WriteLine();
    Console.WriteLine($"{"Server",-30} {"Type",-14} {"RakNet",-8} {"NetherNet",-10} Status");
    foreach (var server in relay.Servers)
    {
        var kind = server.Kind switch
        {
            UpstreamKind.NetherNetHttp => "NetherNet",
            UpstreamKind.NetherNetLan => "NetherNet LAN",
            UpstreamKind.RakNet => "RakNet",
            _ => "unknown",
        };
        var rakNet = server.IsAdvertisedOnRakNet ? server.RakNetPort?.ToString() ?? "-" : "-";
        var netherNet = server.IsAdvertisedOnNetherNet ? "yes" : "-";
        var status = server.IsOnline ? server.Status?.ToString() ?? "online" : "offline" + (server.LastError is null ? "" : $" ({server.LastError})");
        Console.WriteLine($"{Truncate(server.Address, 30),-30} {kind,-14} {rakNet,-8} {netherNet,-10} {status}");
    }

    Console.WriteLine();

    static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
