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
        if (sheet != key) BarkOpened(key);
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
                FillToys(content);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
        ShowSettingsWindow(key, content);
    }

    public void OpenEffect(string id)
    {
        capturePanic = false;
        var definition = EffectCatalog.All.Single(d => d.Id == id && (!d.WindowsOnly || windows));
        BarkOpened("Effect." + id);
        sheet = null;
        var content = new StackPanel { Spacing = 12, Margin = new(14, 12) };
        var card = new EffectCard(definition, Settings.Effects[id], text, () =>
        {
            foreach (var tile in this.GetVisualDescendants().OfType<EffectTile>()) tile.Refresh();
            effects?.RefreshSettings();
        }, EffectCustomization(id));
        card.ActionRequested += key => { if (id == "spiral" && key == "loom") OpenLoom(); };
        card.ValueChanged += key =>
        {
            if (id != "spiral" || key != "spiral") return;
            Settings.CustomSpiralId = null; Settings.Spiral.UseProcedural = true; effects?.RefreshSettings();
        };
        content.Children.Add(card);
        content.Children.Add(Note(DesktopEffectsService.IsPlayable(id) ? "LiveSettings" : "Unavailable"));
        ShowSettingsWindow(definition.Title, content);
    }

    private void ShowSettingsWindow(string key, Control content)
    {
        SettingsWindow?.Close();
        var frame = new Grid { RowDefinitions = new("58,*,32") };
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new(18, 0) };
        var title = Label(text[key], 21); title.Name = "SheetTitle"; title.FontWeight = FontWeight.SemiBold;
        title.Foreground = Brush("#FF9BD5"); title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2;
        header.Children.Add(title);
        var close = Button("Close", CloseSheet); close.Name = "SheetClose";
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(HelpButton(key)); actions.Children.Add(close);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); frame.Children.Add(header);
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var maximumHeight = Math.Max(200, (screen?.WorkingArea.Height / (screen?.Scaling ?? 1) ?? 900) - 48);
        var maximumWidth = Math.Max(320, (screen?.WorkingArea.Width / (screen?.Scaling ?? 1) ?? 1280) - 40);
        var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(100, maximumHeight - 124),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); frame.Children.Add(scroll);
        var credit = Label(text["ByCredit"], 12); credit.Foreground = Brush("#AFA6C7"); credit.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(credit, 2); frame.Children.Add(credit);
        var child = new Window { Title = "CCP Classic · " + text[key], Width = Math.Min(760, maximumWidth),
            SizeToContent = SizeToContent.Height, MinWidth = Math.Min(400, maximumWidth), MinHeight = 180,
            MaxHeight = maximumHeight, MaxWidth = maximumWidth, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border { Name = "SheetHost", Child = frame } };
        if (key == "Library")
        {
            child.SizeToContent = SizeToContent.Manual; child.Width = Math.Min(920, maximumWidth);
            child.Height = Math.Min(740, maximumHeight); child.MinWidth = Math.Min(880, maximumWidth);
        }
        VisualPolish.ApplyWindow(child, Settings);
        child.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        child.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        child.Deactivated += (_, _) => ReleaseKeysWhenUnfocused();
        child.Closed += (_, _) => { if (ReferenceEquals(SettingsWindow, child)) SettingsWindow = null; capturePanic = false; };
        SettingsWindow = child;
        child.Show(this);
        VisualPolish.Enter(content, Settings);
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
        var startup = Toggle(Settings.StartWithSystem, text["StartWithSystem"], ChangeStartupPreference); startup.Name = "StartWithSystem";
        content.Children.Add(Row("StartWithSystem", startup));
        content.Children.Add(Row("StartHidden", Toggle(Settings.StartHidden, text["StartHidden"], value => Settings.StartHidden = value), "StartHiddenHelp"));
        content.Children.Add(Row("AutoRun", Toggle(Settings.AutoRun, text["AutoRun"], value => Settings.AutoRun = value)));
        content.Children.Add(Row("FillAllMonitors", Toggle(Settings.FillAllMonitors, text["FillAllMonitors"], value => Settings.FillAllMonitors = value)));
        var panicKey = Button("Change", () => { capturePanic = true; SetStatus("CapturePanicKey"); });
        panicKey.Content = PanicName() + " · " + text["Change"];
        content.Children.Add(Row("PanicKey", panicKey, "PanicKeyHelp"));
        var soft = Toggle(Settings.GentleVisuals, text["GentleVisuals"], SetGentle); soft.Name = "SettingsGentle";
        content.Children.Add(Row("GentleVisuals", soft, "GentleVisualsHelp"));
        content.Children.Add(Row("Performance", Toggle(Settings.PerformanceMode, text["Performance"], value => { Settings.PerformanceMode = value; VisualPolish.ApplyWindow(this, Settings); })));
        content.Children.Add(Row("ReducedMotion", Toggle(Settings.ReducedMotion, text["ReducedMotion"], value => { Settings.ReducedMotion = value; VisualPolish.ApplyWindow(this, Settings); })));
        content.Children.Add(Row("Export", Button("Export", ExportSettings)));
        content.Children.Add(Row("Reset", Button("Reset", ResetSettings)));
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
        content.Children.Add(OptionHelp.Wrap(noPanic, "NoPanic", text)); content.Children.Add(red);
        var rows = content.Children.ToArray(); content.Children.Clear(); content.Spacing = 0;
        content.Children.Add(OptionLayout.Group(rows.Take(5), text["OptionsGeneral"]));
        content.Children.Add(OptionLayout.Group(rows.Skip(5).Take(4), text["GentleVisuals"], 1));
        content.Children.Add(OptionLayout.Group(rows.Skip(9), text["OptionsUtility"], 2));
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
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, MinHeight = 40 };
        var words = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var label = Label(text[key], 14); label.Foreground = Brush("#D7C9F0"); label.FontWeight = FontWeight.Medium;
        label.TextWrapping = TextWrapping.Wrap; words.Children.Add(label);
        if (hint is not null) words.Children.Add(Note(hint));
        grid.Children.Add(words); Grid.SetColumn(control, 1); grid.Children.Add(control);
        var helped = OptionHelp.Wrap(grid, key, text);
        if (key is not ("Intiface" or "Lovense")) helped.Classes.Add("option-compact"); return helped;
    }
    private TextBlock Note(string key)
    {
        var label = Label(text[key], 12.5); label.Classes.Add("muted"); label.TextWrapping = TextWrapping.Wrap; return label;
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
