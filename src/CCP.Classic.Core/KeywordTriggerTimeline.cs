using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CCP.Classic.Core;

public sealed record KeywordActionCue(string Keyword, KeywordAction Action);

// Local, bounded and ephemeral. Matching follows public CCP KeywordTriggerService's whole-word/regex rules.
public sealed class KeywordTriggerTimeline(ClassicSettings settings)
{
    private readonly StringBuilder buffer = new(1024);
    private readonly Dictionary<string, double> fired = [], words = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Regex?> patterns = [];
    private readonly Queue<KeywordActionCue> pending = new();
    private double lastTime, lastInput, lastGlobal = double.NegativeInfinity;
    private string foreground = "";
    public bool IsRunning { get; private set; }
    public void Start() { Stop(); IsRunning = true; lastTime = lastInput = 0; }
    public void Stop() { IsRunning = false; Clear(); fired.Clear(); words.Clear(); patterns.Clear(); lastGlobal = double.NegativeInfinity; foreground = ""; }
    public void Clear() { buffer.Clear(); pending.Clear(); }

    public void FeedText(string text, string foregroundApp = "")
    {
        if (!IsRunning || !settings.KeywordTriggersEnabled) { Clear(); return; }
        if (!string.Equals(foreground, foregroundApp, StringComparison.OrdinalIgnoreCase)) buffer.Clear();
        foreground = foregroundApp;
        if (!Allows(settings.Keywords.AppScope, settings.Keywords.Apps, foregroundApp)) { Clear(); return; }
        if ((lastTime - lastInput) * 1000 >= settings.Keywords.BufferTimeoutMs) buffer.Clear();
        lastInput = lastTime;
        foreach (var c in text.Take(2048))
        {
            if (c == '\u001b') { Clear(); continue; }
            if (c == '\b') { if (buffer.Length > 0) buffer.Length--; continue; }
            if (!char.IsControl(c) || char.IsWhiteSpace(c)) buffer.Append(c);
            if (buffer.Length > 1024) buffer.Remove(0, buffer.Length - 1024);
        }
        if (lastTime - lastGlobal < settings.Keywords.GlobalCooldownSeconds || buffer.Length == 0) return;
        var matches = new List<KeywordDefinition>(); var matchMilliseconds = 0d; var input = buffer.ToString();
        foreach (var trigger in settings.Keywords.Triggers)
        {
            if (matchMilliseconds >= 25 || pending.Count >= 64) break;
            if (!trigger.Enabled || trigger.Keyword.Length == 0
                || fired.TryGetValue(trigger.Id, out var at) && lastTime - at < trigger.CooldownSeconds
                || words.TryGetValue(trigger.Keyword, out at) && lastTime - at < Math.Max(settings.Keywords.SameWordCooldownSeconds,
                    settings.Keywords.LoopProtection ? settings.Keywords.LoopProtectionMs / 1000d : 0)) continue;
            var listed = trigger.Apps.Any(app => string.Equals(Path.GetFileNameWithoutExtension(app), foregroundApp, StringComparison.OrdinalIgnoreCase));
            if (trigger.AppScope == KeywordAppScope.OnlyListed && !listed || trigger.AppScope == KeywordAppScope.ExceptListed && listed) continue;
            if (Matches(input, trigger, ref matchMilliseconds)) matches.Add(trigger);
        }
        if (matches.Count == 0) return;
        buffer.Clear(); lastGlobal = lastTime;
        foreach (var match in matches)
        {
            fired[match.Id] = lastTime; words[match.Keyword] = lastTime;
            foreach (var action in match.Actions.Where(a => a.Enabled).Take(16))
                if (pending.Count < 64) pending.Enqueue(new(match.Keyword, action));
        }
        foreach (var key in fired.Keys.Where(key => !settings.Keywords.Triggers.Any(t => t.Id == key)).ToArray()) fired.Remove(key);
        foreach (var key in words.Keys.Where(key => !settings.Keywords.Triggers.Any(t => t.Keyword.Equals(key, StringComparison.OrdinalIgnoreCase))).ToArray()) words.Remove(key);
    }

    private static bool Allows(KeywordAppScope scope, IEnumerable<string> apps, string foreground)
    {
        var listed = apps.Any(app => string.Equals(Path.GetFileNameWithoutExtension(app), foreground, StringComparison.OrdinalIgnoreCase));
        return scope == KeywordAppScope.Everywhere || scope == KeywordAppScope.OnlyListed && listed
            || scope == KeywordAppScope.ExceptListed && !listed;
    }

    private bool Matches(string input, KeywordDefinition trigger, ref double matchMilliseconds)
    {
        var key = trigger.Match + ":" + trigger.Keyword;
        if (!patterns.TryGetValue(key, out var regex))
        {
            if (patterns.Count >= 256) patterns.Clear();
            var pattern = trigger.Match == KeywordMatch.Regex ? trigger.Keyword
                : @"\b" + string.Join(@"\s+", trigger.Keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @"\b";
            try { regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(10)); }
            catch (ArgumentException) { regex = null; }
            patterns[key] = regex;
        }
        if (regex is null) return false;
        var started = Stopwatch.GetTimestamp();
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
        finally { matchMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    public IReadOnlyList<KeywordActionCue> Step(double seconds)
    {
        if (!IsRunning) return [];
        if (double.IsFinite(seconds)) lastTime = Math.Max(lastTime, seconds);
        if (!settings.KeywordTriggersEnabled) { Clear(); return []; }
        if ((lastTime - lastInput) * 1000 >= settings.Keywords.BufferTimeoutMs) buffer.Clear();
        var result = pending.ToArray(); pending.Clear(); return result;
    }
}
