using System.Text.Json;

namespace CCP.Classic.Core;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP Classic", "settings.json");

    public ClassicSettings Load(out bool recovered)
    {
        recovered = false;
        try
        {
            if (!File.Exists(path)) return new();
            var settings = JsonSerializer.Deserialize<ClassicSettings>(File.ReadAllText(path))
                ?? throw new JsonException("Missing settings");
            settings.Normalize();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            recovered = true;
            return new();
        }
    }

    /// <summary>The saved form of the settings, used to tell whether anything changed since the last save.</summary>
    public static string Snapshot(ClassicSettings settings) => JsonSerializer.Serialize(settings, Options);

    public void Save(ClassicSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
