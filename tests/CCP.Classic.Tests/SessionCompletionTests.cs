using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class SessionCompletionTests
{
    [Fact]
    public void AnExplicitStopCompletesAnUntimedThreeHourRunAndAwardsLongSession()
    {
        using var run = new Run(0); run.Clock.Advance(TimeSpan.FromHours(3)); run.Progress.Tick(false, false);
        Assert.True(run.Session.IsRunning); Assert.DoesNotContain("deep_sleep", run.Progress.Data.UnlockedAchievements);
        Assert.Equal(0, run.Progress.Data.LongestSessionSeconds);
        Assert.True(run.Session.Stop(finishUntilStopped: true));
        Assert.True(run.Session.CompletedNormally); Assert.False(run.Session.EndedBySchedule);
        Assert.False(run.Progress.IsRunning); Assert.Equal(10800, run.Progress.Data.LongestSessionSeconds);
        Assert.Contains("deep_sleep", run.Progress.Data.UnlockedAchievements);
        Assert.Equal(10800, Assert.Single(run.Completed).Seconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmergencyOrChildStopAtThreeHoursRemainsAnInterruption(bool panicKey)
    {
        using var run = new Run(0); run.Clock.Advance(TimeSpan.FromHours(3));
        Assert.True(panicKey ? run.Session.KeyDown("Escape") : run.Session.Stop());
        Assert.False(run.Session.CompletedNormally); Assert.False(run.Session.EndedBySchedule);
        Assert.False(run.Progress.IsRunning); Assert.Empty(run.Completed);
        Assert.Equal(10800, run.Progress.Data.SessionSeconds); Assert.Equal(0, run.Progress.Data.LongestSessionSeconds);
        Assert.DoesNotContain("deep_sleep", run.Progress.Data.UnlockedAchievements);
    }

    [Fact]
    public void ACompletedFifteenMinuteScheduleStillReportsItsActualScheduledEnd()
    {
        using var run = new Run(15); run.Clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        run.Session.Tick(); Assert.True(run.Session.IsRunning);
        run.Clock.Advance(TimeSpan.FromSeconds(1)); run.Session.Tick();
        Assert.False(run.Session.IsRunning); Assert.True(run.Session.CompletedNormally); Assert.True(run.Session.EndedBySchedule);
        Assert.Equal(900, Assert.Single(run.Completed).Seconds); Assert.Equal(900, run.Progress.Data.LongestSessionSeconds);
        Assert.DoesNotContain("deep_sleep", run.Progress.Data.UnlockedAchievements);
    }

    [Fact]
    public void ExplicitStopBeforeAThirtyMinuteDeadlineDoesNotCompleteTheTimedRun()
    {
        using var run = new Run(30); run.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(run.Session.Stop(finishUntilStopped: true));
        Assert.False(run.Session.IsRunning); Assert.False(run.Session.CompletedNormally); Assert.False(run.Session.EndedBySchedule);
        Assert.Empty(run.Completed); Assert.Equal(0, run.Progress.Data.LongestSessionSeconds);
    }

    [Fact]
    public void PanicDisabledBlocksBothManualStopOverloadsAndTheKey()
    {
        using var run = new Run(0); run.Session.SetPanicEnabled(false, warningAcknowledged: true);
        run.Clock.Advance(TimeSpan.FromHours(3));
        Assert.False(run.Session.Stop()); Assert.False(run.Session.Stop(finishUntilStopped: true));
        Assert.False(run.Session.KeyDown("Escape")); Assert.True(run.Session.IsRunning); Assert.True(run.Progress.IsRunning);
        Assert.False(run.Session.CompletedNormally); Assert.Empty(run.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerDisposalAlwaysInterruptsRatherThanCompletingTheRun(bool panicDisabled)
    {
        using var run = new Run(0);
        if (panicDisabled) run.Session.SetPanicEnabled(false, warningAcknowledged: true);
        run.Clock.Advance(TimeSpan.FromHours(3)); run.Session.Dispose();
        Assert.False(run.Session.IsRunning); Assert.False(run.Progress.IsRunning);
        Assert.False(run.Session.CompletedNormally); Assert.False(run.Session.EndedBySchedule);
        Assert.Empty(run.Completed); Assert.DoesNotContain("deep_sleep", run.Progress.Data.UnlockedAchievements);
    }

    [Fact]
    public void TheThreeHourBoundaryRequiresACompletedFullThreeHours()
    {
        using var run = new Run(0); run.Clock.Advance(TimeSpan.FromHours(3) - TimeSpan.FromTicks(1));
        run.Session.Stop(finishUntilStopped: true);
        Assert.True(run.Session.CompletedNormally); Assert.Single(run.Completed);
        Assert.DoesNotContain("deep_sleep", run.Progress.Data.UnlockedAchievements);
    }

    [Fact]
    public void RestartClearsCompletionFlagsAndRepeatedStopsDoNotDuplicateCredit()
    {
        using var run = new Run(0); run.Clock.Advance(TimeSpan.FromHours(3));
        run.Session.Stop(finishUntilStopped: true); run.Session.Stop(finishUntilStopped: true); Assert.Single(run.Completed);
        run.Session.Start(); Assert.False(run.Session.CompletedNormally); Assert.False(run.Session.EndedBySchedule);
        run.Clock.Advance(TimeSpan.FromSeconds(5)); run.Session.Stop(); Assert.Single(run.Completed);
        Assert.Equal(10805, run.Progress.Data.SessionSeconds); Assert.Equal(10800, run.Progress.Data.LongestSessionSeconds);
    }

    private sealed class Run : IDisposable
    {
        public readonly ManualClock Clock = new();
        public readonly SessionController Session;
        public readonly ProgressTracker Progress;
        public readonly List<CompletedSession> Completed = [];
        public Run(int minutes)
        {
            var settings = new ClassicSettings { RunMinutes = minutes, Preset = "Daily" };
            Session = new(settings, Clock); Progress = new(clock: Clock);
            Session.Changed += () => { if (Session.IsRunning) Progress.StartSession(settings.Preset); };
            Session.StopRequested += () => Progress.EndSession(Session.CompletedNormally, !Session.PanicEnabled,
                settings.Effects["video"].Enabled && settings.Effects["video"].Values["strict"] != 0);
            Progress.SessionCompleted += Completed.Add; Session.Start();
        }
        public void Dispose() => Session.Dispose();
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset utc = new(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => utc;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) { timestamp += elapsed.Ticks; utc += elapsed; }
    }
}
