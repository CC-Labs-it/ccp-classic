using System.Text.Json;

namespace CCP.Classic.Core;

public sealed class ProgressData
{
    public int SchemaVersion { get; set; } = 1;
    public double LifetimeXp { get; set; }
    public double SessionSeconds { get; set; }
    public double PinkSeconds { get; set; }
    public double SpiralSeconds { get; set; }
    public double VideoSeconds { get; set; }
    public double LongestSessionSeconds { get; set; }
    public long VideosFinished { get; set; }
    public long BubblesPopped { get; set; }
    public long FlashesShown { get; set; }
    public int ConsecutiveDays { get; set; }
    public DateOnly? LastLaunchDay { get; set; }
    public DateTimeOffset? LastPanicUtc { get; set; }
    public int BubbleCountCorrectStreak { get; set; }
    public int BubbleCountBestStreak { get; set; }
    public long AttentionFailures { get; set; }
    public long LockCardsCompleted { get; set; }
    public HashSet<string> UnlockedAchievements { get; set; } = [];

    public void Normalize()
    {
        LifetimeXp = Clean(LifetimeXp); SessionSeconds = Clean(SessionSeconds);
        PinkSeconds = Clean(PinkSeconds); SpiralSeconds = Clean(SpiralSeconds);
        VideoSeconds = Clean(VideoSeconds); LongestSessionSeconds = Clean(LongestSessionSeconds);
        VideosFinished = Math.Clamp(VideosFinished, 0, 1_000_000_000_000);
        BubblesPopped = Math.Clamp(BubblesPopped, 0, 1_000_000_000_000);
        FlashesShown = Math.Clamp(FlashesShown, 0, 1_000_000_000_000);
        AttentionFailures = Math.Clamp(AttentionFailures, 0, 1_000_000_000_000);
        LockCardsCompleted = Math.Clamp(LockCardsCompleted, 0, 1_000_000_000_000);
        ConsecutiveDays = Math.Clamp(ConsecutiveDays, 0, 1_000_000);
        BubbleCountCorrectStreak = Math.Clamp(BubbleCountCorrectStreak, 0, 1_000_000);
        BubbleCountBestStreak = Math.Clamp(BubbleCountBestStreak, BubbleCountCorrectStreak, 1_000_000);
        UnlockedAchievements = (UnlockedAchievements ?? []).Where(id => id is { Length: > 0 and <= 64 }
            && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')).Take(27).ToHashSet(StringComparer.Ordinal);
    }

    internal static double Clean(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1e15) : 0;
}

public sealed class ProgressStore(string path)
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 16 };
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "progress.json");

    public ProgressData Load(out bool recovered)
    {
        recovered = false;
        try
        {
            if (!File.Exists(path)) return new();
            using var stream = File.OpenRead(path);
            var bytes = new byte[MaximumBytes + 1]; var count = 0;
            while (count < bytes.Length)
            {
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) throw new JsonException("Progress is too large");
            var data = JsonSerializer.Deserialize<ProgressData>(bytes.AsSpan(0, count), Options)
                ?? throw new JsonException("Missing progress");
            if (data.SchemaVersion != 1) throw new JsonException("Unsupported progress version");
            data.Normalize(); return data;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        { recovered = true; return new(); }
    }

    public void Save(ProgressData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.SchemaVersion != 1) throw new ArgumentException("Unsupported progress version", nameof(data));
        data.Normalize();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Options);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("Progress is too large", nameof(data));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!; Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public enum ProgressEventKind { Xp, Launch, SessionStarted, SessionEnded, Time, Flash, Bubble, Video }
public readonly record struct ProgressEvent(ProgressEventKind Kind, double Amount = 0);
public readonly record struct EffectProgressState(bool PinkActive, bool SpiralActive,
    bool BubblesActive = false, bool BouncingTextActive = false, bool StrictLock = false, bool PanicDisabled = false);
public sealed record CompletedSession(string Preset, double Seconds, bool PanicDisabled, bool StrictLock,
    bool SwitchedAway, DateTimeOffset FinishedAt);

// Runtime-only counters are kept outside the durable model so restarts cannot complete a continuous task.
public sealed partial class ProgressTracker
{
    public const int MaximumLevel = 500;
    private readonly TimeProvider clock;
    private long started, lastTick;
    private string preset = "";
    private bool switchedAway;
    private EffectProgressState currentEffects;
    public ProgressData Data { get; }
    public bool IsRunning { get; private set; }
    public double ContinuousSpiralSeconds { get; private set; }
    public event Action? Changed;
    public event Action<ProgressEvent>? Recorded;
    public event Action<CompletedSession>? SessionCompleted;
    public int Level => DeriveLevel(Data.LifetimeXp).Level;
    public double XpIntoLevel => DeriveLevel(Data.LifetimeXp).XpIntoLevel;
    public double XpForNextLevel => GetXpForLevel(Level);

    public ProgressTracker(ProgressData? data = null, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        Data = data ?? new(); Data.Normalize();
        RecordLaunch();
    }

    // Original public CCP v5 curve. No ranks, account state or feature gating are carried over.
    public static double GetXpForLevel(int level)
    {
        level = Math.Clamp(level, 1, MaximumLevel);
        return level switch
        {
            <= 80 => Math.Round(800 + (level - 1) * (1700.0 / 79)),
            <= 100 => Math.Round(2500 + (level - 80) * (1500.0 / 20)),
            <= 125 => Math.Round(4000 + (level - 100) * (2000.0 / 25)),
            <= 150 => Math.Round(6000 + (level - 125) * (4000.0 / 25)),
            _ => Math.Round(10000 * Math.Pow(1.03, level - 150))
        };
    }

    public static (int Level, double XpIntoLevel) DeriveLevel(double lifetimeXp)
    {
        var remaining = ProgressData.Clean(lifetimeXp); var level = 1;
        while (level < MaximumLevel && remaining >= GetXpForLevel(level))
        { remaining -= GetXpForLevel(level); level++; }
        return (level, remaining);
    }

    public void AddXp(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return;
        Data.LifetimeXp = ProgressData.Clean(Data.LifetimeXp + amount);
        Notify(new(ProgressEventKind.Xp, amount));
    }

    public void RecordLaunch()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (Data.LastLaunchDay == today) return;
        Data.ConsecutiveDays = Data.LastLaunchDay == today.AddDays(-1)
            ? Math.Min(Data.ConsecutiveDays + 1, 1_000_000) : 1;
        Data.LastLaunchDay = today; Notify(new(ProgressEventKind.Launch));
    }

    public void StartSession(string presetName)
    {
        if (IsRunning) return;
        preset = presetName ?? ""; switchedAway = false; ContinuousSpiralSeconds = 0;
        currentEffects = default; started = lastTick = clock.GetTimestamp(); IsRunning = true;
        Notify(new(ProgressEventKind.SessionStarted));
    }

    public void Tick(bool pinkActive, bool spiralActive) => Tick(new EffectProgressState(pinkActive, spiralActive));
    public void Tick(EffectProgressState effects)
    {
        if (!IsRunning) return;
        var now = clock.GetTimestamp();
        var seconds = Math.Max(0, clock.GetElapsedTime(lastTick, now).TotalSeconds); lastTick = now;
        Data.SessionSeconds = ProgressData.Clean(Data.SessionSeconds + seconds);
        if (effects.PinkActive) Data.PinkSeconds = ProgressData.Clean(Data.PinkSeconds + seconds);
        if (effects.SpiralActive)
        {
            Data.SpiralSeconds = ProgressData.Clean(Data.SpiralSeconds + seconds);
            ContinuousSpiralSeconds += seconds;
        }
        else ContinuousSpiralSeconds = 0;
        currentEffects = effects; EvaluateFrame(effects);
        if (seconds > 0) Notify(new(ProgressEventKind.Time, seconds));
    }

    public void SwitchedToOtherApplication() { if (IsRunning) switchedAway = true; }

    public void EndSession(bool naturalCompletion = false, bool panicDisabled = false, bool strictLock = false)
    {
        if (!IsRunning) return;
        Tick(currentEffects);
        var seconds = Math.Max(0, clock.GetElapsedTime(started).TotalSeconds);
        IsRunning = false; ContinuousSpiralSeconds = 0;
        if (naturalCompletion)
        {
            Data.LongestSessionSeconds = Math.Max(Data.LongestSessionSeconds, seconds);
            var completed = new CompletedSession(preset, seconds, panicDisabled, strictLock, switchedAway, clock.GetLocalNow());
            EvaluateSession(completed); SessionCompleted?.Invoke(completed);
        }
        Notify(new(ProgressEventKind.SessionEnded, seconds));
    }

    public void FlashShown() { if (IsRunning) { Data.FlashesShown++; Notify(new(ProgressEventKind.Flash)); } }
    public void BubblePopped() { if (IsRunning) { Data.BubblesPopped++; Notify(new(ProgressEventKind.Bubble)); } }
    public void VideoFinished(double seconds)
    {
        if (!IsRunning || !double.IsFinite(seconds) || seconds <= 0) return;
        Data.VideosFinished++; Data.VideoSeconds = ProgressData.Clean(Data.VideoSeconds + seconds);
        Notify(new(ProgressEventKind.Video, seconds));
    }

    private void Notify(ProgressEvent progressEvent)
    {
        EvaluateProgress(progressEvent); Recorded?.Invoke(progressEvent); Changed?.Invoke();
    }
    partial void EvaluateProgress(ProgressEvent progressEvent);
    partial void EvaluateFrame(EffectProgressState effects);
    partial void EvaluateSession(CompletedSession session);
}
