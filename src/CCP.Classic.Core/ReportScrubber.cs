using System.Text.RegularExpressions;

namespace CCP.Classic.Core;

public sealed record ScrubCounts(int Paths, int Files, int Emails, int Tokens)
{
    public static ScrubCounts None { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Cleans text before it may leave the computer in a bug report. The path, email and token rules are
/// the full CCP desktop's (Services/Logging/LogRedactor.cs, LogScrubber.cs): a home folder name becomes
/// &lt;redacted&gt;. Classic also replaces every file name in a path with &lt;file&gt; plus its extension,
/// since library paths name the player's own media. Pure, so the preview and the tests see the same result.
/// </summary>
public static class ReportScrubber
{
    private static readonly Regex UserPath = new(@"(?i)([A-Z]:[\\/])Users[\\/]([^\\/\r\n""']+)", RegexOptions.Compiled);
    private static readonly Regex PosixHome = new(
        @"(?<=^|[\s""'(\[=:,;])(/home/|/Users/)(?!root(?![^/\r\n""'\s,;)\]]))([^/\r\n""'\s,;)\]]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Email = new(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsonToken = new(
        @"(?i)(""?(?:access_token|refresh_token|auth_token|api_key|apikey|authorization|bearer_token|x-auth-token|client_secret)""?\s*[:=]\s*)""?[A-Z0-9._~+/=\-]{8,}""?",
        RegexOptions.Compiled);
    private static readonly Regex Bearer = new(@"(?i)\bBearer\s+[A-Z0-9._~+/=\-]{16,}\b", RegexOptions.Compiled);
    // A name with an extension right after a path separator: D:\Media\clip.mp4, /home/x/a b.png. A web host after // stays.
    private static readonly Regex FileName = new(
        @"(?<=[\\/])(?<!//)(?!<redacted>)[^\\/\r\n""'<>|*?]+?\.([A-Za-z0-9]{1,5})(?=$|[\s""'),;\]:])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static (string Text, ScrubCounts Counts) Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return ("", ScrubCounts.None);
        int paths = 0, files = 0, emails = 0, tokens = 0;
        var text = UserPath.Replace(input, m => { paths++; return m.Groups[1].Value + "Users\\<redacted>"; });
        text = PosixHome.Replace(text, m => { paths++; return m.Groups[1].Value + "<redacted>"; });
        text = Email.Replace(text, _ => { emails++; return "[email redacted]"; });
        text = JsonToken.Replace(text, m => { tokens++; return m.Groups[1].Value + "[token redacted]"; });
        text = Bearer.Replace(text, _ => { tokens++; return "Bearer [token redacted]"; });
        text = FileName.Replace(text, m => { files++; return "<file>." + m.Groups[1].Value.ToLowerInvariant(); });
        return (text, new(paths, files, emails, tokens));
    }
}
