using System.Collections.Concurrent;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LayeredAudioServiceTests
{
    [Fact]
    public async Task ConstructionIsIdleAndEightTracksUseOneOutputWhileLiveGainsNeverRebuild()
    {
        using var fixture = new Fixture(); var graph = new Graph(); var service = fixture.Service(graph);
        try
        {
            Assert.Equal(0, graph.Created); Assert.False(service.IsActive); Assert.Empty(service.GetAudioSamples());
            var options = fixture.Options(8); await service.StartAsync(options, .5);
            await Until(() => service.IsPlaying && service.GetAudioSamples().Count == 8);
            Assert.Equal(1, graph.Created); Assert.Equal(8, graph.Decoders.Count); Assert.Single(graph.Outputs);
            options.Tracks[0].Volume = 0; options.MasterVolume = 35; service.UpdateGains(options, .5, .2);
            await Until(() => service.GetAudioSamples().Count == 7);
            Assert.Equal(1, graph.Created); Assert.Single(graph.Outputs);
            await service.StopAsync(); Assert.Empty(service.GetAudioSamples());
            service.UpdateGains(options, 1); await Task.Delay(75, TestContext.Current.CancellationToken);
            Assert.False(service.IsActive); Assert.Equal(1, graph.Created); Assert.Single(graph.Outputs);
        }
        finally { service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task StopMutesAndClearsImmediatelyButWaitsForOwnedOutputTeardown()
    {
        using var fixture = new Fixture(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var graph = new Graph(entered, release); var service = fixture.Service(graph);
        try
        {
            await service.StartAsync(fixture.Options(1), 1); await Until(() => service.IsPlaying);
            var stopped = Task.Run(service.StopAsync, TestContext.Current.CancellationToken);
            await Until(() => entered.IsSet); Assert.True(graph.Outputs.First().Muted); Assert.Empty(service.GetAudioSamples());
            Assert.False(stopped.IsCompleted); release.Set(); await stopped.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.All(graph.Outputs, output => Assert.True(output.Disposed)); Assert.False(service.IsActive);
        }
        finally { release.Set(); service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task RapidStructuralReplacementIsLastWinsAndNeverOverlapsRealOutputGraphs()
    {
        using var fixture = new Fixture(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var graph = new Graph(entered, release); var service = fixture.Service(graph);
        try
        {
            await service.StartAsync(fixture.Options(1, "first"), 1); await Until(() => service.IsPlaying);
            var superseded = service.ApplyAsync(fixture.Options(1, "superseded"), 1);
            await Until(() => entered.IsSet);
            var latest = fixture.Options(1, "latest"); var replacing = service.ApplyAsync(latest, 1);
            Assert.Equal(1, graph.Created); release.Set();
            await Task.WhenAll(superseded, replacing).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Until(() => service.IsPlaying && service.GetAudioSamples().Any(sample => sample.Path == latest.Tracks[0].Path));
            Assert.Equal(2, graph.Created); Assert.Equal(2, graph.Outputs.Count); Assert.Equal(1, graph.MaximumOutputs);
            Assert.All(service.GetAudioSamples(), sample => Assert.Equal(latest.Tracks[0].Path, sample.Path));
        }
        finally { release.Set(); service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task FailedTrackKeepsOtherTracksPlayingAndDisabledMasterStaysIdle()
    {
        using var fixture = new Fixture(); var graph = new Graph(); var service = fixture.Service(graph);
        try
        {
            var options = fixture.Options(2); options.Enabled = false;
            await service.StartAsync(options, 1); Assert.Equal(0, graph.Created);
            options.Enabled = true; options.Tracks[0].Path = fixture.File("bad.wav");
            await service.ApplyAsync(options, 1);
            await Until(() => service.IsPlaying && service.StatusKey == "AudioLayersPartialFailure");
            Assert.Equal(options.Tracks[1].Path, Assert.Single(service.GetAudioSamples()).Path); Assert.Single(graph.Outputs);
            options.Enabled = false; await service.ApplyAsync(options, 1); Assert.False(service.IsActive); Assert.Empty(service.GetAudioSamples());
        }
        finally { service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutputOpenOrPollFailureRemainsUnavailableAfterInternalCleanup(bool opening)
    {
        using var fixture = new Fixture(); var graph = new Graph { FailOpen = opening, FailPoll = !opening }; var service = fixture.Service(graph);
        try
        {
            var options = fixture.Options(1); await service.StartAsync(options, 1);
            await Until(() => !service.IsActive && service.StatusKey == "AudioLayersUnavailable");
            Assert.False(service.IsPlaying); Assert.Empty(service.GetAudioSamples()); Assert.Equal(1, graph.Created);
            Assert.All(graph.Outputs, output => Assert.True(output.Disposed));
            service.UpdateGains(options, .5); await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.Equal("AudioLayersUnavailable", service.StatusKey); Assert.Equal(1, graph.Created);
        }
        finally { service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task AStoppedStatusObserverCannotStartAnotherGraphDuringDispose()
    {
        using var fixture = new Fixture(); var graph = new Graph(); var service = fixture.Service(graph);
        try
        {
            var options = fixture.Options(1); await service.StartAsync(options, 1); await Until(() => service.IsPlaying);
            var retries = 0; var restarted = Task.CompletedTask;
            service.StateChanged += () => { if (service.StatusKey is null) { Interlocked.Increment(ref retries); restarted = service.StartAsync(options, 1); } };
            service.Dispose(); await restarted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, retries); Assert.False(service.IsActive); Assert.False(service.IsPlaying);
            Assert.Empty(service.GetAudioSamples()); Assert.Equal(1, graph.Created); Assert.Single(graph.Outputs);
        }
        finally { service.Dispose(); await service.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    private static async Task Until(Func<bool> ready)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(10, stop.Token);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "classic-layered-service-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(root);
        public string File(string name) { var path = Path.Combine(root, name); System.IO.File.WriteAllBytes(path, "RIFF0000WAVE"u8.ToArray()); return path; }
        public LayeredAudioOptions Options(int tracks, string prefix = "track") => new() { Enabled = true,
            Tracks = Enumerable.Range(0, tracks).Select(i => new AudioLayerTrack { Path = File(prefix + i + ".wav") }).ToList() };
        public LayeredAudioService Service(Graph graph) => new(graph.Create,
            () => new LayeredPcmCache(Path.Combine(root, "cache"), resources: () => true));
        public void Dispose() => Directory.Delete(root, true);
    }
    private sealed class Graph(ManualResetEventSlim? entered = null, ManualResetEventSlim? release = null)
    {
        private readonly ManualResetEventSlim? teardownEntered = entered, teardownRelease = release;
        private int created, outputCount, maximumOutputs;
        public int Created => Volatile.Read(ref created);
        public int MaximumOutputs => Volatile.Read(ref maximumOutputs);
        public readonly ConcurrentQueue<Decoder> Decoders = new();
        public readonly ConcurrentQueue<Output> Outputs = new();
        public bool FailOpen, FailPoll;
        public ILayeredAudioBackend Create() => new Backend(this, Interlocked.Increment(ref created) == 1);
        private sealed class Backend(Graph graph, bool first) : ILayeredAudioBackend
        {
            public ILayeredAudioDecoder OpenDecoder(LayeredPcmTrack track, CancellationToken token, Action changed)
            {
                if (!track.Path.EndsWith("bad.wav", StringComparison.Ordinal)) Assert.True(track.Append(Enumerable.Repeat((short)8192, 4096).ToArray()));
                track.Finish(!track.Path.EndsWith("bad.wav", StringComparison.Ordinal));
                var decoder = new Decoder(); graph.Decoders.Enqueue(decoder); return decoder;
            }
            public ILayeredAudioOutput OpenOutput(LayeredMixStream stream, TimeProvider clock)
            {
                if (graph.FailOpen) throw new IOException("Test output open failure");
                var output = new Output(stream, first ? graph.teardownEntered : null, first ? graph.teardownRelease : null,
                    () => Interlocked.Decrement(ref graph.outputCount), graph.FailPoll);
                var active = Interlocked.Increment(ref graph.outputCount); var previous = Volatile.Read(ref graph.maximumOutputs);
                while (active > previous)
                { var actual = Interlocked.CompareExchange(ref graph.maximumOutputs, active, previous); if (actual == previous) break; previous = actual; }
                graph.Outputs.Enqueue(output); return output;
            }
            public void Dispose() { }
        }
    }
    private sealed class Decoder : ILayeredAudioDecoder
    {
        public bool Finished => true;
        public void Poll(bool paused) { }
        public void Dispose() { }
    }
    private sealed class Output(LayeredMixStream stream, ManualResetEventSlim? entered, ManualResetEventSlim? release, Action disposed, bool failPoll) : ILayeredAudioOutput
    {
        private int muted, ended;
        public bool Muted => Volatile.Read(ref muted) != 0;
        public bool Disposed => Volatile.Read(ref ended) != 0;
        public bool IsPlaying { get; private set; } = true;
        public double PositionSeconds => stream.ProducedFrames / 44100d;
        public void Poll(bool paused)
        {
            if (failPoll) throw new IOException("Test output runtime failure");
            IsPlaying = !paused && !Muted && !Disposed;
            if (IsPlaying) { var buffer = new byte[2048]; IsPlaying = stream.Read(buffer, 0, buffer.Length) > 0; }
        }
        public void Mute() { Interlocked.Exchange(ref muted, 1); IsPlaying = false; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0) return;
            entered?.Set(); if (release is not null) Assert.True(release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            disposed(); IsPlaying = false;
        }
    }
}
