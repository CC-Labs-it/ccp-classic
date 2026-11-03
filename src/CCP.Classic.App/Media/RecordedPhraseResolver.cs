using CCP.Classic.Core;

namespace CCP.Classic.App.Media;

internal static class RecordedPhraseResolver
{
    // Mirrors CCP FindLinkedAudio: explicit local mapping, filename match, then approved neutral clip.
    public static StarterPhrase Resolve(SubliminalPhrase phrase, IReadOnlyList<StarterPhrase> neutral,
        IEnumerable<string> selectedAudio)
    {
        if (!string.IsNullOrWhiteSpace(phrase.AudioPath)) return new(phrase.Text, LocalAudioInput.LocalPath(phrase.AudioPath));
        var text = Key(phrase.Text);
        var matching = selectedAudio.Take(100000).FirstOrDefault(path => Key(Path.GetFileNameWithoutExtension(path)) == text);
        return new(phrase.Text, matching is not null ? LocalAudioInput.LocalPath(matching)
            : neutral.FirstOrDefault(item => Key(item.Text) == text)?.AudioPath);
    }
    private static string Key(string value) => value.Trim().Replace('\u2019', '\'').ToUpperInvariant();
}
