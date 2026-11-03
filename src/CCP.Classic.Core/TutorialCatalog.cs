namespace CCP.Classic.Core;

public sealed record TutorialTopic(string Id, string TitleKey, string BodyKey, string? SceneId);

// Scene identifiers follow the public CCP help registry, not the obsolete local movie names.
public static class TutorialCatalog
{
    public static IReadOnlyList<TutorialTopic> All { get; } = Array.AsReadOnly(new TutorialTopic[]
    {
        new("guide", "HelpGuide", "HelpGuideBody", "FlashImages"),
        new("flash", "FlashImages", "HelpFlashBody", "FlashImages"),
        new("video", "MandatoryVideos", "HelpVideoBody", "Video"),
        new("sub", "Subliminals", "HelpSubBody", "Subliminals"),
        new("spiral", "Spiral", "HelpSpiralBody", "SpiralOverlay"),
        new("pink", "PinkFilter", "HelpPinkBody", "PinkFilter"),
        new("bubble", "BubblePop", "HelpBubbleBody", "BubblePop"),
        new("lock", "LockCard", "HelpLockBody", "LockCard"),
        new("bounce", "BouncingText", "HelpBounceBody", "BouncingText"),
        new("wipe", "MindWipe", "HelpWipeBody", "MindWipe"),
        new("drain", "BrainDrain", "HelpDrainBody", "BrainDrain"),
        new("audio", "Audio", "HelpAudioBody", "Audio"),
        new("wall", "Wallpaper", "HelpWallBody", null),
        new("Library", "Library", "HelpLibraryBody", "FlashImages"),
        new("Companion", "Companion", "HelpCompanionBody", null),
        new("Toys", "Toys", "HelpToysBody", null),
        new("Presets", "Presets", "HelpPresetsBody", "Presets"),
        new("Progress", "Progress", "HelpProgressBody", "BubblePop"),
        new("Settings", "Settings", "HelpSettingsBody", null)
    });

    public static TutorialTopic Find(string? id) => All.FirstOrDefault(topic =>
        string.Equals(topic.Id, id == "Studio" ? "guide" : id == "MakeYourOwn" ? "spiral" : id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(topic.TitleKey, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static IReadOnlyList<TutorialTopic> ForPlatform(bool windows) =>
        windows ? All : All.Where(topic => topic.Id != "wall").ToArray();
}
