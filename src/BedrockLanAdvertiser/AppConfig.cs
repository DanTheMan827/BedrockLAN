using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using BedrockLan;

namespace BedrockLanAdvertiser;

/// <summary>Shape of the optional servers.json. Every setting is optional.</summary>
public sealed class AppConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public double PollIntervalSeconds { get; set; } = 5;
    public double OfflineAfterSeconds { get; set; } = 20;
    public bool HideOfflineServers { get; set; } = true;
    public TransferTiming TransferTiming { get; set; } = TransferTiming.AfterSpawn;
    public double TransferDisconnectDelaySeconds { get; set; } = 10;
    public bool Verbose { get; set; }

    /// <summary>
    /// Protocol config (packets and transfer conversation): a file path, an http(s) URL, or "builtin". Default:
    /// <see cref="ProtocolConfig.DefaultUpdateUrl"/>. A URL is downloaded again every
    /// <see cref="ProtocolRefreshHours"/>; until a download succeeds (HTTP 200, valid config) the built-in config
    /// (or the last good download) is used.
    /// </summary>
    public string? Protocol { get; set; }

    /// <summary>How often a protocol config URL is downloaded again (0 = only at startup).</summary>
    public double ProtocolRefreshHours { get; set; } = 12;

    /// <summary>Where the NetherNet operator key is kept. Default: ./persist in the working directory.</summary>
    public string? PersistDirectory { get; set; }

    public RakNetSection RakNet { get; set; } = new();
    public NetherNetSection NetherNet { get; set; } = new();
    public List<ServerEntry> Servers { get; set; } = new();

    public sealed class RakNetSection
    {
        public bool Enabled { get; set; } = true;
        public int DiscoveryPort { get; set; } = 19132;
        public int FirstServerPort { get; set; } = 19140;
    }

    public sealed class NetherNetSection
    {
        public bool Enabled { get; set; } = true;
        public int DiscoveryPort { get; set; } = 7551;
        public int CandidateWindowMilliseconds { get; set; } = 500;
        public bool TrickleAnswerCandidates { get; set; } = true;
    }

    /// <summary>
    /// A server to advertise. In JSON this can be just a string (<c>"play.example.com"</c>,
    /// <c>"192.168.1.20:19133"</c>, <c>"[fd00::5]:19132"</c>) or an object for overrides.
    /// </summary>
    [JsonConverter(typeof(ServerEntryConverter))]
    public sealed class ServerEntry
    {
        public string Host { get; set; } = "";
        public int? Port { get; set; }
        public string? Name { get; set; }
        public string? Level { get; set; }

        /// <summary>auto (default: detected by querying the host) | raknet | nethernet | nethernet-lan</summary>
        public string Type { get; set; } = "auto";

        public bool Https { get; set; }
        public bool AllowInvalidCertificate { get; set; }
        public string? TransferHost { get; set; }
        public int? TransferPort { get; set; }
        public bool AdvertiseRakNet { get; set; } = true;
        public bool AdvertiseNetherNet { get; set; } = true;
        public int? LocalPort { get; set; }

        /// <summary>auto (transfer when the WebRTC server is available, else relay) | relay | transfer</summary>
        public string NetherNetMode { get; set; } = "auto";

        public bool Enabled { get; set; } = true;

        /// <summary>The address string for <see cref="BedrockRelay.AddServer(string, RelayServerOptions?)"/>.</summary>
        public string Address
        {
            get
            {
                if (Port is null)
                {
                    return Host;
                }

                var (host, _) = BedrockRelay.ParseAddress(Host);
                return host.Contains(':') ? $"[{host}]:{Port}" : $"{host}:{Port}";
            }
        }

        public RelayServerOptions ToOptions() => new()
        {
            Kind = Type.Trim().ToLowerInvariant() switch
            {
                "" or "auto" => UpstreamKind.Auto,
                "raknet" => UpstreamKind.RakNet,
                "nethernet" or "nethernet-http" or "webrtc" => UpstreamKind.NetherNetHttp,
                "nethernet-lan" or "lan" => UpstreamKind.NetherNetLan,
                _ => throw new InvalidDataException($"Unknown server type '{Type}' for {Host}. Use auto, raknet, nethernet or nethernet-lan."),
            },
            DisplayName = Name,
            LevelName = Level,
            UseHttps = Https,
            AllowInvalidCertificate = AllowInvalidCertificate,
            TransferHost = TransferHost,
            TransferPort = TransferPort,
            AdvertiseRakNet = AdvertiseRakNet,
            AdvertiseNetherNet = AdvertiseNetherNet,
            RakNetLocalPort = LocalPort,
            NetherNetMode = NetherNetMode.Trim().ToLowerInvariant() switch
            {
                "" or "auto" => NetherNetConnectMode.Auto,
                "relay" => NetherNetConnectMode.Relay,
                "transfer" => NetherNetConnectMode.Transfer,
                _ => throw new InvalidDataException($"Unknown netherNetMode '{NetherNetMode}' for {Host}. Use auto, relay or transfer."),
            },
        };
    }

    /// <summary>Accepts either a plain "host[:port]" string or a full object for each server.</summary>
    public sealed class ServerEntryConverter : JsonConverter<ServerEntry>
    {
        public override ServerEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new ServerEntry { Host = reader.GetString() ?? "" };
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Each server must be a \"host[:port]\" string or an object.");
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var entry = new ServerEntry();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var target = typeof(ServerEntry).GetProperties()
                    .FirstOrDefault(p => p.CanWrite && string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new JsonException($"Unknown server setting '{property.Name}'.");
                target.SetValue(entry, property.Value.Deserialize(target.PropertyType, options));
            }

            return entry;
        }

        public override void Write(Utf8JsonWriter writer, ServerEntry value, JsonSerializerOptions options) =>
            throw new NotSupportedException();
    }

    public static AppConfig Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), options)
               ?? throw new InvalidDataException("Config file is empty.");
    }

    public IPAddress ParseBindAddress() =>
        IPAddress.TryParse(BindAddress, out var address) ? address : throw new InvalidDataException($"Invalid bindAddress '{BindAddress}'.");
}
