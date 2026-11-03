namespace CCP.Classic.Core;

// Pinned CCP AudioLayerTrack/LayeredAudioService defaults, without global output identity.
public sealed class AudioLayerTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    public int Volume { get; set; } = 70;
    public bool Enabled { get; set; } = true;
}

public sealed class LayeredAudioOptions
{
    public const int MaximumTracks = 8;
    public bool Enabled { get; set; }
    public int MasterVolume { get; set; } = 70;
    public bool AudioOnlySession { get; set; }
    public List<AudioLayerTrack> Tracks { get; set; } = [];
    public void Normalize()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0, 100);
        var ids = new HashSet<Guid>();
        Tracks = (Tracks ?? []).Where(track => track is not null).Take(MaximumTracks).ToList();
        foreach (var track in Tracks)
        {
            if (track.Id == Guid.Empty || !ids.Add(track.Id)) { track.Id = Guid.NewGuid(); ids.Add(track.Id); }
            track.Volume = Math.Clamp(track.Volume, 0, 100);
            track.Path = LocalAudio(track.Path) ?? "";
        }
    }
    public LayeredAudioOptions Copy()
    {
        var copy = new LayeredAudioOptions { Enabled = Enabled, MasterVolume = MasterVolume, AudioOnlySession = AudioOnlySession,
            Tracks = (Tracks ?? []).Where(track => track is not null).Take(MaximumTracks).Select(track => new AudioLayerTrack
            { Id = track.Id, Path = track.Path, Volume = track.Volume, Enabled = track.Enabled }).ToList() };
        copy.Normalize(); return copy;
    }
    private static string? LocalAudio(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl)
            || !Path.IsPathFullyQualified(path) || !MediaLibrary.IsLocalPath(path)) return null;
        try
        {
            return LocalAudioFormats.SupportsPath(path)
                ? Path.GetFullPath(path) : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }
}
