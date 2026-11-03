using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LibraryPanelTests
{
    [Fact]
    public async Task ThumbnailWallFiltersSelectsAndPreviewsActualFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-library-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        LibraryPanel? panel = null; Window? window = null;
        try
        {
            using (var source = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/flash.png")))
            using (var target = File.Create(Path.Combine(folder, "test-image.png"))) source.CopyTo(target);
            File.WriteAllBytes(Path.Combine(folder, "second.mp4"), [0, 1, 2]);
            var settings = new ClassicSettings(); settings.Library.ImageFolders.Add(folder); settings.Library.VideoFolders.Add(folder);
            var changes = 0; LibraryAsset? preview = null;
            await UiDispatcher.Run(() =>
            {
                panel = new(settings, new("en"), () => changes++, _ => { }); panel.PreviewRequested += value => preview = value;
                window = new() { Width = 900, Height = 650, Content = panel }; window.Show();
            });
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var loaded = false; await UiDispatcher.Run(() => loaded = panel!.Assets.Any(asset => asset.Kind == "Videos"));
                if (loaded) break; await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await UiDispatcher.Run(() =>
            {
                Named<ToggleButton>(panel!, "LibraryFilter_Images").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var included = Assert.Single(panel!.GetLogicalDescendants().OfType<CheckBox>());
                Assert.True(included.IsChecked); included.IsChecked = false;
                Assert.False(settings.Library.Includes(Path.Combine(folder, "test-image.png"))); Assert.Equal(1, changes);
                var tile = Assert.Single(panel!.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("media-card"));
                tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("Images", preview!.Kind);
                Assert.True(File.Exists(preview.Path));
                Named<TextBox>(panel!, "LibrarySearch").Text = "does not exist";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(panel!.GetLogicalDescendants().OfType<CheckBox>());
                Named<TextBox>(panel!, "LibrarySearch").Text = "test";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Single(panel!.GetLogicalDescendants().OfType<CheckBox>());
            });
        }
        finally
        {
            await UiDispatcher.Run(() => { window?.Close(); panel?.Dispose(); });
            await DeleteAfterThumbnailReadersFinish(folder);
        }
    }

    [Fact]
    public async Task FreshLibraryShowsStarterArtworkAndRecordingsWithoutChangingTheirSelection()
    {
        LibraryPanel? panel = null; Window? window = null;
        try
        {
            await UiDispatcher.Run(() => { panel = new(new(), new("en"), () => Assert.Fail("Starter files are not selectable"), _ => { }); window = new() { Width = 900, Height = 650, Content = panel }; window.Show(); });
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var ready = false; await UiDispatcher.Run(() => ready = panel!.Assets.Count > 0);
                if (ready) break; await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await UiDispatcher.Run(() =>
            {
                Assert.Equal(6, panel!.Assets.Count); Assert.All(panel.Assets, asset => Assert.True(asset.Starter));
                Assert.All(panel.GetLogicalDescendants().OfType<CheckBox>(), check => Assert.False(check.IsEnabled));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR") is { } folder)
                {
                    using var proof = new RenderTargetBitmap(new PixelSize(900, 650), new Vector(96, 96)); proof.Render(window!);
                    Directory.CreateDirectory(folder); proof.Save(Path.Combine(folder, "library-polished.png"), PngBitmapEncoderOptions.Default);
                }
            });
        }
        finally { await UiDispatcher.Run(() => { window?.Close(); panel?.Dispose(); }); }
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static async Task DeleteAfterThumbnailReadersFinish(string folder)
    {
        // A decoder already inside the native call releases its local read handle after cancellation.
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try { Directory.Delete(folder, true); return; }
            catch (IOException) when (Environment.TickCount64 < deadline)
            { await Task.Delay(25, TestContext.Current.CancellationToken); }
        }
    }
}
