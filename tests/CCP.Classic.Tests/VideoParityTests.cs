using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Flash;
using CCP.Classic.Core.Video;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class VideoParityTests
{
    [Fact]
    public void OriginalMinimumFilterCutAndRandomStartKeepLongClipsEligible()
    {
        var options = new VideoOptions { MinDurationSeconds = 30, MaxDurationSeconds = 45 };
        Assert.False(options.Eligible(29)); Assert.True(options.Eligible(30)); Assert.True(options.Eligible(0));
        Assert.True(options.Eligible(600)); Assert.Equal(45, options.PlaybackCap(480)); Assert.Equal(30, options.PlaybackCap(30));
        Assert.Equal(0, VideoOptions.StartOffset(45, 30000, .8)); Assert.Equal(0, VideoOptions.StartOffset(0, 600000, .8));
        Assert.Equal(277500, VideoOptions.StartOffset(45, 600000, .5));
        Assert.Equal(555000, VideoOptions.StartOffset(45, 600000, 1));
    }

    [Fact]
    public void OneTogetherTriggerFillsOnlyTheFreeSlotsAndKeepsAttentionFullscreenOnly()
    {
        var settings = new ClassicSettings(); var effect = settings.Effects["video"];
        effect.Enabled = true; effect.Values["launch"] = 1; effect.Values["floating"] = 1; effect.Values["maximum"] = 4;
        settings.Video.FloatingTogether = true; settings.Video.MaxDurationSeconds = 12;
        var timeline = new VideoTimeline(settings); timeline.Start(); var first = timeline.Step(0);
        Assert.Equal(4, first.Launches.Count); Assert.All(first.Launches, cue => { Assert.False(cue.Attention); Assert.Equal(12, cue.CapSeconds); });
        Assert.Empty(timeline.Step(1).Launches); Assert.False(timeline.TryTriggerBubble());
        effect.Values["strict"] = 1; var retired = timeline.Step(2); Assert.Empty(retired.Active);
    }

    [Fact]
    public void SourceTargetsUseCountNotRateSharedHitAndLifespanWithNeutralFallback()
    {
        var options = new VideoOptions { TargetCount = 3, TargetLifespan = 2, TargetPhrases = [] };
        var targets = new VideoAttentionTimeline(1); var answers = new List<bool>(); targets.Answered += answers.Add;
        targets.Start(10, options); Assert.True(targets.Passed); targets.Step(2.99, options, 1000, 800, false, false);
        Assert.Empty(targets.Targets); targets.Step(4, options, 1000, 800, false, false);
        var first = Assert.Single(targets.Targets); Assert.Equal("Focus", first.Text);
        targets.Step(4, options, 1000, 800, false, false); Assert.Single(targets.Targets);
        Assert.True(targets.Hit(first.Id)); Assert.False(targets.Hit(first.Id)); Assert.Empty(targets.Targets);
        Assert.Single(targets.VisualTargets); targets.Step(4.31, options, 1000, 800, false, false); Assert.Empty(targets.VisualTargets);
        targets.Step(7, options, 1000, 800, false, false); Assert.Single(targets.Targets);
        targets.Step(9, options, 1000, 800, false, false); Assert.False(targets.Passed);
        Assert.Equal(new[] { true, false }, answers); Assert.Equal(1, targets.Hits);
        targets.Clear(); Assert.Empty(targets.Targets); Assert.False(targets.IsStarted); Assert.False(targets.Hit(first.Id));
    }

    [Fact]
    public void ReducedMotionKeepsTargetsStillButLifespanAndSoftHitFadeContinue()
    {
        var options = new VideoOptions { TargetCount = 1 }; var clock = new VideoAttentionTimeline(1);
        clock.Start(10, options); clock.Step(4, options, 1000, 800, false, true);
        var before = Assert.Single(clock.Targets); clock.Step(4.1, options, 1000, 800, false, true);
        Assert.Equal(before.X, Assert.Single(clock.Targets).X); Assert.Equal(before.Y, clock.Targets[0].Y);
        clock.Hit(before.Id); clock.Step(4.5, options, 1000, 800, false, true); Assert.Empty(clock.VisualTargets);
    }

    [Fact]
    public void SharedMonitorViewsDecodeOncePauseOnlyWhenAllHiddenAndDisposeOnce()
    {
        var source = new FakePlayback(); var shared = new SharedVideoPlayback(source);
        using var first = shared.CreateView(); var second = shared.CreateView(); first.Start(); second.Start();
        Assert.Equal(1, source.Starts); first.SetPaused(false); second.SetPaused(true); Assert.False(source.Paused);
        source.Frame = new([1, 2, 3, 255], 1, 1, 1);
        Assert.True(first.TryCopyFrame(out var a)); Assert.True(second.TryCopyFrame(out var b)); Assert.Same(a, b);
        Assert.False(second.TryCopyFrame(out _)); first.SetPaused(true); Assert.True(source.Paused);
        first.SetGain(.5); second.SetGain(0); Assert.Equal(.5, source.Gain);
        second.Dispose(); Assert.Equal(0, source.Disposals); first.Dispose(); Assert.Equal(1, source.Disposals);
        Assert.False(first.TryCopyFrame(out _));
    }

    [Fact]
    public void OriginalThrowPathKeepsEightyMillisecondSamplesSpeedCapBounceAndMotionOff()
    {
        var motion = new FloatingVideoMotion(100, 100, 300, 170, 0, 0, 1200, 800) { Speed = .35 };
        Assert.False(motion.Active); motion.Press(150, 150, 0);
        motion.Move(250, 150, 20); motion.Move(350, 150, 40); motion.Step(.04);
        Assert.Equal(FlashDragOutcome.Fling, motion.Release(40, MotionLevel.Full));
        Assert.InRange(Math.Abs(motion.Vx), 100, 3600 * .35);
        var before = motion.X; motion.Step(.1); Assert.True(motion.X > before);
        motion.Speed = .1; Assert.InRange(Math.Abs(motion.Vx), 0, 360);
        motion.Press(400, 150, 100); motion.Move(600, 150, 140); motion.Step(.04);
        Assert.Equal(FlashDragOutcome.Place, motion.Release(140, MotionLevel.Off)); Assert.False(motion.Active);
        Assert.InRange(motion.X, 0, 900); Assert.InRange(motion.Y, 0, 630);
    }

    [Fact]
    public void VideoCustomizationSnapshotsNormalizeAndDoNotShareMutablePhrasePools()
    {
        var settings = new ClassicSettings(); settings.Video.TargetPhrases = new() { ["Own phrase"] = true };
        settings.Video.TargetCount = 200; settings.Video.MercyAfterFails = 0;
        var snapshot = FeatureCustomization.Capture(settings);
        Assert.Equal(10, snapshot.Video.TargetCount); Assert.Equal(2, snapshot.Video.MercyAfterFails);
        settings.Video.TargetPhrases.Clear(); Assert.True(snapshot.Video.TargetPhrases["Own phrase"]);
        snapshot.Apply(settings); settings.Video.TargetPhrases.Clear(); Assert.Single(snapshot.Video.TargetPhrases);
    }

    [Fact]
    public void LockCardBlocksAndConsumesVideoLaunchesWithoutAnUnblockBurst()
    {
        var settings = new ClassicSettings(); settings.Effects["video"].Enabled = true;
        settings.Effects["video"].Values["launch"] = 1; settings.Effects["video"].Values["rate"] = 20;
        var clock = new VideoTimeline(settings); clock.Start();
        Assert.True(clock.TryTriggerBubble()); Assert.Empty(clock.Step(0, true).Launches);
        Assert.Empty(clock.Step(1).Launches); Assert.Empty(clock.Step(180, true).Launches);
        Assert.Empty(clock.Step(181).Launches); Assert.NotEmpty(clock.Step(360).Launches);
    }

    [Theory]
    [InlineData(false, 4)] [InlineData(true, 1)]
    public void TestVideoReplacesTheOldestReservationAndNeverExceedsPresentationCapacity(bool strict, int maximum)
    {
        var settings = new ClassicSettings(); settings.Effects["video"].Enabled = true;
        settings.Effects["video"].Values["strict"] = strict ? 1 : 0;
        settings.Effects["video"].Values["maximum"] = 4; settings.Video.FloatingTogether = true;
        var timeline = new VideoTimeline(settings); timeline.Start(); Assert.True(timeline.TryPreview());
        var first = timeline.Step(0); Assert.Equal(maximum, first.Active.Count); var oldest = first.Active[0].Id;
        Assert.True(timeline.TryPreview()); Assert.False(timeline.TryPreview()); var replaced = timeline.Step(1);
        Assert.Equal(maximum, replaced.Active.Count); Assert.Single(replaced.Launches);
        Assert.DoesNotContain(replaced.Active, cue => cue.Id == oldest); Assert.False(timeline.Complete(oldest));
        Assert.All(replaced.Active, cue => Assert.Equal(strict, cue.Strict));
    }

    [Fact]
    public void ManualTestDoesNotEnableTheVideoScheduleOrLeaveAQueuedLaunchAfterStop()
    {
        var settings = new ClassicSettings(); settings.Effects["video"].Enabled = false;
        var timeline = new VideoTimeline(settings); timeline.Start(); Assert.True(timeline.TryPreview());
        var cue = Assert.Single(timeline.Step(0).Launches); Assert.Single(timeline.Step(1).Active);
        timeline.Complete(cue.Id); Assert.Empty(timeline.Step(10000).Launches); Assert.False(settings.Effects["video"].Enabled);
        timeline.TryPreview(); timeline.Stop(); timeline.Start(); Assert.Empty(timeline.Step(0).Active);
    }

    private sealed class FakePlayback : ILocalVideoPlayback
    {
        public bool IsFinished => false;
        public string? LastError => null;
        public int Starts, Disposals;
        public bool Paused;
        public double Gain;
        public VideoFrame? Frame;
        public void Start() => Starts++;
        public void SetGain(double value) => Gain = value;
        public void SetPaused(bool value) => Paused = value;
        public bool TryCopyFrame(out VideoFrame? frame) { frame = Frame; Frame = null; return frame is not null; }
        public void Dispose() => Disposals++;
    }
}
