namespace CCP.Classic.Core;

public sealed record TimedEffectChange(double AtSeconds, string EffectId, bool Enabled)
{
    public static List<TimedEffectChange> Normalize(IEnumerable<TimedEffectChange>? changes) =>
        (changes ?? []).Where(change => change is not null && double.IsFinite(change.AtSeconds)
            && change.AtSeconds >= 0 && change.AtSeconds <= 86400
            && EffectCatalog.All.Any(effect => effect.Id == change.EffectId))
            .Take(64).OrderBy(change => change.AtSeconds).ToList();
}

// A run captures its own immutable schedule. Editing a preset affects the next run.
public sealed class TimedEffectTimeline
{
    private TimedEffectChange[] changes = [];
    private int next;
    private double last;
    public bool IsRunning { get; private set; }
    public void Start(IEnumerable<TimedEffectChange>? schedule)
    {
        changes = TimedEffectChange.Normalize(schedule).ToArray(); next = 0; last = 0; IsRunning = true;
    }
    public void Stop() { changes = []; next = 0; IsRunning = false; }
    public IReadOnlyList<TimedEffectChange> Step(double elapsed)
    {
        if (!IsRunning) return [];
        var now = double.IsFinite(elapsed) ? Math.Max(last, elapsed) : last; last = now;
        var from = next;
        while (next < changes.Length && changes[next].AtSeconds <= now) next++;
        return Array.AsReadOnly(changes[from..next]);
    }
}
