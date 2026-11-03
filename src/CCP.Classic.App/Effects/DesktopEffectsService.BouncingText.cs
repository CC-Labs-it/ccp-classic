using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private BouncingDesktopLayout? bouncingDesktop;
    private double bouncingDesktopScale;

    private void ResetBouncingDesktop()
    {
        bouncingDesktop = null; bouncingDesktopScale = 0; interactive.SetBouncingDesktop(null);
    }

    private void ConfigureBouncingDesktop()
    {
        if (useFallback)
        {
            bouncingDesktop = null;
            var size = fallback?.ClientSize;
            interactive.SetBouncingDesktop(new(0, 0, Math.Max(1, size?.Width ?? 640), Math.Max(1, size?.Height ?? 480)));
            return;
        }
        var screens = BouncingScreens();
        if (screens.Count == 0) { bouncingDesktop = null; interactive.SetBouncingDesktop(null); return; }
        if (bouncingDesktopScale == 0)
            bouncingDesktopScale = Math.Clamp(owner.Screens.Primary?.Scaling ?? screens[0].Scaling, .25, 8);
        bouncingDesktop = new(screens.Select(screen => new BouncingMonitor(screen.Bounds.X, screen.Bounds.Y,
            screen.Bounds.Width, screen.Bounds.Height)), bouncingDesktopScale);
        interactive.SetBouncingDesktop(bouncingDesktop.Bounds);
    }

    private IReadOnlyList<Screen> BouncingScreens()
    {
        if (settings.FillAllMonitors) return owner.Screens.All;
        var selected = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        return selected is null ? [] : [selected];
    }

    private EffectFrame ProjectBouncingText(EffectFrame frame, Window host)
    {
        if (ReferenceEquals(host, fallback)) return BouncingTextHostProjection.Apply(frame, new(0, 0, 1));
        if (bouncingDesktop is null) return BouncingTextHostProjection.Apply(frame, null);
        var screen = owner.Screens.ScreenFromWindow(host);
        var selected = screen is not null && bouncingDesktop.Monitors.Any(m => m.X == screen.Bounds.X && m.Y == screen.Bounds.Y
            && m.Width == screen.Bounds.Width && m.Height == screen.Bounds.Height);
        return BouncingTextHostProjection.Apply(frame, selected
            ? bouncingDesktop.Project(host.Position.X, host.Position.Y, host.RenderScaling) : null);
    }
}

internal static class BouncingTextHostProjection
{
    public static EffectFrame Apply(EffectFrame frame, BouncingTextProjection? projection)
    {
        var cues = projection is { } value ? frame.Interactive.BouncingTexts.Select(cue => cue with { Projection = value }).ToArray() : [];
        return frame with { Interactive = frame.Interactive with { BouncingTexts = Array.AsReadOnly(cues), BouncingText = cues.FirstOrDefault() } };
    }
}
