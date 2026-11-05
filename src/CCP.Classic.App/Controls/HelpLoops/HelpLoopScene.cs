namespace CCP.Classic.App.Controls.HelpLoops;

internal sealed record HelpLoopStep(string LocKey, double StartMs, double EndMs);

internal abstract class HelpLoopScene
{
    public abstract string Id { get; }
    public abstract double DurationMs { get; }
    public abstract double StillMs { get; }
    public abstract IReadOnlyList<HelpLoopStep> Steps { get; }
    public abstract void Draw(LoopFrame frame, double milliseconds);
    public virtual void Reset() { }
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> Types = new(() =>
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(HelpLoopScene).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(HelpLoopScene).IsAssignableFrom(type)))
        {
            var scene = (HelpLoopScene)Activator.CreateInstance(type, nonPublic: true)!;
            map.Add(scene.Id, type);
        }
        return map;
    });

    public static HelpLoopScene? Create(string? id) => id is not null && Types.Value.TryGetValue(id, out var type)
        ? (HelpLoopScene?)Activator.CreateInstance(type, nonPublic: true) : null;
}
