using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Classic.App.Controls.HelpLoops;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class TutorialAnimation : Control, IDisposable
{
    private readonly ClassicSettings settings;
    private readonly LocalizationService text;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;
    private readonly LoopPalette palette = new();
    private HelpLoopScene? scene;
    private bool attached, disposed, userPaused, available, previousPreferences;
    private double elapsed, lastClock;
    public event Action? MotionChanged;
    public bool PreferencesPaused => settings.GentleVisuals || settings.ReducedMotion;
    public bool PresentationAvailable { get => available; set { available = value; lastClock = clock.Elapsed.TotalMilliseconds; SyncTimer(); } }
    public bool UserPaused { get => userPaused; set { userPaused = value; lastClock = clock.Elapsed.TotalMilliseconds; SyncTimer(); InvalidateVisual(); } }
    public bool MotionActive => attached && !disposed && available && !PreferencesPaused && !userPaused && scene is not null;
    public double PositionMilliseconds => PreferencesPaused ? scene?.StillMs ?? 0 : elapsed;
    internal bool TimerRunning => timer.IsEnabled;

    public TutorialAnimation(ClassicSettings settings, LocalizationService text)
    {
        this.settings = settings; this.text = text;
        timer = new(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) => Pulse(clock.Elapsed.TotalMilliseconds));
        AttachedToVisualTree += (_, _) => { attached = true; lastClock = clock.Elapsed.TotalMilliseconds; SyncTimer(); };
        DetachedFromVisualTree += (_, _) => { attached = false; SyncTimer(); };
    }

    public void SelectScene(string? id)
    {
        if (disposed) return;
        scene = HelpLoopScene.Create(id); elapsed = 0; lastClock = clock.Elapsed.TotalMilliseconds;
        SyncTimer(); InvalidateVisual();
    }

    internal void Pulse(double now)
    {
        if (disposed) return;
        if (!double.IsFinite(now)) now = lastClock;
        var dt = Math.Clamp(now - lastClock, 0, 120); lastClock = now;
        if (previousPreferences != PreferencesPaused) { previousPreferences = PreferencesPaused; MotionChanged?.Invoke(); InvalidateVisual(); }
        if (MotionActive && scene is not null)
        {
            var next = elapsed + dt;
            if (next >= scene.DurationMs) { next %= scene.DurationMs; scene.Reset(); }
            elapsed = next; InvalidateVisual();
        }
        SyncTimer();
    }

    private void SyncTimer()
    {
        // A slow preference check allows resume after the settings window changes motion options.
        timer.Interval = TimeSpan.FromMilliseconds(MotionActive ? settings.PerformanceMode ? 100 : 40 : 250);
        if (attached && !disposed && available && scene is not null) timer.Start(); else timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (disposed || scene is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width / LoopFrame.StageWidth, Bounds.Height / LoopFrame.StageHeight);
        var x = (Bounds.Width - LoopFrame.StageWidth * scale) / 2;
        var y = (Bounds.Height - LoopFrame.StageHeight * scale) / 2;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y));
        using var stageClip = context.PushClip(new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
        var ground = new DrawingGroup(); var back = new DrawingGroup(); var front = new DrawingGroup();
        double blur;
        using (var groundDrawing = ground.Open())
        using (var backDrawing = back.Open())
        using (var frontDrawing = front.Open())
        using (var stage = new LoopContext(groundDrawing))
        using (var background = new LoopContext(backDrawing))
        using (var foreground = new LoopContext(frontDrawing))
        {
            var frame = new LoopFrame(stage, background, foreground, palette) { TextService = text };
            frame.Ground(); scene.Draw(frame, PositionMilliseconds); blur = frame.BackBlur;
        }
        ground.Draw(context);
        using (context.PushEffect(new BlurEffect { Radius = Math.Clamp(blur, 0, 7) }, new(0, 0, 480, 270))) back.Draw(context);
        front.Draw(context);
    }

    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); scene = null; InvalidateVisual(); }
}
