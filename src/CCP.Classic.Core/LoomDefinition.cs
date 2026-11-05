using System.Text.Json;

namespace CCP.Classic.Core;

// Native schema2 counterpart of CCP's public loomField.js. Saved definitions contain no media paths.
public sealed record LoomLayer
{
    public int Arms { get; init; } = 4;
    public double Turns { get; init; } = 2;
    public double Duty { get; init; } = .5;
    public string Style { get; init; } = "log";
    public int Direction { get; init; } = 1;
    public string[] Colors { get; init; } = ["#ff69b4"];
    public string BandMode { get; init; } = "hard";
    public int SpeedMul { get; init; } = 1;
    public bool Enabled { get; init; }
    public LoomLayer Normalize(string fallbackColor = "#ff69b4") => this with
    {
        Arms = Math.Clamp(Arms, 1, 12), Turns = LoomDefinition.Number(Turns, 2, .5, 6, .25),
        Duty = LoomDefinition.Number(Duty, .5, .2, .8, .05), Direction = Direction == -1 ? -1 : 1,
        Style = LoomDefinition.Styles.Contains(Style) ? Style : "log", SpeedMul = Math.Clamp(SpeedMul, 1, 3),
        BandMode = BandMode == "gradient" ? "gradient" : "hard",
        Colors = (Colors ?? []).Where(LoomDefinition.IsHex).Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Take(6).DefaultIfEmpty(fallbackColor).ToArray()
    };
}
public sealed record LoomBackground(string Kind = "solid", string Color = "#14060f", string Outer = "#08040e");
public sealed record LoomPulse(double Amp = 0, int Cycles = 1);
public sealed record LoomWobble(double Amp = 0, int Freq = 3, int Cycles = 1);
public sealed record LoomCenterpiece(string Kind = "none", string Color = "#ffffff", double SizeFrac = .16, string Text = "", int FlashCycles = 1);

public sealed record LoomDefinition
{
    public static IReadOnlyList<string> Styles { get; } = Array.AsReadOnly(new[] { "log", "arch", "ribbon", "golden", "tunnel", "petal" });
    public int Schema { get; init; } = 2;
    public string Format { get; init; } = "square";
    public int Speed { get; init; } = 3;
    public LoomLayer Layer { get; init; } = new();
    public LoomLayer Layer2 { get; init; } = new() { Duty = .35, Direction = -1, Colors = ["#8a5cff"] };
    public LoomBackground Bg { get; init; } = new();
    public double Glow { get; init; }
    public LoomPulse Pulse { get; init; } = new();
    public LoomWobble Wobble { get; init; } = new();
    public int HueCycles { get; init; }
    public LoomCenterpiece Centerpiece { get; init; } = new();
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, MaxDepth = 16 };

    public LoomDefinition Normalize()
    {
        if (Schema is not (1 or 2)) throw new ArgumentException("Unsupported spiral version");
        var bg = Bg ?? new(); var pulse = Pulse ?? new(); var wobble = Wobble ?? new(); var cp = Centerpiece ?? new();
        return this with
        {
            Schema = 2, Format = Format is "wide" or "tall" ? Format : "square", Speed = Math.Clamp(Speed, 1, 5),
            Layer = (Layer ?? new()).Normalize(), Layer2 = (Layer2 ?? new() { Colors = ["#8a5cff"] }).Normalize("#8a5cff"),
            Bg = new(bg.Kind == "radial" ? "radial" : "solid", Hex(bg.Color, "#14060f"), Hex(bg.Outer, "#08040e")),
            Glow = Number(Glow, 0, 0, 1), Pulse = new(Number(pulse.Amp, 0, 0, .25), Math.Clamp(pulse.Cycles, 1, 4)),
            Wobble = new(Number(wobble.Amp, 0, 0, .35), Math.Clamp(wobble.Freq, 1, 6), Math.Clamp(wobble.Cycles, 1, 3)),
            HueCycles = Math.Clamp(HueCycles, 0, 2),
            Centerpiece = new(cp.Kind is "dot" or "star" or "cross" or "x" or "mantra" ? cp.Kind : "none",
                Hex(cp.Color, "#ffffff"), Number(cp.SizeFrac, .16, .08, .4),
                new string((cp.Text ?? "").Where(character => !char.IsControl(character)).Take(12).ToArray()), Math.Clamp(cp.FlashCycles, 0, 4))
        };
    }
    public static LoomDefinition Parse(string json)
    {
        if (json.Length > 65536) throw new JsonException("Spiral is too large");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Missing spiral");
        if (root.TryGetProperty("schema", out var version)
            && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var known) || known is not (1 or 2)))
            throw new JsonException("Unsupported spiral version");
        var v1 = root.TryGetProperty("schema", out var schema) && schema.TryGetInt32(out var value) && value == 1
            || !root.TryGetProperty("layer", out _) && (root.TryGetProperty("arms", out _) || root.TryGetProperty("colors", out _));
        if (v1)
        {
            var layer = JsonSerializer.Deserialize<LoomLayer>(json, JsonOptions) ?? new();
            var speed = root.TryGetProperty("speed", out var s) && s.TryGetInt32(out var n) ? n : 3;
            var bg = root.TryGetProperty("bg", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "#14060f";
            return new LoomDefinition { Layer = layer, Speed = speed, Bg = new(Color: bg) }.Normalize();
        }
        return (JsonSerializer.Deserialize<LoomDefinition>(json, JsonOptions) ?? new()).Normalize();
    }
    internal static double Number(double value, double fallback, double min, double max, double step = 0)
    {
        var finite = double.IsFinite(value) ? value : fallback;
        return Math.Clamp(step > 0 ? Math.Floor(finite / step + .5) * step : finite, min, max);
    }
    internal static bool IsHex(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);
    private static string Hex(string? value, string fallback) => IsHex(value) ? value!.ToLowerInvariant() : fallback;
}

public readonly record struct LoomBand(bool Lit, int ColorIndex);

// These band equations match the additive exports in loomField.js, independent of a drawing backend.
public static class LoomMath
{
    private const double Golden = 1.9459101;
    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double Fract(double value) => value - Math.Floor(value);
    public static double StyleT(double radius, string style)
    {
        var r = Unit(radius);
        return style switch { "arch" => r, "ribbon" => .5 - Math.Sin(Math.Asin(1 - 2 * r) / 3),
            "golden" or "tunnel" or "petal" => Math.Log(1 + 6 * r) / Golden, _ => Math.Pow(r, .5714286) };
    }
    public static double StyleR(double t, string style)
    {
        var x = Unit(t);
        return style switch { "arch" => x, "ribbon" => (1 - Math.Sin(3 * Math.Asin(.5 - x))) / 2,
            "golden" or "tunnel" or "petal" => (Math.Exp(x * Golden) - 1) / 6, _ => Math.Pow(x, 1.75) };
    }
    public static double Span(LoomLayer layer)
    {
        var arms = Math.Clamp(layer.Arms, 1, 12); var count = Math.Clamp(layer.Colors?.Length ?? 1, 1, 6);
        // Radial tunnel bands repeat after a whole color cycle even when the arm count is odd.
        return Math.Tau / arms * (layer.Style == "tunnel" || arms % count == 0 ? count : arms);
    }
    public static double Rotation(LoomLayer layer, double phase) => (layer.Direction == -1 ? -1 : 1)
        * Unit(phase) * Span(layer) * Math.Clamp(layer.SpeedMul, 1, 3);
    public static int LoopMilliseconds(int speed) => Math.Clamp(speed, 1, 5) switch { 1 => 3600, 2 => 2520, 3 => 1800, 4 => 1080, _ => 720 };
    public static (int Width, int Height) Dimensions(string format, int longSide)
    {
        var side = Math.Clamp(longSide, 16, 512); var shortSide = (int)Math.Floor(side * 9d / 16 + .5);
        return format switch { "wide" => (side, shortSide), "tall" => (shortSide, side), _ => (side, side) };
    }
    public static LoomBand BandAt(LoomLayer layer, double radius, double theta, double rotation = 0, double wobble = 0)
    {
        if (!double.IsFinite(theta + rotation + wobble)) return new(false, 0);
        var arms = Math.Clamp(layer.Arms, 1, 12); var count = Math.Clamp(layer.Colors?.Length ?? 1, 1, 6);
        var r = Unit(radius); var t = StyleT(r, layer.Style);
        var turns = LoomDefinition.Number(layer.Turns, 2, .5, 6); var duty = LoomDefinition.Number(layer.Duty, .5, .2, .8);
        double position;
        if (layer.Style == "tunnel")
        {
            var helix = Math.Floor((turns - .5) / count + .5) * count;
            position = t * arms - rotation * arms / Math.Tau + helix * theta / Math.Tau + wobble * .5;
        }
        else if (layer.Style == "petal")
        {
            var angle = theta - rotation - Math.Tau * turns * t * .35 + wobble;
            return new(.5 + .5 * Math.Cos(angle * arms) >= 1 - duty && r > .03,
                (int)Math.Floor(Fract(angle / Math.Tau) * arms + .5) % count);
        }
        else position = Fract((theta - rotation - Math.Tau * turns * t + wobble) / Math.Tau) * arms;
        var index = (int)Math.Floor(position);
        return new(Fract(position) < duty, (index % count + count) % count);
    }
}

public sealed record SavedLoom(string Id, string Name, LoomDefinition Definition);

public sealed class LoomStore(string directory)
{
    public const int MaximumSpirals = 12;
    public static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "spirals");
    public IReadOnlyList<SavedLoom> LoadAll() => LoadAll(out _);
    public IReadOnlyList<SavedLoom> LoadAll(out bool recovered)
    {
        recovered = false; var result = new List<SavedLoom>();
        try
        {
            CheckDirectory();
            if (!Directory.Exists(directory)) return result.AsReadOnly();
            var examined = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (++examined > 48) { recovered = true; break; }
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked spiral");
                    using var stream = File.OpenRead(path);
                    var bytes = new byte[65537]; var count = stream.ReadAtLeast(bytes, 65537, throwOnEndOfStream: false);
                    if (count > 65536) throw new JsonException("Spiral is too large");
                    var saved = JsonSerializer.Deserialize<SavedLoom>(bytes.AsSpan(0, count), LoomDefinition.JsonOptions) ?? throw new JsonException("Missing spiral");
                    if (!ValidId(saved.Id) || saved.Id != Path.GetFileNameWithoutExtension(path) || result.Count >= MaximumSpirals)
                        throw new JsonException("Invalid spiral identifier or library capacity");
                    result.Add(new(saved.Id, Name(saved.Name), saved.Definition?.Normalize() ?? throw new JsonException("Missing spiral definition")));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { recovered = true; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { recovered = true; }
        return result.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    public SavedLoom Save(string name, LoomDefinition definition, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        id ??= Guid.NewGuid().ToString("N");
        if (!ValidId(id)) throw new ArgumentException("Invalid spiral identifier", nameof(id));
        CheckDirectory();
        var target = Path.Combine(directory, id + ".json");
        var existing = false;
        try { if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked spiral"); existing = true; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        if (!existing && LoadAll().Count >= MaximumSpirals) throw new IOException("Spiral library is full");
        var saved = new SavedLoom(id, Name(name), definition.Normalize());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(saved, LoomDefinition.JsonOptions);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return saved;
    }
    public void Delete(string id)
    {
        if (!ValidId(id)) throw new ArgumentException("Invalid spiral identifier", nameof(id));
        CheckDirectory(); File.Delete(Path.Combine(directory, id + ".json"));
    }
    private static bool ValidId(string? id) => Guid.TryParseExact(id, "N", out var guid) && guid.ToString("N") == id;
    private static string Name(string? value)
    {
        var name = value?.Trim();
        return !string.IsNullOrEmpty(name) && name.Length <= 80 && !name.Any(char.IsControl) ? name : throw new ArgumentException("Invalid spiral name");
    }
    private void CheckDirectory()
    {
        if (!MediaLibrary.IsLocalPath(directory)) throw new IOException("Spirals require a local folder");
        var full = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network) throw new IOException("Remote spiral folder");
        var ancestors = new Stack<string>();
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        foreach (var ancestor in ancestors)
        {
            try { if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked spiral folder"); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        }
    }
}
