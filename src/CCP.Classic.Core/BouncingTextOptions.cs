namespace CCP.Classic.Core;

public sealed class BouncingTextOptions
{
    public int ColorMode { get; set; }
    public string FixedColor { get; set; } = "#FF69B4";
    public string FontFamily { get; set; } = "Inter";
    public bool SecondText { get; set; }
    public bool ShowOverVideos { get; set; }
    public bool Breathing { get; set; }
    public bool Wobble { get; set; }
    public bool Spin { get; set; }
    public bool VelocityTilt { get; set; }
    public bool SquashStretch { get; set; } = true;
    public bool CornerBurst { get; set; } = true;
    public bool Outline { get; set; }
    public Dictionary<string, bool> PhrasePool { get; set; } = new()
    { ["Breathe"] = true, ["Relax"] = true, ["Focus"] = true, ["Listen"] = true, ["Deeper"] = true };

    public void Normalize()
    {
        ColorMode = Math.Clamp(ColorMode, 0, 2);
        FixedColor = LockCardOptions.ValidColor(FixedColor, "#FF69B4");
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 128 ? "Inter" : FontFamily.Trim();
        PhrasePool = (PhrasePool ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key)).Take(500)
            .GroupBy(p => p.Key.Trim()[..Math.Min(p.Key.Trim().Length, 2000)])
            .ToDictionary(g => g.Key, g => g.First().Value);
    }
}
