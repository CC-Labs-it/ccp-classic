using CCP.Classic.Core.Flash;

namespace CCP.Classic.Core;

public sealed record FlashCue(long Id, int Image, double X, double Y, double Size, double Opacity)
{
    public bool IsRemix { get; init; }
    public FlashExitState? Exit { get; init; }
    public double Width { get; init; } = Size;
    public double Height { get; init; } = Size;
    public double Rotation { get; init; }
    public FlashShatterState? Shatter { get; init; }
    public (double X, double Y, double Width, double Height) HitArea { get; init; } = (X, Y, Size, Size);
}
public sealed record EffectsSnapshot(IReadOnlyList<FlashCue> Flashes, int Phrase, double TextOpacity,
    double PinkOpacity, double SpiralOpacity, double SpiralPhase, int SpiralKind, bool Whisper)
{
    public long SubliminalId { get; init; }
    public SubliminalPhrase? TextCue { get; init; }
    public bool ManualSubliminal { get; init; }
}

// Pure elapsed-time scheduling. The desktop host owns windows, decoded images and recorded audio.
public sealed partial class EffectsTimeline(ClassicSettings settings, int imageCount, int phraseCount, int seed = 0)
{
    private readonly Random random = new(seed);
    private readonly List<Flash> flashes = [];
    private readonly FadeTrack pinkFade = new(), spiralFade = new(), textGain = new();
    private double lastTime, lastFlash, lastText, phase, pink, spiral, textAlpha;
    private int phrase;
    private CueEnvelope? textCue;
    private CueEnvelope? spiralPulse;
    private CueEnvelope? pinkPulse;
    private FlashExitStyle? lastExit;
    private bool requestedWhisper;
    private long nextId, nextTextId, textId;
    private IReadOnlyList<SubliminalPhrase>? phraseSource, configuredPhrases;
    private SubliminalPhrase? capturedPhrase;
    private bool manualText;
    private long audioBatchId;
    public event Func<long, bool>? FlashAudioRequested;
    public Func<double?>? ScheduledRemix { get; set; }
    public bool IsRunning { get; private set; }

    public void Start()
    {
        Stop(); IsRunning = true;
        lastTime = phase = pink = spiral = textAlpha = 0;
        lastFlash = lastText = -1;
        phrase = -1;
        textId = 0;
        textCue = null; capturedPhrase = null; manualText = false; textGain.Reset();
        pinkFade.Reset(); spiralFade.Reset();
    }

    public void Stop() { IsRunning = false; flashes.Clear(); draggedFlash = null; textCue = spiralPulse = pinkPulse = null; capturedPhrase = null; manualText = false; requestedWhisper = false; pink = spiral = textAlpha = 0; }

    public void SetPhrases(IReadOnlyList<SubliminalPhrase> phrases)
    {
        if (ReferenceEquals(phraseSource, phrases)) return;
        phraseSource = phrases;
        configuredPhrases = phrases.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Text)).Take(256)
            .Select(item => new SubliminalPhrase(SubliminalOptions.Text(item.Text), SubliminalOptions.LocalAudio(item.AudioPath))).ToArray();
        phraseCount = configuredPhrases.Count;
        if (phrase >= phraseCount) phrase = -1;
    }

    public bool TriggerSubliminal(string? exactText = null)
    {
        if (!IsRunning) return false;
        var selected = exactText is null ? configuredPhrases?.FirstOrDefault() ?? new SubliminalPhrase("FOCUS")
            : new SubliminalPhrase(SubliminalOptions.Text(exactText));
        if (selected.Text.Length == 0) return false;
        selected = configuredPhrases?.FirstOrDefault(item => string.Equals(item.Text, selected.Text, StringComparison.OrdinalIgnoreCase)) ?? selected;
        capturedPhrase = selected; manualText = true; phrase = -1;
        BeginText(lastTime); requestedWhisper = Enabled("audio") && Value("sub", "whispers") != 0; return true;
    }

    private void BeginText(double now)
    {
        lastText = now; textId = ++nextTextId;
        textCue = new(now, settings.GentleVisuals ? .75 : .08,
            settings.GentleVisuals ? .6 : Value("sub", "frames") / 60, settings.GentleVisuals);
        textGain.Reset(Value("sub", "opacity") / 100);
    }

    public bool TriggerFlash(int? selectedImage = null)
    {
        if (!IsRunning || imageCount == 0 || selectedImage is < 0 || selectedImage >= imageCount) return false;
        var before = flashes.Count; SpawnBatch(lastTime, 1, selectedImage, manual: true); return flashes.Count > before;
    }
    public bool TriggerWhisper(int selectedPhrase = 0)
    {
        if (!IsRunning || selectedPhrase < 0 || selectedPhrase >= phraseCount) return false;
        phrase = selectedPhrase; capturedPhrase = configuredPhrases?[selectedPhrase]; manualText = true;
        BeginText(lastTime);
        requestedWhisper = Enabled("audio") && Value("sub", "whispers") != 0; return true;
    }
    public bool TriggerSpiralPulse()
    {
        if (!IsRunning) return false;
        spiralPulse = new(lastTime, settings.GentleVisuals ? .75 : .3, 1, settings.GentleVisuals); return true;
    }
    public bool TriggerPinkPulse()
    {
        if (!IsRunning) return false;
        pinkPulse = new(lastTime, settings.GentleVisuals ? .75 : .3, 1, settings.GentleVisuals); return true;
    }

    public void SetImageCount(int count)
    {
        imageCount = Math.Max(0, count);
        flashes.RemoveAll(flash => flash.Image >= imageCount);
    }
    public void ReplaceImages(int count)
    {
        flashes.Clear(); draggedFlash = null; SetImageCount(count);
    }

    public EffectsSnapshot Step(double seconds)
    {
        if (!IsRunning) return new([], -1, 0, 0, 0, phase, 0, false);
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime;
        var dt = Math.Clamp(now - lastTime, 0, .1);
        lastTime = now;
        var gentle = settings.GentleVisuals;
        var fade = gentle ? .75 : .3;
        var whisper = requestedWhisper; requestedWhisper = false;
        pink = pinkFade.Sample(now, Enabled("pink") ? Value("pink", "opacity") / 100 * (gentle ? .5 : 1) : 0, fade);
        if (pinkPulse is { } pinkCue)
        {
            pink = Math.Max(pink, pinkCue.Sample(now, gentle) * (gentle ? .1 : .25));
            if (pinkCue.Done) pinkPulse = null;
        }
        spiral = spiralFade.Sample(now, Enabled("spiral") ? Value("spiral", "opacity") / 100 * (gentle ? .5 : 1) : 0, fade);
        if (spiralPulse is { } pulse)
        {
            spiral = Math.Max(spiral, pulse.Sample(now, gentle) * (gentle ? .2 : .4));
            if (pulse.Done) spiralPulse = null;
        }
        if (!settings.ReducedMotion) phase = (phase + dt * Math.Tau / 12 * (gentle ? .5 : 1)) % Math.Tau;

        var flashRate = Value("flash", "rate");
        if (Enabled("flash") && imageCount > 0 && flashRate > 0
            && (lastFlash < 0 ? now >= 2 : now - lastFlash >= 3600 / flashRate))
        {
            var count = settings.Flash.RandomCount ? random.Next(Math.Min(settings.Flash.MinimumCount, settings.Flash.Count),
                Math.Max(settings.Flash.MinimumCount, settings.Flash.Count) + 1) : settings.Flash.Count;
            SpawnBatch(now, count);
            lastFlash = now;
        }
        var output = new List<FlashCue>();
        foreach (var flash in flashes.ToArray())
        {
            if (flash.Shatter is { } shards)
            {
                if (settings.GentleVisuals || settings.ReducedMotion || settings.PerformanceMode) { flash.Shatter = null; flash.Cue.End(now, gentle); }
                else
                {
                    FlashShatter.Step(shards, dt);
                    if (shards.Done) flashes.Remove(flash); else output.Add(FlashOutput(flash));
                    continue;
                }
            }
            if (flash.Exit is null) MoveFlash(flash, dt);
            if (flash.Exit is { } exit)
            {
                if (gentle && exit.DurationSec < .75 || settings.ReducedMotion && exit.Style != FlashExitStyle.Mix)
                { flash.Exit = exit = FlashExit.Soften(exit, gentle ? .75 : .12); flash.ExitStarted = now; }
                flash.Exit = exit = exit with { ElapsedSec = now - flash.ExitStarted };
                if (exit.Done) flashes.Remove(flash);
                else output.Add(FlashOutput(flash));
                continue;
            }
            if (!Enabled("flash") && !flash.Manual) flash.Cue.End(now, gentle);
            var gain = flash.Cue.Ending ? flash.Gain.Value
                : flash.Gain.Sample(now, Value("flash", "opacity") / 100, gentle ? .75 : 0);
            flash.Alpha = flash.Cue.Sample(now, gentle) * gain;
            if (flash.Cue.Done) flashes.Remove(flash);
            else output.Add(FlashOutput(flash));
        }

        var textRate = Value("sub", "rate");
        if (Enabled("sub") && phraseCount > 0 && textRate > 0
            && (lastText < 0 ? now >= 1 : now - lastText >= 60 / textRate && now >= textCue!.EndsAt))
        {
            phrase = (phrase + 1) % phraseCount;
            capturedPhrase = configuredPhrases?[phrase]; manualText = false;
            BeginText(now);
            whisper = Enabled("audio") && Value("sub", "whispers") != 0;
        }
        if (!Enabled("sub") && !manualText) textCue?.End(now, gentle);
        var textOpacity = textCue?.Ending == true ? textGain.Value
            : textGain.Sample(now, Value("sub", "opacity") / 100, gentle ? .75 : 0);
        textAlpha = (textCue?.Sample(now, gentle) ?? 0) * textOpacity;
        return new(output, phrase, textAlpha, pink, spiral, phase,
            (int)Value("spiral", "spiral"), whisper) { SubliminalId = textId, TextCue = capturedPhrase,
                ManualSubliminal = manualText && textCue?.Done == false };
    }

    public bool Dismiss(long id)
    {
        if (!IsRunning || Value("flash", "dismiss") == 0) return false;
        var flash = flashes.FirstOrDefault(f => f.Id == id && f.Exit is null && f.Shatter is null && !f.Cue.Ending && !f.Cue.Done);
        if (flash is null) return false;
        var style = FlashExit.Pick((FlashExitStyle)(int)Math.Clamp(settings.Effects["flash"].Values.GetValueOrDefault("exit"), 0, 6), lastExit, random);
        if (style is null && (settings.GentleVisuals || settings.ReducedMotion)) style = FlashExitStyle.Mix;
        if (style is null && !settings.Flash.Shatter) flashes.Remove(flash);
        else
        {
            lastExit = style; flash.ExitStarted = lastTime;
            flash.Exit = FlashExit.Begin(style ?? FlashExitStyle.Mix, random.Next(), settings.GentleVisuals, settings.ReducedMotion, settings.PerformanceMode);
            flash.Alpha = flash.Cue.Sample(lastTime, settings.GentleVisuals) * flash.Gain.Value;
        }
        if (settings.Flash.Shatter && !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode)
        {
            flash.Exit = null;
            var state = flash.Motion!;
            flash.Shatter = FlashShatter.Create(state.X, state.Y, state.MediaW, state.MediaH, 0, 0, viewportWidth, viewportHeight,
                MotionLevel.Full, random);
            if (state.Style == FlashMotionStyle.Pendulum)
                FlashShatter.TakeOverHangingRect(flash.Shatter, state.PivotX, state.PivotY, state.Rope,
                    state.AngleRad, state.MediaW, state.MediaH);
        }
        while (flashes.Count(item => item.Exit is not null || item.Shatter is not null) > 32)
            flashes.Remove(flashes.First(item => item.Exit is not null || item.Shatter is not null));
        if (!flash.IsRemix && Value("flash", "hydra") != 0 && Enabled("flash"))
        {
            var remaining = settings.Flash.HydraLinkedTiming ? Math.Max(1, flash.Cue.EndsAt - lastTime) : (double?)null;
            var inheritedAudio = settings.Flash.HydraLinkedTiming ? flash.AudioBatch : 0;
            Spawn(lastTime, lifetimeOverride: remaining, audioBatch: inheritedAudio);
            Spawn(lastTime, lifetimeOverride: remaining, audioBatch: inheritedAudio);
        }
        return true;
    }

    private void SpawnBatch(double now, int count, int? selectedImage = null, bool manual = false)
    {
        if (flashes.Count(item => item.Exit is null && item.Shatter is null) >= (int)Value("flash", "maximum")) return;
        var remixAspect = !manual && settings.Flash.RemixEnabled && !settings.GentleVisuals && !settings.ReducedMotion
            && !settings.PerformanceMode && !flashes.Any(flash => flash.IsRemix) ? ScheduledRemix?.Invoke() : null;
        var batch = ++audioBatchId;
        var accepted = settings.Flash.AudioEnabled && FlashAudioRequested?.Invoke(batch) == true;
        if (remixAspect is > 0 && double.IsFinite(remixAspect.Value))
        { SpawnRemix(now, remixAspect.Value, accepted ? batch : 0); return; }
        for (var index = 0; index < count; index++) Spawn(now, selectedImage, manual, audioBatch: accepted ? batch : 0);
    }
    public void CompleteFlashAudio(long batch)
    {
        foreach (var flash in flashes.Where(item => item.AudioBatch == batch))
        {
            flash.AudioBatch = 0;
            if (!settings.Flash.StayUntilPopped) flash.Cue.End(lastTime, settings.GentleVisuals);
        }
    }
    private void Spawn(double now, int? selectedImage = null, bool manual = false, double? lifetimeOverride = null, long audioBatch = 0)
    {
        if (imageCount == 0 || flashes.Count(item => item.Exit is null && item.Shatter is null) >= (int)Value("flash", "maximum")) return;
        var size = Math.Clamp(Value("flash", "size") / 100 * .4, .2, 1);
        var flash = new Flash { Id = ++nextId, Image = selectedImage ?? random.Next(imageCount), Manual = manual, Size = size, AudioBatch = audioBatch,
            X = random.NextDouble() * (1 - size), Y = random.NextDouble() * (1 - size),
            Cue = new(now, Math.Max(Value("flash", "fade") / 1000, settings.GentleVisuals ? .75 : .001),
                settings.Flash.StayUntilPopped || audioBatch > 0 ? double.PositiveInfinity : settings.Flash.HoldSeconds,
                settings.GentleVisuals, lifetimeOverride) };
        InitializeFlash(flash);
        flash.Gain.Reset(Value("flash", "opacity") / 100);
        flashes.Add(flash);
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private double Value(string id, string key) => settings.Effects[id].Values[key];
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private sealed class Flash
    {
        public long Id; public int Image;
        public long AudioBatch;
        public bool Manual, IsRemix;
        public double X, Y, Size, Alpha, Aspect;
        public FlashMotionState? Motion;
        public FlashShatterState? Shatter;
        public CueEnvelope Cue = null!;
        public FlashExitState? Exit;
        public double ExitStarted;
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

        public CueEnvelope(double now, double fade, double hold, bool gentle, double? totalLifetime = null)
        {
            started = now; fadeIn = fadeOut = fade;
            // Linked Hydra inherits the total remaining lifetime, including both fades.
            // The public source's one-second minimum is applied by Dismiss, before this split.
            if (totalLifetime is { } lifetime && double.IsFinite(lifetime))
            { fadeIn = fadeOut = Math.Min(fade, lifetime / 2); hold = Math.Max(0, lifetime - fadeIn - fadeOut); }
            this.hold = hold;
            holdUntil = now + fadeIn + hold; EndsAt = holdUntil + fadeOut;
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
