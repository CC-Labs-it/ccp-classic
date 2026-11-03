namespace CCP.Classic.Core;

public sealed class LockCardOptions
{
    public Dictionary<string, bool> Phrases { get; set; } = new()
    { ["Breathe"] = true, ["Relax"] = true, ["Focus"] = true, ["Listen"] = true, ["Deeper"] = true };
    public bool RandomRepeats { get; set; }
    public int MinimumRepeats { get; set; } = 1;
    public bool TargetLengthEnabled { get; set; }
    public int TargetLength { get; set; } = 120;
    public int TargetVariance { get; set; } = 20;
    public bool ResetOnTypo { get; set; }
    public bool StrictCopy { get; set; } = true;
    public string Background { get; set; } = "#1A1A2E";
    public string TextColor { get; set; } = "#FF69B4";
    public string InputBackground { get; set; } = "#252542";
    public string InputText { get; set; } = "#FFFFFF";
    public string Accent { get; set; } = "#FF69B4";

    public void Normalize()
    {
        Phrases = (Phrases ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Take(500).GroupBy(p => p.Key.Trim()[..Math.Min(p.Key.Trim().Length, 2000)])
            .ToDictionary(g => g.Key, g => g.First().Value);
        MinimumRepeats = Math.Clamp(MinimumRepeats, 1, 10);
        TargetLength = Math.Clamp(TargetLength, 20, 600);
        TargetVariance = Math.Clamp(TargetVariance, 0, 200);
        Background = ValidColor(Background, "#1A1A2E"); TextColor = ValidColor(TextColor, "#FF69B4");
        InputBackground = ValidColor(InputBackground, "#252542"); InputText = ValidColor(InputText, "#FFFFFF");
        Accent = ValidColor(Accent, "#FF69B4");
    }

    internal static string ValidColor(string? color, string fallback) => color is { Length: 7 }
        && color[0] == '#' && color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false ? color : fallback;

    // Ported from CCP LockCardService.ResolveRepeats, public fe40ea7587ac0b09e47d0724e59d7178db3336da.
    public int ResolveRepeats(string phrase, int maximum, double roll)
    {
        maximum = Math.Clamp(maximum, 1, 10);
        roll = double.IsFinite(roll) ? Math.Clamp(roll, 0, .9999999) : 0;
        if (TargetLengthEnabled)
        {
            var budget = Math.Max(1, TargetLength + (int)Math.Round((roll * 2 - 1) * TargetVariance));
            return Math.Clamp((int)Math.Ceiling((double)budget / Math.Max(1, LockCardText.TypedLength(phrase))), 1, 30);
        }
        var minimum = Math.Clamp(MinimumRepeats, 1, 10);
        return RandomRepeats ? Math.Min(minimum, maximum) + (int)(roll * (Math.Abs(maximum - minimum) + 1)) : maximum;
    }
}
