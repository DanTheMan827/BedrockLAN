using System.Collections.Concurrent;
using BedrockLan.Bedrock;
using BedrockLan.NetherNet;
using SIPSorcery.Net;

namespace BedrockLan.WebRtc;

/// <summary>
/// A minimal NetherNet server: accepts the client's WebRTC connection (answering LAN signaling with a signed
/// SDP answer), runs the Bedrock login just far enough, then sends a Transfer packet. Use it for upstreams the
/// client cannot be pointed at directly over NetherNet signaling (e.g. RakNet servers for 26.50-era clients),
/// or when <see cref="NetherNetConnectMode.Transfer"/> is preferred over signaling relay. Normally created via
/// <see cref="WebRtcTransfer.CreateFactory"/> (BedrockRelay picks that up automatically).
/// </summary>
public sealed class WebRtcTransferHandler : INetherNetSignalHandler, IDisposable
{
    private readonly NetherNetHandlerContext _context;
    private readonly NetherNetServerIdentity _identity;
    private readonly ConcurrentDictionary<(ulong ClientId, ulong ConnectionId), WebRtcTransferPeer> _peers = new();

    public WebRtcTransferHandler(NetherNetHandlerContext context, NetherNetServerIdentity identity)
    {
        _context = context;
        _identity = identity;
    }

    /// <summary>Convenience factory for <see cref="BedrockRelayOptions.TransferHandlerFactory"/>.</summary>
    public static Func<NetherNetHandlerContext, INetherNetSignalHandler> Factory(NetherNetServerIdentity identity) =>
        context => new WebRtcTransferHandler(context, identity);

    public void OnClientMessage(NetherNetClientMessage message)
    {
        if (!NetherNetSignal.TryParse(message.Data, out var signal))
        {
            return;
        }

        var key = (message.ClientId, signal.ConnectionId);
        switch (signal.Type)
        {
            case NetherNetSignal.Offer:
            {
                var peer = new WebRtcTransferPeer(_context, _identity, message, signal.ConnectionId);
                if (_peers.TryRemove(key, out var previous))
                {
                    previous.Close();
                }

                _peers[key] = peer;
                peer.Closed += () => _peers.TryRemove(new KeyValuePair<(ulong, ulong), WebRtcTransferPeer>(key, peer));
                _ = Task.Run(() => peer.StartAsync(signal.Data)); // keep the discovery loop non-blocking
                break;
            }

            case NetherNetSignal.Candidate:
                if (_peers.TryGetValue(key, out var existing))
                {
                    existing.AddRemoteCandidate(signal.Data);
                }

                break;

            case NetherNetSignal.Error:
                if (_peers.TryRemove(key, out var failed))
                {
                    failed.Close();
                }

                break;
        }
    }

    public void Dispose()
    {
        foreach (var (_, peer) in _peers)
        {
            peer.Close();
        }

        _peers.Clear();
    }
}

internal sealed class WebRtcTransferPeer
{
    private const string ReliableChannelLabel = "ReliableDataChannel";
    private const int MaxSegmentPayload = 16 * 1024;

    private readonly NetherNetHandlerContext _context;
    private readonly NetherNetServerIdentity _identity;
    private readonly NetherNetClientMessage _origin;
    private readonly ulong _connectionId;
    private readonly object _sync = new();
    private readonly List<string> _queuedCandidates = new();
    private readonly MemoryStream _reassembly = new();
    private readonly TransferSession _session;
    private RTCPeerConnection? _peer;
    private RTCDataChannel? _reliable;
    private string _mid = "0";
    private bool _remoteDescriptionSet;
    private bool _closed;

    public WebRtcTransferPeer(NetherNetHandlerContext context, NetherNetServerIdentity identity, NetherNetClientMessage origin, ulong connectionId)
    {
        _context = context;
        _identity = identity;
        _origin = origin;
        _connectionId = connectionId;
        _session = new TransferSession(
            $"[{context.Server.Name}] NetherNet {origin.ClientEndPoint}",
            context.Target,
            context.TransferOptions,
            SendBatch,
            delay => _ = CloseLaterAsync(delay),
            context.Logger,
            context.Transferred is null ? null : (target, protocol) => context.Transferred(origin.ClientEndPoint, target, protocol));
    }

    public event Action? Closed;

    public async Task StartAsync(string offer)
    {
        try
        {
            var peer = new RTCPeerConnection(new RTCConfiguration { iceServers = new List<RTCIceServer>() });
            lock (_sync)
            {
                _peer = peer;
            }

            peer.ondatachannel += OnDataChannel;
            peer.onconnectionstatechange += state =>
            {
                Log(LanLogLevel.Debug, $"WebRTC connection {state} ({Elapsed}).");
                if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                {
                    Close();
                }
            };

            _mid = Sdp.GetMid(offer) ?? "0";
            var result = peer.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = Sdp.StripIdentity(offer),
            });
            if (result != SetDescriptionResultEnum.OK)
            {
                Log(LanLogLevel.Warning, $"rejected offer ({result}).");
                Signal(NetherNetSignal.ErrorSignal(_connectionId, NetherNetSignal.ErrorFailedToSetRemoteDescription));
                Close();
                return;
            }

            // Candidates embedded in the offer are applied by setRemoteDescription; trickled ones may have queued.
            List<string> queued;
            lock (_sync)
            {
                _remoteDescriptionSet = true;
                queued = new List<string>(_queuedCandidates);
                _queuedCandidates.Clear();
            }

            foreach (var candidate in queued)
            {
                AddRemoteCandidate(candidate);
            }

            var answer = peer.createAnswer(null);
            await peer.setLocalDescription(answer).ConfigureAwait(false);

            var signedAnswer = _identity.SignAnswer(answer.sdp);
            Signal(new NetherNetSignal(NetherNetSignal.Answer, _connectionId, signedAnswer));
            foreach (var candidate in Sdp.ExtractCandidates(signedAnswer))
            {
                Signal(new NetherNetSignal(NetherNetSignal.Candidate, _connectionId, candidate));
            }

            Log(LanLogLevel.Debug, "answered offer; waiting for data channels.");
        }
        catch (Exception ex)
        {
            Log(LanLogLevel.Warning, $"WebRTC setup failed: {ex.Message}");
            Signal(NetherNetSignal.ErrorSignal(_connectionId, NetherNetSignal.ErrorFailedToCreateAnswer));
            Close();
        }
    }

    public void AddRemoteCandidate(string candidate)
    {
        var normalized = Sdp.NormalizeCandidate(candidate);
        if (normalized is null)
        {
            return;
        }

        RTCPeerConnection? peer;
        lock (_sync)
        {
            if (!_remoteDescriptionSet)
            {
                _queuedCandidates.Add(normalized);
                return;
            }

            peer = _peer;
        }

        try
        {
            peer?.addIceCandidate(new RTCIceCandidateInit { candidate = normalized, sdpMid = _mid, sdpMLineIndex = 0 });
        }
        catch (Exception ex)
        {
            Log(LanLogLevel.Debug, $"ignored remote candidate: {ex.Message}");
        }
    }

    private void OnDataChannel(RTCDataChannel channel)
    {
        if (channel.label != ReliableChannelLabel)
        {
            return; // UnreliableDataChannel carries nothing we need
        }

        lock (_sync)
        {
            _reliable = channel;
        }

        Log(LanLogLevel.Debug, $"data channel '{channel.label}' opened by client ({Elapsed}).");
        channel.onclose += () => Log(LanLogLevel.Debug, $"data channel closed ({Elapsed}); transfer sent: {_session.IsFinished}.");
        channel.onmessage += (_, _, data) => OnReliableMessage(data);
    }

    private void OnReliableMessage(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        byte[]? complete = null;
        var tooLarge = false;
        lock (_sync)
        {
            // NetherNet framing: first byte = number of segments still to follow.
            _reassembly.Write(data, 1, data.Length - 1);
            if (_reassembly.Length > BatchCodec.MaxDecompressedSize)
            {
                _reassembly.SetLength(0);
                tooLarge = true;
            }
            else if (data[0] == 0)
            {
                complete = _reassembly.ToArray();
                _reassembly.SetLength(0);
            }
        }

        if (tooLarge)
        {
            Log(LanLogLevel.Warning, "oversized message; closing.");
            _ = CloseLaterAsync(TimeSpan.Zero);
            return;
        }

        if (complete is not null)
        {
            _session.HandleBatch(complete); // NetherNet batches have no 0xFE header and are never encrypted
        }
    }

    private void SendBatch(byte[] batch)
    {
        RTCDataChannel? channel;
        lock (_sync)
        {
            channel = _reliable;
        }

        if (channel is null)
        {
            return;
        }

        var segments = Math.Max(1, (batch.Length + MaxSegmentPayload - 1) / MaxSegmentPayload);
        if (segments > 255)
        {
            Log(LanLogLevel.Warning, "batch too large to frame; dropped.");
            return;
        }

        for (var i = 0; i < segments; i++)
        {
            var offset = i * MaxSegmentPayload;
            var length = Math.Min(MaxSegmentPayload, batch.Length - offset);
            var frame = new byte[length + 1];
            frame[0] = (byte)(segments - 1 - i);
            Buffer.BlockCopy(batch, offset, frame, 1, length);
            channel.send(frame);
        }
    }

    private async Task CloseLaterAsync(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
        else
        {
            await Task.Yield(); // never close from inside SIPSorcery's receive callback
        }

        Close();
    }

    public void Close()
    {
        RTCPeerConnection? peer;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            Log(LanLogLevel.Debug, $"closing WebRTC peer ({Elapsed}).");

            _closed = true;
            peer = _peer;
        }

        try
        {
            peer?.close();
        }
        catch
        {
            // Best effort.
        }

        Closed?.Invoke();
    }

    private void Signal(NetherNetSignal signal) =>
        _origin.Hub.SendMessage(_origin.AdvertisementId, _origin.ClientId, signal.ToString(), _origin.ClientEndPoint);

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private string Elapsed => $"+{_clock.Elapsed.TotalSeconds:0.0}s";

    private void Log(LanLogLevel level, string message) =>
        _context.Logger?.Invoke(level, $"[{_context.Server.Name}] NetherNet {_origin.ClientEndPoint}: {message}");
}
