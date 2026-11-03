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
    public LibrarySettings Library { get; set; } = new();
    public List<TimedEffectChange> TimedChanges { get; set; } = [];
    public Dictionary<string, EffectSettings> Effects { get; set; } = [];

    public ClassicSettings() => Normalize();

    public void Normalize()
    {
        Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language;
        if (!new[] { 0, 15, 30, 60 }.Contains(RunMinutes)) RunMinutes = 0;
        if (string.IsNullOrWhiteSpace(PanicKey)) PanicKey = "Escape";
        BarkIntervalSeconds = BarkIntervalSeconds <= 0 ? 60 : Math.Clamp(BarkIntervalSeconds, 1, 3600);
        Library ??= new();
        Library.Normalize();
        TimedChanges = TimedEffectChange.Normalize(TimedChanges);
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
}
