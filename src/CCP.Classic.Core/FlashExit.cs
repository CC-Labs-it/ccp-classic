namespace CCP.Classic.Core;

public enum FlashExitStyle { Mix, Pop, TvOff, Spiral, Melt, Glitch, None }
public sealed record FlashExitState(FlashExitStyle Style, double DurationSec, bool Reduced, int Seed, double ElapsedSec = 0)
{
    public FlashExitSample? Frozen { get; init; }
    public double Progress => DurationSec <= 0 ? 1 : Math.Clamp(ElapsedSec / DurationSec, 0, 1);
    public bool Done => ElapsedSec >= DurationSec;
}
public readonly record struct FlashExitSample(double ScaleX, double ScaleY, double RotationDeg,
    double PivotY, double OffsetY, double Alpha, double Whiten, double Glitch);

// Public CCP Services/Flash/FlashExit.cs at fe40ea7, By CodeBambi. Original free exit maths.
public static class FlashExit
{
    public static IReadOnlyList<FlashExitStyle> Pool { get; } = Array.AsReadOnly(new[]
        { FlashExitStyle.Pop, FlashExitStyle.TvOff, FlashExitStyle.Spiral, FlashExitStyle.Melt, FlashExitStyle.Glitch });
    public static double DurationOf(FlashExitStyle style) => style switch
    { FlashExitStyle.Pop => .22, FlashExitStyle.TvOff => .24, FlashExitStyle.Spiral => .38,
        FlashExitStyle.Melt => .48, FlashExitStyle.Glitch => .28, _ => .12 };
    public static FlashExitStyle? Pick(FlashExitStyle setting, FlashExitStyle? last, Random random)
    {
        if (setting == FlashExitStyle.None) return null;
        if (setting != FlashExitStyle.Mix) return setting;
        var choices = Pool.Where(style => style != last).ToArray(); return choices[random.Next(choices.Length)];
    }
    public static FlashExitState Begin(FlashExitStyle style, int seed, bool gentle, bool motionOff, bool reduced) =>
        gentle ? new(FlashExitStyle.Mix, .75, true, seed)
        : motionOff ? new(FlashExitStyle.Mix, .12, true, seed)
        : new(style, DurationOf(style) * (reduced ? .75 : 1), reduced, seed);
    public static FlashExitState Soften(FlashExitState state, double duration) =>
        new(FlashExitStyle.Mix, duration, true, state.Seed) { Frozen = Sample(state) with { Whiten = 0, Glitch = 0 } };
    public static FlashExitSample Sample(FlashExitState state)
    {
        var p = state.Progress; var e = p * p;
        if (state.Frozen is { } frozen) return frozen with { Alpha = frozen.Alpha * (1 - p) };
        switch (state.Style)
        {
            case FlashExitStyle.Pop:
                var scale = state.Reduced ? 1 - e : p < .3 ? 1 + .08 * EaseOut(p / .3)
                    : 1.08 * (1 - Math.Pow((p - .3) / .7, 2));
                return new(scale, scale, 0, .5, 0, p < .6 ? 1 : 1 - (p - .6) / .4, 0, 0);
            case FlashExitStyle.TvOff:
                if (p < .55) { var q = p / .55; return new(1 + .04 * q, 1 - .98 * q * q, 0, .5, 0, 1, .9 * q, 0); }
                var r = (p - .55) / .45;
                return new(1.04 * (1 - r * r), .02, 0, .5, 0, 1 - r * r * r, .9, 0);
            case FlashExitStyle.Spiral:
                return new(1 - e, 1 - e, state.Reduced ? 0 : 300 * e * ((state.Seed & 1) == 0 ? 1 : -1),
                    .5, 0, p < .7 ? 1 : 1 - (p - .7) / .3, 0, 0);
            case FlashExitStyle.Melt:
                return new(1 - .15 * e, 1 + (state.Reduced ? .3 : .7) * e, 0, 0, .35 * e, 1 - e, 0, 0);
            case FlashExitStyle.Glitch:
                var alpha = p < .5 || state.Reduced ? 1 - e : (int)(p * 20) % 2 == 0 ? 1 - p : .25 * (1 - p);
                return new(1, 1 - .2 * p, 0, .5, 0, alpha, 0, p);
            default: return new(1, 1, 0, .5, 0, 1 - p, 0, 0);
        }
    }
    public static int SparkCount(FlashExitState state) => state.Style == FlashExitStyle.Pop ? state.Reduced ? 5 : 10 : 0;
    public static (double Dx, double Dy, double Distance, double Radius, double Alpha) Spark(FlashExitState state, int i)
    {
        var n = Math.Max(1, SparkCount(state)); var p = state.Progress;
        var jitter = ((state.Seed * 7919 + i * 104729) & 0xffff) / 65535d - .5;
        var angle = Math.Tau * (i + .5 * jitter) / n;
        return (Math.Cos(angle), Math.Sin(angle), .55 + .75 * EaseOut(p) * (1 + .3 * jitter),
            (3 + 6 * Math.Abs(jitter)) * (1 - p), 1 - p);
    }
    public static double SliceOffset(FlashExitState state, int slice) =>
        Math.Sin(state.Seed * 13.1 + slice * 7.1 + (int)(state.Progress * 12) * 3.7) * state.Progress * .12;
    private static double EaseOut(double value) => 1 - (1 - value) * (1 - value);
}
