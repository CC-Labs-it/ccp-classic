using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class EffectsTimelineTests
{
    [Fact]
    public void UpdatingTheImageCatalogPreservesTextAndRemovesInvalidFlashReferences()
    {
        var settings = new ClassicSettings { GentleVisuals = true };
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(1);
        var before = timeline.Step(1.8);
        timeline.SetImageCount(3);
        var after = timeline.Step(1.9);
        Assert.Equal(before.Phrase, after.Phrase);
        Assert.False(after.Whisper);
        Assert.True(after.TextOpacity > 0);
        Assert.NotEmpty(timeline.Step(2).Flashes);
        timeline.SetImageCount(0);
        Assert.Empty(timeline.Step(2.1).Flashes);
        Assert.Equal(after.Phrase, timeline.Step(2.2).Phrase);
    }

    [Fact]
    public void StarterEffectsAppearWithinThirtySecondsAndStopLeavesNothing()
    {
        var timeline = new EffectsTimeline(new(), 1, 5);
        timeline.Start();
        var frames = Enumerable.Range(0, 300).Select(i => timeline.Step(i / 30d)).ToArray();
        Assert.Contains(frames, f => f.Flashes.Any(x => x.Opacity > 0));
        Assert.Contains(frames, f => f.TextOpacity > 0);
        Assert.Contains(frames, f => f.PinkOpacity > 0 && f.SpiralOpacity > 0);
        timeline.Stop();
        var empty = timeline.Step(10);
        Assert.Empty(empty.Flashes);
        Assert.Equal(0, empty.TextOpacity + empty.PinkOpacity + empty.SpiralOpacity);
    }

    [Fact]
    public void GentleOnlySlowsFadesAndMotionOffPreservesTheCurrentPhase()
    {
        var settings = new ClassicSettings { GentleVisuals = true };
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start();
        var first = timeline.Step(.033);
        Assert.InRange(first.PinkOpacity, 0, .033 / .75);
        var phase = first.SpiralPhase;
        settings.ReducedMotion = true;
        var bright = timeline.Step(.1);
        Assert.Equal(phase, bright.SpiralPhase);
        settings.Effects["pink"].Enabled = false;
        Assert.InRange(timeline.Step(.133).PinkOpacity, 0, bright.PinkOpacity);
    }

    [Fact]
    public void LowOpacityStillTakesTheWholeGentleFadeDuration()
    {
        var timeline = new EffectsTimeline(new() { GentleVisuals = true }, 1, 5);
        timeline.Start(); timeline.Step(0);
        Assert.Equal(.03, timeline.Step(.375).PinkOpacity, 6);
        Assert.Equal(.06, timeline.Step(.75).PinkOpacity, 6);
    }

    [Fact]
    public void ZeroRatesDisableSpawnsAndWhispersRequireBothSwitches()
    {
        var settings = new ClassicSettings();
        settings.Effects["flash"].Values["rate"] = 0;
        settings.Effects["sub"].Values["rate"] = 0;
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start();
        var silent = timeline.Step(30);
        Assert.Empty(silent.Flashes);
        Assert.Equal(0, silent.TextOpacity);
        Assert.False(silent.Whisper);
        settings.Effects["sub"].Values["rate"] = 6;
        settings.Effects["sub"].Values["whispers"] = 1;
        Assert.True(timeline.Step(31).Whisper);
        Assert.False(timeline.Step(31.1).Whisper);
        settings.Effects["audio"].Enabled = false;
        Assert.False(timeline.Step(42).Whisper);
    }

    [Fact]
    public void ClickDismissFadesOutAndHydraRespectsTheMaximum()
    {
        var settings = new ClassicSettings();
        settings.Effects["flash"].Values["hydra"] = 1;
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start();
        var flash = Assert.Single(timeline.Step(2).Flashes);
        Assert.True(timeline.Dismiss(flash.Id));
        Assert.False(timeline.Dismiss(flash.Id));
        Assert.InRange(timeline.Step(2.1).Flashes.Count, 1, 3);
        settings.Effects["flash"].Values["dismiss"] = 0;
        Assert.False(timeline.Dismiss(timeline.Step(2.2).Flashes.Last().Id));
    }

    [Fact]
    public void EnablingGentleDoesNotResurrectAnExpiredSubliminal()
    {
        var settings = new ClassicSettings();
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(1);
        Assert.Equal(0, timeline.Step(1.4).TextOpacity);
        settings.GentleVisuals = true;
        var after = timeline.Step(1.5);
        Assert.Equal(0, after.TextOpacity);
        Assert.False(after.Whisper);
        Assert.Equal(0, timeline.Step(2.5).TextOpacity);
        timeline.Step(11);
        Assert.InRange(timeline.Step(11.375).TextOpacity, .19, .21);
        Assert.Equal(.4, timeline.Step(11.75).TextOpacity, 6);
    }

    [Fact]
    public void EnablingGentleDuringAFlashExitStartsAContinuousFullLengthExit()
    {
        var settings = new ClassicSettings();
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(2);
        var before = Assert.Single(timeline.Step(6.5).Flashes).Opacity;
        settings.GentleVisuals = true;
        Assert.Equal(before, Assert.Single(timeline.Step(6.51).Flashes).Opacity, 6);
        Assert.Equal(before / 2, Assert.Single(timeline.Step(6.885).Flashes).Opacity, 6);
        Assert.True(Assert.Single(timeline.Step(7.25).Flashes).Opacity > 0);
        Assert.Empty(timeline.Step(7.27).Flashes);
    }

    [Fact]
    public void EnablingGentleDuringAFlashEntranceContinuesFromItsVisibleOpacity()
    {
        var settings = new ClassicSettings();
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(2);
        var before = Assert.Single(timeline.Step(2.1).Flashes).Opacity;
        settings.GentleVisuals = true;
        Assert.Equal(before, Assert.Single(timeline.Step(2.11).Flashes).Opacity, 6);
        Assert.InRange(Assert.Single(timeline.Step(2.485).Flashes).Opacity, before, .849);
        Assert.True(Assert.Single(timeline.Step(2.85).Flashes).Opacity < .85);
        Assert.Equal(.85, Assert.Single(timeline.Step(2.87).Flashes).Opacity, 6);
    }

    [Fact]
    public void GentleLiveOpacityChangesEaseWithoutRestartingTheCueLifetime()
    {
        var settings = new ClassicSettings { GentleVisuals = true };
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(2);
        var held = Assert.Single(timeline.Step(3).Flashes);
        settings.Effects["flash"].Values["opacity"] = 0;
        Assert.Equal(held.Opacity, Assert.Single(timeline.Step(3.01).Flashes).Opacity, 6);
        Assert.Equal(held.Opacity / 2, Assert.Single(timeline.Step(3.385).Flashes).Opacity, 6);
        Assert.Equal(0, Assert.Single(timeline.Step(3.77).Flashes).Opacity, 6);
        settings.Effects["flash"].Values["opacity"] = 100;
        Assert.Equal(0, Assert.Single(timeline.Step(3.78).Flashes).Opacity, 6);
        Assert.InRange(Assert.Single(timeline.Step(3.79).Flashes).Opacity, 0, .001);
        Assert.Equal(.5, Assert.Single(timeline.Step(4.155).Flashes).Opacity, 6);
        Assert.Equal(1, Assert.Single(timeline.Step(4.54).Flashes).Opacity, 6);
        Assert.Equal(held.Id, Assert.Single(timeline.Step(7.49).Flashes).Id);
        Assert.Empty(timeline.Step(7.51).Flashes);
    }

    [Fact]
    public void ExistingCuesKeepTheirCapturedHoldAndFadeWhenKnobsChange()
    {
        var settings = new ClassicSettings();
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(1);
        Assert.True(timeline.Step(1.1).TextOpacity > 0);
        settings.Effects["sub"].Values["frames"] = 12;
        Assert.Equal(0, timeline.Step(1.3).TextOpacity);
        timeline.Step(2); timeline.Step(2.4);
        settings.Effects["flash"].Values["fade"] = 1500;
        Assert.InRange(Assert.Single(timeline.Step(6.5).Flashes).Opacity, .21, .23);
        Assert.Empty(timeline.Step(6.7).Flashes);
    }

    [Fact]
    public void GentleCuesKeepTheirFullFadeEvenAfterGentleIsTurnedOff()
    {
        var settings = new ClassicSettings { GentleVisuals = true };
        var timeline = new EffectsTimeline(settings, 1, 5);
        timeline.Start(); timeline.Step(0); timeline.Step(2);
        Assert.Equal(.425, Assert.Single(timeline.Step(2.375).Flashes).Opacity, 6);
        Assert.Equal(.85, Assert.Single(timeline.Step(2.75).Flashes).Opacity, 6);
        settings.GentleVisuals = false;
        Assert.Equal(.425, Assert.Single(timeline.Step(7.125).Flashes).Opacity, 6);
        Assert.Empty(timeline.Step(7.51).Flashes);
    }
}
