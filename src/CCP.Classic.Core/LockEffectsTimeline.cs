namespace CCP.Classic.Core;

public sealed record LockCardCue(long Id, string Phrase, int Repetitions, int Completed, bool Strict, double Opacity);
public sealed record MindWipeCue(long Id, double Volume, bool Loop);
public sealed record BrainDrainCue(double Intensity, double Opacity, double Pulse, bool HighRefresh);
public sealed record LockEffectsFrame(LockCardCue? LockCard, MindWipeCue? MindWipe, BrainDrainCue? BrainDrain)
{
    public static LockEffectsFrame Empty { get; } = new(null, null, null);
}

// The host owns media and routes every manual stop through the shared session panic gate.
public sealed class LockEffectsTimeline(ClassicSettings settings)
{
    private static readonly string[] Words = ["Breathe", "Relax", "Focus", "Listen", "Deeper"];
    private static readonly Dictionary<string, Dictionary<string, KnobDefinition>> Knobs = EffectCatalog.All
        .ToDictionary(effect => effect.Id, effect => effect.Primary.Concat(effect.More).ToDictionary(knob => knob.Key));
    private readonly EaseTrack drainFade = new();
    private Card? card;
    private long nextId, wipeId;
    private double lastTime, nextLock, nextWipe, lockInterval, wipeInterval, drainPhase;
    private int phrase;
    private bool wipeEnabled, previousLoop, loopPending;
    public bool IsRunning { get; private set; }
    public bool CanDismissLock => card is { } current && (!current.Strict || current.Completed >= current.Repetitions);
    public event Action<InteractiveEffectEvent>? EventOccurred;

    public void Start()
    {
        Stop(); IsRunning = true; lastTime = drainPhase = 0; phrase = 0;
        lockInterval = Value("lock", "interval") * 60; nextLock = lockInterval;
        wipeInterval = Value("wipe", "interval") * 60; nextWipe = wipeInterval;
        wipeEnabled = Enabled("wipe"); previousLoop = Value("wipe", "loop") != 0;
        loopPending = wipeEnabled && previousLoop;
    }

    public void Stop()
    {
        IsRunning = false; card = null; wipeId = 0; loopPending = false; drainFade.Reset();
    }

    public bool SubmitLock(string text) => card is { } current && SubmitLock(current.Id, text);
    public bool SubmitLock(long id, string text)
    {
        if (!IsRunning || !Enabled("lock") || card is not { Ending: false } current
            || current.Id != id || !string.Equals(text, current.Phrase, StringComparison.Ordinal)) return false;
        current.Completed++;
        if (current.Completed >= current.Repetitions)
        {
            current.Ending = true;
            EventOccurred?.Invoke(new(InteractiveEffectEventKind.LockCompleted));
        }
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

    public bool CancelMindWipe(long id) => ReleaseWipe(id);
    private bool ReleaseWipe(long id)
    {
        if (!IsRunning || id <= 0 || wipeId != id) return false;
        wipeId = 0; loopPending = false; nextWipe = lastTime + Value("wipe", "interval") * 60;
        return true;
    }

    public LockEffectsFrame Step(double seconds)
    {
        if (!IsRunning) return LockEffectsFrame.Empty;
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime;
        var dt = Math.Clamp(now - lastTime, 0, .1); lastTime = now;
        var fade = settings.GentleVisuals ? .75 : .25;
        var interval = Value("lock", "interval") * 60;
        if (interval != lockInterval || !Enabled("lock")) nextLock = now + interval;
        lockInterval = interval;
        if (Enabled("lock") && now >= nextLock)
        {
            nextLock = now + interval;
            card ??= new(++nextId, Words[phrase++ % Words.Length], (int)Value("lock", "repeats"), Value("lock", "strict") != 0);
        }
        LockCardCue? lockCue = null;
        if (card is { } current)
        {
            current.Ending |= !Enabled("lock");
            var alpha = current.Fade.Sample(now, current.Ending ? 0 : 1, fade);
            if (current.Ending && alpha == 0) card = null;
            else lockCue = new(current.Id, current.Phrase, current.Repetitions, current.Completed, current.Strict, alpha);
        }

        var enabled = Enabled("wipe");
        var loop = Value("wipe", "loop") != 0;
        interval = Value("wipe", "interval") * 60;
        if (interval != wipeInterval || !enabled || !wipeEnabled) nextWipe = now + interval;
        if (enabled && loop && (!wipeEnabled || !previousLoop)) loopPending = true;
        if (!enabled) { wipeId = 0; loopPending = false; }
        if (!loop) loopPending = false;
        wipeInterval = interval; wipeEnabled = enabled; previousLoop = loop;
        if (enabled && wipeId == 0 && (loopPending || now >= nextWipe))
        { wipeId = ++nextId; loopPending = false; nextWipe = now + interval; }
        var wipe = wipeId > 0 ? new MindWipeCue(wipeId, Value("wipe", "volume") / 100, loop) : null;

        var intensity = Value("drain", "intensity") / 100;
        var drainEnabled = Enabled("drain") && intensity > 0;
        if (drainEnabled && !settings.ReducedMotion)
            drainPhase = (drainPhase + dt * Math.Tau / (settings.GentleVisuals ? 12 : 6)) % Math.Tau;
        var opacity = drainFade.Sample(now, drainEnabled ? intensity * (settings.GentleVisuals ? .15 : .3) : 0, fade);
        var drain = drainEnabled || opacity > 0 ? new BrainDrainCue(intensity, opacity, .5 + .5 * Math.Sin(drainPhase),
            Value("drain", "refresh") != 0 && !settings.GentleVisuals && !settings.PerformanceMode && !settings.ReducedMotion) : null;
        return new(lockCue, wipe, drain);
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
        public bool Ending;
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
