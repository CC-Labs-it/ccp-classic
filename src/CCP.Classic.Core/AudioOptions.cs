namespace CCP.Classic.Core;

public sealed class AudioOptions
{
    // Output identity is global to this computer and deliberately stays outside presets.
    public string OutputDeviceId { get; set; } = "";
    public bool DuckingEnabled { get; set; } = true;
    public void Normalize()
    {
        if (OutputDeviceId is null || OutputDeviceId.Length > 1024 || OutputDeviceId.Any(char.IsControl)) OutputDeviceId = "";
    }
}
