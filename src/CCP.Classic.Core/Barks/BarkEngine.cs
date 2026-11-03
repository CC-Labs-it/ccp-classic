namespace CCP.Classic.Core;

public sealed record BarkPick(BarkRule Rule, BarkLine Line);

// CCP's bark gate, pure: event key -> rules in priority order -> conditions, once-per-session latch,
// per-rule cooldown, chance -> the global gap (skipped only by Always rules) -> a line that is not the
// last one spoken and not reused inside its rule until the rule's pool is exhausted.
// A panic holds every bark, Always rules included, for PanicQuietSeconds.
public sealed class BarkEngine(BarkLibrary library, Func<double> clock, Func<double>? random = null)
{
    public const double PanicQuietSeconds = 30, DefaultGapSeconds = 45;
    private readonly Func<double> next = random ?? Random.Shared.NextDouble;
    private readonly Dictionary<string, double> lastFired = new(StringComparer.Ordinal);
    private readonly HashSet<string> firedThisSession = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> used = new(StringComparer.Ordinal);
    private double lastSpoke = double.NegativeInfinity, quietUntil = double.NegativeInfinity;
    private string? lastClip;
    public BarkLibrary Library => library;
    public double MinimumGapSeconds { get; set; } = DefaultGapSeconds;
    public bool IsQuiet => Now < quietUntil;

    public void Panic() => quietUntil = Now + PanicQuietSeconds;
    // Session-scoped one-shots may fire again in a new session.
    public void BeginSession() => firedThisSession.Clear();

    // Busy: another bark is showing; only an Always rule replaces it.
    public BarkPick? Raise(string trigger, IReadOnlyDictionary<string, double>? facts = null, bool busy = false)
    {
        var now = Now;
        if (now < quietUntil) return null;
        foreach (var rule in library.For(trigger))
        {
            if (!Matches(rule, facts) || !rule.Repeatable && firedThisSession.Contains(rule.Id)) continue;
            if (rule.CooldownSeconds > 0 && lastFired.TryGetValue(rule.Id, out var at) && now - at < rule.CooldownSeconds) continue;
            if (rule.Chance < 1 && next() >= rule.Chance) continue;
            if (!rule.Always && (busy || now - lastSpoke < MinimumGapSeconds)) return null;
            var line = PickLine(rule);
            lastSpoke = lastFired[rule.Id] = now; lastClip = line.Clip;
            if (!rule.Repeatable) firedThisSession.Add(rule.Id);
            return new(rule, line);
        }
        return null;
    }

    private BarkLine PickLine(BarkRule rule)
    {
        if (!used.TryGetValue(rule.Id, out var spent)) used[rule.Id] = spent = new(StringComparer.Ordinal);
        var fresh = rule.Lines.Where(line => !spent.Contains(line.Clip) && line.Clip != lastClip).ToArray();
        if (fresh.Length == 0)
        {
            spent.Clear();
            fresh = rule.Lines.Where(line => line.Clip != lastClip).ToArray();
            if (fresh.Length == 0) fresh = rule.Lines.ToArray();
        }
        var line = fresh[Math.Clamp((int)(next() * fresh.Length), 0, fresh.Length - 1)];
        spent.Add(line.Clip); return line;
    }

    private static bool Matches(BarkRule rule, IReadOnlyDictionary<string, double>? facts)
    {
        foreach (var (key, range) in rule.When)
            if (facts is null || !facts.TryGetValue(key, out var value) || !range.Contains(value)) return false;
        return true;
    }

    private double Now { get { var now = clock(); return double.IsFinite(now) ? now : 0; } }
}
