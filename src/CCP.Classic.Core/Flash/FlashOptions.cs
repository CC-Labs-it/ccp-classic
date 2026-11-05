namespace CCP.Classic.Core.Flash;

public enum FlashMotionStyle { Still, DriftBounce, Pendulum, Mix }
public enum MotionLevel { Off, Reduced, Full }

public sealed class FlashOptions
{
    public int Count { get; set; } = 1;
    public bool RandomCount { get; set; }
    public int MinimumCount { get; set; } = 1;
    public int HoldSeconds { get; set; } = 4;
    public bool StayUntilPopped { get; set; }
    public bool AudioEnabled { get; set; } = true;
    public bool RemixEnabled { get; set; }
    public bool Glow { get; set; } = true;
    public bool RoundedCorners { get; set; }
    public bool Draggable { get; set; }
    public bool Shatter { get; set; }
    public bool HydraLinkedTiming { get; set; } = true;
    public FlashMotionStyle Motion { get; set; }
    public double DriftSpeed { get; set; } = 1;
    public double GifSpeed { get; set; } = 1;
    public void Normalize()
    {
        Count = Math.Clamp(Count, 1, 20); MinimumCount = Math.Clamp(MinimumCount, 1, 20);
        HoldSeconds = Math.Clamp(HoldSeconds, 1, 30); if (!Enum.IsDefined(Motion)) Motion = FlashMotionStyle.Still;
        DriftSpeed = double.IsFinite(DriftSpeed) ? Math.Clamp(DriftSpeed, .25, 3) : 1;
        GifSpeed = double.IsFinite(GifSpeed) ? Math.Clamp(GifSpeed, .25, 4) : 1;
    }
}
