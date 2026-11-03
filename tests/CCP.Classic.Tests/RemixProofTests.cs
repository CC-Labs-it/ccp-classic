using CCP.Classic.App.Effects;
using CCP.Classic.App.Media;
using CCP.Classic.Core.Remix;
using SkiaSharp;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class RemixProofTests
{
    [Fact]
    public Task NativeCompositorProducesAnOpaqueSixteenLayoutGallery() => UiDispatcher.Run(() =>
    {
        var proof = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
        var assets = proof is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(proof))!, "test-assets");
        var originals = new[] { "CCP-Classic-Test-Spiral.gif", "CCP-Classic-Test-Corner-Spiral.gif" };
        var owned = new List<RemixStrip>();
        try
        {
            foreach (var original in originals)
            {
                if (assets is not null && File.Exists(Path.Combine(assets, original)))
                {
                    using var clip = LocalImageClip.Decode(Path.Combine(assets, original), TestContext.Current.CancellationToken, 4 * 1024 * 1024, 480);
                    owned.Add(new(clip.CopyNativeFrames(TestContext.Current.CancellationToken), clip.FrameEnds));
                }
                else
                {
                    var red = new SKBitmap(48, 32); red.Erase(new SKColor(255, 105, 180)); red.SetImmutable();
                    var blue = new SKBitmap(48, 32); blue.Erase(new SKColor(60, 160, 240)); blue.SetImmutable();
                    owned.Add(new([red, blue], [.1, .3]));
                }
            }
            var strips = Enumerable.Range(0, 8).Select(i => owned[i % owned.Count]).ToArray();
            var plan = RemixScene.Create(7, strips.Select(strip => strip.Aspect).ToArray()) with { Loop = "clean" };
            using var gallery = SKSurface.Create(new SKImageInfo(1920, 1200)); gallery.Canvas.Clear(new SKColor(20, 20, 43));
            using var font = new SKFont(SKTypeface.FromFamilyName("Inter"), 17); using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (var index = 0; index < RemixScene.Layouts.Count; index++)
            {
                var scene = plan with { Layout = RemixScene.Layouts[index] }; var frame = (index * 7 + 17) % 45;
                using var renderer = new RemixRenderer(scene, strips); using var target = SKSurface.Create(new SKImageInfo(scene.Width, scene.Height));
                renderer.Draw(target, frame); using var pixels = target.Snapshot();
                using var sample = SKBitmap.FromImage(pixels); Assert.Equal(255, sample.GetPixel(scene.Width / 2, scene.Height / 2).Alpha);
                var x = index % 4 * 480; var y = index / 4 * 300;
                gallery.Canvas.DrawImage(pixels, SKRect.Create(x, y, 480, 270));
                gallery.Canvas.DrawText(scene.Layout + " / " + frame, x + 8, y + 290, font, paint);
            }
            Assert.InRange(owned.Sum(strip => strip.Bytes), 1, 8 * 1024 * 1024);
            if (proof is not null)
            {
                Directory.CreateDirectory(proof); using var image = gallery.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(proof, "remix-native.png")); encoded.SaveTo(file);
            }
        }
        finally { foreach (var strip in owned) strip.Dispose(); }
    });
}
