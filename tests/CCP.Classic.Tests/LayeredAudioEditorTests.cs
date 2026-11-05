using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Classic.App.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class LayeredAudioEditorTests
{
    [Fact]
    public Task OpeningAnEnabledSavedMixRemainsIdleAndGainsNeverInvokeStructuralApply() => UiDispatcher.Run(() =>
    {
        var track = new AudioLayerTrack { Path = Path.Combine(Path.GetTempPath(), "saved.wav") };
        var options = new LayeredAudioOptions { Enabled = true, Tracks = [track] };
        var saved = 0; var applies = 0; var gains = 0;
        var editor = new LayeredAudioEditor(options, new(), () => saved++, () => applies++, () => gains++);
        editor.Measure(new Size(380, 900)); editor.Arrange(new Rect(0, 0, 380, editor.DesiredSize.Height));
        SaveProof(editor);
        Assert.Equal(0, saved); Assert.Equal(0, applies); Assert.Equal(0, gains);
        Find<Slider>(editor, "LayeredMasterVolume").Value = 35;
        Find<Slider>(editor, "LayeredTrackVolume_" + track.Id.ToString("N")).Value = 44;
        Assert.Equal(35, options.MasterVolume); Assert.Equal(44, track.Volume);
        Assert.Equal(2, gains); Assert.Equal(0, applies); Assert.Equal(2, saved);
        Find<CheckBox>(editor, "LayeredAudioOnly").IsChecked = true;
        Assert.True(options.AudioOnlySession); Assert.Equal(2, gains); Assert.Equal(0, applies); Assert.Equal(3, saved);
        Find<CheckBox>(editor, "LayeredAudioEnabled").IsChecked = false;
        Assert.False(options.Enabled); Assert.Equal(1, applies);
        Find<Slider>(editor, "LayeredMasterVolume").Value = 50;
        Assert.Equal(1, applies); Assert.Equal(3, gains);
    });

    [Fact]
    public Task AddRemoveAndTrackEnableRetainSourceDefaultsAndTheEightTrackBound() => UiDispatcher.Run(() =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "CCP Classic layered editor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var paths = Enumerable.Range(0, 10).Select(i => Path.Combine(folder, $"local-{i}.wma")).ToArray();
            foreach (var path in paths) File.WriteAllBytes(path, AudioFormatsTests.AsfHeader);
            var options = new LayeredAudioOptions(); var applies = 0;
            var editor = new LayeredAudioEditor(options, new(), () => { }, () => applies++);
            Assert.Equal(0, editor.AddTracks(["https://example.invalid/file.wma", Path.Combine(folder, "missing.wav")]));
            Assert.Equal(0, applies); Assert.Equal(8, editor.AddTracks(paths)); Assert.Equal(1, applies);
            Assert.All(options.Tracks, track => { Assert.Equal(70, track.Volume); Assert.True(track.Enabled); });
            Assert.Equal(8, options.Tracks.Select(track => track.Id).Distinct().Count());
            Assert.False(Find<Button>(editor, "LayeredAddTrack").IsEnabled); Assert.Equal(0, editor.AddTracks(paths));
            var first = options.Tracks[0];
            Find<CheckBox>(editor, "LayeredTrackEnabled_" + first.Id.ToString("N")).IsChecked = false;
            Assert.False(first.Enabled); Assert.Equal(2, applies);
            Assert.False(Find<Slider>(editor, "LayeredTrackVolume_" + first.Id.ToString("N")).IsEffectivelyEnabled);
            Find<Button>(editor, "LayeredRemoveTrack_" + first.Id.ToString("N")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(7, options.Tracks.Count); Assert.Equal(3, applies); Assert.True(Find<Button>(editor, "LayeredAddTrack").IsEnabled);
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public Task BufferingAndPartialFailureStatusAreLocalizedWithoutStartingPlayback() => UiDispatcher.Run(() =>
    {
        string? key = "AudioLayersBuffering"; var text = new LocalizationService("fr");
        var editor = new LayeredAudioEditor(new(), text, () => { }, status: () => key);
        var label = Find<TextBlock>(editor, "LayeredAudioStatus");
        Assert.True(label.IsVisible); Assert.Equal(text[key], label.Text);
        key = "AudioLayersPartialFailure"; editor.RefreshStatus(); Assert.Equal(text[key], label.Text);
        key = null; editor.RefreshStatus(); Assert.False(label.IsVisible);
    });

    private static T Find<T>(Control editor, string name) where T : Control =>
        editor.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
    private static void SaveProof(Control editor)
    {
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
        var panel = new Border { Padding = new(12), Background = Brush.Parse("#151321"), Child = editor };
        var window = new Window { Width = 460, SizeToContent = SizeToContent.Height, Content = panel };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
            bitmap.Render(window); Directory.CreateDirectory(folder);
            bitmap.Save(Path.Combine(folder, "layered-audio-editor.png"), PngBitmapEncoderOptions.Default);
        }
        finally { panel.Child = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
