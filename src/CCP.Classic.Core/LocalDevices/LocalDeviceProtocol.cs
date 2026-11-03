using System.Globalization;
using System.Text.Json;

namespace CCP.Classic.Core.LocalDevices;

public enum LocalActuatorKind { Vibrate, Rotate, Oscillate, Position, Constrict, Thrust, Finger, Suction, Pump, Depth, Stroke }
public sealed record LocalActuator(int Index, LocalActuatorKind Kind, int Minimum, int Maximum, string Command,
    int MinimumDuration = 0, int MaximumDuration = 1000);
public sealed record LocalDeviceInfo(string Id, string Name, IReadOnlyList<LocalActuator> Actuators)
{
    public LocalDeviceProvider Provider { get; init; }
}

// Public CCP's ProviderV2 names an architecture. Intiface wire is spec4, not spec2.
// Sources: buttplug.io/docs/spec/{device_information,output}/ and developer.lovense.com/docs/cam-solutions/basic-api
public static class LocalDeviceProtocol
{
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 16 };
    public static JsonElement Parse(string json)
    {
        if (json.Length > 65536) throw new IOException("Device response is too large");
        using var document = JsonDocument.Parse(json, JsonOptions); return document.RootElement.Clone();
    }
    private static JsonElement Unwrap(JsonElement value) => value.ValueKind == JsonValueKind.String ? Parse(value.GetString()!) : value;
    private static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var text)
        ? text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : text.ToString() : "";
    private static string Name(string value) => new(value.Where(c => !char.IsControl(c)).Take(80).ToArray());
    private static int? Integer(JsonElement value) => int.TryParse(value.ToString(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var number) ? number : null;

    public static IReadOnlyList<LocalDeviceInfo> IntifaceDevices(JsonElement body)
    {
        if (!body.TryGetProperty("Devices", out var devices) || devices.ValueKind != JsonValueKind.Object)
            throw new IOException("Invalid Intiface device list");
        var result = new List<LocalDeviceInfo>();
        foreach (var device in devices.EnumerateObject().Take(16))
        {
            if (!int.TryParse(device.Name, out var deviceIndex) || deviceIndex < 0 || device.Value.ValueKind != JsonValueKind.Object
                || !device.Value.TryGetProperty("DeviceIndex", out var actualIndex) || Integer(actualIndex) != deviceIndex
                || !device.Value.TryGetProperty("DeviceFeatures", out var features) || features.ValueKind != JsonValueKind.Object) continue;
            var actuators = new List<LocalActuator>();
            foreach (var feature in features.EnumerateObject().Take(16))
            {
                if (!int.TryParse(feature.Name, out var index) || index < 0 || feature.Value.ValueKind != JsonValueKind.Object
                    || !feature.Value.TryGetProperty("FeatureIndex", out var actualFeature) || Integer(actualFeature) != index
                    || !feature.Value.TryGetProperty("Output", out var output) || output.ValueKind != JsonValueKind.Object) continue;
                foreach (var item in output.EnumerateObject())
                {
                    var command = item.Name;
                    if (command is not ("Vibrate" or "Rotate" or "Oscillate" or "Position" or "HwPositionWithDuration" or "Constrict")) continue;
                    if (command == "Position" && output.TryGetProperty("HwPositionWithDuration", out _)) continue;
                    if (!item.Value.TryGetProperty("Value", out var range) || range.ValueKind != JsonValueKind.Array || range.GetArrayLength() != 2
                        || Integer(range[0]) is not { } minimum || Integer(range[1]) is not { } maximum
                        || minimum > 0 || minimum < -65535 || maximum <= 0 || maximum > 65535) continue;
                    var kind = command == "HwPositionWithDuration" ? LocalActuatorKind.Position : Enum.Parse<LocalActuatorKind>(command);
                    var durationMin = 0; var durationMax = 1000;
                    if (item.Value.TryGetProperty("Duration", out var duration) && duration.ValueKind == JsonValueKind.Array && duration.GetArrayLength() == 2)
                    { durationMin = Math.Clamp(Integer(duration[0]) ?? 0, 0, 60000); durationMax = Math.Clamp(Integer(duration[1]) ?? 1000, durationMin, 60000); }
                    actuators.Add(new(index, kind, minimum, maximum, command, durationMin, durationMax));
                }
            }
            var display = Text(device.Value, "DeviceDisplayName"); if (string.IsNullOrWhiteSpace(display)) display = Text(device.Value, "DeviceName");
            if (actuators.Count > 0) result.Add(new(device.Name, Name(display), actuators.Take(16).ToList().AsReadOnly()));
        }
        return result.AsReadOnly();
    }

    public static IReadOnlyList<LocalDeviceInfo> LovenseDevices(JsonElement root)
    {
        CheckLovense(root);
        var container = root.TryGetProperty("data", out var data) ? Unwrap(data) : root;
        if (container.ValueKind != JsonValueKind.Object) throw new IOException("Invalid Lovense device list");
        var toys = container.TryGetProperty("toys", out var nested) ? Unwrap(nested) : container;
        if (toys.ValueKind != JsonValueKind.Object) throw new IOException("Invalid Lovense toy list");
        var result = new List<LocalDeviceInfo>();
        foreach (var entry in toys.EnumerateObject().Take(16))
        {
            var toy = entry.Value; if (toy.ValueKind != JsonValueKind.Object) continue;
            var status = Text(toy, "status"); if (status.Length > 0 && status is not ("1" or "true")) continue;
            var id = Text(toy, "id"); if (id.Length == 0) id = entry.Name;
            if (id.Length > 128 || id.Any(char.IsControl)) continue;
            var model = Text(toy, "name"); var display = Text(toy, "nickName"); if (string.IsNullOrWhiteSpace(display)) display = model;
            var names = toy.TryGetProperty("shortFunctionNames", out var shortNames) ? shortNames
                : toy.TryGetProperty("fullFunctionNames", out var fullNames) ? fullNames : default;
            var codes = names.ValueKind == JsonValueKind.Array ? names.EnumerateArray().Take(16).Select(v => v.ToString())
                : Fallback(model);
            var actuators = codes.Select(Function).Where(a => a is not null).Cast<LocalActuator>()
                .DistinctBy(a => a.Command).Take(16).ToList();
            if (actuators.Count > 0) result.Add(new(id, Name(display), actuators.AsReadOnly()) { Provider = LocalDeviceProvider.LovenseConnect });
        }
        return result.AsReadOnly();
    }

    public static void CheckLovense(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("code", out var code) && Integer(code) is not (0 or 200))
            throw new IOException("Lovense rejected the command");
    }

    private static IEnumerable<string> Fallback(string model) => model.ToLowerInvariant().Replace(" ", "") switch
    {
        "nora" => ["v", "r"], "max" or "max2" => ["v", "p"], "edge" or "edge2" or "gemini" => ["v1", "v2"],
        "lapis" => ["v1", "v2", "v3"], "solace" => ["t", "d"], "solacepro" => ["t", "pos"],
        "gravity" or "vulse" => ["v", "t"], "flexer" => ["v", "f"], "tenera" or "tenera2" => ["s"], _ => ["v"]
    };

    private static LocalActuator? Function(string raw)
    {
        var code = raw.Trim().ToLowerInvariant(); var stem = code.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var motor = stem.Length == code.Length ? 0 : int.TryParse(code.AsSpan(stem.Length), out var parsed) ? parsed : -1;
        if (motor is < 0 or > 8) return null;
        var command = stem switch { "v" or "vibrate" => "Vibrate", "r" or "rotate" => "Rotate", "t" or "thrusting" => "Thrusting",
            "f" or "fingering" => "Fingering", "s" or "suction" => "Suction", "o" or "oscillate" => "Oscillate",
            "p" or "pump" => "Pump", "d" or "depth" => "Depth", "pos" or "position" => "Position", "stroke" => "Stroke",
            "c" or "constrict" => "Constrict", _ => "" };
        if (command.Length == 0) return null;
        var kind = command switch { "Thrusting" => LocalActuatorKind.Thrust, "Fingering" => LocalActuatorKind.Finger,
            _ => Enum.Parse<LocalActuatorKind>(command) };
        return new(Math.Max(0, motor - 1), kind, 0, kind is LocalActuatorKind.Pump or LocalActuatorKind.Depth ? 3
            : kind is LocalActuatorKind.Position or LocalActuatorKind.Stroke ? 100 : 20,
            command + (motor == 0 ? "" : motor.ToString(CultureInfo.InvariantCulture)));
    }

    public static int NativeLevel(LocalActuator actuator, double intensity) => (int)Math.Clamp(Math.Round(
        (double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0) * actuator.Maximum, MidpointRounding.AwayFromZero), 0, actuator.Maximum);

    public static string LovenseAction(LocalDeviceInfo device, double intensity) => string.Join(',', device.Actuators.Select(a =>
        a.Command + ":" + (a.Kind == LocalActuatorKind.Stroke
            ? "0-" + Math.Max(20, NativeLevel(a, intensity)).ToString(CultureInfo.InvariantCulture)
            : NativeLevel(a, intensity).ToString(CultureInfo.InvariantCulture))));
}
