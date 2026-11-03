using Avalonia;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls.HelpLoops;

internal sealed partial class LoopFrame
{
    public LocalizationService? TextService { get; init; }
    public string Label(string key) => TextService?[key] ?? key;
    private string Resolve(string value)
    {
        var key = value.ToUpperInvariant() switch
        {
            "RELAX" => "Relax", "DEEPER" => "Deeper", "BREATHE" => "HelpBreathe",
            "FOCUS" => "HelpFocus", "LISTEN" => "HelpListen", "SAVE" or "REMEMBER ME" => "Save",
            "STRICT: NO SKIP" or "STRICT" => "Strict", "WATCHING" => "AttentionCheck",
            "SPIRAL OPACITY" or "PINK OPACITY" or "OPACITY" => "Opacity",
            "FLASH SIZE" => "Size", "WIPE VOLUME" => "Volume", "BLUR" => "Intensity",
            "AUDIO" => "Audio", "CCP" => "MandatoryVideos", "DUCK 70%" => "Duck", "LISTENING" => "KeywordTriggers",
            "CALM" => "Gentle", "EVENING" => "Daily", "MINE" => "MyPreset",
            "SAVED" => "Saved", "PRESETS" => "Presets", "HYDRA X2" => "Hydra",
            _ => value.StartsWith("TRIGGER: ", StringComparison.Ordinal) ? "TriggerPhrases" : null
        };
        return key is null ? value : Label(key);
    }
}

internal sealed partial class LoopPalette
{
    public static LinearGradientBrush Linear(Color first, Color second, double angle) =>
        Linear(new GradientStops { new(first, 0), new(second, 1) }, angle);
    public static LinearGradientBrush Linear(Color first, Color second, Point start, Point end) => new()
    {
        GradientStops = [new(first, 0), new(second, 1)],
        StartPoint = new RelativePoint(start, RelativeUnit.Relative), EndPoint = new RelativePoint(end, RelativeUnit.Relative)
    };
    public static LinearGradientBrush Linear(GradientStops stops, double angle)
    {
        var a = angle * Math.PI / 180;
        var d = new Vector(Math.Cos(a), Math.Sin(a)) * .5;
        return new() { GradientStops = stops,
            StartPoint = new RelativePoint(.5 - d.X, .5 - d.Y, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.5 + d.X, .5 + d.Y, RelativeUnit.Relative) };
    }
}
