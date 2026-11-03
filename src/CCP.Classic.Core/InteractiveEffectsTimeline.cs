namespace CCP.Classic.Core;

public sealed record BubbleCue(long Id, double X, double Y, double Size, double Opacity, bool CanPop);
public sealed record BouncingTextCue(string Text, double X, double Y, double Size, double Opacity);
public sealed record BounceMotion(double X, double Y, double VelocityX, double VelocityY);
public enum InteractiveEffectEventKind { BubblePopped, CornerHit, LockCompleted, WipeCompleted }
public sealed record InteractiveEffectEvent(InteractiveEffectEventKind Kind, bool PlaySound = false);
public sealed record InteractiveEffectsFrame(IReadOnlyList<BubbleCue> Bubbles, BouncingTextCue? BouncingText)
{
    public static InteractiveEffectsFrame Empty { get; } = new([], null);
}

// Coordinates are normalized. Bounce positions describe the travel space left around the text.
public sealed class InteractiveEffectsTimeline(ClassicSettings settings, int seed = 0, BounceMotion? initialBounce = null)
{
    private static readonly string[] Words = ["Breathe", "Relax", "Focus", "Listen", "Deeper"];
    private static readonly Dictionary<string, Dictionary<string, KnobDefinition>> Knobs = EffectCatalog.All
        .ToDictionary(effect => effect.Id, effect => effect.Primary.Concat(effect.More).ToDictionary(knob => knob.Key));
    private readonly Random random = new(seed);
    private readonly List<Bubble> bubbles = [];
    private readonly EaseTrack bounceFade = new();
    private long nextId;
    private double lastTime, nextBubble, bubbleRate, x, y, vx, vy;
    private int word = 2;
    public bool IsRunning { get; private set; }
    public event Action<InteractiveEffectEvent>? EventOccurred;

    public void Start()
    {
        Stop(); IsRunning = true; lastTime = 0;
        bubbleRate = Value("bubble", "rate"); nextBubble = Interval(bubbleRate);
        x = Clamp(initialBounce?.X ?? .25, 0, 1);
        y = Clamp(initialBounce?.Y ?? .5, 0, 1);
        vx = Clamp(initialBounce?.VelocityX ?? .12, -1, 1);
        vy = Clamp(initialBounce?.VelocityY ?? .08, -1, 1);
        word = 2;
    }

    public void Stop() { IsRunning = false; bubbles.Clear(); bounceFade.Reset(); }

    public bool Pop(long id)
    {
        if (!IsRunning || !Enabled("bubble") || Value("bubble", "click") == 0) return false;
        var bubble = bubbles.FirstOrDefault(item => item.Id == id && !item.Ending && item.Fade.Value > .01);
        if (bubble is null) return false;
        bubble.Ending = true;
        EventOccurred?.Invoke(new(InteractiveEffectEventKind.BubblePopped, Value("bubble", "sound") != 0));
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
            if (bubbles.Count < 8) bubbles.Add(new(++nextId, .05 + random.NextDouble() * .8,
                .7 + random.NextDouble() * .15, now + 12));
        }
        var output = new List<BubbleCue>();
        foreach (var bubble in bubbles.ToArray())
        {
            bubble.Ending |= !Enabled("bubble") || now >= bubble.Expires;
            if (!settings.ReducedMotion && !bubble.Ending)
                bubble.Y = Math.Max(0, bubble.Y - dt * (settings.GentleVisuals ? .0075 : .015));
            var alpha = bubble.Fade.Sample(now, bubble.Ending ? 0 : 1, fade);
            if (bubble.Ending && alpha == 0) bubbles.Remove(bubble);
            else output.Add(new(bubble.Id, bubble.X, bubble.Y, .1, alpha,
                !bubble.Ending && alpha > .01 && Value("bubble", "click") != 0));
        }

        var bounceEnabled = Enabled("bounce");
        var bounceAlpha = bounceFade.Sample(now, bounceEnabled ? Value("bounce", "opacity") / 100 : 0, fade);
        if (bounceEnabled && !settings.ReducedMotion)
        {
            var movement = dt * Value("bounce", "speed") / 4 * (settings.GentleVisuals ? .5 : 1);
            var hitX = Advance(ref x, ref vx, movement);
            var hitY = Advance(ref y, ref vy, movement);
            // Two edge collisions during one frame are not a corner unless their times agree.
            if (hitX is { } tx && hitY is { } ty && Math.Abs(tx - ty) <= 1e-10)
            {
                word = (word + 1) % Words.Length;
                EventOccurred?.Invoke(new(InteractiveEffectEventKind.CornerHit));
            }
        }
        var bounce = bounceEnabled || bounceAlpha > 0
            ? new BouncingTextCue(Words[word], x, y, Value("bounce", "size") / 100, bounceAlpha) : null;
        return new(Array.AsReadOnly(output.ToArray()), bounce);
    }

    private static double? Advance(ref double position, ref double velocity, double delta)
    {
        if (velocity == 0 || delta <= 0) return null;
        var boundary = velocity > 0 ? (1 - position) / velocity : -position / velocity;
        position += velocity * delta;
        if (boundary > delta + 1e-12) return null;
        position = velocity > 0 ? 2 - position : -position;
        velocity = -velocity;
        position = Math.Clamp(position, 0, 1);
        return boundary;
    }

    private bool Enabled(string id) => settings.Effects[id].Enabled;
    private double Value(string id, string key)
    {
        var knob = Knobs[id][key];
        return settings.Effects[id].Values.TryGetValue(key, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, knob.Min, knob.Max) : knob.Default;
    }
    private static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
    private static double Interval(double frequency) => frequency > 0 ? 60 / frequency : double.PositiveInfinity;
    private sealed class Bubble(long id, double x, double y, double expires)
    {
        public readonly long Id = id;
        public readonly double X = x, Expires = expires;
        public double Y = y;
        public bool Ending;
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
