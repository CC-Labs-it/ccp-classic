namespace CCP.Classic.Core;

public sealed class VideoOptions
{
    public int FloatingSizePercent { get; set; } = 33;
    public int FloatingSpeedPercent { get; set; } = 35;
    public bool FloatingTogether { get; set; }
    public bool FloatingKeepSound { get; set; }
    public int TargetMonitor { get; set; } = -1;
    public int MinDurationSeconds { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool RandomStart { get; set; } = true;
    public bool BlurredBackground { get; set; } = true;
    public int TargetCount { get; set; } = 3;
    public bool RandomTargets { get; set; }
    public int TargetLifespan { get; set; } = 12;
    public int TargetSize { get; set; } = 70;
    public bool ReplayAfterFailedCheck { get; set; } = true;
    public bool MercyEnabled { get; set; } = true;
    public int MercyAfterFails { get; set; } = 3;
    public Dictionary<string, bool> TargetPhrases { get; set; } = new()
    { ["Breathe"] = true, ["Relax"] = true, ["Focus"] = true, ["Listen"] = true, ["Deeper"] = true };
    public string TargetColor1 { get; set; } = "#FF1493";
    public string TargetColor2 { get; set; } = "#FF69B4";
    public string TargetTextColor { get; set; } = "#FF1493";
    public string TargetBorderColor { get; set; } = "#FF1493";
    public string TargetFont { get; set; } = "Inter";
    public bool TargetFloatingText { get; set; } = true;
    public bool TargetShowBorder { get; set; }
    public void Normalize()
    {
        FloatingSizePercent = Math.Clamp(FloatingSizePercent, 15, 60); FloatingSpeedPercent = Math.Clamp(FloatingSpeedPercent, 10, 100);
        TargetMonitor = Math.Clamp(TargetMonitor, -2, 31); MinDurationSeconds = Math.Clamp(MinDurationSeconds, 0, 600);
        MaxDurationSeconds = Math.Clamp(MaxDurationSeconds, 0, 1800); TargetCount = Math.Clamp(TargetCount, 1, 10);
        TargetLifespan = Math.Clamp(TargetLifespan, 1, 30); TargetSize = Math.Clamp(TargetSize, 30, 150);
        MercyAfterFails = Math.Clamp(MercyAfterFails, 2, 10);
        TargetColor1 = LockCardOptions.ValidColor(TargetColor1, "#FF1493"); TargetColor2 = LockCardOptions.ValidColor(TargetColor2, "#FF69B4");
        TargetTextColor = LockCardOptions.ValidColor(TargetTextColor, "#FF1493"); TargetBorderColor = LockCardOptions.ValidColor(TargetBorderColor, "#FF1493");
        TargetFont = string.IsNullOrWhiteSpace(TargetFont) || TargetFont.Length > 128 ? "Inter" : TargetFont.Trim();
        TargetPhrases = (TargetPhrases ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key)).Take(500)
            .GroupBy(p => p.Key.Trim()[..Math.Min(p.Key.Trim().Length, 2000)]).ToDictionary(g => g.Key, g => g.First().Value);
    }
    public double PlaybackCap(double sessionCap) => MaxDurationSeconds > 0 ? Math.Min(sessionCap, MaxDurationSeconds) : sessionCap;
    public bool Eligible(double duration) => duration <= 0 || MinDurationSeconds == 0 || duration >= MinDurationSeconds;
    public static long StartOffset(int capSeconds, long durationMilliseconds, double roll) => capSeconds <= 0
        || durationMilliseconds <= capSeconds * 1000L ? 0
        : (long)((durationMilliseconds - capSeconds * 1000L) * Math.Clamp(double.IsFinite(roll) ? roll : 0, 0, 1));
}
