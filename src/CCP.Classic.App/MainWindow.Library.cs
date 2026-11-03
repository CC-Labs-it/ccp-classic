using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? libraryScan;

    private void FillLibrary(StackPanel content)
    {
        libraryScan?.Cancel(); libraryScan?.Dispose();
        libraryScan = new();
        var token = libraryScan.Token;
        foreach (var kind in new[] { "Images", "Videos", "Audio" })
        {
            var paths = LibraryFolders(kind);
            content.Children.Add(Row(kind, Button("Change", () => ChooseLibraryFolder(kind))));
            content.Children.Add(new TextBlock { Text = paths.Count > 0 ? string.Join("\n", paths) : text["NoFolder"],
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 });
        }
        content.Children.Add(Note("StarterPackReady"));
        content.Children.Add(Note("LibraryNextSession"));
        var files = new StackPanel { Spacing = 8 };
        content.Children.Add(files);
        _ = PopulateLibrary(files, token);
    }

    private List<string> LibraryFolders(string kind) => kind switch
    {
        "Images" => Settings.Library.ImageFolders,
        "Videos" => Settings.Library.VideoFolders,
        _ => Settings.Library.AudioFolders
    };

    private async void ChooseLibraryFolder(string kind)
    {
        try
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = text[kind], AllowMultiple = true });
            var paths = picked.Select(item => item.TryGetLocalPath()).OfType<string>()
                .Where(MediaLibrary.IsLocalPath).ToArray();
            if (paths.Length == 0) return;
            var folders = LibraryFolders(kind);
            folders.Clear(); folders.AddRange(paths);
            Settings.Library.Normalize(); SaveSettings();
            if (sheet == "Library") OpenSheet("Library");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        { SetStatus("SaveFailed"); }
    }

    private async Task PopulateLibrary(StackPanel target, CancellationToken token)
    {
        var library = Settings.Library;
        var images = library.ImageFolders.ToArray();
        var videos = library.VideoFolders.ToArray();
        var audio = library.AudioFolders.ToArray();
        try
        {
            var snapshot = await Task.Run(() => MediaLibrary.Scan(images, videos, audio,
                cancellationToken: token), token);
            if (token.IsCancellationRequested) return;
            foreach (var (key, paths) in new[] { ("Images", snapshot.Images), ("Videos", snapshot.Videos), ("Audio", snapshot.Audio) })
            {
                target.Children.Add(Label(text[key] + ": " + paths.Count));
                var list = new ListBox { ItemsSource = paths, Height = Math.Min(180, paths.Count * 30) };
                list.ItemTemplate = new FuncDataTemplate<string>((path, _) =>
                {
                    var check = new CheckBox { Content = Path.GetFileName(path), IsChecked = library.Includes(path),
                        MinHeight = 26 };
                    ToolTip.SetTip(check, path);
                    check.IsCheckedChanged += (_, _) => { library.Include(path, check.IsChecked == true); SaveSettings(); };
                    return check;
                });
                target.Children.Add(list);
            }
            if (snapshot.IsTruncated) target.Children.Add(Note("LibraryLimited"));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus("SaveFailed"); }
    }
}
