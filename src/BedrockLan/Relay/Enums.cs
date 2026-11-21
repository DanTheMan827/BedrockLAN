// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan;

/// <summary>What kind of Bedrock server an entry points at.</summary>
public enum UpstreamKind
{
    /// <summary>
    /// Detect by querying the host: NetherNet HTTP signaling (http, then https), NetherNet LAN discovery
    /// (UDP 7551) and RakNet ping are tried together; HTTP signaling wins over LAN, which wins over RakNet.
    /// The type is re-detected if the server stays offline.
    /// </summary>
    Auto,

    /// <summary>A classic RakNet server (BDS before 26.50, PocketMine, Nukkit, Geyser in RakNet mode, ...).</summary>
    RakNet,

    /// <summary>A NetherNet server using HTTP signaling (BDS 26.50+, Geyser NetherNet). Queried with GET /v1/join.</summary>
    NetherNetHttp,

    /// <summary>A NetherNet LAN host reachable on UDP 7551 by unicast (e.g. a LAN world on another subnet).</summary>
    NetherNetLan,
}

/// <summary>How NetherNet LAN clients are sent to a NetherNet upstream.</summary>
public enum NetherNetConnectMode
{
    /// <summary>
    /// Transfer when a WebRTC transfer handler is available (NetherNet LAN hosts, which have no host:port, are
    /// relayed); otherwise relay.
    /// </summary>
    Auto,

    /// <summary>
    /// Relay WebRTC signaling so the client connects to the server directly. For HTTP-signaling servers, a refusal
    /// (or no answer) falls back to the WebRTC transfer handler when one is available. Only for servers known to
    /// work this way: a server can accept the relayed LAN offer and the client still fail to connect.
    /// </summary>
    Relay,

    /// <summary>Terminate WebRTC locally and send a Transfer packet (requires a transfer handler, e.g. BedrockLan.WebRtc).</summary>
    Transfer,
}

/// <summary>The LAN transport a client used to reach the relay.</summary>
public enum RelayTransport
{
    RakNet,
    NetherNet,
}

/// <summary>How a client was sent on to the real server.</summary>
public enum RedirectMethod
{
    /// <summary>The client received a Transfer packet and reconnected to the target itself.</summary>
    TransferPacket,

    /// <summary>The client's WebRTC signaling was relayed; it connects to the server directly.</summary>
    SignalingRelay,
}
