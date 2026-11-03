using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Media;

internal readonly record struct PcmAudioFeature(double Seconds, float Rms, float Bass, float Onset, float HighFlux, float BassFlux, float High);
public readonly record struct AudioDspLevels(double Combined, double Low, double High);

// Original AudioAnalyzer's four passes, fed by the existing timestamped PCM decode.
internal sealed class AudioFeatureTrack
{
    private sealed class Chunk(PcmAudioFeature[] frames)
    {
        public readonly PcmAudioFeature[] Frames = frames;
        public Task<AudioDspLevels[]>? Pending;
        public DspKey Key;
    }
    private readonly object gate = new();
    private readonly List<PcmAudioFeature> pending = [];
    private readonly List<Chunk> chunks = [];
    private double origin;
    internal readonly record struct DspKey(double Sensitivity, double Smoothing, double Rms, double Bass, double Onset, double Minimum, double Maximum)
    {
        public static DspKey From(AudioSyncDspOptions options) => new(options.Sensitivity, options.Smoothing, options.RmsWeight,
            options.BassWeight, options.OnsetWeight, options.MinimumIntensity, options.MaximumIntensity);
    }
    public void Append(PcmAudioFeature value)
    {
        lock (gate)
        {
            if (pending.Count > 0 && value.Seconds - origin >= 300) SealCore();
            if (pending.Count == 0) origin = value.Seconds;
            if (chunks.Count < 25 && pending.Count < 30000) pending.Add(value);
        }
    }
    public void Seal() { lock (gate) SealCore(); }
    private void SealCore()
    {
        if (pending.Count == 0) return;
        chunks.Add(new(pending.ToArray())); pending.Clear();
    }
    public AudioDspLevels Sample(double seconds, AudioSyncDspOptions options)
    {
        lock (gate)
        {
            var chunk = chunks.LastOrDefault(value => value.Frames[0].Seconds <= seconds);
            if (chunk is null || seconds > chunk.Frames[^1].Seconds + .05) return default;
            var key = DspKey.From(options);
            if (chunk.Pending is null || chunk.Pending.IsCompleted && chunk.Key != key)
            { chunk.Key = key; chunk.Pending = Task.Run(() => Render(chunk.Frames, key)); }
            if (chunk.Key != key || !chunk.Pending.IsCompletedSuccessfully) return default;
            var index = Index(chunk.Frames, seconds); return chunk.Pending.Result[index];
        }
    }
    public AudioSpectralFeatures Features(double seconds)
    {
        lock (gate)
        {
            var frames = chunks.LastOrDefault(value => value.Frames[0].Seconds <= seconds)?.Frames;
            if (frames is null || seconds > frames[^1].Seconds + .05) return default;
            var value = frames[Index(frames, seconds)];
            return new(value.Rms / Math.Max(.001f, frames.Max(frame => frame.Rms)), value.Bass / Math.Max(.001f, frames.Max(frame => frame.Bass)),
                value.Onset / Math.Max(.001f, frames.Max(frame => frame.Onset)));
        }
    }
    private static int Index(PcmAudioFeature[] frames, double seconds)
    {
        var low = 0; var high = frames.Length;
        while (low < high) { var middle = (low + high) / 2; if (frames[middle].Seconds <= seconds) low = middle + 1; else high = middle; }
        return Math.Max(0, low - 1);
    }
    internal static AudioDspLevels[] Render(PcmAudioFeature[] frames, DspKey settings)
    {
        if (frames.Length == 0) return [];
        var maxRms = Math.Max(.001f, frames.Max(frame => frame.Rms)); var maxBass = Math.Max(.001f, frames.Max(frame => frame.Bass));
        var maxOnset = Math.Max(.001f, frames.Max(frame => frame.Onset)); var maxHigh = Math.Max(.001f, frames.Max(frame => frame.High));
        var raw = new float[frames.Length]; var highPulses = new bool[frames.Length]; var bassPulses = new bool[frames.Length];
        var highHistory = new Queue<float>(); var bassHistory = new Queue<float>(); var highCooldown = 0; var bassCooldown = 0;
        var rmsBaseline = frames[0].Rms; var bassBaseline = frames[0].Bass;
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            raw[index] = (float)(Math.Max(0, frame.Rms / maxRms - rmsBaseline / maxRms * .85f) * settings.Rms
                + Math.Max(0, frame.Bass / maxBass - bassBaseline / maxBass * .85f) * settings.Bass
                + (maxOnset > .001f ? frame.Onset / maxOnset : 0) * settings.Onset);
            rmsBaseline += (frame.Rms - rmsBaseline) * (frame.Rms > rmsBaseline ? .015f : .003f);
            bassBaseline += (frame.Bass - bassBaseline) * (frame.Bass > bassBaseline ? .015f : .003f);
            highPulses[index] = Detect(frame.HighFlux, highHistory, ref highCooldown, 2, 6);
            bassPulses[index] = Detect(frame.BassFlux, bassHistory, ref bassCooldown, 2.5f, 8);
        }
        var sorted = raw.Order().ToArray(); var threshold = sorted[sorted.Length / 2] * .3f;
        var result = new AudioDspLevels[frames.Length]; var highRemaining = 0; var bassRemaining = 0;
        double previous = 0, previousLow = 0, previousHigh = 0;
        for (var index = 0; index < frames.Length; index++)
        {
            if (highPulses[index]) highRemaining = 6; if (bassPulses[index]) bassRemaining = 8;
            double combined;
            if (highRemaining > 0) { combined = previous = 1; highRemaining--; bassRemaining = 0; }
            else if (bassRemaining > 0) { combined = previous = .95; bassRemaining--; }
            else
            {
                var shaped = raw[index] < threshold ? 0 : Shape(raw[index], settings);
                combined = previous = previous * settings.Smoothing + shaped * (1 - settings.Smoothing);
            }
            var low = combined <= 0 ? 0 : Shape(frames[index].Bass / maxBass, settings);
            var high = combined <= 0 ? 0 : Shape(frames[index].High / maxHigh, settings);
            previousLow = previousLow * settings.Smoothing + low * (1 - settings.Smoothing);
            previousHigh = previousHigh * settings.Smoothing + high * (1 - settings.Smoothing);
            result[index] = new(combined, previousLow, previousHigh);
        }
        return result;
    }
    private static bool Detect(float value, Queue<float> history, ref int cooldown, float multiplier, int pulse)
    {
        history.Enqueue(value); if (history.Count > 43) history.Dequeue();
        if (cooldown > 0) { cooldown--; return false; }
        if (history.Count < 10) return false;
        var average = history.Average();
        if (average <= .001f || value <= average * multiplier) return false;
        cooldown = pulse; return true;
    }
    private static double Shape(double value, DspKey settings) => Math.Clamp(value > 0 ? Math.Pow(value, 1 / settings.Sensitivity) : value,
        settings.Minimum, settings.Maximum);
}
