// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.Protocol;

namespace BedrockLan;

/// <summary>A protocol config couldn't be used: invalid JSON, an unknown key, type, packet, stage or variable, etc.</summary>
public sealed class ProtocolConfigException : Exception
{
    public ProtocolConfigException(string message)
        : base(message)
    {
    }

    public ProtocolConfigException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The packets the transfer server sends and the conversation it holds with a client, as data: everything the
/// <c>TransferSession</c> does between a client's first packet and the Transfer comes from here, so it can be
/// updated for a new game version without a new build. Load one with <see cref="Parse"/> (JSON with comments and
/// trailing commas; the format is described at the top of the built-in default) and set it on
/// <see cref="TransferOptions.Protocol"/>. Immutable and thread-safe.
/// </summary>
public sealed class ProtocolConfig
{
    /// <summary>The newest config format this library reads.</summary>
    public const int SupportedFormatVersion = ProtocolConfigParser.SupportedFormatVersion;

    /// <summary>
    /// Where updated configs are published, one file per config format: this library only ever fetches files in
    /// the format it understands (protocol-1.jsonc for format 1), so a future format can't break older apps.
    /// </summary>
    public static readonly string DefaultUpdateUrl = $"https://dantheman827.github.io/BedrockLAN/protocol-{SupportedFormatVersion}.jsonc";

    private const string DefaultResourceName = "BedrockLan.default-protocol.jsonc";

    private static readonly Lazy<ProtocolConfig> DefaultConfig = new(LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    internal ProtocolConfig(int formatVersion, string configVersion, IReadOnlyDictionary<string, PacketSpec> packets, SessionSpec session, string text)
    {
        FormatVersion = formatVersion;
        ConfigVersion = configVersion;
        Packets = packets;
        Session = session;
        Text = text;
    }

    /// <summary>The config built into this library.</summary>
    public static ProtocolConfig Default => DefaultConfig.Value;

    /// <summary>The text of <see cref="Default"/> (a starting point for a hosted copy).</summary>
    public static string DefaultText => Default.Text;

    public int FormatVersion { get; }

    /// <summary>The config's own version label ("configVersion"), for display and logs.</summary>
    public string ConfigVersion { get; }

    /// <summary>The text this config was parsed from (to cache it as is).</summary>
    public string Text { get; }

    /// <summary>Names of the packets the config defines.</summary>
    public IReadOnlyCollection<string> PacketNames => Packets.Keys.ToList();

    internal IReadOnlyDictionary<string, PacketSpec> Packets { get; }

    internal SessionSpec Session { get; }

    /// <summary>Parses and fully validates a config.</summary>
    /// <exception cref="ProtocolConfigException">The text isn't a valid config; the message says where and why.</exception>
    public static ProtocolConfig Parse(string text) => ProtocolConfigParser.Parse(text ?? throw new ArgumentNullException(nameof(text)));

    /// <summary>Parses a config, returning false (with the reason) instead of throwing when it isn't valid.</summary>
    public static bool TryParse(string text, out ProtocolConfig? config, out string? error)
    {
        try
        {
            config = Parse(text);
            error = null;
            return true;
        }
        catch (ProtocolConfigException ex)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Builds one packet (id + body) for a protocol, with the given variable values (others default to 0 / "").</summary>
    internal byte[] BuildPacket(string name, int protocol, IReadOnlyDictionary<string, object?> variables) =>
        PacketBuilder.Build(Packets[name], protocol, v => v == "protocol" ? protocol : variables.GetValueOrDefault(v));

    private static ProtocolConfig LoadDefault()
    {
        using var stream = typeof(ProtocolConfig).Assembly.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {DefaultResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public override string ToString() => $"protocol config {ConfigVersion} (format {FormatVersion})";
}
