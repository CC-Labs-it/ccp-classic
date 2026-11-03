namespace CCP.Classic.Core;

public sealed record SubliminalPhrase(string Text, string? AudioPath = null);

public sealed class SubliminalOptions
{
    public List<SubliminalPhrase> Phrases { get; set; } = Defaults();
    public string FontFamily { get; set; } = "Arial";
    public double WhisperVolume { get; set; } = .5;

    public void Normalize()
    {
        Phrases = (Phrases ?? Defaults()).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Text))
            .Take(256).Select(item => new SubliminalPhrase(Text(item.Text), LocalAudio(item.AudioPath)))
            .Where(item => item.Text.Length > 0).DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase).ToList();
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Arial" : Text(FontFamily, 120);
        WhisperVolume = double.IsFinite(WhisperVolume) ? Math.Clamp(WhisperVolume, 0, 1) : .5;
    }
    public static List<SubliminalPhrase> Defaults() => [new("FOCUS"), new("BREATHE"), new("RELAX"), new("LISTEN"), new("DEEPER")];
    public static string Text(string? text, int limit = 2000) => new((text ?? "").Trim().Where(c => !char.IsControl(c) || c == '\n').Take(limit).ToArray());
    public static string? LocalAudio(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl)
            || !Path.IsPathFullyQualified(path) || !MediaLibrary.IsLocalPath(path)) return null;
        try
        {
            return LocalAudioFormats.SupportsPath(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }
}

public sealed class PinkFilterOptions
{
    public string Colour { get; set; } = "#FF69B4";
    public bool UnderEffects { get; set; }
    public int Monitor { get; set; } = -1;

    public void Normalize()
    {
        if (Colour is not { Length: 7 } || Colour[0] != '#' || !Colour.AsSpan(1).ToString().All(Uri.IsHexDigit)) Colour = "#FF69B4";
        Monitor = Math.Clamp(Monitor, -1, 8);
    }
}

public sealed class MindWipeOptions
{
    public string? AudioPath { get; set; }
    public void Normalize() => AudioPath = SubliminalOptions.LocalAudio(AudioPath);
}

// Source MindWipeRunRule is lifecycle policy, not a user-selectable effect mode.
public static class MindWipeRunRule
{
    public static bool CanPlay(bool sessionRunning, bool cuePresent, bool audioEnabled) => sessionRunning && cuePresent && audioEnabled;
}
