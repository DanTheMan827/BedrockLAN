// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan;

/// <summary>Where a transferred client should go.</summary>
public readonly record struct TransferTarget(string Host, ushort Port)
{
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// How clients are handed a Transfer packet. Thread-safe: every property may be read and changed from any
/// thread at any time; changes apply to the next connection step that reads them.
/// </summary>
public sealed class TransferOptions
{
    private int _timing = (int)TransferTiming.AfterSpawn;
    private long _disconnectDelayTicks = TimeSpan.FromSeconds(10).Ticks;
    private long _spawnTimeoutTicks = TimeSpan.FromSeconds(5).Ticks;
    private string _loadingWorldName = "Joining server...";
    private string _offlineMessage = "That server is offline right now.";
    private ProtocolConfig _protocol = ProtocolConfig.Default;

    /// <summary>
    /// The packets and the conversation to follow (built-in default unless replaced, e.g. with a newer config
    /// downloaded at run time). New connections use the config set at the time they start.
    /// </summary>
    public ProtocolConfig Protocol
    {
        get => Volatile.Read(ref _protocol);
        set => Volatile.Write(ref _protocol, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public TransferTiming Timing
    {
        get => (TransferTiming)Volatile.Read(ref _timing);
        set => Volatile.Write(ref _timing, (int)value);
    }

    /// <summary>How long to keep the connection open after sending Transfer, giving the client time to act on it.</summary>
    public TimeSpan DisconnectDelay
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _disconnectDelayTicks));
        set => Interlocked.Exchange(ref _disconnectDelayTicks, Math.Max(0, value.Ticks));
    }

    /// <summary>AfterSpawn: transfer anyway if the client hasn't confirmed spawning within this time.</summary>
    public TimeSpan SpawnTimeout
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _spawnTimeoutTicks));
        set => Interlocked.Exchange(ref _spawnTimeoutTicks, Math.Max(0, value.Ticks));
    }

    /// <summary>AfterSpawn: world name in the temporary StartGame.</summary>
    public string LoadingWorldName
    {
        get => Volatile.Read(ref _loadingWorldName);
        set => Volatile.Write(ref _loadingWorldName, value ?? "");
    }

    /// <summary>Message shown if the destination is currently unknown/offline.</summary>
    public string OfflineMessage
    {
        get => Volatile.Read(ref _offlineMessage);
        set => Volatile.Write(ref _offlineMessage, value ?? "");
    }
}
