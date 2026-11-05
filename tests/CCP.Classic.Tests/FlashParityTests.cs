using CCP.Classic.Core;
using CCP.Classic.Core.Flash;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class FlashParityTests
{
    [Fact]
    public void RepeatedHydraShattersShareTheBoundedTransientBudget()
    {
        var settings = new ClassicSettings(); settings.Flash.Shatter = true;
        settings.Effects["flash"].Values["hydra"] = 1;
        var timeline = new EffectsTimeline(settings, 1, 0); timeline.Start(); timeline.Step(2);
        for (var i = 0; i < 100; i++)
        {
            var frame = timeline.Step(3);
            var active = frame.Flashes.First(f => f.Exit is null && f.Shatter is null);
            Assert.True(timeline.Dismiss(active.Id));
        }
        var final = timeline.Step(3);
        Assert.InRange(final.Flashes.Count(f => f.Exit is not null || f.Shatter is not null), 1, 32);
        Assert.InRange(final.Flashes.Count, 1, 35);
    }
    [Fact]
    public void OneRecordedVoiceHoldsBatchAndLinkedHydraUntilItsActualCompletion()
    {
        var settings = new ClassicSettings(); settings.Flash.Count = 2;
        settings.Effects["flash"].Values["maximum"] = 8; settings.Effects["flash"].Values["hydra"] = 1;
        var timeline = new EffectsTimeline(settings, 2, 0); var requests = 0; long batch = 0;
        timeline.FlashAudioRequested += id => { batch = id; requests++; return true; };
        timeline.Start(); Assert.Equal(2, timeline.Step(2).Flashes.Count); Assert.Equal(1, requests);
        var original = timeline.Step(20).Flashes[0]; Assert.True(timeline.Dismiss(original.Id));
        Assert.Equal(1, requests); Assert.Equal(3, timeline.Step(21).Flashes.Count);
        timeline.CompleteFlashAudio(batch);
        Assert.Empty(timeline.Step(22).Flashes);
    }
    [Fact]
    public void SourceSizePreservesPortraitAspectAndCountRespectsCapacity()
    {
        var settings = new ClassicSettings(); settings.Flash.Count = 6;
        settings.Effects["flash"].Values["maximum"] = 4;
        var timeline = new EffectsTimeline(settings, 3, 0) { ImageAspectRatio = _ => .5 };
        timeline.SetViewport(1600, 900); timeline.Start();
        var frame = timeline.Step(2); Assert.Equal(4, frame.Flashes.Count);
        foreach (var flash in frame.Flashes)
        {
            Assert.Equal(.5, flash.Width * 1600 / (flash.Height * 900), 6);
            Assert.Equal(360, flash.Height * 900, 6);
        }
    }

    [Fact]
    public void StayUntilPoppedRemainsBoundedAndClickShattersOnlyOnce()
    {
        var settings = new ClassicSettings(); settings.Flash.StayUntilPopped = true; settings.Flash.Shatter = true;
        settings.Effects["flash"].Values["rate"] = 0;
        var timeline = new EffectsTimeline(settings, 1, 0); timeline.Start(); Assert.True(timeline.TriggerFlash());
        var held = Assert.Single(timeline.Step(300).Flashes); Assert.True(held.Opacity > 0);
        Assert.True(timeline.Dismiss(held.Id)); Assert.False(timeline.Dismiss(held.Id));
        var shards = Assert.Single(timeline.Step(300.1).Flashes).Shatter!;
        Assert.InRange(shards.Shards.Length, 6, 9);
        for (var i = 2; i <= 9; i++) timeline.Step(300 + i * .1);
        Assert.Empty(timeline.Step(301).Flashes);
        timeline.Stop(); Assert.False(timeline.TriggerFlash());
    }

    [Fact]
    public void DragMovesImageWithoutDismissingOrMultiplyingAndTapStillDismisses()
    {
        var settings = new ClassicSettings(); settings.Flash.Draggable = true;
        settings.Effects["flash"].Values["hydra"] = 1;
        var timeline = new EffectsTimeline(settings, 1, 0); timeline.Start(); timeline.Step(2);
        var before = Assert.Single(timeline.Step(2.4).Flashes);
        Assert.True(timeline.BeginDrag(before.Id, before.X + .1, before.Y + .1));
        timeline.Step(2.5); timeline.MoveDrag(before.X + .2, before.Y + .2);
        var dragged = Assert.Single(timeline.Step(2.6).Flashes); timeline.EndDrag();
        Assert.NotEqual(before.X, dragged.X); Assert.Null(dragged.Exit);
        var after = Assert.Single(timeline.Step(2.7).Flashes);
        Assert.True(timeline.BeginDrag(after.Id, after.X + .1, after.Y + .1)); timeline.EndDrag();
        Assert.NotNull(timeline.Step(2.71).Flashes.First(f => f.Id == after.Id).Exit);
    }

    [Fact]
    public void PendulumKeepsItsFrozenPoseWhenBrokenAndMotionOffDoesNotShatter()
    {
        var settings = new ClassicSettings(); settings.Flash.Motion = FlashMotionStyle.Pendulum; settings.Flash.Shatter = true;
        var timeline = new EffectsTimeline(settings, 1, 0); timeline.Start(); timeline.Step(2);
        var frame = Assert.Single(timeline.Step(2.4).Flashes); Assert.NotEqual(0, frame.Rotation);
        Assert.True(timeline.Dismiss(frame.Id));
        Assert.Equal(frame.Rotation, Assert.Single(timeline.Step(2.41).Flashes).Shatter!.FrozenAngleRad, 8);
        settings.ReducedMotion = true;
        var soft = Assert.Single(timeline.Step(2.42).Flashes); Assert.Null(soft.Shatter);
        Assert.Null(soft.Exit);
    }

    [Fact]
    public void CenterExclusionUsesSourceAdaptiveBandsAndShortMonitorEdge()
    {
        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(FlashPlacement.TryPickAvoidCenterPointAdaptive(1920, 1080, 768, 432, 25, random,
                out var x, out var y, out var effective));
            var side = effective / 100d * 1080; var left = (1920 - side) / 2; var top = (1080 - side) / 2;
            Assert.True(x + 768 <= left + 1 || x >= left + side - 1 || y + 432 <= top + 1 || y >= top + side - 1);
            Assert.InRange(effective, 5, 25);
        }
    }
}
