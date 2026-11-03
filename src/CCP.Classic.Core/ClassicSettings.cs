namespace CCP.Classic.Core;

public sealed class EffectSettings
{
    public bool Enabled { get; set; }
    public Dictionary<string, double> Values { get; set; } = [];
}

public sealed class ClassicSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "en";
    public bool GentleVisuals { get; set; }
    public bool ReducedMotion { get; set; }
    public bool FillAllMonitors { get; set; } = true;
    public bool PerformanceMode { get; set; }
    public bool StartWithSystem { get; set; }
    public bool StartHidden { get; set; }
    public bool AutoRun { get; set; }
    public bool KeywordTriggersEnabled { get; set; }
    public bool CompanionEnabled { get; set; }
    public bool BarksEnabled { get; set; }
    public bool TriggerPhrasesEnabled { get; set; }
    public int BarkIntervalSeconds { get; set; } = 60;
    public int RunMinutes { get; set; }
    public string PanicKey { get; set; } = "Escape";
    public string Preset { get; set; } = "Daily";
    public string? CustomSpiralId { get; set; }
    public LibrarySettings Library { get; set; } = new();
    public List<TimedEffectChange> TimedChanges { get; set; } = [];
    public LocalDevices.LocalDevicePreferences Devices { get; set; } = new();
    public Dictionary<string, EffectSettings> Effects { get; set; } = [];

    public ClassicSettings() => Normalize();

    public void ResetToDefaults()
    {
        var defaults = new ClassicSettings();
        SchemaVersion = defaults.SchemaVersion; Language = defaults.Language;
        GentleVisuals = defaults.GentleVisuals; ReducedMotion = defaults.ReducedMotion;
        FillAllMonitors = defaults.FillAllMonitors; PerformanceMode = defaults.PerformanceMode;
        StartWithSystem = defaults.StartWithSystem; StartHidden = defaults.StartHidden; AutoRun = defaults.AutoRun;
        KeywordTriggersEnabled = defaults.KeywordTriggersEnabled; CompanionEnabled = defaults.CompanionEnabled;
        BarksEnabled = defaults.BarksEnabled; TriggerPhrasesEnabled = defaults.TriggerPhrasesEnabled;
        BarkIntervalSeconds = defaults.BarkIntervalSeconds; RunMinutes = defaults.RunMinutes;
        PanicKey = defaults.PanicKey; Preset = defaults.Preset; Effects = defaults.Effects;
        CustomSpiralId = defaults.CustomSpiralId;
        Library = defaults.Library; TimedChanges = defaults.TimedChanges; Devices = defaults.Devices;
    }

    public void Normalize()
    {
        Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language;
        if (!new[] { 0, 15, 30, 60 }.Contains(RunMinutes)) RunMinutes = 0;
        if (string.IsNullOrWhiteSpace(PanicKey)) PanicKey = "Escape";
        BarkIntervalSeconds = BarkIntervalSeconds <= 0 ? 60 : Math.Clamp(BarkIntervalSeconds, 1, 3600);
        CustomSpiralId = NormalizeSpiralId(CustomSpiralId);
        Library ??= new();
        Library.Normalize();
        TimedChanges = TimedEffectChange.Normalize(TimedChanges);
        Devices ??= new(); Devices.Normalize();
        Effects ??= [];
        foreach (var definition in EffectCatalog.All)
        {
            if (!Effects.TryGetValue(definition.Id, out var effect) || effect is null)
                Effects[definition.Id] = effect = new() { Enabled = definition.DefaultEnabled };
            effect.Values ??= [];
            foreach (var knob in definition.Primary.Concat(definition.More))
            {
                if (!effect.Values.TryGetValue(knob.Key, out var value) || !double.IsFinite(value)) value = knob.Default;
                effect.Values[knob.Key] = Math.Clamp(value, knob.Min, knob.Max);
            }
        }
    }

    internal static string? NormalizeSpiralId(string? id) => Guid.TryParseExact(id, "N", out var value)
        && value.ToString("N") == id ? id : null;
}
