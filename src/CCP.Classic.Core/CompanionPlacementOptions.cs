namespace CCP.Classic.Core;

public sealed class CompanionPlacementOptions
{
    public bool Detached { get; set; }
    public int? Left { get; set; }
    public int? Top { get; set; }
    public double Scale { get; set; } = 1;
    public bool AlwaysOnTop { get; set; } = true;
    public void Normalize()
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .5, 1.5) : 1;
        if (Left is < -100000 or > 100000 || Top is < -100000 or > 100000) Left = Top = null;
    }
}
