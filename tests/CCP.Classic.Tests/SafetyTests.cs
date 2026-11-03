using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class SafetyTests
{
    [Fact]
    public void DisposalCannotResurrectButARecoveredBackendCanRestartAfterAbort()
    {
        var session = new SessionController(new());
        session.Start(); session.Abort(); Assert.False(session.IsRunning);
        session.Start(); Assert.True(session.IsRunning);
        session.Dispose(); session.Start(); Assert.False(session.IsRunning);
        Assert.False(session.Stop());
    }

    [Fact]
    public void PanicStopsImmediatelyAndDoesNotRepeat()
    {
        var settings = new ClassicSettings();
        using var session = new SessionController(settings);
        var stops = 0;
        session.StopRequested += () => stops++;
        session.Start();
        Assert.True(session.KeyDown("Escape"));
        Assert.False(session.IsRunning);
        session.Start();
        Assert.False(session.KeyDown("Escape"));
        Assert.True(session.IsRunning);
        session.KeyUp("Escape");
        Assert.True(session.KeyDown("Escape"));
        Assert.Equal(2, stops);
    }

    [Fact]
    public void EveryManualStopUsesTheAcknowledgedPanicGate()
    {
        using var session = new SessionController(new());
        session.SetPanicEnabled(false);
        Assert.True(session.PanicEnabled);
        session.SetPanicEnabled(false, warningAcknowledged: true);
        session.Start();
        Assert.False(session.KeyDown("Escape"));
        Assert.False(session.Stop());
        Assert.True(session.IsRunning);
        session.SetPanicEnabled(true);
        Assert.True(session.Stop());
        using var nextLaunch = new SessionController(new());
        Assert.True(nextLaunch.PanicEnabled);
    }

    [Fact]
    public void RunLengthUsesElapsedTimeAndDisposalAlwaysReleasesResources()
    {
        var clock = new TestClock();
        var session = new SessionController(new() { RunMinutes = 15 }, clock);
        session.Start();
        clock.Advance(TimeSpan.FromMinutes(15));
        session.Tick();
        Assert.False(session.IsRunning);
        session.SetPanicEnabled(false, true);
        session.Start();
        session.Dispose();
        Assert.False(session.IsRunning);
    }

    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan value) => ticks += value.Ticks;
    }
}
