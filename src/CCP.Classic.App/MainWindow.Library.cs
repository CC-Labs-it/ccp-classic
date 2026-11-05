using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CCP.Classic.Core;
using CCP.Classic.App.Controls;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private LibraryPreviewWindow? libraryPreview;

    private void FillLibrary(StackPanel content)
    {
        var panel = new LibraryPanel(Settings, text, () => { SaveSettings(); effects?.RefreshSettings(); }, ChooseLibraryFolder);
        panel.RefreshRequested += () => effects?.ReloadLibrary();
        panel.PreviewRequested += asset =>
        {
            libraryPreview?.Close();
            var preview = new LibraryPreviewWindow(asset, Settings, text, ConfigureEffectInput);
            libraryPreview = preview;
            VisualPolish.ApplyWindow(preview, Settings);
            Session.StopRequested += preview.Close;
            preview.Closed += (_, _) => { Session.StopRequested -= preview.Close; if (ReferenceEquals(libraryPreview, preview)) libraryPreview = null; };
            preview.Show(this);
        };
        content.Children.Add(panel);
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
            Settings.Library.Normalize(); SaveSettings(); effects?.RefreshSettings();
            if (sheet == "Library") OpenSheet("Library");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        { SetStatus("SaveFailed"); }
    }

}
