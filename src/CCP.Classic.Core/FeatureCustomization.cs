using System.Text.Json;

namespace CCP.Classic.Core;

// Shared by saved presets. Account, device connection and global listening state stay outside presets.
public sealed class FeatureCustomization
{
    public bool AudioDuckingEnabled { get; set; } = true;
    public LockCardOptions LockCard { get; set; } = new();
    public BouncingTextOptions BouncingText { get; set; } = new();
    public BrainDrainOptions BrainDrain { get; set; } = new();
    public SubliminalOptions Subliminal { get; set; } = new();
    public PinkFilterOptions PinkFilter { get; set; } = new();
    public MindWipeOptions MindWipe { get; set; } = new();
    public KeywordOptions Keywords { get; set; } = new();
    public BubbleOptions Bubbles { get; set; } = new();
    public SpiralOptions Spiral { get; set; } = new();
    public VideoOptions Video { get; set; } = new();
    public WallpaperOptions Wallpaper { get; set; } = new();
    public Flash.FlashOptions Flash { get; set; } = new();
    public static FeatureCustomization Capture(ClassicSettings settings) => Copy(new()
    {
        AudioDuckingEnabled = settings.Audio.DuckingEnabled,
        LockCard = settings.LockCard, BouncingText = settings.BouncingText, BrainDrain = settings.BrainDrain,
        Subliminal = settings.Subliminal, PinkFilter = settings.PinkFilter, MindWipe = settings.MindWipe, Keywords = settings.Keywords,
        Bubbles = settings.Bubbles, Flash = settings.Flash, Spiral = settings.Spiral, Video = settings.Video, Wallpaper = settings.Wallpaper
    });
    public static FeatureCustomization Copy(FeatureCustomization source)
    {
        var copy = JsonSerializer.Deserialize<FeatureCustomization>(JsonSerializer.Serialize(source))!;
        copy.LockCard ??= new(); copy.LockCard.Normalize(); copy.BouncingText ??= new(); copy.BouncingText.Normalize();
        copy.BrainDrain ??= new(); copy.BrainDrain.Normalize(); copy.Subliminal ??= new(); copy.Subliminal.Normalize();
        copy.PinkFilter ??= new(); copy.PinkFilter.Normalize(); copy.MindWipe ??= new(); copy.MindWipe.Normalize();
        copy.Keywords ??= new(); copy.Keywords.Normalize(); copy.Bubbles ??= new(); copy.Bubbles.Normalize();
        copy.Spiral ??= new(); copy.Spiral.Normalize();
        copy.Video ??= new(); copy.Video.Normalize();
        copy.Wallpaper ??= new(); copy.Wallpaper.Normalize();
        copy.Flash ??= new(); copy.Flash.Normalize(); return copy;
    }
    public void Apply(ClassicSettings target)
    {
        target.Audio.DuckingEnabled = AudioDuckingEnabled;
        var copy = Copy(this); target.LockCard = copy.LockCard; target.BouncingText = copy.BouncingText;
        target.BrainDrain = copy.BrainDrain; target.Subliminal = copy.Subliminal; target.PinkFilter = copy.PinkFilter;
        target.MindWipe = copy.MindWipe; target.Keywords = copy.Keywords;
        target.Bubbles = copy.Bubbles;
        target.Flash = copy.Flash;
        target.Spiral = copy.Spiral;
        target.Video = copy.Video;
        target.Wallpaper = copy.Wallpaper;
    }
}
