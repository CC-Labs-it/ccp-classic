namespace CCP.Classic.App.Media;

// An open local handle and an exact demuxer keep native audio away from paths and playlists.
internal sealed class LocalAudioInput : IDisposable
{
    public FileStream Stream { get; }
    public string Demux { get; }
    public LocalAudioInput(string path)
    {
        var local = LocalPath(path) ?? throw new IOException("Audio must be an existing local file.");
        Stream = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> header = stackalloc byte[12];
            var read = Stream.Read(header); Stream.Position = 0;
            Demux = DetectDemux(header[..read], Path.GetExtension(local)) ?? throw new IOException("Unsupported audio container.");
        }
        catch { Stream.Dispose(); throw; }
    }
    public void Dispose() => Stream.Dispose();

    internal static string? LocalPath(string? value)
    {
        if (!CCP.Classic.Core.MediaLibrary.IsLocalPath(value)) return null;
        try
        {
            var full = Path.GetFullPath(value!);
            if (Path.GetExtension(full).ToLowerInvariant() is not (".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a" or ".aac" or ".opus")) return null;
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) return null;
            var ancestors = new Stack<string>();
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
            foreach (var ancestor in ancestors)
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) return null;
            return File.Exists(full) ? full : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return null; }
    }
    internal static string? DetectDemux(ReadOnlySpan<byte> header, string extension = ".mp3")
    {
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WAVE"u8)) return "wav";
        if (header.Length >= 4 && header[..4].SequenceEqual("OggS"u8)) return "ogg";
        if (header.Length >= 4 && header[..4].SequenceEqual("fLaC"u8)) return "flac";
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8)) return "mp4";
        if (header.Length >= 3 && header[..3].SequenceEqual("ID3"u8)) return extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ? "aac" : "mp3";
        if (header.Length >= 2 && header[0] == 0xff)
        {
            if ((header[1] & 0xf6) == 0xf0) return "aac";
            if ((header[1] & 0xe0) == 0xe0 && (header[1] & 6) != 0) return "mp3";
        }
        return null;
    }
}
