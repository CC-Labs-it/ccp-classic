// Ported from pinned public CCP CollageComposer, MIT. See PARITY-WALLPAPER-COMPANION.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace CCP.Classic.App.Wallpaper
{
    public enum CollageLayout
    {
        SideHero,
        FillerOnly,
        CenterHero,
    }

    internal enum CollageSlotKind { Fill, Hero, Spill }

    internal sealed class CollageSlot
    {
        public CollageSlotKind Kind;
        public int Index;
        public float Cx, Cy, AngleDeg;
        public float Base;
        public int HeroW, HeroH, Feather;
        public bool HardL, HardT, HardR, HardB;

        public (int W, int H) TileSize(int srcW, int srcH)
        {
            if (Kind == CollageSlotKind.Hero) return (HeroW, HeroH);
            float aspect = CollageComposer.ClampAspect(srcW, srcH);
            return (Math.Max(1, (int)Base), Math.Max(1, (int)(Base / aspect)));
        }

        public void HeroFeather(out float fL, out float fT, out float fR, out float fB)
        {
            fL = HardL ? 0 : Feather;
            fT = HardT ? 0 : Feather;
            fR = HardR ? 0 : Feather;
            fB = HardB ? 0 : Feather;
        }
    }

    internal static partial class CollageComposer
    {
        public static string? Compose(IReadOnlyList<string> sources, string outputDir, string accentHex, string backgroundHex,
            CollageLayout layout = CollageLayout.SideHero, int width = 1920, int height = 1080, CancellationToken token = default, int? seed = null)
        {
            var files = sources.Take(100000).ToList();
            if (files.Count == 0)
            {
                return null;
            }

            var rng = seed is { } chosenSeed ? new Random(chosenSeed) : new Random();
            Shuffle(files, rng);

            var decoded = new List<(SKBitmap Bmp, long Pixels, string Path)>();
            foreach (var f in files)
            {
                if (token.IsCancellationRequested) { foreach (var item in decoded) item.Bmp.Dispose(); token.ThrowIfCancellationRequested(); }
                if (decoded.Count >= DecodeBudget) break;
                var bmp = DecodeCapped(f, TileDecodeCap, out var pixels);
                if (bmp != null && bmp.Width > 0 && bmp.Height > 0) decoded.Add((bmp, pixels, f));
                else bmp?.Dispose();
            }
            decoded.Sort((a, b) => b.Pixels.CompareTo(a.Pixels));
            var images = decoded.Select(d => d.Bmp).ToList();

            if (images.Count < 2)
            {
                foreach (var b in images) b.Dispose();
                return null;
            }

            try
            {
                var w = Math.Clamp(width, 640, 3840); var h = Math.Clamp(height, 480, 2160);
                var accent = HexToColor(accentHex, DefaultAccent);
                var background = HexToColor(backgroundHex, DefaultBackground);

                using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (surface == null)
                {
                    return null;
                }
                var canvas = surface.Canvas;

                var heroBmp = DecodeCapped(decoded[0].Path, Math.Max(w, h), out _);
                if (heroBmp != null) { images[0].Dispose(); images[0] = heroBmp; }

                var focus = new List<SKRect>(images.Count);
                foreach (var b in images) focus.Add(ComputeFocus(b));

                DrawBackdrop(canvas, w, h, background, images[rng.Next(images.Count)]);

                DrawPlan(canvas, PlanLayout(layout, w, h, rng), images, focus);

                DrawFinish(canvas, w, h, accent);

                canvas.Flush();

                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(outputDir);
                var outPath = Path.Combine(outputDir, "ccp_collage_" + Guid.NewGuid().ToString("N") + ".jpg");
                using (var image = surface.Snapshot())
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 92))
                using (var fs = File.Create(outPath))
                {
                    data.SaveTo(fs);
                }

                return outPath;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                foreach (var b in images) b.Dispose();
            }
        }

        internal static List<CollageSlot> PlanLayout(CollageLayout layout, int w, int h, Random rng)
        {
            var slots = new List<CollageSlot>();
            switch (layout)
            {
                case CollageLayout.FillerOnly:
                    PlanFill(slots, new SKRect(0, 0, w, h), rng, cols: 9);
                    break;

                case CollageLayout.CenterHero:
                {
                    int heroW = (int)(w * Range(rng, 0.44f, 0.50f));
                    int heroH = (int)(h * Range(rng, 0.90f, 1.0f));
                    int heroX = (w - heroW) / 2;
                    int heroY = (h - heroH) / 2;
                    int feather = Math.Max(30, heroW / 11);
                    bool tallHero = heroH >= h;
                    int leftEdge = heroX, rightEdge = heroX + heroW;

                    PlanFill(slots, new SKRect(0, 0, leftEdge + feather, h), rng, cols: 2);
                    PlanFill(slots, new SKRect(rightEdge - feather, 0, w, h), rng, cols: 2);
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Hero, Cx = heroX + heroW / 2f, Cy = heroY + heroH / 2f,
                        HeroW = heroW, HeroH = heroH, Feather = feather,
                        HardL = false, HardT = tallHero, HardR = false, HardB = tallHero,
                    });
                    for (int k = 0; k < 6; k++)
                    {
                        float edge = (k % 2 == 0) ? leftEdge : rightEdge;
                        slots.Add(new CollageSlot
                        {
                            Kind = CollageSlotKind.Spill, Index = k,
                            Cx = edge + Range(rng, -0.04f, 0.04f) * w,
                            Cy = Range(rng, 0.07f, 0.93f) * h,
                            Base = Math.Max(1, (int)(heroW * Range(rng, 0.18f, 0.26f))),
                            AngleDeg = Range(rng, -MaxTiltDeg, MaxTiltDeg),
                        });
                    }
                    break;
                }

                default:
                {
                    bool heroLeft = rng.NextDouble() < 0.5;
                    int heroW = (int)(w * Range(rng, 0.40f, 0.48f));
                    int heroH = (int)(h * Range(rng, 0.90f, 1.0f));
                    float bleed = Range(rng, 0.06f, 0.18f);
                    int heroX = heroLeft ? -(int)(heroW * bleed) : w - heroW + (int)(heroW * bleed);
                    int heroY = (h - heroH) / 2;
                    int feather = Math.Max(30, heroW / 11);
                    int heroInner = heroLeft ? heroX + heroW : heroX;
                    bool tallHero = heroH >= h;

                    PlanFill(slots, heroLeft ? new SKRect(heroInner - feather, 0, w, h) : new SKRect(0, 0, heroInner + feather, h),
                        rng, cols: 5);
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Hero, Cx = heroX + heroW / 2f, Cy = heroY + heroH / 2f,
                        HeroW = heroW, HeroH = heroH, Feather = feather,
                        HardL = heroLeft, HardT = tallHero, HardR = !heroLeft, HardB = tallHero,
                    });
                    for (int k = 0; k < 6; k++)
                    {
                        slots.Add(new CollageSlot
                        {
                            Kind = CollageSlotKind.Spill, Index = k,
                            Cx = heroInner + Range(rng, -0.05f, 0.05f) * w,
                            Cy = Range(rng, 0.07f, 0.93f) * h,
                            Base = Math.Max(1, (int)(heroW * Range(rng, 0.20f, 0.30f))),
                            AngleDeg = Range(rng, -MaxTiltDeg, MaxTiltDeg),
                        });
                    }
                    break;
                }
            }
            return slots;
        }

        private static void PlanFill(List<CollageSlot> slots, SKRect region, Random rng, int cols)
        {
            const float coverage = 1.81f;
            const float upright = 0.34f;
            if (cols < 1) cols = 1;
            float rw = region.Width, rh = region.Height;
            if (rw <= 8) return;

            float cellW = rw / cols;
            int rows = Math.Max(1, (int)Math.Round(rh / cellW));
            float cellH = rh / rows;
            int i = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    slots.Add(new CollageSlot
                    {
                        Kind = CollageSlotKind.Fill, Index = i++,
                        Cx = region.Left + (c + 0.5f) * cellW + Range(rng, -0.28f, 0.28f) * cellW,
                        Cy = region.Top + (r + 0.5f) * cellH + Range(rng, -0.28f, 0.28f) * cellH,
                        Base = Math.Max(cellW, cellH) * coverage * Range(rng, 0.92f, 1.18f),
                        AngleDeg = rng.NextDouble() < upright ? 0f : Range(rng, -MaxTiltDeg, MaxTiltDeg),
                    });
                }
            }
        }

        private static void DrawPlan(SKCanvas canvas, List<CollageSlot> slots, List<SKBitmap> images, List<SKRect> focus)
        {
            int baseIdx = slots.Exists(s => s.Kind == CollageSlotKind.Hero) ? 1 : 0;
            int n = Math.Max(1, images.Count - baseIdx);
            foreach (var s in slots)
            {
                if (s.Kind == CollageSlotKind.Hero)
                {
                    s.HeroFeather(out var hL, out var hT, out var hR, out var hB);
                    using var sharpen = HeroSharpen();
                    DrawTileCore(canvas, images[0], s.Cx, s.Cy, s.HeroW, s.HeroH, hL, hT, hR, hB, 0f, sharpen);
                    continue;
                }

                int idx = s.Kind == CollageSlotKind.Fill
                    ? baseIdx + s.Index % n
                    : 1 + (s.Index * 5 + 2) % (images.Count - 1);
                var src = images[idx];
                var (tw, th) = s.TileSize(src.Width, src.Height);
                PerEdgeFeather(focus[idx], src.Width, src.Height, tw, th, out var fL, out var fT, out var fR, out var fB);
                DrawTileCore(canvas, src, s.Cx, s.Cy, tw, th, fL, fT, fR, fB, s.AngleDeg, null);
            }
        }
    }
}
