using SkiaSharp;

namespace CCP.Classic.App.Media;

internal static class ImageLuminance
{
    // Public CCP FlashService.SampleLuminance: first frame, 8x8, Rec.601 with straight alpha weights.
    public static double Sample(SKBitmap source)
    {
        using var small = new SKBitmap(new SKImageInfo(Math.Min(8, source.Width), Math.Min(8, source.Height), SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!source.ScalePixels(small.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear))) return 0;
        double sum = 0, weight = 0;
        for (var y = 0; y < small.Height; y++) for (var x = 0; x < small.Width; x++)
        {
            var pixel = small.GetPixel(x, y); var alpha = pixel.Alpha / 255d;
            sum += (.299 * pixel.Red + .587 * pixel.Green + .114 * pixel.Blue) / 255 * alpha; weight += alpha;
        }
        return weight > 0 ? Math.Clamp(sum / weight, 0, 1) : 0;
    }
}
