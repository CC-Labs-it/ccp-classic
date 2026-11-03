using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class VideoTimelineTests
{
    private static ClassicSettings Settings(bool launch = false, double rate = 20)
    {
        var settings = new ClassicSettings();
        settings.Effects["video"].Enabled = true;
        settings.Effects["video"].Values["launch"] = launch ? 1 : 0;
        settings.Effects["video"].Values["rate"] = rate;
        return settings;
    }

    [Fact]
    public void PeriodicPlaybackWaitsOneIntervalAndNeverCatchesUp()
    {
        var timeline = new VideoTimeline(Settings());
        Assert.Empty(timeline.Step(1000).Active);
        timeline.Start();
        Assert.Empty(timeline.Step(0).Launches);
        Assert.Empty(timeline.Step(179.9).Launches);
        var first = Assert.Single(timeline.Step(180).Launches);
        Assert.True(timeline.Complete(first.Id));
        var next = timeline.Step(3600);
        Assert.Single(next.Launches);
        Assert.Single(next.Active);
        Assert.Empty(timeline.Step(3600).Launches);
        Assert.Empty(timeline.Step(3779).Launches);
        Assert.Single(timeline.Step(3780).Launches);
    }

    [Fact]
    public void StartOnLaunchRunsOnceEvenAtZeroRate()
    {
        var timeline = new VideoTimeline(Settings(launch: true, rate: 0));
        timeline.Start();
        var cue = Assert.Single(timeline.Step(0).Launches);
        Assert.True(timeline.Complete(cue.Id));
        Assert.Empty(timeline.Step(0).Launches);
        Assert.Empty(timeline.Step(100000).Launches);
    }

    [Fact]
    public void ZeroRateWithoutStartOnLaunchNeverSchedules()
    {
        var timeline = new VideoTimeline(Settings(rate: 0));
        timeline.Start();
        Assert.Empty(timeline.Step(100000).Launches);
    }

    [Fact]
    public void BusyTriggerIsConsumedRatherThanQueuedUntilCapacityReturns()
    {
        var settings = Settings(launch: true);
        settings.Effects["video"].Values["maximum"] = 1;
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        var first = Assert.Single(timeline.Step(0).Launches);
        Assert.Empty(timeline.Step(180).Launches);
        Assert.Single(timeline.Active);
        Assert.True(timeline.Complete(first.Id));
        Assert.Empty(timeline.Step(181).Launches);
        Assert.Single(timeline.Step(360).Launches);
    }

    [Theory]
    [InlineData(false, true, false, false, 4)]
    [InlineData(true, true, true, true, 1)]
    [InlineData(false, false, false, true, 1)]
    public void EffectivePresentationConstrainsCapacityAndAttention(bool strict, bool floating,
        bool expectedStrict, bool expectedAttention, int expectedMaximum)
    {
        var settings = Settings(launch: true);
        settings.Effects["video"].Values["strict"] = strict ? 1 : 0;
        settings.Effects["video"].Values["floating"] = floating ? 1 : 0;
        settings.Effects["video"].Values["maximum"] = 4;
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        for (var index = 0; index < 6; index++) timeline.Step(index * 180);
        Assert.Equal(expectedMaximum, timeline.Active.Count);
        Assert.All(timeline.Active, cue =>
        {
            Assert.Equal(expectedStrict, cue.Strict);
            Assert.Equal(!strict && floating, cue.Floating);
            Assert.Equal(expectedAttention, cue.Attention);
        });
    }

    [Fact]
    public void CapturePlayerOptionsAndLeavePlaybackCapToThePlayer()
    {
        var settings = Settings(launch: true, rate: 0);
        settings.Effects["video"].Values["floating"] = 0;
        settings.Effects["video"].Values["cap"] = 1;
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        var first = Assert.Single(timeline.Step(0).Launches);
        settings.Effects["video"].Values["cap"] = 30;
        settings.Effects["video"].Values["attention"] = 0;
        var active = Assert.Single(timeline.Step(10000).Active);
        Assert.Equal(first.Id, active.Id);
        Assert.Equal(60, active.CapSeconds);
        Assert.Equal(10000, active.Elapsed);
        Assert.True(active.Attention);
        Assert.True(timeline.Complete(active.Id));
        Assert.False(timeline.Complete(active.Id));
    }

    [Fact]
    public void PresentationChangesRetireIncompatiblePlayersWithoutCreatingAnExtraTrigger()
    {
        var settings = Settings(launch: true);
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        var first = Assert.Single(timeline.Step(0).Active);
        settings.Effects["video"].Values["strict"] = 1;
        Assert.Empty(timeline.Step(1).Active);
        Assert.False(timeline.Complete(first.Id));
        var fullscreen = Assert.Single(timeline.Step(180).Active);
        Assert.False(fullscreen.Floating);
        Assert.True(fullscreen.Strict);
    }

    [Fact]
    public void DisablingRetiresEverythingAndReenablingWaitsOneInterval()
    {
        var settings = Settings(launch: true);
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        var cue = Assert.Single(timeline.Step(0).Active);
        settings.Effects["video"].Enabled = false;
        Assert.Empty(timeline.Step(10).Active);
        Assert.False(timeline.Complete(cue.Id));
        settings.Effects["video"].Enabled = true;
        Assert.Empty(timeline.Step(2000).Launches);
        Assert.Empty(timeline.Step(2179).Launches);
        Assert.Single(timeline.Step(2180).Launches);
    }

    [Fact]
    public void RateChangesStartANewIntervalRatherThanLaunchingImmediately()
    {
        var settings = Settings(rate: 1);
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        Assert.Empty(timeline.Step(3599).Launches);
        settings.Effects["video"].Values["rate"] = 20;
        Assert.Empty(timeline.Step(3600).Launches);
        Assert.Empty(timeline.Step(3779).Launches);
        Assert.Single(timeline.Step(3780).Launches);
        settings.Effects["video"].Values["rate"] = 0;
        Assert.Empty(timeline.Step(4000).Launches);
        Assert.Empty(timeline.Step(10000).Launches);
    }

    [Fact]
    public void InvalidOrBackwardTimesDoNotAdvanceTheScheduleOrMakeCueAgeNegative()
    {
        var timeline = new VideoTimeline(Settings(launch: true));
        timeline.Start();
        Assert.Equal(0, Assert.Single(timeline.Step(-100).Active).Elapsed);
        timeline.Step(20);
        foreach (var invalid in new[] { -1, 5, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            var frame = timeline.Step(invalid);
            Assert.Empty(frame.Launches);
            Assert.Equal(20, Assert.Single(frame.Active).Elapsed);
        }
        Assert.Single(timeline.Step(180).Launches);
    }

    [Fact]
    public void LoweringMaximumRetainsTheOldestPlayersWithinTheNewLimit()
    {
        var settings = Settings(launch: true);
        settings.Effects["video"].Values["maximum"] = 4;
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        var oldest = Assert.Single(timeline.Step(0).Active);
        for (var index = 1; index < 4; index++) timeline.Step(index * 180);
        Assert.Equal(4, timeline.Active.Count);
        settings.Effects["video"].Values["maximum"] = 1;
        var remaining = Assert.Single(timeline.Step(541).Active);
        Assert.Equal(oldest.Id, remaining.Id);
    }

    [Fact]
    public void RestartClearsReservationsAndOldCompletionCannotCancelTheNewRun()
    {
        var timeline = new VideoTimeline(Settings(launch: true));
        timeline.Start();
        var old = Assert.Single(timeline.Step(0).Launches);
        timeline.Stop();
        Assert.False(timeline.IsRunning);
        Assert.Empty(timeline.Active);
        Assert.Empty(timeline.Step(10000).Active);
        timeline.Start();
        Assert.True(timeline.IsRunning);
        var fresh = Assert.Single(timeline.Step(0).Launches);
        Assert.NotEqual(old.Id, fresh.Id);
        Assert.False(timeline.Complete(old.Id));
        Assert.Single(timeline.Active);
    }

    [Fact]
    public void ALongRunKeepsOnlyItsBoundedReservations()
    {
        var settings = Settings(launch: true);
        settings.Effects["video"].Values["maximum"] = 4;
        var timeline = new VideoTimeline(settings);
        timeline.Start();
        for (var index = 0; index < 1000; index++)
        {
            var frame = timeline.Step(index * 180);
            Assert.InRange(frame.Active.Count, 1, 4);
            Assert.InRange(frame.Launches.Count, 0, 1);
            if (index % 3 == 0) timeline.Complete(frame.Active[0].Id);
        }
    }
}
