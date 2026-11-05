using CCP.Classic.Core.Flash;
using CCP.Classic.Core.Remix;

namespace CCP.Classic.Core;

public sealed partial class EffectsTimeline
{
    private double viewportWidth = 1600, viewportHeight = 900;
    private long? draggedFlash;
    public Func<int, double>? ImageAspectRatio { get; set; }
    public void SetViewport(double width, double height)
    {
        if (double.IsFinite(width + height) && width > 0 && height > 0)
        { viewportWidth = Math.Clamp(width, 200, 16384); viewportHeight = Math.Clamp(height, 200, 16384); }
    }
    private MotionLevel FlashLevel => settings.ReducedMotion || settings.PerformanceMode ? MotionLevel.Off
        : settings.GentleVisuals ? MotionLevel.Reduced : MotionLevel.Full;

    private void InitializeFlash(Flash flash)
    {
        var aspect = ImageAspectRatio?.Invoke(flash.Image) ?? viewportWidth / viewportHeight;
        if (!double.IsFinite(aspect) || aspect <= 0) aspect = 1;
        var width = Math.Max(50, Math.Min(viewportWidth, viewportHeight * aspect) * flash.Size);
        var height = Math.Max(50, Math.Min(viewportHeight, viewportWidth / aspect) * flash.Size);
        var placement = FlashPlacement.SpawnBounds((int)viewportWidth, (int)viewportHeight, (int)width, (int)height);
        var x = random.Next(placement.MinX, placement.MaxX); var y = random.Next(placement.MinY, placement.MaxY);
        if (settings.FlashAvoidCenter && FlashPlacement.TryPickAvoidCenterPointAdaptive((int)viewportWidth,
            (int)viewportHeight, (int)width, (int)height, settings.FlashCenterExclusionPercent, random, out var avoidX, out var avoidY, out _))
        { x = avoidX; y = avoidY; }
        flash.X = x / viewportWidth; flash.Y = y / viewportHeight;
        var style = FlashMotion.Resolve(settings.Flash.Motion, true, true, FlashLevel, random);
        var neighbours = flashes.Where(f => f.Motion?.Style == FlashMotionStyle.Pendulum)
            .Select(f => new PendulumNeighbour(f.Motion!.PivotX, FlashPendulumRig.EffectivePhase(f.Motion))).ToArray();
        flash.Motion = FlashMotion.Create(style, flash.X * viewportWidth, flash.Y * viewportHeight,
            width, height, 0, 0, viewportWidth, viewportHeight, FlashLevel, random, neighbours);
        flash.Motion.Vx *= settings.Flash.DriftSpeed; flash.Motion.Vy *= settings.Flash.DriftSpeed;
        flash.Aspect = aspect;
    }
    private void MoveFlash(Flash flash, double dt)
    {
        if (flash.Motion is not { } motion) return;
        RefreshFlashAspect(flash, motion);
        if (FlashLevel != MotionLevel.Off || motion.Drag?.Held == true) FlashMotion.Step(motion, dt);
        flash.X = motion.X / viewportWidth; flash.Y = motion.Y / viewportHeight;
    }
    private void RefreshFlashAspect(Flash flash, FlashMotionState motion)
    {
        // The host delegate reads only cached metadata. Its first value may be a lazy image fallback.
        var aspect = flash.IsRemix ? flash.Aspect : ImageAspectRatio?.Invoke(flash.Image) ?? flash.Aspect;
        if (!double.IsFinite(aspect) || aspect <= 0 || Math.Abs(aspect - flash.Aspect) < 1e-6) return;
        flash.Aspect = aspect;
        var width = Math.Max(50, Math.Min(viewportWidth, viewportHeight * aspect) * flash.Size);
        var height = Math.Max(50, Math.Min(viewportHeight, viewportWidth / aspect) * flash.Size);
        var centerX = motion.X + motion.W / 2; var centerY = motion.Y + motion.H / 2;
        motion.MediaW = width; motion.MediaH = height;
        if (motion.Style == FlashMotionStyle.Pendulum)
            (motion.X, motion.Y, motion.W, motion.H) = FlashMotion.HangingBounds(motion.PivotX, motion.PivotY,
                motion.Rope, motion.AngleRad, width, height);
        else
        {
            var oldX = motion.X; var oldY = motion.Y;
            motion.W = width; motion.H = height;
            motion.X = Math.Clamp(centerX - width / 2, motion.BoundsX, motion.BoundsX + Math.Max(0, motion.BoundsW - width));
            motion.Y = Math.Clamp(centerY - height / 2, motion.BoundsY, motion.BoundsY + Math.Max(0, motion.BoundsH - height));
            if (motion.Drag is { Held: true } drag)
                lock (drag.Sync) { drag.GrabDx += motion.X - oldX; drag.GrabDy += motion.Y - oldY; }
        }
    }
    private FlashCue FlashOutput(Flash flash)
    {
        var motion = flash.Motion;
        var width = (motion?.MediaW ?? flash.Size * viewportWidth) / viewportWidth;
        var height = (motion?.MediaH ?? flash.Size * viewportHeight) / viewportHeight;
        var centerX = motion is null ? flash.X + width / 2 : (motion.X + motion.W / 2) / viewportWidth;
        var centerY = motion is null ? flash.Y + height / 2 : (motion.Y + motion.H / 2) / viewportHeight;
        return new(flash.Id, flash.Image, centerX - width / 2, centerY - height / 2, flash.Size, flash.Alpha)
        {
            Width = width, Height = height,
            HitArea = motion is null ? (flash.X, flash.Y, width, height)
                : (motion.X / viewportWidth, motion.Y / viewportHeight, motion.W / viewportWidth, motion.H / viewportHeight),
            Rotation = motion?.AngleRad ?? 0, Exit = flash.Exit, Shatter = flash.Shatter, IsRemix = flash.IsRemix
        };
    }
    private void SpawnRemix(double now, double aspect, long audioBatch)
    {
        var bounds = RemixPlan.Centered(viewportWidth, viewportHeight, aspect, 1);
        var flash = new Flash { Id = ++nextId, Image = -1, IsRemix = true, Aspect = aspect, AudioBatch = audioBatch,
            Size = bounds.Width / viewportWidth, X = bounds.X / viewportWidth, Y = bounds.Y / viewportHeight,
            Cue = new(now, Math.Max(Value("flash", "fade") / 1000, .001),
                settings.Flash.StayUntilPopped || audioBatch > 0 ? double.PositiveInfinity : settings.Flash.HoldSeconds, false) };
        flash.Motion = FlashMotion.Create(FlashMotionStyle.Still, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            0, 0, viewportWidth, viewportHeight, FlashLevel, random);
        flash.Gain.Reset(Value("flash", "opacity") / 100); flashes.Add(flash);
    }
    public bool BeginDrag(long id, double normalizedX, double normalizedY)
    {
        if (!IsRunning || !settings.Flash.Draggable || Value("flash", "dismiss") == 0) return false;
        var flash = flashes.FirstOrDefault(f => f.Id == id && f.Exit is null && f.Shatter is null && f.Alpha > .01);
        if (flash?.Motion is not { } motion) return false;
        FlashDrag.Begin(motion, normalizedX * viewportWidth, normalizedY * viewportHeight, lastTime * 1000);
        draggedFlash = id; return true;
    }
    public void MoveDrag(double normalizedX, double normalizedY)
    {
        if (!double.IsFinite(normalizedX + normalizedY)) return;
        if (flashes.FirstOrDefault(f => f.Id == draggedFlash)?.Motion?.Drag is { } drag)
            FlashDrag.Sample(drag, normalizedX * viewportWidth, normalizedY * viewportHeight, lastTime * 1000);
    }
    public void EndDrag()
    {
        var flash = flashes.FirstOrDefault(f => f.Id == draggedFlash); draggedFlash = null;
        if (flash?.Motion is { } motion && FlashDrag.Release(motion, lastTime * 1000, FlashLevel) == FlashDragOutcome.Tap)
            Dismiss(flash.Id);
    }
}
