// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using BedrockLan.NetherNet;
using BedrockLan.RakNet;

namespace BedrockLan;

/// <summary>Immutable snapshot of an upstream server's status, as last queried.</summary>
public sealed class ServerStatus
{
    /// <summary>Server name / MOTD line 1, with formatting codes removed.</summary>
    public string Name { get; init; } = "";

    /// <summary>Level name / MOTD line 2, with formatting codes removed.</summary>
    public string Level { get; init; } = "";

    /// <summary>Name exactly as the upstream sent it (may contain § formatting codes).</summary>
    public string RawName { get; init; } = "";

    /// <summary>Level exactly as the upstream sent it.</summary>
    public string RawLevel { get; init; } = "";
    public int Protocol { get; init; }
    public string Version { get; init; } = "";
    public int Players { get; init; }
    public int MaxPlayers { get; init; }
    public int GameMode { get; init; }

    /// <summary>Raw RakNet MOTD when the upstream is RakNet.</summary>
    internal BedrockMotd? RakNetMotd { get; init; }

    /// <summary>Parsed NetherNet LAN server data when the upstream is a NetherNet LAN host.</summary>
    internal NetherNetServerData? NetherNetData { get; init; }

    /// <summary>Raw NetherNet LAN server data bytes (kept even when the format version is unknown).</summary>
    internal byte[]? NetherNetRaw { get; init; }

    internal static ServerStatus FromMotd(BedrockMotd motd) => new()
    {
        Name = BedrockText.StripFormatting(motd.Motd),
        Level = BedrockText.StripFormatting(motd.SubMotd),
        RawName = motd.Motd,
        RawLevel = motd.SubMotd,
        Protocol = motd.Protocol,
        Version = motd.Version,
        Players = motd.Players,
        MaxPlayers = motd.MaxPlayers,
        GameMode = motd.GameMode.ToLowerInvariant() switch
        {
            "creative" => 1,
            "adventure" => 2,
            _ => 0,
        },
        RakNetMotd = motd,
    };

    internal static ServerStatus FromHttp(HttpJoinInfo info) => new()
    {
        Name = BedrockText.StripFormatting(info.Name),
        Level = BedrockText.StripFormatting(info.Level),
        RawName = info.Name,
        RawLevel = info.Level,
        Protocol = info.Protocol,
        Version = info.Version,
        Players = info.Players,
        MaxPlayers = info.MaxPlayers,
        GameMode = info.GameType,
    };

    internal static ServerStatus FromLan(byte[] raw)
    {
        if (NetherNetServerData.TryParse(raw, out var data) && data is not null)
        {
            return new ServerStatus
            {
                Name = BedrockText.StripFormatting(data.ServerName),
                Level = BedrockText.StripFormatting(data.LevelName),
                RawName = data.ServerName,
                RawLevel = data.LevelName,
                Protocol = data.Protocol,
                Version = data.Version,
                Players = data.PlayerCount,
                MaxPlayers = data.MaxPlayerCount,
                GameMode = data.GameType,
                NetherNetData = data,
                NetherNetRaw = raw,
            };
        }

        return new ServerStatus { Name = "NetherNet LAN host", NetherNetRaw = raw };
    }

    /// <summary>Game mode name (Survival, Creative, Adventure).</summary>
    public string GameModeName => BedrockMotd.GameModeName(GameMode);

    /// <summary>True if the user-visible details differ (players, names, version...).</summary>
    public bool DiffersFrom(ServerStatus? other) =>
        other is null || Name != other.Name || Level != other.Level || Protocol != other.Protocol
        || Version != other.Version || Players != other.Players || MaxPlayers != other.MaxPlayers || GameMode != other.GameMode;

    public override string ToString() => $"\"{Name}\" / \"{Level}\" {Players}/{MaxPlayers} v{Version} (protocol {Protocol})";
}
