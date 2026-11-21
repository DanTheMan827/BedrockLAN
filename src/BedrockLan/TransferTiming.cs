// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan;

// Also compiled into tools/ProtocolGen (linked), which doesn't reference the library: keep this file free of
// other library types.

/// <summary>When the transfer server sends the Transfer packet.</summary>
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
    /// (client error "Terracotta", InitialConnection-14). In the built-in protocol config this runs
    /// for protocol 2207 and newer; older clients fall back to <see cref="AfterResourcePacks"/>.
    /// </summary>
    AfterSpawn,
}
