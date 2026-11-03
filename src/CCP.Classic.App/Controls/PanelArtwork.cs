using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.Core;

namespace CCP.Classic.App.Controls;

// Public CCP masks and badge originals remain unchanged. Only their presentation is cropped or tinted.
internal sealed class PanelArtwork : Control
{
    private static readonly Dictionary<string, Bitmap> Cache = [];
    private readonly Bitmap? image;
    private readonly bool badge;
    private readonly IBrush background, tint;
    private readonly ImageBrush? mask;
    private readonly Rect sourceRegion;
    private double phase;
    public bool Locked { get; set; }
    public bool Animate { get; set; } = true;
    internal static int CachedImages => Cache.Count;

    public PanelArtwork(string path, string accent, bool badge = false)
    {
        this.badge = badge; IsHitTestVisible = false; sourceRegion = BadgeRegion(Path.GetFileNameWithoutExtension(path));
        background = new LinearGradientBrush { StartPoint = RelativePoint.TopLeft, EndPoint = RelativePoint.BottomRight,
            GradientStops = [new(Color.Parse("#24203F"), 0), new(Color.Parse("#10101F"), 1)] };
        tint = new LinearGradientBrush { StartPoint = RelativePoint.TopLeft, EndPoint = RelativePoint.BottomRight,
            GradientStops = [new(Color.Parse(accent), 0), new(Color.Parse("#FF8FD1"), .65), new(Colors.White, 1)] };
        if (!Cache.TryGetValue(path, out image))
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/" + path));
                image = Bitmap.DecodeToWidth(stream, badge ? 256 : 512);
                if (Cache.Count < 32) Cache.Add(path, image);
            }
            catch (Exception error) when (error is IOException or ArgumentException) { }
        }
        if (image is not null) mask = new(image) { Stretch = Stretch.UniformToFill };
    }
    public void Step(double value) { if (Animate && !Locked) { phase = value; InvalidateVisual(); } }
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size); if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using (context.PushClip(new RoundedRect(bounds, 13)))
        {
            context.DrawRectangle(background, null, bounds);
            if (image is null) return;
            if (badge)
            {
                // The central emblem excludes the original baked legacy captions.
                var source = new Rect(image.Size.Width * sourceRegion.X, image.Size.Height * sourceRegion.Y,
                    image.Size.Width * sourceRegion.Width, image.Size.Height * sourceRegion.Height);
                var scale = Math.Min(Math.Max(1, bounds.Width - 12) / source.Width, Math.Max(1, bounds.Height - 12) / source.Height);
                var target = new Rect(bounds.Center.X - source.Width * scale / 2, bounds.Center.Y - source.Height * scale / 2,
                    source.Width * scale, source.Height * scale);
                using (context.PushOpacity(Locked ? .76 : 1)) context.DrawImage(image, source, target);
            }
            else
            {
                using (context.PushTransform(Matrix.CreateTranslation(Math.Sin(phase) * 7, Math.Cos(phase * .7) * 3)))
                using (context.PushOpacity(.8))
                using (context.PushOpacityMask(mask!, bounds.Inflate(10))) context.DrawRectangle(tint, null, bounds.Inflate(10));
            }
            using (context.PushOpacity(Locked ? .08 : .16 + Math.Sin(phase) * .04))
                context.DrawRectangle(tint, null, bounds);
        }
    }

    private static Rect BadgeRegion(string id) => id switch
    {
        "brainwashed_slavedoll" => new(.03, .03, .94, .63),
        "deep_sleep" => new(.08, .22, .84, .51),
        "daily_maintenance" => new(.05, .35, .59, .55),
        "retinal_burn" => new(.01, .02, .69, .63),
        "morning_glory" => new(.51, .36, .42, .24),
        "rose_tinted_reality" => new(.20, .02, .60, .60),
        "corner_hit" => new(.02, .01, .50, .76),
        "neon_obsession" => new(.10, .07, .54, .63),
        "obedience_reflex" => new(.03, .24, .94, .48),
        "pop_the_thought" => new(.09, .16, .82, .58),
        "system_overload" => new(.03, .03, .47, .38),
        "typing_tutor" => new(.03, .02, .47, .72),
        "look_but_dont_touch" => new(.05, .02, .90, .65),
        "mathematicians_nightmare" => new(.06, .04, .88, .65),
        "what_panic_button" => new(.04, .03, .92, .69),
        "total_lockdown" => new(.03, .02, .94, .78),
        "relapse" => new(.02, .02, .96, .76),
        _ => new(.03, .03, .94, .70)
    };
}

// One attached-panel clock drives all its art. Motion gates keep the last pose and stop redraws.
internal sealed class PanelArtworkClock
{
    private readonly Control owner;
    private readonly ClassicSettings settings;
    private readonly List<PanelArtwork> artwork = [];
    private readonly DispatcherTimer timer;
    private double phase;
    internal bool IsRunning => timer.IsEnabled;
    internal int Frames { get; private set; }
    public PanelArtworkClock(Control owner, ClassicSettings settings)
    {
        this.owner = owner; this.settings = settings;
        timer = new(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Tick());
        owner.AttachedToVisualTree += (_, _) => timer.Start();
        owner.DetachedFromVisualTree += (_, _) => timer.Stop();
    }
    public PanelArtwork Add(PanelArtwork value) { artwork.Add(value); return value; }
    internal void Tick()
    {
        var allowed = !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode
            && owner.IsEffectivelyVisible && TopLevel.GetTopLevel(owner) is Window { IsVisible: true, WindowState: not WindowState.Minimized };
        timer.Interval = TimeSpan.FromMilliseconds(allowed ? 50 : 250);
        if (!allowed) return;
        phase = (phase + .018) % Math.Tau; Frames++;
        foreach (var item in artwork) item.Step(phase);
    }
}
