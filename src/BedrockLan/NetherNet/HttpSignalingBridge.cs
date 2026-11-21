// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Collections.Concurrent;
using System.Globalization;

namespace BedrockLan.NetherNet;

/// <summary>
/// Bridges LAN signaling (UDP 7551 messages, possibly trickle ICE) to an upstream server's NetherNet HTTP
/// signaling endpoint. The client's offer is forwarded unchanged (identity assertion included) apart from
/// folding trickled candidates into it; the upstream's answer is relayed back. The client then opens its
/// WebRTC connection directly to the upstream server: no game traffic passes through this process.
/// </summary>
public sealed class HttpSignalingBridge : INetherNetSignalHandler
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromSeconds(60);

    private readonly Func<IReadOnlyList<HttpSignalingClient>> _clients;
    private readonly string _name;
    private readonly Func<TimeSpan> _candidateWindow;
    private readonly Func<bool> _trickleAnswerCandidates;
    private readonly LanLogger? _log;
    private readonly Action<IPEndPoint>? _relayed;
    private readonly Action<HttpSignalingClient>? _answeredBy;
    private readonly Func<NetherNetClientMessage, ulong, Exception, bool>? _failed;
    private readonly TimeSpan? _joinTimeout;
    private readonly ConcurrentDictionary<(ulong ClientId, ulong ConnectionId), PendingOffer> _pending = new();

    /// <param name="client">Upstream HTTP signaling client.</param>
    /// <param name="name">Name used in logs.</param>
    /// <param name="candidateWindow">How long to collect trickled client candidates before posting an incomplete offer.</param>
    /// <param name="trickleAnswerCandidates">Also send each answer candidate as a CANDIDATEADD (harmless duplicates, helps trickle-only peers).</param>
    /// <param name="log">Optional logger.</param>
    public HttpSignalingBridge(HttpSignalingClient client, string name, TimeSpan candidateWindow, bool trickleAnswerCandidates, LanLogger? log)
        : this(() => client, name, () => candidateWindow, () => trickleAnswerCandidates, log)
    {
    }

    /// <param name="client">Returns the current upstream HTTP signaling client (it may change after re-detection).</param>
    /// <param name="name">Name used in logs.</param>
    /// <param name="candidateWindow">How long to collect trickled client candidates before posting an incomplete offer.</param>
    /// <param name="trickleAnswerCandidates">Also send each answer candidate as a CANDIDATEADD.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="relayed">Called after the upstream accepted a join and its answer was delivered to the client.</param>
    public HttpSignalingBridge(Func<HttpSignalingClient?> client, string name, Func<TimeSpan> candidateWindow, Func<bool> trickleAnswerCandidates, LanLogger? log, Action<IPEndPoint>? relayed = null)
        : this(() => client() is { } c ? new[] { c } : System.Array.Empty<HttpSignalingClient>(), name, candidateWindow, trickleAnswerCandidates, log, relayed)
    {
    }

    /// <param name="clients">
    /// Upstream signaling endpoints to try in order, e.g. the detected scheme first and the other scheme second. The
    /// next one is only tried when a request didn't get an HTTP response at all (refused, TLS failure, timeout); an
    /// HTTP error from the server is its answer.
    /// </param>
    /// <param name="name">Name used in logs.</param>
    /// <param name="candidateWindow">How long to collect trickled client candidates before posting an incomplete offer.</param>
    /// <param name="trickleAnswerCandidates">Also send each answer candidate as a CANDIDATEADD.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="relayed">Called after the upstream accepted a join and its answer was delivered to the client.</param>
    /// <param name="answeredBy">Called with the endpoint that answered (so the caller can prefer it next time).</param>
    /// <param name="failed">
    /// Called when the join couldn't be relayed: the client's message, the connection id and the error. Return true
    /// if it was handled (e.g. answered another way); otherwise the client gets a signaling error.
    /// </param>
    /// <param name="joinTimeout">Give up on an endpoint after this long (default: the client's own timeout).</param>
    public HttpSignalingBridge(
        Func<IReadOnlyList<HttpSignalingClient>> clients,
        string name,
        Func<TimeSpan> candidateWindow,
        Func<bool> trickleAnswerCandidates,
        LanLogger? log,
        Action<IPEndPoint>? relayed = null,
        Action<HttpSignalingClient>? answeredBy = null,
        Func<NetherNetClientMessage, ulong, Exception, bool>? failed = null,
        TimeSpan? joinTimeout = null)
    {
        _clients = clients;
        _name = name;
        _candidateWindow = candidateWindow;
        _trickleAnswerCandidates = trickleAnswerCandidates;
        _log = log;
        _relayed = relayed;
        _answeredBy = answeredBy;
        _failed = failed;
        _joinTimeout = joinTimeout;
    }

    public void OnClientMessage(NetherNetClientMessage message)
    {
        if (!NetherNetSignal.TryParse(message.Data, out var signal))
        {
            return; // "Ping" keep-alives and anything unknown
        }

        PruneStale();
        var key = (message.ClientId, signal.ConnectionId);
        switch (signal.Type)
        {
            case NetherNetSignal.Offer:
            {
                var pending = new PendingOffer(signal.Data);
                _pending[key] = pending;
                _ = Task.Run(() => CompleteAsync(message, signal.ConnectionId, key, pending));
                break;
            }

            case NetherNetSignal.Candidate:
                if (_pending.TryGetValue(key, out var existing))
                {
                    lock (existing)
                    {
                        if (!existing.Posted)
                        {
                            existing.Candidates.Add(signal.Data);
                        }
                    }
                }

                break;

            case NetherNetSignal.Error:
                _pending.TryRemove(key, out _);
                _log.Debug($"[{_name}] client {message.ClientId} reported signaling error {signal.Data}.");
                break;
        }
    }

    /// <summary>Whether an error means the server itself refused the join (an HTTP error response).</summary>
    public static bool IsRejection(Exception ex) => ex is HttpRequestException { StatusCode: not null };

    private async Task CompleteAsync(NetherNetClientMessage message, ulong connectionId, (ulong, ulong) key, PendingOffer pending)
    {
        try
        {
            if (!Sdp.HasCompleteCandidates(pending.Offer))
            {
                await Task.Delay(_candidateWindow()).ConfigureAwait(false);
            }

            string offer;
            int trickled;
            lock (pending)
            {
                pending.Posted = true;
                trickled = pending.Candidates.Count;
                offer = Sdp.AddCandidates(pending.Offer, pending.Candidates);
            }

            var clientId = message.ClientId.ToString(CultureInfo.InvariantCulture);
            var hasIdentity = offer.Contains("a=identity:", StringComparison.Ordinal);
            var (answer, answeredBy) = await JoinAsync(clientId, offer, trickled, hasIdentity, message).ConfigureAwait(false);

            Reply(message, new NetherNetSignal(NetherNetSignal.Answer, connectionId, answer));
            if (_trickleAnswerCandidates())
            {
                foreach (var candidate in Sdp.ExtractCandidates(answer))
                {
                    Reply(message, new NetherNetSignal(NetherNetSignal.Candidate, connectionId, candidate));
                }
            }

            _answeredBy?.Invoke(answeredBy);
            _relayed?.Invoke(message.ClientEndPoint);
            _log.Info($"[{_name}] server accepted the join; answer delivered to {message.ClientEndPoint}, client now connects directly.");
        }
        catch (Exception ex)
        {
            var reason = ex is TaskCanceledException ? "timed out" : ex.Message;
            if (_failed is not null && _failed(message, connectionId, ex))
            {
                _log.Info($"[{_name}] signaling relay for {message.ClientEndPoint} failed ({reason}); handled another way.");
            }
            else
            {
                var hint = ex.Message.Contains("401", StringComparison.Ordinal) || ex.Message.TrimEnd().EndsWith(": 37", StringComparison.Ordinal)
                    ? " (the server requires a player identity the LAN offer doesn't provide; use transfer mode)"
                    : "";
                _log.Warn($"[{_name}] signaling relay failed for {message.ClientEndPoint}: {reason}{hint}");
                Reply(message, NetherNetSignal.ErrorSignal(connectionId, NetherNetSignal.ErrorFailedToCreateAnswer));
            }
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<(ulong, ulong), PendingOffer>(key, pending));
        }
    }

    /// <summary>Posts the offer to each endpoint in turn until one gives an HTTP response.</summary>
    private async Task<(string Answer, HttpSignalingClient Client)> JoinAsync(string clientId, string offer, int trickled, bool hasIdentity, NetherNetClientMessage message)
    {
        var clients = _clients();
        if (clients.Count == 0)
        {
            throw new InvalidOperationException("upstream signaling endpoint is not known yet");
        }

        Exception? last = null;
        foreach (var client in clients)
        {
            _log.Info($"[{_name}] relaying join from {message.ClientEndPoint} (network id {clientId}, {Sdp.ExtractCandidates(offer).Count} candidate(s), {trickled} trickled, identity {(hasIdentity ? "present" : "absent")}) to {client.BaseUri}.");
            using var timeout = _joinTimeout is { } t ? new CancellationTokenSource(t) : null;
            try
            {
                return (await client.JoinAsync(clientId, offer, timeout?.Token ?? default).ConfigureAwait(false), client);
            }
            catch (Exception ex) when (!IsRejection(ex) && ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // No HTTP response (refused, TLS failure, timeout): the next endpoint (other scheme) may work.
                last = ex;
                _log.Debug($"[{_name}] {client.BaseUri} didn't answer ({(ex is TaskCanceledException ? "timed out" : ex.Message)}).");
            }
        }

        throw last!;
    }

    private static void Reply(NetherNetClientMessage message, NetherNetSignal signal) =>
        message.Hub.SendMessage(message.AdvertisementId, message.ClientId, signal.ToString(), message.ClientEndPoint);

    private void PruneStale()
    {
        var cutoff = DateTime.UtcNow - PendingLifetime;
        foreach (var entry in _pending)
        {
            if (entry.Value.Created < cutoff)
            {
                _pending.TryRemove(entry);
            }
        }
    }

    private sealed class PendingOffer
    {
        public PendingOffer(string offer)
        {
            Offer = offer;
        }

        public string Offer { get; }

        public List<string> Candidates { get; } = new();

        public bool Posted { get; set; }

        public DateTime Created { get; } = DateTime.UtcNow;
    }
}
