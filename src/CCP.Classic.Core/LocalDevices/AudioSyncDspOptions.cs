namespace CCP.Classic.Core.LocalDevices;

public sealed class AudioSyncDspOptions
{
    public double Sensitivity { get; set; } = 1;
    public double Smoothing { get; set; } = .3;
    public double BassWeight { get; set; } = .4;
    public double RmsWeight { get; set; } = .35;
    public double OnsetWeight { get; set; } = .25;
    public double MinimumIntensity { get; set; } = .05;
    public double MaximumIntensity { get; set; } = 1;
    public int OffsetMilliseconds { get; set; }
    public bool BandSplit { get; set; }
    public double LiveIntensity { get; set; } = 1;
    public void Normalize()
    {
        Sensitivity = Clamp(Sensitivity, .1, 3, 1); Smoothing = Clamp(Smoothing, 0, .95, .3);
        BassWeight = Clamp(BassWeight, 0, 1, .4); RmsWeight = Clamp(RmsWeight, 0, 1, .35); OnsetWeight = Clamp(OnsetWeight, 0, 1, .25);
        MinimumIntensity = Clamp(MinimumIntensity, 0, .5, .05); MaximumIntensity = Clamp(MaximumIntensity, .5, 1, 1);
        OffsetMilliseconds = Math.Clamp(OffsetMilliseconds, -600, 600);
        LiveIntensity = Clamp(LiveIntensity, 0, 1, 1);
    }
    private static double Clamp(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
