using System.Net;
using BedrockLan.Internal;
using BedrockLan.NetherNet;
using BedrockLan.RakNet;

namespace BedrockLan;

internal sealed record DetectionResult(UpstreamKind Kind, ServerStatus Status, HttpSignalingClient? Http, NetherNetLanQueryResult? Lan);

/// <summary>
/// Works out how a Bedrock server can be reached right now by querying all transports in parallel:
/// NetherNet HTTP signaling (<c>GET /v1/join</c> over http and https, TCP on the server's port), NetherNet LAN
/// discovery (UDP 7551) and a RakNet ping (UDP on the server's port). Preference: HTTP signaling, then NetherNet
/// LAN, then RakNet. There's no reliable way to tell from outside which protocol a server "really" speaks: a
/// NetherNet server whose TCP port is blocked may still answer RakNet pings on its UDP port, so the result is the
/// best transport that answered, re-checked on every poll for <see cref="UpstreamKind.Auto"/> servers.
/// </summary>
internal static class UpstreamDetector
{
    public static async Task<DetectionResult?> DetectAsync(RelayServer server, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout + TimeSpan.FromSeconds(3));

        var address = await UdpSockets.ResolveAsync(server.Host, cts.Token).ConfigureAwait(false);
        var plain = server.GetHttpClient(https: false);
        var secure = server.GetHttpClient(https: true);

        var httpTask = TryAsync(() => plain.GetInfoAsync(cts.Token));
        var httpsTask = TryAsync(() => secure.GetInfoAsync(cts.Token));
        var lanPort = server.PortSpecified && server.Port == DiscoveryPacketCodec.DefaultPort ? server.Port : DiscoveryPacketCodec.DefaultPort;
        var lanTask = TryAsync(() => NetherNetLanQuery.QueryAsync(new IPEndPoint(address, lanPort), timeout, cts.Token));
        var rakNetTask = TryAsync(() => RakNetPinger.PingAsync(server.Host, server.Port, timeout, cts.Token));

        await Task.WhenAll(httpTask, httpsTask, lanTask, rakNetTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (httpTask.Result is { } info)
        {
            return new DetectionResult(UpstreamKind.NetherNetHttp, ServerStatus.FromHttp(info), plain, null);
        }

        if (httpsTask.Result is { } secureInfo)
        {
            return new DetectionResult(UpstreamKind.NetherNetHttp, ServerStatus.FromHttp(secureInfo), secure, null);
        }

        if (lanTask.Result is { } lan)
        {
            return new DetectionResult(UpstreamKind.NetherNetLan, ServerStatus.FromLan(lan.ServerData), null, lan);
        }

        if (rakNetTask.Result is { } motd)
        {
            return new DetectionResult(UpstreamKind.RakNet, ServerStatus.FromMotd(motd), null, null);
        }

        return null;
    }

    public static string Describe(UpstreamKind kind, RelayServer server) => kind switch
    {
        UpstreamKind.NetherNetHttp => $"NetherNet server (HTTP signaling at {server.HttpClient?.BaseUri})",
        UpstreamKind.NetherNetLan => "NetherNet LAN host (UDP 7551)",
        UpstreamKind.RakNet => $"RakNet server (UDP {server.Port})",
        _ => kind.ToString(),
    };

    private static async Task<T?> TryAsync<T>(Func<Task<T?>> probe) where T : class
    {
        try
        {
            return await probe().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null; // refused, TLS failure, timeout, unreachable: not this transport
        }
    }
}
