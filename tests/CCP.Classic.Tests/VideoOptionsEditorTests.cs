using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class VideoOptionsEditorTests
{
    [Fact]
    public Task FullOptionsSaveCutsTargetStyleAndTypedCustomPhrases() => UiDispatcher.Run(() =>
    {
        var options = new VideoOptions(); var changes = 0; var previews = 0;
        var editor = new VideoOptionsEditor(options, new LocalizationService(), () => changes++, () => previews++);
        var controls = editor.GetLogicalDescendants().OfType<Control>().ToArray();
        controls.OfType<CheckBox>().Single(c => c.Name == "VideoTogether").IsChecked = true;
        controls.OfType<NumericUpDown>().Single(c => c.Name == "VideoMaximum").Value = 42;
        controls.OfType<CheckBox>().Single(c => c.Name == "VideoTargetBorder").IsChecked = true;
        controls.OfType<TextBox>().Single(c => c.Name == "VideoNewPhrase").Text = "My target";
        controls.OfType<Button>().Single(c => c.Name == "VideoAddPhrase").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(options.FloatingTogether); Assert.Equal(42, options.MaxDurationSeconds); Assert.True(options.TargetShowBorder);
        Assert.True(options.TargetPhrases["My target"]); Assert.True(changes >= 4);
        controls.OfType<Button>().Single(c => c.Name == "TestVideo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, previews);
        Assert.All(controls.OfType<CheckBox>().Select(c => c.Content).OfType<string>(), label => Assert.DoesNotContain("Video", label));
    });

    [Fact]
    public Task OriginalOutlinedAndPanelTargetsHaveABoundedNativeProof() => UiDispatcher.Run(() =>
    {
        var options = new VideoOptions { TargetCount = 1 }; var clock = new VideoAttentionTimeline(1);
        clock.Start(10, options); clock.Step(4, options, 960, 540, false, true);
        var original = Assert.Single(clock.Targets);
        var plain = new VideoAttentionLayer();
        plain.Update([original with { Text = "Focus", X = .15, Y = .2 }], options, 5, false);
        var styled = new VideoAttentionLayer(); var panelOptions = new VideoOptions { TargetFloatingText = false, TargetShowBorder = true };
        styled.Update([original with { Id = 99, Text = "My target", X = .8, Y = .75 }], panelOptions, 5, false);
        var surface = new Grid { Width = 960, Height = 540, Background = Brush.Parse("#151321") };
        surface.Children.Add(plain); surface.Children.Add(styled);
        surface.Measure(new Size(960, 540)); surface.Arrange(new Rect(0, 0, 960, 540));
        using var bitmap = new RenderTargetBitmap(new PixelSize(960, 540)); bitmap.Render(surface);
        Assert.InRange(plain.TargetBounds(original.Id).Width, 150, 960);
        Assert.InRange(styled.TargetBounds(99).Height, 60, 540);
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); bitmap.Save(Path.Combine(folder, "video-attention-parity.png"), PngBitmapEncoderOptions.Default);
    });
}
