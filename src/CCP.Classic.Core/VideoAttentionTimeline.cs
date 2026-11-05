namespace CCP.Classic.Core;

public sealed record VideoAttentionTarget(long Id, string Text, double Spawned, double Expires, double X, double Y,
    double VelocityX, double VelocityY, double Opacity = 1)
{ public double? HitAt { get; init; } }

// Public CCP VideoService.SetupAttention/SpawnTarget and AttentionTargetVisual, fe40ea7.
public sealed class VideoAttentionTimeline(int seed = 0)
{
    private readonly Random random = new(seed);
    private readonly List<double> due = [];
    private readonly List<VideoAttentionTarget> live = [];
    private long nextId;
    private double previous;
    public int Spawned { get; private set; }
    public int Hits { get; private set; }
    public bool Passed => Spawned == 0 || Hits >= Spawned;
    public bool IsStarted { get; private set; }
    public IReadOnlyList<VideoAttentionTarget> Targets => live.Where(t => t.HitAt is null).ToArray();
    public IReadOnlyList<VideoAttentionTarget> VisualTargets => live.ToArray();
    public Func<string, (double Width, double Height)>? MeasureTarget { get; set; }
    public event Action<bool>? Answered;
    public event Action? TargetStarted;
    public void Start(double duration, VideoOptions options)
    {
        Clear();
        IsStarted = true;
        var count = options.RandomTargets ? random.Next(1, Math.Clamp(options.TargetCount, 1, 10) + 1) : Math.Clamp(options.TargetCount, 1, 10);
        for (var i = 0; i < count; i++) due.Add(3 + random.NextDouble() * Math.Max(1, duration - 8));
        due.Sort(); for (var i = 1; i < due.Count; i++) due[i] = Math.Max(due[i], due[i - 1] + 3);
    }
    public void Clear() { due.Clear(); live.Clear(); previous = 0; Spawned = Hits = 0; IsStarted = false; }
    public bool Hit(long id)
    {
        var index = live.FindIndex(t => t.Id == id && t.HitAt is null);
        if (index < 0) return false;
        live[index] = live[index] with { HitAt = previous }; Hits++; Answered?.Invoke(true); return true;
    }
    public void Finish()
    {
        foreach (var item in live.Where(t => t.HitAt is null).ToArray()) { live.Remove(item); Answered?.Invoke(false); }
        due.Clear();
    }
    public void Step(double elapsed, VideoOptions options, double width, double height, bool gentle, bool reduced)
    {
        elapsed = double.IsFinite(elapsed) ? Math.Max(previous, elapsed) : previous;
        var dt = Math.Clamp(elapsed - previous, 0, .1); previous = elapsed;
        if (dt <= 0) return;
        foreach (var item in live.Where(t => t.HitAt is null && elapsed >= t.Expires).ToArray())
        { live.Remove(item); Answered?.Invoke(false); }
        if (due.Count > 0 && elapsed >= due[0])
        {
            due.RemoveAt(0); Spawned++;
            var pool = options.TargetPhrases.Where(p => p.Value && !string.IsNullOrWhiteSpace(p.Key)).Select(p => p.Key).ToArray();
            var angle = random.NextDouble() * Math.Tau;
            live.Add(new(++nextId, pool.Length > 0 ? pool[random.Next(pool.Length)] : "Focus", elapsed,
                elapsed + Math.Clamp(options.TargetLifespan, 1, 30), random.NextDouble(), random.NextDouble(),
                Math.Cos(angle) * 187.5, Math.Sin(angle) * 187.5));
            TargetStarted?.Invoke();
        }
        for (var i = 0; i < live.Count; i++)
        {
            var t = live[i]; var scale = gentle ? .5 : 1;
            if (t.HitAt is { } hit)
            { var alpha = Math.Clamp(1 - (elapsed - hit) / (gentle ? .75 : .3), 0, 1);
                if (alpha <= 0) { live.RemoveAt(i--); continue; } live[i] = t with { Opacity = alpha }; continue; }
            if (reduced) continue;
            var measured = MeasureTarget?.Invoke(t.Text) ?? (150, 60);
            var travelX = Math.Max(1, width - measured.Item1 - Math.Min(40, width * .08) * 2);
            var travelY = Math.Max(1, height - measured.Item2 - Math.Min(30, height * .08) * 2);
            var x = t.X + t.VelocityX * dt * scale / travelX;
            var y = t.Y + t.VelocityY * dt * scale / travelY;
            var vx = t.VelocityX; var vy = t.VelocityY;
            if (x < 0 || x > 1) { vx = -vx; x = Math.Clamp(x, 0, 1); }
            if (y < 0 || y > 1) { vy = -vy; y = Math.Clamp(y, 0, 1); }
            live[i] = t with { X = x, Y = y, VelocityX = vx, VelocityY = vy };
        }
    }
}
