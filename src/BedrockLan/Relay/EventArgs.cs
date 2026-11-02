using System.Net;

namespace BedrockLan;

public class RelayServerEventArgs : EventArgs
{
    public RelayServerEventArgs(RelayServer server)
    {
        Server = server;
        Timestamp = DateTime.UtcNow;
    }

    public RelayServer Server { get; }

    /// <summary>When the event happened (UTC). Handlers run slightly later, on the relay's event thread.</summary>
    public DateTime Timestamp { get; }
}

/// <summary>A LAN client was sent on to the real server.</summary>
public sealed class TransferEventArgs : RelayServerEventArgs
{
    public TransferEventArgs(RelayServer server, IPEndPoint client, RelayTransport transport, RedirectMethod method, TransferTarget? target, int? clientProtocol)
        : base(server)
    {
        Client = client;
        Transport = transport;
        Method = method;
        Target = target;
        ClientProtocol = clientProtocol;
    }

    /// <summary>The client's address on the LAN.</summary>
    public IPEndPoint Client { get; }

    public RelayTransport Transport { get; }

    public RedirectMethod Method { get; }

    /// <summary>Destination in the Transfer packet (null for <see cref="RedirectMethod.SignalingRelay"/>).</summary>
    public TransferTarget? Target { get; }

    /// <summary>Bedrock protocol version reported by the client, when known.</summary>
    public int? ClientProtocol { get; }
}

public sealed class ServerStatusChangedEventArgs : RelayServerEventArgs
{
    public ServerStatusChangedEventArgs(RelayServer server, ServerStatus? previous, ServerStatus current)
        : base(server)
    {
        Previous = previous;
        Current = current;
    }

    public ServerStatus? Previous { get; }

    public ServerStatus Current { get; }
}

public sealed class OnlineChangedEventArgs : RelayServerEventArgs
{
    public OnlineChangedEventArgs(RelayServer server, bool isOnline, string? error)
        : base(server)
    {
        IsOnline = isOnline;
        Error = error;
    }

    public bool IsOnline { get; }

    /// <summary>Last query error when going offline.</summary>
    public string? Error { get; }
}

public sealed class KindDetectedEventArgs : RelayServerEventArgs
{
    public KindDetectedEventArgs(RelayServer server, UpstreamKind kind)
        : base(server)
    {
        Kind = kind;
    }

    public UpstreamKind Kind { get; }
}
