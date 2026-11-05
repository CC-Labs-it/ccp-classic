using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ShellTests
{
    [Fact]
    public Task LoadingAPresetClosesEditorsHoldingThePreviousOptions() => WithWindow(window =>
    {
        window.OpenEffect("flash"); var previous = window.SettingsWindow!;
        window.SelectTab("Presets"); Dispatcher.UIThread.RunJobs();
        Named<Button>(window, "PresetLoad_builtin-gentle").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(previous.IsVisible); Assert.Null(window.SettingsWindow);
        window.OpenEffect("flash"); Assert.NotSame(previous, window.SettingsWindow);
    });

    [Fact]
    public Task DefaultWindowFitsTheMosaicWithoutEmptySideColumns() => WithWindow(window =>
    {
        Assert.InRange(window.Width / window.Height, .9, 1.1);
        var mosaic = Named<Grid>(window, "DashboardMosaic");
        var corner = mosaic.TranslatePoint(new Point(0, 0), window)!.Value;
        Assert.InRange(corner.X, 0, 40);
        Assert.True(corner.X + mosaic.Bounds.Width <= window.ClientSize.Width);
        var start = Named<Button>(window, "StartStop").TranslatePoint(new Point(0, 0), window)!.Value;
        Assert.True(corner.Y + mosaic.Bounds.Height <= start.Y);
    });

    [Fact]
    public Task SettingsSaveOnTheirOwnWithoutASaveButton() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-classic-ui-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        var store = new SettingsStore(path);
        var window = new MainWindow(new ClassicSettings(), store, windows: true);
        try
        {
            window.Show();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "SaveSettings");
            window.FlushSettings();
            Assert.False(File.Exists(path));
            Click(window, Named<ToggleButton>(window, "GentleToggle"));
            window.FlushSettings();
            Assert.True(store.Load(out _).GentleVisuals);
            Click(window, Named<ToggleButton>(window, "GentleToggle"));
            window.Close();
            Assert.False(store.Load(out _).GentleVisuals);
        }
        finally
        {
            window.Close();
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    });

    [Fact]
    public Task SettingsShowTheVersionAndLinkToTheReleasesPage() => WithWindow(window =>
    {
        window.OpenSheet("Settings");
        var child = Owned(window);
        var link = Named<HyperlinkButton>(child, "NewVersionLink");
        Assert.Equal(new Uri("https://github.com/CC-Labs-it/ccp-classic/releases"), link.NavigateUri);
        Assert.False(string.IsNullOrWhiteSpace(Named<TextBlock>(child, "AppVersionText").Text));
        Assert.DoesNotContain("+", MainWindow.AppVersion);
    });

    [Fact]
    public Task SupportBannerUsesTheFixedSystemBrowserLinkAndLocalizedCaption() => WithWindow(window =>
    {
        var expected = new Uri("https://www.patreon.com/CodeBambi");
        foreach (var code in new[] { "en", "it" })
        {
            window.RefreshLanguage(code); Dispatcher.UIThread.RunJobs();
            var link = Named<HyperlinkButton>(window, "SupportLink");
            Assert.Equal(expected, link.NavigateUri);
            Assert.Equal(new LocalizationService(code)["SupportLink"], link.Content);
            Assert.True(link.IsEffectivelyVisible);
            var banner = Named<Border>(window, "SupportBanner");
            Assert.True(banner.Bounds.Width <= window.ClientSize.Width);
        }
    });

    [Fact]
    public Task FlashEditorFitsItsControlsAndMoreExpandsWithinTheScreen() => WithWindow(window =>
    {
        window.OpenEffect("flash"); var editor = Owned(window); editor.UpdateLayout();
        var collapsed = editor.ClientSize.Height;
        Assert.Equal(SizeToContent.Height, editor.SizeToContent);
        Assert.InRange(collapsed, 180, 700);
        Assert.True(collapsed <= editor.MaxHeight);
        Click(editor, Named<Button>(editor, "More_flash")); editor.UpdateLayout();
        Assert.True(editor.ClientSize.Height > collapsed);
        Assert.True(editor.ClientSize.Height <= editor.MaxHeight);
        var fade = Named<Slider>(editor, "Knob_flash_fade");
        fade.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(fade.IsEffectivelyVisible); Assert.True(fade.IsEffectivelyEnabled);
        AssertFrame(editor, "flash-settings-expanded", 300);
        Click(editor, Named<Button>(editor, "More_flash")); editor.UpdateLayout();
        Assert.Equal(collapsed, editor.ClientSize.Height, 1);
        AssertFrame(editor, "flash-settings-compact", 300);
        AssertFrame(window, "studio-support", 700);
    }, settings: new() { GentleVisuals = true, ReducedMotion = true });

    [Theory]
    [InlineData(true, 12)]
    [InlineData(false, 11)]
    public Task DashboardTilesOpenOwnedEditorsWithUsefulPrimaryControls(bool windows, int count) =>
        WithWindow(window =>
        {
            var tiles = window.GetVisualDescendants().OfType<EffectTile>().ToArray();
            Assert.Equal(count, tiles.Length);
            Assert.Equal(windows, tiles.Any(tile => tile.Name == "Effect_wall"));
            Assert.Empty(window.GetVisualDescendants().OfType<EffectCard>());
            foreach (var definition in EffectCatalog.All.Where(item => !item.WindowsOnly || windows))
            {
                Click(window, Named<EffectTile>(window, "Effect_" + definition.Id));
                var editor = Owned(window);
                var card = Assert.Single(editor.GetVisualDescendants().OfType<EffectCard>());
                Assert.InRange(card.PrimaryControlCount, 1, 6);
                if (definition.Id == "flash")
                {
                    var more = Named<Button>(editor, "More_flash");
                    var fade = Named<Slider>(editor, "Knob_flash_fade");
                    Assert.False(fade.IsEffectivelyVisible);
                    Click(editor, more);
                    Assert.True(fade.IsEffectivelyVisible);
                    Click(editor, more);
                    Assert.False(fade.IsEffectivelyVisible);
                    AssertFrame(editor, "flash-settings", 300);
                }
                Click(editor, Named<Button>(editor, "SheetClose"));
                Assert.Null(window.SettingsWindow);
            }
        }, windows);

    [Fact]
    public Task OneEscapeStopsThroughAnOwnedWindowAndHeldRepeatNeedsRelease() =>
        WithWindow(window =>
        {
            var stops = 0;
            window.Session.StopRequested += () => stops++;
            Click(window, Named<Button>(window, "StartStop"));
            Assert.True(window.Session.IsRunning);
            window.OpenSheet("Settings");
            var child = Owned(window);
            Assert.True(Named<Button>(child, "SheetClose").Focus());
            PressEscape(child);
            Assert.False(window.Session.IsRunning);
            Assert.Null(window.SettingsWindow);
            Assert.Equal(1, stops);

            window.Session.Start();
            PressEscape(window);
            Assert.True(window.Session.IsRunning);
            Assert.Equal(1, stops);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            PressEscape(window);
            Assert.False(window.Session.IsRunning);
            Assert.Equal(2, stops);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        });

    [Fact]
    public Task NoPanicNeedsTheRedWarningAndBlocksBothManualStops() =>
        WithWindow(window =>
        {
            window.OpenSheet("Settings");
            var settings = Owned(window);
            Assert.False(Named<Button>(settings, "ConfirmNoPanic").IsEffectivelyVisible);
            Click(settings, Named<Button>(settings, "NoPanic"));
            Assert.True(window.Session.PanicEnabled);
            Assert.True(Named<Button>(settings, "ConfirmNoPanic").IsEffectivelyVisible);
            Click(settings, Named<Button>(settings, "ConfirmNoPanic"));
            Assert.False(window.Session.PanicEnabled);
            settings = Owned(window);
            Click(settings, Named<Button>(settings, "SheetClose"));
            Click(window, Named<Button>(window, "StartStop"));
            Assert.True(window.Session.IsRunning);
            Click(window, Named<Button>(window, "StartStop"));
            Assert.True(window.Session.IsRunning);
            PressEscape(window);
            Assert.True(window.Session.IsRunning);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            window.OpenSheet("Settings");
            settings = Owned(window);
            Click(settings, Named<Button>(settings, "NoPanic"));
            Assert.True(window.Session.PanicEnabled);
            settings = Owned(window);
            Click(settings, Named<Button>(settings, "SheetClose"));
            Click(window, Named<Button>(window, "StartStop"));
            Assert.False(window.Session.IsRunning);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeldPanicDoesNotRetriggerWhenOpeningAnotherWindow(bool effectEditor) =>
        WithWindow(window =>
        {
            window.Activate();
            window.Session.Start();
            PressEscape(window);
            Assert.False(window.Session.IsRunning);
            Click(window, Named<Button>(window, "StartStop"));
            Assert.True(window.Session.IsRunning);
            if (effectEditor) window.OpenEffect("flash");
            else window.OpenSheet("Settings");
            var child = Owned(window);
            Deactivate(window);
            child.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(child.IsActive);
            Assert.False(window.IsActive);
            PressEscape(child);
            Assert.True(window.Session.IsRunning);
            child.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            PressEscape(child);
            Assert.False(window.Session.IsRunning);
            Assert.Null(window.SettingsWindow);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        });

    [Fact]
    public Task LeavingClassicStillResetsKeysForTheNextPress() => WithWindow(window =>
    {
        window.Activate();
        window.Session.Start();
        PressEscape(window);
        window.Session.Start();
        Deactivate(window);
        Dispatcher.UIThread.RunJobs();
        window.Activate();
        PressEscape(window);
        Assert.False(window.Session.IsRunning);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("ru")]
    public Task HeaderUsesTheTranslatedXpLabel(string language) => WithWindow(window =>
    {
        window.RefreshLanguage(language);
        Dispatcher.UIThread.RunJobs();
        var text = new LocalizationService(language);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text == text["Level"] + " 1     0 " + text["XP"]);
    });

    [Fact]
    public Task GentleSwitchesShareTheSavedSetting() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-classic-ui-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(folder, "settings.json"));
        var window = new MainWindow(new ClassicSettings(), store, windows: true);
        try
        {
            window.Show();
            Click(window, Named<ToggleButton>(window, "GentleToggle"));
            Assert.True(window.Settings.GentleVisuals);
            window.OpenSheet("Settings");
            var child = Owned(window);
            Assert.True(Named<ToggleButton>(child, "SettingsGentle").IsChecked);
            Click(child, Named<ToggleButton>(child, "SettingsGentle"));
            Assert.False(window.Settings.GentleVisuals);
            Click(child, Named<Button>(child, "SheetClose"));
            Assert.False(Named<ToggleButton>(window, "GentleToggle").IsChecked);
            Click(window, Named<ToggleButton>(window, "GentleToggle"));
            window.FlushSettings();
            var saved = store.Load(out var recovered);
            Assert.False(recovered);
            Assert.True(saved.GentleVisuals);
        }
        finally
        {
            window.Close();
            var savedPath = Path.Combine(folder, "settings.json");
            if (File.Exists(savedPath)) File.Delete(savedPath);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    });

    [Fact]
    public Task EveryLanguageRendersTheThreeTabsAndFourOwnedSettingsWindows() =>
        WithWindow(window =>
        {
            foreach (var language in LocalizationService.Languages)
            {
                window.RefreshLanguage(language.Code);
                var text = new LocalizationService(language.Code);
                foreach (var tab in new[] { "Studio", "Presets", "Progress" })
                {
                    window.SelectTab(tab);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(text[tab], Named<Button>(window, "Tab_" + tab).Content);
                    AssertFrame(window, language.Code == "en" ? tab.ToLowerInvariant() : null, 700);
                }
                foreach (var sheet in new[] { "Library", "Companion", "Toys", "Settings" })
                {
                    window.OpenSheet(sheet);
                    var child = Owned(window);
                    Assert.True(Named<Border>(child, "SheetHost").IsVisible);
                    Assert.Equal(text[sheet], Named<TextBlock>(child, "SheetTitle").Text);
                    AssertFrame(child, language.Code == "en" && sheet == "Settings" ? "settings" : null, 300);
                    Click(child, Named<Button>(child, "SheetClose"));
                }
            }
        });

    private static Task WithWindow(Action<MainWindow> action, bool windows = true, ClassicSettings? settings = null) => UiDispatcher.Run(() =>
    {
        var window = new MainWindow(settings ?? new ClassicSettings(), windows: windows);
        try { window.Show(); Dispatcher.UIThread.RunJobs(); action(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static T Named<T>(Window window, string name) where T : Control =>
        Assert.Single(window.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static Window Owned(MainWindow owner)
    {
        Dispatcher.UIThread.RunJobs();
        var child = owner.SettingsWindow;
        Assert.NotNull(child);
        Assert.True(child.IsVisible);
        Assert.Same(owner, child.Owner);
        return child;
    }

    private static void Click(Window window, Control control)
    {
        window.Activate();
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.IsEffectivelyEnabled);
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseMove(centre.Value);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressEscape(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
    }

    private static void Deactivate(Window window)
    {
        // Headless activation does not deactivate the previous window like a window manager.
        typeof(WindowBase).GetMethod("HandleDeactivated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
    }

    private static void AssertFrame(Window window, string? name, int minimumWidth = 1000)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width >= minimumWidth);
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
        if (folder is null || name is null) return;
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
