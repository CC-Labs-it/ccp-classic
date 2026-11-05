using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class SpiralMediaTests
{
    private const string Gif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQIFAAAACwAAAAABAAEAIEAgAAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQIHgAAACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Fact]
    public void SettingsBoundTwoIndependentSlotsAndPresetsDoNotShareSelections()
    {
        var source = new ClassicSettings();
        source.Spiral.Path = "https://invalid.example/spiral.gif"; source.Spiral.TargetMonitor = 900;
        source.Spiral.Corners = [new() { Enabled = true, Size = 9000, Opacity = -1, Position = (SpiralCorner)99 }, new(), new()];
        source.Spiral.Normalize(); Assert.Null(source.Spiral.Path); Assert.Equal(31, source.Spiral.TargetMonitor);
        Assert.Equal(2, source.Spiral.Corners.Count); Assert.Equal(800, source.Spiral.Corners[0].Size);
        Assert.Equal(5, source.Spiral.Corners[0].Opacity); Assert.Equal(SpiralCorner.BottomLeft, source.Spiral.Corners[0].Position);
        var copy = FeatureCustomization.Capture(source); var target = new ClassicSettings(); copy.Apply(target);
        source.Spiral.Corners[0].Enabled = false;
        Assert.True(target.Spiral.Corners[0].Enabled); target.Spiral.Corners[0].Size = 300;
        Assert.Equal(800, copy.Spiral.Corners[0].Size);
    }

    [Fact]
    public void RandomPoolAvoidsConsecutiveChoicesWithoutRepeatingAttempts()
    {
        var random = new Random(1); string? previous = null;
        for (var i = 0; i < 100; i++)
        {
            var next = SpiralMediaLibrary.Pick(["a.gif", "b.gif"], previous, random);
            Assert.NotEqual(previous, next); previous = next;
        }
        Assert.Equal("a.gif", SpiralMediaLibrary.Pick(["a.gif"], "a.gif", random));
        Assert.Null(SpiralMediaLibrary.Pick([], previous, random));
    }

    [Theory]
    [InlineData(SpiralCorner.TopLeft, 0, 0)]
    [InlineData(SpiralCorner.TopRight, 1620, 0)]
    [InlineData(SpiralCorner.BottomLeft, 0, 880)]
    [InlineData(SpiralCorner.BottomRight, 1620, 880)]
    public void CornerGeometryPreservesAspectAndNudgesADuplicateInward(SpiralCorner corner, double x, double y)
    {
        var options = new CornerGifOptions { Position = corner, Size = 300 };
        var normal = CornerGifWindow.BoundsFor(new(1920, 1080), new(360, 240), options, 0);
        Assert.Equal(new Rect(x, y, 300, 200), normal);
        var stacked = CornerGifWindow.BoundsFor(new(1920, 1080), new(360, 240), options, 1);
        Assert.Equal(40, Math.Abs(stacked.X - normal.X)); Assert.Equal(40, Math.Abs(stacked.Y - normal.Y));
        Assert.Equal(default, CornerGifWindow.BoundsFor(new(1920, 1080), default, options, 0));
    }

    [Fact]
    public async Task ASessionChoosesOnlyExplicitPoolFilesAndKeepsTheSelectionUntilRestart()
    {
        using var fixture = new Fixture(); Bitmap fallback = null!; SpiralMediaLibrary media = null!;
        var settings = new ClassicSettings(); settings.Spiral.Randomize = true; settings.Spiral.Folders = [fixture.Pool];
        settings.Spiral.Path = fixture.Outside;
        await UiDispatcher.Run(() => { fallback = new WriteableBitmap(new(1, 1), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul); media = new(settings, fallback, fixture.Pool); media.Start(); });
        try
        {
            await media.ScanCompletion.WaitAsync(TestContext.Current.CancellationToken);
            string? first = null;
            await UiDispatcher.Run(() =>
            {
                media.Update(0); first = media.SelectedPath; Assert.Contains(first, fixture.Files);
                for (var time = 1; time < 20; time++) { media.Update(time); Assert.Equal(first, media.SelectedPath); }
                Assert.NotNull(media.MainImage); Assert.Equal(2, media.CornerImages.Count);
            });
            await media.Completion.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.InRange(media.CachedCount, 1, 3); media.Start(); });
            await media.ScanCompletion.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() =>
            {
                media.Update(0); Assert.Contains(media.SelectedPath, fixture.Files); Assert.NotEqual(first, media.SelectedPath);
                media.Stop(); Assert.Null(media.MainImage); Assert.All(media.CornerImages, image => Assert.Null(image));
            });
        }
        finally { media.Dispose(); await media.Completion.WaitAsync(TestContext.Current.CancellationToken); fallback.Dispose(); }
    }

    [Fact]
    public Task EditorExposesTwoSlotsAndRandomizationWithoutCreatingDesktopHosts() => UiDispatcher.Run(() =>
    {
        using var fixture = new Fixture(); var settings = new ClassicSettings(); settings.Spiral.Folders = [fixture.Pool];
        var changes = 0; var editor = new SpiralOptionsEditor(settings, new LocalizationService("en"), () => changes++);
        var toggles = editor.GetLogicalDescendants().OfType<CheckBox>().ToArray(); Assert.Equal(3, toggles.Length);
        foreach (var toggle in toggles) toggle.IsChecked = true;
        Assert.True(settings.Spiral.Randomize); Assert.All(settings.Spiral.Corners, slot => Assert.True(slot.Enabled)); Assert.Equal(3, changes);
    });

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Pool { get; }
        public string[] Files { get; }
        public string Outside { get; }
        public Fixture()
        {
            Pool = Path.Combine(root, "pool"); Directory.CreateDirectory(Pool);
            Files = [Path.Combine(Pool, "a.gif"), Path.Combine(Pool, "b.gif")];
            Outside = Path.Combine(root, "personal.gif"); foreach (var path in Files.Append(Outside)) File.WriteAllBytes(path, Convert.FromBase64String(Gif));
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
