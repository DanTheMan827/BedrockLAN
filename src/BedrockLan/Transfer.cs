namespace BedrockLan;

/// <summary>Where a transferred client should go.</summary>
public readonly record struct TransferTarget(string Host, ushort Port)
{
    public override string ToString() => $"{Host}:{Port}";
}

public enum TransferTiming
{
    /// <summary>Send Transfer right after PlayStatus(LoginSuccess).</summary>
    Immediate,

    /// <summary>
    /// Complete the (empty) resource pack negotiation first, then send Transfer. Falls back to
    /// <see cref="Immediate"/> for protocols older than 729 (1.21.30).
    /// </summary>
    AfterResourcePacks,

    /// <summary>
    /// Start a minimal empty game (StartGame → chunk radius → PlayStatus(PlayerSpawn)) and transfer once the
    /// client reports it has spawned. Required by 26.60 clients, which ignore a Transfer received during login
    /// (client error "Terracotta", InitialConnection-14). Runs for protocol 2207 and newer (layouts checked
    /// against Mojang's protocol docs up to 2250, newer ones assumed unchanged); older clients fall back to
    /// <see cref="AfterResourcePacks"/>.
    /// </summary>
    AfterSpawn,
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
