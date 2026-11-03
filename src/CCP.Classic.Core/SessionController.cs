namespace CCP.Classic.Core;

// One policy owns every user stop. Closing the application always disposes resources.
public sealed class SessionController(ClassicSettings settings, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly HashSet<string> held = new(StringComparer.OrdinalIgnoreCase);
    private long started;
    public bool IsRunning { get; private set; }
    public bool PanicEnabled { get; private set; } = true;
    public bool EndedBySchedule { get; private set; }
    public bool CompletedNormally { get; private set; }
    public TimeSpan Elapsed => IsRunning ? time.GetElapsedTime(started) : TimeSpan.Zero;
    public event Action? Changed;
    public event Action? StopRequested;

    public void Start()
    {
        if (IsRunning) return;
        started = time.GetTimestamp();
        EndedBySchedule = false;
        CompletedNormally = false;
        IsRunning = true;
        Changed?.Invoke();
    }

    public bool Stop() => Stop(finishUntilStopped: false);

    public bool Stop(bool finishUntilStopped)
    {
        if (!PanicEnabled) return false;
        StopAll(finished: finishUntilStopped && settings.RunMinutes == 0);
        return true;
    }

    public bool KeyDown(string key)
    {
        if (!held.Add(key) || !string.Equals(key, settings.PanicKey, StringComparison.OrdinalIgnoreCase)) return false;
        return Stop();
    }

    public bool ConsumesKey(string key) => PanicEnabled
        && string.Equals(key, settings.PanicKey, StringComparison.OrdinalIgnoreCase);

    public void KeyUp(string key) => held.Remove(key);
    public void ReleaseKeys() => held.Clear();
    public void SetPanicEnabled(bool enabled, bool warningAcknowledged = false)
    {
        if (!enabled && !warningAcknowledged) return;
        PanicEnabled = enabled;
        Changed?.Invoke();
    }

    public void Tick()
    {
        // A preset's scheduled end remains valid even after its owner disables manual panic.
        if (IsRunning && settings.RunMinutes > 0 && Elapsed >= TimeSpan.FromMinutes(settings.RunMinutes)) StopAll(timed: true);
    }

    private void StopAll(bool timed = false, bool finished = false)
    {
        EndedBySchedule = timed;
        CompletedNormally = timed || finished;
        IsRunning = false;
        settings.KeywordTriggersEnabled = false;
        StopRequested?.Invoke();
        Changed?.Invoke();
    }

    public void Dispose() => StopAll();
}
