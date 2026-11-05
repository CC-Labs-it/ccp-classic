namespace CCP.Classic.Core.LocalDevices;

// Exact pinned public HapticTemperament table. All multipliers apply before the device power cap.
public sealed record LocalHapticTemperament(string Key, double ContinuousScale, double TransientScale,
    double AttackScale, double DecayScale, int PulsePriorityBias)
{
    public static IReadOnlyList<LocalHapticTemperament> All { get; } = Array.AsReadOnly(new[]
    {
        new LocalHapticTemperament("gentle", .70, .75, 1.40, 1.30, -1),
        new LocalHapticTemperament("balanced", 1, 1, 1, 1, 0),
        new LocalHapticTemperament("tease", .85, .90, 1.60, 1.80, 0),
        new LocalHapticTemperament("intense", 1.15, 1.20, .80, .90, 1),
        new LocalHapticTemperament("cruel", 1.25, 1.40, .45, .70, 2)
    });
    public static LocalHapticTemperament FromKey(string? key) => All.FirstOrDefault(value =>
        string.Equals(value.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[1];
    public LocalHapticStep Shape(LocalHapticStep step) => step with
    {
        AttackMilliseconds = Scale(step.AttackMilliseconds, AttackScale), DecayMilliseconds = Scale(step.DecayMilliseconds, DecayScale)
    };
    private static int Scale(int milliseconds, double scale) => milliseconds <= 0 ? 0 : Math.Clamp((int)Math.Round(milliseconds * scale), 1, 60000);
}
