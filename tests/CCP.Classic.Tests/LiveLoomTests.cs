using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LiveLoomTests
{
    [Fact]
    public async Task SavedDefinitionRendersAndReducedMotionFreezesItsPose()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        LiveLoom? live = null;
        try
        {
            var store = new LoomStore(folder); var saved = store.Save("Test", new() { Format = "wide" });
            var settings = new ClassicSettings { CustomSpiralId = saved.Id, GentleVisuals = true };
            await UiDispatcher.Run(() => live = new(settings, store));
            await WaitForFrame();
            await UiDispatcher.Run(() =>
            {
                Assert.Equal(512, live!.Image!.PixelSize.Width); Assert.Equal(288, live.Image.PixelSize.Height);
                settings.ReducedMotion = true; live.Update(.3);
            });
            var phase = live!.Phase;
            await UiDispatcher.Run(() => { live.Update(30); Assert.Equal(phase, live.Phase); });
            await UiDispatcher.Run(() => { settings.CustomSpiralId = null; live.Update(31); Assert.Null(live.Image); });
            await Task.Delay(40, TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { live.Update(32); Assert.Null(live.Image); });

            async Task WaitForFrame()
            {
                for (var attempt = 0; attempt < 250; attempt++)
                {
                    var ready = false;
                    await UiDispatcher.Run(() => { live!.Update(attempt * .001); ready = live.Image is not null; });
                    if (ready) return;
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
                Assert.Fail("Saved spiral did not render");
            }
        }
        finally
        {
            if (live is not null) await UiDispatcher.Run(live.Dispose);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void PresetsNormalizeSpiralIdentifiersAndBuiltInsClearCustomSelection()
    {
        var id = Guid.NewGuid().ToString("N"); var settings = new ClassicSettings { CustomSpiralId = id };
        var preset = PresetStore.Capture("Custom", settings);
        PresetStore.Apply(preset, settings); Assert.Equal(id, settings.CustomSpiralId);
        PresetStore.Apply(PresetStore.BuiltIns[0], settings); Assert.Null(settings.CustomSpiralId);
        settings.CustomSpiralId = "../../file"; settings.Normalize(); Assert.Null(settings.CustomSpiralId);
        settings.CustomSpiralId = id; settings.ResetToDefaults(); Assert.Null(settings.CustomSpiralId);
    }
}
