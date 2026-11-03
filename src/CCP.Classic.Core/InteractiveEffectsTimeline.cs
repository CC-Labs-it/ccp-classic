namespace CCP.Classic.Core;

public enum BubbleEffectKind { None, Flash, Subliminal, Pink, Spiral, BrainDrain, Video, Glitch, Cascade, BrainDrainWash }
public sealed record BubbleCue(long Id, double X, double Y, double Size, double Opacity, bool CanPop,
    BubbleEffectKind Effect = BubbleEffectKind.None, int Image = -1, double PopProgress = 0)
{
    public BubbleAnimation? Animation { get; init; }
    public double DiameterDip { get; init; }
}
public sealed record BouncingTextCue(string Text, double X, double Y, double Size, double Opacity)
{
    public string Colour { get; init; } = "#FF69B4";
    public string FontFamily { get; init; } = "Inter";
    public double ScaleX { get; init; } = 1;
    public double ScaleY { get; init; } = 1;
    public double Angle { get; init; }
    public bool Outline { get; init; }
    public double BurstSeconds { get; init; } = -1;
    public double BurstCenterX { get; init; }
    public double BurstCenterY { get; init; }
}
public sealed record BounceMotion(double X, double Y, double VelocityX, double VelocityY);
public enum InteractiveEffectEventKind { BubblePopped, CornerHit, LockCompleted, WipeCompleted, WallHit }
public sealed record InteractiveEffectEvent(InteractiveEffectEventKind Kind, bool PlaySound = false, BubbleCue? Bubble = null);
public sealed record InteractiveEffectsFrame(IReadOnlyList<BubbleCue> Bubbles, BouncingTextCue? BouncingText)
{
    public static InteractiveEffectsFrame Empty { get; } = new([], null);
    public IReadOnlyList<BouncingTextCue> BouncingTexts { get; init; } = BouncingText is null ? [] : [BouncingText];
    public IReadOnlyList<BubbleImageVisual> ImageVisuals { get; init; } = [];
    public bool CascadeActive { get; init; }
}

// Coordinates are normalized. Bounce positions describe the travel space left around the text.
public sealed class InteractiveEffectsTimeline(ClassicSettings settings, int seed = 0, BounceMotion? initialBounce = null)
{
    private static readonly Dictionary<string, Dictionary<string, KnobDefinition>> Knobs = EffectCatalog.All
        .ToDictionary(effect => effect.Id, effect => effect.Primary.Concat(effect.More).ToDictionary(knob => knob.Key));
    private readonly Random random = new(seed);
    private readonly List<Bubble> bubbles = [];
    private readonly EaseTrack bounceFade = new();
    private long nextId;
    private double lastTime, nextBubble, bubbleRate;
    private int queuedBubbles;
    private readonly BouncingTextTimeline bounceMotion = new(seed, initialBounce);
    private readonly BubbleVisualTimeline bubbleVisuals = new(seed);
    private int imageCount;
    private double viewportWidth = 1600, viewportHeight = 900;
    public bool VideoAvailable { get; set; }
    public bool VideoPlaying { get; set; }
    public Func<string, string, double, (double Width, double Height)>? BounceTextMeasurer
    { get => bounceMotion.MeasureText; set => bounceMotion.MeasureText = value; }
    public bool IsRunning { get; private set; }
    public event Action<InteractiveEffectEvent>? EventOccurred;

    public void Start()
    {
        Stop(); IsRunning = true; lastTime = 0;
        bubbleRate = Value("bubble", "rate"); nextBubble = Interval(bubbleRate);
        bounceMotion.CornerHit -= BounceCorner; bounceMotion.CornerHit += BounceCorner;
        bounceMotion.WallHit -= BounceWall; bounceMotion.WallHit += BounceWall;
    }

    public void Stop() { IsRunning = false; bubbles.Clear(); queuedBubbles = 0; bounceFade.Reset(); bounceMotion.Reset(); bubbleVisuals.Clear(); }
    public bool TriggerBubbles(int count = 3)
    {
        if (!IsRunning || count <= 0 || bubbles.Count + queuedBubbles >= 8) return false;
        queuedBubbles = Math.Min(8 - bubbles.Count, queuedBubbles + Math.Clamp(count, 0, 8)); return true;
    }
    public void SetImageCount(int count) => imageCount = Math.Clamp(count, 0, 100000);
    public void ReplaceImages(int count)
    {
        SetImageCount(count); bubbleVisuals.Clear();
        bubbles.RemoveAll(bubble => bubble.Image >= 0 || bubble.Effect is BubbleEffectKind.Glitch or BubbleEffectKind.Cascade or BubbleEffectKind.BrainDrainWash);
    }
    public void SetViewport(double widthDip, double heightDip)
    {
        if (double.IsFinite(widthDip) && widthDip > 0) viewportWidth = Math.Clamp(widthDip, 200, 16384);
        if (double.IsFinite(heightDip) && heightDip > 0) viewportHeight = Math.Clamp(heightDip, 200, 16384);
    }

    public bool Pop(long id)
    {
        if (!IsRunning || Value("bubble", "click") == 0) return false;
        var bubble = bubbles.FirstOrDefault(item => item.Id == id && !item.Ending && item.Fade.Value > .01);
        if (bubble is null || !bubble.Manual && !Enabled("bubble")) return false;
        bubble.Ending = bubble.Popped = true;
        bubble.PopStarted = lastTime; bubble.PopAlpha = bubble.LastAlpha;
        bubble.SoftPop = settings.GentleVisuals || settings.ReducedMotion;
        bubble.PopDuration = settings.GentleVisuals ? .75 : settings.ReducedMotion ? .12 : BubbleChoreography.PopDuration;
        bubble.PopPose = bubble.Pose;
        var effect = BubbleValue("effects", 1, 0, 1) != 0 && Available(bubble.Effect) ? bubble.Effect : BubbleEffectKind.None;
        bubbleVisuals.Trigger(effect, lastTime, imageCount);
        EventOccurred?.Invoke(new(InteractiveEffectEventKind.BubblePopped, Value("bubble", "sound") != 0,
            Cue(bubble, bubble.Fade.Value, false, effect)));
        return true;
    }

    public InteractiveEffectsFrame Step(double seconds)
    {
        if (!IsRunning) return InteractiveEffectsFrame.Empty;
        var now = double.IsFinite(seconds) ? Math.Max(lastTime, seconds) : lastTime;
        var dt = Math.Clamp(now - lastTime, 0, .1); lastTime = now;
        var fade = settings.GentleVisuals ? .75 : .25;
        var nextRate = Value("bubble", "rate");
        if (nextRate != bubbleRate || !Enabled("bubble")) nextBubble = now + Interval(nextRate);
        bubbleRate = nextRate;
        if (Enabled("bubble") && now >= nextBubble)
        {
            nextBubble = now + Interval(bubbleRate);
            if (bubbles.Count < 8)
            {
                SpawnBubble(now, false);
            }
        }
        while (queuedBubbles > 0 && bubbles.Count < 8) { queuedBubbles--; SpawnBubble(now, true); }
        queuedBubbles = 0;
        var output = new List<BubbleCue>();
        foreach (var bubble in bubbles.ToArray())
        {
            bubble.Ending |= !Enabled("bubble") && !bubble.Manual || now >= bubble.Expires;
            if (bubble.Effect != BubbleEffectKind.None && BubbleValue("effects", 1, 0, 1) == 0) bubble.Ending = true;
            if (!settings.ReducedMotion && !bubble.Ending)
            {
                var frames = dt * 30 * (settings.GentleVisuals ? .5 : 1);
                bubble.Frames += frames; bubble.RiseDip += frames * bubble.Speed * (bubble.Motion == BubbleMotion.Rain ? -1 : 1);
                var sway = BubbleChoreography.Sway(bubble.Kind, bubble.Frames);
                var breathing = BubbleChoreography.Breathe(bubble.Frames, bubble.Phase) * (settings.GentleVisuals ? .5 : 1);
                bubble.Pose = new(1 + breathing, bubble.Angle + BubbleChoreography.Rotation(bubble.Kind, bubble.Frames),
                    sway, bubble.RiseDip, viewportWidth, viewportHeight, bubble.Seed);
            }
            var spiralAlpha = 1d;
            if (bubble.Motion == BubbleMotion.SpiralIn && !settings.ReducedMotion && !bubble.Ending)
            {
                var radius = Math.Max(96, Math.Min(viewportWidth, viewportHeight) * .42);
                var path = BubbleSpiralPath.Sample(radius, bubble.Phase, bubble.Speed, bubble.Frames,
                    bubble.Kind % 2 == 0 ? 1 : -1, settings.GentleVisuals);
                bubble.X = .5 + (Math.Cos(path.Angle) * path.Radius - bubble.SizeDip / 2) / viewportWidth;
                bubble.Y = .5 + (Math.Sin(path.Angle) * path.Radius - bubble.SizeDip / 2) / viewportHeight;
                bubble.RiseDip = 0; spiralAlpha = path.Opacity;
                if (bubble.Pose is { } pose) bubble.Pose = pose with { SwayDip = 0, RiseDip = 0 };
                if (path.Radius <= 24) bubble.Ending = true;
            }
            var alpha = bubble.Fade.Sample(now, bubble.Ending ? 0 : 1, fade) * spiralAlpha;
            if (bubble.Effect == BubbleEffectKind.BrainDrain && !bubble.Popped && !settings.ReducedMotion)
                alpha *= BubbleChoreography.DrainPulse(bubble.Frames);
            var progress = 0d;
            if (bubble.Popped)
            {
                if (!bubble.SoftPop && (settings.GentleVisuals || settings.ReducedMotion))
                {
                    bubble.PopAlpha = bubble.LastAlpha; bubble.PopStarted = now; bubble.PopPose = bubble.Pose;
                    bubble.SoftPop = true; bubble.PopDuration = settings.GentleVisuals ? .75 : .12;
                }
                progress = Math.Clamp((now - bubble.PopStarted) / bubble.PopDuration, 0, 1);
                alpha = bubble.PopAlpha * (bubble.SoftPop ? 1 - progress * progress * (3 - 2 * progress) : 1 - progress);
                var popFrames = (now - bubble.PopStarted) * 30;
                var pose = bubble.PopPose ?? new BubbleAnimation(1, 0, 0, bubble.RiseDip, viewportWidth, viewportHeight, bubble.Seed);
                bubble.Pose = pose with { Scale = pose.Scale + (bubble.SoftPop ? 0 : .04 * popFrames),
                    Rotation = pose.Rotation + (bubble.SoftPop ? 0 : 2 * popFrames), PopFrames = bubble.SoftPop ? 0 : popFrames };
            }
            bubble.LastAlpha = alpha;
            if (bubble.Ending && alpha == 0) bubbles.Remove(bubble);
            else output.Add(Cue(bubble, alpha, !bubble.Ending && alpha > .01 && Value("bubble", "click") != 0,
                bubble.Effect) with { PopProgress = progress });
        }

        var bounceEnabled = Enabled("bounce") && (!VideoPlaying || settings.BouncingText.ShowOverVideos);
        var bounceAlpha = bounceFade.Sample(now, bounceEnabled ? Value("bounce", "opacity") / 100 : 0, fade);
        var bounce = bounceEnabled || bounceAlpha > 0
            ? bounceMotion.Step(bounceEnabled ? dt : 0, viewportWidth, viewportHeight, Value("bounce", "speed"),
                Value("bounce", "size") / 100, bounceAlpha, settings.BouncingText, settings.GentleVisuals, settings.ReducedMotion) : [];
        var visuals = bubbleVisuals.Step(now, viewportHeight, settings.GentleVisuals, settings.ReducedMotion, settings.PerformanceMode);
        return new(Array.AsReadOnly(output.ToArray()), bounce.FirstOrDefault()) { BouncingTexts = bounce,
            ImageVisuals = visuals, CascadeActive = bubbleVisuals.CascadeActive };
    }

    private void BounceCorner() => EventOccurred?.Invoke(new(InteractiveEffectEventKind.CornerHit));
    private void BounceWall() => EventOccurred?.Invoke(new(InteractiveEffectEventKind.WallHit));
    private void SpawnBubble(double now, bool manual)
    {
        var effect = RollEffect(); var speed = (1 + random.NextDouble()) * (1 + settings.Bubbles.SpeedBoostPercent / 100d);
        var motion = effect != BubbleEffectKind.None ? BubbleMotion.FloatUp
            : settings.Bubbles.Motion == BubbleMotion.Mix ? (BubbleMotion)random.Next(3) : settings.Bubbles.Motion;
        var diameter = effect == BubbleEffectKind.None ? Math.Clamp(random.Next(150, 250) * settings.Bubbles.SizePercent / 100d, 60, 500)
            : BubbleChoreography.Diameter(effect, random.NextDouble());
        var size = diameter;
        var lifetime = effect == BubbleEffectKind.None ? Math.Max(12, (viewportHeight + size + 50) / (speed * 30 * (settings.GentleVisuals ? .5 : 1))) : 7;
        bubbles.Add(new(++nextId, .05 + random.NextDouble() * .75,
            settings.ReducedMotion ? .7 + random.NextDouble() * .1 : motion == BubbleMotion.Rain ? -diameter / viewportHeight : 1,
            now + lifetime, effect, effect == BubbleEffectKind.Flash ? random.Next(imageCount) : -1,
            speed, random.Next(4), random.NextDouble() * 100, random.Next(360), random.Next()) { Manual = manual, Motion = motion, SizeDip = diameter });
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private BubbleEffectKind RollEffect()
    {
        // CCP ambient trigger bubbles are benign payloads fired only by a deliberate pop.
        if (Value("bubble", "click") == 0 || BubbleValue("effects", 1, 0, 1) == 0
            || bubbles.Count(b => b.Effect != BubbleEffectKind.None) >= 4
            || random.Next(100) >= BubbleValue("chance", 10, 0, 100)) return BubbleEffectKind.None;
        var chosen = (int)BubbleValue("kind", 0, 0, 9);
        var pool = Enum.GetValues<BubbleEffectKind>().Where(kind => kind != BubbleEffectKind.None
            && (chosen == 0 || (int)kind == chosen) && settings.Bubbles.Variants.Contains(kind) && Available(kind)).ToArray();
        return pool.Length > 0 ? pool[random.Next(pool.Length)] : BubbleEffectKind.None;
    }
    private bool Available(BubbleEffectKind kind) => kind switch
    {
        BubbleEffectKind.Flash => imageCount > 0 && Enabled("flash"),
        BubbleEffectKind.Subliminal => Enabled("sub"), BubbleEffectKind.Pink => Enabled("pink"),
        BubbleEffectKind.Spiral => Enabled("spiral"), BubbleEffectKind.BrainDrain => !Enabled("drain") && settings.BrainDrain.BlurStrength > 0,
        BubbleEffectKind.Video => VideoAvailable && Enabled("video") && settings.Effects["video"].Values["strict"] == 0,
        BubbleEffectKind.Glitch or BubbleEffectKind.Cascade or BubbleEffectKind.BrainDrainWash => imageCount > 0,
        _ => false
    };
    private static double BubbleSize(BubbleEffectKind effect) => effect == BubbleEffectKind.None ? .1 : .16;
    private BubbleCue Cue(Bubble bubble, double alpha, bool canPop, BubbleEffectKind effect) =>
        new(bubble.Id, bubble.X + (bubble.Pose?.SwayDip ?? 0) / (bubble.Pose?.ReferenceWidth ?? viewportWidth),
            bubble.Y - bubble.RiseDip / (bubble.Pose?.ReferenceHeight ?? viewportHeight), BubbleSize(bubble.Effect), alpha, canPop, effect, bubble.Image)
        { Animation = bubble.Pose, DiameterDip = bubble.SizeDip };
    private double BubbleValue(string key, double fallback, double min, double max) =>
        settings.Effects["bubble"].Values.TryGetValue(key, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, min, max) : fallback;
    private double Value(string id, string key)
    {
        var knob = Knobs[id][key];
        return settings.Effects[id].Values.TryGetValue(key, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, knob.Min, knob.Max) : knob.Default;
    }
    private static double Interval(double frequency) => frequency > 0 ? 60 / frequency : double.PositiveInfinity;
    private sealed class Bubble(long id, double x, double y, double expires, BubbleEffectKind effect, int image,
        double speed, int kind, double phase, double angle, int seed)
    {
        public readonly long Id = id;
        public readonly double Expires = expires;
        public double X = x, SizeDip;
        public BubbleMotion Motion;
        public double Y = y;
        public bool Ending, Popped, Manual;
        public bool SoftPop;
        public double Frames, RiseDip, PopStarted, PopAlpha, PopDuration, LastAlpha;
        public readonly double Speed = speed, Phase = phase, Angle = angle;
        public readonly int Kind = kind, Seed = seed;
        public BubbleAnimation? Pose, PopPose;
        public readonly BubbleEffectKind Effect = effect;
        public readonly int Image = image;
        public readonly EaseTrack Fade = new();
    }
    private sealed class EaseTrack
    {
        private double from, target, value, started, duration;
        public double Value => value;
        public void Reset() { from = target = value = started = duration = 0; }
        public double Sample(double now, double next, double fade)
        {
            if (target != next || fade > duration)
            { from = value; target = next; started = now; duration = fade; }
            var t = duration > 0 ? Math.Clamp((now - started) / duration, 0, 1) : 1;
            return value = from + (target - from) * t * t * (3 - 2 * t);
        }
    }
}
