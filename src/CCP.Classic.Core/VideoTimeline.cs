namespace CCP.Classic.Core;

public sealed record VideoCue(long Id, double Elapsed, double CapSeconds,
    bool Floating, bool Strict, bool Attention);

public sealed record VideoSnapshot(IReadOnlyList<VideoCue> Active, IReadOnlyList<VideoCue> Launches);

// Reservations use session time; the player owns caps measured in actual playback time.
public sealed class VideoTimeline(ClassicSettings settings)
{
    private sealed record Reservation(long Id, double Started, double CapSeconds,
        bool Floating, bool Strict, bool Attention, bool FromBubble, bool Manual)
    {
        public VideoCue At(double now) => new(Id, Math.Max(0, now - Started), CapSeconds,
            Floating, Strict, Attention);
    }

    private readonly List<Reservation> active = [];
    private double lastTime, rate, nextDue;
    private bool wasEnabled, launchPending, bubblePending, previewPending;
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
    public bool TryPreview()
    {
        if (!IsRunning || previewPending) return false;
        previewPending = true; return true;
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
        launchPending = bubblePending = previewPending = false;
    }

    public bool Complete(long id)
    {
        var index = active.FindIndex(cue => cue.Id == id);
        if (index < 0) return false;
        var fromBubble = active[index].FromBubble;
        active.RemoveAt(index);
        if (!fromBubble && active.Count == 0) nextDue = lastTime + Interval(rate);
        return true;
    }

    public VideoSnapshot Step(double elapsed, bool launchBlocked = false)
    {
        if (!IsRunning) return new([], []);
        var now = double.IsFinite(elapsed) ? Math.Max(lastTime, elapsed) : lastTime;
        lastTime = now;
        var nextRate = Value("rate", 2, 0, 20);
        var enabled = Enabled;
        if (!enabled)
        {
            if (wasEnabled) active.Clear(); else active.RemoveAll(cue => !cue.Manual);
            launchPending = bubblePending = false;
        }
        if (nextRate != rate || !wasEnabled || !enabled)
            nextDue = now + Interval(nextRate);
        rate = nextRate;
        wasEnabled = enabled;
        if (!enabled && !previewPending && active.Count == 0) return new([], []);

        var strict = Value("strict", 0, 0, 1) != 0;
        var floating = !strict && Value("floating", 1, 0, 1) != 0;
        var maximum = floating ? (int)Value("maximum", 2, 1, 4) : 1;
        active.RemoveAll(cue => cue.Floating != floating);
        if (active.Count > maximum) active.RemoveRange(maximum, active.Count - maximum);

        if (launchBlocked)
        {
            if (launchPending || bubblePending || now >= nextDue) nextDue = now + Interval(rate);
            launchPending = bubblePending = previewPending = false;
            return new(Active, []);
        }

        var fromBubble = bubblePending && floating;
        bubblePending = false;
        var fromPreview = previewPending; previewPending = false;
        var scheduled = enabled && (launchPending || now >= nextDue);
        var launch = scheduled || fromBubble || fromPreview;
        launchPending = false;
        if (!launch) return new(Active, []);
        // A skipped trigger is consumed too. Completing a player does not cause a catch-up burst.
        if (scheduled) nextDue = now + (rate > 0 && floating && !settings.Video.FloatingTogether && maximum > 1
            ? Math.Clamp(Interval(rate) / 4, 30, 600) : Interval(rate));
        if (active.Count >= maximum)
        { if (fromPreview) active.RemoveAt(0); else return new(Active, []); }

        var launches = new List<VideoCue>();
        var count = floating && settings.Video.FloatingTogether && !fromBubble ? maximum - active.Count : 1;
        for (var index = 0; index < count; index++)
        {
            var reservation = new Reservation(++nextId, now, settings.Video.PlaybackCap(fromBubble ? 30 : Value("cap", 8, 1, 30) * 60),
                floating, strict, !floating && Value("attention", 1, 0, 1) != 0, fromBubble, fromPreview);
            active.Add(reservation); launches.Add(reservation.At(now));
        }
        return new(Active, launches);
    }

    private bool Enabled => settings.Effects.TryGetValue("video", out var effect) && effect.Enabled;
    private double Value(string key, double fallback, double min, double max) =>
        settings.Effects.TryGetValue("video", out var effect) && effect.Values.TryGetValue(key, out var value)
            && double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static double Interval(double frequency) => frequency > 0 ? 3600 / frequency : double.PositiveInfinity;
}
