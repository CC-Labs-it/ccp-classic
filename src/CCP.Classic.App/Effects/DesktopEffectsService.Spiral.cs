using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Classic.App.Media;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private SpiralMediaLibrary? spiralMedia;
    private readonly CornerGifWindow?[] cornerGifs = new CornerGifWindow?[2];
    private readonly double[] cornerAlpha = new double[2];
    private double lastSpiralTime, nextCornerShow;
    private string? spiralStatus;
    public string? SpiralStatus => spiralStatus;
    private void InitializeSpiralMedia() { }
    private void StartSpiralMedia()
    {
        StopSpiralMedia(); lastSpiralTime = nextCornerShow = 0;
        if (images.Count > 0) { spiralMedia = new(settings, images[0]); spiralMedia.Start(); }
    }
    public void ReloadSpiralMedia()
    { if (running && !disposed) { spiralMedia?.Start(); Tick(); } }
    private void TickSpiralMedia(double elapsed)
    {
        spiralMedia?.Update(elapsed);
        var dt = Math.Clamp(elapsed - lastSpiralTime, 0, .25); lastSpiralTime = elapsed;
        spiralStatus = null;
        var primary = owner.Screens.Primary ?? owner.Screens.ScreenFromWindow(owner);
        for (var index = 0; index < 2; index++)
        {
            var slot = settings.Spiral.Corners[index];
            var desired = slot.Enabled && primary is not null;
            var target = desired ? slot.Opacity / 100d * (settings.GentleVisuals ? .5 : 1) : 0;
            var fade = settings.GentleVisuals ? .75 : .3;
            cornerAlpha[index] += Math.Clamp(target - cornerAlpha[index], -dt / fade, dt / fade);
            if (!desired && cornerAlpha[index] <= .001)
            { cornerGifs[index]?.Finish(); cornerGifs[index] = null; continue; }
            if (primary is null || spiralMedia?.CornerImages[index] is not { } image) continue;
            if (cornerGifs[index] is null)
            {
                if (elapsed < nextCornerShow) continue;
                cornerGifs[index] = new(configureInput); nextCornerShow = elapsed + .4;
            }
            var stacked = index == 1 && settings.Spiral.Corners[0].Enabled && settings.Spiral.Corners[0].Position == slot.Position ? 1 : 0;
            if (!cornerGifs[index]!.Update(primary, slot, image, stacked, cornerAlpha[index]))
            { cornerGifs[index]!.Finish(); cornerGifs[index] = null; spiralStatus = "SpiralUnavailable"; nextCornerShow = elapsed + 2; }
        }
    }
    private IReadOnlyList<Screen> SpiralScreens()
    {
        var screens = owner.Screens.All;
        return settings.Spiral.TargetMonitor switch
        {
            -2 => screens,
            >= 0 when settings.Spiral.TargetMonitor < screens.Count => [screens[settings.Spiral.TargetMonitor]],
            _ => settings.FillAllMonitors ? screens : owner.Screens.ScreenFromWindow(owner) is { } current ? [current] : []
        };
    }
    private EffectFrame ApplySpiralMedia(EffectFrame frame, Screen? screen)
    {
        var matches = screen is null || SpiralScreens().Any(target => target.Bounds == screen.Bounds);
        return frame with { SpiralOpacity = matches ? frame.SpiralOpacity : 0,
            CustomSpiral = settings.CustomSpiralId is not null || settings.Spiral.UseProcedural ? frame.CustomSpiral : spiralMedia?.MainImage ?? frame.CustomSpiral };
    }
    private void StopSpiralMedia()
    {
        for (var index = 0; index < 2; index++) { cornerGifs[index]?.Finish(); cornerGifs[index] = null; cornerAlpha[index] = 0; }
        spiralMedia?.Dispose(); spiralMedia = null; spiralStatus = null;
    }
    private void DisposeSpiralMedia() => StopSpiralMedia();
}
