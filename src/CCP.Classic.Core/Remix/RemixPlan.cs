namespace CCP.Classic.Core.Remix;

public sealed class RemixRollState
{
    public bool Carried { get; private set; }
    public int Seed { get; private set; }
    public bool Take(bool eligible, bool hit, bool ready, int seed)
    {
        if (!eligible || !hit && !Carried) return false;
        if (ready) { Carried = false; return true; }
        if (!Carried) { Carried = true; Seed = seed; }
        return false;
    }
    public void Clear() { Carried = false; Seed = 0; }
}
public static class RemixPlan
{
    public const long MaximumSourceBytes = 12 * 1024 * 1024;
    public static IReadOnlyList<string> SelectSources(IEnumerable<string> paths, int seed, Func<string, long> sizeOf)
    {
        var gifs = paths.Where(MediaLibrary.IsLocalPath).Where(path => Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100000).ToArray();
        var random = new Random(seed);
        for (var i = gifs.Length - 1; i > 0; i--) { var j = random.Next(i + 1); (gifs[i], gifs[j]) = (gifs[j], gifs[i]); }
        var usable = gifs.Where(path => { try { var size = sizeOf(path); return size is >= 0 and <= MaximumSourceBytes; } catch { return false; } }).Take(8).ToArray();
        return usable.Length == 0 ? [] : Enumerable.Range(0, 8).Select(index => usable[index % usable.Length]).ToArray();
    }
    public static (double X, double Y, double Width, double Height) Centered(double width, double height, double sourceWidth, double sourceHeight)
    {
        if (width <= 0 || height <= 0 || sourceWidth <= 0 || sourceHeight <= 0) return (0, 0, Math.Max(1, width), Math.Max(1, height));
        var scale = Math.Min(Math.Min(width, height) * .65 / Math.Min(sourceWidth, sourceHeight),
            Math.Min(Math.Max(1, width - 48) / sourceWidth, Math.Max(1, height - 48) / sourceHeight));
        var w = Math.Max(1, RemixScene.Round(sourceWidth * scale)); var h = Math.Max(1, RemixScene.Round(sourceHeight * scale));
        return ((int)((width - w) / 2), (int)((height - h) / 2), w, h);
    }
}
