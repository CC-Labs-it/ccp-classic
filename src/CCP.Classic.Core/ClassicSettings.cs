namespace CCP.Classic.Core;

public sealed class EffectSettings
{
    public bool Enabled { get; set; }
    public Dictionary<string, double> Values { get; set; } = [];
}

public sealed class ClassicSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string Language { get; set; } = "en";
    public bool GentleVisuals { get; set; }
    public bool ReducedMotion { get; set; }
    public bool FillAllMonitors { get; set; } = true;
    public bool PerformanceMode { get; set; }
    public bool StartWithSystem { get; set; }
    public bool StartHidden { get; set; }
    public bool AutoRun { get; set; }
    /// <summary>Opt-in: ask GitHub once at startup whether a newer release exists. Off by default.</summary>
    public bool CheckForUpdates { get; set; }
    public bool CompanionEnabled { get; set; } = true;
    public bool BarksEnabled { get; set; } = true;
    public bool TriggerPhrasesEnabled { get; set; }
    public int BarkIntervalSeconds { get; set; } = 60;
    public int RunMinutes { get; set; }
    public string PanicKey { get; set; } = "Escape";
    public string Preset { get; set; } = "Daily";
    public string? CustomSpiralId { get; set; }
    public LibrarySettings Library { get; set; } = new();
    public LockCardOptions LockCard { get; set; } = new();
    public BrainDrainOptions BrainDrain { get; set; } = new();
    public BouncingTextOptions BouncingText { get; set; } = new();
    public SubliminalOptions Subliminal { get; set; } = new();
    public PinkFilterOptions PinkFilter { get; set; } = new();
    public MindWipeOptions MindWipe { get; set; } = new();
    public BubbleOptions Bubbles { get; set; } = new();
    public Flash.FlashOptions Flash { get; set; } = new();
    public VideoOptions Video { get; set; } = new();
    public SpiralOptions Spiral { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public LayeredAudioOptions LayeredAudio { get; set; } = new();
    public CompanionPlacementOptions CompanionPlacement { get; set; } = new();
    public WallpaperOptions Wallpaper { get; set; } = new();
    public bool FlashAvoidCenter { get; set; }
    public int FlashCenterExclusionPercent { get; set; } = 25;
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
        StartWithSystem = defaults.StartWithSystem; StartHidden = defaults.StartHidden; AutoRun = defaults.AutoRun; CheckForUpdates = defaults.CheckForUpdates;
        CompanionEnabled = defaults.CompanionEnabled;
        BarksEnabled = defaults.BarksEnabled; TriggerPhrasesEnabled = defaults.TriggerPhrasesEnabled;
        BarkIntervalSeconds = defaults.BarkIntervalSeconds; RunMinutes = defaults.RunMinutes;
        PanicKey = defaults.PanicKey; Preset = defaults.Preset; Effects = defaults.Effects;
        CustomSpiralId = defaults.CustomSpiralId;
        Library = defaults.Library; TimedChanges = defaults.TimedChanges; Devices = defaults.Devices;
        LockCard = defaults.LockCard; BrainDrain = defaults.BrainDrain;
        BouncingText = defaults.BouncingText; Subliminal = defaults.Subliminal;
        PinkFilter = defaults.PinkFilter; MindWipe = defaults.MindWipe;
        Bubbles = defaults.Bubbles;
        Video = defaults.Video; Spiral = defaults.Spiral; Audio = defaults.Audio; LayeredAudio = defaults.LayeredAudio;
        CompanionPlacement = defaults.CompanionPlacement;
        Wallpaper = defaults.Wallpaper;
        Flash = defaults.Flash; FlashAvoidCenter = defaults.FlashAvoidCenter; FlashCenterExclusionPercent = defaults.FlashCenterExclusionPercent;
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
        LockCard ??= new(); LockCard.Normalize();
        BrainDrain ??= new(); BrainDrain.Normalize();
        BouncingText ??= new(); BouncingText.Normalize();
        Subliminal ??= new(); Subliminal.Normalize();
        PinkFilter ??= new(); PinkFilter.Normalize();
        MindWipe ??= new(); MindWipe.Normalize();
        Bubbles ??= new(); Bubbles.Normalize();
        Video ??= new(); Video.Normalize();
        Spiral ??= new(); Spiral.Normalize();
        Audio ??= new(); Audio.Normalize();
        LayeredAudio ??= new(); LayeredAudio.Normalize();
        CompanionPlacement ??= new(); CompanionPlacement.Normalize();
        Wallpaper ??= new(); Wallpaper.Normalize();
        Flash ??= new(); Flash.Normalize(); FlashCenterExclusionPercent = Math.Clamp(FlashCenterExclusionPercent, 5, 60);
        TimedChanges = TimedEffectChange.Normalize(TimedChanges);
        Devices ??= new(); Devices.Normalize();
        Effects ??= [];
        if (SchemaVersion < 2)
        {
            foreach (var id in new[] { "lock", "wipe" })
                if (Effects.TryGetValue(id, out var legacy) && legacy?.Values is { } values
                    && !values.ContainsKey("rate") && values.TryGetValue("interval", out var minutes) && double.IsFinite(minutes) && minutes > 0)
                    values["rate"] = 60 / minutes;
            if (Effects.TryGetValue("bounce", out var bounce) && bounce?.Values is { } knobs && knobs.TryGetValue("size", out var size))
                knobs["size"] = size * 2.5;
            if (Effects.TryGetValue("flash", out var flash) && flash?.Values is { } flashKnobs && flashKnobs.TryGetValue("size", out var flashSize))
                flashKnobs["size"] = flashSize * 2.5;
            SchemaVersion = 2;
        }
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
