using Avalonia.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Flash;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class FlashOptionsEditor : OptionEditor
{
    public FlashOptionsEditor(ClassicSettings settings, LocalizationService text, Action changed) : base(text, changed)
    {
        var options = settings.Flash; Spacing = 8;
        Number("FlashCount", options.Count, 1, 20, value => options.Count = (int)value);
        Toggle("FlashRandomCount", options.RandomCount, value => options.RandomCount = value);
        Number("FlashMinimumCount", options.MinimumCount, 1, 20, value => options.MinimumCount = (int)value);
        Number("FlashHold", options.HoldSeconds, 1, 30, value => options.HoldSeconds = (int)value);
        Toggle("FlashStay", options.StayUntilPopped, value => options.StayUntilPopped = value);
        Toggle("FlashAudio", options.AudioEnabled, value => options.AudioEnabled = value);
        Toggle("FlashRemix", options.RemixEnabled, value => options.RemixEnabled = value);
        Toggle("FlashHydraTiming", options.HydraLinkedTiming, value => options.HydraLinkedTiming = value);
        Heading("FlashMotion");
        var styles = new[] { "FlashStill", "FlashDrift", "FlashPendulum", "Mixed" };
        var motion = new ComboBox { Name = "FlashMotion", ItemsSource = styles.Select(key => text[key]).ToArray(), SelectedIndex = (int)options.Motion };
        motion.SelectionChanged += (_, _) => { if (motion.SelectedIndex < 0) return; options.Motion = (FlashMotionStyle)motion.SelectedIndex; Changed(); };
        Children.Add(Row("FlashMotion", motion));
        var drift = Number("FlashDriftSpeed", options.DriftSpeed, .25, 3, value => options.DriftSpeed = value); drift.Increment = .25m;
        var gif = Number("FlashGifSpeed", options.GifSpeed, .25, 4, value => options.GifSpeed = value); gif.Increment = .25m;
        Toggle("FlashDraggable", options.Draggable, value => options.Draggable = value);
        Toggle("FlashShatter", options.Shatter, value => options.Shatter = value);
        Toggle("FlashGlow", options.Glow, value => options.Glow = value);
        Toggle("FlashRounded", options.RoundedCorners, value => options.RoundedCorners = value);
        Toggle("FlashAvoidCenter", settings.FlashAvoidCenter, value => settings.FlashAvoidCenter = value);
        Number("FlashCenterExclusion", settings.FlashCenterExclusionPercent, 5, 60, value => settings.FlashCenterExclusionPercent = (int)value);
    }
}
