namespace CCP.Classic.Core;

public sealed class LibrarySelectionPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> ExcludedFiles { get; set; } = [];
    public List<string> IncludedFiles { get; set; } = [];
    public Dictionary<string, bool> Folders { get; set; } = [];
    public override string ToString() => Name;
}

// The original asset presets save selection rules only. New files obey the saved folder rules.
public sealed partial class LibrarySettings
{
    public List<string> IncludedFiles { get; set; } = [];
    public Dictionary<string, bool> FolderSelections { get; set; } = [];
    public List<LibrarySelectionPreset> SelectionPresets { get; set; } = [];
    public string? SelectionPresetId { get; set; }
    private void NormalizeSelections()
    {
        IncludedFiles = Local(IncludedFiles, 10000); FolderSelections = LocalFolders(FolderSelections);
        SelectionPresets = (SelectionPresets ?? []).Where(preset => preset is not null).Take(16).ToList();
        var ids = new HashSet<string>();
        foreach (var preset in SelectionPresets)
        {
            if (!Guid.TryParseExact(preset.Id, "N", out _) || !ids.Add(preset.Id)) { preset.Id = Guid.NewGuid().ToString("N"); ids.Add(preset.Id); }
            preset.Name = Name(preset.Name); preset.ExcludedFiles = Local(preset.ExcludedFiles, 10000);
            preset.IncludedFiles = Local(preset.IncludedFiles, 10000); preset.Folders = LocalFolders(preset.Folders);
        }
        if (!SelectionPresets.Any(preset => preset.Id == SelectionPresetId)) SelectionPresetId = null;
    }
    private bool IncludesSelection(string path) => !ExcludedFiles.Contains(path, PathComparer)
        && (IncludedFiles.Contains(path, PathComparer) || FolderIncluded(path));
    private bool FolderIncluded(string path) => FolderSelections.Where(rule => Within(path, rule.Key))
        .OrderByDescending(rule => rule.Key.Length).Select(rule => rule.Value).DefaultIfEmpty(true).First();
    public void IncludeFolder(string folder, bool included)
    {
        if (!MediaLibrary.IsLocalPath(folder)) return;
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (var child in FolderSelections.Keys.Where(path => Within(path, folder)).ToArray()) FolderSelections.Remove(child);
        FolderSelections[folder] = included;
        ExcludedFiles.RemoveAll(path => Within(path, folder)); IncludedFiles.RemoveAll(path => Within(path, folder));
    }
    public LibrarySelectionPreset SaveSelection(string name, string? id = null)
    {
        var preset = SelectionPresets.FirstOrDefault(item => item.Id == id);
        if (preset is null)
        {
            if (SelectionPresets.Count >= 16) throw new InvalidOperationException("Selection preset limit reached");
            preset = new(); SelectionPresets.Add(preset);
        }
        preset.Name = Name(name); preset.ExcludedFiles = Local(ExcludedFiles, 10000);
        preset.IncludedFiles = Local(IncludedFiles, 10000); preset.Folders = LocalFolders(FolderSelections);
        SelectionPresetId = preset.Id; return preset;
    }
    public bool LoadSelection(string? id)
    {
        if (id is null) { ExcludedFiles.Clear(); IncludedFiles.Clear(); FolderSelections.Clear(); SelectionPresetId = null; return true; }
        var preset = SelectionPresets.FirstOrDefault(item => item.Id == id); if (preset is null) return false;
        ExcludedFiles = Local(preset.ExcludedFiles, 10000); IncludedFiles = Local(preset.IncludedFiles, 10000);
        FolderSelections = LocalFolders(preset.Folders); SelectionPresetId = id; return true;
    }
    public void DeleteSelection(string id)
    { SelectionPresets.RemoveAll(item => item.Id == id); if (SelectionPresetId == id) SelectionPresetId = null; }
    public static bool Within(string path, string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(folder);
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return PathComparer.Equals(path, folder) || path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private static Dictionary<string, bool> LocalFolders(Dictionary<string, bool>? source) => (source ?? [])
        .Where(rule => MediaLibrary.IsLocalPath(rule.Key)).Take(2048).GroupBy(rule => Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Key)), PathComparer)
        .ToDictionary(group => group.Key, group => group.Last().Value, PathComparer);
    private static string Name(string? value) => string.IsNullOrWhiteSpace(value) ? "" : new string(value.Trim().Where(character => !char.IsControl(character)).Take(80).ToArray());
}
