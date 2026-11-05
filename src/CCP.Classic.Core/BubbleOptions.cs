namespace CCP.Classic.Core;

public enum BubbleMotion { FloatUp, Rain, SpiralIn, Mix }
public sealed class BubbleOptions
{
    public int SizePercent { get; set; } = 100;
    public int SpeedBoostPercent { get; set; }
    public int VolumePercent { get; set; } = 70;
    public BubbleMotion Motion { get; set; }
    public List<BubbleEffectKind> Variants { get; set; } = [BubbleEffectKind.Flash, BubbleEffectKind.Subliminal,
        BubbleEffectKind.Pink, BubbleEffectKind.Spiral, BubbleEffectKind.BrainDrain, BubbleEffectKind.Video,
        BubbleEffectKind.Glitch, BubbleEffectKind.Cascade];
    public void Normalize()
    {
        SizePercent = Math.Clamp(SizePercent, 50, 150); SpeedBoostPercent = Math.Clamp(SpeedBoostPercent, 0, 500);
        VolumePercent = Math.Clamp(VolumePercent, 0, 100); if (!Enum.IsDefined(Motion)) Motion = BubbleMotion.FloatUp;
        Variants = (Variants ?? []).Where(kind => Enum.IsDefined(kind) && kind != BubbleEffectKind.None).Distinct().ToList();
    }
}

// Public CCP SpiralInPath. DIPs at the source's 30Hz, independent of display scale.
public static class BubbleSpiralPath
{
    public static (double Radius, double Angle, double Opacity) Sample(double startRadius, double startAngle,
        double speed, double frames, double direction, bool gentle)
    {
        var radial = Math.Max(.01, speed * .55);
        var radius = Math.Max(24, startRadius - radial * frames);
        var total = Math.Max(1, (startRadius - 24) / radial);
        return (radius, startAngle + Math.Sign(direction) * Math.Tau * (gentle ? 1.25 : 2.5) / total * frames,
            Math.Clamp((radius - 24) / 110, 0, 1));
    }
}
