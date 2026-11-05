using System.Diagnostics;
using System.Text.Json;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class NativeLayeredAudioTests
{
    public static bool NativeRequested => OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_PROBE") == "1";

    [Fact(Skip = "Set CCP_CLASSIC_FFMPEG_PROBE=1 for the silent native layered-audio check.", SkipUnless = nameof(NativeRequested))]
    public async Task TwoGeneratedTracksKeepTheirWholeTailsLoopLiveGainsAndCleanUpWithoutAnAudioDevice()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "classic-native-layers-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var cacheRoot = Path.Combine(folder, "cache"); var backend = new ObservedBackend();
        var options = new LayeredAudioOptions { Enabled = true, Tracks = [new() { Path = WriteWave(folder, "short.wav", 44100, 2, 16317) },
            new() { Path = WriteWave(folder, "long.wav", 48000, 1, 161760) }] };
        using var service = new LayeredAudioService(() => backend, () => new LayeredPcmCache(cacheRoot));
        try
        {
            AnalysisProcessBudget.RequireHeadroom();
            await service.StartAsync(options, .8).WaitAsync(TimeSpan.FromSeconds(15), token);
            await Wait(() => service.IsPlaying && backend.Tracks.Count == 2 && backend.Tracks.All(track => track.Complete), token);
            Assert.Equal(1, backend.Outputs); Assert.DoesNotContain(backend.Tracks, track => track.Failed);
            Assert.Equal(16317, backend.Tracks[0].Frames); Assert.Equal(148617, backend.Tracks[1].Frames);
            Assert.True(backend.Pauses > 0);
            await Wait(() => backend.LastPosition > .8 && service.GetAudioSamples().Count == 2, token);
            var before = service.GetAudioSamples(); var outputPosition = backend.LastPosition;
            Assert.All(before, sample => Assert.Equal(.392, sample.Gain, 4));
            Assert.All(before, sample => Assert.InRange(TimeProvider.System.GetElapsedTime(sample.ObservedAt).TotalMilliseconds, 0, 300));
            foreach (var sample in before)
            {
                var duration = backend.Tracks.Single(track => track.Path == sample.Path).Frames / 44100d;
                Assert.InRange(sample.SourceSeconds, 0, duration);
                var delta = Math.Abs(outputPosition % duration - sample.SourceSeconds);
                Assert.InRange(Math.Min(delta, Math.Abs(duration - delta)), 0, .08);
            }
            options.MasterVolume = 60; options.Tracks[0].Volume = 20; service.UpdateGains(options, .8, .25);
            await Wait(() => service.GetAudioSamples().Count == 2 && Math.Abs(service.GetAudioSamples()[0].Gain - .072) < .0001, token);
            Assert.Equal(1, backend.Outputs); Assert.Equal(.252, service.GetAudioSamples()[1].Gain, 4);
            var samples = service.GetAudioSamples(); var stop = Stopwatch.StartNew(); await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.False(service.IsActive); Assert.Empty(service.GetAudioSamples()); Assert.Empty(Directory.GetDirectories(cacheRoot));
            Assert.InRange(stop.Elapsed.TotalSeconds, 0, 5);
            if (Environment.GetEnvironmentVariable("CCP_CLASSIC_FFMPEG_FIXTURES") is { } proof)
                File.WriteAllText(Path.Combine(proof, "native-layered-audio.json"), JsonSerializer.Serialize(new
                {
                    backend.Outputs, backend.Pauses, backend.LastPosition, sampleFrames = backend.Tracks.Select(track => track.Frames).ToArray(),
                    gains = samples.Select(sample => sample.Gain).ToArray(), sourcePositions = samples.Select(sample => sample.SourceSeconds).ToArray(),
                    stopMilliseconds = stop.Elapsed.TotalMilliseconds, silentOutput = true, cacheRemoved = true
                }));
        }
        finally { await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), token); Directory.Delete(folder, true); }
    }
    private static async Task Wait(Func<bool> condition, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed.TotalSeconds < 15)
        { AnalysisProcessBudget.RequireHeadroom(); await Task.Delay(20, token); }
        Assert.True(condition());
    }
    private static string WriteWave(string folder, string name, int rate, int channels, int frames)
    {
        var path = Path.Combine(folder, name); using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + frames * channels * 4); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)3); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 4);
        writer.Write((short)(channels * 4)); writer.Write((short)32); writer.Write("data"u8); writer.Write(frames * channels * 4);
        for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < channels; channel++) writer.Write((float)(.2 * Math.Sin((frame + channel * 7) * .117)));
        return path;
    }
    private sealed class ObservedBackend : ILayeredAudioBackend
    {
        private readonly LayeredAudioNative native = new(silentOutput: true);
        public List<LayeredPcmTrack> Tracks { get; } = [];
        public int Outputs, Pauses; public double LastPosition;
        public ILayeredAudioDecoder OpenDecoder(LayeredPcmTrack track, CancellationToken token, Action changed)
        { Tracks.Add(track); return new Decoder(native.OpenDecoder(track, token, changed), this); }
        public ILayeredAudioOutput OpenOutput(LayeredMixStream stream, TimeProvider clock)
        { Outputs++; return new Output(native.OpenOutput(stream, clock), this); }
        public void Dispose() => native.Dispose();
        private sealed class Decoder(ILayeredAudioDecoder inner, ObservedBackend owner) : ILayeredAudioDecoder
        {
            private bool paused;
            public bool Finished => inner.Finished;
            public void Poll(bool pause) { if (pause && !paused) Interlocked.Increment(ref owner.Pauses); paused = pause; inner.Poll(pause); }
            public void Dispose() => inner.Dispose();
        }
        private sealed class Output(ILayeredAudioOutput inner, ObservedBackend owner) : ILayeredAudioOutput
        {
            public bool IsPlaying => inner.IsPlaying;
            public double PositionSeconds => inner.PositionSeconds;
            public void Poll(bool paused) { inner.Poll(paused); Volatile.Write(ref owner.LastPosition, inner.PositionSeconds); }
            public void Mute() => inner.Mute(); public void Dispose() => inner.Dispose();
        }
    }
}
