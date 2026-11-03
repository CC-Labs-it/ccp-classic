using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore? store;
    private readonly bool windows;
    private readonly LocalizationService text;
    private readonly DispatcherTimer timer;
    private Grid root = null!;
    private ContentControl page = null!;
    private Button startStop = null!;
    private TextBlock elapsed = null!;
    private TextBlock status = null!;
    private ToggleButton gentle = null!;
    private string tab = "Studio";
    private string? sheet;
    private string statusKey = "ShellOnly";
    private bool capturePanic;
    private DesktopEffectsService? effects;
    private readonly PanicInputRouter input;
    private readonly PresetStore presetStore;
    public ClassicSettings Settings { get; }
    public SessionController Session { get; }
    public Window? SettingsWindow { get; private set; }
    public bool SettingsRecovered { get; set; }

    public MainWindow(ClassicSettings? settings = null, SettingsStore? store = null, bool? windows = null,
        bool desktopEffects = false, Func<bool>? globalPanicAvailable = null, PresetStore? presetStore = null,
        bool startupLaunch = false, StartupRegistration? startupRegistration = null, LocalDeviceController? deviceController = null)
    {
        Settings = settings ?? new();
        Settings.Normalize();
        Settings.PanicKey = PanicKeys.Normalize(Settings.PanicKey);
        this.store = store;
        this.presetStore = presetStore ?? new(PresetStore.DefaultDirectory);
        this.startupRegistration = startupRegistration;
        haptics = deviceController ?? new();
        this.windows = windows ?? OperatingSystem.IsWindows();
        text = new(Settings.Language);
        Settings.Language = text.Language;
        Session = new(Settings);
        InitializeProgress();
        input = new(Session, globalPanicAvailable ?? (() => effects?.GlobalPanicAvailable == true));
        InitializeToys();
        Title = "CCP Classic";
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        Closing += AwaitLocalCleanup;
        Width = 724; Height = 760; MinWidth = 700; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildShell();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => ReleaseKeysWhenUnfocused();
        Session.Changed += UpdateSession;
        Session.StopRequested += () => { statusKey = "Stopped"; UpdateSession(); };
        timer = new(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            Session.Tick();
            TickStartup();
            TickProgress();
            elapsed.Text = Session.Elapsed.ToString(@"hh\:mm\:ss");
        });
        Opened += (_, _) =>
        {
            if (desktopEffects && effects is null)
            {
                effects = new(this, Settings, Session, ConfigureEffectInput, HandleGlobalKey);
                effects.StatusChanged += SetStatus;
                effects.DeviceEvent += EmitToys;
                effects.AudioLevelChanged += ToysAudioLevel;
                effects.InteractiveEvent += value =>
                {
                    if (value.Kind == InteractiveEffectEventKind.BubblePopped) Progress.BubblePopped();
                    if (value.Kind == InteractiveEffectEventKind.CornerHit) Progress.CornerHit();
                };
                effects.VideoFinished += seconds => Progress.VideoFinished(seconds);
                effects.FlashShown += Progress.FlashShown;
                effects.AvatarClicked += Progress.AvatarClicked;
                effects.LockCompleted += (errors, phrases, seconds) => Progress.LockResult(errors, phrases, seconds);
                effects.WipePlayingChanged += playing => { if (playing) Progress.WipeStarted(); else Progress.WipeEnded(); };
                effects.CountAnswered += correct =>
                {
                    Progress.BubbleCountAnswer(correct); Progress.AttentionAnswer(correct);
                    if (correct) Progress.AddXp(10);
                };
                SetStatus("Idle");
                InitializeStartup(startupLaunch);
            }
            if (SettingsRecovered) SetStatus("SettingsRecovered");
            timer.Start();
        };
        Closed += (_, _) => { DisposeToys(); DisposeStartup(); libraryScan?.Cancel(); libraryScan?.Dispose(); timer.Stop(); effects?.Dispose(); SettingsWindow?.Close(); Session.Dispose(); SaveProgress(); };
    }

    private void BuildShell()
    {
        root = new Grid { RowDefinitions = new("80,*,56") };
        root.Children.Add(Chrome(Header(), false));
        page = new ContentControl();
        Grid.SetRow(page, 1); root.Children.Add(page);
        var bottom = Chrome(Bottom(), true);
        Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        Content = root;
        SelectTab(tab);
        UpdateSession();
    }

    private Control Header()
    {
        var grid = new Grid { ColumnDefinitions = new("*,110,110"), RowDefinitions = new("36,36"), ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new TextBlock { Text = "● CCP Classic", Foreground = Brush("#FF69B4"),
            FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var presets = PresetStore.BuiltIns.Concat(presetStore.LoadAll(out _)).ToArray();
        var preset = new ComboBox { ItemsSource = presets,
            SelectedItem = presets.FirstOrDefault(item => item.Name == Settings.Preset), MinHeight = 26, FontSize = 12,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PresetDefinition>((item, _) =>
                new TextBlock { Text = item is null ? "" : item.Id.StartsWith("builtin-", StringComparison.Ordinal) ? text[item.Name] : item.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis }) };
        preset.Name = "PresetPicker";
        AutomationProperties.SetName(preset, text["Presets"]);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is not PresetDefinition selected || selected.Name == Settings.Preset) return;
            PresetStore.Apply(selected, Settings); PresetChanged();
        };
        Grid.SetColumn(preset, 1); grid.Children.Add(preset);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var name in new[] { "Studio", "Presets", "Progress" })
        {
            var button = Button(name, () => SelectTab(name));
            button.Name = "Tab_" + name;
            tabs.Children.Add(button);
        }
        var navigation = new Grid { ColumnDefinitions = new("*,Auto") };
        navigation.Children.Add(tabs);
        Grid.SetRow(navigation, 1); Grid.SetColumnSpan(navigation, 3); grid.Children.Add(navigation);
        var level = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var levelText = Label(text["Level"] + " " + Progress.Level + "     " + (int)Progress.XpIntoLevel + " " + text["XP"], 11);
        levelText.Name = "HeaderLevel"; level.Children.Add(levelText);
        level.Children.Add(new ProgressBar { Name = "HeaderXp", Value = Progress.XpIntoLevel, Height = 3, MinHeight = 0, Maximum = Progress.XpForNextLevel });
        Grid.SetColumn(level, 2); grid.Children.Add(level);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var name in new[] { "Library", "Companion", "Toys", "Settings" })
        {
            var button = Button(name, () => OpenSheet(name));
            button.Name = "Open_" + name;
            if (name == "Settings") { button.Content = "⚙"; AutomationProperties.SetName(button, text[name]); }
            tools.Children.Add(button);
        }
        Grid.SetColumn(tools, 1); navigation.Children.Add(tools);
        return grid;
    }

    private Control Bottom()
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center };
        startStop = Button("Start", () =>
        {
            if (Session.IsRunning)
            {
                if (!Session.Stop(finishUntilStopped: true)) SetStatus("DisabledByPanic");
            }
            else { statusKey = "ShellOnly"; Session.Start(); }
        });
        startStop.Name = "StartStop"; startStop.Classes.Add("primary"); grid.Children.Add(startStop);
        elapsed = Label("00:00:00", 15);
        Grid.SetColumn(elapsed, 1); grid.Children.Add(elapsed);
        var save = Button("Save", SaveSettings); save.Name = "SaveSettings";
        Grid.SetColumn(save, 2); grid.Children.Add(save);
        status = Label(text[statusKey], 11); status.Classes.Add("muted"); status.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(status, 3); grid.Children.Add(status);
        var panicBadge = Label(text["PanicKey"] + ": " + PanicName(), 11);
        panicBadge.Name = "PanicBadge";
        Grid.SetColumn(panicBadge, 4); grid.Children.Add(panicBadge);
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        group.Children.Add(Label(text["GentleVisuals"], 12));
        gentle = Toggle(Settings.GentleVisuals, text["GentleVisuals"], SetGentle); gentle.Name = "GentleToggle";
        group.Children.Add(gentle);
        Grid.SetColumn(group, 5); grid.Children.Add(group);
        return grid;
    }

    public void SelectTab(string key)
    {
        tab = key;
        Control body = key switch
        {
            "Presets" => CreatePresetPanel(),
            "Progress" => new ProgressPanel(Progress, text),
            _ => Studio()
        };
        page.Content = new ScrollViewer { Content = body, Padding = new(12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var name in new[] { "Studio", "Presets", "Progress" })
        if (root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "Tab_" + name) is { } button)
            {
                button.Background = Brush(name == key ? "#FF69B4" : "#181830");
                button.Foreground = Brush(name == key ? "#1A0A12" : "#C9C9DE");
            }
    }

    private PresetPanel CreatePresetPanel()
    {
        var panel = new PresetPanel(Settings, text, presetStore, PresetChanged);
        panel.StatusChanged += SetStatus;
        return panel;
    }

    private void PresetChanged() { SaveSettings(); BuildShell(); effects?.RefreshSettings(); }

    private Control Studio()
    {
        return DashboardMosaic.Build(Settings, text, windows,
            () => new AnimatedLogo(() => tab == "Studio" && IsActive && WindowState != WindowState.Minimized
                && !Settings.GentleVisuals && !Settings.ReducedMotion && !Settings.PerformanceMode),
            definition => OpenEffect(definition.Id));
    }

    public void RefreshLanguage(string code)
    {
        text.SetLanguage(code); Settings.Language = text.Language;
        BuildShell();
        RefreshTrayMenu();
        if (sheet is not null) OpenSheet(sheet);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (capturePanic)
        {
            if (!PanicKeys.IsBindable(e.Key)) return;
            if (Session.KeyDown(e.Key.ToString())) { Progress.PanicPressed(); SetStatus("PanicStopped"); }
            Settings.PanicKey = e.Key.ToString(); capturePanic = false;
            RefreshLanguage(Settings.Language); e.Handled = true; return;
        }
        if (input.LocalDown(e.Key.ToString()))
        {
            Progress.PanicPressed();
            SetStatus("PanicStopped"); CloseSheet(); e.Handled = true;
        }
        if (input.ConsumesKey(e.Key.ToString())) e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        input.LocalUp(e.Key.ToString());
        if (input.ConsumesKey(e.Key.ToString())) e.Handled = true;
    }

    private void ReleaseKeysWhenUnfocused()
    {
        // Let an owned window finish activating before deciding that Classic lost focus.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsActive && !OwnedWindows.Any(window => window.IsActive))
            {
                Progress.SwitchedToOtherApplication();
                if (!input.GlobalAvailable) Session.ReleaseKeys();
            }
        }, DispatcherPriority.Background);
    }

    private void ConfigureEffectInput(Window window)
    {
        window.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Deactivated += (_, _) => ReleaseKeysWhenUnfocused();
    }

    private void HandleGlobalKey(string key, bool down)
    {
        if (input.GlobalChanged(key, down)) { Progress.PanicPressed(); SetStatus("PanicStopped"); CloseSheet(); }
    }

    private void OnTextInput(object? sender, TextInputEventArgs args)
    {
        if (effects?.GlobalTextAvailable != true && args.Text is { } characters) effects?.FeedText(characters);
    }

    private void UpdateSession()
    {
        RefreshTrayMenu();
        startStop.Content = text[Session.IsRunning ? "Stop" : "Start"];
        elapsed.IsVisible = Session.IsRunning;
        status.Text = text[statusKey];
        if (root.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PanicBadge") is { } badge)
            badge.Text = Session.PanicEnabled ? text["PanicKey"] + ": " + PanicName() : text["PanicOff"];
    }
    private void SetStatus(string key) { statusKey = key; status.Text = text[key]; }
    private string PanicName() => Settings.PanicKey == "Escape" ? "Esc" : Settings.PanicKey;
    private void SaveSettings()
    {
        try { store?.Save(Settings); SetStatus("Saved"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus("SaveFailed"); }
    }
    private void SetGentle(bool value)
    {
        Settings.GentleVisuals = value; gentle.IsChecked = value;
        if (SettingsWindow?.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(c => c.Name == "SettingsGentle") is { } other)
            other.IsChecked = value;
        effects?.RefreshSettings();
    }
    private static Border Chrome(Control child, bool bottom) => new() { Child = child, Padding = new(12, 0),
        Background = Brush("#1C1C35"), BorderBrush = Brush("#26263F"), BorderThickness = bottom ? new(0, 1, 0, 0) : new(0, 0, 0, 1) };
    private static SolidColorBrush Brush(string colour) => new(Color.Parse(colour));
    private static TextBlock Label(string value, double size = 12) => new() { Text = value, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
    private Button Button(string key, Action action)
    {
        var result = new Button { Content = text[key], Padding = new(10, 3) }; result.Classes.Add("pill");
        result.Click += (_, _) => action(); return result;
    }
    private static ToggleButton Toggle(bool value, string label, Action<bool> changed)
    {
        var result = new ToggleButton { IsChecked = value }; result.Classes.Add("switch");
        AutomationProperties.SetName(result, label); result.Click += (_, _) => changed(result.IsChecked == true); return result;
    }
}
