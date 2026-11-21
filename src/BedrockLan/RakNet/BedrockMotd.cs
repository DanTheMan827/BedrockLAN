// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;

namespace BedrockLan.RakNet;

/// <summary>
/// The semicolon separated status string carried in a RakNet unconnected pong:
/// <c>MCPE;motd;protocol;version;players;max;guid;subMotd;gameMode;gameModeNumeric;port4;port6;...</c>
/// </summary>
public sealed class BedrockMotd
{
    public string Edition { get; set; } = "MCPE";
    public string Motd { get; set; } = "";
    public int Protocol { get; set; }
    public string Version { get; set; } = "";
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public long ServerGuid { get; set; }
    public string SubMotd { get; set; } = "";
    public string GameMode { get; set; } = "Survival";
    public int GameModeNumeric { get; set; } = 1;
    public int PortV4 { get; set; } = 19132;
    public int PortV6 { get; set; } = 19133;

    /// <summary>Any fields after port6 are preserved verbatim.</summary>
    public List<string> Extra { get; set; } = new();

    public static bool TryParse(string value, out BedrockMotd motd)
    {
        motd = new BedrockMotd();
        var parts = value.Split(';');
        if (parts.Length < 6 || (parts[0] != "MCPE" && parts[0] != "MCEE"))
        {
            return false;
        }

        motd.Edition = parts[0];
        motd.Motd = parts[1];
        motd.Protocol = ParseInt(parts, 2);
        motd.Version = parts[3];
        motd.Players = ParseInt(parts, 4);
        motd.MaxPlayers = ParseInt(parts, 5);
        if (parts.Length > 6 && long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid))
        {
            motd.ServerGuid = guid;
        }

        motd.SubMotd = parts.Length > 7 ? parts[7] : "";
        motd.GameMode = parts.Length > 8 && parts[8].Length > 0 ? parts[8] : "Survival";
        motd.GameModeNumeric = parts.Length > 9 ? ParseInt(parts, 9, 1) : 1;
        motd.PortV4 = parts.Length > 10 ? ParseInt(parts, 10, 19132) : 19132;
        motd.PortV6 = parts.Length > 11 ? ParseInt(parts, 11, 19133) : 19133;

        // A trailing ';' produces an empty final element; drop it so round-tripping is stable.
        var extraCount = parts.Length - 12;
        if (extraCount > 0 && parts[^1].Length == 0)
        {
            extraCount--;
        }

        for (var i = 0; i < extraCount; i++)
        {
            motd.Extra.Add(parts[12 + i]);
        }

        return true;
    }

    public BedrockMotd Clone()
    {
        var clone = (BedrockMotd)MemberwiseClone();
        clone.Extra = new List<string>(Extra);
        return clone;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        void Field(string s) => sb.Append(Sanitize(s)).Append(';');
        void Number(long n) => sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(';');

        Field(Edition);
        Field(Motd);
        Number(Protocol);
        Field(Version);
        Number(Players);
        Number(MaxPlayers);
        Number(ServerGuid);
        Field(SubMotd);
        Field(GameMode);
        Number(GameModeNumeric);
        Number(PortV4);
        Number(PortV6);
        foreach (var extra in Extra)
        {
            Field(extra);
        }

        return sb.ToString();
    }

    public static string GameModeName(int gameMode) => gameMode switch
    {
        1 => "Creative",
        2 => "Adventure",
        _ => "Survival",
    };

    private static string Sanitize(string value) => value.Replace(';', ':');

    private static int ParseInt(string[] parts, int index, int fallback = 0) =>
        int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
