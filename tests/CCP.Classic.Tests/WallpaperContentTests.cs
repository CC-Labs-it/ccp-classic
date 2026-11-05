using Avalonia;
using CCP.Classic.App.Wallpaper;
using CCP.Classic.Core;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class WallpaperContentTests
{
    [Fact]
    public void EveryOriginalCollageLayoutRendersDeterministicallyAtTheRequestedSize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "classic-collage-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var files = new List<string>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                using var bitmap = new SKBitmap(80 + index * 20, 120); bitmap.Erase(new SKColor((byte)(80 + index * 40), 90, 160));
                var path = Path.Combine(directory, index + ".png"); using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                using (var stream = File.Create(path)) data.SaveTo(stream); files.Add(path);
            }
            foreach (var layout in Enum.GetValues<CollageLayout>())
            {
                var first = CollageComposer.Compose(files, directory, "#E84393", "#08080C", layout, 640, 480, TestContext.Current.CancellationToken, seed: 17);
                var second = CollageComposer.Compose(files, directory, "#E84393", "#08080C", layout, 640, 480, TestContext.Current.CancellationToken, seed: 17);
                Assert.NotNull(first); Assert.NotNull(second); Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
                using var rendered = SKBitmap.Decode(first); Assert.Equal(640, rendered.Width); Assert.Equal(480, rendered.Height);
                Assert.True(rendered.Pixels.Select(pixel => pixel.Red).Distinct().Count() > 10);
            }
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    [Fact]
    public async Task OwnWebpIsConvertedForWindowsAndCleanupOnlyDeletesOwnedOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "classic-own-" + Guid.NewGuid().ToString("N") + ".webp");
        var content = new WallpaperContent();
        try
        {
            using var bitmap = new SKBitmap(32, 48); bitmap.Erase(SKColors.MediumPurple);
            using (var data = bitmap.Encode(SKEncodedImageFormat.Webp, 100))
            using (var stream = File.Create(path)) data.SaveTo(stream);
            var choice = WallpaperChoice.From(new WallpaperOptions { OwnImagePath = path });
            var result = await content.SelectAsync(false, new([], []), choice, [new(new PixelRect(0, 0, 640, 480), 1)], null,
                TestContext.Current.CancellationToken);
            Assert.NotNull(result); Assert.EndsWith(".png", result); Assert.True(File.Exists(result));
            using var decoded = SKBitmap.Decode(result); Assert.Equal(32, decoded.Width); Assert.Equal(48, decoded.Height);
            content.ClearExcept(result); Assert.True(File.Exists(result)); content.ClearExcept(null);
            Assert.False(File.Exists(result)); Assert.True(File.Exists(path));
        }
        finally { content.ClearExcept(null); File.Delete(path); }
    }
}
