namespace CCP.Classic.Core;

// Shared by local library discovery, saved clip paths and every recording picker.
public static class LocalAudioFormats
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[]
        { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma" });
    public static IReadOnlyList<string> Patterns { get; } = Array.AsReadOnly(Extensions.Select(extension => "*" + extension).ToArray());
    public static bool SupportsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}
