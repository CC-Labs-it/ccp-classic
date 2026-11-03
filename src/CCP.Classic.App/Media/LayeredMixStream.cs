using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CCP.Classic.App.Media;

internal sealed class LayeredMixStream(LayeredPcmTrack[] tracks, CancellationToken cancellationToken, Action buffering) : Stream
{
    private const int Rate = LayeredPcmTrack.Rate;
    private readonly byte[] header = Header(), pending = new byte[2048];
    private readonly short[][] samples = tracks.Select(_ => new short[512]).ToArray();
    private readonly long[] positions = new long[tracks.Length];
    private readonly double[] gains = tracks.Select(_ => .7).ToArray();
    private readonly Stopwatch clock = new();
    private int headerAt, pendingAt = 2048;
    private long frames;
    private double master;
    private volatile bool waiting;
    internal long ProducedFrames => Interlocked.Read(ref frames);
    internal double AudibleSeconds => Math.Max(0, ProducedFrames / (double)Rate - .12);
    internal double MaximumAheadSeconds { get; private set; }
    internal bool Waiting => waiting;
    internal void SetGain(Guid id, double gain)
    { for (var i = 0; i < tracks.Length; i++) if (tracks[i].Id == id) Volatile.Write(ref gains[i], Finite(gain)); }
    internal void SetMaster(double gain) => Volatile.Write(ref master, Finite(gain));
    private static double Finite(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (cancellationToken.IsCancellationRequested) return 0;
        var total = 0; count = Math.Min(count, pending.Length);
        while (total < count && !cancellationToken.IsCancellationRequested)
        {
            if (headerAt < header.Length)
            { var take = Math.Min(count - total, header.Length - headerAt); header.AsSpan(headerAt, take).CopyTo(buffer.AsSpan(offset + total)); total += take; headerAt += take; continue; }
            if (pendingAt == pending.Length && !Build()) return total;
            var copy = Math.Min(count - total, pending.Length - pendingAt);
            pending.AsSpan(pendingAt, copy).CopyTo(buffer.AsSpan(offset + total)); pendingAt += copy; total += copy;
        }
        return total;
    }
    private bool Build()
    {
        if (!clock.IsRunning) clock.Start();
        var due = (frames + 256d) / Rate - .12;
        while (!cancellationToken.IsCancellationRequested && clock.Elapsed.TotalSeconds < due)
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Clamp(due - clock.Elapsed.TotalSeconds, .001, .02)));
        if (cancellationToken.IsCancellationRequested) return false;
        while (Enumerable.Range(0, tracks.Length).Any(i => !tracks[i].Failed && !tracks[i].Complete && tracks[i].Frames * 2 - positions[i] < 512))
        {
            if (!waiting) { waiting = true; clock.Stop(); buffering(); }
            if (cancellationToken.WaitHandle.WaitOne(10)) return false;
        }
        if (waiting) { waiting = false; clock.Start(); }
        if (tracks.All(track => track.Failed)) return false;
        for (var i = 0; i < tracks.Length; i++)
        {
            var target = samples[i].AsSpan(); target.Clear(); var copied = 0;
            while (copied < target.Length && !tracks[i].Failed)
            {
                var read = tracks[i].Read(positions[i], target[copied..]); positions[i] += read; copied += read;
                if (copied == target.Length) break;
                if (!tracks[i].Complete || tracks[i].Frames == 0) break;
                positions[i] = 0;
            }
            Interlocked.Exchange(ref tracks[i].ConsumedFrames, tracks[i].Complete ? tracks[i].Frames : positions[i] / 2);
        }
        var mixed = MemoryMarshal.Cast<byte, float>(pending.AsSpan()); var volume = Volatile.Read(ref master);
        for (var sample = 0; sample < mixed.Length; sample++)
        {
            double sum = 0;
            for (var track = 0; track < tracks.Length; track++) sum += samples[track][sample] / 32768d * Volatile.Read(ref gains[track]);
            mixed[sample] = (float)Math.Clamp(sum * volume, -1, 1);
        }
        Interlocked.Add(ref frames, 256); MaximumAheadSeconds = Math.Max(MaximumAheadSeconds, frames / (double)Rate - clock.Elapsed.TotalSeconds);
        pendingAt = 0; return true;
    }
    internal IReadOnlyList<AudioPlaybackSample> AudioSamples(double outputSeconds, TimeProvider time)
    {
        if (!double.IsFinite(outputSeconds) || outputSeconds < 0 || cancellationToken.IsCancellationRequested) return [];
        var played = (long)(Math.Min(outputSeconds, ProducedFrames / (double)Rate) * Rate);
        var volume = Volatile.Read(ref master); if (volume <= 0 || waiting) return [];
        return tracks.Select((track, index) => (Track: track, Gain: Volatile.Read(ref gains[index])))
            .Where(item => !item.Track.Failed && item.Track.Frames > 0 && item.Gain > 0)
            .Select(item => new AudioPlaybackSample(item.Track.Path,
                (item.Track.Complete ? played % item.Track.Frames : Math.Min(played, item.Track.Frames)) / (double)Rate,
                item.Gain * volume, true, time.GetTimestamp())).ToArray();
    }
    private static byte[] Header()
    {
        var bytes = new byte[44]; "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), uint.MaxValue);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 3); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), Rate); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), Rate * 8);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 8); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 32);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), uint.MaxValue); return bytes;
    }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
