// Ported from pinned public CCP CollageComposer, MIT. See PARITY-WALLPAPER-COMPANION.md.
using System;
using System.IO;
using SkiaSharp;

namespace CCP.Classic.App.Wallpaper
{
    internal static partial class CollageComposer
    {

        internal static readonly string[] SourceExtensions =
            { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".apng" };

        private const int MaxTiltDeg = 20;
        private const int DecodeBudget = 40;
        private const int TileDecodeCap = 768;

        internal static readonly SKColor DefaultAccent = new(0xE8, 0x43, 0x93);
        internal static readonly SKColor DefaultBackground = new(0x08, 0x08, 0x0C);

        private static void DrawTileCore(SKCanvas canvas, SKBitmap src, float cx, float cy, int w, int h,
            float fL, float fT, float fR, float fB, float angleDeg, SKImageFilter? sharpen = null)
        {
            if (w <= 0 || h <= 0) return;
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return;
            var tc = surface.Canvas;
            tc.Clear(SKColors.Transparent);

            var srcRect = CoverSrcRect(src.Width, src.Height, w, h);
            using (var ip = new SKPaint { IsAntialias = true })
            {
                if (sharpen != null) ip.ImageFilter = sharpen;
                tc.DrawBitmap(src, srcRect, new SKRect(0, 0, w, h), ip);
            }

            ApplyEdgeFeather(tc, w, h, fL, fT, fR, fB);

            using var img = surface.Snapshot();
            canvas.Save();
            canvas.Translate(cx, cy);
            if (angleDeg != 0f) canvas.RotateDegrees(angleDeg);
            using (var dp = new SKPaint { IsAntialias = true })
                canvas.DrawImage(img, -w / 2f, -h / 2f, dp);
            canvas.Restore();
        }

        internal static void ApplyEdgeFeather(SKCanvas tc, int w, int h, float fL, float fT, float fR, float fB)
        {
            if (w > 0 && (fL > 0.5f || fR > 0.5f))
            {
                float l = Math.Clamp(fL, 0, w), r = Math.Clamp(w - fR, 0, w);
                if (r <= l) { l = w / 2f; r = w / 2f; }
                var colors = new[] { SKColors.White.WithAlpha(0), SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) };
                var pos = new[] { 0f, l / w, r / w, 1f };
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0), colors, pos, SKShaderTileMode.Clamp);
                using var p = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = sh, IsAntialias = true };
                tc.DrawRect(0, 0, w, h, p);
            }
            if (h > 0 && (fT > 0.5f || fB > 0.5f))
            {
                float t = Math.Clamp(fT, 0, h), b = Math.Clamp(h - fB, 0, h);
                if (b <= t) { t = h / 2f; b = h / 2f; }
                var colors = new[] { SKColors.White.WithAlpha(0), SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) };
                var pos = new[] { 0f, t / h, b / h, 1f };
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), colors, pos, SKShaderTileMode.Clamp);
                using var p = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = sh, IsAntialias = true };
                tc.DrawRect(0, 0, w, h, p);
            }
        }

        private static readonly SKRect DefaultFocus = new SKRect(0.2f, 0.15f, 0.8f, 0.85f);

        internal static SKRect ComputeFocus(SKBitmap bmp)
        {
            try
            {
                const int S = 40;
                using var small = new SKBitmap(new SKImageInfo(S, S, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (!bmp.ScalePixels(small.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear))) return DefaultFocus;
                var px = small.Pixels;
                if (px == null || px.Length < S * S) return DefaultFocus;

                var lum = new float[S * S];
                for (int i = 0; i < S * S; i++) { var c = px[i]; lum[i] = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue; }

                var e = new float[S * S];
                float sum = 0, mx = 0, my = 0;
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float gx = Math.Abs(lum[i] - lum[i + (x < S - 1 ? 1 : 0)]);
                    float gy = Math.Abs(lum[i] - lum[i + (y < S - 1 ? S : 0)]);
                    float en = gx + gy; e[i] = en; sum += en; mx += en * x; my += en * y;
                }
                if (sum < 1e-3f) return DefaultFocus;

                float cx = mx / sum, cy = my / sum, vx = 0, vy = 0;
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float en = e[y * S + x]; vx += en * (x - cx) * (x - cx); vy += en * (y - cy) * (y - cy);
                }
                float sx = (float)Math.Sqrt(vx / sum), sy = (float)Math.Sqrt(vy / sum), k = 1.15f;
                float x0 = Clamp01((cx - k * sx) / S), x1 = Clamp01((cx + k * sx) / S);
                float y0 = Clamp01((cy - k * sy) / S), y1 = Clamp01((cy + k * sy) / S);
                return new SKRect(x0, y0, Math.Max(x0 + 0.08f, x1), Math.Max(y0 + 0.08f, y1));
            }
            catch (Exception)
            {
                return DefaultFocus;
            }
        }

        internal static void PerEdgeFeather(SKRect focus, int sw, int sh, int tw, int th,
            out float fL, out float fT, out float fR, out float fB)
        {
            float scale = Math.Max((float)tw / sw, (float)th / sh);
            float cropW = tw / scale, cropH = th / scale;
            float left = (sw - cropW) / 2f, top = (sh - cropH) / 2f;
            float fx0 = Clamp01((focus.Left * sw - left) / cropW);
            float fx1 = Clamp01((focus.Right * sw - left) / cropW);
            float fy0 = Clamp01((focus.Top * sh - top) / cropH);
            float fy1 = Clamp01((focus.Bottom * sh - top) / cropH);
            float baseF = Math.Min(tw, th);
            fL = EdgeFeather(fx0) * baseF;
            fR = EdgeFeather(1f - fx1) * baseF;
            fT = EdgeFeather(fy0) * baseF;
            fB = EdgeFeather(1f - fy1) * baseF;
        }
        private static float EdgeFeather(float gap) => 0.06f + 0.14f * Clamp01(gap / 0.18f);
        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static void DrawAtmosphere(SKCanvas canvas, SKBitmap src, int w, int h)
        {
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return;
            var srcRect = CoverSrcRect(src.Width, src.Height, w, h);
            using (var p = new SKPaint { IsAntialias = true })
                surface.Canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, w, h), p);
            using var img = surface.Snapshot();

            float sigma = Math.Max(20f, w / 45f);
            using var blur = SKImageFilter.CreateBlur(sigma, sigma);
            using var dim = SKColorFilter.CreateColorMatrix(BrightnessMatrix(0.52f));
            using var desat = SKColorFilter.CreateColorMatrix(SaturationMatrix(0.65f));
            using var grade = SKColorFilter.CreateCompose(dim, desat);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = blur,
                ColorFilter = grade,
                Color = SKColors.White.WithAlpha(224),
            };
            canvas.DrawImage(img, 0, 0, paint);
        }

        internal static void DrawBackdrop(SKCanvas canvas, int w, int h, SKColor background, SKBitmap? atmosphere)
        {
            DrawVerticalGradient(canvas, w, h, Lift(background, 0.10f), background);
            if (atmosphere != null) DrawAtmosphere(canvas, atmosphere, w, h);
        }

        internal static void DrawFinish(SKCanvas canvas, int w, int h, SKColor accent)
        {
            DrawGrade(canvas, w, h, accent, 0.10f);
            DrawVignette(canvas, w, h, 0.55f);
        }

        internal static SKImageFilter HeroSharpen() => SKImageFilter.CreateMatrixConvolution(
            new SKSizeI(3, 3),
            new float[] { 0, -0.4f, 0, -0.4f, 2.6f, -0.4f, 0, -0.4f, 0 },
            1f, 0f, new SKPointI(1, 1), SKShaderTileMode.Clamp, false);

        private static void DrawVerticalGradient(SKCanvas canvas, int w, int h, SKColor top, SKColor bottom)
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, h),
                new[] { top, bottom }, null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        private static void DrawGrade(SKCanvas canvas, int w, int h, SKColor tint, float amount)
        {
            using var paint = new SKPaint { Color = tint.WithAlpha((byte)(amount * 255)) };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        private static void DrawVignette(SKCanvas canvas, int w, int h, float strength)
        {
            var center = new SKPoint(w / 2f, h / 2f);
            float radius = (float)Math.Sqrt(w * (double)w + h * (double)h) / 2f;
            var colors = new[]
            {
                SKColors.Black.WithAlpha(0),
                SKColors.Black.WithAlpha(0),
                SKColors.Black.WithAlpha((byte)(strength * 255)),
            };
            var stops = new[] { 0f, 0.45f, 1f };
            using var shader = SKShader.CreateRadialGradient(center, radius, colors, stops, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        internal static SKBitmap? DecodeCapped(string path, int maxSide, out long sourcePixels)
        {
            sourcePixels = 0;
            try
            {
                if (Media.LocalImageLibrary.LocalPath(path) is not { } local || new FileInfo(local).Length > 64 * 1024 * 1024) return null;
                using var codec = SKCodec.Create(local);
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
                sourcePixels = (long)codec.Info.Width * codec.Info.Height;
                if (sourcePixels > 40_000_000 || codec.FrameCount > 600) return null;
                var ratio = Math.Min(1, (double)maxSide / Math.Max(codec.Info.Width, codec.Info.Height));
                var dimensions = codec.GetScaledDimensions((float)ratio);
                var info = new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if ((long)info.Width * info.Height > 16_777_216) return null;
                var bitmap = new SKBitmap(info); var success = false;
                try
                {
                    var representative = Math.Clamp(codec.FrameCount / 2, 0, 120);
                    for (var index = 0; index <= representative; index++)
                    {
                        var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(index, index == 0 ? -1 : index - 1));
                        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return null;
                    }
                    success = true; return bitmap;
                }
                finally { if (!success) bitmap.Dispose(); }
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { return null; }
        }

        internal static SKRect CoverSrcRect(int sw, int sh, int tw, int th)
        {
            float scale = Math.Max((float)tw / sw, (float)th / sh);
            float cropW = tw / scale, cropH = th / scale;
            float left = (sw - cropW) / 2f, top = (sh - cropH) / 2f;
            return new SKRect(left, top, left + cropW, top + cropH);
        }

        internal static float ClampAspect(int width, int height)
        {
            float a = height <= 0 ? 1f : (float)width / height;
            return Math.Clamp(a, 0.62f, 1.62f);
        }

        private static float Range(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        internal static SKColor HexToColor(string? hex, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if (h.Length >= 6 &&
                uint.TryParse(h.Substring(0, 6), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                return new SKColor((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            }
            return fallback;
        }

        private static SKColor Lift(SKColor c, float amt) => new SKColor(
            (byte)(c.Red + (255 - c.Red) * amt),
            (byte)(c.Green + (255 - c.Green) * amt),
            (byte)(c.Blue + (255 - c.Blue) * amt));

        private static float[] BrightnessMatrix(float b) => new float[]
        {
            b, 0, 0, 0, 0,
            0, b, 0, 0, 0,
            0, 0, b, 0, 0,
            0, 0, 0, 1, 0,
        };

        private static float[] SaturationMatrix(float s)
        {
            const float lr = 0.213f, lg = 0.715f, lb = 0.072f;
            return new float[]
            {
                lr * (1 - s) + s, lg * (1 - s),     lb * (1 - s),     0, 0,
                lr * (1 - s),     lg * (1 - s) + s, lb * (1 - s),     0, 0,
                lr * (1 - s),     lg * (1 - s),     lb * (1 - s) + s, 0, 0,
                0,                0,                0,                1, 0,
            };
        }
    }
}
