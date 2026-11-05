namespace CCP.Classic.Core;

public sealed class WallpaperOptions
{
    public string? SourceFolder { get; set; }
    public string? OwnImagePath { get; set; }
    public bool Collage { get; set; }
    public int LayoutMask { get; set; } = 7;
    public bool Rotate { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public string Accent { get; set; } = "#E84393";
    public string Background { get; set; } = "#08080C";
    public void Normalize()
    {
        SourceFolder = Local(SourceFolder); OwnImagePath = Local(OwnImagePath);
        if ((LayoutMask & 7) == 0) LayoutMask = 7; else LayoutMask &= 7;
        IntervalSeconds = Math.Clamp(IntervalSeconds, 10, 86400);
        Accent = Hex(Accent, "#E84393"); Background = Hex(Background, "#08080C");
    }
    private static string? Local(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !MediaLibrary.IsLocalPath(path)) return null;
        try { return Path.GetFullPath(path); } catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    private static string Hex(string? value, string fallback) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value : fallback;
}
