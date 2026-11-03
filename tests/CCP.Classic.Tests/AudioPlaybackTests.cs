using System.Collections.Concurrent;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class AudioPlaybackTests
{
    private static Task Until(Func<bool> condition) => Task.Run(() =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), "Audio worker did not reach the expected state"),
        TestContext.Current.CancellationToken);

    [Fact]
    public async Task InvalidOrNetworkInputsNeverInitializeNativeAudio()
    {
        var opened = 0;
        var playback = new LibVlcAudioPlayback(() => { Interlocked.Increment(ref opened); return new Device(); });
        try
        {
            foreach (var path in new[] { "https://example.invalid/test.mp3", @"\\server\share\test.mp3", "//server/share/test.mp3", "missing.mp3" })
                Assert.False(playback.Play(path, .5));
            using var file = new LocalFile(".m3u");
            Assert.False(playback.Play(file.Path, .5));
            Assert.False(playback.Play(file.Path, double.NaN));
            Assert.Equal(0, Volatile.Read(ref opened));
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task PanicDuringNativeInitializationCancelsBothOpeningAndQueuedRequests()
    {
        using var file = new LocalFile(".wav");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = new Device();
        var playback = new LibVlcAudioPlayback(() => { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return device; });
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(playback.Play(file.Path, .5));
            playback.StopAll();
            release.Set();
            Assert.True(playback.Play(file.Path, .25));
            await Until(() => device.Voices.Any(v => v.Started));
            Assert.Single(device.Voices);
            playback.StopAll();
            await Until(() => device.Voices.All(v => v.Disposed));
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task AStopReceivedDuringRefreshDoesNotStopTheNextGeneration()
    {
        using var file = new LocalFile(".wav");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blockRefresh = 0;
        var device = new Device(refreshing: () =>
        {
            if (Interlocked.Exchange(ref blockRefresh, 0) == 0) return;
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
        });
        var playback = new LibVlcAudioPlayback(() => device);
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            await Until(() => device.Voices.Any(voice => voice.Started));
            var previous = Assert.Single(device.Voices);
            Interlocked.Exchange(ref blockRefresh, 1);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            playback.StopAll();
            Assert.True(playback.Play(file.Path, .5));
            release.Set();
            await Until(() => device.Voices.Count == 2
                && device.Voices.Single(voice => !ReferenceEquals(voice, previous)).RefreshCount >= 2);
            Assert.True(previous.Disposed);
            Assert.False(device.Voices.Single(voice => !ReferenceEquals(voice, previous)).Disposed);
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task MasterGainChangesEveryActiveVoiceWithoutLosingPerSoundGain()
    {
        using var file = new LocalFile(".wav");
        var device = new Device();
        var playback = new LibVlcAudioPlayback(() => device);
        double[] Gains() => device.Voices.Select(voice => Math.Round(voice.Gain, 8)).Order().ToArray();
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            Assert.True(playback.Play(file.Path, .25));
            await Until(() => device.Voices.Count == 2 && device.Voices.All(voice => voice.Started));
            playback.SetMasterGain(.4);
            await Until(() => Gains().SequenceEqual(new[] { .1, .2 }));
            playback.SetMasterGain(0);
            await Until(() => Gains().All(gain => gain == 0));
            playback.SetMasterGain(2);
            await Until(() => Gains().SequenceEqual(new[] { .25, .5 }));
            playback.SetMasterGain(double.NaN);
            await Until(() => Gains().All(gain => gain == 0));
            Assert.All(device.Voices, voice => Assert.False(voice.Disposed));
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task AVoiceOpeningDuringAMasterChangeStartsAtTheLatestGain()
    {
        using var file = new LocalFile(".wav");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var device = new Device(opening: () =>
        {
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
        });
        var playback = new LibVlcAudioPlayback(() => device);
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            playback.SetMasterGain(.4);
            release.Set();
            await Until(() => device.Voices.Any(voice => voice.Started));
            Assert.Equal(.2, Assert.Single(device.Voices).StartGain, 8);
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task ConcurrencyIsBoundedAndNativeTeardownNeverBlocksTheCaller()
    {
        using var file = new LocalFile(".wav");
        using var release = new ManualResetEventSlim();
        using var stopEntered = new ManualResetEventSlim();
        var device = new Device(() => { stopEntered.Set(); release.Wait(TestContext.Current.CancellationToken); });
        var playback = new LibVlcAudioPlayback(() => device);
        try
        {
            for (var index = 0; index < 15; index++) Assert.True(playback.Play(file.Path, .5));
            await Until(() => device.Voices.Count == 10);
            Assert.Equal(10, device.Voices.Count);
            var dispose = Task.Run(playback.Dispose, TestContext.Current.CancellationToken);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(stopEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(10, device.Voices.Count);
        }
        finally { release.Set(); playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
        Assert.All(device.Voices, voice => Assert.True(voice.Disposed));
    }

    [Fact]
    public async Task MissingNativeRuntimeIsReportedWithoutEscapingTheWorker()
    {
        using var file = new LocalFile(".wav");
        var playback = new LibVlcAudioPlayback(() => throw new DllNotFoundException("libvlc unavailable"));
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            await Until(() => playback.LastError is not null);
            Assert.Equal("libvlc unavailable", playback.LastError);
            Assert.False(playback.Play(file.Path, .5));
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task LaterSuccessfulPlaybackClearsAnEarlierOutputError()
    {
        using var file = new LocalFile(".wav");
        var attempts = 0;
        var device = new Device(opening: () =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Output not ready");
        });
        var playback = new LibVlcAudioPlayback(() => device);
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            await Until(() => playback.LastError is not null);
            Assert.True(playback.Play(file.Path, .5));
            await Until(() => device.Voices.Any(v => v.Started) && playback.LastError is null);
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task AudioFailureAfterStartingIsReportedAndTheVoiceIsReleased()
    {
        using var file = new LocalFile(".wav");
        var device = new Device();
        var playback = new LibVlcAudioPlayback(() => device);
        try
        {
            Assert.True(playback.Play(file.Path, .5));
            await Until(() => device.Voices.Any(voice => voice.Started));
            var voice = Assert.Single(device.Voices);
            voice.Fail();
            await Until(() => voice.Disposed && playback.LastError == "Playback failed after starting");
        }
        finally { playback.Dispose(); await playback.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public void LinearGainMappingAndLinuxRuntimeSonamesMatchThePort()
    {
        Assert.Equal(50, NativeAudioDevice.PlayerVolume(.125));
        Assert.Equal(100, NativeAudioDevice.PlayerVolume(2));
        var candidates = new List<string>();
        var handle = NativeAudioDevice.Resolve("libvlc", name =>
        {
            candidates.Add(name);
            return name == "libvlc.so.5" ? new IntPtr(1) : IntPtr.Zero;
        });
        Assert.Equal(new IntPtr(1), handle);
        Assert.Equal(new[] { "libvlc.so", "libvlc.so.5" }, candidates);
        Assert.Contains("--aout=directsound", NativeAudioDevice.Options(true));
        Assert.DoesNotContain(NativeAudioDevice.Options(true), option => option.Contains(",any", StringComparison.Ordinal));
        Assert.DoesNotContain(NativeAudioDevice.Options(false), option => option.StartsWith("--aout=", StringComparison.Ordinal));
        Assert.Contains("--no-metadata-network-access", NativeAudioDevice.Options(true));
    }

    private sealed class Device(Action? stopping = null, Action? opening = null, Action? refreshing = null) : IAudioDevice
    {
        public ConcurrentBag<Voice> Voices { get; } = new();
        public IAudioVoice Open(string path, double gain, Action wake)
        {
            opening?.Invoke();
            var voice = new Voice(gain, stopping, refreshing); Voices.Add(voice); return voice;
        }
        public void Dispose() { }
    }

    private sealed class Voice(double initialGain, Action? stopping, Action? refreshing) : IAudioVoice
    {
        private int started, disposed, failed, refreshCount;
        private double gain = initialGain, startGain;
        public bool Started => Volatile.Read(ref started) != 0;
        public bool Disposed => Volatile.Read(ref disposed) != 0;
        public bool IsFinished => Volatile.Read(ref failed) != 0;
        public int RefreshCount => Volatile.Read(ref refreshCount);
        public double Gain => Volatile.Read(ref gain);
        public double StartGain => Volatile.Read(ref startGain);
        public void Start() { startGain = Gain; Interlocked.Exchange(ref started, 1); }
        public void SetGain(double value) => Volatile.Write(ref gain, value);
        public void Fail() => Interlocked.Exchange(ref failed, 1);
        public void Refresh()
        {
            refreshing?.Invoke();
            Interlocked.Increment(ref refreshCount);
            if (IsFinished) throw new InvalidOperationException("Playback failed after starting");
        }
        public void Stop() => stopping?.Invoke();
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    }

    private sealed class LocalFile : IDisposable
    {
        public string Path { get; }
        public LocalFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(Path, [0]);
        }
        public void Dispose() => File.Delete(Path);
    }
}
