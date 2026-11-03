namespace CCP.Classic.Core;

public sealed partial class LibrarySettings
{
    public List<string> ImageFolders { get; set; } = [];
    public List<string> VideoFolders { get; set; } = [];
    public List<string> AudioFolders { get; set; } = [];
    public List<string> ExcludedFiles { get; set; } = [];

    public void Normalize()
    {
        ImageFolders = Local(ImageFolders, 32);
        VideoFolders = Local(VideoFolders, 32);
        AudioFolders = Local(AudioFolders, 32);
        ExcludedFiles = Local(ExcludedFiles, 10000);
        NormalizeSelections();
    }

    public bool Includes(string path) => IncludesSelection(path);
    public void Include(string path, bool included)
    {
        ExcludedFiles.RemoveAll(item => PathComparer.Equals(item, path));
        IncludedFiles.RemoveAll(item => PathComparer.Equals(item, path));
        if (!MediaLibrary.IsLocalPath(path)) return;
        if (!included) ExcludedFiles.Add(path);
        else if (!FolderIncluded(path)) IncludedFiles.Add(path);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static List<string> Local(IEnumerable<string>? paths, int limit) =>
        (paths ?? []).Where(MediaLibrary.IsLocalPath).Select(Path.GetFullPath)
            .Distinct(PathComparer).Take(limit).ToList();
}
