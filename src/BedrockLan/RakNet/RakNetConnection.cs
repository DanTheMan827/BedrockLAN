// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using BedrockLan.Internal;

namespace BedrockLan.RakNet;

/// <summary>
/// Server side of a single RakNet connection. Implements just enough of the reliability layer
/// (ACK/NACK, resends, split packets, ordering) to complete a Bedrock login and send a transfer.
/// Thread-safe: every entry point takes the same lock.
/// </summary>
internal sealed class RakNetConnection
{
    private const int MaxSplitCount = 1024;
    private const int MaxConcurrentSplits = 64;
    private const int MaxAckRecords = 8192;
    private const int MaxQueuedOrdered = 2048;
    private static readonly TimeSpan ResendAfter = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SplitTimeout = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly Action<byte[], IPEndPoint> _send;
    private readonly long _serverGuid;
    private readonly int _mtu;
    private readonly LanLogger? _log;

    // Sending state.
    private int _sendSequence;
    private int _reliableIndex;
    private int _orderIndex;
    private ushort _splitId;
    private readonly Dictionary<int, PendingDatagram> _unacked = new();
    private readonly List<int> _pendingAcks = new();

    // Receiving state.
    private readonly HashSet<int> _seenReliable = new();
    private int _highestReliable = -1;
    private readonly int[] _nextOrderIndex = new int[32];
    private readonly Dictionary<int, byte[]>[] _orderQueues = CreateOrderQueues();
    private readonly Dictionary<ushort, SplitAssembly> _splits = new();

    public RakNetConnection(IPEndPoint remote, int mtu, long clientGuid, long serverGuid, Action<byte[], IPEndPoint> send, LanLogger? log)
    {
        RemoteEndPoint = remote;
        _mtu = Math.Clamp(mtu, RakNetProtocol.MinMtu, RakNetProtocol.MaxMtu);
        ClientGuid = clientGuid;
        _serverGuid = serverGuid;
        _send = send;
        _log = log;
        LastReceived = DateTime.UtcNow;
    }

    public IPEndPoint RemoteEndPoint { get; }

    public long ClientGuid { get; }

    public DateTime LastReceived { get; private set; }

    public bool IsConnected { get; private set; }

    public bool IsClosed { get; private set; }

    /// <summary>Invoked with the payload of each 0xFE game packet (without the 0xFE byte).</summary>
    public Action<byte[]>? GamePacketReceived { get; set; }

    public event Action? Closed;

    public void HandleDatagram(byte[] data)
    {
        lock (_sync)
        {
            if (IsClosed || data.Length == 0)
            {
                return;
            }

            LastReceived = DateTime.UtcNow;
            var flags = data[0];
            if ((flags & 0x40) != 0)
            {
                foreach (var sequence in ReadRecords(data))
                {
                    _unacked.Remove(sequence);
                }

                return;
            }

            if ((flags & 0x20) != 0)
            {
                foreach (var sequence in ReadRecords(data))
                {
                    if (_unacked.Remove(sequence, out var pending))
                    {
                        SendDatagram(pending.Frames, track: true);
                    }
                }

                return;
            }

            var reader = new ByteReader(data, 1, data.Length - 1);
            _pendingAcks.Add(reader.UInt24LE());
            while (reader.Remaining > 0 && !IsClosed)
            {
                ReadFrame(reader);
            }

            FlushAcks();
        }
    }

    public void Tick(DateTime now)
    {
        lock (_sync)
        {
            if (IsClosed)
            {
                return;
            }

            if (now - LastReceived > IdleTimeout)
            {
                _log.Debug($"RakNet {RemoteEndPoint} timed out.");
                CloseInternal(notifyPeer: false);
                return;
            }

            List<int>? expired = null;
            foreach (var (sequence, pending) in _unacked)
            {
                if (now - pending.SentAt > ResendAfter)
                {
                    (expired ??= new List<int>()).Add(sequence);
                }
            }

            if (expired is not null)
            {
                foreach (var sequence in expired)
                {
                    if (_unacked.Remove(sequence, out var pending))
                    {
                        SendDatagram(pending.Frames, track: true);
                    }
                }
            }

            List<ushort>? staleSplits = null;
            foreach (var (id, split) in _splits)
            {
                if (now - split.Created > SplitTimeout)
                {
                    (staleSplits ??= new List<ushort>()).Add(id);
                }
            }

            if (staleSplits is not null)
            {
                foreach (var id in staleSplits)
                {
                    _splits.Remove(id);
                }
            }
        }
    }

    /// <summary>Sends a Bedrock batch (the bytes that follow 0xFE) reliably and in order.</summary>
    public void SendGamePayload(byte[] batch)
    {
        lock (_sync)
        {
            if (IsClosed)
            {
                return;
            }

            var body = new byte[batch.Length + 1];
            body[0] = RakNetProtocol.GamePacket;
            batch.CopyTo(body, 1);
            SendReliableOrdered(body);
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            if (!IsClosed)
            {
                CloseInternal(notifyPeer: true);
            }
        }
    }

    private void CloseInternal(bool notifyPeer)
    {
        if (notifyPeer)
        {
            SendReliableOrdered(new[] { RakNetProtocol.DisconnectNotification });
        }

        IsClosed = true;
        _unacked.Clear();
        _splits.Clear();
        Closed?.Invoke();
    }

    private void ReadFrame(ByteReader reader)
    {
        var header = reader.Byte();
        var reliability = header >> 5;
        var isSplit = (header & 0x10) != 0;
        var length = (reader.UInt16BE() + 7) >> 3;

        var reliableIndex = -1;
        var orderIndex = 0;
        var orderChannel = 0;
        if (IsReliable(reliability))
        {
            reliableIndex = reader.UInt24LE();
        }

        if (IsSequenced(reliability))
        {
            reader.UInt24LE(); // sequence index; sequenced delivery is not needed here
        }

        if (IsOrdered(reliability) || IsSequenced(reliability))
        {
            orderIndex = reader.UInt24LE();
            orderChannel = reader.Byte() & 31;
        }

        var splitCount = 0;
        ushort splitId = 0;
        var splitIndex = 0;
        if (isSplit)
        {
            splitCount = reader.Int32BE();
            splitId = reader.UInt16BE();
            splitIndex = reader.Int32BE();
        }

        var body = reader.Bytes(length);

        if (reliableIndex >= 0 && !MarkReliable(reliableIndex))
        {
            return; // duplicate (resent) frame
        }

        if (isSplit)
        {
            var assembled = AddSplit(splitId, splitCount, splitIndex, body);
            if (assembled is null)
            {
                return;
            }

            body = assembled;
        }

        if (IsOrdered(reliability))
        {
            DeliverOrdered(orderChannel, orderIndex, body);
        }
        else
        {
            Deliver(body);
        }
    }

    private bool MarkReliable(int index)
    {
        if (!_seenReliable.Add(index))
        {
            return false;
        }

        if (index > _highestReliable)
        {
            _highestReliable = index;
        }

        if (_seenReliable.Count > 8192)
        {
            var floor = _highestReliable - 4096;
            _seenReliable.RemoveWhere(i => i < floor);
        }

        return true;
    }

    private byte[]? AddSplit(ushort id, int count, int index, byte[] fragment)
    {
        if (count <= 0 || count > MaxSplitCount || index < 0 || index >= count)
        {
            _log.Debug($"RakNet {RemoteEndPoint} sent an invalid split ({index}/{count}).");
            return null;
        }

        if (!_splits.TryGetValue(id, out var split))
        {
            if (_splits.Count >= MaxConcurrentSplits)
            {
                return null;
            }

            split = new SplitAssembly(count);
            _splits[id] = split;
        }

        if (split.Parts.Length != count || split.Parts[index] is not null)
        {
            return null;
        }

        split.Parts[index] = fragment;
        split.Received++;
        if (split.Received < count)
        {
            return null;
        }

        _splits.Remove(id);
        var total = 0;
        foreach (var part in split.Parts)
        {
            total += part!.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in split.Parts)
        {
            part!.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private void DeliverOrdered(int channel, int index, byte[] body)
    {
        var expected = _nextOrderIndex[channel];
        if (index < expected)
        {
            return;
        }

        var queue = _orderQueues[channel];
        if (index > expected)
        {
            if (queue.Count >= MaxQueuedOrdered)
            {
                _log.Debug($"RakNet {RemoteEndPoint} ordering queue overflow.");
                CloseInternal(notifyPeer: false);
                return;
            }

            queue[index] = body;
            return;
        }

        Deliver(body);
        expected++;
        while (!IsClosed && queue.Remove(expected, out var next))
        {
            Deliver(next);
            expected++;
        }

        _nextOrderIndex[channel] = expected;
    }

    private void Deliver(byte[] body)
    {
        if (body.Length == 0 || IsClosed)
        {
            return;
        }

        switch (body[0])
        {
            case RakNetProtocol.ConnectionRequest:
                HandleConnectionRequest(body);
                break;
            case RakNetProtocol.NewIncomingConnection:
                IsConnected = true;
                break;
            case RakNetProtocol.ConnectedPing:
            {
                var reader = new ByteReader(body, 1, body.Length - 1);
                var pingTime = reader.Int64BE();
                var writer = new ByteWriter(17);
                writer.Byte(RakNetProtocol.ConnectedPong);
                writer.Int64BE(pingTime);
                writer.Int64BE(RakNetProtocol.Timestamp());
                SendUnreliable(writer.ToArray());
                break;
            }
            case RakNetProtocol.DisconnectNotification:
                _log.Debug($"RakNet {RemoteEndPoint} disconnected.");
                CloseInternal(notifyPeer: false);
                break;
            case RakNetProtocol.GamePacket:
                GamePacketReceived?.Invoke(body.AsSpan(1).ToArray());
                break;
        }
    }

    private void HandleConnectionRequest(byte[] body)
    {
        var reader = new ByteReader(body, 1, body.Length - 1);
        reader.Int64BE(); // client guid
        var requestTime = reader.Int64BE();

        var writer = new ByteWriter(200);
        writer.Byte(RakNetProtocol.ConnectionRequestAccepted);
        RakNetProtocol.WriteAddress(writer, RemoteEndPoint);
        writer.UInt16BE(0); // system index
        for (var i = 0; i < 20; i++)
        {
            RakNetProtocol.WriteAddress(writer, null);
        }

        writer.Int64BE(requestTime);
        writer.Int64BE(RakNetProtocol.Timestamp());
        SendReliableOrdered(writer.ToArray());
    }

    private void SendReliableOrdered(byte[] body)
    {
        // MTU includes 28 bytes of IP/UDP headers; leave room for the datagram (4) and frame (<=20) headers.
        var maxBody = _mtu - 60;
        var orderIndex = _orderIndex;
        _orderIndex = (_orderIndex + 1) & 0xFFFFFF;

        if (body.Length <= maxBody)
        {
            SendDatagram(new List<byte[]> { EncodeFrame(3, body, 0, body.Length, NextReliableIndex(), orderIndex, null) }, track: true);
            return;
        }

        var count = (body.Length + maxBody - 1) / maxBody;
        var splitId = _splitId++;
        for (var i = 0; i < count; i++)
        {
            var offset = i * maxBody;
            var length = Math.Min(maxBody, body.Length - offset);
            var frame = EncodeFrame(3, body, offset, length, NextReliableIndex(), orderIndex, (count, splitId, i));
            SendDatagram(new List<byte[]> { frame }, track: true);
        }
    }

    private void SendUnreliable(byte[] body) =>
        SendDatagram(new List<byte[]> { EncodeFrame(0, body, 0, body.Length, 0, 0, null) }, track: false);

    private int NextReliableIndex()
    {
        var index = _reliableIndex;
        _reliableIndex = (_reliableIndex + 1) & 0xFFFFFF;
        return index;
    }

    private static byte[] EncodeFrame(int reliability, byte[] source, int offset, int length, int reliableIndex, int orderIndex, (int Count, ushort Id, int Index)? split)
    {
        var writer = new ByteWriter(length + 24);
        writer.Byte((byte)((reliability << 5) | (split.HasValue ? 0x10 : 0)));
        writer.UInt16BE((ushort)(length << 3));
        if (IsReliable(reliability))
        {
            writer.UInt24LE(reliableIndex);
        }

        if (IsOrdered(reliability))
        {
            writer.UInt24LE(orderIndex);
            writer.Byte(0); // order channel
        }

        if (split is { } s)
        {
            writer.Int32BE(s.Count);
            writer.UInt16BE(s.Id);
            writer.Int32BE(s.Index);
        }

        writer.Bytes(source.AsSpan(offset, length));
        return writer.ToArray();
    }

    private void SendDatagram(List<byte[]> frames, bool track)
    {
        var sequence = _sendSequence;
        _sendSequence = (_sendSequence + 1) & 0xFFFFFF;

        var writer = new ByteWriter(_mtu);
        writer.Byte(0x84); // valid datagram | needs B&AS
        writer.UInt24LE(sequence);
        foreach (var frame in frames)
        {
            writer.Bytes(frame);
        }

        _send(writer.ToArray(), RemoteEndPoint);
        if (track)
        {
            _unacked[sequence] = new PendingDatagram(frames, DateTime.UtcNow);
        }
    }

    private void FlushAcks()
    {
        if (_pendingAcks.Count == 0)
        {
            return;
        }

        _pendingAcks.Sort();
        var ranges = new List<(int Start, int End)>();
        var start = _pendingAcks[0];
        var previous = start;
        for (var i = 1; i < _pendingAcks.Count; i++)
        {
            var value = _pendingAcks[i];
            if (value == previous)
            {
                continue;
            }

            if (value == previous + 1)
            {
                previous = value;
                continue;
            }

            ranges.Add((start, previous));
            start = previous = value;
        }

        ranges.Add((start, previous));
        _pendingAcks.Clear();

        var writer = new ByteWriter(3 + ranges.Count * 7);
        writer.Byte(0xC0);
        writer.UInt16BE((ushort)ranges.Count);
        foreach (var (rangeStart, rangeEnd) in ranges)
        {
            if (rangeStart == rangeEnd)
            {
                writer.Byte(1);
                writer.UInt24LE(rangeStart);
            }
            else
            {
                writer.Byte(0);
                writer.UInt24LE(rangeStart);
                writer.UInt24LE(rangeEnd);
            }
        }

        _send(writer.ToArray(), RemoteEndPoint);
    }

    private static List<int> ReadRecords(byte[] data)
    {
        var result = new List<int>();
        try
        {
            var reader = new ByteReader(data, 1, data.Length - 1);
            int count = reader.UInt16BE();
            for (var i = 0; i < count; i++)
            {
                var single = reader.Bool();
                var start = reader.UInt24LE();
                var end = single ? start : reader.UInt24LE();
                if (end < start || end - start > 4096)
                {
                    continue;
                }

                for (var sequence = start; sequence <= end && result.Count < MaxAckRecords; sequence++)
                {
                    result.Add(sequence);
                }

                if (result.Count >= MaxAckRecords)
                {
                    break;
                }
            }
        }
        catch (PacketFormatException)
        {
            // Use whatever records were readable.
        }

        return result;
    }

    private static bool IsReliable(int reliability) => reliability is 2 or 3 or 4 or 6 or 7;

    private static bool IsSequenced(int reliability) => reliability is 1 or 4;

    private static bool IsOrdered(int reliability) => reliability is 3 or 7;

    private static Dictionary<int, byte[]>[] CreateOrderQueues()
    {
        var queues = new Dictionary<int, byte[]>[32];
        for (var i = 0; i < queues.Length; i++)
        {
            queues[i] = new Dictionary<int, byte[]>();
        }

        return queues;
    }

    private sealed class PendingDatagram
    {
        public PendingDatagram(List<byte[]> frames, DateTime sentAt)
        {
            Frames = frames;
            SentAt = sentAt;
        }

        public List<byte[]> Frames { get; }

        public DateTime SentAt { get; }
    }

    private sealed class SplitAssembly
    {
        public SplitAssembly(int count)
        {
            Parts = new byte[]?[count];
            Created = DateTime.UtcNow;
        }

        public byte[]?[] Parts { get; }

        public int Received { get; set; }

        public DateTime Created { get; }
    }
}
