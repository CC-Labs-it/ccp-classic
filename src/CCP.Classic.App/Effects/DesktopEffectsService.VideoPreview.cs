namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    public bool PreviewVideo() => !disposed && videos.Preview();
}
