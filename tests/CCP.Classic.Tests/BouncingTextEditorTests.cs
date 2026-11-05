using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CCP.Classic.App.Controls;
using CCP.Classic.App.Effects;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class BouncingTextEditorTests
{
    [Fact]
    public Task OriginalControlsSaveLocalOptionsAndCustomPhraseWithoutPlayingEffects() => UiDispatcher.Run(() =>
    {
        var settings = new ClassicSettings(); var changed = 0;
        var editor = new BouncingTextEditor(settings, new LocalizationService("en"), () => changed++);
        var children = editor.GetVisualDescendants().OfType<Control>().ToArray();
        var mode = children.OfType<ComboBox>().Single(c => c.Name == "BounceColorMode");
        mode.SelectedIndex = 1; Assert.Equal(1, settings.BouncingText.ColorMode);
        children.OfType<CheckBox>().Single(c => c.Name == "setting_second_text").IsChecked = true;
        Assert.True(settings.BouncingText.SecondText);
        children.OfType<CheckBox>().Single(c => c.Name == "setting_fx_spin").IsChecked = true;
        Assert.True(settings.BouncingText.Spin);
        children.OfType<TextBox>().Single(c => c.Name == "BounceNewPhrase").Text = "My local phrase";
        children.OfType<Button>().Single(c => c.Name == "BounceAddPhrase").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(settings.BouncingText.PhrasePool["My local phrase"]); Assert.True(changed >= 4);
        Assert.Contains("Fredoka", LocalFontPicker.Families);
        Assert.Equal(new FontFamily("Inter"), LocalFontPicker.Resolve("missing-font://untrusted"));
        var measurement = BouncingTextRenderer.Measure("Focus", "Fredoka", 72);
        Assert.InRange(measurement.Width, 50, 500); Assert.InRange(measurement.Height, 40, 200);
    });

    [Fact]
    public Task OriginalMotionAndFontOptionsHaveAStableNativeProof() => UiDispatcher.Run(() =>
    {
        var cues = new[]
        {
            new BouncingTextCue("Breathe", .1, .15, 1, 1) { FontFamily = "Fredoka", Colour = "#FF69B4", Outline = true,
                ScaleX = 1.08, ScaleY = 1.08, Angle = -6 },
            new BouncingTextCue("Custom phrase", .8, .7, 1.1, 1) { FontFamily = "Inter", Colour = "#00FFFF", Angle = 12,
                ScaleX = .8, ScaleY = 1.14, BurstSeconds = .25, BurstCenterX = .7, BurstCenterY = .7 }
        };
        var view = new VisualEffectsControl(); view.UpdateFrame(new EffectFrame { Interactive = new([], cues[0]) { BouncingTexts = cues } });
        var panel = new Border { Width = 960, Height = 540, Background = Brush.Parse("#151321"), Child = view };
        panel.Measure(new Size(960, 540)); panel.Arrange(new Rect(0, 0, 960, 540));
        using var target = new RenderTargetBitmap(new PixelSize(960, 540)); target.Render(panel);
        var folder = Environment.GetEnvironmentVariable("CCP_CLASSIC_PROOF_DIR"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); target.Save(Path.Combine(folder, "bouncing-text-parity.png"), PngBitmapEncoderOptions.Default);
    });
}
