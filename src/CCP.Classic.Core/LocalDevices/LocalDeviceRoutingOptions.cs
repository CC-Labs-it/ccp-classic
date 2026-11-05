namespace CCP.Classic.Core.LocalDevices;

public enum LocalToyRole { All, Reward, Punish, Ambient }
public sealed class LocalDeviceRule
{
    public bool Enabled { get; set; } = true;
    public double Trim { get; set; } = 1;
    public LocalToyRole Role { get; set; }
    public string Nickname { get; set; } = "";
}

public sealed class LocalDeviceRoutingOptions
{
    public double MaximumPower { get; set; } = .7;
    public int SoftRampMilliseconds { get; set; } = 800;
    public LocalToyRole FunScriptRole { get; set; }
    public LocalToyRole LuminanceRole { get; set; }
    public string Temperament { get; set; } = "balanced";
    public int MaximumConcurrentPulses { get; set; } = 4;
    public Dictionary<string, LocalDeviceRule> DeviceRules { get; set; } = [];
    public Dictionary<string, LocalToyRole> EventTargets { get; set; } = [];
    public static string Key(LocalDeviceInfo device) => device.Provider + ":" + device.Id;
    public LocalDeviceRule Device(LocalDeviceInfo device)
    {
        var key = Key(device);
        if (!DeviceRules.TryGetValue(key, out var rule)) DeviceRules[key] = rule = new();
        return rule;
    }
    public void Normalize()
    {
        MaximumPower = double.IsFinite(MaximumPower) ? Math.Clamp(MaximumPower, 0, 1) : .7;
        SoftRampMilliseconds = Math.Clamp(SoftRampMilliseconds, 1, 60000);
        if (!Enum.IsDefined(FunScriptRole)) FunScriptRole = LocalToyRole.All;
        if (!Enum.IsDefined(LuminanceRole)) LuminanceRole = LocalToyRole.All;
        Temperament = LocalHapticTemperament.FromKey(Temperament).Key;
        MaximumConcurrentPulses = Math.Clamp(MaximumConcurrentPulses, 1, 12);
        DeviceRules = (DeviceRules ?? []).Where(pair => pair.Key.Length is > 0 and <= 180 && !pair.Key.Any(char.IsControl))
            .Take(128).ToDictionary(pair => pair.Key, pair =>
            {
                var rule = pair.Value ?? new();
                rule.Trim = double.IsFinite(rule.Trim) ? Math.Clamp(rule.Trim, 0, 1) : 1;
                if (!Enum.IsDefined(rule.Role)) rule.Role = LocalToyRole.All;
                rule.Nickname = new((rule.Nickname ?? "").Where(c => !char.IsControl(c)).Take(80).ToArray());
                return rule;
            });
        EventTargets = (EventTargets ?? []).Where(pair => Enum.TryParse<LocalDeviceEvent>(pair.Key, out _))
            .ToDictionary(pair => pair.Key, pair => Enum.IsDefined(pair.Value) ? pair.Value : LocalToyRole.All);
    }
    internal LocalDeviceRoutingOptions Snapshot()
    {
        var copy = new LocalDeviceRoutingOptions
        {
            MaximumPower = MaximumPower,
            SoftRampMilliseconds = SoftRampMilliseconds,
            FunScriptRole = FunScriptRole,
            LuminanceRole = LuminanceRole,
            Temperament = Temperament, MaximumConcurrentPulses = MaximumConcurrentPulses,
            DeviceRules = DeviceRules.ToDictionary(pair => pair.Key, pair => new LocalDeviceRule
                { Enabled = pair.Value.Enabled, Trim = pair.Value.Trim, Role = pair.Value.Role, Nickname = pair.Value.Nickname }),
            EventTargets = new(EventTargets)
        };
        copy.Normalize(); return copy;
    }
    internal LocalToyRole Target(LocalDeviceEvent kind) => EventTargets.GetValueOrDefault(kind.ToString());
    // Source HapticMixer.RoleMatches, without source accounts/providers.
    internal static bool Matches(LocalToyRole target, LocalToyRole role) => target == LocalToyRole.All || role == LocalToyRole.All || target == role;
}
