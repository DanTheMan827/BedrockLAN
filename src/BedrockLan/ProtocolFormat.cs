// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan;

// Also compiled into tools/ProtocolGen (linked), which doesn't reference the library.

/// <summary>The protocol config format this version of the library reads (and the generator writes).</summary>
internal static class ProtocolFormat
{
    public const int Version = 1;
}
