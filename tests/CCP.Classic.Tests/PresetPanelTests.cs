using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class PresetPanelTests
{
    [Fact]
    public Task BuiltInLoadChangesTheProfileAndDurationButtonsStayExclusive() => WithPanel((window, panel, settings, store, changed) =>
    {
        settings.Language = "it"; settings.PanicKey = "F8";
        Click(window, Named<Button>(window, "PresetLoad_builtin-gentle"));
        Assert.Equal("Gentle", settings.Preset); Assert.True(settings.GentleVisuals);
        Assert.False(settings.Effects["spiral"].Enabled);
        Assert.Equal("it", settings.Language); Assert.Equal("F8", settings.PanicKey);
        Click(window, Named<ToggleButton>(window, "PresetDuration_30"));
        Assert.Equal(30, settings.RunMinutes);
        Assert.Single(window.GetVisualDescendants().OfType<ToggleButton>(), b => b.IsChecked == true);
        Click(window, Named<Button>(window, "PresetLoad_builtin-daily"));
        Assert.Equal("Daily", settings.Preset); Assert.True(settings.Effects["spiral"].Enabled);
        Assert.Equal(0, settings.RunMinutes);
        Assert.True(Named<ToggleButton>(window, "PresetDuration_0").IsChecked);
        Assert.Equal(3, changed());
    });

    [Fact]
    public Task CustomProfileCanBeSavedLoadedOverwrittenAndDeleted() => WithPanel((window, panel, settings, store, changed) =>
    {
        settings.Effects["flash"].Values["rate"] = 18;
        Named<TextBox>(window, "PresetName").Text = "Reading";
        Click(window, Named<Button>(window, "PresetSave"));
        var saved = Assert.Single(store.LoadAll(out var recovered));
        Assert.False(recovered); Assert.Equal("Saved", panel.StatusKey);
        Assert.Equal("Reading", settings.Preset);
        settings.Effects["flash"].Values["rate"] = 50;
        Click(window, Named<Button>(window, "PresetLoad_" + saved.Id));
        Assert.Equal(18, settings.Effects["flash"].Values["rate"]);
        settings.Effects["flash"].Values["rate"] = 24;
        Named<TextBox>(window, "PresetName").Text = "reading";
        Click(window, Named<Button>(window, "PresetSave"));
        Assert.Equal(saved.Id, Assert.Single(store.LoadAll(out recovered)).Id);
        Assert.Equal(24, Assert.Single(store.LoadAll(out recovered)).Effects["flash"].Values["rate"]);
        Click(window, Named<Button>(window, "PresetDelete_" + saved.Id));
        Assert.Empty(store.LoadAll(out recovered));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "PresetLoad_" + saved.Id);
    });

    [Fact]
    public Task InvalidNameShowsAVisibleFailureWithoutChangingTheProfile() => WithPanel((window, panel, settings, store, changed) =>
    {
        Named<TextBox>(window, "PresetName").Text = "  ";
        Click(window, Named<Button>(window, "PresetSave"));
        Assert.Equal("SaveFailed", panel.StatusKey);
        Assert.Equal(new LocalizationService()["SaveFailed"], Named<TextBlock>(window, "PresetStatus").Text);
        Assert.Equal("Daily", settings.Preset); Assert.Equal(0, changed());
        Assert.Empty(store.LoadAll(out var recovered)); Assert.False(recovered);
    });

    [Fact]
    public Task DamagedCustomFileShowsRecoveryAndBuiltInsRemainUsable() => UiDispatcher.Run(() =>
    {
        using var fixture = new PanelFixture();
        File.WriteAllText(Path.Combine(fixture.Root, Guid.NewGuid().ToString("N") + ".json"), "{");
        var settings = new ClassicSettings();
        var panel = new PresetPanel(settings, new(), fixture.Store, () => { });
        var window = Window(panel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("SettingsRecovered", panel.StatusKey);
            Click(window, Named<Button>(window, "PresetLoad_builtin-deep"));
            Assert.True(settings.Effects["video"].Enabled); Assert.Equal("Deep", settings.Preset);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public Task UnwritableStoreShowsFailureInsteadOfThrowingFromTheSaveEvent() => UiDispatcher.Run(() =>
    {
        using var fixture = new PanelFixture();
        var blocker = Path.Combine(fixture.Root, "not-a-directory"); File.WriteAllText(blocker, "file");
        var panel = new PresetPanel(new(), new(), new PresetStore(blocker), () => { });
        var window = Window(panel);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Named<TextBox>(window, "PresetName").Text = "Name";
            Click(window, Named<Button>(window, "PresetSave"));
            Assert.Equal("SaveFailed", panel.StatusKey);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static Task WithPanel(Action<Window, PresetPanel, ClassicSettings, PresetStore, Func<int>> action) => UiDispatcher.Run(() =>
    {
        using var fixture = new PanelFixture();
        var settings = new ClassicSettings(); var changes = 0;
        var panel = new PresetPanel(settings, new(), fixture.Store, () => changes++);
        var window = Window(panel);
        try { window.Show(); Dispatcher.UIThread.RunJobs(); action(window, panel, settings, fixture.Store, () => changes); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static Window Window(PresetPanel panel) => new()
    {
        Width = 700, Height = 650, Content = new ScrollViewer { Content = panel, Padding = new Thickness(12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
    };

    private static T Named<T>(Window window, string name) where T : Control =>
        Assert.Single(window.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static void Click(Window window, Control control)
    {
        window.Activate(); control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(control.IsEffectivelyVisible); Assert.True(control.IsEffectivelyEnabled);
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseMove(centre.Value); window.MouseDown(centre.Value, MouseButton.Left); window.MouseUp(centre.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class PanelFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public PresetStore Store { get; }
        public PanelFixture() { Directory.CreateDirectory(Root); Store = new(Root); }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
