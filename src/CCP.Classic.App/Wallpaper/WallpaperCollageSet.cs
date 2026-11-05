using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace CCP.Classic.App.Wallpaper;

internal sealed class WallpaperCollageSet : IDisposable
{
    internal const int AnimatedCap = 19;
    private static readonly SemaphoreSlim BakeLane = new(1, 1);
    private readonly List<IDisposable> owned = [];
    private readonly List<WallpaperTileClip> moving = [];
    private readonly Dictionary<WallpaperMonitor, Scene> scenes = [];
    private double last, animation;
    private bool disposed, wasAllowed;
    public long DecodedBytes { get; private set; }
    public int MovingTiles { get; private set; }
    public IImage For(WallpaperMonitor monitor) => scenes.GetValueOrDefault(monitor) ?? scenes.Values.First();
    public bool Advance(double seconds, bool allowed, double speed)
    {
        if (disposed || !double.IsFinite(seconds)) return false;
        seconds = Math.Max(last, seconds); if (allowed && wasAllowed) animation += (seconds - last) * speed;
        last = seconds; wasAllowed = allowed;
        var changed = false; foreach (var clip in moving) changed |= clip.Advance(animation); return changed;
    }
    public static async Task<WallpaperCollageSet> BuildAsync(IReadOnlyList<string> files, IReadOnlyList<WallpaperMonitor> monitors,
        WallpaperChoice choice, CancellationToken token, int? seed = null, Action? checkResources = null)
    {
        await BakeLane.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Build(files, monitors, choice, token, seed ?? Random.Shared.Next(), checkResources), token).ConfigureAwait(false); }
        finally { BakeLane.Release(); }
    }
    private sealed record Source(string Path, WallpaperTileClip Still);
    private sealed class Slot(CollageSlot plan, Source source)
    { public CollageSlot Plan = plan; public Source Source = source; public bool Animated; }
    private static WallpaperCollageSet Build(IReadOnlyList<string> files, IReadOnlyList<WallpaperMonitor> monitors,
        WallpaperChoice choice, CancellationToken token, int seed, Action? checkResources)
    {
        var result = new WallpaperCollageSet(); var budget = new WallpaperFrameBudget(checkResources); var rng = new Random(seed);
        var candidates = files.Distinct().Take(100000).ToArray(); rng.Shuffle(candidates);
        var sources = new List<Source>(); var clips = new Dictionary<(string, int), WallpaperTileClip>();
        try
        {
            foreach (var path in candidates)
            {
                token.ThrowIfCancellationRequested(); if (sources.Count >= 40) break;
                try { var still = WallpaperTileClip.Decode(path, false, 768, budget, token); result.owned.Add(still); sources.Add(new(path, still)); }
                catch (Exception error) when (error is not WallpaperResourceException && error is IOException or ArgumentException or InvalidOperationException) { }
            }
            sources = sources.OrderByDescending(s => s.Still.SourcePixels).ToList();
            if (sources.Count < 2) throw new IOException("Collage requires two readable local images.");
            var sceneSizes = monitors.Select(m =>
            { var k = Math.Min(1, 3840d / Math.Max(m.Bounds.Width, m.Bounds.Height));
                return (Width: Math.Max(16, (int)(m.Bounds.Width * k)), Height: Math.Max(16, (int)(m.Bounds.Height * k))); }).ToArray();
            budget.Reserve(sceneSizes.Sum(s => (long)s.Width * s.Height * 8));
            var layouts = Enum.GetValues<CollageLayout>().Where(l => (choice.LayoutMask & (1 << (int)l)) != 0).ToArray();
            var layout = layouts.Length > 0 ? layouts[rng.Next(layouts.Length)] : CollageLayout.SideHero;
            foreach (var monitor in monitors)
            {
                token.ThrowIfCancellationRequested(); budget.Check();
                var (w, h) = sceneSizes[result.scenes.Count];
                var plans = CollageComposer.PlanLayout(layout, w, h, rng);
                var hero = sources[result.scenes.Count % sources.Count]; var rest = sources.Where(s => !ReferenceEquals(s, hero)).ToArray(); var next = 0;
                var slots = plans.Select(p => new Slot(p, p.Kind == CollageSlotKind.Hero ? hero : rest[next++ % rest.Length])).ToList();
                RollMoving(slots, rng);
                var tiles = new List<Tile>();
                foreach (var slot in slots)
                {
                    token.ThrowIfCancellationRequested(); var plan = slot.Plan; var source = slot.Source;
                    var clip = source.Still; var cap = plan.Kind == CollageSlotKind.Hero ? 512 : 320;
                    if (slot.Animated)
                    {
                        if (!clips.TryGetValue((source.Path, cap), out var animated))
                        {
                            try { animated = WallpaperTileClip.Decode(source.Path, true, cap, budget, token, plan.Kind == CollageSlotKind.Hero);
                                clips[(source.Path, cap)] = animated; result.owned.Add(animated); if (animated.FrameCount > 1) result.moving.Add(animated); }
                            catch (Exception error) when (error is not WallpaperResourceException && error is IOException or ArgumentException or InvalidOperationException) { animated = source.Still; }
                        }
                        clip = animated; if (clip.FrameCount > 1) result.MovingTiles++;
                    }
                    else if (plan.Kind == CollageSlotKind.Hero)
                    {
                        var heroCap = Math.Clamp(Math.Max(plan.HeroW, plan.HeroH), 512, 3840);
                        if (!clips.TryGetValue((source.Path, -heroCap), out var sharp))
                        {
                            try { sharp = WallpaperTileClip.Decode(source.Path, false, heroCap, budget, token, true);
                                clips[(source.Path, -heroCap)] = sharp; result.owned.Add(sharp); }
                            catch (Exception error) when (error is not WallpaperResourceException && error is IOException or ArgumentException or InvalidOperationException) { sharp = source.Still; }
                        }
                        clip = sharp;
                    }
                    var (tw, th) = plan.TileSize((int)source.Still.Size.Width, (int)source.Still.Size.Height);
                    float left, top, right, bottom;
                    if (plan.Kind == CollageSlotKind.Hero) plan.HeroFeather(out left, out top, out right, out bottom);
                    else CollageComposer.PerEdgeFeather(source.Still.Focus, (int)source.Still.Size.Width, (int)source.Still.Size.Height, tw, th,
                        out left, out top, out right, out bottom);
                    tiles.Add(new(clip, new(plan.Cx, plan.Cy), new(tw, th), plan.AngleDeg, left, top, right, bottom));
                }
                using var atmosphere = sources[rng.Next(sources.Count)].Still.CopyStill();
                var backdrop = Layer(w, h, budget, canvas => CollageComposer.DrawBackdrop(canvas, w, h, Parse(choice.Background, CollageComposer.DefaultBackground), atmosphere));
                result.owned.Add(backdrop);
                var finish = Layer(w, h, budget, canvas => CollageComposer.DrawFinish(canvas, w, h, Parse(choice.Accent, CollageComposer.DefaultAccent)));
                result.owned.Add(finish); result.scenes.Add(monitor, new(w, h, backdrop, finish, tiles));
            }
            result.DecodedBytes = budget.Used; return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static SKColor Parse(string value, SKColor fallback) => SKColor.TryParse(value, out var color) ? color : fallback;
    private static Bitmap Layer(int w, int h, WallpaperFrameBudget budget, Action<SKCanvas> paint)
    {
        budget.Check((long)w * h * 8); using var surface = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(surface); canvas.Clear(SKColors.Transparent); paint(canvas); canvas.Flush();
        return new(PixelFormat.Bgra8888, AlphaFormat.Premul, surface.GetPixels(), new(w, h), new(96, 96), surface.RowBytes);
    }
    private static void RollMoving(List<Slot> slots, Random rng)
    {
        var capable = slots.Where(s => s.Source.Still.SourceFrames is > 1 and <= 900).ToList(); var moving = new List<Slot>();
        var hero = capable.FirstOrDefault(s => s.Plan.Kind == CollageSlotKind.Hero);
        if (hero is not null && rng.NextDouble() < Math.Min(.9, (double)AnimatedCap / Math.Max(1, capable.Count) + .25))
        { hero.Animated = true; moving.Add(hero); }
        var pool = capable.Where(s => !ReferenceEquals(s, hero)).ToList();
        while (moving.Count < AnimatedCap && pool.Count > 0)
        {
            var farthest = moving.Count == 0 ? pool : pool.OrderByDescending(s => moving.Min(m =>
                Math.Pow(s.Plan.Cx - m.Plan.Cx, 2) + Math.Pow(s.Plan.Cy - m.Plan.Cy, 2))).Take(3).ToList();
            var pick = farthest[rng.Next(farthest.Count)]; pick.Animated = true; moving.Add(pick); pool.Remove(pick);
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; foreach (var resource in owned) resource.Dispose(); owned.Clear(); moving.Clear(); scenes.Clear(); }

    private sealed record Tile(IImage Image, Point Centre, Size Size, double Angle, float Left, float Top, float Right, float Bottom)
    {
        public IBrush Horizontal { get; } = Mask(Left / Size.Width, Right / Size.Width, true);
        public IBrush Vertical { get; } = Mask(Top / Size.Height, Bottom / Size.Height, false);
    }
    private static IBrush Mask(double first, double last, bool horizontal)
    {
        var l = Math.Clamp(first, 0, .5); var r = Math.Clamp(1 - last, .5, 1);
        return new LinearGradientBrush { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = horizontal ? new(1, 0, RelativeUnit.Relative) : new(0, 1, RelativeUnit.Relative),
            GradientStops = [new(l == 0 ? Colors.White : Colors.Transparent, 0), new(Colors.White, l), new(Colors.White, r),
                new(r == 1 ? Colors.White : Colors.Transparent, 1)] }.ToImmutable();
    }
    private sealed class Scene(int width, int height, IImage backdrop, IImage finish, List<Tile> tiles) : IImage
    {
        public Size Size => new(width, height);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destination)
        {
            using (context.PushClip(destination))
            using (context.PushTransform(Matrix.CreateTranslation(-sourceRect.X, -sourceRect.Y)
                * Matrix.CreateScale(destination.Width / sourceRect.Width, destination.Height / sourceRect.Height)
                * Matrix.CreateTranslation(destination.X, destination.Y)))
            {
                context.DrawImage(backdrop, new Rect(Size));
                foreach (var tile in tiles)
                {
                    var rect = new Rect(-tile.Size.Width / 2, -tile.Size.Height / 2, tile.Size.Width, tile.Size.Height);
                    var imageSize = tile.Image.Size; if (imageSize.Width <= 0 || imageSize.Height <= 0) continue;
                    var crop = CollageComposer.CoverSrcRect((int)imageSize.Width, (int)imageSize.Height, (int)tile.Size.Width, (int)tile.Size.Height);
                    using (context.PushTransform(Matrix.CreateRotation(tile.Angle * Math.PI / 180) * Matrix.CreateTranslation(tile.Centre.X, tile.Centre.Y)))
                    using (context.PushOpacityMask(tile.Horizontal, rect))
                    using (context.PushOpacityMask(tile.Vertical, rect))
                        context.DrawImage(tile.Image, new Rect(crop.Left, crop.Top, crop.Width, crop.Height), rect);
                }
                context.DrawImage(finish, new Rect(Size));
            }
        }
    }
}
