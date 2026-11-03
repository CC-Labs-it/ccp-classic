using System.Reflection;
using System.Text.Json;

namespace CCP.Classic.Core;

// One recorded line: the bubble text, its clip under Starter/Barks and the clip's length.
public sealed record BarkLine(string Text, string Clip, double Seconds)
{
    // The clip's stem is the line id, as in CCP (flash_2.mp3 -> flash_2).
    public string LineId => Path.GetFileNameWithoutExtension(Clip);
}
public readonly record struct BarkRange(double Min, double Max)
{
    public bool Contains(double value) => double.IsFinite(value) && value >= Min && value <= Max;
}
// Always: a direct answer to the player (a click, a level, a lock card) skips the global gap,
// never its own cooldown. Repeatable false: once per session (app run for idle-time events).
public sealed record BarkRule(string Id, string Trigger, int Priority, double CooldownSeconds, bool Repeatable, double Chance,
    bool Always, string Mood, IReadOnlyDictionary<string, BarkRange> When, IReadOnlyList<BarkLine> Lines);

// The curated, neutral Circe lines (docs/BARKS.md). Loaded once from the embedded barks.json.
public sealed class BarkLibrary
{
    private static readonly Lazy<BarkLibrary> shipped = new(() =>
    {
        using var stream = typeof(BarkLibrary).Assembly.GetManifestResourceStream("CCP.Classic.Core.Barks.barks.json")
            ?? throw new InvalidOperationException("barks.json is not embedded");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });
    private readonly Dictionary<string, BarkRule[]> byTrigger;
    private readonly HashSet<string> texts;
    public static BarkLibrary Default => shipped.Value;
    public IReadOnlyList<BarkRule> Rules { get; }

    public BarkLibrary(IEnumerable<BarkRule> rules)
    {
        Rules = rules.Where(rule => rule.Lines.Count > 0 && rule.Trigger.Length > 0).ToArray();
        byTrigger = Rules.GroupBy(rule => rule.Trigger, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.Priority).ToArray(), StringComparer.Ordinal);
        texts = new(Rules.SelectMany(rule => rule.Lines).Select(line => line.Text), StringComparer.Ordinal);
    }

    // Highest priority first.
    public IReadOnlyList<BarkRule> For(string trigger) => byTrigger.TryGetValue(trigger, out var rules) ? rules : [];
    public bool IsApproved(string text) => texts.Contains(text);

    public static BarkLibrary Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rules = new List<BarkRule>();
        foreach (var item in document.RootElement.GetProperty("rules").EnumerateArray())
        {
            var when = new Dictionary<string, BarkRange>(StringComparer.Ordinal);
            if (item.TryGetProperty("when", out var conditions))
                foreach (var condition in conditions.EnumerateObject())
                    when[condition.Name] = new(condition.Value[0].GetDouble(), condition.Value[1].GetDouble());
            var lines = item.GetProperty("lines").EnumerateArray()
                .Select(line => new BarkLine(line.GetProperty("text").GetString() ?? "", line.GetProperty("clip").GetString() ?? "",
                    line.GetProperty("seconds").GetDouble()))
                .Where(line => line.Text.Length > 0 && line.Clip.Length > 0).ToArray();
            rules.Add(new(item.GetProperty("id").GetString() ?? "", item.GetProperty("trigger").GetString() ?? "",
                item.GetProperty("priority").GetInt32(), item.GetProperty("cooldown").GetDouble(), item.GetProperty("repeatable").GetBoolean(),
                item.GetProperty("chance").GetDouble(), item.GetProperty("always").GetBoolean(),
                item.TryGetProperty("mood", out var mood) ? mood.GetString() ?? "" : "", when, lines));
        }
        return new(rules);
    }
}
