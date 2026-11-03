using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class ImagePoolReplacementTests
{
    [Fact]
    public void ReplacingASelectedPoolDoesNotRetainOldFlashIndicesOrAudioReservations()
    {
        var settings = new ClassicSettings(); settings.Flash.AudioEnabled = true;
        var timeline = new EffectsTimeline(settings, 4, 0);
        timeline.FlashAudioRequested += _ => true;
        timeline.Start(); Assert.True(timeline.TriggerFlash(3));
        Assert.NotEmpty(timeline.Step(0).Flashes);
        timeline.ReplaceImages(0);
        Assert.Empty(timeline.Step(.1).Flashes); Assert.False(timeline.TriggerFlash());
        timeline.ReplaceImages(1); Assert.True(timeline.TriggerFlash());
        Assert.All(timeline.Step(.2).Flashes, flash => Assert.Equal(0, flash.Image));
    }

    [Fact]
    public void ReplacingImagesRemovesOldBubblePayloadsAndWashWithoutStoppingText()
    {
        var settings = new ClassicSettings(); settings.Effects["bubble"].Enabled = true;
        settings.Effects["bubble"].Values["rate"] = 30;
        settings.Effects["bubble"].Values["effects"] = 1;
        settings.Effects["bubble"].Values["chance"] = 100;
        settings.Effects["bubble"].Values["kind"] = (int)BubbleEffectKind.Flash;
        var timeline = new InteractiveEffectsTimeline(settings); timeline.SetImageCount(4); timeline.Start();
        Assert.Single(timeline.Step(2).Bubbles);
        timeline.ReplaceImages(0);
        var frame = timeline.Step(2.1);
        Assert.Empty(frame.Bubbles); Assert.Empty(frame.ImageVisuals); Assert.False(frame.CascadeActive);
        Assert.True(timeline.IsRunning);
    }
}
