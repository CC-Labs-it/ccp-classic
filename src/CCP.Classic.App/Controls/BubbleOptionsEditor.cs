using Avalonia.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class BubbleOptionsEditor : OptionEditor
{
    public BubbleOptionsEditor(BubbleOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 8;
        Number("Size", options.SizePercent, 50, 150, v => options.SizePercent = (int)v);
        Number("Speed", options.SpeedBoostPercent, 0, 500, v => options.SpeedBoostPercent = (int)v);
        Number("Volume", options.VolumePercent, 0, 100, v => options.VolumePercent = (int)v);
        var styles = new[] { "bubble_motion_float_up", "bubble_motion_rain", "bubble_motion_spiral_in", "Mixed" };
        var motion = new ComboBox { ItemsSource = styles.Select(k => text[k]).ToArray(), SelectedIndex = (int)options.Motion };
        motion.SelectionChanged += (_, _) => { if (motion.SelectedIndex >= 0) { options.Motion = (BubbleMotion)motion.SelectedIndex; Changed(); } };
        Children.Add(Row("Mode", motion)); Heading("EffectBubbleType");
        foreach (var kind in Enum.GetValues<BubbleEffectKind>().Where(k => k != BubbleEffectKind.None))
        {
            var key = kind switch { BubbleEffectKind.Flash => "FlashImages", BubbleEffectKind.Subliminal => "Subliminals",
                BubbleEffectKind.Pink => "PinkFilter", BubbleEffectKind.Spiral => "Spiral", BubbleEffectKind.BrainDrain => "BrainDrain",
                BubbleEffectKind.Glitch => "Glitch", BubbleEffectKind.Cascade => "GifRain", BubbleEffectKind.BrainDrainWash => "ImageWash", _ => "MandatoryVideos" };
            Toggle(key, options.Variants.Contains(kind), value =>
            { options.Variants.Remove(kind); if (value) options.Variants.Add(kind); });
        }
    }
}
