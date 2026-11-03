using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalFunScriptTests
{
    private const string Script = "{\"actions\":[{\"at\":0,\"pos\":0},{\"at\":200,\"pos\":100},{\"at\":1200,\"pos\":0}]}";
    [Fact]
    public void OriginalPositionInterpolationAndSpeedMappingPreserveInversionAndIgnoreInvalidActions()
    {
        Assert.True(LocalFunScript.TryParse(Script, out var script)); Assert.NotNull(script);
        Assert.Equal(.5, script.PositionAt(100)); Assert.Equal(1, script.IntensityAt(100));
        Assert.Equal(90d / 490, script.IntensityAt(500), 8); Assert.Equal(0, script.IntensityAt(1200));
        Assert.Equal(0, script.IntensityAt(-1)); Assert.Equal(0, script.PositionAt(2000));
        Assert.True(LocalFunScript.TryParse("{\"inverted\":true,\"actions\":[{\"at\":30000000000,\"pos\":1},{\"at\":0,\"pos\":25},{\"at\":100,\"pos\":100}]}", out var inverse));
        Assert.Equal(.75, inverse!.PositionAt(0)); Assert.Equal(2, inverse.Actions.Count);
        Assert.False(LocalFunScript.TryParse("{\"actions\":[{\"at\":-1,\"pos\":0}]}", out _));
        Assert.False(LocalFunScript.TryParse("{broken", out _)); Assert.Empty(LocalFunScript.CandidatePaths("https://example.invalid/video.mp4"));
    }

    [Fact]
    public async Task LocalDiscoveryUsesVideoTimeLookaheadAndZeroesOnPauseDisableOrStaleReports()
    {
        var clock = new Clock(); var path = Path.Combine(Path.GetTempPath(), "classic-script.mp4"); var read = new List<string>();
        using var playback = new LocalFunScriptPlayback((candidate, _) => { lock (read) read.Add(candidate); return Task.FromResult<string?>(candidate.Contains("funscripts") ? Script : null); });
        var source = new AudioPlaybackSample(path, .1, 0, true, clock.GetTimestamp());
        ScriptOutput value = default;
        await Until(() => (value = playback.Sample([source], clock, true, true)).Position is not null);
        Assert.Equal(1, value.Intensity); Assert.Equal(.8, value.Position!.Value, 8);
        Assert.Equal(LocalFunScript.CandidatePaths(path), read);
        Assert.Equal(0, playback.Sample([source], clock, true, false).Intensity);
        clock.Seconds = 1; source = source with { ObservedAt = clock.GetTimestamp() };
        Assert.Equal(default, playback.Sample([source], clock, true, true));
        Assert.Equal(default, playback.Sample([source with { IsPlaying = false }], clock, true, true));
        Assert.Equal(default, playback.Sample([source], clock, false, true));
    }

    [Fact]
    public async Task IgnoredCancellationCannotInstallALateScriptAfterStop()
    {
        var load = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var playback = new LocalFunScriptPlayback((_, _) => load.Task); var clock = new Clock();
        var sample = new AudioPlaybackSample(Path.Combine(Path.GetTempPath(), "late.mp4"), .1, 1, true, clock.GetTimestamp());
        playback.Sample([sample], clock, true, true); playback.Clear(); load.SetResult(Script); await Task.Yield();
        Assert.Equal(default, playback.Sample([], clock, true, true));
    }
    private sealed class Clock : TimeProvider
    {
        public double Seconds;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => (long)(Seconds * TimeSpan.TicksPerSecond);
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); stop.CancelAfter(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, stop.Token);
    }
}
