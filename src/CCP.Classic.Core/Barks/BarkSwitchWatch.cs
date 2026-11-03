namespace CCP.Classic.Core;

// Turns the player's own switches into bark events: a switch that goes from off to on raises its key
// (Enabled.spiral, BarksOn, PanicOff...). Several switches flipping together are a preset or a reset,
// not one choice, so they rebase silently. Automatic changes (timed effect changes) call Rebase.
public sealed class BarkSwitchWatch(ClassicSettings settings, Func<bool> panicEnabled)
{
    private static readonly string[] Effects = ["flash", "sub", "spiral", "pink", "video", "bubble", "bounce", "lock", "wipe", "drain", "wall"];
    private Dictionary<string, bool> last = Read(settings, panicEnabled());

    public IReadOnlyList<string> Poll()
    {
        var now = Read(settings, panicEnabled());
        var changed = now.Count(pair => last.GetValueOrDefault(pair.Key) != pair.Value);
        var rising = now.Where(pair => pair.Value && !last.GetValueOrDefault(pair.Key)).Select(pair => Trigger(pair.Key)).ToArray();
        last = now;
        return changed is > 0 and <= 2 ? rising : [];
    }

    public void Rebase() => last = Read(settings, panicEnabled());

    private static string Trigger(string key) => key switch
    {
        "barks" => "BarksOn", "detached" => "Detached", "panicoff" => "PanicOff", _ => "Enabled." + key
    };

    private static Dictionary<string, bool> Read(ClassicSettings settings, bool panic)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["strict"] = settings.Effects.TryGetValue("video", out var video) && video.Enabled
                && video.Values.GetValueOrDefault("strict") != 0,
            ["keywords"] = settings.KeywordTriggersEnabled, ["allmonitors"] = settings.FillAllMonitors,
            ["startup"] = settings.StartWithSystem, ["autorun"] = settings.AutoRun, ["barks"] = settings.BarksEnabled,
            ["detached"] = settings.CompanionPlacement.Detached, ["panicoff"] = !panic
        };
        foreach (var id in Effects) values[id] = settings.Effects.TryGetValue(id, out var effect) && effect.Enabled;
        return values;
    }
}
