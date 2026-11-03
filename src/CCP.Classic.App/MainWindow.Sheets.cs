using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    public void OpenSheet(string key)
    {
        capturePanic = false;
        sheet = key;
        var content = new StackPanel { Spacing = 10, Margin = new(14, 12) };
        switch (key)
        {
            case "Settings": FillSettings(content); break;
            case "Library":
                FillLibrary(content);
                break;
            case "Companion":
                FillCompanion(content);
                break;
            case "Toys":
                foreach (var provider in new[] { "Intiface", "LovenseConnect" })
                    content.Children.Add(Row(provider, PendingButton("Connect"), "NotConnected"));
                content.Children.Add(Note("LocalhostOnly"));
                content.Children.Add(Note("ToysPending"));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
        ShowSettingsWindow(key, content);
    }

    public void OpenEffect(string id)
    {
        capturePanic = false;
        var definition = EffectCatalog.All.Single(d => d.Id == id && (!d.WindowsOnly || windows));
        sheet = null;
        var content = new StackPanel { Spacing = 12, Margin = new(14, 12) };
        content.Children.Add(new EffectCard(definition, Settings.Effects[id], text, () =>
        {
            foreach (var tile in this.GetVisualDescendants().OfType<EffectTile>()) tile.Refresh();
        }));
        content.Children.Add(Note(DesktopEffectsService.IsPlayable(id) ? "LiveSettings" : "Unavailable"));
        ShowSettingsWindow(definition.Title, content);
    }

    private void ShowSettingsWindow(string key, Control content)
    {
        SettingsWindow?.Close();
        var frame = new Grid { RowDefinitions = new("44,*,44") };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(14, 0) };
        var title = Label(text[key], 14); title.Name = "SheetTitle"; title.FontWeight = FontWeight.SemiBold;
        header.Children.Add(title);
        var close = Button("Close", CloseSheet); close.Name = "SheetClose";
        Grid.SetColumn(close, 1); header.Children.Add(close); frame.Children.Add(header);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); frame.Children.Add(scroll);
        var credit = Label(text["ByCredit"], 11); credit.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(credit, 2); frame.Children.Add(credit);
        var child = new Window { Title = "CCP Classic · " + text[key], Width = 450, Height = 650,
            MinWidth = 420, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border { Name = "SheetHost", Child = frame } };
        child.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        child.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        child.Deactivated += (_, _) => ReleaseKeysWhenUnfocused();
        child.Closed += (_, _) => { if (ReferenceEquals(SettingsWindow, child)) SettingsWindow = null; capturePanic = false; };
        SettingsWindow = child;
        child.Show(this);
        close.Focus();
    }

    private void CloseSheet()
    {
        SettingsWindow?.Close(); sheet = null; capturePanic = false;
    }

    private void FillSettings(StackPanel content)
    {
        var language = new ComboBox { ItemsSource = LocalizationService.Languages,
            SelectedItem = LocalizationService.Languages.First(l => l.Code == Settings.Language), MinWidth = 160 };
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedItem is LanguageOption selected && selected.Code != Settings.Language)
                RefreshLanguage(selected.Code);
        };
        content.Children.Add(Row("Language", language));
        foreach (var key in new[] { "StartWithSystem", "StartHidden", "AutoRun" })
            content.Children.Add(Row(key, DisabledToggle()));
        content.Children.Add(Row("FillAllMonitors", Toggle(Settings.FillAllMonitors, text["FillAllMonitors"], value => Settings.FillAllMonitors = value)));
        var panicKey = Button("Change", () => { capturePanic = true; SetStatus("CapturePanicKey"); });
        panicKey.Content = PanicName() + " · " + text["Change"];
        content.Children.Add(Row("PanicKey", panicKey, "PanicKeyHelp"));
        var soft = Toggle(Settings.GentleVisuals, text["GentleVisuals"], SetGentle); soft.Name = "SettingsGentle";
        content.Children.Add(Row("GentleVisuals", soft, "GentleVisualsHelp"));
        content.Children.Add(Row("Performance", Toggle(Settings.PerformanceMode, text["Performance"], value => Settings.PerformanceMode = value)));
        content.Children.Add(Row("ReducedMotion", Toggle(Settings.ReducedMotion, text["ReducedMotion"], value => Settings.ReducedMotion = value)));
        content.Children.Add(Row("Export", Button("Export", ExportSettings)));
        content.Children.Add(Row("Reset", PendingButton("Reset")));
        var warning = new StackPanel { Spacing = 10, IsVisible = false };
        warning.Children.Add(Note("NoPanicWarning"));
        var confirm = Button("ConfirmNoPanic", () => { Session.SetPanicEnabled(false, true); OpenSheet("Settings"); });
        confirm.Foreground = Brush("#FF8A8A"); confirm.Name = "ConfirmNoPanic";
        warning.Children.Add(confirm);
        warning.Children.Add(Button("Cancel", () => warning.IsVisible = false));
        var red = new Border { Background = Brush("#2A1A22"), BorderBrush = Brush("#6A3030"),
            BorderThickness = new(1), CornerRadius = new(6), Padding = new(10), Child = warning };
        red.IsVisible = false;
        var noPanic = Button(Session.PanicEnabled ? "NoPanic" : "RestorePanic", () =>
        {
            if (!Session.PanicEnabled) { Session.SetPanicEnabled(true); OpenSheet("Settings"); }
            else { red.IsVisible = true; warning.IsVisible = true; }
        });
        noPanic.Name = "NoPanic"; noPanic.Foreground = Brush("#FF8A8A");
        content.Children.Add(noPanic); content.Children.Add(red);
    }

    private async void ExportSettings()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = text["Export"], SuggestedFileName = "ccp-classic-settings.json",
                DefaultExtension = "json", FileTypeChoices = [new("JSON") { Patterns = ["*.json"] }]
            });
            if (file?.TryGetLocalPath() is { } path) { new SettingsStore(path).Save(Settings); SetStatus("Saved"); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { SetStatus("SaveFailed"); }
    }

    private Control Row(string key, Control control, string? hint = null)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, MinHeight = 36 };
        var words = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Label(text[key]));
        if (hint is not null) words.Children.Add(Note(hint));
        grid.Children.Add(words); Grid.SetColumn(control, 1); grid.Children.Add(control);
        return grid;
    }
    private TextBlock Note(string key)
    {
        var label = Label(text[key], 11); label.Classes.Add("muted"); label.TextWrapping = TextWrapping.Wrap; return label;
    }
    private Button PendingButton(string key)
    {
        var button = Button(key, () => { }); button.IsEnabled = false; ToolTip.SetTip(button, text["Unavailable"]); return button;
    }
    private ToggleButton DisabledToggle()
    {
        var toggle = Toggle(false, text["Unavailable"], _ => { }); toggle.IsEnabled = false;
        ToolTip.SetTip(toggle, text["Unavailable"]); return toggle;
    }
}
