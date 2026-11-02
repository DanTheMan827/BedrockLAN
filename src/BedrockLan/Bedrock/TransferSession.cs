using BedrockLan.Internal;

namespace BedrockLan.Bedrock;

/// <summary>
/// Transport-agnostic Bedrock login state machine that answers just enough of the handshake to send a
/// Transfer packet. The transport hands in raw batches (for RakNet: the bytes after 0xFE; for NetherNet:
/// the reassembled data channel message) and sends replies through the <c>sendBatch</c> callback.
/// No encryption is negotiated (the server may skip ServerToClientHandshake).
/// </summary>
public sealed class TransferSession
{
    private enum Stage
    {
        AwaitNetworkSettingsRequest,
        AwaitLogin,
        AwaitPacksResponse,
        AwaitStackResponse,
        AwaitChunkRadiusRequest,
        AwaitSpawned,
        Done,
    }

    private readonly object _sync = new();
    private readonly string _peer;
    private readonly Func<TransferTarget?> _target;
    private readonly TransferOptions _options;
    private readonly Action<byte[]> _sendBatch;
    private readonly Action<TimeSpan> _closeAfter;
    private readonly LanLogger? _log;
    private readonly Action<TransferTarget, int>? _transferred;

    private Stage _stage = Stage.AwaitNetworkSettingsRequest;
    private bool _compressed;
    private int _protocol;

    /// <param name="peer">Description of the remote peer, for logs.</param>
    /// <param name="target">Resolves the destination at transfer time (null = offline).</param>
    /// <param name="options">Transfer behaviour.</param>
    /// <param name="sendBatch">Sends one batch to the client over the transport.</param>
    /// <param name="closeAfter">Asks the transport to close the connection after the given delay.</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="transferred">Called (on the session's thread, under its lock: keep it short) after a Transfer was sent, with the target and client protocol.</param>
    public TransferSession(string peer, Func<TransferTarget?> target, TransferOptions options, Action<byte[]> sendBatch, Action<TimeSpan> closeAfter, LanLogger? log, Action<TransferTarget, int>? transferred = null)
    {
        _transferred = transferred;
        _peer = peer;
        _target = target;
        _options = options;
        _sendBatch = sendBatch;
        _closeAfter = closeAfter;
        _log = log;
    }

    public int ClientProtocol => _protocol;

    public bool IsFinished => _stage == Stage.Done;

    public void HandleBatch(byte[] data)
    {
        lock (_sync)
        {
            if (_stage == Stage.Done)
            {
                LogIgnoredAfterTransfer(data);
                return;
            }

            try
            {
                foreach (var packet in BatchCodec.Split(Decompress(data)))
                {
                    HandlePacket(packet);
                    if (_stage == Stage.Done)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is PacketFormatException or InvalidDataException)
            {
                _log.Warn($"{_peer}: unreadable game data ({ex.Message}); the client may be too old (pre-1.19.30) or unsupported.");
                _stage = Stage.Done;
                _closeAfter(TimeSpan.Zero);
            }
        }
    }

    private byte[] Decompress(byte[] data)
    {
        if (!_compressed)
        {
            return data;
        }

        if (_protocol < BedrockPackets.ProtocolCompressionHeader)
        {
            return BatchCodec.Inflate(data);
        }

        if (data.Length == 0)
        {
            throw new PacketFormatException("Empty batch.");
        }

        return data[0] switch
        {
            0xFF => data[1..],
            0x00 => BatchCodec.Inflate(data.AsSpan(1)),
            _ => throw new PacketFormatException($"Unsupported compression algorithm 0x{data[0]:X2}."),
        };
    }

    private void HandlePacket(byte[] packet)
    {
        var reader = new ByteReader(packet);
        var id = (int)(reader.VarUInt32() & 0x3FF);
        _log.Debug($"{_peer}: <- packet 0x{id:X2} ({packet.Length} bytes) while {_stage}.");

        switch (_stage)
        {
            case Stage.AwaitNetworkSettingsRequest when id == BedrockPackets.IdRequestNetworkSettings:
                _protocol = reader.Int32BE();
                _log.Debug($"{_peer}: client protocol {_protocol}.");
                Send(BedrockPackets.NetworkSettings(256)); // sent uncompressed
                _compressed = true;
                _stage = Stage.AwaitLogin;
                break;

            case Stage.AwaitLogin when id == BedrockPackets.IdLogin:
            {
                var loginProtocol = reader.Int32BE();
                if (_protocol == 0)
                {
                    _protocol = loginProtocol;
                }

                Send(BedrockPackets.PlayStatus(BedrockPackets.PlayStatusLoginSuccess));
                if (_options.Timing != TransferTiming.Immediate && BedrockPackets.SupportsResourcePackStage(_protocol))
                {
                    Send(BedrockPackets.ResourcePacksInfo(_protocol));
                    _stage = Stage.AwaitPacksResponse;
                }
                else
                {
                    SendTransfer();
                }

                break;
            }

            case Stage.AwaitPacksResponse when id == BedrockPackets.IdResourcePackClientResponse:
                Send(BedrockPackets.ResourcePackStack(_protocol));
                _stage = Stage.AwaitStackResponse;
                break;

            case Stage.AwaitStackResponse when id == BedrockPackets.IdResourcePackClientResponse:
                if (_options.Timing == TransferTiming.AfterSpawn && BedrockPackets.SupportsSpawnStage(_protocol))
                {
                    _log.Debug($"{_peer}: starting temporary game.");
                    Send(BedrockPackets.JigsawStructureData());
                    Send(BedrockPackets.VoxelShapes());
                    Send(BedrockPackets.StartGame(_options.LoadingWorldName));
                    Send(BedrockPackets.ItemRegistry());
                    _stage = Stage.AwaitChunkRadiusRequest;
                }
                else
                {
                    if (_options.Timing == TransferTiming.AfterSpawn)
                    {
                        _log.Debug($"{_peer}: no StartGame layout for protocol {_protocol}; transferring before spawn.");
                    }

                    SendTransfer();
                }

                break;

            case Stage.AwaitChunkRadiusRequest when id == BedrockPackets.IdRequestChunkRadius:
            {
                var radius = Math.Clamp(reader.VarInt32(), 1, 8);
                Send(BedrockPackets.ChunkRadiusUpdated(radius));
                Send(BedrockPackets.PlayStatus(BedrockPackets.PlayStatusPlayerSpawn));
                Send(BedrockPackets.CreativeContent());
                _stage = Stage.AwaitSpawned;
                _ = TransferIfSpawnNotConfirmedAsync();
                break;
            }

            case Stage.AwaitSpawned when id == BedrockPackets.IdSetLocalPlayerAsInitialised:
                _log.Debug($"{_peer}: client spawned.");
                SendTransfer();
                break;

            default:
                // ClientCacheStatus, ResourcePackClientResponse duplicates, etc. are not interesting.
                break;
        }
    }

    private async Task TransferIfSpawnNotConfirmedAsync()
    {
        await Task.Delay(_options.SpawnTimeout).ConfigureAwait(false);
        lock (_sync)
        {
            if (_stage == Stage.AwaitSpawned)
            {
                _log.Debug($"{_peer}: no spawn confirmation after {_options.SpawnTimeout.TotalSeconds:0}s; transferring anyway.");
                SendTransfer();
            }
        }
    }

    private void LogIgnoredAfterTransfer(byte[] data)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            foreach (var packet in BatchCodec.Split(Decompress(data)))
            {
                var id = (int)(new ByteReader(packet).VarUInt32() & 0x3FF);
                _log.Debug($"{_peer}: <- packet 0x{id:X2} ({packet.Length} bytes) after transfer.");
            }
        }
        catch (Exception ex) when (ex is PacketFormatException or InvalidDataException)
        {
        }
    }

    private void SendTransfer()
    {
        var target = _target();
        if (target is { } t)
        {
            _log.Info($"{_peer}: transferring to {t} (protocol {_protocol}).");
            Send(BedrockPackets.Transfer(_protocol, t.Host, t.Port));
            try
            {
                _transferred?.Invoke(t, _protocol);
            }
            catch (Exception ex)
            {
                _log.Warn($"{_peer}: transfer callback threw: {ex.Message}");
            }
        }
        else
        {
            _log.Warn($"{_peer}: destination unknown/offline; disconnecting.");
            Send(BedrockPackets.Disconnect(_protocol, _options.OfflineMessage));
        }

        _stage = Stage.Done;
        _closeAfter(_options.DisconnectDelay);
    }

    private void Send(byte[] packet)
    {
        var batch = BatchCodec.Join(packet);
        byte[] wire;
        if (!_compressed)
        {
            wire = batch;
        }
        else if (_protocol >= BedrockPackets.ProtocolCompressionHeader)
        {
            var compressed = BatchCodec.Deflate(batch);
            wire = new byte[compressed.Length + 1];
            wire[0] = 0x00; // flate
            compressed.CopyTo(wire, 1);
        }
        else
        {
            wire = BatchCodec.Deflate(batch);
        }

        _sendBatch(wire);
    }
}
