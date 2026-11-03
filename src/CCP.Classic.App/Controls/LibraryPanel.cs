using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed record LibraryAsset(string Path, string Kind, bool Starter = false);

// CCP AssetsTabView's folder rail, thumbnail wall and checkbox overlays, with a bounded page.
public sealed partial class LibraryPanel : UserControl, IDisposable
{
    private readonly ClassicSettings settings;
    private readonly LocalizationService text;
    private readonly Action changed;
    private readonly Action<string> chooseFolder;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource pageStop = new();
    private readonly SemaphoreSlim decoders = new(2, 2);
    private readonly WrapPanel wall = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock counter = new(), message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "LibrarySearch", MinWidth = 180 };
    private readonly StackPanel folders = new() { Spacing = 9 };
    private readonly List<Bitmap> thumbnails = [];
    private readonly Dictionary<string, Bitmap> art = [];
    private readonly Button previous, next;
    private readonly StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private LibraryAsset[] assets = [];
    private string kind = "All";
    private int pageIndex, scanVersion;
    private bool disposed;
    public event Action<LibraryAsset>? PreviewRequested;
    public event Action? RefreshRequested;
    internal IReadOnlyList<LibraryAsset> Assets => assets;

    public LibraryPanel(ClassicSettings settings, LocalizationService text, Action changed, Action<string> chooseFolder)
    {
        this.settings = settings; this.text = text; this.changed = changed; this.chooseFolder = chooseFolder;
        Name = "LibraryManager"; Height = 570;
        foreach (var (key, file) in new[] { ("Images", "flash"), ("Videos", "video"), ("Audio", "audio") })
        {
            using var stream = AssetLoader.Open(new Uri($"avares://CCP.Classic.App/Assets/{file}.png"));
            art[key] = new Bitmap(stream);
        }
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12 }; Content = root;
        var toolbar = new Grid { ColumnDefinitions = new("*,220"), ColumnSpacing = 10 };
        foreach (var category in new[] { "All", "Images", "Videos", "Audio" })
        {
            var choice = new ToggleButton { Name = "LibraryFilter_" + category, Content = text[category], IsChecked = category == kind };
            choice.Classes.Add("pill");
            choice.Click += (_, _) =>
            {
                kind = category; pageIndex = 0;
                foreach (var button in filters.Children.OfType<ToggleButton>()) button.IsChecked = ReferenceEquals(button, choice);
                RenderPage();
            };
            filters.Children.Add(choice);
        }
        toolbar.Children.Add(filters); search.PlaceholderText = text["Search"]; AutomationProperties.SetName(search, text["Search"]);
        search.TextChanged += (_, _) => { pageIndex = 0; RenderPage(); };
        Grid.SetColumn(search, 1); toolbar.Children.Add(search); root.Children.Add(toolbar);
        var split = new Grid { ColumnDefinitions = new("174,*"), ColumnSpacing = 12 };
        var rail = new StackPanel { Spacing = 12 }; rail.Children.Add(Label(text["Folders"], 15));
        rail.Children.Add(new ScrollViewer { Content = folders, MaxHeight = 310, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var refresh = ActionButton("Refresh", () => { RefreshRequested?.Invoke(); _ = ReloadAsync(); }); refresh.Name = "LibraryRefresh"; rail.Children.Add(refresh);
        rail.Children.Add(new TextBlock { Text = text["LibraryNextSession"], Foreground = Brush("#9D9DB9"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var side = Plate(rail); split.Children.Add(side);
        var browser = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 8 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(ActionButton("SelectAll", () => SelectFiltered(true)));
        actions.Children.Add(ActionButton("DeselectAll", () => SelectFiltered(false)));
        browser.Children.Add(actions);
        var scroll = new ScrollViewer { Content = wall, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); browser.Children.Add(scroll);
        Grid.SetRow(message, 2); message.Foreground = Brush("#A0A0BC"); message.FontSize = 11; browser.Children.Add(message);
        var plate = Plate(browser); Grid.SetColumn(plate, 1); split.Children.Add(plate); Grid.SetRow(split, 1); root.Children.Add(split);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 10 };
        counter.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(counter);
        previous = ActionButton("Previous", () => { pageIndex--; RenderPage(); }); next = ActionButton("Next", () => { pageIndex++; RenderPage(); });
        Grid.SetColumn(previous, 1); Grid.SetColumn(next, 2); footer.Children.Add(previous); footer.Children.Add(next);
        var selections = SelectionControls(); Grid.SetRow(selections, 1); Grid.SetColumnSpan(selections, 3); footer.Children.Add(selections);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (disposed) return;
        var version = ++scanVersion; var cancellation = lifetime.Token;
        var library = settings.Library;
        var images = library.ImageFolders.ToArray(); var videos = library.VideoFolders.ToArray(); var audio = library.AudioFolders.ToArray();
        folders.Children.Clear();
        foreach (var category in new[] { "Images", "Videos", "Audio" })
        {
            var paths = category == "Images" ? images : category == "Videos" ? videos : audio;
            folders.Children.Add(ActionButton(category, () => chooseFolder(category)));
            foreach (var path in paths)
            {
                var name = Label(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), 11);
                name.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(name, path); folders.Children.Add(name);
            }
        }
        message.Text = text["Loading"];
        try
        {
            var snapshot = await Task.Run(() => MediaLibrary.Scan(images, videos, audio, cancellationToken: cancellation), cancellation);
            if (disposed || version != scanVersion) return;
            var starter = new StarterPack();
            assets = snapshot.Images.Select(path => new LibraryAsset(path, "Images"))
                .Concat(snapshot.Videos.Select(path => new LibraryAsset(path, "Videos")))
                .Concat(snapshot.Audio.Select(path => new LibraryAsset(path, "Audio")))
                .Concat(images.Length == 0 ? starter.Images.Select(path => new LibraryAsset(path, "Images", true)) : [])
                .Concat(audio.Length == 0 ? starter.Phrases.Where(word => word.AudioPath is not null).Select(word => new LibraryAsset(word.AudioPath!, "Audio", true)) : []).ToArray();
            pageIndex = 0; RenderPage();
            RenderFolders(images, videos, audio);
            if (snapshot.IsTruncated) message.Text = text["LibraryLimited"];
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (!disposed) message.Text = text["SaveFailed"]; }
    }
    private LibraryAsset[] Filtered() => assets.Where(asset => (kind == "All" || kind == asset.Kind)
        && (folderFilter is null || LibrarySettings.Within(asset.Path, folderFilter))
        && Path.GetFileName(asset.Path).Contains(search.Text?.Trim() ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
    private void SelectFiltered(bool selected)
    {
        foreach (var asset in Filtered().Where(asset => !asset.Starter)) settings.Library.Include(asset.Path, selected);
        changed(); RenderPage();
        RefreshFolderChecks();
    }
    private void RenderPage()
    {
        if (disposed) return;
        pageStop.Cancel(); pageStop.Dispose(); pageStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        wall.Children.Clear(); foreach (var bitmap in thumbnails) bitmap.Dispose(); thumbnails.Clear();
        var filtered = Filtered(); pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, (filtered.Length - 1) / 12));
        foreach (var asset in filtered.Skip(pageIndex * 12).Take(12)) wall.Children.Add(Tile(asset, pageStop.Token));
        counter.Text = $"{text["Library"]}  ·  {filtered.Length}  ·  {text["Page"]} {pageIndex + 1} / {Math.Max(1, (filtered.Length + 11) / 12)}";
        previous.IsEnabled = pageIndex > 0; next.IsEnabled = (pageIndex + 1) * 12 < filtered.Length;
        message.Text = filtered.Length == 0 ? text["LibraryEmpty"] : text["LibraryPreviewHint"];
    }
    private Control Tile(LibraryAsset asset, CancellationToken token)
    {
        var image = new Image { Source = art[asset.Kind], Stretch = Stretch.UniformToFill, Height = 98 };
        var body = new Grid { RowDefinitions = new("98,Auto") };
        body.Children.Add(new Border { Child = image, ClipToBounds = true, CornerRadius = new(8, 8, 0, 0) });
        var caption = new StackPanel { Spacing = 2, Margin = new(7, 5) };
        caption.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(asset.Path), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        caption.Children.Add(new TextBlock { Text = asset.Starter ? text["StarterPack"] : Path.GetExtension(asset.Path).TrimStart('.').ToUpperInvariant(), Foreground = Brush("#B9A6CE"), FontSize = 10 });
        Grid.SetRow(caption, 1); body.Children.Add(caption);
        var button = new Button { Content = body, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush("#303050"), BorderBrush = Brush("#55436A"), CornerRadius = new(9), ClipToBounds = true };
        button.Classes.Add("media-card"); AutomationProperties.SetName(button, text["Preview"] + " " + Path.GetFileName(asset.Path));
        VisualPolish.AttachHover(button, settings, 1.025);
        button.Click += (_, _) => PreviewRequested?.Invoke(asset); ToolTip.SetTip(button, asset.Path);
        var tile = new Grid { Width = 140, Height = 148, Margin = new(0, 0, 9, 9) }; tile.Children.Add(button);
        var check = new CheckBox { Name = "LibraryInclude", IsChecked = asset.Starter || settings.Library.Includes(asset.Path),
            IsEnabled = !asset.Starter, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(6, 2) };
        AutomationProperties.SetName(check, text["Included"] + " " + Path.GetFileName(asset.Path));
        image.Opacity = check.IsChecked == true ? 1 : .35;
        check.IsCheckedChanged += (_, _) => { settings.Library.Include(asset.Path, check.IsChecked == true); image.Opacity = check.IsChecked == true ? 1 : .35; changed(); RefreshFolderChecks(); };
        tile.Children.Add(check);
        if (asset.Kind == "Images") _ = LoadImage(asset.Path, image, token);
        return tile;
    }
    private async Task LoadImage(string path, Image target, CancellationToken token)
    {
        try
        {
            await decoders.WaitAsync(token);
            Bitmap bitmap;
            try { bitmap = await Task.Run(() => { token.ThrowIfCancellationRequested(); return LocalImageLibrary.DecodeThumbnail(path); }, token); }
            finally { decoders.Release(); }
            if (disposed || token.IsCancellationRequested) { bitmap.Dispose(); return; }
            thumbnails.Add(bitmap); target.Source = bitmap;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ArgumentException or UnauthorizedAccessException) { }
    }
    private Button ActionButton(string key, Action action)
    { var button = new Button { Content = text[key], Padding = new(10, 5) }; button.Classes.Add("pill"); button.Click += (_, _) => action(); return button; }
    private static TextBlock Label(string value, double size) => new() { Text = value, FontSize = size };
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
    private static Border Plate(Control child)
    { var result = new Border { Child = child, Padding = new(10), CornerRadius = new(12) }; result.Classes.Add("card"); return result; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); pageStop.Cancel();
        wall.Children.Clear(); foreach (var bitmap in thumbnails) bitmap.Dispose(); thumbnails.Clear();
        foreach (var bitmap in art.Values) bitmap.Dispose(); art.Clear(); pageStop.Dispose(); lifetime.Dispose();
    }
}
