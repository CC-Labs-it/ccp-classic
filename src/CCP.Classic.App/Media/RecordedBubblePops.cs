namespace CCP.Classic.App.Media;

// CCP's original three recorded pops. The existing cue gate owns mute, gain and cancellation.
internal sealed class RecordedBubblePops
{
    private readonly Random random;
    public IReadOnlyList<string> Clips { get; }

    public RecordedBubblePops(string? directory = null, int? seed = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "Starter", "Audio", "Bubbles");
        Clips = Array.AsReadOnly(new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }
            .Select(name => Path.Combine(directory, name)).Where(File.Exists).ToArray());
        random = seed is { } value ? new Random(value) : Random.Shared;
    }

    public bool Play(AudioPlaybackGate gate, bool soundEnabled, double volume = .7) => soundEnabled && Clips.Count > 0
        && gate.Play(Clips[random.Next(Clips.Count)], Math.Clamp(volume, 0, 1));
}
