using Avalonia.Input;

namespace CCP.Classic.App;

public static class PanicKeys
{
    public static string Normalize(string? key)
    {
        var candidate = key?.Trim();
        // Match a named key first: Enum.TryParse also accepts numbers and combined names.
        if (candidate is null || !Enum.GetNames<Key>().Contains(candidate, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse<Key>(candidate, ignoreCase: true, out var parsed) || !IsBindable(parsed))
            return Key.Escape.ToString();
        return parsed.ToString();
    }

    public static bool IsBindable(Key key) => Enum.IsDefined(key) && key is not
        (Key.None or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
        or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.NoName);
}
