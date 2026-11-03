using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using CCP.Classic.App;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Controls.HelpLoops;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class TutorialTests
{
    [Fact]
    public void EveryEditorAndPanelHasHelpAndLinuxOmitsWallpaper()
    {
        foreach (var effect in EffectCatalog.All)
        {
            Assert.Equal(effect.Id, TutorialCatalog.Find(effect.Id).Id);
            Assert.Equal(effect.Id, TutorialCatalog.Find(effect.Title).Id);
        }
        foreach (var panel in new[] { "Library", "Companion", "Toys", "Presets", "Progress", "Settings" })
            Assert.Equal(panel, TutorialCatalog.Find(panel).Id);
        Assert.Equal("guide", TutorialCatalog.Find("Studio").Id);
        Assert.Equal("spiral", TutorialCatalog.Find("MakeYourOwn").Id);
        Assert.Equal("guide", TutorialCatalog.Find("unknown").Id);
        Assert.DoesNotContain(TutorialCatalog.ForPlatform(false), topic => topic.Id == "wall");
    }

    [Fact]
    public Task EveryPublicSceneRendersItsOriginalStagesUsingNativeDrawing() => UiDispatcher.Run(() =>
    {
        var ids = TutorialCatalog.All.Select(topic => topic.SceneId).OfType<string>().Distinct().ToArray();
        // Twelve since the keyword triggers scene left with that feature.
        Assert.Equal(12, ids.Length);
        foreach (var id in ids)
        {
            var scene = Assert.IsAssignableFrom<HelpLoopScene>(HelpLoopScene.Create(id));
            Assert.InRange(scene.StillMs, 0, scene.DurationMs);
            foreach (var t in new[] { 0, scene.StillMs, scene.DurationMs * .9, scene.DurationMs - 1 })
            {
                var group = new DrawingGroup();
                using (var dc = group.Open())
                using (var frameContext = new LoopContext(dc))
                {
                    var frame = new LoopFrame(frameContext, frameContext, new()) { TextService = new("en") };
                    frame.Ground(); scene.Draw(frame, t);
                }
                Assert.NotEmpty(group.Children);
            }
            scene.Reset();
        }
    });

    [Fact]
    public Task HelpAnimationHasPixelsAndPreferencesHoldTheSourceStillFrame() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings { ReducedMotion = true };
        using var animation = new TutorialAnimation(settings, new("en")) { Width = 480, Height = 270 };
        animation.SelectScene("SpiralOverlay"); animation.Measure(new Size(480, 270)); animation.Arrange(new Rect(0, 0, 480, 270));
        Assert.Equal(3200, animation.PositionMilliseconds);
        using var bitmap = new RenderTargetBitmap(new PixelSize(480, 270), new Vector(96, 96));
        bitmap.Render(animation);
        using var output = new MemoryStream(); bitmap.Save(output, PngBitmapEncoderOptions.Default);
        Assert.True(output.Length > 5000);
        settings.ReducedMotion = false; settings.GentleVisuals = true;
        animation.Pulse(10000); Assert.Equal(3200, animation.PositionMilliseconds);
        animation.SelectScene(null); Assert.Equal(0, animation.PositionMilliseconds);
    });

    [Fact]
    public Task OriginalStageClipsSpiralArmsBeforeTheyReachLetterboxMargins() => UiDispatcher.Run(() =>
    {
        using var animation = new TutorialAnimation(new() { ReducedMotion = true }, new("en"));
        animation.SelectScene("SpiralOverlay"); animation.Measure(new Size(640, 270)); animation.Arrange(new Rect(0, 0, 640, 270));
        using var bitmap = new RenderTargetBitmap(new PixelSize(640, 270), new Vector(96, 96)); bitmap.Render(animation);
        var pixels = new int[640 * 270]; var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 640, 270), pin.AddrOfPinnedObject(), pixels.Length * 4, 640 * 4); }
        finally { pin.Free(); }
        for (var y = 0; y < 270; y++)
        {
            Assert.Equal(0, pixels[y * 640 + 20] >>> 24);
            Assert.Equal(0, pixels[y * 640 + 620] >>> 24);
        }
        Assert.NotEqual(0, pixels[135 * 640 + 320] >>> 24);
    });

    [Fact]
    public Task HelpOwnsItsWindowReusesItAndPanicClosesItWithoutStartingEffects() => UiDispatcher.Run(() =>
    {
        var owner = new MainWindow(windows: false); owner.Show();
        try
        {
            owner.OpenHelp("FlashImages"); var help = Assert.IsType<TutorialWindow>(owner.HelpWindow);
            Assert.Equal(owner, help.Owner); Assert.Equal("flash", help.TopicId);
            Assert.False(owner.Session.IsRunning); Assert.False(help.Topmost);
            owner.OpenHelp("Settings"); Assert.Same(help, owner.HelpWindow); Assert.Equal("Settings", help.TopicId);
            Assert.True(owner.Session.Stop()); Assert.Null(owner.HelpWindow); Assert.False(help.Animation.TimerRunning);
            owner.OpenHelp("Library"); Assert.NotSame(help, owner.HelpWindow);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task HidingOrMinimizingEitherWindowStopsAnimationAndCloseReleasesIt() => UiDispatcher.Run(() =>
    {
        var owner = new Window(); owner.Show();
        var help = new TutorialWindow(owner, new(), new("en"), _ => { }, windows: false); help.SelectTopic("flash"); help.Show(owner);
        try
        {
            Assert.True(help.Animation.MotionActive);
            help.WindowState = WindowState.Minimized; Assert.False(help.Animation.MotionActive); Assert.False(help.Animation.TimerRunning);
            help.WindowState = WindowState.Normal; Assert.True(help.Animation.MotionActive);
            owner.WindowState = WindowState.Minimized; Assert.False(help.Animation.MotionActive); Assert.False(help.Animation.TimerRunning);
            owner.WindowState = WindowState.Normal; help.Hide(); Assert.False(help.Animation.MotionActive);
            help.Show(owner); Assert.True(help.Animation.MotionActive);
            help.Close(); Assert.False(help.Animation.TimerRunning);
            help.Animation.SelectScene("FlashImages"); Assert.False(help.Animation.MotionActive);
        }
        finally { help.Close(); owner.Close(); }
    });

    [Fact]
    public Task MotionPreferenceChangesPauseAndResumeWithoutChangingTheChosenTopic() => UiDispatcher.Run(() =>
    {
        var owner = new Window(); owner.Show(); var settings = new ClassicSettings();
        var help = new TutorialWindow(owner, settings, new("en"), _ => { }); help.SelectTopic("bubble"); help.Show(owner);
        try
        {
            settings.GentleVisuals = true; help.Animation.Pulse(0);
            Assert.False(help.Animation.MotionActive); Assert.False(Named<Button>(help, "HelpPause").IsEnabled);
            Assert.True(Named<TextBlock>(help, "HelpMotionStatus").IsVisible);
            settings.GentleVisuals = false; help.Animation.Pulse(0);
            Assert.True(help.Animation.MotionActive); Assert.Equal("bubble", help.TopicId);
            help.Animation.UserPaused = true; Assert.False(help.Animation.MotionActive);
            settings.ReducedMotion = true; help.Animation.Pulse(0); settings.ReducedMotion = false; help.Animation.Pulse(0);
            Assert.True(help.Animation.UserPaused); Assert.False(help.Animation.MotionActive);
        }
        finally { help.Close(); owner.Close(); }
    });

    [Fact]
    public Task NavigationAndLanguageChangesUpdateLocalizedTextWithoutChangingSettings() => UiDispatcher.Run(() =>
    {
        var owner = new Window(); owner.Show(); var settings = new ClassicSettings(); var text = new LocalizationService("en");
        var help = new TutorialWindow(owner, settings, text, _ => { }, windows: false); help.Show(owner);
        try
        {
            Named<Button>(help, "HelpNext").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("flash", help.TopicId);
            help.SelectTopic("Library"); text.SetLanguage("it");
            Assert.Equal(text["HelpLibraryBody"], Named<TextBlock>(help, "HelpBody").Text);
            Assert.Equal(text["Library"], Named<TextBlock>(help, "HelpHeading").Text);
            Assert.Equal("en", settings.Language);
            help.SelectTopic("wall"); Assert.Equal("guide", help.TopicId);
        }
        finally { help.Close(); owner.Close(); }
    });

    [Fact]
    public Task ExportOriginalSceneAndGuideProofsWhenRequested() => UiDispatcher.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var settings = new ClassicSettings { GentleVisuals = true };
        var text = new LocalizationService("en"); var owner = new Window(); owner.Show();
        var help = new TutorialWindow(owner, settings, text, _ => { }); help.Show(owner);
        try
        {
            foreach (var topic in new[] { "guide", "spiral", "Settings" })
            {
                help.SelectTopic(topic); help.UpdateLayout(); Dispatcher.UIThread.RunJobs(); help.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)help.ClientSize.Width, (int)help.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(help); bitmap.Save(Path.Combine(folder, "help-" + topic + ".png"), PngBitmapEncoderOptions.Default);
            }
            using var view = new TutorialAnimation(settings, text);
            foreach (var id in TutorialCatalog.All.Select(topic => topic.SceneId).OfType<string>().Distinct())
            { view.SelectScene(id); Save(view, 480, 270, "help-scene-" + id + ".png"); }
        }
        finally { help.Close(); owner.Close(); }
        void Save(Control control, int width, int height, string name)
        {
            control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)); bitmap.Render(control);
            bitmap.Save(Path.Combine(folder, name), PngBitmapEncoderOptions.Default);
        }
    });

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
}
