using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.App.Wallpaper;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private WallpaperSession? wallpaper;

    private void InitializeWallpaper()
    {
        if (!OperatingSystem.IsWindows() || wallpaper is not null) return;
        var current = new WallpaperSession(images.FirstOrDefault(), starter.Images); wallpaper = current;
        current.StatusChanged += key => Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && ReferenceEquals(wallpaper, current) && current.StatusKey == key && key is not null) Report(key);
        });
        _ = current.RecoverAsync();
    }

    private void TickWallpaper()
    {
        if (wallpaper is null) return;
        IReadOnlyList<Screen> selected = settings.FillAllMonitors ? owner.Screens.All
            : owner.Screens.ScreenFromWindow(owner) is { } current ? [current] : [];
        if (selected.Count == 0 && owner.Screens.Primary is { } primary) selected = [primary];
        var monitors = selected.Select(screen => new WallpaperMonitor(screen.Bounds, screen.Scaling)).Distinct()
            .OrderBy(monitor => monitor.Bounds.X).ThenBy(monitor => monitor.Bounds.Y).ToArray();
        var effect = settings.Effects["wall"];
        wallpaper.Update(running && effect.Enabled, effect.Values["mode"] != 0, monitors, settings.Library,
            settings.GentleVisuals, settings.ReducedMotion, settings.PerformanceMode, settings.Wallpaper);
    }

    public Task StopWallpaperAsync(bool immediate = true) => wallpaper?.StopAsync(immediate) ?? Task.CompletedTask;
    public void ShuffleWallpaper() => wallpaper?.Shuffle();
    private void DisposeWallpaper() => wallpaper?.Dispose();
}
