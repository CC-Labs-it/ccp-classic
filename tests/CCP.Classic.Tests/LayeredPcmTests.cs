using System.Runtime.InteropServices;
using CCP.Classic.App.Media;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LayeredPcmTests
{
    [Fact]
    public void FileBackedLoopsKeepUnequalStereoBoundariesAndClampOnlyTheFinalMix()
    {
        using var fixture = new Fixture(); using var cache = new LayeredPcmCache(fixture.Root, resources: () => true);
        var a = cache.Create(Guid.NewGuid(), "a.wav", () => { }); var b = cache.Create(Guid.NewGuid(), "b.wav", () => { });
        short[] first = [12000, -12000, 24000, -24000, 6000, -6000]; short[] second = [16000, 8000, -8000, -16000];
        Assert.True(a.Append(first)); Assert.True(b.Append(second)); a.Finish(true); b.Finish(true);
        using var stream = new LayeredMixStream([a, b], TestContext.Current.CancellationToken, () => { });
        stream.SetGain(a.Id, 1); stream.SetGain(b.Id, .5); stream.SetMaster(.4);
        var bytes = new byte[2048]; Assert.Equal(2048, stream.Read(bytes, 0, bytes.Length));
        var samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(44));
        for (var i = 0; i < samples.Length; i++) Assert.Equal((float)((first[i % first.Length] + second[i % second.Length] * .5) / 32768 * .4), samples[i]);
        var voices = stream.AudioSamples(2d / 44100, TimeProvider.System); Assert.Equal(2, voices.Count);
        Assert.Equal(2d / 44100, voices[0].SourceSeconds, 8); Assert.Equal(0, voices[1].SourceSeconds);
        Assert.Equal(.4, voices[0].Gain); Assert.Equal(.2, voices[1].Gain);
    }
    [Fact]
    public async Task ALongBufferStallDoesNotCreateCatchupReadAheadAndCancellationReleasesTheRead()
    {
        using var fixture = new Fixture(); using var cache = new LayeredPcmCache(fixture.Root, resources: () => true);
        var track = cache.Create(Guid.NewGuid(), "a.wav", () => { });
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new LayeredMixStream([track], stopped.Token, () => { }); stream.SetGain(track.Id, 1); stream.SetMaster(1);
        var bytes = new byte[2048]; var waiting = Task.Run(() => stream.Read(bytes, 0, bytes.Length), TestContext.Current.CancellationToken);
        for (var i = 0; i < 100 && !stream.Waiting; i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(stream.Waiting); await Task.Delay(350, TestContext.Current.CancellationToken);
        Assert.True(track.Append(Enumerable.Repeat((short)16384, 1024).ToArray())); track.Finish(true);
        Assert.Equal(2048, await waiting.WaitAsync(TestContext.Current.CancellationToken));
        stream.SetGain(track.Id, .2); var resumed = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 40; i++) Assert.Equal(bytes.Length, stream.Read(bytes, 0, bytes.Length));
        Assert.InRange(resumed.Elapsed.TotalMilliseconds, 80, 3000);
        Assert.Equal(.1f, MemoryMarshal.Cast<byte, float>(bytes.AsSpan())[^1]);
        Assert.InRange(stream.MaximumAheadSeconds, 0, .121);
        stopped.Cancel(); Assert.Equal(0, stream.Read(bytes, 0, bytes.Length)); Assert.Empty(stream.AudioSamples(0, TimeProvider.System));
    }
    [Fact]
    public void AggregateLimitsAndLateCallbacksFailClosedAndCleanupOnlyOwnedFiles()
    {
        using var fixture = new Fixture(); var foreign = Path.Combine(fixture.Root, "keep.txt"); File.WriteAllText(foreign, "not a cache");
        using (var cache = new LayeredPcmCache(fixture.Root, 16, () => true))
        {
            var a = cache.Create(Guid.NewGuid(), "a.wav", () => { }); var b = cache.Create(Guid.NewGuid(), "b.wav", () => { });
            Assert.True(a.Append([1, 2, 3, 4])); Assert.True(b.Append([1, 2, 3, 4])); Assert.False(a.Append([1, 2])); Assert.True(a.Failed);
            b.Dispose(); Assert.False(b.Append([1, 2]));
            using var other = new LayeredPcmCache(fixture.Root, resources: () => true);
            Assert.Equal(2, Directory.GetDirectories(fixture.Root).Length);
        }
        Assert.Empty(Directory.GetDirectories(fixture.Root)); Assert.Equal("not a cache", File.ReadAllText(foreign));
    }
    [Fact]
    public void TinyCompletedLoopsReadFromDiskOnlyOnce()
    {
        using var fixture = new Fixture(); using var cache = new LayeredPcmCache(fixture.Root, resources: () => true);
        var track = cache.Create(Guid.NewGuid(), "tiny.wav", () => { }); Assert.True(track.Append([12000, -12000])); track.Finish(true);
        var samples = new short[512];
        for (var i = 0; i < 100; i++) Assert.Equal(512, track.Read(i * 512L, samples));
        Assert.Equal(1, track.DiskReads); Assert.All(samples.Where((_, index) => index % 2 == 0), sample => Assert.Equal(12000, sample));
        var growing = cache.Create(Guid.NewGuid(), "growing.wav", () => { }); Assert.True(growing.Append([1, 2]));
        Assert.Equal(2, growing.Read(0, samples)); Assert.True(growing.Append([3, 4])); growing.Finish(true);
        Assert.Equal(512, growing.Read(0, samples)); Assert.Equal(new short[] { 1, 2, 3, 4, 1, 2, 3, 4 }, samples[..8]);
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "classic-layers-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
