namespace CCP.Classic.Core;

public readonly record struct BouncingTextBounds(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && Math.Abs(X) <= 1_000_000 && Math.Abs(Y) <= 1_000_000 && Width is > 0 and <= 1_000_000 && Height is > 0 and <= 1_000_000;
}

public readonly record struct BouncingMonitor(double X, double Y, double Width, double Height);
public readonly record struct BouncingTextProjection(double X, double Y, double Scale)
{
    public (double X, double Y) ToLocal(double x, double y) => (x * Scale + X, y * Scale + Y);
}

// CCP uses one rectangular desktop travel box. Gaps and monitor seams are not walls.
public sealed class BouncingDesktopLayout
{
    public BouncingTextBounds Bounds { get; }
    public double ReferenceScale { get; }
    public IReadOnlyList<BouncingMonitor> Monitors { get; }

    public BouncingDesktopLayout(IEnumerable<BouncingMonitor> monitors, double referenceScale = 1)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (!double.IsFinite(referenceScale) || referenceScale is < .25 or > 8) throw new ArgumentOutOfRangeException(nameof(referenceScale));
        var valid = monitors.Take(32).Where(m => new BouncingTextBounds(m.X, m.Y, m.Width, m.Height).IsValid).Distinct().ToArray();
        if (valid.Length == 0) throw new ArgumentException("A desktop requires a valid monitor.", nameof(monitors));
        ReferenceScale = referenceScale; Monitors = Array.AsReadOnly(valid);
        var x = valid.Min(m => m.X); var y = valid.Min(m => m.Y);
        Bounds = new(x / referenceScale, y / referenceScale,
            (valid.Max(m => m.X + m.Width) - x) / referenceScale,
            (valid.Max(m => m.Y + m.Height) - y) / referenceScale);
        if (!Bounds.IsValid) throw new ArgumentException("The desktop bounds exceed the supported coordinate range.", nameof(monitors));
    }

    // The glyph's physical size remains continuous across different host DPI scales.
    public BouncingTextProjection Project(double hostPixelX, double hostPixelY, double hostScale)
    {
        if (!double.IsFinite(hostPixelX) || !double.IsFinite(hostPixelY) || !double.IsFinite(hostScale) || hostScale is < .25 or > 8)
            throw new ArgumentOutOfRangeException(nameof(hostScale));
        return new(-hostPixelX / hostScale, -hostPixelY / hostScale, ReferenceScale / hostScale);
    }
}
