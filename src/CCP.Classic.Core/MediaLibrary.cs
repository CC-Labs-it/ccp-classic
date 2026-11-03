namespace CCP.Classic.Core;

public sealed class MediaLibrarySnapshot
{
    public IReadOnlyList<string> Images { get; }
    public IReadOnlyList<string> Videos { get; }
    public IReadOnlyList<string> Audio { get; }
    public bool IsTruncated { get; }

    internal MediaLibrarySnapshot(IEnumerable<string> images, IEnumerable<string> videos,
        IEnumerable<string> audio, bool isTruncated)
    {
        Images = Freeze(images); Videos = Freeze(videos); Audio = Freeze(audio);
        IsTruncated = isTruncated;
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> paths) =>
        Array.AsReadOnly(paths.Order(StringComparer.Ordinal).ToArray());
}

// Run scans on a worker. Snapshots can then be shared with the UI without a watcher.
public static class MediaLibrary
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".mpeg", ".mpg" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".opus" };

    public static MediaLibrarySnapshot Scan(IEnumerable<string>? imageDirectories = null,
        IEnumerable<string>? videoDirectories = null, IEnumerable<string>? audioDirectories = null,
        int maximumFiles = 10000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFiles, 100000);
        cancellationToken.ThrowIfCancellationRequested();
        var budget = new ScanBudget(maximumFiles);
        var images = ScanKind(imageDirectories, ImageExtensions, budget, cancellationToken);
        var videos = ScanKind(videoDirectories, VideoExtensions, budget, cancellationToken);
        var audio = ScanKind(audioDirectories, AudioExtensions, budget, cancellationToken);
        return new(images, videos, audio, budget.Truncated);
    }

    public static bool IsLocalPath(string? path) => TryCanonicalPath(path, out _);

    private static bool TryCanonicalPath(string? path, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\0')) return false;
        // Colon is allowed only in a Windows drive prefix. This also excludes streams and URIs.
        var colon = path.IndexOf(':');
        if (colon >= 0 && (!OperatingSystem.IsWindows() || colon != 1 || !char.IsAsciiLetter(path[0])
            || path.IndexOf(':', colon + 1) >= 0)) return false;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        { return false; }
    }

    private static HashSet<string> ScanKind(IEnumerable<string>? roots, HashSet<string> extensions,
        ScanBudget budget, CancellationToken token)
    {
        var found = new HashSet<string>(PathComparer);
        if (roots is null || budget.Truncated) return found;
        var visited = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (!TryCanonicalPath(root, out var path) || !LocalDirectory(path)) continue;
            var pending = new Stack<string>(); pending.Push(path);
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                if (!visited.Add(directory)) continue;
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!budget.Visit()) return found;
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                            if (!extensions.Contains(Path.GetExtension(entry))) continue;
                            var fullPath = Path.GetFullPath(entry);
                            if (found.Contains(fullPath)) continue;
                            if (!budget.AddFile()) return found;
                            found.Add(fullPath);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return found;
    }

    private static bool LocalDirectory(string path)
    {
        // Inspect ancestors from the volume down, before following a junction into a selected root.
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            ancestors.Push(current);
        try
        {
            foreach (var ancestor in ancestors)
            {
                var attributes = File.GetAttributes(ancestor);
                if ((attributes & FileAttributes.ReparsePoint) != 0
                    || (attributes & FileAttributes.Directory) == 0) return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed class ScanBudget(int maximumFiles)
    {
        private int files, entries;
        public bool Truncated { get; private set; }
        public bool Visit()
        {
            if (++entries <= Math.Max(100, maximumFiles * 20)) return true;
            Truncated = true; return false;
        }
        public bool AddFile()
        {
            if (files < maximumFiles) { files++; return true; }
            Truncated = true; return false;
        }
    }
}
