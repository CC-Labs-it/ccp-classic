using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

// The original separates audio rate from visual strength; the catalog keeps the audio rate.
public sealed class BrainDrainEditor : StackPanel
{
    private readonly ClassicSettings settings;
    private readonly LocalizationService text;
    private readonly Action changed;
    private readonly TextBlock folders = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { FontSize = 12 };
    public event Action? RefreshRequested;
    public BrainDrainEditor(ClassicSettings settings, LocalizationService text, Action changed)
    {
        this.settings = settings; this.text = text; this.changed = changed; Spacing = 10;
        var heading = new TextBlock { Text = text["st4_braindrain_blur_strength"], FontSize = 14, Foreground = Brush("#C9B5E2") };
        Children.Add(heading);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = settings.BrainDrain.BlurStrength, Name = "BrainDrainBlur" };
        var value = new TextBlock { Text = slider.Value + "%", FontSize = 16, Foreground = Brush("#8AE0E8"), HorizontalAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(slider, heading.Text);
        slider.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty) return;
            settings.BrainDrain.BlurStrength = (int)Math.Round(slider.Value); value.Text = settings.BrainDrain.BlurStrength + "%"; changed();
        };
        Children.Add(OptionHelp.Wrap(slider, "st4_braindrain_blur_strength", text)); Children.Add(value);
        Children.Add(Toggle("st4_braindrain_melt", "BrainDrainMelt", settings.BrainDrain.MeltEnabled,
            enabled => settings.BrainDrain.MeltEnabled = enabled));
        Children.Add(Toggle("st4_braindrain_allow_capture", "BrainDrainCapture", settings.BrainDrain.AllowCapture,
            enabled => settings.BrainDrain.AllowCapture = enabled));
        Children.Add(new TextBlock { Text = text["st4_braindrain_clip_library"], FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brush("#FF9BD5"), Margin = new(0, 6, 0, 0) });
        Children.Add(folders); RefreshFolders();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var pick = new Button { Content = text["Folders"], Name = "BrainDrainFolders" };
        pick.Click += async (_, _) => await PickFolders();
        var refresh = new Button { Content = text["Refresh"], Name = "BrainDrainRefresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        actions.Children.Add(OptionHelp.Wrap(pick, "st4_braindrain_clip_library", text)); actions.Children.Add(OptionHelp.Wrap(refresh, "Refresh", text)); Children.Add(actions); Children.Add(status);
        var sections = Children.ToArray(); Children.Clear();
        Children.Add(OptionLayout.Group(sections.Take(5), text["BrainDrain"], 0));
        Children.Add(OptionLayout.Group(sections.Skip(6), text["st4_braindrain_clip_library"], 2));
    }
    private Control Toggle(string key, string name, bool initial, Action<bool> set)
    {
        var toggle = new ToggleSwitch { Content = text[key], IsChecked = initial, Name = name, FontSize = 14 };
        toggle.IsCheckedChanged += (_, _) => { set(toggle.IsChecked == true); changed(); };
        var result = OptionHelp.Wrap(toggle, key, text); result.Classes.Add("option-compact"); return result;
    }
    private async Task PickFolders()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not { } window) return;
            var selected = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = text["Audio"], AllowMultiple = true });
            if (selected.Count == 0) return;
            settings.BrainDrain.AudioFolders = selected.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToList();
            settings.BrainDrain.Normalize(); RefreshFolders(); changed(); RefreshRequested?.Invoke(); status.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { status.Text = text["SaveFailed"]; }
    }
    private void RefreshFolders() => folders.Text = settings.BrainDrain.AudioFolders.Count == 0
        ? text["LibraryEmpty"] : string.Join("\n", settings.BrainDrain.AudioFolders.Select(Path.GetFileName));
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
}
