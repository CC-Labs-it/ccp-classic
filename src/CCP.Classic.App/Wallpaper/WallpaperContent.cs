using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Wallpaper;

internal sealed record WallpaperChoice(string? SourceFolder, string? OwnImage, bool Collage, int LayoutMask, string Accent, string Background)
{
    public static WallpaperChoice From(WallpaperOptions options) => new(options.SourceFolder, options.OwnImagePath,
        options.Collage, options.LayoutMask, options.Accent, options.Background);
}

internal sealed class WallpaperContent
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CCP Classic", "wallpaper-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> generated = [];
    public Task<string?> SelectAsync(bool dynamic, WallpaperMedia media, WallpaperChoice options,
        IReadOnlyList<WallpaperMonitor> monitors, string? previous, CancellationToken token)
    {
        var pool = dynamic ? media.Videos.ToArray() : media.Images.ToArray();
        if (!dynamic && !string.IsNullOrWhiteSpace(options.OwnImage))
        {
            if (Path.GetExtension(options.OwnImage).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp")
                return Task.FromResult<string?>(options.OwnImage);
            return Task.Run<string?>(() =>
            {
                token.ThrowIfCancellationRequested();
                using var image = CollageComposer.DecodeCapped(options.OwnImage, 3840, out _);
                if (image is null) return null;
                Directory.CreateDirectory(directory); var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using (var file = File.Create(path)) encoded.SaveTo(file);
                lock (generated) generated.Add(path);
                return path;
            }, token);
        }
        if (!dynamic && options.Collage)
            return Task.Run(() =>
            {
                var layouts = Enum.GetValues<CollageLayout>().Where(layout => (options.LayoutMask & (1 << (int)layout)) != 0).ToArray();
                var layout = layouts.Length == 0 ? CollageLayout.SideHero : layouts[Random.Shared.Next(layouts.Length)];
                var area = monitors[0].Bounds;
                var path = CollageComposer.Compose(pool, directory, options.Accent, options.Background, layout,
                    area.Width, area.Height, token);
                if (path is not null) { lock (generated) generated.Add(path); }
                return path;
            }, token);
        var candidates = pool.Where(path => path != previous).ToArray();
        return Task.FromResult(candidates.Length > 0 ? candidates[Random.Shared.Next(candidates.Length)] : pool.FirstOrDefault());
    }
    public void ClearExcept(string? retained)
    {
        lock (generated)
        {
            foreach (var path in generated.Where(path => path != retained).ToArray())
            {
                try { File.Delete(path); generated.Remove(path); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
