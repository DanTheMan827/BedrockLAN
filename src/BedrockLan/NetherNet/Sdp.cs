namespace BedrockLan.NetherNet;

/// <summary>Small line-based SDP helpers for bridging trickle and non-trickle NetherNet signaling.</summary>
public static class Sdp
{
    private const string CandidatePrefix = "a=candidate:";
    private const string EndOfCandidates = "a=end-of-candidates";
    private const string IdentityPrefix = "a=identity:";
    private const string FingerprintPrefix = "a=fingerprint:";

    /// <summary>Returns ICE candidates as <c>candidate:...</c> strings (the form used in CANDIDATEADD).</summary>
    public static IReadOnlyList<string> ExtractCandidates(string sdp) =>
        Lines(sdp).Where(l => l.StartsWith(CandidatePrefix, StringComparison.Ordinal)).Select(l => l[2..]).ToList();

    /// <summary>True if the SDP already carries candidates and declares gathering complete.</summary>
    public static bool HasCompleteCandidates(string sdp)
    {
        var lines = Lines(sdp);
        return lines.Any(l => l.StartsWith(CandidatePrefix, StringComparison.Ordinal))
               && lines.Any(l => l == EndOfCandidates);
    }

    /// <summary>
    /// Inserts trickled candidates into the first media section (NetherNet bundles everything in one) and
    /// marks the end of candidates, producing the complete SDP that HTTP signaling expects.
    /// </summary>
    public static string AddCandidates(string sdp, IEnumerable<string> candidates)
    {
        var newline = NewLine(sdp);
        var lines = Lines(sdp);
        var mediaStart = lines.FindIndex(l => l.StartsWith("m=", StringComparison.Ordinal));
        if (mediaStart < 0)
        {
            return sdp;
        }

        var mediaEnd = lines.FindIndex(mediaStart + 1, l => l.StartsWith("m=", StringComparison.Ordinal));
        if (mediaEnd < 0)
        {
            mediaEnd = lines.Count;
        }

        var existing = new HashSet<string>(
            lines.Where(l => l.StartsWith(CandidatePrefix, StringComparison.Ordinal)).Select(l => l[2..]),
            StringComparer.Ordinal);
        var toAdd = new List<string>();
        foreach (var raw in candidates)
        {
            var normalized = NormalizeCandidate(raw);
            if (normalized is not null && existing.Add(normalized))
            {
                toAdd.Add("a=" + normalized);
            }
        }

        var endIndex = lines.FindIndex(mediaStart, mediaEnd - mediaStart, l => l == EndOfCandidates);
        if (endIndex >= 0)
        {
            lines.InsertRange(endIndex, toAdd);
        }
        else
        {
            toAdd.Add(EndOfCandidates);
            lines.InsertRange(mediaEnd, toAdd);
        }

        return Join(lines, newline);
    }

    /// <summary>Normalises a trickled candidate to <c>candidate:...</c>, or null if empty.</summary>
    public static string? NormalizeCandidate(string candidate)
    {
        var value = candidate.Trim();
        if (value.StartsWith("a=", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length == 0)
        {
            return null;
        }

        return value.StartsWith("candidate:", StringComparison.Ordinal) ? value : "candidate:" + value;
    }

    public static string StripIdentity(string sdp)
    {
        var lines = Lines(sdp);
        lines.RemoveAll(l => l.StartsWith(IdentityPrefix, StringComparison.Ordinal));
        return Join(lines, NewLine(sdp));
    }

    /// <summary>Inserts a session-level attribute line just before the first <c>m=</c> line.</summary>
    public static string InsertSessionAttribute(string sdp, string attributeLine)
    {
        var lines = Lines(sdp);
        var mediaStart = lines.FindIndex(l => l.StartsWith("m=", StringComparison.Ordinal));
        lines.Insert(mediaStart < 0 ? lines.Count : mediaStart, attributeLine);
        return Join(lines, NewLine(sdp));
    }

    public static IReadOnlyList<(string Algorithm, string Digest)> GetFingerprints(string sdp)
    {
        var result = new List<(string, string)>();
        foreach (var line in Lines(sdp))
        {
            if (!line.StartsWith(FingerprintPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[FingerprintPrefix.Length..].Trim();
            var space = value.IndexOf(' ');
            if (space > 0)
            {
                result.Add((value[..space], value[(space + 1)..].Trim()));
            }
        }

        return result;
    }

    public static string? GetMid(string sdp)
    {
        const string prefix = "a=mid:";
        var line = Lines(sdp).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? null : line.Substring(prefix.Length).Trim();
    }

    private static string NewLine(string sdp) => sdp.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static List<string> Lines(string sdp)
    {
        var lines = sdp.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static string Join(List<string> lines, string newline) => string.Join(newline, lines) + newline;
}
