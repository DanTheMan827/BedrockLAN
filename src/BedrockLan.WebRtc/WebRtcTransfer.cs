// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.NetherNet;

namespace BedrockLan.WebRtc;

/// <summary>Entry points for enabling the WebRTC transfer server on a <see cref="BedrockRelay"/>.</summary>
public static class WebRtcTransfer
{
    public const string IdentityFileName = "nethernet-identity.pem";
    public const string DefaultIdentityDomain = "bedrock-lan";

    /// <summary>
    /// Creates a transfer handler factory using the operator key at <c>{persistDirectory}/nethernet-identity.pem</c>
    /// (created on first use). <see cref="BedrockRelay"/> calls this automatically when this assembly is present.
    /// </summary>
    public static Func<NetherNetHandlerContext, INetherNetSignalHandler> CreateFactory(string persistDirectory, LanLogger? log)
    {
        var path = Path.Combine(persistDirectory, IdentityFileName);
        var created = !File.Exists(path);
        var identity = NetherNetServerIdentity.LoadOrCreate(path, DefaultIdentityDomain);
        log?.Invoke(LanLogLevel.Info, $"NetherNet operator key {(created ? "created" : "loaded")}: {Path.GetFullPath(path)} (fingerprint {identity.PublicKeyFingerprint}).");
        return WebRtcTransferHandler.Factory(identity);
    }

    /// <summary>Same as <see cref="CreateFactory(string, LanLogger?)"/> with <c>./persist</c> in the working directory.</summary>
    public static Func<NetherNetHandlerContext, INetherNetSignalHandler> CreateFactory() =>
        CreateFactory(Path.Combine(Directory.GetCurrentDirectory(), "persist"), null);
}
