namespace CCP.Classic.Core.LocalDevices;

// Pinned public HapticMixer promotion, concurrency and shared tick-window sampling.
public sealed class LocalPulseMixer
{
    private sealed record Pulse(LocalDeviceEvent Event, LocalHapticStep Step, int Priority, string? Device, double Started);
    private readonly List<Pulse> pending = [], active = [];
    private double now;
    public int ActiveCount => active.Count;
    public int PendingCount => pending.Count;
    public void Clear() { pending.Clear(); active.Clear(); }
    public void Enqueue(LocalDeviceEvent kind, IReadOnlyList<LocalHapticStep> steps, int priority, string? device, double milliseconds,
        LocalHapticTemperament temperament, int maximum)
    {
        foreach (var step in steps.Take(256 - pending.Count))
            pending.Add(new(kind, step with { OffsetMilliseconds = 0 }, priority, device, milliseconds + Math.Max(0, step.OffsetMilliseconds)));
        Advance(milliseconds, temperament, maximum);
    }
    public void Advance(double milliseconds, LocalHapticTemperament temperament, int maximum)
    {
        now = double.IsFinite(milliseconds) ? Math.Max(now, milliseconds) : now;
        active.RemoveAll(pulse => now >= End(pulse) + 100);
        var capacity = Math.Clamp(Math.Max(1, maximum) + temperament.PulsePriorityBias, 1, 12);
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            var pulse = pending[index]; if (pulse.Started > now) continue; pending.RemoveAt(index);
            if (active.Count >= capacity)
            {
                var victim = active.FindIndex(value => now >= End(value));
                if (victim < 0)
                {
                    var weakest = active.Min(value => value.Priority);
                    if (weakest >= pulse.Priority) continue;
                    victim = active.FindIndex(value => value.Priority == weakest);
                }
                active.RemoveAt(victim);
            }
            active.Add(pulse with { Started = now, Step = temperament.Shape(pulse.Step) });
        }
    }
    public double Sample(string device, Func<LocalDeviceEvent, bool> matches, double transientScale)
    {
        var eligible = active.Where(value => value.Device is { } selected ? selected == device : matches(value.Event)).ToArray();
        var instants = new List<double> { now, now + 50, now + 100 };
        foreach (var pulse in active)
        {
            var end = End(pulse) - 1; var crest = Math.Min(pulse.Started + pulse.Step.AttackMilliseconds, end);
            var crestEnd = Math.Min(crest + pulse.Step.HoldMilliseconds, end);
            if (crest <= now + 100 && crestEnd >= now) instants.Add(Math.Clamp(crest, now, now + 100));
        }
        return eligible.GroupBy(value => value.Priority).Select(group => instants.Max(instant =>
            Math.Min(1, group.Sum(pulse => LocalHapticPatterns.Sample([pulse.Step], instant - pulse.Started) * transientScale)))).DefaultIfEmpty().Max();
    }
    private static double End(Pulse pulse) => pulse.Started + pulse.Step.AttackMilliseconds + pulse.Step.HoldMilliseconds + pulse.Step.DecayMilliseconds;
}
