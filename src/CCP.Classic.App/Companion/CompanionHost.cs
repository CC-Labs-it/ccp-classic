using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Classic.Core;

namespace CCP.Classic.App.Companion;

// Keeps the tube alive while Avatar is on and the app is open. Idle frames come from a light
// timer; a running session drives frames and barks itself. Stopping a session never closes it.
internal sealed class CompanionHost : IDisposable
{
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly Func<Window, CompanionWindow> create;
    private readonly TimeProvider clock;
    private readonly long epoch;
    private readonly DispatcherTimer idle;
    private bool session, disposed;
    public CompanionWindow? Window { get; private set; }
    public event Action? StopRequested, AvatarClicked, PlacementChanged;
    // One monotonic clock for idle and session frames; session time restarts at zero.
    public double Now => clock.GetElapsedTime(epoch).TotalSeconds;
    internal bool IdleRunning => idle.IsEnabled;

    public CompanionHost(Window owner, ClassicSettings settings, Func<Window, CompanionWindow> create, TimeProvider? clock = null)
    {
        this.owner = owner; this.settings = settings; this.create = create; this.clock = clock ?? TimeProvider.System;
        epoch = this.clock.GetTimestamp();
        idle = new(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => IdleTick());
    }

    public void Refresh()
    {
        if (disposed || !settings.CompanionEnabled) { Release(); return; }
        if (Window is null)
        {
            Window = create(owner);
            Window.StopRequested += () => StopRequested?.Invoke();
            Window.AvatarClicked += () => AvatarClicked?.Invoke();
            Window.PlacementChanged += () => PlacementChanged?.Invoke();
        }
        if (!session) IdleTick();
    }

    public void BeginSession() { session = true; idle.Stop(); }

    public void SessionFrame(CompanionBarkCue? bark)
    {
        if (settings.CompanionEnabled != (Window is not null)) Refresh();
        Window?.Update(bark, Now);
    }

    // Barks and talk end at once; the tube returns to idle.
    public void EndSession() { session = false; Refresh(); }

    internal void IdleTick()
    {
        if (session || disposed || Window is not { } window) { idle.Stop(); return; }
        window.Update(null, Now);
        // About 20 fps while she moves or loads; a still frame only polls window state.
        var interval = TimeSpan.FromMilliseconds(window.WantsFrames ? 50 : 250);
        if (idle.Interval != interval) idle.Interval = interval;
        if (!idle.IsEnabled) idle.Start();
    }

    private void Release() { idle.Stop(); Window?.FinishImmediately(); Window = null; }

    public void Dispose() { if (disposed) return; disposed = true; Release(); }
}
