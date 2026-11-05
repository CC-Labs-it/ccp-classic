namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    public bool PreviewVideo()
    {
        if (disposed || !videos.Preview()) return false;
        Bark("PreviewVideo"); return true;
    }
}
