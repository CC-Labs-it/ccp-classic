namespace CCP.Classic.Core;

// Independent audio probability and visual strength match public CCP's Brain Drain controls.
public sealed class BrainDrainOptions
{
    public int BlurStrength { get; set; } = 50;
    public bool MeltEnabled { get; set; }
    public bool AllowCapture { get; set; }
    public List<string> AudioFolders { get; set; } = [];

    public void Normalize()
    {
        BlurStrength = Math.Clamp(BlurStrength, 0, 100);
        var library = new LibrarySettings { AudioFolders = AudioFolders ?? [] };
        library.Normalize(); AudioFolders = library.AudioFolders;
    }
}
