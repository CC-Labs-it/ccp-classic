namespace CCP.Classic.Core;

public sealed record BubbleAnimation(double Scale, double Rotation, double SwayDip, double RiseDip,
    double ReferenceWidth, double ReferenceHeight, int Seed, double PopFrames = 0);

// Public CCP BubbleService.cs3344-3363,3827-3843,4059-4064,4298-4310 at fe40ea7.
public static class BubbleChoreography
{
    public const double PopDuration = 1 / (.066 * 30);
    public static double Sway(int kind, double frames)
    {
        var t = frames * .02;
        return kind switch { 0 => Math.Sin(t * 6) * 25, 1 => Math.Sin(t * 7.5) * 30,
            2 => Math.Cos(t * 5.4) * 25, _ => Math.Sin(t * 3) * 30 + Math.Cos(t * 6) * 15 };
    }
    public static double Rotation(int kind, double frames) => frames * (kind switch
        { 0 => .34, 1 => .14, 2 => -.66, _ => .54 });
    public static double Breathe(double frames, double phase) => .06 * Math.Sin(frames * .02 * 7.5 + phase);
    public static double DrainPulse(double frames) => .82 + .18 * (.5 + .5 * Math.Sin(frames * .02 * 1.9));
    public static double Diameter(BubbleEffectKind kind, double roll)
    {
        if (kind == BubbleEffectKind.Glitch) return 200;
        if (kind == BubbleEffectKind.BrainDrain) return 220;
        var (minimum, maximum) = kind switch { BubbleEffectKind.Flash => (150d, 210d), BubbleEffectKind.Subliminal => (170, 220),
            BubbleEffectKind.BrainDrainWash => (240, 320), BubbleEffectKind.Video => (240, 300),
            BubbleEffectKind.Cascade => (200, 280), _ => (180, 240) };
        return (minimum + (maximum - minimum) * Math.Clamp(roll * .7 + .3 * .45, 0, 1)) * .75
            * (kind is BubbleEffectKind.Video or BubbleEffectKind.Cascade ? .7 : 1);
    }
    public static (double X, double Y, double Size, double Alpha) Spark(int seed, int index, double frames)
    {
        var random = new Random(unchecked(seed * 7919 + index * 104729));
        var angle = random.NextDouble() * Math.Tau; var speed = 2 + random.NextDouble() * 4;
        return (Math.Cos(angle) * speed * frames,
            Math.Sin(angle) * speed * frames + .15 * frames * Math.Max(0, frames - 1) / 2,
            3 + random.NextDouble() * 3, Math.Max(0, 1 - .06 * frames));
    }
}
