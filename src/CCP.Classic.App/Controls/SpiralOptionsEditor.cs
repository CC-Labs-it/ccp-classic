using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class SpiralOptionsEditor : OptionEditor
{
    private readonly ClassicSettings settings;
    private readonly ComboBox library = new() { Name = "SpiralLibrary", MinWidth = 190, MaxWidth = 290 };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
    private readonly CancellationTokenSource stop = new();
    private string?[] libraryPaths = [];
    private int scanVersion;
    private bool rebuilding;
    public event Action? ReloadRequested;
    public SpiralOptionsEditor(ClassicSettings settings, LocalizationService text, Action changed) : base(text, changed)
    {
        this.settings = settings; Spacing = 8;
        Toggle("setting_spiral_randomize", settings.Spiral.Randomize, value => { settings.Spiral.Randomize = value; ReloadRequested?.Invoke(); });
        var monitors = new ComboBox { Name = "SpiralMonitor", MinWidth = 170 };
        void Monitors()
        {
            var screens = TopLevel.GetTopLevel(this)?.Screens?.All;
            var values = new[] { Text["FollowDisplay"], Text["AllMonitors"] }.Concat(Enumerable.Range(0, screens?.Count ?? 0).Select(index => Text["Monitor"] + " " + (index + 1))).ToArray();
            monitors.ItemsSource = values;
            monitors.SelectedIndex = settings.Spiral.TargetMonitor == -1 ? 0 : settings.Spiral.TargetMonitor == -2 ? 1
                : settings.Spiral.TargetMonitor + 2 < values.Length ? settings.Spiral.TargetMonitor + 2 : 0;
        }
        monitors.DropDownOpened += (_, _) => Monitors(); Monitors();
        monitors.SelectionChanged += (_, _) =>
        { if (monitors.SelectedIndex < 0) return; settings.Spiral.TargetMonitor = monitors.SelectedIndex == 0 ? -1 : monitors.SelectedIndex == 1 ? -2 : monitors.SelectedIndex - 2; Changed(); };
        Children.Add(Row("TargetMonitor", monitors)); Heading("SpiralLibrary"); Children.Add(library);
        library.SelectionChanged += (_, _) =>
        {
            if (rebuilding || library.SelectedIndex < 0 || library.SelectedIndex >= libraryPaths.Length) return;
            settings.Spiral.Path = libraryPaths[library.SelectedIndex]; settings.Spiral.UseProcedural = false;
            settings.CustomSpiralId = null; Changed(); ReloadRequested?.Invoke();
        };
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Action("SelectGif", async () => { var path = await PickImage(); if (path is null) return; settings.Spiral.Path = path;
            settings.Spiral.UseProcedural = false; settings.CustomSpiralId = null; Changed(); ReloadRequested?.Invoke(); await Reload(); }));
        tools.Children.Add(Action("Folders", async () =>
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return;
            var selected = await provider.OpenFolderPickerAsync(new() { Title = Text["SpiralLibrary"], AllowMultiple = true });
            if (selected.Count == 0) return;
            settings.Spiral.Folders = selected.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToList();
            settings.Spiral.Normalize(); Changed(); ReloadRequested?.Invoke(); await Reload();
        }));
        tools.Children.Add(Action("Refresh", async () => { await Reload(); ReloadRequested?.Invoke(); }));
        Children.Add(tools); Children.Add(status);
        for (var index = 0; index < 2; index++) Corner(index);
        DetachedFromVisualTree += (_, _) => stop.Cancel();
        _ = Reload();
    }
    private void Corner(int index)
    {
        var slot = settings.Spiral.Corners[index]; Heading("CornerGif");
        Toggle(index == 0 ? "CornerGifOne" : "CornerGifTwo", slot.Enabled, value => slot.Enabled = value);
        var pick = Action("SelectGif", async () => { var path = await PickImage(); if (path is not null) { slot.Path = path; Changed(); } });
        Children.Add(pick);
        var reset = Action("SystemDefault", () => { slot.Path = null; Changed(); return Task.CompletedTask; }); Children.Add(reset);
        var positions = new ComboBox { Name = "CornerPosition" + index, ItemsSource = new[] { Text["TopLeft"], Text["TopRight"], Text["BottomLeft"], Text["BottomRight"] }, SelectedIndex = (int)slot.Position, MinWidth = 170 };
        positions.SelectionChanged += (_, _) => { if (positions.SelectedIndex >= 0) { slot.Position = (SpiralCorner)positions.SelectedIndex; Changed(); } };
        Children.Add(Row("Position", positions));
        Number("Size", slot.Size, 100, 800, value => slot.Size = (int)value);
        Number("Opacity", slot.Opacity, 5, 100, value => slot.Opacity = (int)value);
    }
    private Button Action(string key, Func<Task> action)
    {
        var button = new Button { Content = Text[key], Margin = new(0, 0, 6, 5) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException)
            { if (!stop.IsCancellationRequested) status.Text = Text["PreviewUnavailable"]; }
        };
        return button;
    }
    private async Task<string?> PickImage()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return null;
        var picked = await provider.OpenFilePickerAsync(new() { Title = Text["SelectGif"], AllowMultiple = false,
            FileTypeFilter = [new(Text["Images"]) { Patterns = ["*.gif", "*.webp", "*.png", "*.jpg", "*.jpeg", "*.bmp"] }] });
        return LocalImageLibrary.LocalPath(picked.FirstOrDefault()?.TryGetLocalPath());
    }
    private async Task Reload()
    {
        var version = ++scanVersion; var token = stop.Token;
        try
        {
            var roots = settings.Spiral.Folders.Count > 0 ? settings.Spiral.Folders.ToArray() : [SpiralOptions.DefaultFolder];
            var snapshot = await Task.Run(() => MediaLibrary.Scan(imageDirectories: roots, cancellationToken: token), token);
            if (token.IsCancellationRequested || version != scanVersion) return;
            rebuilding = true;
            libraryPaths = new string?[] { null }.Concat(settings.Spiral.Path is { } path ? [path] : Array.Empty<string>())
                .Concat(snapshot.Images).Distinct().Take(1001).ToArray();
            library.ItemsSource = libraryPaths.Select(path => path is null ? Text["SystemDefault"] : Path.GetFileName(path)).ToArray();
            library.SelectedIndex = Array.IndexOf(libraryPaths, settings.Spiral.Path); rebuilding = false;
            status.Text = snapshot.IsTruncated ? Text["LibraryLimited"] : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) status.Text = Text["PreviewUnavailable"]; }
    }
}
