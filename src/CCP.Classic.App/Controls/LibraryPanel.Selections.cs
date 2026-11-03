using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Controls;

public sealed partial class LibraryPanel
{
    private string? folderFilter;
    private readonly Dictionary<CheckBox, string> folderChecks = [];
    private bool refreshingFolders;
    private ComboBox selections = null!;
    private TextBox selectionName = null!;
    private bool selecting;
    public event Action<string>? FolderOpenRequested;

    private Control SelectionControls()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        selections = new() { Name = "LibrarySelection", MinWidth = 140, MaxWidth = 200, Margin = new(0, 0, 7, 0) };
        selectionName = new() { Name = "LibrarySelectionName", PlaceholderText = text["NewPreset"], MaxLength = 80, Width = 150, Margin = new(0, 0, 7, 0) };
        selections.SelectionChanged += (_, _) =>
        { if (!selecting && selections.SelectedItem is SelectionChoice item) selectionName.Text = item.Id is null ? "" : item.Name; };
        row.Children.Add(selections); row.Children.Add(selectionName);
        foreach (var (key, action) in new (string, Action)[]
        {
            ("Load", () =>
            {
                if (selections.SelectedItem is not SelectionChoice choice || !settings.Library.LoadSelection(choice.Id)) return;
                changed(); RenderPage(); RefreshFolderChecks(); message.Text = text["Saved"];
            }),
            ("Save", () => SaveSelection(false)), ("Update", () => SaveSelection(true)),
            ("Delete", () =>
            {
                if (selections.SelectedItem is not SelectionChoice { Id: { } id }) return;
                settings.Library.DeleteSelection(id); changed(); RefreshSelections();
            })
        })
        {
            var button = new Button { Name = "LibrarySelection" + key, Content = text[key], Padding = new(8, 4), Margin = new(0, 0, 5, 0) };
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                { message.Text = text["SaveFailed"]; }
            };
            row.Children.Add(button);
        }
        RefreshSelections(); return row;
    }
    private void SaveSelection(bool update)
    {
        if (string.IsNullOrWhiteSpace(selectionName.Text)) return;
        var id = update ? (selections.SelectedItem as SelectionChoice)?.Id : null;
        if (update && id is null) return;
        settings.Library.SaveSelection(selectionName.Text, id); changed(); RefreshSelections(); message.Text = text["Saved"];
    }
    private void RefreshSelections()
    {
        selecting = true;
        var list = new[] { new SelectionChoice(null, text["All"]) }.Concat(settings.Library.SelectionPresets.Select(preset => new SelectionChoice(preset.Id, preset.Name))).ToArray();
        selections.ItemsSource = list;
        selections.SelectedItem = list.FirstOrDefault(item => item.Id == settings.Library.SelectionPresetId) ?? list[0]; selecting = false;
    }
    private void RenderFolders(string[] images, string[] videos, string[] audio)
    {
        folders.Children.Clear(); folderChecks.Clear();
        folders.Children.Add(ActionButton("All", () => { folderFilter = null; pageIndex = 0; RenderPage(); }));
        var budget = 512;
        foreach (var category in new[] { "Images", "Videos", "Audio" })
        {
            folders.Children.Add(ActionButton(category, () => chooseFolder(category)));
            var roots = category == "Images" ? images : category == "Videos" ? videos : audio;
            var tree = new TreeView { Name = "LibraryTree_" + category };
            foreach (var root in roots)
            {
                if (budget <= 0) break;
                var directories = assets.Where(asset => asset.Kind == category && LibrarySettings.Within(asset.Path, root))
                    .Select(asset => Path.GetDirectoryName(asset.Path)!).Distinct().Take(512).ToArray();
                var nodes = new Dictionary<string, TreeViewItem>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                TreeViewItem? Node(string path)
                {
                    if (nodes.TryGetValue(path, out var node)) return node;
                    if (budget-- <= 0) return null;
                    node = new() { Header = FolderRow(path), IsExpanded = path == root };
                    nodes[path] = node;
                    if (path != root && Path.GetDirectoryName(path) is { } parent && LibrarySettings.Within(parent, root)) Node(parent)?.Items.Add(node);
                    else tree.Items.Add(node);
                    return node;
                }
                Node(root);
                foreach (var directory in directories.OrderBy(path => path.Length))
                { if (budget <= 0) break; Node(directory); }
            }
            folders.Children.Add(tree);
        }
        RefreshFolderChecks();
    }
    private Control FolderRow(string path)
    {
        var check = new CheckBox { Name = "LibraryFolderInclude", IsThreeState = true };
        folderChecks[check] = path;
        check.IsCheckedChanged += (_, _) =>
        {
            if (refreshingFolders || check.IsChecked is null) return;
            settings.Library.IncludeFolder(path, check.IsChecked == true); changed(); RenderPage(); RefreshFolderChecks();
        };
        var browse = new Button { Content = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), Padding = new(3, 2), MaxWidth = 85 };
        browse.Click += (_, _) => { folderFilter = path; pageIndex = 0; RenderPage(); };
        var open = new MenuItem { Header = text["OpenFolder"] };
        void Open()
        {
            try
            {
                var requested = FolderOpenRequested;
                if (!LocalFolderLauncher.Open(path, requested is null ? null : folder => requested(folder))) message.Text = text["PreviewUnavailable"];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { message.Text = text["PreviewUnavailable"]; }
        }
        open.Click += (_, _) => Open();
        browse.ContextMenu = new() { ItemsSource = new[] { open } }; ToolTip.SetTip(browse, path);
        var launch = new Button { Content = "↗", Name = "LibraryOpenFolder", Padding = new(3, 1) }; ToolTip.SetTip(launch, text["OpenFolder"]);
        launch.Click += (_, _) => Open();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 }; row.Children.Add(check); row.Children.Add(browse); row.Children.Add(launch); return row;
    }
    private void RefreshFolderChecks()
    {
        refreshingFolders = true;
        foreach (var (check, path) in folderChecks)
        {
            var files = assets.Where(asset => !asset.Starter && LibrarySettings.Within(asset.Path, path)).Select(asset => asset.Path).Distinct().ToArray();
            var selected = files.Count(settings.Library.Includes);
            check.IsChecked = files.Length == 0 ? settings.Library.Includes(path) : selected == 0 ? false : selected == files.Length ? true : null;
        }
        refreshingFolders = false;
    }
    private sealed record SelectionChoice(string? Id, string Name) { public override string ToString() => Name; }
}
