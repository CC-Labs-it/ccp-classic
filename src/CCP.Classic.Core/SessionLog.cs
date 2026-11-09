namespace CCP.Classic.Core;

/// <summary>
/// The "log" a bug report can include. Classic writes no log file; this keeps the last status lines
/// and caught errors of the current run in memory only, and is gone when the app closes. It reaches
/// the network only when the player ticks "Include this session's log" and presses Send, after the
/// scrubber and the preview.
/// </summary>
public sealed class SessionLog(int capacity = 200, TimeProvider? time = null)
{
    public const int MaxLineChars = 2000;
    private readonly Queue<string> lines = new();
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private string? last;

    public static SessionLog Shared { get; } = new();

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var text = line.Length > MaxLineChars ? line[..MaxLineChars] : line;
        var stamped = clock.GetUtcNow().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + text.TrimEnd();
        lock (lines)
        {
            // Autosave reports "Saved" on every change; a repeat of the previous line adds nothing.
            if (text == last) return;
            last = text;
            lines.Enqueue(stamped);
            while (lines.Count > capacity) lines.Dequeue();
        }
    }

    public void Error(string where, Exception error) => Add("error " + where + ": " + error);

    public string Text()
    {
        lock (lines) return string.Join("\n", lines);
    }
}
