using Avalonia.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

// Pinned CCP OverlayService picks once per overlay/session start, without consecutive repeats.
// Pools contain only explicitly selected folders, never siblings of an individually selected file.
internal sealed class SpiralMediaLibrary(ClassicSettings settings, IImage fallback, string? starterRoot = null, int seed = 0) : IDisposable
{
    private readonly Random random = new(seed);
    private readonly string root = starterRoot ?? Path.Combine(AppContext.BaseDirectory, "Starter", "Spirals");
    private CancellationTokenSource? stop;
    private Task<MediaLibrarySnapshot>? scan;
    private LocalImageLibrary? images;
    private string[] paths = [], pool = [], scannedFolders = [];
    private string? chosen, previous, configured;
    private bool running, previousEnabled, previousRandom, scanned;
    internal IImage? MainImage { get; private set; }
    internal IReadOnlyList<IImage?> CornerImages { get; private set; } = new IImage?[2];
    internal string? SelectedPath => chosen;
    internal int CachedCount => images?.CachedCount ?? 0;
    internal Task Completion => images?.Completion ?? Task.CompletedTask;
    internal Task ScanCompletion => scan ?? Task.CompletedTask;
    internal void Start()
    {
        Stop(); running = true; previousEnabled = false; configured = chosen = null;
        scannedFolders = settings.Spiral.Folders.Count > 0 ? settings.Spiral.Folders.ToArray() : [SpiralOptions.DefaultFolder];
        stop = new(); var token = stop.Token; var folders = scannedFolders;
        scan = Task.Run(() => MediaLibrary.Scan(imageDirectories: folders, cancellationToken: token), token);
    }
    internal void Update(double elapsed)
    {
        if (!running) return;
        var options = settings.Spiral;
        var enabled = settings.Effects["spiral"].Enabled;
        var poolArrived = false;
        if (!scanned && scan?.IsCompleted == true)
        {
            scanned = true;
            if (scan.IsCompletedSuccessfully) pool = scan.Result.Images.ToArray();
            else if (scan.IsFaulted) _ = scan.Exception;
            poolArrived = true;
        }
        if (configured != options.Path || previousRandom != options.Randomize || enabled && !previousEnabled || options.Randomize && poolArrived)
        {
            configured = options.Path; previousRandom = options.Randomize;
            chosen = options.Randomize && scanned ? Pick(pool, previous, random) : options.Path;
            if (chosen is not null) previous = chosen;
        }
        previousEnabled = enabled;
        var main = chosen ?? options.Path ?? Path.Combine(root, "spiral.gif");
        var corners = options.Corners.Take(2).Select(slot => slot.Path ?? options.Path ?? Path.Combine(root, "spiral_corner.gif")).ToArray();
        var wanted = new[] { main }.Concat(corners).Where(MediaLibrary.IsLocalPath).Distinct(PathComparer).ToArray();
        if (!paths.SequenceEqual(wanted, PathComparer))
        {
            images?.Dispose(); paths = wanted; images = new(paths, fallback);
        }
        images?.AdvanceAnimations(elapsed, !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode);
        MainImage = Find(main);
        CornerImages = corners.Select(Find).ToArray();
        if (enabled && MainImage is { } selected) _ = selected.Size;
        for (var index = 0; index < CornerImages.Count; index++)
            if (options.Corners[index].Enabled && CornerImages[index] is { } image) _ = image.Size;
    }
    private IImage? Find(string path)
    {
        var index = Array.FindIndex(paths, value => PathComparer.Equals(path, value));
        return index >= 0 && images is not null ? images[index] : null;
    }
    internal static string? Pick(IReadOnlyList<string> pool, string? previous, Random random)
    {
        var choices = pool.Where(path => pool.Count == 1 || !PathComparer.Equals(path, previous)).ToArray();
        return choices.Length == 0 ? null : choices[random.Next(choices.Length)];
    }
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal void Stop()
    {
        running = false; stop?.Cancel(); stop?.Dispose(); stop = null;
        images?.Dispose(); images = null; paths = pool = []; scan = null; scanned = false;
        MainImage = null; CornerImages = new IImage?[2];
    }
    public void Dispose() => Stop();
}
