namespace CCP.Classic.Core.LocalDevices;

public sealed class LocalDevicePreferences
{
    public int IntifacePort { get; set; } = 12345;
    public int LovensePort { get; set; } = 20010;
    public double GlobalIntensity { get; set; } = .7;
    public bool AudioSyncEnabled { get; set; }
    public bool VideoSyncEnabled { get; set; } = true;
    public bool ToyInputEnabled { get; set; } = true;
    public bool AttentionCheckToyButton { get; set; }
    public bool FunScriptEnabled { get; set; } = true;
    public bool FunScriptToVibration { get; set; } = true;
    public double FunScriptIntensity { get; set; } = 1;
    public bool LuminanceSyncEnabled { get; set; }
    public double LuminanceSyncIntensity { get; set; } = .5;
    public LocalDeviceRoutingOptions Routing { get; set; } = new();
    public AudioSyncDspOptions AudioSyncDsp { get; set; } = new();
    public Dictionary<string, double> EventLevels { get; set; } = [];
    public Dictionary<string, int> EventPatterns { get; set; } = [];

    public LocalDevicePreferences() => Normalize();
    public void Normalize()
    {
        if (IntifacePort is < 1 or > 65535) IntifacePort = 12345;
        if (LovensePort is < 1 or > 65535) LovensePort = 20010;
        GlobalIntensity = double.IsFinite(GlobalIntensity) ? Math.Clamp(GlobalIntensity, 0, 1) : .7;
        LuminanceSyncIntensity = double.IsFinite(LuminanceSyncIntensity) ? Math.Clamp(LuminanceSyncIntensity, 0, 1) : .5;
        FunScriptIntensity = double.IsFinite(FunScriptIntensity) ? Math.Clamp(FunScriptIntensity, 0, 1) : 1;
        Routing ??= new(); Routing.Normalize();
        AudioSyncDsp ??= new(); AudioSyncDsp.Normalize();
        EventLevels ??= []; EventPatterns ??= [];
        var levels = new Dictionary<string, double>(); var patterns = new Dictionary<string, int>();
        foreach (var kind in Enum.GetValues<LocalDeviceEvent>())
        {
            var key = kind.ToString();
            levels[key] = EventLevels.TryGetValue(key, out var value) && double.IsFinite(value) ? Math.Clamp(value, 0, 1)
                : kind == LocalDeviceEvent.AudioSync ? 1 : kind == LocalDeviceEvent.Manual ? .3 : .5;
            patterns[key] = EventPatterns.TryGetValue(key, out var pattern) && Enum.IsDefined((LocalHapticPattern)pattern) ? pattern : 0;
        }
        EventLevels = levels; EventPatterns = patterns;
    }
    public double Level(LocalDeviceEvent kind) => EventLevels.GetValueOrDefault(kind.ToString(), .5);
    public LocalHapticPattern Pattern(LocalDeviceEvent kind) => (LocalHapticPattern)EventPatterns.GetValueOrDefault(kind.ToString());
}
