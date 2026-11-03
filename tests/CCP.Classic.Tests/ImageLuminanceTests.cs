using CCP.Classic.App.Media;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ImageLuminanceTests
{
    [Fact]
    public void Rec601MatchesOriginalSourceWeightsAndTransparentPixelsHaveNoWeight()
    {
        using var bitmap = new SKBitmap(16, 16); bitmap.Erase(SKColors.Red);
        Assert.Equal(.299, ImageLuminance.Sample(bitmap), 3);
        bitmap.Erase(SKColors.White); Assert.Equal(1, ImageLuminance.Sample(bitmap), 3);
        bitmap.Erase(SKColors.Transparent); Assert.Equal(0, ImageLuminance.Sample(bitmap));
        using var canvas = new SKCanvas(bitmap); using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(0, 0, 8, 16, paint); Assert.Equal(1, ImageLuminance.Sample(bitmap), 3);
    }

    [Fact]
    public async Task ExistingWorkerDecodePublishesFirstFrameBrightnessWithoutRendering()
    {
        var path = Path.Combine(Path.GetTempPath(), "classic-luminance-" + Guid.NewGuid().ToString("N") + ".png");
        LocalImageClip? clip = null; LocalImageLibrary? library = null;
        try
        {
            using var source = new SKBitmap(32, 48); source.Erase(SKColors.Red);
            using (var data = source.Encode(SKEncodedImageFormat.Png, 100)) using (var output = File.Create(path)) data.SaveTo(output);
            await UiDispatcher.Run(() =>
            {
                clip = LocalImageClip.Decode(path, TestContext.Current.CancellationToken);
                Assert.Equal(.299, clip.Brightness!.Value, 3); Assert.Equal(1, clip.FrameCount);
                library = new([path], clip); Assert.Null(library.Brightness(-1)); Assert.Null(library.Brightness(5));
                Assert.Null(library.Brightness(0));
            });
            await library!.Completion.WaitAsync(TestContext.Current.CancellationToken);
            await UiDispatcher.Run(() => { Assert.Equal(.299, library.Brightness(0)!.Value, 3); library.Dispose(); Assert.Null(library.Brightness(0)); });
        }
        finally { await UiDispatcher.Run(() => { library?.Dispose(); clip?.Dispose(); }); File.Delete(path); }
    }
}
