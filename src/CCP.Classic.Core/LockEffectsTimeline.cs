namespace CCP.Classic.Core;

public sealed record LockCardCue(long Id, string Phrase, int Repetitions, int Completed, bool Strict, double Opacity);
public sealed record MindWipeCue(long Id, double Volume, bool Loop);
public sealed record LockEffectsFrame(LockCardCue? LockCard, MindWipeCue? MindWipe)
{
    public static LockEffectsFrame Empty { get; } = new(null, null);
}

// The host owns media and routes every manual stop through the shared session panic gate.
public sealed class LockEffectsTimeline(ClassicSettings settings, Random? random = null)
{
    private readonly Random rng = random ?? new();
    private readonly Queue<string> recentPhrases = new();
    private static readonly Dictionary<string, Dictionary<string, KnobDefinition>> Knobs = EffectCatalog.All
        .ToDictionary(effect => effect.Id, effect => effect.Primary.Concat(effect.More).ToDictionary(knob => knob.Key));
    private Card? card;
    private long nextId, wipeId;
    private double lastTime, nextLock, nextWipe, lockInterval, wipeInterval;
    private bool wipeEnabled, previousLoop, loopPending, manualWipe;
    public bool IsRunning { get; private set; }
    public bool VideoBlocking { get; set; }
    public bool CanDismissLock => card is { } current && (!current.Strict || current.Completed >= current.Repetitions);
    public event Action<InteractiveEffectEvent>? EventOccurred;

    public void Start()
    {
        Stop(); IsRunning = true; lastTime = 0; recentPhrases.Clear();
        lockInterval = 3600 / Value("lock", "rate");
        nextLock = rng.NextDouble() * (settings.RunMinutes > 0 ? Math.Min(lockInterval, settings.RunMinutes * 48) : lockInterval);
        wipeInterval = 3600 / Value("wipe", "rate"); nextWipe = wipeInterval;
        wipeEnabled = Enabled("wipe"); previousLoop = Value("wipe", "loop") != 0;
        loopPending = wipeEnabled && previousLoop;
    }

    public void Stop()
    {
        IsRunning = false; card = null; wipeId = 0; loopPending = manualWipe = false;
    }

    public bool SubmitLock(string text) => card is { } current && SubmitLock(current.Id, text);
    public bool SubmitLock(long id, string text)
    {
        if (!IsRunning || card is not { Ending: false } current
            || current.Id != id || !LockCardText.Matches(text, current.Phrase)) return false;
        current.Completed++;
        if (current.Completed >= current.Repetitions)
        {
            current.Ending = true;
            EventOccurred?.Invoke(new(InteractiveEffectEventKind.LockCompleted));
        }
        return true;
    }

    public void RegisterTypo(long id)
    {
        if (settings.LockCard.ResetOnTypo && card is { Ending: false } current && current.Id == id)
            current.Completed = 0;
    }

    public bool TriggerLock(bool scheduled = false, bool preview = false)
    {
        if (!IsRunning || card is not null || VideoBlocking && !preview) return false;
        var pool = settings.LockCard.Phrases.Where(p => p.Value && !string.IsNullOrWhiteSpace(p.Key)).Select(p => p.Key).ToArray();
        if (pool.Length == 0) return false;
        while (recentPhrases.Count >= pool.Length) recentPhrases.Dequeue();
        var available = pool.Where(p => !recentPhrases.Contains(p)).ToArray();
        if (available.Length == 0) available = pool;
        var phrase = available[rng.Next(available.Length)];
        recentPhrases.Enqueue(phrase); while (recentPhrases.Count > 3) recentPhrases.Dequeue();
        card = new(++nextId, phrase, settings.LockCard.ResolveRepeats(phrase, (int)Value("lock", "repeats"), rng.NextDouble()), Value("lock", "strict") != 0 && !preview) { Manual = !scheduled };
        return true;
    }

    // The caller must first allow this gesture through SessionController's stop policy.
    public bool DismissLock()
    {
        if (!IsRunning || !CanDismissLock || card!.Ending) return false;
        card.Ending = true; return true;
    }

    public bool CompleteMindWipe(long id)
    {
        if (!ReleaseWipe(id)) return false;
        loopPending = Enabled("wipe") && Value("wipe", "loop") != 0;
        EventOccurred?.Invoke(new(InteractiveEffectEventKind.WipeCompleted));
        return true;
    }

    public bool TriggerMindWipe()
    {
        if (!IsRunning || wipeId != 0) return false;
        wipeId = ++nextId; manualWipe = true; return true;
    }
    public bool CancelMindWipe(long id) => ReleaseWipe(id);
    private bool ReleaseWipe(long id)
    {
        if (!IsRunning || id <= 0 || wipeId != id) return false;
        wipeId = 0; loopPending = manualWipe = false; nextWipe = lastTime + 3600 / Value("wipe", "rate");
        return true;
    }

    public LockEffectsFrame Step(double seconds)
    {
        if (!IsRunning) return LockEffectsFrame.Empty;
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime;
        var dt = Math.Clamp(now - lastTime, 0, .1); lastTime = now;
        var fade = settings.GentleVisuals ? .75 : .25;
        var interval = 3600 / Value("lock", "rate");
        if (interval != lockInterval || !Enabled("lock")) nextLock = now + interval;
        lockInterval = interval;
        if (Enabled("lock") && !VideoBlocking && now >= nextLock)
        {
            nextLock = now + interval * (.7 + rng.NextDouble() * .6);
            TriggerLock(scheduled: true);
        }
        LockCardCue? lockCue = null;
        if (card is { } current)
        {
            current.Ending |= !Enabled("lock") && !current.Manual && !current.Strict;
            var alpha = current.Fade.Sample(now, current.Ending ? 0 : 1, fade);
            if (current.Ending && alpha == 0) card = null;
            else lockCue = new(current.Id, current.Phrase, current.Repetitions, current.Completed, current.Strict, alpha);
        }

        var enabled = Enabled("wipe");
        var loop = Value("wipe", "loop") != 0;
        interval = 3600 / Value("wipe", "rate");
        if (interval != wipeInterval || !enabled || !wipeEnabled) nextWipe = now + interval;
        if (enabled && loop && (!wipeEnabled || !previousLoop)) loopPending = true;
        if (!enabled && !manualWipe) { wipeId = 0; loopPending = false; }
        if (!loop) loopPending = false;
        wipeInterval = interval; wipeEnabled = enabled; previousLoop = loop;
        if (enabled && wipeId == 0 && (loopPending || now >= nextWipe))
        { wipeId = ++nextId; loopPending = false; nextWipe = now + interval; }
        var wipe = wipeId > 0 ? new MindWipeCue(wipeId, Value("wipe", "volume") / 100, loop && enabled) : null;

        return new(lockCue, wipe);
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private double Value(string id, string key)
    {
        var knob = Knobs[id][key];
        return settings.Effects[id].Values.TryGetValue(key, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, knob.Min, knob.Max) : knob.Default;
    }
    private sealed class Card(long id, string phrase, int repetitions, bool strict)
    {
        public readonly long Id = id;
        public readonly string Phrase = phrase;
        public readonly int Repetitions = repetitions;
        public readonly bool Strict = strict;
        public int Completed;
        public bool Ending, Manual;
        public readonly EaseTrack Fade = new();
    }
    private sealed class EaseTrack
    {
        private double from, target, value, started, duration;
        public void Reset() { from = target = value = started = duration = 0; }
        public double Sample(double now, double next, double fade)
        {
            if (next != target || fade > duration)
            { from = value; target = next; started = now; duration = fade; }
            var t = duration > 0 ? Math.Clamp((now - started) / duration, 0, 1) : 1;
            return value = from + (target - from) * t * t * (3 - 2 * t);
        }
    }
}
