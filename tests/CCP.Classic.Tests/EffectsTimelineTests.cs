using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class EffectsTimelineTests
{
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
}
