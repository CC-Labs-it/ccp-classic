// Adapted from public CCP fe40ea7587ac0b09e47d0724e59d7178db3336da, MIT.
using System.Text;

namespace CCP.Classic.Core;

public static class LockCardText
{

    public static string Normalize(string? text) => Normalize(text, dropSentencePunctuation: true);

    public static int TypedLength(string? text) => Normalize(text, dropSentencePunctuation: false).Length;

    private static string Normalize(string? text, bool dropSentencePunctuation)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length + 4);
        bool pendingSpace = false;

        foreach (var raw in text)
        {

            if (char.IsWhiteSpace(raw))
            {
                if (sb.Length > 0) pendingSpace = true;
                continue;
            }

            if (IsInvisible(raw)) continue;

            foreach (var c in MapPunctuation(raw))
            {

                if (dropSentencePunctuation && IsIgnorablePunctuation(c))
                {
                    if (sb.Length > 0) pendingSpace = true;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static bool Matches(string? typed, string? expected)
        => string.Equals(Normalize(typed), Normalize(expected), StringComparison.OrdinalIgnoreCase);

    public static bool IsPrefixOf(string? typed, string? expected)
    {
        var t = Normalize(typed);
        if (t.Length == 0) return true;
        return Normalize(expected).StartsWith(t, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvisible(char c)
        => c == (char)0x00AD
        || c == (char)0x200B
        || c == (char)0x200C
        || c == (char)0x200D
        || c == (char)0xFEFF;

    private static bool IsIgnorablePunctuation(char c)
        => c == ',' || c == '.' || c == ';' || c == ':' || c == '!' || c == '?' || c == '-';

    private static string MapPunctuation(char c) => c switch
    {
        '…' => "...",
        '‘' or '’' or '‛' or 'ʼ' => "'",
        '“' or '”' or '‟' => "\"",
        '\u2013' or '\u2014' or '‒' or '―' or '−' => "-",
        _ => c.ToString(),
    };
}
