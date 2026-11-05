using System.Globalization;
using BedrockLan.Internal;
using BedrockLan.Protocol;

namespace BedrockLan.Bedrock;

/// <summary>
/// Transport-agnostic Bedrock login state machine that answers just enough of the handshake to send a
/// Transfer packet. The transport hands in raw batches (for RakNet: the bytes after 0xFE; for NetherNet:
/// the reassembled data channel message) and sends replies through the <c>sendBatch</c> callback.
/// No encryption is negotiated (the server may skip ServerToClientHandshake).
/// </summary>
/// <remarks>
/// The conversation itself (which client packet each stage waits for, what is sent back, when to transfer) and
/// every packet's layout come from a <see cref="ProtocolConfig"/> (<see cref="TransferOptions.Protocol"/>, read
/// once when the session starts), so they can be updated without a new build. Only batching, compression and the
/// Transfer / offline hand-off are fixed here.
/// </remarks>
public sealed class TransferSession
{
    private readonly object _sync = new();
    private readonly string _peer;
    private readonly Func<TransferTarget?> _target;
    private readonly TransferOptions _options;
    private readonly ProtocolConfig _config;
    private readonly Action<byte[]> _sendBatch;
    private readonly Action<TimeSpan> _closeAfter;
    private readonly LanLogger? _log;
    private readonly Action<TransferTarget, int>? _transferred;
    private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);

    private StageSpec? _stage; // null once finished
    private int _stageEntry;   // counts stage changes, so a stale timeout doesn't fire in a later visit
    private bool _compressed;
    private int _protocol;
    private TransferTarget? _transferTarget;

    /// <param name="peer">Description of the remote peer, for logs.</param>
    /// <param name="target">Resolves the destination at transfer time (null = offline).</param>
    /// <param name="options">Transfer behaviour, including the protocol config to follow.</param>
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
        _config = options.Protocol;
        _sendBatch = sendBatch;
        _closeAfter = closeAfter;
        _log = log;
        lock (_sync)
        {
            Enter(_config.Session.Stages[_config.Session.Start]);
        }
    }

    public int ClientProtocol => _protocol;

    public bool IsFinished => _stage is null;

    /// <summary>The config this session follows (fixed for its lifetime).</summary>
    public ProtocolConfig Protocol => _config;

    public void HandleBatch(byte[] data)
    {
        lock (_sync)
        {
            if (_stage is null)
            {
                LogIgnoredAfterTransfer(data);
                return;
            }

            try
            {
                foreach (var packet in BatchCodec.Split(Decompress(data)))
                {
                    HandlePacket(packet);
                    if (_stage is null)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is PacketFormatException or InvalidDataException)
            {
                _log.Warn($"{_peer}: unreadable game data ({ex.Message}); the client may be too old (pre-1.19.30) or unsupported.");
                _stage = null;
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

        if (_protocol < _config.Session.CompressionHeaderSince)
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

    // ---- Stages ------------------------------------------------------------------------------------

    private void HandlePacket(byte[] packet)
    {
        var stage = _stage!;
        var reader = new ByteReader(packet);
        var id = (int)(reader.VarUInt32() & 0x3FF);
        _log.Debug($"{_peer}: <- packet 0x{id:X2} ({packet.Length} bytes) while {stage.Name}: {Hex(packet)}");
        if (id != stage.ExpectPacket)
        {
            return; // ClientCacheStatus, duplicates, etc. are not interesting
        }

        // Read everything first: a value that doesn't match means this isn't the packet the stage waits for.
        var stored = new List<(string Name, object Value)>();
        foreach (var read in stage.Read)
        {
            if (!read.Range.Contains(_protocol))
            {
                continue;
            }

            var value = Read(reader, read.Type);
            if (read.Match is { } match)
            {
                if (!match.Any(m => Same(m, value)))
                {
                    _log.Debug($"{_peer}: packet 0x{id:X2} ignored while {stage.Name} ({value} isn't one of {string.Join(", ", match)}).");
                    return;
                }

                continue;
            }

            if (read.Keep && IsSet(read.Variable!))
            {
                continue;
            }

            if (read.ClampMin is { } min && read.ClampMax is { } max && value is long n)
            {
                value = Math.Clamp(n, min, max);
            }

            stored.Add((read.Variable!, value));
        }

        foreach (var (name, value) in stored)
        {
            Set(name, value);
        }

        RunSteps(stage.Steps);
    }

    /// <summary>Runs steps in order until one ends the stage (goto / transfer / close).</summary>
    private void RunSteps(IReadOnlyList<StepSpec> steps)
    {
        var context = new ConditionContext(_options.Timing.ToString(), _protocol, () => _target() is not null, Resolve);
        foreach (var step in steps)
        {
            if (step.If is { } condition && !condition.Evaluate(context))
            {
                continue;
            }

            if (step.Log is { } log)
            {
                _log.Debug($"{_peer}: {log}");
            }

            foreach (var name in step.Send)
            {
                Send(name);
            }

            if (step.Compression)
            {
                _compressed = true;
            }

            switch (step.Ending)
            {
                case StepEnding.Goto:
                    Enter(_config.Session.Stages[step.Goto!]);
                    return;
                case StepEnding.Transfer:
                    SendTransfer();
                    return;
                case StepEnding.Close:
                    _stage = null;
                    _closeAfter(TimeSpan.Zero);
                    return;
            }
        }
    }

    private void Enter(StageSpec stage)
    {
        _stage = stage;
        var entry = ++_stageEntry;
        if (stage.Timeout is { } timeout)
        {
            var seconds = timeout.Seconds.Variable is { } name ? ToDouble(Resolve(name)) : (double)(timeout.Seconds.Literal ?? 0d);
            _ = RunTimeoutAsync(stage, entry, TimeSpan.FromSeconds(Math.Max(0, seconds)));
        }
    }

    private async Task RunTimeoutAsync(StageSpec stage, int entry, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        lock (_sync)
        {
            if (ReferenceEquals(_stage, stage) && _stageEntry == entry)
            {
                RunSteps(stage.Timeout!.Steps);
            }
        }
    }

    // ---- Variables ---------------------------------------------------------------------------------

    private bool IsSet(string name) => name == "protocol" ? _protocol != 0 : _variables.ContainsKey(name);

    private void Set(string name, object value)
    {
        if (name == "protocol")
        {
            _protocol = (int)ToLong(value);
            _log.Debug($"{_peer}: client protocol {_protocol}.");
            return;
        }

        _variables[name] = value;
    }

    private object? Resolve(string name) => name switch
    {
        "protocol" => _protocol,
        "target.host" => (_transferTarget ?? _target())?.Host ?? "",
        "target.port" => (_transferTarget ?? _target())?.Port ?? 0,
        "worldName" => _options.LoadingWorldName,
        "offlineMessage" => _options.OfflineMessage,
        "spawnTimeout" => _options.SpawnTimeout.TotalSeconds,
        "uuid.random" => Guid.NewGuid().ToString(),
        "timing" => _options.Timing.ToString(),
        _ => _variables.GetValueOrDefault(name),
    };

    private static object Read(ByteReader reader, FieldType type) => type switch
    {
        FieldType.Byte => (long)reader.Byte(),
        FieldType.Bool => reader.Bool(),
        FieldType.UInt16LE => (long)reader.UInt16LE(),
        FieldType.Int32LE => (long)reader.Int32LE(),
        FieldType.Int32BE => (long)reader.Int32BE(),
        FieldType.UInt32LE => (long)reader.UInt32LE(),
        FieldType.VarInt32 => (long)reader.VarInt32(),
        FieldType.VarUInt32 => (long)reader.VarUInt32(),
        FieldType.String => reader.String(),
        _ => throw new PacketFormatException($"{type} can't be read."),
    };

    /// <summary>The start of a packet as hex, for debug logs (Login is ~30 KB; its start is enough).</summary>
    private static string Hex(byte[] packet) =>
        packet.Length <= 64 ? Convert.ToHexString(packet) : Convert.ToHexString(packet, 0, 64) + $"... (+{packet.Length - 64} bytes)";

    private static bool Same(object expected, object actual) => (expected, actual) switch
    {
        (long a, long b) => a == b,
        (bool a, bool b) => a == b,
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        _ => false,
    };

    private static long ToLong(object? value) => value switch
    {
        long l => l,
        int i => i,
        IConvertible c => c.ToInt64(CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0,
    };

    // ---- Sending -----------------------------------------------------------------------------------

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

    /// <summary>Sends the config's transfer packet (or its offline packet when there's no destination), then closes.</summary>
    private void SendTransfer()
    {
        var target = _target();
        if (target is { } t)
        {
            _transferTarget = t;
            _log.Info($"{_peer}: transferring to {t} (protocol {_protocol}).");
            Send(_config.Session.TransferPacket);
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
            Send(_config.Session.OfflinePacket);
        }

        _stage = null;
        _closeAfter(_options.DisconnectDelay);
    }

    private void Send(string packetName)
    {
        var packet = PacketBuilder.Build(_config.Packets[packetName], _protocol, Resolve);
        var batch = BatchCodec.Join(packet);
        byte[] wire;
        if (!_compressed)
        {
            wire = batch;
        }
        else if (_protocol >= _config.Session.CompressionHeaderSince)
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
