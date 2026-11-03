using CCP.Classic.Core;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Media;

public sealed class RmsEnvelope
{
    internal const int BinsPerSecond = 20, MaximumBins = 144000;
    private float[] bins = [];
    internal readonly AudioFeatureTrack FeatureTrack = new();
    private double end;
    private int failed, complete;
    public bool Failed => Volatile.Read(ref failed) != 0;
    public bool IsComplete => Volatile.Read(ref complete) != 0;
    public bool IsReady => !Failed && Volatile.Read(ref end) > 0;
    internal string? FailureReason { get; private set; }
    public double DurationSeconds => Volatile.Read(ref end);
    public double Sample(double actualPlaybackSeconds)
    {
        if (Failed || !double.IsFinite(actualPlaybackSeconds) || actualPlaybackSeconds < 0 || actualPlaybackSeconds >= Volatile.Read(ref end)) return 0;
        var index = (int)(actualPlaybackSeconds * BinsPerSecond); var values = Volatile.Read(ref bins);
        return index < values.Length ? Volatile.Read(ref values[index]) : 0;
    }
    internal void Publish(int index, double rms, double until)
    {
        if (Failed || index is < 0 or >= MaximumBins) return;
        if (index >= bins.Length) { var grown = new float[Math.Min(MaximumBins, Math.Max(index + 1, Math.Max(32, bins.Length * 2)))]; bins.CopyTo(grown, 0); Volatile.Write(ref bins, grown); }
        Volatile.Write(ref bins[index], (float)Math.Clamp(rms, 0, 1)); Volatile.Write(ref end, Math.Max(end, until));
    }
    internal void Finish() { FeatureTrack.Seal(); Volatile.Write(ref complete, 1); }
    public AudioSpectralFeatures SampleFeatures(double seconds)
    {
        if (Sample(seconds) <= 0) return default;
        return FeatureTrack.Features(seconds);
    }
    public AudioDspLevels SampleDsp(double seconds, AudioSyncDspOptions options) => Sample(seconds) <= 0 ? default : FeatureTrack.Sample(seconds, options);
    internal void Fail(string? reason = null) { FailureReason = reason; Volatile.Write(ref failed, 1); Finish(); }
    internal static RmsEnvelope Unavailable { get; } = CreateUnavailable();
    private static RmsEnvelope CreateUnavailable() { var result = new RmsEnvelope(); result.Fail(); return result; }
}

internal sealed class PcmRmsAccumulator(RmsEnvelope envelope, int rate = 48000, int channels = 2)
{
    private double[] squares = [];
    private readonly PcmSpectralFeatures spectral = new(envelope, rate);
    public bool AtLimit { get; private set; }
    public void Append(ReadOnlySpan<short> samples, double startSeconds)
    {
        if (envelope.Failed || !double.IsFinite(startSeconds) || rate < 20 || channels is < 1 or > 8 || samples.Length % channels != 0) return;
        var frameCount = samples.Length / channels;
        if (startSeconds <= -frameCount / (double)rate) return;
        var first = Math.Max(0, (int)Math.Ceiling(-startSeconds * rate));
        var previous = -1; var until = 0d;
        for (var frame = first; frame < frameCount; frame++)
        {
            var seconds = startSeconds + frame / (double)rate;
            if (seconds >= RmsEnvelope.MaximumBins / (double)RmsEnvelope.BinsPerSecond) { AtLimit = true; break; }
            var bin = (int)Math.Floor(seconds * RmsEnvelope.BinsPerSecond + 1e-8);
            if (bin < 0) continue;
            if (previous != bin && previous >= 0) envelope.Publish(previous, Math.Sqrt(squares[previous] / (rate / (double)RmsEnvelope.BinsPerSecond * channels)), until);
            if (bin >= squares.Length) Array.Resize(ref squares, Math.Min(RmsEnvelope.MaximumBins, Math.Max(bin + 1, Math.Max(32, squares.Length * 2))));
            double mono = 0;
            for (var channel = 0; channel < channels; channel++) { var value = samples[frame * channels + channel] / 32768d; squares[bin] += value * value; mono += value; }
            spectral.Append(mono / channels, seconds);
            // Empty parts of a 50ms window are silence, including genuine timestamp gaps.
            previous = bin; until = Math.Min(7200, seconds + 1d / rate);
        }
        if (previous >= 0) envelope.Publish(previous, Math.Sqrt(squares[previous] / (rate / (double)RmsEnvelope.BinsPerSecond * channels)), until);
    }
}

public sealed class AudioEnvelopeCache : IDisposable
{
    private sealed class Entry(string path)
    { public readonly string Path = path; public readonly RmsEnvelope Envelope = new(); public readonly CancellationTokenSource Stop = new(); }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly LinkedList<string> recent = new();
    private readonly Queue<Entry> pending = new();
    private readonly Action<string, PcmRmsAccumulator, CancellationToken> decode;
    private Task worker = Task.CompletedTask;
    private bool working, disposed;
    private long version;
    public AudioEnvelopeCache() : this(new FfmpegEnvelopeDecoder().Decode) { }
    internal AudioEnvelopeCache(Action<string, PcmRmsAccumulator, CancellationToken> decoder) => decode = decoder;
    internal Task Completion { get { lock (gate) return worker; } }
    internal int Count { get { lock (gate) return entries.Count; } }
    internal int PendingCount { get { lock (gate) return pending.Count; } }

    public RmsEnvelope Get(string? path)
    {
        if (!MediaLibrary.IsLocalPath(path)) return RmsEnvelope.Unavailable;
        var full = Path.GetFullPath(path!);
        lock (gate)
        {
            if (disposed) return RmsEnvelope.Unavailable;
            if (entries.TryGetValue(full, out var existing)) { recent.Remove(full); recent.AddLast(full); return existing.Envelope; }
            while (entries.Count >= 16) { var oldest = recent.First!.Value; recent.RemoveFirst(); var evicted = entries[oldest]; entries.Remove(oldest); evicted.Envelope.Fail(); Cancel(evicted); }
            var entry = new Entry(full); entries.Add(full, entry); recent.AddLast(full);
            while (pending.Count >= 16) { var dropped = pending.Dequeue(); dropped.Envelope.Fail(); Cancel(dropped); dropped.Stop.Dispose(); }
            pending.Enqueue(entry);
            if (!working) { working = true; var own = ++version; worker = Task.Run(() => Work(own)); }
            return entry.Envelope;
        }
    }
    private void Work(long ownVersion)
    {
        try
        {
            while (true)
            {
                Entry current;
                lock (gate) { if (disposed || !pending.TryDequeue(out current!)) { working = false; return; } }
                try
                {
                    if (!current.Envelope.Failed) { decode(current.Path, new(current.Envelope), current.Stop.Token); if (!current.Envelope.IsReady) current.Envelope.Fail("No PCM samples were produced"); else current.Envelope.Finish(); }
                }
                catch (Exception error) { current.Envelope.Fail(error.GetType().Name + ": " + error.Message); }
                finally { current.Stop.Dispose(); }
            }
        }
        finally { lock (gate) { if (version == ownVersion) working = false; } }
    }
    public void Clear()
    {
        lock (gate)
        {
            foreach (var entry in entries.Values) { entry.Envelope.Fail(); Cancel(entry); }
            entries.Clear(); recent.Clear();
        }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; Clear(); while (pending.TryDequeue(out var entry)) entry.Stop.Dispose(); } }
    private static void Cancel(Entry entry) { try { entry.Stop.Cancel(); } catch (ObjectDisposedException) { } }

}
