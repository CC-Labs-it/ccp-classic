namespace CCP.Classic.Core.LocalDevices;

public enum LocalDeviceEvent { Flash, Subliminal, BubblePop, BouncingText, Video, Attention, LockCard, Achievement, LevelUp, Companion, MindWipe, BrainDrain, AudioSync, Manual }
public enum LocalHapticPattern { Constant, Pulse, Wave, Heartbeat, Escalate, Earthquake }
public readonly record struct LocalHapticStep(int OffsetMilliseconds, double Intensity, int AttackMilliseconds, int HoldMilliseconds, int DecayMilliseconds);

// Timing comes from public CCP Services/Haptics/Core/HapticPatterns.cs, MIT.
// Preserve motor spin-up time and evaluate envelopes in one mixer instead of rounding commands to seconds.
public static class LocalHapticPatterns
{
    public const int MinimumOnMilliseconds = 130, MinimumDurationMilliseconds = 200;
    public static IReadOnlyList<LocalHapticStep> Render(LocalHapticPattern pattern, double intensity, TimeSpan duration, int? seed = null)
    {
        var strength = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
        if (strength <= 0 || duration <= TimeSpan.Zero) return [];
        var milliseconds = (int)Math.Clamp(duration.TotalMilliseconds, MinimumDurationMilliseconds, 60000);
        var steps = new List<LocalHapticStep>();
        switch (pattern)
        {
            case LocalHapticPattern.Pulse:
                for (var index = 0; index < Math.Clamp(milliseconds / 200, 1, 40); index++)
                    steps.Add(new(index * 200, strength, 8, MinimumOnMilliseconds, 20));
                break;
            case LocalHapticPattern.Wave:
                var half = Math.Max(MinimumOnMilliseconds, milliseconds / 2);
                steps.Add(new(0, strength, half, 0, half));
                break;
            case LocalHapticPattern.Heartbeat:
                for (var index = 0; index < Math.Clamp(milliseconds / 400, 1, 20); index++)
                {
                    steps.Add(new(index * 400, strength, 10, MinimumOnMilliseconds, 30));
                    steps.Add(new(index * 400 + 140, strength * .7, 10, MinimumOnMilliseconds, 30));
                }
                break;
            case LocalHapticPattern.Escalate:
                var count = Math.Clamp(milliseconds / MinimumOnMilliseconds, 1, 8);
                var slice = Math.Max(MinimumOnMilliseconds, milliseconds / count);
                for (var index = 1; index <= count; index++)
                    steps.Add(new((index - 1) * slice, strength * (.2 + .8 * index / count), 10, slice, 20));
                break;
            case LocalHapticPattern.Earthquake:
                var random = new Random(seed ?? Random.Shared.Next());
                for (var index = 0; index < Math.Clamp(milliseconds / 170, 2, 60); index++)
                    steps.Add(new(index * 170, strength * (.3 + random.NextDouble() * .7), 5, MinimumOnMilliseconds, 25));
                break;
            default:
                steps.Add(new(0, strength, Math.Clamp(milliseconds / 6, 10, 60), milliseconds, Math.Clamp(milliseconds / 4, 30, 150)));
                break;
        }
        return steps.AsReadOnly();
    }

    public static int TotalMilliseconds(IReadOnlyList<LocalHapticStep> steps) => steps.Count == 0 ? 0
        : steps.Max(s => s.OffsetMilliseconds + s.AttackMilliseconds + s.HoldMilliseconds + s.DecayMilliseconds);

    public static double Sample(IReadOnlyList<LocalHapticStep> steps, double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0) return 0;
        var level = 0d;
        foreach (var step in steps)
        {
            var elapsed = elapsedMilliseconds - step.OffsetMilliseconds;
            if (elapsed < 0) continue;
            var value = elapsed < step.AttackMilliseconds ? step.Intensity * elapsed / step.AttackMilliseconds
                : elapsed < step.AttackMilliseconds + step.HoldMilliseconds ? step.Intensity
                : elapsed < step.AttackMilliseconds + step.HoldMilliseconds + step.DecayMilliseconds
                    ? step.Intensity * (1 - (elapsed - step.AttackMilliseconds - step.HoldMilliseconds) / step.DecayMilliseconds) : 0;
            level = Math.Max(level, value);
        }
        return Math.Clamp(level, 0, 1);
    }
}
