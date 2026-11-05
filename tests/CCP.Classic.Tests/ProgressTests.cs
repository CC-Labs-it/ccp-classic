using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class ProgressTests
{
    [Theory]
    [InlineData(1, 800)]
    [InlineData(80, 2500)]
    [InlineData(100, 4000)]
    [InlineData(125, 6000)]
    [InlineData(150, 10000)]
    [InlineData(151, 10300)]
    public void XpCostsMatchTheOriginalCurveAtEverySegment(int level, double expected) =>
        Assert.Equal(expected, ProgressTracker.GetXpForLevel(level));

    [Fact]
    public void LifetimeXpDerivesLevelsAtExactBoundariesAndRejectsInvalidAwards()
    {
        var tracker = new ProgressTracker();
        tracker.AddXp(799); Assert.Equal(1, tracker.Level); Assert.Equal(799, tracker.XpIntoLevel);
        tracker.AddXp(1); Assert.Equal(2, tracker.Level); Assert.Equal(0, tracker.XpIntoLevel);
        var thirdCost = ProgressTracker.GetXpForLevel(2);
        tracker.AddXp(thirdCost + 25); Assert.Equal(3, tracker.Level); Assert.Equal(25, tracker.XpIntoLevel);
        tracker.AddXp(-5); tracker.AddXp(double.NaN); tracker.AddXp(double.PositiveInfinity);
        Assert.Equal(800 + thirdCost + 25, tracker.Data.LifetimeXp);
        Assert.Equal(1, ProgressTracker.DeriveLevel(double.NaN).Level);
        Assert.InRange(ProgressTracker.DeriveLevel(double.MaxValue).Level, 1, ProgressTracker.MaximumLevel);
    }

    [Fact]
    public void OnlyRunningTimeAndRealEventsAccumulateWithoutDuplicateEndCredit()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        tracker.FlashShown(); tracker.BubblePopped(); tracker.VideoFinished(30);
        Assert.Equal(0, tracker.Data.FlashesShown);
        tracker.StartSession("Daily"); tracker.Tick(true, true);
        clock.Advance(TimeSpan.FromSeconds(20)); tracker.Tick(true, true);
        tracker.FlashShown(); tracker.BubblePopped(); tracker.VideoFinished(30);
        tracker.VideoFinished(double.NaN); tracker.VideoFinished(-1);
        Assert.Equal(20, tracker.Data.SessionSeconds); Assert.Equal(20, tracker.Data.PinkSeconds);
        Assert.Equal(20, tracker.Data.SpiralSeconds); Assert.Equal(20, tracker.ContinuousSpiralSeconds);
        clock.Advance(TimeSpan.FromSeconds(10)); tracker.Tick(false, false);
        Assert.Equal(0, tracker.ContinuousSpiralSeconds);
        Assert.Equal(20, tracker.Data.PinkSeconds); Assert.Equal(30, tracker.Data.SessionSeconds);
        tracker.EndSession(); tracker.EndSession();
        clock.Advance(TimeSpan.FromSeconds(10)); tracker.Tick(true, true);
        Assert.Equal(30, tracker.Data.SessionSeconds); Assert.Equal(1, tracker.Data.VideosFinished);
        Assert.Equal(30, tracker.Data.VideoSeconds); Assert.Equal(1, tracker.Data.BubblesPopped);
        Assert.Equal(1, tracker.Data.FlashesShown);
    }

    [Fact]
    public void NaturalCompletionCapturesTheStartedPresetFocusAndLocalTime()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        var completed = new List<CompletedSession>(); tracker.SessionCompleted += completed.Add;
        tracker.StartSession("Gentle"); tracker.StartSession("Deep");
        tracker.SwitchedToOtherApplication(); clock.Advance(TimeSpan.FromMinutes(30));
        tracker.EndSession(naturalCompletion: true, panicDisabled: true, strictLock: true);
        var result = Assert.Single(completed);
        Assert.Equal("Gentle", result.Preset); Assert.Equal(1800, result.Seconds);
        Assert.True(result.SwitchedAway); Assert.True(result.PanicDisabled); Assert.True(result.StrictLock);
        Assert.Equal(clock.GetLocalNow(), result.FinishedAt);
        Assert.Equal(1800, tracker.Data.LongestSessionSeconds);
        tracker.StartSession("Daily"); clock.Advance(TimeSpan.FromSeconds(10)); tracker.EndSession();
        Assert.Single(completed); Assert.Equal(1810, tracker.Data.SessionSeconds);
    }

    [Fact]
    public void LaunchStreakUsesLocalCalendarDaysAndBanksADayOnlyOnce()
    {
        var clock = new ManualClock(); var tracker = new ProgressTracker(clock: clock);
        Assert.Equal(1, tracker.Data.ConsecutiveDays);
        tracker.RecordLaunch(); Assert.Equal(1, tracker.Data.ConsecutiveDays);
        clock.Advance(TimeSpan.FromDays(1)); tracker.RecordLaunch(); Assert.Equal(2, tracker.Data.ConsecutiveDays);
        clock.Advance(TimeSpan.FromDays(2)); tracker.RecordLaunch(); Assert.Equal(1, tracker.Data.ConsecutiveDays);
        var loaded = new ProgressTracker(tracker.Data, clock); Assert.Equal(1, loaded.Data.ConsecutiveDays);
        Assert.Equal(DateOnly.FromDateTime(clock.GetLocalNow().DateTime), loaded.Data.LastLaunchDay);
    }

    [Fact]
    public void ProgressRoundTripsAtomicallyAndRecoversMalformedOversizedOrFutureFiles()
    {
        using var fixture = new ProgressFixture();
        var tracker = new ProgressTracker(clock: new ManualClock()); tracker.AddXp(1200);
        tracker.StartSession("Gentle"); tracker.FlashShown(); tracker.BubblePopped(); tracker.VideoFinished(15);
        fixture.Store.Save(tracker.Data);
        var loaded = fixture.Store.Load(out var recovered); Assert.False(recovered);
        Assert.Equal(1200, loaded.LifetimeXp); Assert.Equal(1, loaded.FlashesShown);
        Assert.Equal(1, loaded.BubblesPopped); Assert.Equal(1, loaded.VideosFinished);
        Assert.Single(Directory.GetFiles(fixture.Root));
        foreach (var damaged in new[] { "{", "null", "{\"SchemaVersion\":99}", new string(' ', 256 * 1024 + 1) })
        {
            File.WriteAllText(fixture.Path, damaged);
            Assert.Equal(0, fixture.Store.Load(out recovered).LifetimeXp); Assert.True(recovered);
        }
    }

    [Fact]
    public void InvalidPersistedCountersAreNormalizedAndContinuousStateDoesNotPersist()
    {
        using var fixture = new ProgressFixture();
        var data = new ProgressData { LifetimeXp = double.NaN, SessionSeconds = -20, BubblesPopped = -1,
            BubbleCountCorrectStreak = -5, UnlockedAchievements = ["valid_id", "invalid/name"] };
        fixture.Store.Save(data); var loaded = fixture.Store.Load(out var recovered);
        Assert.False(recovered); Assert.Equal(0, loaded.LifetimeXp); Assert.Equal(0, loaded.SessionSeconds);
        Assert.Equal(0, loaded.BubblesPopped); Assert.Equal(["valid_id"], loaded.UnlockedAchievements);
        var tracker = new ProgressTracker(loaded);
        Assert.False(tracker.IsRunning); Assert.Equal(0, tracker.ContinuousSpiralSeconds);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset utc = new(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => utc;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) { utc += elapsed; timestamp += elapsed.Ticks; }
    }

    private sealed class ProgressFixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "progress.json");
        public ProgressStore Store { get; }
        public ProgressFixture() { Directory.CreateDirectory(Root); Store = new(Path); }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
