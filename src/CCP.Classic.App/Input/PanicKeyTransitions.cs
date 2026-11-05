namespace CCP.Classic.App.Input;

internal sealed class PanicKeyTransitions(Action<string, bool> changed)
{
    private readonly Dictionary<int, string> pressed = [];
    internal void Down(int code, string name)
    {
        if (code > 0 && pressed.TryAdd(code, name)) changed(name, true);
    }
    internal void Seed(int code, string name)
    {
        if (code > 0) pressed.TryAdd(code, name);
    }
    internal void Bind(ReadOnlySpan<byte> map, int code, string name)
    {
        if (IsDown(map, code)) Seed(code, name);
        else if (code > 0) Up(code, name);
    }
    internal void Up(int code, string? releaseName = null)
    {
        if (pressed.Remove(code, out var name)) changed(name, false);
        else if (releaseName is not null) changed(releaseName, false);
    }
    internal void ReadMap(ReadOnlySpan<byte> map, int code, string name)
    {
        foreach (var held in pressed.Keys.ToArray())
            if (!IsDown(map, held)) Up(held);
        if (IsDown(map, code)) Down(code, name);
    }
    internal static bool IsDown(ReadOnlySpan<byte> map, int code) =>
        code is > 0 and < 256 && map.Length >= 32 && (map[code >> 3] & (1 << (code & 7))) != 0;
}
