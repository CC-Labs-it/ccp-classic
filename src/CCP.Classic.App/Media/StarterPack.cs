namespace CCP.Classic.App.Media;

public sealed record StarterPhrase(string Text, string? AudioPath);

public sealed class StarterPack
{
    public IReadOnlyList<StarterPhrase> Phrases { get; }
    public IReadOnlyList<string> Images { get; }

    // Root is the physical Starter folder, allowing a portable install and isolated tests.
    public StarterPack(string? root = null)
    {
        root ??= Path.Combine(AppContext.BaseDirectory, "Starter");
        Phrases = Array.AsReadOnly(new[] { "focus", "breathe", "relax", "listen", "deeper" }
            .Select(word =>
            {
                var file = Path.Combine(root, "Words", word + ".mp3");
                return new StarterPhrase(word.ToUpperInvariant(), File.Exists(file) ? file : null);
            }).ToArray());
        var image = Path.Combine(root, "Images", "spiral.png");
        Images = Array.AsReadOnly(File.Exists(image) ? new[] { image } : Array.Empty<string>());
    }
}
