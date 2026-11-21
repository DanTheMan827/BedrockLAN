// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace BedrockLan.NetherNet;

/// <summary>
/// Relays LAN signaling between a client and an upstream NetherNet LAN host that the client cannot discover
/// itself (for example a world hosted on another subnet or over a VPN). Each client gets its own relay
/// network id towards the upstream. Only signaling is relayed; the WebRTC connection is direct.
/// </summary>
public sealed class LanSignalingBridge : INetherNetSignalHandler, IDisposable
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(2);

    private readonly NetherNetLanDiscovery _hub;
    private readonly Func<(ulong NetworkId, IPEndPoint EndPoint)?> _upstream;
    private readonly string _name;
    private readonly LanLogger? _log;
    private readonly Action<IPEndPoint>? _relayed;
    private readonly Dictionary<ulong, Session> _sessions = new();
    private readonly Timer _cleanup;

    public LanSignalingBridge(NetherNetLanDiscovery hub, Func<(ulong NetworkId, IPEndPoint EndPoint)?> upstream, string name, LanLogger? log, Action<IPEndPoint>? relayed = null)
    {
        _relayed = relayed;
        _hub = hub;
        _upstream = upstream;
        _name = name;
        _log = log;
        _cleanup = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void OnClientMessage(NetherNetClientMessage message)
    {
        var upstream = _upstream();
        if (upstream is null)
        {
            if (NetherNetSignal.TryParse(message.Data, out var signal) && signal.Type == NetherNetSignal.Offer)
            {
                message.Hub.SendMessage(message.AdvertisementId, message.ClientId,
                    NetherNetSignal.ErrorSignal(signal.ConnectionId, NetherNetSignal.ErrorDestinationNotLoggedIn).ToString(),
                    message.ClientEndPoint);
            }

            return;
        }

        Session session;
        lock (_sessions)
        {
            if (_sessions.TryGetValue(message.ClientId, out var existing))
            {
                session = existing;
            }
            else
            {
                var created = new Session(message.ClientId, message.AdvertisementId, message.ClientEndPoint);
                created.RelayId = _hub.RegisterRelay(m => OnUpstreamMessage(created, m));
                _sessions[message.ClientId] = created;
                session = created;
                _log.Info($"[{_name}] relaying LAN signaling for {message.ClientEndPoint} via relay id {created.RelayId}.");
            }

            session.ClientEndPoint = message.ClientEndPoint;
            session.LastActivity = DateTime.UtcNow;
        }

        _hub.SendMessage(session.RelayId, upstream.Value.NetworkId, message.Data, upstream.Value.EndPoint);
        if (NetherNetSignal.TryParse(message.Data, out var forwarded) && forwarded.Type == NetherNetSignal.Offer)
        {
            _relayed?.Invoke(message.ClientEndPoint);
        }
    }

    private void OnUpstreamMessage(Session session, NetherNetRelayMessage message)
    {
        var upstream = _upstream();
        if (upstream is null || upstream.Value.NetworkId != message.SenderId)
        {
            return;
        }

        IPEndPoint clientEndPoint;
        lock (_sessions)
        {
            session.LastActivity = DateTime.UtcNow;
            clientEndPoint = session.ClientEndPoint;
        }

        _hub.SendMessage(session.AdvertisementId, session.ClientId, message.Data, clientEndPoint);
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - SessionLifetime;
        lock (_sessions)
        {
            foreach (var (clientId, session) in _sessions.ToList())
            {
                if (session.LastActivity < cutoff)
                {
                    _hub.UnregisterRelay(session.RelayId);
                    _sessions.Remove(clientId);
                }
            }
        }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        lock (_sessions)
        {
            foreach (var session in _sessions.Values)
            {
                _hub.UnregisterRelay(session.RelayId);
            }

            _sessions.Clear();
        }
    }

    private sealed class Session
    {
        public Session(ulong clientId, ulong advertisementId, IPEndPoint clientEndPoint)
        {
            ClientId = clientId;
            AdvertisementId = advertisementId;
            ClientEndPoint = clientEndPoint;
            LastActivity = DateTime.UtcNow;
        }

        public ulong ClientId { get; }

        public ulong AdvertisementId { get; }

        public ulong RelayId { get; set; }

        public IPEndPoint ClientEndPoint { get; set; }

        public DateTime LastActivity { get; set; }
    }
}
