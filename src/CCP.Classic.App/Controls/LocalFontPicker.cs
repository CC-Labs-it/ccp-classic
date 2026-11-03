using Avalonia.Media;

namespace CCP.Classic.App.Controls;

// CCP FontPickerHelper's installed-family picker and bundled Fredoka, without WPF.
public static class LocalFontPicker
{
    public const string BundledFredoka = "Fredoka";
    private static IReadOnlyList<string>? families;
    public static IReadOnlyList<string> Families => families ??= Enumerate();
    public static FontFamily Resolve(string? name)
    {
        if (name == BundledFredoka) return new FontFamily("avares://CCP.Classic.App/Assets/Fonts#Fredoka");
        return !string.IsNullOrWhiteSpace(name) && Families.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? new FontFamily(name + ", Inter") : new FontFamily("Inter");
    }
    private static IReadOnlyList<string> Enumerate()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Inter", BundledFredoka };
        try { foreach (var family in FontManager.Current.SystemFonts) result.Add(family.Name); }
        catch (InvalidOperationException) { }
        return Array.AsReadOnly(result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
