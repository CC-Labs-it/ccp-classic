using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LibrarySelectionTests
{
    [Fact]
    public void FolderRulesApplyToNewFilesWithSpecificFolderAndFileOverrides()
    {
        using var fixture = new Fixture(); var settings = new LibrarySettings();
        settings.IncludeFolder(fixture.Root, false); Assert.False(settings.Includes(fixture.Image));
        Assert.False(settings.Includes(Path.Combine(fixture.Root, "future.gif")));
        settings.IncludeFolder(fixture.Nested, true); Assert.True(settings.Includes(fixture.Image));
        settings.Include(fixture.Image, false); Assert.False(settings.Includes(fixture.Image));
        settings.IncludeFolder(fixture.Root, false); settings.Include(fixture.Image, true);
        Assert.True(settings.Includes(fixture.Image)); Assert.False(settings.Includes(Path.Combine(fixture.Nested, "future.gif")));
        Assert.True(settings.Includes(fixture.Root + "-different/image.gif"));
        Assert.True(LibrarySettings.Within(fixture.Root, Path.GetPathRoot(fixture.Root)!));
        settings.IncludeFolder(fixture.Root, true); Assert.True(settings.Includes(fixture.Image)); Assert.Empty(settings.IncludedFiles);
    }

    [Fact]
    public void SelectionPresetsRoundTripLocallyWithoutChangingFoldersOrSharingRules()
    {
        using var fixture = new Fixture(); var settings = new ClassicSettings(); settings.Library.ImageFolders = [fixture.Root];
        settings.Library.IncludeFolder(fixture.Nested, false); var saved = settings.Library.SaveSelection("  Focus set  ");
        settings.Library.IncludeFolder(fixture.Nested, true); Assert.False(saved.Folders[fixture.Nested]);
        settings.Library.LoadSelection(saved.Id); Assert.False(settings.Library.Includes(fixture.Image));
        var store = new SettingsStore(Path.Combine(fixture.Root, "settings.json")); store.Save(settings);
        var loaded = store.Load(out var recovered); Assert.False(recovered);
        var preset = Assert.Single(loaded.Library.SelectionPresets); Assert.Equal("Focus set", preset.Name);
        Assert.False(loaded.Library.Includes(fixture.Image)); Assert.Equal([fixture.Root], loaded.Library.ImageFolders);
        loaded.Library.LoadSelection(null); Assert.True(loaded.Library.Includes(fixture.Image)); Assert.False(preset.Folders[fixture.Nested]);
        Assert.False(loaded.Library.LoadSelection("../../outside")); loaded.Library.DeleteSelection(saved.Id); Assert.Empty(loaded.Library.SelectionPresets);
    }

    [Fact]
    public void LocalFolderOpenRejectsRemoteInputsAndOnlyInvokesTheInjectedLauncher()
    {
        using var fixture = new Fixture(); var launched = new List<string>();
        Assert.True(LocalFolderLauncher.Open(fixture.Root, launched.Add)); Assert.Equal([fixture.Root], launched);
        foreach (var invalid in new[] { "https://example.invalid/a", "\\\\server\\share", "relative", fixture.Image })
            Assert.False(LocalFolderLauncher.Open(invalid, launched.Add));
        Assert.Single(launched);
    }

    [Fact]
    public async Task FolderTreeAndSavedSelectionsUseTheVisibleControlsWithoutOpeningExplorer()
    {
        using var fixture = new Fixture(); LibraryPanel panel = null!; Window window = null!;
        var settings = new ClassicSettings(); settings.Library.ImageFolders = [fixture.Root];
        var opened = new List<string>(); var changes = 0;
        await UiDispatcher.Run(() =>
        {
            panel = new(settings, new LocalizationService("en"), () => changes++, _ => { }); panel.FolderOpenRequested += opened.Add;
            window = new() { Width = 1000, Height = 700, Content = panel }; window.Show();
        });
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var ready = false; await UiDispatcher.Run(() => ready = panel.Assets.Any(asset => asset.Path == fixture.Image));
                if (ready) break; await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await UiDispatcher.Run(() =>
            {
                var checks = panel.GetLogicalDescendants().OfType<CheckBox>().Where(check => check.Name == "LibraryFolderInclude").ToArray();
                Assert.True(checks.Length >= 2); checks[0].IsChecked = false;
                Assert.False(settings.Library.Includes(fixture.Image));
                Named<TextBox>(panel, "LibrarySelectionName").Text = "Images off";
                Click(panel, "LibrarySelectionSave"); Assert.Single(settings.Library.SelectionPresets);
                checks[0].IsChecked = true; Assert.True(settings.Library.Includes(fixture.Image));
                Click(panel, "LibrarySelectionLoad"); Assert.False(settings.Library.Includes(fixture.Image));
                var open = panel.GetLogicalDescendants().OfType<Button>().First(button => button.Name == "LibraryOpenFolder");
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal([fixture.Root], opened);
                Click(panel, "LibrarySelectionDelete"); Assert.Empty(settings.Library.SelectionPresets); Assert.True(changes >= 5);
            });
        }
        finally
        {
            await UiDispatcher.Run(() => { window.Close(); panel.Dispose(); });
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Control root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Nested { get; }
        public string Image { get; }
        public Fixture()
        {
            Nested = Path.Combine(Root, "nested"); Directory.CreateDirectory(Nested); Image = Path.Combine(Nested, "test.gif");
            File.WriteAllBytes(Image, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        }
        public void Dispose()
        {
            var deadline = Environment.TickCount64 + 5000;
            while (true)
                try { Directory.Delete(Root, true); return; }
                catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(20); }
        }
    }
}
