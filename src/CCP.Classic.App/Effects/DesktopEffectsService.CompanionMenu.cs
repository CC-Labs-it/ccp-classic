using CCP.Classic.App.Companion;

namespace CCP.Classic.App.Effects;

// The tube's right-click menu reaches the panel through the companion host.
public sealed partial class DesktopEffectsService
{
    internal event Action<CompanionMenuCommand>? CompanionMenu
    {
        add => companionHost.MenuCommand += value;
        remove => companionHost.MenuCommand -= value;
    }
    internal Func<CompanionSessionState>? CompanionMenuSession
    {
        get => companionHost.SessionState;
        set => companionHost.SessionState = value;
    }
}
