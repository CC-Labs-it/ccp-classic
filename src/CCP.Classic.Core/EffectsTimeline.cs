namespace CCP.Classic.Core;

public sealed record FlashCue(long Id, int Image, double X, double Y, double Size, double Opacity);
public sealed record EffectsSnapshot(IReadOnlyList<FlashCue> Flashes, int Phrase, double TextOpacity,
    double PinkOpacity, double SpiralOpacity, double SpiralPhase, int SpiralKind, bool Whisper);

// Pure elapsed-time scheduling. The desktop host owns windows, decoded images and recorded audio.
public sealed class EffectsTimeline(ClassicSettings settings, int imageCount, int phraseCount, int seed = 0)
{
    private readonly Random random = new(seed);
    private readonly List<Flash> flashes = [];
    private readonly FadeTrack pinkFade = new(), spiralFade = new(), textGain = new();
    private double lastTime, lastFlash, lastText, phase, pink, spiral, textAlpha;
    private int phrase;
    private CueEnvelope? textCue;
    private long nextId;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop(); IsRunning = true;
        lastTime = phase = pink = spiral = textAlpha = 0;
        lastFlash = lastText = -1;
        phrase = -1;
        textCue = null; textGain.Reset();
        pinkFade.Reset(); spiralFade.Reset();
    }

    public void Stop() { IsRunning = false; flashes.Clear(); textCue = null; pink = spiral = textAlpha = 0; }

    public void SetImageCount(int count)
    {
        imageCount = Math.Max(0, count);
        flashes.RemoveAll(flash => flash.Image >= imageCount);
    }

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
        foreach (var flash in flashes.ToArray())
        {
            if (!Enabled("flash")) flash.Cue.End(now, gentle);
            var gain = flash.Cue.Ending ? flash.Gain.Value
                : flash.Gain.Sample(now, Value("flash", "opacity") / 100, gentle ? .75 : 0);
            flash.Alpha = flash.Cue.Sample(now, gentle) * gain;
            if (flash.Cue.Done) flashes.Remove(flash);
            else output.Add(new(flash.Id, flash.Image, flash.X, flash.Y, flash.Size, flash.Alpha));
        }

        var textRate = Value("sub", "rate");
        var textFade = gentle ? .75 : .08;
        var hold = gentle ? .6 : Value("sub", "frames") / 60;
        if (Enabled("sub") && phraseCount > 0 && textRate > 0
            && (lastText < 0 ? now >= 1 : now - lastText >= 60 / textRate && now >= textCue!.EndsAt))
        {
            phrase = (phrase + 1) % phraseCount;
            lastText = now;
            textCue = new(now, textFade, hold, gentle);
            textGain.Reset(Value("sub", "opacity") / 100);
            whisper = Enabled("audio") && Value("sub", "whispers") != 0;
        }
        if (!Enabled("sub")) textCue?.End(now, gentle);
        var textOpacity = textCue?.Ending == true ? textGain.Value
            : textGain.Sample(now, Value("sub", "opacity") / 100, gentle ? .75 : 0);
        textAlpha = (textCue?.Sample(now, gentle) ?? 0) * textOpacity;
        return new(output, phrase, textAlpha, pink, spiral, phase,
            (int)Value("spiral", "spiral"), whisper);
    }

    public bool Dismiss(long id)
    {
        if (!IsRunning || Value("flash", "dismiss") == 0) return false;
        var flash = flashes.FirstOrDefault(f => f.Id == id && !f.Cue.Ending && !f.Cue.Done);
        if (flash is null) return false;
        flash.Cue.End(lastTime, settings.GentleVisuals);
        if (Value("flash", "hydra") != 0 && Enabled("flash")) { Spawn(lastTime); Spawn(lastTime); }
        return true;
    }

    private void Spawn(double now)
    {
        if (imageCount == 0 || flashes.Count >= (int)Value("flash", "maximum")) return;
        var size = Math.Clamp(Value("flash", "size") / 100, .1, 1);
        var flash = new Flash { Id = ++nextId, Image = random.Next(imageCount), Size = size,
            X = random.NextDouble() * (1 - size), Y = random.NextDouble() * (1 - size),
            Cue = new(now, Math.Max(Value("flash", "fade") / 1000, settings.GentleVisuals ? .75 : .05), 4, settings.GentleVisuals) };
        flash.Gain.Reset(Value("flash", "opacity") / 100);
        flashes.Add(flash);
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private double Value(string id, string key) => settings.Effects[id].Values[key];
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private sealed class Flash
    {
        public long Id; public int Image;
        public double X, Y, Size, Alpha;
        public CueEnvelope Cue = null!;
        public readonly FadeTrack Gain = new();
    }
    private sealed class CueEnvelope
    {
        private double started, fadeIn, fadeOut, inFrom, holdUntil, outFrom = 1, value;
        private readonly double hold;
        private bool gentleApplied;
        public double EndsAt { get; private set; }
        public bool Ending { get; private set; }
        public bool Done { get; private set; }

        public CueEnvelope(double now, double fade, double hold, bool gentle)
        {
            started = now; fadeIn = fadeOut = fade; this.hold = hold;
            holdUntil = now + fade + hold; EndsAt = holdUntil + fade;
            gentleApplied = gentle;
        }

        public double Sample(double now, bool gentle)
        {
            // Check the captured end before any retiming: an expired word must never return.
            if (Done || now >= EndsAt) { Done = true; return value = 0; }
            if (gentle && !gentleApplied)
            {
                gentleApplied = true;
                fadeIn = Math.Max(fadeIn, .75); fadeOut = Math.Max(fadeOut, .75);
                if (Ending || now >= holdUntil)
                { outFrom = value; holdUntil = now; EndsAt = now + fadeOut; }
                else if (value < 1)
                { inFrom = value; started = now; holdUntil = now + fadeIn + hold; EndsAt = holdUntil + fadeOut; }
                else EndsAt = holdUntil + fadeOut;
            }
            value = now >= holdUntil ? outFrom * (1 - Smooth((now - holdUntil) / fadeOut))
                : now < started + fadeIn ? inFrom + (1 - inFrom) * Smooth((now - started) / fadeIn) : 1;
            return value;
        }

        public void End(double now, bool gentle)
        {
            if (Ending || Done) return;
            Ending = true;
            if (now >= EndsAt || value <= 0) { Done = true; value = 0; return; }
            fadeOut = Math.Max(fadeOut, gentle ? .75 : 0);
            gentleApplied |= gentle;
            outFrom = value; holdUntil = now; EndsAt = now + fadeOut;
        }
    }
    private sealed class FadeTrack
    {
        private double from, target, value, started, duration;
        public double Value => value;
        public void Reset(double initial = 0) { from = target = value = initial; started = duration = 0; }
        public double Sample(double now, double next, double fade)
        {
            if (next != target || fade > duration)
            { from = value; target = next; started = now; duration = fade; }
            value = duration > 0 ? from + (target - from) * Smooth((now - started) / duration) : target;
            return value;
        }
    }
}
