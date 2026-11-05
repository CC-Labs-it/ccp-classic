using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Companion;

// What the panel does for a tube menu choice. Placement choices stay inside the tube window.
internal enum CompanionMenuCommand { StartSession, StopSession, BarksChanged, AvatarHidden, OpenSettings }

// The session row: Idle offers Start, Running offers Stop, Locked shows Stop refused (panic is off),
// Unavailable hides the row (no panel answers it).
internal enum CompanionSessionState { Unavailable, Idle, Running, Locked }

// CCP's avatar tube right-click menu (AvatarContextMenu): dark body, pink rim and separators,
// rows that light on hover, green Start and red Stop. Only items Classic has are kept.
internal sealed class CompanionMenu
{
    internal const double ScaleStep = .25;
    private static readonly IBrush Rim = new SolidColorBrush(Color.Parse("#FF69B4"));
    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#121220"));
    private static readonly IBrush Plain = Brushes.White;
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#90EE90"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF6347"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#666666"));
    private readonly LocalizationService text = new();
    private readonly Dictionary<string, MenuItem> items = [];
    public ContextMenu Menu { get; }

    public CompanionMenu(Action<string> chosen)
    {
        Menu = new ContextMenu { Name = "CompanionMenu", Background = Body, BorderBrush = Rim, BorderThickness = new(2),
            CornerRadius = new(8), Padding = new(4), MinWidth = 190 };
        foreach (var (key, colour) in new (string, string)[]
        {
            ("MenuFlyoutPresenterBackground", "#121220"), ("MenuFlyoutPresenterBorderBrush", "#FF69B4"),
            ("MenuFlyoutItemBackground", "#00000000"), ("MenuFlyoutItemBackgroundPointerOver", "#3D3D60"),
            ("MenuFlyoutItemBackgroundPressed", "#4A4A72"), ("MenuFlyoutItemBackgroundDisabled", "#00000000"),
            ("MenuFlyoutItemForeground", "#FFFFFF"), ("MenuFlyoutItemForegroundPointerOver", "#FFFFFF"),
            ("MenuFlyoutItemForegroundPressed", "#FFFFFF"), ("MenuFlyoutItemForegroundDisabled", "#666666")
        })
            Menu.Resources[key] = new SolidColorBrush(Color.Parse(colour));
        var rows = new List<Control>
        {
            Row("Settings", chosen, bold: true), Line(),
            Row("Detach", chosen), Row("Attach", chosen), Row("Grow", chosen), Row("Shrink", chosen),
            Row("ResetSize", chosen), Row("AlwaysOnTop", chosen), Row("Hide", chosen),
            Line(), Row("Session", chosen), Row("Barks", chosen), Line(), Row("ShowPanel", chosen)
        };
        Menu.ItemsSource = rows;
    }

    internal MenuItem this[string name] => items[name];

    // Labels and states are read again on every open, so the menu always matches the settings.
    public void Refresh(ClassicSettings settings, CompanionSessionState session)
    {
        if (text.Language != settings.Language) text.SetLanguage(settings.Language);
        var placement = settings.CompanionPlacement; var detached = placement.Detached;
        Set("Settings", text["CompanionMenuSettings"], Rim);
        Set("Detach", text["CompanionMenuDetach"], Plain, visible: !detached);
        Set("Attach", text["CompanionMenuAttach"], Plain, visible: detached);
        Set("Grow", "+  " + text["CompanionMenuGrow"], Plain, enabled: placement.Scale < 1.5 - .001);
        Set("Shrink", "−  " + text["CompanionMenuShrink"], Plain, enabled: placement.Scale > .5 + .001);
        Set("ResetSize", text["CompanionMenuResetSize"], Plain, enabled: Math.Abs(placement.Scale - 1) > .001);
        Set("AlwaysOnTop", Check(placement.AlwaysOnTop) + text["AlwaysOnTop"], Plain, visible: detached);
        Set("Hide", "✕  " + text["CompanionMenuHide"], Plain);
        var running = session is CompanionSessionState.Running or CompanionSessionState.Locked;
        Set("Session", running ? "■  " + text["CompanionMenuStop"] : "▶  " + text["CompanionMenuStart"],
            running ? Red : Green, visible: session != CompanionSessionState.Unavailable, enabled: session != CompanionSessionState.Locked);
        Set("Barks", Check(settings.BarksEnabled) + text["Barks"], settings.BarksEnabled ? Plain : Red);
        Set("ShowPanel", text["CompanionMenuShowPanel"], Plain);
    }

    private static string Check(bool on) => (on ? "☑" : "☐") + "  ";

    private void Set(string name, string header, IBrush colour, bool visible = true, bool enabled = true)
    {
        var item = items[name]; var label = (TextBlock)item.Header!;
        label.Text = header; label.Foreground = enabled ? colour : Off;
        item.IsVisible = visible; item.IsEnabled = enabled;
    }

    private MenuItem Row(string name, Action<string> chosen, bool bold = false)
    {
        // The label carries its own colour so a coloured row keeps it under the pointer, as in CCP.
        var item = new MenuItem { Name = "CompanionMenu_" + name, Padding = new(10, 6),
            Header = new TextBlock { FontWeight = bold ? FontWeight.Bold : FontWeight.Normal } };
        item.Click += (_, args) => { args.Handled = true; chosen(name); };
        items[name] = item; return item;
    }

    private static Separator Line() => new() { Background = Rim, Height = 1, Margin = new Thickness(8, 4) };
}
