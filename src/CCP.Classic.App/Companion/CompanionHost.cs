using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Classic.Core;

namespace CCP.Classic.App.Companion;

// Keeps the tube alive while Avatar is on and the app is open. Idle frames and idle barks come from a
// light timer; a running session drives frames and barks itself. Stopping a session never closes it.
internal sealed class CompanionHost : IDisposable
{
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly Func<Window, CompanionWindow> create;
    private readonly TimeProvider clock;
    private readonly long epoch;
    private readonly DispatcherTimer idle;
    private readonly CompanionTimeline barks;
    private long spoken;
    private bool session, disposed;
    public CompanionWindow? Window { get; private set; }
    public event Action? StopRequested, AvatarClicked, PlacementChanged;
    // A new idle word to voice. Session words are voiced by the session itself.
    public event Action<CompanionBarkCue>? IdleBark;
    // Right-click menu work for the panel, and the panel's answer for the menu's session row.
    public event Action<CompanionMenuCommand>? MenuCommand;
    public Func<CompanionSessionState>? SessionState { get; set; }
    // One monotonic clock for idle and session frames; session time restarts at zero.
    public double Now => clock.GetElapsedTime(epoch).TotalSeconds;
    internal bool IdleRunning => idle.IsEnabled;

    // The timeline is shared with the session: one word rotation, one bark at a time.
    public CompanionHost(Window owner, ClassicSettings settings, Func<Window, CompanionWindow> create, TimeProvider? clock = null,
        CompanionTimeline? barks = null)
    {
        this.owner = owner; this.settings = settings; this.create = create; this.clock = clock ?? TimeProvider.System;
        this.barks = barks ?? new(settings);
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
            Window.AvatarClicked += () =>
            {
                // An idle click answers at once; a session pokes the timeline on its own clock.
                if (!session && barks.Poke(Now)) IdleTick();
                AvatarClicked?.Invoke();
            };
            Window.PlacementChanged += () => PlacementChanged?.Invoke();
            Window.MenuCommand += command => MenuCommand?.Invoke(command);
            Window.SessionState = () => SessionState?.Invoke() ?? CompanionSessionState.Unavailable;
        }
        if (!session) IdleTick();
    }

    public void BeginSession() { session = true; idle.Stop(); }

    public void SessionFrame(CompanionBarkCue? bark)
    {
        if (settings.CompanionEnabled != (Window is not null)) Refresh();
        Window?.Update(bark, Now);
    }

    // Barks and talk end at once; the tube returns to idle and the next idle word waits a full interval.
    public void EndSession() { session = false; if (!disposed) barks.Stop(); Window?.StopReactions(Now); Refresh(); }

    // The one entry point for spoken barks: line id, mood, text and the recording's length in seconds.
    // The avatar plays CCP's talk clips for the line, then one reaction; a short line plays only its reaction.
    public void SpeakLine(string? lineId, string? mood, string? text, double seconds) => Window?.SpeakLine(lineId, mood, text, seconds, Now);
    // A giggle or other short non-verbal sound: one expressive clip with no mouth movement.
    public void NonVerbal() => Window?.NonVerbal(Now);
    // A keyword or other repetitive trigger: one expressive clip from idle, never talk.
    public bool TriggerWord() => Window?.TriggerWord(Now) ?? false;

    internal void IdleTick()
    {
        if (session || disposed || Window is not { } window) { idle.Stop(); return; }
        var now = Now;
        window.Update(IdleWord(window, now), now);
        // About 20 fps while she moves or loads (15 in Performance mode); a still frame only polls window state.
        var interval = TimeSpan.FromSeconds(window.WantsFrames ? window.FrameSeconds : .25);
        if (idle.Interval != interval) idle.Interval = interval;
        if (!idle.IsEnabled) idle.Start();
    }

    // Words only while the tube can be seen; a hidden tube restarts the interval instead of saving words up.
    private CompanionBarkCue? IdleWord(CompanionWindow window, double now)
    {
        var present = window.CanPresent;
        if (!present || !barks.IsIdle) barks.StartIdle(now);
        if (!present) return null;
        var bark = barks.Step(now).Bark;
        if (bark is not null && bark.Id != spoken) { spoken = bark.Id; IdleBark?.Invoke(bark); }
        return bark;
    }

    private void Release() { idle.Stop(); if (!session) barks.Stop(); Window?.FinishImmediately(); Window = null; }

    public void Dispose() { if (disposed) return; disposed = true; Release(); }
}
