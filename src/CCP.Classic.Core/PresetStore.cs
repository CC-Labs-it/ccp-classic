using System.Text.Json;

namespace CCP.Classic.Core;

public sealed class PresetDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Dictionary<string, EffectSettings> Effects { get; set; } = [];
    public bool GentleVisuals { get; set; }
    public bool ReducedMotion { get; set; }
    public int RunMinutes { get; set; }
    public List<TimedEffectChange> TimedChanges { get; set; } = [];
}

public sealed class PresetStore(string directory)
{
    private const int MaximumBytes = 256 * 1024, MaximumPresets = 256;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 16 };
    public static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "presets");
    public static IReadOnlyList<PresetDefinition> BuiltIns =>
        Array.AsReadOnly(new[] { BuiltIn("Gentle"), BuiltIn("Daily"), BuiltIn("Deep") });

    public static PresetDefinition Capture(string name, ClassicSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Normalize(new PresetDefinition
        {
            Name = name, Effects = settings.Effects, GentleVisuals = settings.GentleVisuals,
            ReducedMotion = settings.ReducedMotion, RunMinutes = settings.RunMinutes,
            TimedChanges = TimedEffectChange.Normalize(settings.TimedChanges)
        });
    }

    public static void Apply(PresetDefinition definition, ClassicSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(definition);
        var output = settings.Effects.TryGetValue("audio", out var audio)
            && audio.Values.TryGetValue("output", out var selected) ? selected : 0;
        settings.Effects = normalized.Effects;
        settings.Effects["audio"].Values["output"] = output;
        settings.GentleVisuals = normalized.GentleVisuals;
        settings.ReducedMotion = normalized.ReducedMotion;
        settings.RunMinutes = normalized.RunMinutes;
        settings.TimedChanges = normalized.TimedChanges.ToList();
        settings.Preset = normalized.Name;
    }

    // User presets only. BuiltIns is separate so callers can localize those names.
    public IReadOnlyList<PresetDefinition> LoadAll(out bool recovered)
    {
        recovered = false;
        var presets = new List<PresetDefinition>();
        try
        {
            if (!Directory.Exists(directory)) return presets.AsReadOnly();
            var examined = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (++examined > MaximumPresets) { recovered = true; break; }
                var id = Path.GetFileNameWithoutExtension(path);
                if (!IsCustomId(id)) { recovered = true; continue; }
                try
                {
                    var definition = ReadBounded(path);
                    if (definition.Id != id) throw new JsonException("Preset identifier mismatch");
                    presets.Add(Normalize(definition));
                }
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
                { recovered = true; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { recovered = true; }
        return presets.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList().AsReadOnly();
    }

    public PresetDefinition Save(PresetDefinition definition)
    {
        var normalized = Normalize(definition);
        if (!IsCustomId(normalized.Id)) throw new ArgumentException("Only user presets can be saved", nameof(definition));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, Options);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("Preset is too large", nameof(definition));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, normalized.Id + ".json");
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return normalized;
    }

    public void Delete(string id)
    {
        if (!IsCustomId(id)) throw new ArgumentException("Invalid preset identifier", nameof(id));
        File.Delete(Path.Combine(directory, id + ".json"));
    }

    private static PresetDefinition ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[MaximumBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes) throw new JsonException("Preset is too large");
        return JsonSerializer.Deserialize<PresetDefinition>(bytes.AsSpan(0, count), Options)
            ?? throw new JsonException("Missing preset");
    }

    private static bool IsCustomId(string? id) => id is not null
        && Guid.TryParseExact(id, "N", out var parsed) && parsed.ToString("N") == id;

    private static PresetDefinition Normalize(PresetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SchemaVersion != 1) throw new ArgumentException("Unsupported preset version", nameof(definition));
        var name = definition.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Invalid preset name", nameof(definition));
        if (!IsCustomId(definition.Id) && definition.Id is not ("builtin-gentle" or "builtin-daily" or "builtin-deep"))
            throw new ArgumentException("Invalid preset identifier", nameof(definition));
        var settings = new ClassicSettings { RunMinutes = definition.RunMinutes };
        foreach (var effect in EffectCatalog.All)
        {
            var target = settings.Effects[effect.Id]; target.Enabled = false;
            if (definition.Effects is null || !definition.Effects.TryGetValue(effect.Id, out var source) || source is null) continue;
            target.Enabled = source.Enabled;
            foreach (var knob in effect.Primary.Concat(effect.More))
            {
                if (knob.Key == "output") { target.Values.Remove(knob.Key); continue; }
                if (source.Values is not null && source.Values.TryGetValue(knob.Key, out var value)) target.Values[knob.Key] = value;
            }
        }
        settings.Normalize();
        settings.Effects["audio"].Values.Remove("output");
        return new PresetDefinition
        {
            Id = definition.Id, Name = name, Effects = settings.Effects, RunMinutes = settings.RunMinutes,
            GentleVisuals = definition.GentleVisuals, ReducedMotion = definition.ReducedMotion,
            TimedChanges = TimedEffectChange.Normalize(definition.TimedChanges)
        };
    }

    private static PresetDefinition BuiltIn(string name)
    {
        var settings = new ClassicSettings { GentleVisuals = name == "Gentle" };
        foreach (var (id, effect) in settings.Effects)
            effect.Enabled = name == "Deep" ? id != "wall" : id is "flash" or "sub" or "audio"
                || name == "Daily" && id is "spiral" or "pink" or "bubble";
        if (name != "Deep")
        {
            settings.Effects["flash"].Values["rate"] = 6;
            settings.Effects["flash"].Values["size"] = 30;
            settings.Effects["flash"].Values["opacity"] = 55;
            settings.Effects["sub"].Values["rate"] = 4;
            settings.Effects["sub"].Values["opacity"] = 25;
        }
        settings.Effects["video"].Values["strict"] = 0;
        var preset = Capture(name, settings); preset.Id = "builtin-" + name.ToLowerInvariant();
        return preset;
    }
}
