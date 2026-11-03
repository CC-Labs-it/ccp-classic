namespace CCP.Classic.App.Input;

// Only held physical keys are retained here. The trigger engine owns a bounded ephemeral text buffer.
internal sealed class KeywordInput(Action<string> emit)
{
    internal const string Clear = "\u001b";
    private readonly HashSet<int> held = [];
    private bool enabled, mapSeeded;
    private long? context;
    private int generation;
    internal int Generation => Volatile.Read(ref generation);

    internal bool SetEnabled(bool value)
    {
        if (value == enabled) return false;
        enabled = value; Interlocked.Increment(ref generation);
        if (enabled) emit(Clear);
        return true;
    }
    internal void SetContext(long value)
    {
        if (context == value) return;
        context = value; Interlocked.Increment(ref generation);
        if (enabled) emit(Clear);
    }
    internal void Seed(ReadOnlySpan<byte> map)
    {
        held.Clear();
        for (var code = 1; code < 256; code++) if (PanicKeyTransitions.IsDown(map, code)) held.Add(code);
        mapSeeded = true;
    }
    internal void Key(int code, bool down, bool optedIn, Func<string?> translate, bool shortcut = false)
    {
        SetEnabled(optedIn);
        if (code is <= 0 or >= 256) return;
        if (!down) { held.Remove(code); return; }
        if (!held.Add(code) || !enabled) return;
        emit(Filter(shortcut ? null : translate(), shortcut));
    }
    internal void ReadMap(ReadOnlySpan<byte> map, bool optedIn, Func<int, string?> translate, bool shortcut)
    {
        var changed = SetEnabled(optedIn);
        if (!mapSeeded || changed && enabled) { Seed(map); return; }
        var fresh = 0;
        var count = 0;
        for (var code = 1; code < 256; code++)
        {
            if (!PanicKeyTransitions.IsDown(map, code)) held.Remove(code);
            else if (held.Add(code)) { fresh = code; count++; }
        }
        if (!enabled || count == 0) return;
        // Polling cannot establish the order of simultaneous new presses. Never invent a word.
        emit(count == 1 ? Filter(shortcut ? null : translate(fresh), shortcut) : Clear);
    }
    internal static string Filter(string? text, bool shortcut = false) =>
        shortcut || string.IsNullOrEmpty(text) || text.Length > 8
        || text.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n')) ? Clear : text;

    internal static bool WindowsShortcut(int key, Func<int, bool> down) =>
        key is 0x11 or 0x12 or 0xa2 or 0xa3 or 0xa4 or 0xa5 or 0x5b or 0x5c
        || new[] { 0x11, 0x12, 0xa2, 0xa3, 0xa4, 0xa5, 0x5b, 0x5c }.Any(down);

    internal static string? X11Text(nuint symbol)
    {
        if (symbol is >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff) return ((char)symbol).ToString();
        if ((symbol & 0xff000000) == 0x01000000 && symbol is <= 0x0110ffff
            && (symbol & 0xffffff) is not (>= 0xd800 and <= 0xdfff))
            return char.ConvertFromUtf32((int)(symbol & 0xffffff));
        return symbol switch
        {
            0xff09 or 0xff89 => "\t", 0xff0d or 0xff8d => "\n", 0xff80 => " ",
            >= 0xffb0 and <= 0xffb9 => ((char)('0' + symbol - 0xffb0)).ToString(),
            0xffaa => "*", 0xffab => "+", 0xffac => ",", 0xffad => "-", 0xffae => ".", 0xffaf => "/", 0xffbd => "=",
            _ => null
        };
    }
}
