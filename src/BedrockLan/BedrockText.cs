using System.Text;

namespace BedrockLan;

/// <summary>Helpers for Minecraft text.</summary>
public static class BedrockText
{
    /// <summary>The formatting-code prefix (section sign).</summary>
    public const char FormattingPrefix = '§';

    /// <summary>
    /// Removes formatting codes (<c>§a</c>, <c>§l</c>, <c>§r</c>, ...), replacement characters and control
    /// characters, and collapses the runs of padding spaces servers use to centre their MOTD.
    /// </summary>
    public static string StripFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == FormattingPrefix)
            {
                i++; // skip the code character too
                continue;
            }

            if (c == '�' || char.IsControl(c))
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd();
    }
}
