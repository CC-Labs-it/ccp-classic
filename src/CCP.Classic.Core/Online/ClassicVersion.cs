namespace CCP.Classic.Core.Online;

/// <summary>
/// A release version such as 1.0.0-beta.6, ordered by SemVer 2 precedence:
/// 1.0.0-beta.6 &lt; 1.0.0-beta.10 &lt; 1.0.0 &lt; 1.0.1. Build metadata after '+' is ignored.
/// </summary>
public readonly record struct ClassicVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<ClassicVersion>
{
    public static bool TryParse(string? value, out ClassicVersion version)
    {
        version = default;
        var text = (value ?? "").Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        var dash = text.IndexOf('-');
        var core = dash >= 0 ? text[..dash] : text;
        var prerelease = dash >= 0 ? text[(dash + 1)..] : "";
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!IsNumber(parts[i]) || !int.TryParse(parts[i], out numbers[i])) return false;
        if (dash >= 0 && (prerelease.Length == 0 || prerelease.Split('.').Any(id => id.Length == 0
            || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))) return false;
        version = new(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public int CompareTo(ClassicVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : ComparePrerelease(Prerelease ?? "", other.Prerelease ?? "");
    }

    public static bool operator >(ClassicVersion left, ClassicVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ClassicVersion left, ClassicVersion right) => left.CompareTo(right) < 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (string.IsNullOrEmpty(Prerelease) ? "" : "-" + Prerelease);

    private static bool IsNumber(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    // A release outranks every prerelease of the same number; identifiers compare one by one,
    // numbers numerically and below words, and a shorter list loses when the rest is equal.
    private static int ComparePrerelease(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return Math.Sign(right.Length.CompareTo(left.Length));
        var a = left.Split('.'); var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var numberA = IsNumber(a[i]); var numberB = IsNumber(b[i]);
            int result;
            if (numberA && numberB)
                result = a[i].TrimStart('0').Length != b[i].TrimStart('0').Length
                    ? a[i].TrimStart('0').Length.CompareTo(b[i].TrimStart('0').Length)
                    : string.CompareOrdinal(a[i].TrimStart('0'), b[i].TrimStart('0'));
            else if (numberA != numberB) result = numberA ? -1 : 1;
            else result = string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return Math.Sign(result);
        }
        return a.Length.CompareTo(b.Length);
    }
}
