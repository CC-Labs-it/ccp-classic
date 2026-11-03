using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App;
using CCP.Classic.App.Companion;
using CCP.Classic.Core;
using Xunit;

namespace CCP.Classic.Tests;

// CCP parity: right-clicking the tube opens its dark, pink-rimmed menu with the items Classic has.
[Collection("Avalonia UI")]
public sealed class CompanionMenuTests
{
    [Fact]
    public async Task RightClickOpensTheMenuWithoutAPokeAndPlacementChoicesPersistLikeCtrlWheel()
    {
        Window owner = null!; CompanionWindow companion = null!; var settings = new ClassicSettings();
        var clicks = 0; var stops = 0; var saves = 0; var commands = new List<CompanionMenuCommand>();
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new() { Position = new PixelPoint(800, 100) }; owner.Show(); owner.Activate();
                companion = new(owner, settings, _ => { }, requireTransparency: false);
                companion.AvatarClicked += () => clicks++; companion.StopRequested += () => stops++;
                companion.PlacementChanged += () => saves++; companion.MenuCommand += commands.Add;
                companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0 && companion.IsVisible, () => companion.Update(null, .1));
            await UiDispatcher.Run(() =>
            {
                var menu = companion.Menu;
                // The look: CCP's dark body, pink rim, rounded corners.
                Assert.Equal(Color.Parse("#121220"), ((ISolidColorBrush)menu.Background!).Color);
                Assert.Equal(Color.Parse("#FF69B4"), ((ISolidColorBrush)menu.BorderBrush!).Color);
                Assert.Equal(new Thickness(2), menu.BorderThickness); Assert.Equal(new CornerRadius(8), menu.CornerRadius);

                OpenByRightClick(companion);
                Assert.True(menu.IsOpen); Assert.Equal(0, clicks); Assert.Equal(0, stops);
                Assert.Equal("Companion settings", Label(companion, "Settings"));
                Assert.True(Item(companion, "Detach").IsVisible); Assert.False(Item(companion, "Attach").IsVisible);
                Assert.False(Item(companion, "AlwaysOnTop").IsVisible);
                // No panel answers this window, so there is no session row; Reset size waits for a change.
                Assert.False(Item(companion, "Session").IsVisible); Assert.False(Item(companion, "ResetSize").IsEnabled);
                Assert.Equal("☑  Barks", Label(companion, "Barks"));
                Assert.All(new[] { "Settings", "Detach", "Grow", "Shrink", "Hide", "Barks", "ShowPanel" },
                    name => Assert.True(Item(companion, name).IsVisible && Item(companion, name).IsEnabled, name));

                // Detach floats her from where she stands and saves that spot; Always on top then applies.
                var docked = companion.Position;
                Choose(companion, "Detach");
                Assert.True(settings.CompanionPlacement.Detached); Assert.Equal(TubeDockSide.Float, companion.DockSide);
                Assert.Equal(docked, companion.Position); Assert.Equal(docked.X, settings.CompanionPlacement.Left);
                Assert.Equal(1, saves); Assert.True(companion.Topmost);
                OpenByRightClick(companion);
                Assert.False(Item(companion, "Detach").IsVisible); Assert.True(Item(companion, "Attach").IsVisible);
                Assert.Equal("☑  Always on top", Label(companion, "AlwaysOnTop"));
                Choose(companion, "AlwaysOnTop");
                Assert.False(settings.CompanionPlacement.AlwaysOnTop); Assert.False(companion.Topmost); Assert.Equal(2, saves);

                // Grow, Shrink and Reset size share Ctrl+wheel's bounds and steps.
                Choose(companion, "Grow"); Choose(companion, "Grow");
                Assert.Equal(1.5, settings.CompanionPlacement.Scale); Assert.Equal(4, saves);
                OpenByRightClick(companion); Assert.False(Item(companion, "Grow").IsEnabled); Assert.True(Item(companion, "ResetSize").IsEnabled);
                for (var step = 0; step < 4; step++) Choose(companion, "Shrink");
                Assert.Equal(.5, settings.CompanionPlacement.Scale);
                OpenByRightClick(companion); Assert.False(Item(companion, "Shrink").IsEnabled);
                Choose(companion, "ResetSize"); Assert.Equal(1, settings.CompanionPlacement.Scale);

                // Attach docks her beside the panel again.
                var before = saves; Choose(companion, "Attach");
                Assert.False(settings.CompanionPlacement.Detached); Assert.Equal(TubeDockSide.Left, companion.DockSide);
                Assert.Equal(before + 1, saves);
                Assert.Empty(commands); Assert.Equal(0, clicks); Assert.Equal(0, stops);
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    [Fact]
    public async Task BarksHideShowPanelAndTheSessionRowFollowTheirSwitchesAndThePanicGate()
    {
        Window owner = null!; CompanionWindow companion = null!; var settings = new ClassicSettings();
        var commands = new List<CompanionMenuCommand>(); var state = CompanionSessionState.Idle;
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                companion = new(owner, settings, _ => { }, requireTransparency: false) { SessionState = () => state };
                companion.MenuCommand += commands.Add; companion.Update(null, 0);
            });
            await Until(() => companion.DecodedFrames > 0 && companion.IsVisible, () => companion.Update(null, .1));
            await UiDispatcher.Run(() =>
            {
                OpenByRightClick(companion);
                Assert.Equal("▶  Start session", Label(companion, "Session")); Assert.True(Item(companion, "Session").IsEnabled);
                Choose(companion, "Session"); Assert.Empty(commands);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(CompanionMenuCommand.StartSession, Assert.Single(commands));

                state = CompanionSessionState.Running; OpenByRightClick(companion);
                Assert.Equal("■  Stop session", Label(companion, "Session")); Assert.True(Item(companion, "Session").IsEnabled);
                Choose(companion, "Session"); Dispatcher.UIThread.RunJobs();
                Assert.Equal(CompanionMenuCommand.StopSession, commands[^1]);

                // Panic off: Stop shows refused and a stray click sends nothing.
                state = CompanionSessionState.Locked; OpenByRightClick(companion);
                Assert.Equal("■  Stop session", Label(companion, "Session")); Assert.False(Item(companion, "Session").IsEnabled);
                var sent = commands.Count; Choose(companion, "Session"); Dispatcher.UIThread.RunJobs(); Assert.Equal(sent, commands.Count);

                Choose(companion, "Barks"); Dispatcher.UIThread.RunJobs();
                Assert.False(settings.BarksEnabled); Assert.Equal(CompanionMenuCommand.BarksChanged, commands[^1]);
                OpenByRightClick(companion);
                Assert.Equal("☐  Barks", Label(companion, "Barks"));
                Assert.Equal(Color.Parse("#FF6347"), ((ISolidColorBrush)((TextBlock)Item(companion, "Barks").Header!).Foreground!).Color);
                Choose(companion, "Barks"); Dispatcher.UIThread.RunJobs(); Assert.True(settings.BarksEnabled);

                Choose(companion, "Settings"); Dispatcher.UIThread.RunJobs();
                Assert.Equal(CompanionMenuCommand.OpenSettings, commands[^1]);

                // Show main window brings the panel back up, even from the taskbar.
                owner.WindowState = WindowState.Minimized; Choose(companion, "ShowPanel");
                Assert.Equal(WindowState.Normal, owner.WindowState); Assert.True(owner.IsActive);
                companion.Update(null, .5); Assert.True(companion.IsVisible);

                Choose(companion, "Hide");
                Assert.False(settings.CompanionEnabled); Assert.False(companion.IsVisible);
                Dispatcher.UIThread.RunJobs(); Assert.Equal(CompanionMenuCommand.AvatarHidden, commands[^1]);
                companion.Update(null, 1); Assert.False(companion.IsVisible);
            });
        }
        finally { await UiDispatcher.Run(() => { companion?.FinishImmediately(); owner?.Close(); }); }
    }

    [Fact]
    public async Task TheHostForwardsMenuWorkAndAsksThePanelForTheSessionRow()
    {
        Window owner = null!; CompanionHost host = null!; var settings = new ClassicSettings();
        var commands = new List<CompanionMenuCommand>();
        try
        {
            await UiDispatcher.Run(() =>
            {
                owner = new(); owner.Show(); owner.Activate();
                host = new(owner, settings, window => new(window, settings, _ => { }, requireTransparency: false));
                host.MenuCommand += commands.Add; host.Refresh();
            });
            await Until(() => host.Window!.DecodedFrames > 0 && host.Window.IsVisible, host.IdleTick);
            await UiDispatcher.Run(() =>
            {
                var companion = host.Window!;
                OpenByRightClick(companion); Assert.False(Item(companion, "Session").IsVisible);
                host.SessionState = () => CompanionSessionState.Running;
                OpenByRightClick(companion); Assert.True(Item(companion, "Session").IsVisible);
                Choose(companion, "Session"); Dispatcher.UIThread.RunJobs();
                Assert.Equal(CompanionMenuCommand.StopSession, Assert.Single(commands));
                // Hiding releases the tube once the panel refreshes; a later refresh brings a new one back.
                Choose(companion, "Hide"); Dispatcher.UIThread.RunJobs(); host.Refresh();
                Assert.Null(host.Window); Assert.Equal(CompanionMenuCommand.AvatarHidden, commands[^1]);
                settings.CompanionEnabled = true; host.Refresh(); Assert.NotNull(host.Window);
            });
        }
        finally { await UiDispatcher.Run(() => { host?.Dispose(); owner?.Close(); }); }
    }

    [Fact]
    public Task ThePanelRunsMenuWorkThroughItsOwnStartStopAndSaveWithoutTouchingPanic() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "classic-tube-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        var settings = new ClassicSettings();
        var window = new MainWindow(settings, new SettingsStore(path), windows: true,
            presetStore: new PresetStore(Path.Combine(folder, "presets")));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(CompanionSessionState.Idle, window.CompanionMenuSession());
            window.CompanionMenuChosen(CompanionMenuCommand.StartSession);
            Assert.True(window.Session.IsRunning); Assert.Equal(CompanionSessionState.Running, window.CompanionMenuSession());
            window.CompanionMenuChosen(CompanionMenuCommand.StopSession);
            Assert.False(window.Session.IsRunning);

            // A session whose preset turned panic off: the row shows Stop refused and Stop stays refused.
            window.CompanionMenuChosen(CompanionMenuCommand.StartSession); window.Session.SetPanicEnabled(false, true);
            Assert.Equal(CompanionSessionState.Locked, window.CompanionMenuSession());
            window.CompanionMenuChosen(CompanionMenuCommand.StopSession);
            Assert.True(window.Session.IsRunning); Assert.False(window.Session.PanicEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Panic is disabled. Restore it in Settings to stop.");
            foreach (var command in Enum.GetValues<CompanionMenuCommand>().Where(command => command != CompanionMenuCommand.StopSession))
            { window.CompanionMenuChosen(command); Assert.False(window.Session.PanicEnabled); }
            window.Session.SetPanicEnabled(true); window.Session.Stop();
            settings.CompanionEnabled = settings.BarksEnabled = true;

            // Barks and Hide persist at once and keep an open Companion window's switches in step.
            window.OpenSheet("Companion"); Dispatcher.UIThread.RunJobs();
            settings.BarksEnabled = false; window.CompanionMenuChosen(CompanionMenuCommand.BarksChanged);
            Assert.False(Toggle(window, "CompanionBarks").IsChecked); Assert.False(new SettingsStore(path).Load(out _).BarksEnabled);
            settings.CompanionEnabled = false; window.CompanionMenuChosen(CompanionMenuCommand.AvatarHidden);
            Assert.False(Toggle(window, "CompanionAvatar").IsChecked); Assert.False(new SettingsStore(path).Load(out _).CompanionEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Avatar hidden. Turn it back on in Companion.");
            // The way back: the Avatar switch in Companion.
            var avatar = Toggle(window, "CompanionAvatar"); avatar.IsChecked = true;
            avatar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(settings.CompanionEnabled);

            window.SettingsWindow!.Close(); Dispatcher.UIThread.RunJobs();
            window.CompanionMenuChosen(CompanionMenuCommand.OpenSettings); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Companion", window.SettingsWindow!.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "SheetTitle").Text);
        }
        finally
        {
            window.SettingsWindow?.Close(); window.Close(); Dispatcher.UIThread.RunJobs();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    });

    private static void OpenByRightClick(CompanionWindow companion)
    {
        companion.Menu.Close(); Dispatcher.UIThread.RunJobs();
        var hit = companion.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "CompanionHit");
        var point = hit.TranslatePoint(new Point(hit.Bounds.Width / 2, hit.Bounds.Height / 2), companion)!.Value;
        companion.MouseDown(point, MouseButton.Right); companion.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(companion.Menu.IsOpen);
    }
    private static MenuItem Item(CompanionWindow companion, string name) =>
        companion.Menu.Items.OfType<MenuItem>().Single(item => item.Name == "CompanionMenu_" + name);
    private static string? Label(CompanionWindow companion, string name) => ((TextBlock)Item(companion, name).Header!).Text;
    private static void Choose(CompanionWindow companion, string name) =>
        Item(companion, name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static ToggleButton Toggle(MainWindow window, string name) =>
        window.SettingsWindow!.GetVisualDescendants().OfType<ToggleButton>().Single(toggle => toggle.Name == name);

    private static async Task Until(Func<bool> ready, Action update)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var complete = false; await UiDispatcher.Run(() => { update(); complete = ready(); });
            if (complete) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Companion frame was not decoded.");
    }
}
