using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Media;

public sealed class AudioSyncDsp
{
    private sealed class Track { public double Previous, LastTime = -1, LastValue; }
    private readonly Dictionary<string, Track> tracks = new(StringComparer.Ordinal);
    public AudioDspLevels LastLevels { get; private set; }
    public void Clear() { tracks.Clear(); LastLevels = default; }

    public double Sample(IEnumerable<AudioPlaybackSample> sources, Func<string, RmsEnvelope> envelope,
        TimeProvider clock, AudioSyncDspOptions options, out bool unavailable)
    {
        unavailable = false; var level = 0d; double low = 0, high = 0; var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.Take(64))
        {
            if (!source.IsPlaying || source.Gain <= 0 || !double.IsFinite(source.Gain) || source.SourceSeconds < 0 || !double.IsFinite(source.SourceSeconds)
                || clock.GetElapsedTime(source.ObservedAt).TotalMilliseconds is < 0 or >= 300) continue;
            active.Add(source.Path); if (!tracks.TryGetValue(source.Path, out var track)) tracks[source.Path] = track = new();
            var value = envelope(source.Path); unavailable |= value.Failed;
            var seconds = source.SourceSeconds + options.OffsetMilliseconds / 1000d;
            // Genuine silence and unavailable/past-end analysis must zero immediately, including EMA memory.
            if (value.Failed || value.Sample(seconds) <= 0) { track.Previous = track.LastValue = 0; track.LastTime = seconds; continue; }
            var levels = value.SampleDsp(seconds, options);
            var gain = Math.Clamp(source.Gain, 0, 1) * Math.Clamp(options.LiveIntensity, 0, 1);
            low = Math.Max(low, levels.Low * gain); high = Math.Max(high, levels.High * gain);
            track.LastValue = levels.Combined * Math.Clamp(options.LiveIntensity, 0, 1); track.LastTime = seconds;
            level = Math.Max(level, track.LastValue * Math.Clamp(source.Gain, 0, 1));
        }
        foreach (var key in tracks.Keys.Where(key => !active.Contains(key)).ToArray()) tracks.Remove(key);
        LastLevels = new(Math.Clamp(level, 0, 1), low, high); return LastLevels.Combined;
    }
    internal static double Combine(AudioSpectralFeatures feature, AudioSyncDspOptions options, ref double previous)
    {
        var raw = feature.Rms * options.RmsWeight + feature.Bass * options.BassWeight + feature.Onset * options.OnsetWeight;
        if (!double.IsFinite(raw) || raw <= 0) { previous = 0; return 0; }
        var shaped = Math.Pow(Math.Clamp(raw, 0, 1), 1 / Math.Clamp(options.Sensitivity, .1, 3));
        shaped = Math.Clamp(shaped, Math.Clamp(options.MinimumIntensity, 0, .5), Math.Clamp(options.MaximumIntensity, .5, 1));
        var smoothing = Math.Clamp(options.Smoothing, 0, .95);
        return previous = Math.Clamp(previous * smoothing + shaped * (1 - smoothing), 0, 1);
    }
}
