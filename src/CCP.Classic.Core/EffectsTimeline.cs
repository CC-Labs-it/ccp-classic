namespace CCP.Classic.Core;

public sealed record FlashCue(long Id, int Image, double X, double Y, double Size, double Opacity);
public sealed record EffectsSnapshot(IReadOnlyList<FlashCue> Flashes, int Phrase, double TextOpacity,
    double PinkOpacity, double SpiralOpacity, double SpiralPhase, int SpiralKind, bool Whisper);

// Pure elapsed-time scheduling. The desktop host owns windows, decoded images and recorded audio.
public sealed class EffectsTimeline(ClassicSettings settings, int imageCount, int phraseCount, int seed = 0)
{
    private readonly Random random = new(seed);
    private readonly List<Flash> flashes = [];
    private readonly FadeTrack pinkFade = new(), spiralFade = new();
    private double lastTime, lastFlash, lastText, textStarted, phase, pink, spiral, textAlpha;
    private int phrase;
    private double textEnding = -1, textEndAlpha;
    private long nextId;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop(); IsRunning = true;
        lastTime = phase = pink = spiral = textAlpha = 0;
        lastFlash = lastText = textStarted = -1;
        phrase = -1;
        textEnding = -1;
        pinkFade.Reset(); spiralFade.Reset();
    }

    public void Stop() { IsRunning = false; flashes.Clear(); pink = spiral = 0; }

    public EffectsSnapshot Step(double seconds)
    {
        if (!IsRunning) return new([], -1, 0, 0, 0, phase, 0, false);
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime;
        var dt = Math.Clamp(now - lastTime, 0, .1);
        lastTime = now;
        var gentle = settings.GentleVisuals;
        var fade = gentle ? .75 : .3;
        var whisper = false;
        pink = pinkFade.Sample(now, Enabled("pink") ? Value("pink", "opacity") / 100 * (gentle ? .5 : 1) : 0, fade);
        spiral = spiralFade.Sample(now, Enabled("spiral") ? Value("spiral", "opacity") / 100 * (gentle ? .5 : 1) : 0, fade);
        if (!settings.ReducedMotion) phase = (phase + dt * Math.Tau / 12 * (gentle ? .5 : 1)) % Math.Tau;

        var flashRate = Value("flash", "rate");
        if (Enabled("flash") && imageCount > 0 && flashRate > 0
            && (lastFlash < 0 ? now >= 2 : now - lastFlash >= 3600 / flashRate))
        {
            Spawn(now); lastFlash = now;
        }
        var output = new List<FlashCue>();
        var flashFade = Math.Max(Value("flash", "fade") / 1000, gentle ? .75 : .05);
        foreach (var flash in flashes.ToArray())
        {
            if (!Enabled("flash") && flash.Ending < 0) { flash.Ending = now; flash.EndAlpha = flash.Alpha; }
            var age = now - flash.Started;
            var target = Envelope(age, flashFade, 4) * Value("flash", "opacity") / 100;
            if (flash.Ending >= 0) target = flash.EndAlpha * (1 - Smooth((now - flash.Ending) / flashFade));
            flash.Alpha = target;
            if (flash.Alpha <= 0 && (flash.Ending >= 0 || age >= flashFade * 2 + 4)) flashes.Remove(flash);
            else output.Add(new(flash.Id, flash.Image, flash.X, flash.Y, flash.Size, flash.Alpha));
        }

        var textRate = Value("sub", "rate");
        var textFade = gentle ? .75 : .08;
        var hold = gentle ? .6 : Value("sub", "frames") / 60;
        if (Enabled("sub") && phraseCount > 0 && textRate > 0
            && (lastText < 0 ? now >= 1 : now - lastText >= Math.Max(60 / textRate, textFade * 2 + hold)))
        {
            phrase = (phrase + 1) % phraseCount;
            lastText = textStarted = now;
            textEnding = -1;
            whisper = Enabled("audio") && Value("sub", "whispers") != 0;
        }
        if (!Enabled("sub") && textEnding < 0) { textEnding = now; textEndAlpha = textAlpha; }
        textAlpha = textEnding >= 0 ? textEndAlpha * (1 - Smooth((now - textEnding) / textFade))
            : textStarted >= 0 ? Envelope(now - textStarted, textFade, hold) * Value("sub", "opacity") / 100 : 0;
        return new(output, phrase, textAlpha, pink, spiral, phase,
            (int)Value("spiral", "spiral"), whisper);
    }

    public bool Dismiss(long id)
    {
        if (!IsRunning || Value("flash", "dismiss") == 0) return false;
        var flash = flashes.FirstOrDefault(f => f.Id == id && f.Ending < 0);
        if (flash is null) return false;
        flash.Ending = lastTime;
        flash.EndAlpha = flash.Alpha;
        if (Value("flash", "hydra") != 0 && Enabled("flash")) { Spawn(lastTime); Spawn(lastTime); }
        return true;
    }

    private void Spawn(double now)
    {
        if (imageCount == 0 || flashes.Count >= (int)Value("flash", "maximum")) return;
        var size = Math.Clamp(Value("flash", "size") / 100, .1, 1);
        flashes.Add(new() { Id = ++nextId, Image = random.Next(imageCount), Started = now, Size = size,
            X = random.NextDouble() * (1 - size), Y = random.NextDouble() * (1 - size) });
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private double Value(string id, string key) => settings.Effects[id].Values[key];
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static double Envelope(double age, double fade, double hold)
    {
        if (age < 0 || age >= fade * 2 + hold) return 0;
        var t = age < fade ? age / fade : age <= fade + hold ? 1 : (fade * 2 + hold - age) / fade;
        return Smooth(t);
    }
    private sealed class Flash
    {
        public long Id; public int Image;
        public double X, Y, Size, Started, Alpha, EndAlpha, Ending = -1;
    }
    private sealed class FadeTrack
    {
        private double from, target, value, started, duration;
        public void Reset() => from = target = value = started = duration = 0;
        public double Sample(double now, double next, double fade)
        {
            if (next != target || fade > duration)
            { from = value; target = next; started = now; duration = fade; }
            value = duration > 0 ? from + (target - from) * Smooth((now - started) / duration) : target;
            return value;
        }
    }
}
