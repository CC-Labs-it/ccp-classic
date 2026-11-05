using Avalonia.Input;

namespace CCP.Classic.App.Input;

internal static class NativePanicKeys
{
    private static readonly HashSet<string> Names = new(Enum.GetNames<Key>(), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Key, (int Windows, string X11)> Codes = new()
    {
        [Key.Cancel] = (0x03, "Cancel"), [Key.Back] = (0x08, "BackSpace"), [Key.Tab] = (0x09, "Tab"),
        [Key.Clear] = (0x0C, "Clear"), [Key.Return] = (0x0D, "Return"), [Key.Pause] = (0x13, "Pause"),
        [Key.CapsLock] = (0x14, "Caps_Lock"), [Key.Escape] = (0x1B, "Escape"), [Key.Space] = (0x20, "space"),
        [Key.PageUp] = (0x21, "Prior"), [Key.PageDown] = (0x22, "Next"), [Key.End] = (0x23, "End"),
        [Key.Home] = (0x24, "Home"), [Key.Left] = (0x25, "Left"), [Key.Up] = (0x26, "Up"),
        [Key.Right] = (0x27, "Right"), [Key.Down] = (0x28, "Down"), [Key.Select] = (0x29, "Select"),
        [Key.Print] = (0x2A, "Print"), [Key.Execute] = (0x2B, "Execute"), [Key.Snapshot] = (0x2C, "Print"),
        [Key.Insert] = (0x2D, "Insert"), [Key.Delete] = (0x2E, "Delete"), [Key.Help] = (0x2F, "Help"),
        [Key.Apps] = (0x5D, "Menu"), [Key.Sleep] = (0x5F, "XF86Sleep"),
        [Key.Multiply] = (0x6A, "KP_Multiply"), [Key.Add] = (0x6B, "KP_Add"),
        [Key.Separator] = (0x6C, "KP_Separator"), [Key.Subtract] = (0x6D, "KP_Subtract"),
        [Key.Decimal] = (0x6E, "KP_Decimal"), [Key.Divide] = (0x6F, "KP_Divide"),
        [Key.NumLock] = (0x90, "Num_Lock"), [Key.Scroll] = (0x91, "Scroll_Lock"),
        [Key.BrowserBack] = (0xA6, "XF86Back"), [Key.BrowserForward] = (0xA7, "XF86Forward"),
        [Key.BrowserRefresh] = (0xA8, "XF86Refresh"), [Key.BrowserStop] = (0xA9, "XF86Stop"),
        [Key.BrowserSearch] = (0xAA, "XF86Search"), [Key.BrowserFavorites] = (0xAB, "XF86Favorites"),
        [Key.BrowserHome] = (0xAC, "XF86HomePage"), [Key.VolumeMute] = (0xAD, "XF86AudioMute"),
        [Key.VolumeDown] = (0xAE, "XF86AudioLowerVolume"), [Key.VolumeUp] = (0xAF, "XF86AudioRaiseVolume"),
        [Key.MediaNextTrack] = (0xB0, "XF86AudioNext"), [Key.MediaPreviousTrack] = (0xB1, "XF86AudioPrev"),
        [Key.MediaStop] = (0xB2, "XF86AudioStop"), [Key.MediaPlayPause] = (0xB3, "XF86AudioPlay"),
        [Key.LaunchMail] = (0xB4, "XF86Mail"), [Key.SelectMedia] = (0xB5, "XF86AudioMedia"),
        [Key.LaunchApplication1] = (0xB6, "XF86Launch0"), [Key.LaunchApplication2] = (0xB7, "XF86Launch1"),
        [Key.OemSemicolon] = (0xBA, "semicolon"), [Key.OemPlus] = (0xBB, "equal"),
        [Key.OemComma] = (0xBC, "comma"), [Key.OemMinus] = (0xBD, "minus"),
        [Key.OemPeriod] = (0xBE, "period"), [Key.OemQuestion] = (0xBF, "slash"),
        [Key.OemTilde] = (0xC0, "grave"), [Key.OemOpenBrackets] = (0xDB, "bracketleft"),
        [Key.OemPipe] = (0xDC, "backslash"), [Key.OemCloseBrackets] = (0xDD, "bracketright"),
        [Key.OemQuotes] = (0xDE, "apostrophe"), [Key.OemBackslash] = (0xE2, "less")
    };

    internal static bool TryMap(string name, out int windows, out string? x11)
    {
        windows = 0; x11 = null;
        if (!Names.Contains(name) || !Enum.TryParse<Key>(name, true, out var key) || !PanicKeys.IsBindable(key)) return false;
        name = key.ToString();
        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z')
            (windows, x11) = (name[0], name.ToLowerInvariant());
        else if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            (windows, x11) = (name[1], name[1..]);
        else if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
            (windows, x11) = (0x70 + f - 1, name);
        else if (name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 && char.IsAsciiDigit(name[6]))
            (windows, x11) = (0x60 + name[6] - '0', "KP_" + name[6]);
        else if (Codes.TryGetValue(key, out var code)) (windows, x11) = code;
        return windows != 0 && x11 is not null;
    }

    internal static bool IsLocalDisplay(string? display)
    {
        if (display is null) return false;
        if (display.StartsWith("unix/", StringComparison.Ordinal)) display = display[5..];
        else if (display.StartsWith("unix", StringComparison.Ordinal)) display = display[4..];
        if (!display.StartsWith(':')) return false;
        var parts = display[1..].Split('.');
        return parts.Length is 1 or 2 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }
}
