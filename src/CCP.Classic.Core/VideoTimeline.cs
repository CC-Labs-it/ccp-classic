namespace CCP.Classic.Core;

public sealed record VideoCue(long Id, double Elapsed, double CapSeconds,
    bool Floating, bool Strict, bool Attention);

public sealed record VideoSnapshot(IReadOnlyList<VideoCue> Active, IReadOnlyList<VideoCue> Launches);

// Reservations use session time; the player owns caps measured in actual playback time.
public sealed class VideoTimeline(ClassicSettings settings)
{
    private sealed record Reservation(long Id, double Started, double CapSeconds,
        bool Floating, bool Strict, bool Attention)
    {
        public VideoCue At(double now) => new(Id, Math.Max(0, now - Started), CapSeconds,
            Floating, Strict, Attention);
    }

    private readonly List<Reservation> active = [];
    private double lastTime, rate, nextDue;
    private bool wasEnabled, launchPending, bubblePending;
    private long nextId;
    public bool IsRunning { get; private set; }
    public IReadOnlyList<VideoCue> Active => active.Select(cue => cue.At(lastTime)).ToArray();
    public bool CanTriggerBubble => IsRunning && Enabled && !bubblePending
        && Value("strict", 0, 0, 1) == 0 && Value("floating", 1, 0, 1) != 0
        && active.Count < (int)Value("maximum", 2, 1, 4);

    public bool TryTriggerBubble()
    {
        if (!CanTriggerBubble) return false;
        bubblePending = true; return true;
    }

    public void Start()
    {
        Stop();
        IsRunning = true;
        lastTime = 0;
        rate = Value("rate", 2, 0, 20);
        nextDue = Interval(rate);
        wasEnabled = Enabled;
        launchPending = wasEnabled && Value("launch", 0, 0, 1) != 0;
    }

    public void Stop()
    {
        IsRunning = false;
        active.Clear();
        launchPending = bubblePending = false;
    }

    public bool Complete(long id)
    {
        var index = active.FindIndex(cue => cue.Id == id);
        if (index < 0) return false;
        active.RemoveAt(index);
        return true;
    }

    public VideoSnapshot Step(double elapsed)
    {
        if (!IsRunning) return new([], []);
        var now = double.IsFinite(elapsed) ? Math.Max(lastTime, elapsed) : lastTime;
        lastTime = now;
        var nextRate = Value("rate", 2, 0, 20);
        var enabled = Enabled;
        if (!enabled)
        {
            active.Clear();
            launchPending = bubblePending = false;
        }
        if (nextRate != rate || !wasEnabled || !enabled)
            nextDue = now + Interval(nextRate);
        rate = nextRate;
        wasEnabled = enabled;
        if (!enabled) return new([], []);

        var strict = Value("strict", 0, 0, 1) != 0;
        var floating = !strict && Value("floating", 1, 0, 1) != 0;
        var maximum = floating ? (int)Value("maximum", 2, 1, 4) : 1;
        active.RemoveAll(cue => cue.Floating != floating);
        if (active.Count > maximum) active.RemoveRange(maximum, active.Count - maximum);

        var fromBubble = bubblePending && floating;
        bubblePending = false;
        var scheduled = launchPending || now >= nextDue;
        var launch = scheduled || fromBubble;
        launchPending = false;
        if (!launch) return new(Active, []);
        // A skipped trigger is consumed too. Completing a player does not cause a catch-up burst.
        if (scheduled) nextDue = now + Interval(rate);
        if (active.Count >= maximum) return new(Active, []);

        var reservation = new Reservation(++nextId, now, fromBubble ? 30 : Value("cap", 8, 1, 30) * 60,
            floating, strict, !floating && Value("attention", 1, 0, 1) != 0);
        active.Add(reservation);
        return new(Active, [reservation.At(now)]);
    }

    private bool Enabled => settings.Effects.TryGetValue("video", out var effect) && effect.Enabled;
    private double Value(string key, double fallback, double min, double max) =>
        settings.Effects.TryGetValue("video", out var effect) && effect.Values.TryGetValue(key, out var value)
            && double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static double Interval(double frequency) => frequency > 0 ? 3600 / frequency : double.PositiveInfinity;
}
