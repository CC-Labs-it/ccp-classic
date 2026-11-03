namespace CCP.Classic.Core.LocalDevices;

// Source HapticMixer's 100ms / ramp rise allowance, using elapsed time when callbacks wake early.
public sealed class LocalContinuousFloor
{
    public double Value { get; private set; }
    private double previous;
    private bool observed;
    public double Step(double target, double milliseconds, int rampMilliseconds)
    {
        target = double.IsFinite(target) ? Math.Max(0, target) : 0;
        var now = double.IsFinite(milliseconds) ? Math.Max(previous, milliseconds) : previous;
        var elapsed = observed ? Math.Clamp(now - previous, 0, 100) : 100;
        Value = target <= Value ? target : Math.Min(target, Value + elapsed / Math.Max(1, rampMilliseconds));
        previous = now; observed = true; return Value;
    }
    public static double Finish(double raw, double master, double cap, double trim)
    {
        if (!double.IsFinite(raw) || raw <= 0 || !double.IsFinite(master) || master <= 0) return 0;
        var value = Math.Max(raw * Math.Clamp(master, 0, 1), .06);
        return Math.Clamp(Math.Min(value, Math.Clamp(cap, 0, 1)) * Math.Clamp(trim, 0, 1), 0, 1);
    }
}
