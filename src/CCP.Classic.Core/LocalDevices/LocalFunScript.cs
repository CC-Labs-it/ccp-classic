using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

// Pinned public CCP FunScript interpolation and speed mapping, using the existing JSON runtime.
public sealed class LocalFunScript
{
    public readonly record struct ActionPoint(int AtMilliseconds, int Position);
    public IReadOnlyList<ActionPoint> Actions { get; }
    public int DurationMilliseconds => Actions[^1].AtMilliseconds;
    private LocalFunScript(ActionPoint[] actions) => Actions = Array.AsReadOnly(actions);
    public static bool TryParse(string json, out LocalFunScript? script)
    {
        script = null; if (string.IsNullOrWhiteSpace(json) || json.Length > 10 * 1024 * 1024) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 }); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return false;
            var inverted = root.TryGetProperty("inverted", out var flag) && (flag.ValueKind == JsonValueKind.True
                || flag.ValueKind == JsonValueKind.Number && flag.TryGetDouble(out var numeric) && numeric != 0
                || flag.ValueKind == JsonValueKind.String && bool.TryParse(flag.GetString(), out var boolean) && boolean);
            var result = new List<ActionPoint>();
            foreach (var action in actions.EnumerateArray().Take(250000))
            {
                if (action.ValueKind != JsonValueKind.Object || !action.TryGetProperty("at", out var at) || !action.TryGetProperty("pos", out var pos)
                    || !Number(at, out var time) || !Number(pos, out var position) || !double.IsFinite(time) || !double.IsFinite(position)
                    || time < 0 || time > int.MaxValue) continue;
                position = Math.Clamp(position, 0, 100); if (inverted) position = 100 - position;
                result.Add(new((int)Math.Round(time), (int)Math.Round(position)));
            }
            if (result.Count == 0) return false;
            script = new(result.OrderBy(point => point.AtMilliseconds).ToArray()); return true;
        }
        catch (JsonException) { return false; }
    }
    private static bool Number(JsonElement value, out double number) => value.ValueKind == JsonValueKind.Number
        ? value.TryGetDouble(out number) : double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
    public double PositionAt(double milliseconds)
    {
        if (!double.IsFinite(milliseconds)) return 0;
        if (milliseconds <= Actions[0].AtMilliseconds) return Actions[0].Position / 100d;
        if (milliseconds >= DurationMilliseconds) return Actions[^1].Position / 100d;
        var index = UpperBound(milliseconds); var before = Actions[index - 1]; var after = Actions[index];
        var span = after.AtMilliseconds - before.AtMilliseconds;
        return span <= 0 ? after.Position / 100d : Math.Clamp((before.Position + (after.Position - before.Position)
            * (milliseconds - before.AtMilliseconds) / span) / 100d, 0, 1);
    }
    public double IntensityAt(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || Actions.Count < 2 || milliseconds < Actions[0].AtMilliseconds || milliseconds >= DurationMilliseconds) return 0;
        var index = UpperBound(milliseconds); var before = Actions[index - 1]; var after = Actions[index];
        var span = after.AtMilliseconds - before.AtMilliseconds;
        var speed = span <= 0 ? 0 : Math.Abs(after.Position - before.Position) * 1000d / span;
        return Math.Clamp((speed - 10) / 490, 0, 1);
    }
    private int UpperBound(double milliseconds)
    {
        var low = 0; var high = Actions.Count - 1;
        while (low < high) { var middle = (low + high) / 2; if (Actions[middle].AtMilliseconds > milliseconds) high = middle; else low = middle + 1; }
        return low;
    }
    public static string[] CandidatePaths(string videoPath)
    {
        if (!MediaLibrary.IsLocalPath(videoPath)) return [];
        var directory = Path.GetDirectoryName(videoPath); var stem = Path.GetFileNameWithoutExtension(videoPath);
        return string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem) ? []
            : [Path.Combine(directory, stem + ".funscript"), Path.Combine(directory, "funscripts", stem + ".funscript")];
    }
}
