using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Companion;

// Original tube clips inside CCP's glass tube art, one decoded frame in flight. The effects service
// supplies one monotonic clock for idle and session frames.
internal sealed class CompanionWindow : Window
{
    private static Bitmap? tubeArt;
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly bool requireTransparency;
    private readonly Action? beforeDecode;
    private readonly Image portrait = Portrait("CompanionPortrait"), previousPortrait = Portrait("CompanionPrevious");
    private readonly Image tube = new() { Name = "CompanionTube", Width = TubeLayout.TubeSize, Height = TubeLayout.TubeSize,
        Stretch = Stretch.Uniform, IsHitTestVisible = false,
        RenderTransformOrigin = new RelativePoint(TubeLayout.AvatarCentre / TubeLayout.TubeSize, .5, RelativeUnit.Relative) };
    private readonly ScaleTransform mirror = new();
    private readonly TranslateTransform bob = new();
    private readonly TextBlock words = new() { Name = "CompanionBark", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        Foreground = Brushes.White, FontSize = 26 };
    private readonly Border speech;
    private readonly Fade entrance = new(), dissolve = new();
    private WriteableBitmap? bitmap, previous;
    private GifClip? decoder;
    private Task<DecodedFrame?>? pending;
    private string wantedClip = "idle", displayedClip = "";
    private double lastNow, animationTime, lastRequest = double.NegativeInfinity, reaction = 7.2;
    private bool closed, prepared, hasTime, forced, stopPending;
    private Point? dragStart;
    private PixelPoint dragPosition;
    private bool dragged, placing;
    private bool? detached;
    public string? LastError { get; private set; }
    public int DecodedFrames { get; private set; }
    internal bool IsDecodePending => pending is not null;
    internal TubeDockSide DockSide { get; private set; }
    internal bool IsMirrored => mirror.ScaleX < 0;
    internal double BobOffset => bob.Y;
    internal string WantedClip => wantedClip;
    // Idle callers poll quickly only while something moves or is still loading.
    internal bool WantsFrames => !closed && (IsClipPlaying || pending is not null || Opacity is > 0 and < 1
        || portrait.Opacity < 1 || bitmap is null && CanBootstrap);
    // CCP parity: the clip plays whenever the tube can be seen, focused or not. Gentle visuals and
    // Reduced motion only still the bob; Performance mode lowers the clip to 15 fps.
    internal double FrameSeconds => settings.PerformanceMode ? 1d / 15 : .05;
    private bool CanBootstrap => !closed && settings.CompanionEnabled && WindowState != WindowState.Minimized
        && owner.IsVisible && owner.WindowState != WindowState.Minimized;
    internal bool CanPresent => CanBootstrap && LastError is null;
    public bool IsClipPlaying => CanBootstrap && IsVisible;
    public bool IsMotionActive => IsClipPlaying && !settings.GentleVisuals && !settings.ReducedMotion;
    public event Action? AvatarClicked;
    public event Action? StopRequested;
    public event Action? PlacementChanged;

    public CompanionWindow(Window owner, ClassicSettings settings, Action<Window> configureInput, bool requireTransparency = true,
        Action? beforeDecode = null)
    {
        this.owner = owner; this.settings = settings; this.requireTransparency = requireTransparency; this.beforeDecode = beforeDecode;
        Title = "CCP Classic"; var initial = TubeLayout.WindowSize(1); Width = initial.Width; Height = initial.Height;
        WindowStartupLocation = WindowStartupLocation.Manual; WindowDecorations = WindowDecorations.None;
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var images = new Grid { Width = TubeLayout.AvatarWidth, Height = TubeLayout.AvatarHeight, RenderTransform = bob, IsHitTestVisible = false };
        images.Children.Add(previousPortrait); images.Children.Add(portrait);
        tube.RenderTransform = mirror; tube.Source = tubeArt ??= LoadTube();
        // Clicks land on the painted art's box, avatar included.
        var hit = new Border { Name = "CompanionHit", Background = Brushes.Transparent, Width = TubeLayout.BandWidth, Height = 540 };
        hit.PointerPressed += (_, args) =>
        {
            var pointer = args.GetCurrentPoint(hit).Properties;
            if (pointer.IsLeftButtonPressed)
            {
                if (settings.CompanionPlacement.Detached)
                { dragStart = args.GetPosition(this); dragPosition = Position; dragged = false; args.Pointer.Capture(hit); }
                else { reaction = 7.2; AvatarClicked?.Invoke(); }
                args.Handled = true;
            }
            else if (pointer.IsRightButtonPressed) { StopRequested?.Invoke(); args.Handled = true; }
        };
        hit.PointerMoved += (_, args) =>
        {
            if (dragStart is not { } start || !args.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) return;
            var point = args.GetPosition(this); var delta = point - start;
            if (!dragged && delta.X * delta.X + delta.Y * delta.Y < 25) return;
            dragged = true;
            var scaling = Screens.ScreenFromWindow(this)?.Scaling ?? 1;
            Position = ClampPosition(new(Position.X + (int)(delta.X * scaling), Position.Y + (int)(delta.Y * scaling)));
        };
        hit.PointerReleased += (_, args) =>
        {
            if (dragStart is null) return; dragStart = null; args.Pointer.Capture(null);
            if (dragged) PersistPlacement(); else { Position = dragPosition; reaction = 7.2; AvatarClicked?.Invoke(); }
            dragged = false; args.Handled = true;
        };
        hit.PointerCaptureLost += (_, _) => { if (dragged) PersistPlacement(); dragStart = null; dragged = false; };
        hit.PointerWheelChanged += (_, args) =>
        {
            if (!settings.CompanionPlacement.Detached || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            settings.CompanionPlacement.Scale = Math.Clamp(settings.CompanionPlacement.Scale + Math.Sign(args.Delta.Y) * .25, .5, 1.5);
            ApplyPlacement(); PersistPlacement(); args.Handled = true;
        };
        speech = new Border { Child = words, Background = new SolidColorBrush(Color.Parse("#E61C1C35")),
            CornerRadius = new(16), Padding = new(16, 10), IsHitTestVisible = false, Opacity = 0, MaxWidth = TubeLayout.BandWidth - 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        // CCP's 780 x 1020 design canvas, cropped to the band that holds the art.
        var stage = new Canvas { Width = TubeLayout.BandWidth, Height = TubeLayout.CanvasHeight, ClipToBounds = true };
        Place(stage, tube, -TubeLayout.BandLeft, TubeLayout.TubeTop);
        Place(stage, images, TubeLayout.AvatarCentre - TubeLayout.AvatarWidth / 2 - TubeLayout.BandLeft,
            TubeLayout.AvatarFeet - TubeLayout.AvatarHeight);
        Place(stage, new Panel { Width = TubeLayout.BandWidth, Height = TubeLayout.AvatarFeet - TubeLayout.AvatarHeight - 12,
            Children = { speech } }, 0, 0);
        Place(stage, hit, 0, TubeLayout.TubeTop + 230);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = stage };
        owner.PositionChanged += OwnerMoved; owner.PropertyChanged += OwnerChanged;
        configureInput(this);
        Closing += (_, args) =>
        {
            if (forced) return;
            args.Cancel = true;
            if (stopPending) return;
            stopPending = true;
            Dispatcher.UIThread.Post(() => { stopPending = false; if (!closed) StopRequested?.Invoke(); });
        };
        PropertyChanged += (_, args) =>
        {
            if (prepared && requireTransparency && args.Property == ActualTransparencyLevelProperty
                && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
            { LastError = "Companion transparency is unavailable."; Hide(); }
        };
        Closed += (_, _) => Release();
    }

    private static Image Portrait(string name) => new() { Name = name, Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    private static void Place(Canvas stage, Control child, double left, double top)
    { Canvas.SetLeft(child, left); Canvas.SetTop(child, top); stage.Children.Add(child); }
    private static Bitmap LoadTube()
    {
        using var source = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/Companion/tube.png"));
        return Bitmap.DecodeToWidth(source, 1024, BitmapInterpolationMode.HighQuality);
    }
    private void OwnerMoved(object? sender, PixelPointEventArgs args) => FollowOwner();
    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == BoundsProperty || args.Property == ClientSizeProperty || args.Property == WindowStateProperty
            || args.Property == IsVisibleProperty) FollowOwner();
    }
    // The tube leaves with a hidden or minimized main window and returns with it.
    private void FollowOwner()
    {
        if (closed) return;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized) { Hide(); return; }
        if (!prepared) return;
        if (IsVisible) ApplyPlacement();
        else if (settings.CompanionEnabled && bitmap is not null && LastError is null && Opacity > 0) EnsureShown();
    }

    public void Update(CompanionBarkCue? bark, double seconds)
    {
        if (closed) return;
        if (prepared) ApplyPlacement();
        var now = double.IsFinite(seconds) ? Math.Max(lastNow, seconds) : lastNow;
        var dt = hasTime ? Math.Clamp(now - lastNow, 0, .1) : 0;
        lastNow = now; hasTime = true;
        var approved = bark is not null && CompanionTimeline.Phrases.Any(phrase => phrase.Text == bark.Text);
        words.Text = approved ? bark!.Text : ""; speech.Opacity = approved ? Math.Clamp(bark!.Opacity, 0, 1) : 0;
        speech.IsVisible = approved;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized) { Hide(); return; }
        var target = settings.CompanionEnabled && bitmap is not null && LastError is null ? 1 : 0;
        Opacity = entrance.Sample(now, target, settings.GentleVisuals ? .75 : .25);
        if (target > 0) EnsureShown();
        else if (Opacity == 0) Hide();
        var blend = dissolve.Sample(now, 1, settings.GentleVisuals ? .75 : .25);
        portrait.Opacity = blend; previousPortrait.Opacity = 1 - blend;
        if (blend == 1) { previousPortrait.Source = null; previous?.Dispose(); previous = null; }
        if (IsClipPlaying)
        {
            animationTime += dt; reaction = Math.Max(0, reaction - dt);
            var clip = approved ? "talkA" : reaction > 0 ? "greeting" : "idle";
            if (clip != wantedClip) { wantedClip = clip; animationTime = 0; lastRequest = double.NegativeInfinity; }
        }
        bob.Y = IsMotionActive ? 4 * Math.Sin(now * Math.PI) : 0;
        if (pending is null && settings.CompanionEnabled && LastError is null
            && (bitmap is null ? CanBootstrap : IsClipPlaying && now - lastRequest >= FrameSeconds - .001)) RequestFrame(now);
    }

    private void EnsureShown()
    {
        if (IsVisible || closed || LastError is not null) return;
        if (!prepared)
        {
            Position = new(-32000, -32000); Width = Height = 1; Show(owner);
            if (requireTransparency && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
            { LastError = "Companion transparency is unavailable."; Hide(); return; }
            Hide(); prepared = true;
        }
        ApplyPlacement(); Show(owner);
    }

    private void ApplyPlacement()
    {
        if (placing || dragged) return;
        placing = true;
        try
        {
            var options = settings.CompanionPlacement;
            var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            var scaling = screen is { Scaling: > 0 } ? screen.Scaling : 1;
            var scale = screen is null ? 1 : TubeLayout.Scale(screen.WorkingArea, scaling);
            if (options.Detached) scale *= Math.Clamp(options.Scale, .5, 1.5);
            var size = TubeLayout.WindowSize(scale);
            if (Width != size.Width) Width = size.Width;
            if (Height != size.Height) Height = size.Height;
            // CCP parity: a docked tube rides with its owner and is never topmost, or it would float over
            // other apps while the panel sits behind them. Always on top applies to a detached tube.
            var topmost = options.Detached && options.AlwaysOnTop;
            if (Topmost != topmost) Topmost = topmost;
            if (options.Detached)
            {
                mirror.ScaleX = 1; DockSide = TubeDockSide.Float;
                if (options.Left is { } left && options.Top is { } top) MoveTo(ClampPosition(new(left, top)));
                else if (detached != true) Anchor();
            }
            else if (screen is not null)
            {
                var frame = owner.FrameSize ?? owner.ClientSize;
                var ownerScale = owner.DesktopScaling > 0 ? owner.DesktopScaling : scaling;
                var dock = TubeLayout.Dock(new(owner.Position, PixelSize.FromSize(frame, ownerScale)), screen.WorkingArea, scaling, scale);
                MoveTo(dock.Position); mirror.ScaleX = dock.Mirrored ? -1 : 1; DockSide = dock.Side;
            }
            detached = options.Detached;
        }
        finally { placing = false; }
    }
    private void MoveTo(PixelPoint point) { if (Position != point) Position = point; }
    private PixelPoint ClampPosition(PixelPoint point)
    {
        var screen = Screens.ScreenFromPoint(point) ?? owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null) return point;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        return new(Math.Clamp(point.X, area.X, Math.Max(area.X, area.Right - (int)(Width * scale))),
            Math.Clamp(point.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)(Height * scale))));
    }
    private void PersistPlacement()
    {
        if (!settings.CompanionPlacement.Detached || !prepared || closed) return;
        settings.CompanionPlacement.Left = Position.X; settings.CompanionPlacement.Top = Position.Y; PlacementChanged?.Invoke();
    }
    private void Anchor()
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea; var scale = screen.Scaling;
            Position = new(area.Right - (int)(Width * scale) - 20, area.Bottom - (int)(Height * scale) - 20);
        }
    }

    private void RequestFrame(double now)
    {
        var clip = wantedClip; var bootstrap = bitmap is null; var position = bootstrap ? 0 : animationTime;
        lastRequest = now;
        pending = Task.Run(() =>
        {
            beforeDecode?.Invoke();
            if (decoder?.Name != clip) { decoder?.Dispose(); decoder = new(clip); }
            return decoder.Decode(position, bootstrap);
        });
        _ = pending.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            pending = null;
            if (closed) { _ = result.Exception; return; }
            if (result.IsFaulted) { LastError = result.Exception!.GetBaseException().Message; Hide(); return; }
            if (result.Result is not { } frame || clip != wantedClip || !(bitmap is null ? CanBootstrap : IsClipPlaying)) return;
            if (displayedClip != clip || bitmap is null)
            {
                previousPortrait.Source = null; previous?.Dispose(); previous = bitmap;
                previousPortrait.Source = previous;
                bitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                portrait.Source = bitmap; displayedClip = clip; dissolve.Reset(previous is null ? 1 : 0);
            }
            using (var pixels = bitmap.Lock())
                for (var row = 0; row < frame.Height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, row * frame.Width * 4,
                        pixels.Address + row * pixels.RowBytes, frame.Width * 4);
            DecodedFrames++; portrait.InvalidateVisual();
        }), TaskScheduler.Default);
    }

    public void FinishImmediately() { forced = true; Release(); Close(); }
    private void Release()
    {
        if (closed) return;
        closed = true; owner.PositionChanged -= OwnerMoved; owner.PropertyChanged -= OwnerChanged;
        Hide(); portrait.Source = previousPortrait.Source = tube.Source = null; speech.IsVisible = false; bob.Y = 0;
        bitmap?.Dispose(); previous?.Dispose(); bitmap = previous = null;
        if (pending is { } work) _ = work.ContinueWith(_ => decoder?.Dispose(), TaskScheduler.Default);
        else decoder?.Dispose();
    }

    private sealed record DecodedFrame(byte[] Pixels, int Width, int Height);
    private sealed class GifClip : IDisposable
    {
        private readonly SKData data;
        private readonly SKCodec codec;
        private readonly SKBitmap bitmap;
        private readonly double[] ends;
        private int decoded = -1;
        public string Name { get; }
        public GifClip(string name)
        {
            Name = name;
            using var source = AssetLoader.Open(new Uri($"avares://CCP.Classic.App/Assets/Companion/{name}.gif"));
            data = SKData.Create(source); codec = SKCodec.Create(data) ?? throw new InvalidDataException("Companion clip is invalid.");
            if (codec.Info.Width is <= 0 or > 512 || codec.Info.Height is <= 0 or > 512 || codec.FrameCount > 240)
                throw new InvalidDataException("Companion clip exceeds its frame budget.");
            bitmap = new(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            double end = 0; ends = codec.FrameInfo.Select(frame => end += Math.Max(20, frame.Duration) / 1000d).ToArray();
        }
        public DecodedFrame? Decode(double seconds, bool force)
        {
            var position = ends.Length > 0 ? seconds % ends[^1] : 0;
            var index = Array.FindIndex(ends, end => position < end); if (index < 0) index = 0;
            if (index == decoded && !force) return null;
            var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(index));
            if (result != SKCodecResult.Success) throw new InvalidDataException("Companion frame could not be decoded.");
            decoded = index; return new(bitmap.Bytes, bitmap.Width, bitmap.Height);
        }
        public void Dispose() { bitmap.Dispose(); codec.Dispose(); data.Dispose(); }
    }
    private sealed class Fade
    {
        private double from, target, value, started, duration;
        public void Reset(double initial) { from = target = value = initial; started = duration = 0; }
        public double Sample(double now, double next, double fade)
        {
            if (next != target || fade > duration)
            { from = value; target = next; started = now; duration = fade; }
            var t = duration > 0 ? Math.Clamp((now - started) / duration, 0, 1) : 1;
            return value = from + (target - from) * t * t * (3 - 2 * t);
        }
    }
}
