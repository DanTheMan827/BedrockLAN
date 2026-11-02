using System.Globalization;

namespace BedrockLan.NetherNet;

/// <summary>
/// A NetherNet signaling message as carried in LAN discovery "message" packets:
/// <c>TYPE connectionId data</c>.
/// </summary>
public sealed record NetherNetSignal(string Type, ulong ConnectionId, string Data)
{
    public const string Offer = "CONNECTREQUEST";
    public const string Answer = "CONNECTRESPONSE";
    public const string Candidate = "CANDIDATEADD";
    public const string Error = "CONNECTERROR";

    // A subset of NetherNet signaling error codes (see df-mc/go-nethernet).
    public const int ErrorDestinationNotLoggedIn = 1;
    public const int ErrorNegotiationTimeout = 2;
    public const int ErrorFailedToCreateAnswer = 11;
    public const int ErrorFailedToSetRemoteDescription = 13;

    public static bool TryParse(string text, out NetherNetSignal signal)
    {
        signal = null!;
        var parts = text.Split(' ', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (parts[0] is not (Offer or Answer or Candidate or Error))
        {
            return false;
        }

        var digits = 0;
        while (digits < parts[1].Length && char.IsAsciiDigit(parts[1][digits]))
        {
            digits++;
        }

        if (!ulong.TryParse(parts[1].AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var connectionId))
        {
            return false;
        }

        signal = new NetherNetSignal(parts[0], connectionId, parts[2]);
        return true;
    }

    public static NetherNetSignal ErrorSignal(ulong connectionId, int code) =>
        new(Error, connectionId, code.ToString(CultureInfo.InvariantCulture));

    public override string ToString() =>
        $"{Type} {ConnectionId.ToString(CultureInfo.InvariantCulture)} {Data}";
}
