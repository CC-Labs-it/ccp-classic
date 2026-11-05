namespace CCP.Classic.Core;

public enum SpiralCorner { TopLeft, TopRight, BottomLeft, BottomRight }
public sealed class CornerGifOptions
{
    public bool Enabled { get; set; }
    public string? Path { get; set; }
    public SpiralCorner Position { get; set; } = SpiralCorner.BottomLeft;
    public int Size { get; set; } = 300;
    public int Opacity { get; set; } = 18;
    public void Normalize()
    {
        Path = SpiralOptions.ImagePath(Path); if (!Enum.IsDefined(Position)) Position = SpiralCorner.BottomLeft;
        Size = Math.Clamp(Size, 100, 800); Opacity = Math.Clamp(Opacity, 5, 100);
    }
}
public sealed class SpiralOptions
{
    public string? Path { get; set; }
    public bool Randomize { get; set; }
    public bool UseProcedural { get; set; }
    public int TargetMonitor { get; set; } = -1;
    public bool SessionCornerGifAllowed { get; set; } = true;
    public List<string> Folders { get; set; } = [];
    public List<CornerGifOptions> Corners { get; set; } = [new(), new() { Position = SpiralCorner.BottomRight }];
    public static string DefaultFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CCP Classic", "Spirals");
    public void Normalize()
    {
        Path = ImagePath(Path); TargetMonitor = Math.Clamp(TargetMonitor, -2, 31);
        var library = new LibrarySettings { ImageFolders = Folders ?? [] }; library.Normalize(); Folders = library.ImageFolders;
        Corners = (Corners ?? []).Where(slot => slot is not null).Take(2).ToList();
        while (Corners.Count < 2) Corners.Add(new() { Position = Corners.Count == 0 ? SpiralCorner.BottomLeft : SpiralCorner.BottomRight });
        foreach (var slot in Corners) slot.Normalize();
    }
    public static string? ImagePath(string? path) => MediaLibrary.IsLocalPath(path)
        && System.IO.Path.GetExtension(path!).ToLowerInvariant() is ".gif" or ".webp" or ".png" or ".jpg" or ".jpeg" or ".bmp"
        ? System.IO.Path.GetFullPath(path!) : null;
}
