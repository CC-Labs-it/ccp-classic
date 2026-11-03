namespace CCP.Classic.Core;

public enum KnobKind { Range, Toggle, Choice, Action }
public sealed record KnobDefinition(string Key, string Label, KnobKind Kind,
    double Min = 0, double Max = 100, double Default = 0, string Unit = "", string[]? Choices = null);
public sealed record EffectDefinition(string Id, string Title, string Icon, bool DefaultEnabled,
    IReadOnlyList<KnobDefinition> Primary, IReadOnlyList<KnobDefinition> More, bool WindowsOnly = false);

public static class EffectCatalog
{
    private static KnobDefinition R(string key, string label, double min, double max, double value, string unit = "")
        => new(key, label, KnobKind.Range, min, max, value, unit);
    private static KnobDefinition T(string key, string label, bool value = false)
        => new(key, label, KnobKind.Toggle, 0, 1, value ? 1 : 0);
    private static KnobDefinition C(string key, string label, params string[] choices)
        => new(key, label, KnobKind.Choice, 0, choices.Length - 1, Choices: choices);

    public static IReadOnlyList<EffectDefinition> All { get; } =
    [
        new("flash", "FlashImages", "M9 1 3 9h5l-1 6 6-8H8z", true,
            [R("rate", "PerHour", 0, 60, 12), T("dismiss", "ClickToDismiss", true), R("size", "Size", 10, 100, 45, "%")],
            [R("maximum", "MaxOnScreen", 1, 8, 3), R("opacity", "Opacity", 10, 100, 85, "%"), R("fade", "Fade", 50, 1500, 300, "ms"), T("hydra", "Hydra")]),
        new("video", "MandatoryVideos", "M1 3h14v10H1z M6 6v4l4-2z", false,
            [R("rate", "PerHour", 0, 20, 2), T("strict", "StrictLock"), T("floating", "FloatingWindows", true)],
            [R("maximum", "MostAtOnce", 1, 4, 2), T("attention", "AttentionCheck", true), R("cap", "DurationCap", 1, 30, 8, "min"), T("launch", "StartOnLaunch")]),
        new("sub", "Subliminals", "M2 4h12 M2 8h8 M2 12h10", true,
            [R("rate", "PerMinute", 0, 30, 6), R("opacity", "Opacity", 5, 100, 40, "%"), T("whispers", "Whispers")],
            [R("frames", "Frames", 1, 12, 3), C("colour", "TextColour", "Pink", "White", "SoftGrey")]),
        new("spiral", "Spiral", "M8 8c1 0 2 1 2 2 0 2-2 3-4 2C1 10 2 4 6 3c5-2 9 3 8 7", true,
            [R("opacity", "Opacity", 0, 100, 25, "%"), C("spiral", "Spiral", "SpiralOne", "SpiralTwo", "SpiralThree"), new("loom", "MakeYourOwn", KnobKind.Action)], []),
        new("pink", "PinkFilter", "M8 2a6 6 0 1 0 0 12 6 6 0 0 0 0-12", true,
            [R("opacity", "Opacity", 0, 60, 12, "%")], [T("follow", "FollowSession", true)]),
        new("bubble", "BubblePop", "M7 4a5 5 0 1 0 0 10A5 5 0 0 0 7 4 M12 1a2 2 0 1 0 0 4 2 2 0 0 0 0-4", true,
            [R("rate", "PerMinute", 0, 30, 8), T("click", "ClickToPop", true), T("sound", "Sound", true)], []),
        new("lock", "LockCard", "M3 7h10v7H3z M5 7V5a3 3 0 0 1 6 0v2", false,
            [R("interval", "Every", 5, 120, 30, "min"), R("repeats", "Repeats", 1, 10, 3), T("strict", "Strict")], []),
        new("bounce", "BouncingText", "M2 13 6 5l3 6 2-4 3 6", false,
            [R("speed", "Speed", 1, 10, 4), R("size", "Size", 10, 100, 40, "%"), R("opacity", "Opacity", 10, 100, 70, "%")], []),
        new("wipe", "MindWipe", "M2 6c2-2 4-2 6 0s4 2 6 0 M2 10c2-2 4-2 6 0s4 2 6 0", false,
            [R("interval", "Every", 5, 120, 45, "min"), R("volume", "Volume", 0, 100, 60, "%"), T("loop", "Loop")], []),
        new("drain", "BrainDrain", "M5 3C1 3 1 9 5 9h6c4 0 4-6 0-6z M8 9v5 M5 12l3 2 3-2", false,
            [R("intensity", "Intensity", 0, 100, 30, "%"), T("refresh", "HighRefresh")], []),
        new("audio", "Audio", "M2 6h3l4-3v10l-4-3H2z M12 5a4 4 0 0 1 0 6", true,
            [R("master", "Master", 0, 100, 70, "%"), R("video", "Video", 0, 100, 60, "%"), R("duck", "Duck", 0, 100, 40, "%")],
            [C("output", "OutputDevice", "SystemDefault")]),
        new("wall", "Wallpaper", "M1 2h14v11H1z M2 11l4-5 4 4 2-2 2 3", false,
            [C("mode", "Mode", "Static", "Dynamic")], [], true)
    ];
}
