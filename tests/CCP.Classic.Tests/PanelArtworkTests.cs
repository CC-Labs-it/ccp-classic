using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class PanelArtworkTests
{
    [Fact]
    public Task OnePanelClockStopsForEveryMotionGateAndDetachment() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings(); var host = new Border { Width = 100, Height = 100 };
        var clock = new PanelArtworkClock(host, settings);
        host.Child = clock.Add(new("Presets/hero_default.png", "#FF69B4"));
        var window = new Window { Content = host }; window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(clock.IsRunning); clock.Tick(); Assert.True(clock.Frames > 0); var before = clock.Frames;
            foreach (var gate in new[] { "gentle", "motion", "performance" })
            {
                settings.GentleVisuals = gate == "gentle"; settings.ReducedMotion = gate == "motion";
                settings.PerformanceMode = gate == "performance"; clock.Tick(); Assert.Equal(before, clock.Frames);
            }
            window.Content = null; Dispatcher.UIThread.RunJobs(); Assert.False(clock.IsRunning);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OriginalArtworkPanelsRenderAndRetainTheExistingInteractiveControls() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-art-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var settings = new ClassicSettings { GentleVisuals = true };
        var tracker = new ProgressTracker(new ProgressData { BubblesPopped = 1200, VideosFinished = 83, SessionSeconds = 6230,
            LifetimeXp = Enumerable.Range(1, 24).Sum(ProgressTracker.GetXpForLevel) + 200 });
        tracker.RefreshAchievements();
        var presets = new PresetPanel(settings, new(), new(folder), () => { });
        var progress = new ProgressPanel(tracker, new(), settings);
        try
        {
            var window = new Window { Width = 700, Height = 760, Content = new ScrollViewer { Content = presets } }; window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(3, presets.GetVisualDescendants().OfType<PanelArtwork>().Count());
                var daily = presets.GetVisualDescendants().OfType<Border>().Single(card => card.Name == "PresetCard_builtin-daily");
                Assert.Contains("selected", daily.Classes);
                Save(presets, "presets-polished.png");
                ((ScrollViewer)window.Content!).Content = progress; Dispatcher.UIThread.RunJobs();
                Assert.Equal(28, progress.GetVisualDescendants().OfType<PanelArtwork>().Count());
                Assert.Equal(27, progress.GetVisualDescendants().OfType<Border>().Count(card => card.Classes.Contains("badge-art-card")));
                Assert.Contains(progress.GetVisualDescendants().OfType<Border>(), card => card.Classes.Contains("selected"));
                Assert.InRange(PanelArtwork.CachedImages, 30, 32); Save(progress, "progress-polished.png");
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        finally { Directory.Delete(folder, recursive: true); }
    });

    private static void Save(Control control, string name)
    {
        if (Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR") is not { } folder) return;
        control.Measure(new Size(676, double.PositiveInfinity)); control.Arrange(new Rect(0, 0, 676, control.DesiredSize.Height));
        var fullHeight = (int)Math.Ceiling(control.Bounds.Height);
        SaveBitmap(Math.Min(900, fullHeight), name);
        if (name == "progress-polished.png") SaveBitmap(fullHeight, "progress-gallery.png");
        void SaveBitmap(int height, string file)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(676, height), new Vector(96, 96));
            bitmap.Render(control); Directory.CreateDirectory(folder); bitmap.Save(Path.Combine(folder, file), PngBitmapEncoderOptions.Default);
        }
    }
}
