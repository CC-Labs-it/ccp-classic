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

// Original tube clips inside CCP's glass tube art, one decode in flight. The effects service supplies one
// monotonic clock for idle and session frames. CompanionEmoteDirector picks the clips with CCP's rules; each
// plays once at CompanionEmoteDirector.PlaybackRate and crossfades into the next on two image layers.
internal sealed class CompanionWindow : Window
{
    private static Bitmap? tubeArt;
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly bool requireTransparency;
    private readonly Action? beforeDecode;
    private static CompanionEmoteMap? emoteMap;
    private readonly CompanionEmoteDirector director;
    private readonly Layer front = new(Portrait("CompanionPortrait")), back = new(Portrait("CompanionPrevious"));
    private Layer active, outgoing;
    private readonly Image tube = new() { Name = "CompanionTube", Width = TubeLayout.TubeSize, Height = TubeLayout.TubeSize,
        Stretch = Stretch.Uniform, IsHitTestVisible = false,
        RenderTransformOrigin = new RelativePoint(TubeLayout.AvatarCentre / TubeLayout.TubeSize, .5, RelativeUnit.Relative) };
    private readonly ScaleTransform mirror = new();
    private readonly TranslateTransform bob = new();
    private readonly TextBlock words = new() { Name = "CompanionBark", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        Foreground = Brushes.White, FontSize = 26 };
    private readonly Border speech;
    private readonly Fade entrance = new();
    private Task<DecodedFrame?[]>? pending;
    // A click sets reaction above zero; the next frame turns it into a click emote.
    private double lastNow, lastRequest = double.NegativeInfinity, reaction, fadeStarted, outgoingFrom;
    private long lastBarkReaction;
    private bool closed, prepared, forced, stopPending, fading;
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
    internal string WantedClip => director.Clip;
    internal CompanionEmoteDirector Emotes => director;
    internal bool IsCrossfading => fading;
    internal static CompanionEmoteMap EmoteMap => emoteMap ??= LoadEmotes();
    // Idle callers poll quickly only while something moves or is still loading.
    internal bool WantsFrames => !closed && (IsClipPlaying || pending is not null || Opacity is > 0 and < 1
        || fading || !HasPortrait && CanBootstrap);
    // CCP parity: the clip plays whenever the tube can be seen, focused or not. Gentle visuals and
    // Reduced motion only still the bob. Frames are polled at 20 fps (15 in Performance mode); the clip
    // itself advances at PlaybackRate, 13.3 fps of its 60 ms frames.
    internal double FrameSeconds => settings.PerformanceMode ? 1d / 15 : .05;
    private bool HasPortrait => front.Bitmap is not null || back.Bitmap is not null;
    private bool CanBootstrap => !closed && settings.CompanionEnabled && WindowState != WindowState.Minimized
        && owner.IsVisible && owner.WindowState != WindowState.Minimized;
    internal bool CanPresent => CanBootstrap && LastError is null;
    public bool IsClipPlaying => CanBootstrap && IsVisible;
    public bool IsMotionActive => IsClipPlaying && !settings.GentleVisuals && !settings.ReducedMotion;
    public event Action? AvatarClicked;
    public event Action? StopRequested;
    public event Action? PlacementChanged;
    // Panel work chosen from the right-click menu; raised after the menu has closed.
    public event Action<CompanionMenuCommand>? MenuCommand;
    internal Func<CompanionSessionState>? SessionState { get; set; }
    private readonly CompanionMenu menu;
    internal ContextMenu Menu => menu.Menu;

    public CompanionWindow(Window owner, ClassicSettings settings, Action<Window> configureInput, bool requireTransparency = true,
        Action? beforeDecode = null, CompanionEmoteDirector? emotes = null)
    {
        this.owner = owner; this.settings = settings; this.requireTransparency = requireTransparency; this.beforeDecode = beforeDecode;
        director = emotes ?? new(EmoteMap); active = front; outgoing = back;
        Title = "CCP Classic"; var initial = TubeLayout.WindowSize(1); Width = initial.Width; Height = initial.Height;
        WindowStartupLocation = WindowStartupLocation.Manual; WindowDecorations = WindowDecorations.None;
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var images = new Grid { Width = TubeLayout.AvatarWidth, Height = TubeLayout.AvatarHeight, RenderTransform = bob, IsHitTestVisible = false };
        images.Children.Add(back.Image); images.Children.Add(front.Image);
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
            // A right press is left unhandled: its release opens the menu and never pokes her.
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
            if (!args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            ResizeBy(Math.Sign(args.Delta.Y) * .25); args.Handled = true;
        };
        speech = new Border { Child = words, Background = new SolidColorBrush(Color.Parse("#E61C1C35")),
            CornerRadius = new(16), Padding = new(16, 10), IsHitTestVisible = false, Opacity = 0, MaxWidth = TubeLayout.BandWidth - 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        // CCP's 780 x 1020 design canvas, cropped to the band that holds the art.
        var stage = new Canvas { Width = TubeLayout.BandWidth, Height = TubeLayout.CanvasHeight, ClipToBounds = true };
        Place(stage, tube, -TubeLayout.BandLeft, TubeLayout.TubeTop);
        Place(stage, images, TubeLayout.AvatarBoxCentre - TubeLayout.AvatarWidth / 2 - TubeLayout.BandLeft,
            TubeLayout.AvatarFeet - TubeLayout.AvatarHeight);
        Place(stage, new Panel { Width = TubeLayout.BandWidth, Height = TubeLayout.AvatarFeet - TubeLayout.AvatarHeight - 12,
            Children = { speech } }, 0, 0);
        Place(stage, hit, 0, TubeLayout.TubeTop + 230);
        // CCP parity: right-click opens the tube menu on the same box that takes the left click.
        menu = new(Choose);
        menu.Menu.Opening += (_, _) => menu.Refresh(settings, SessionState?.Invoke() ?? CompanionSessionState.Unavailable);
        hit.ContextMenu = menu.Menu;
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
    private static CompanionEmoteMap LoadEmotes() => CompanionEmoteMap.Parse(ReadAsset("emotes.json"), ReadAsset("clip_timing.json"));
    private static string ReadAsset(string name)
    {
        using var reader = new StreamReader(AssetLoader.Open(new Uri($"avares://CCP.Classic.App/Assets/Companion/{name}")));
        return reader.ReadToEnd();
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
        else if (settings.CompanionEnabled && HasPortrait && LastError is null && Opacity > 0) EnsureShown();
    }

    public void Update(CompanionBarkCue? bark, double seconds)
    {
        if (closed) return;
        if (prepared) ApplyPlacement();
        var now = double.IsFinite(seconds) ? Math.Max(lastNow, seconds) : lastNow;
        lastNow = now;
        var approved = bark is not null && CompanionTimeline.IsApproved(bark.Text);
        words.Text = approved ? bark!.Text : ""; words.FontSize = approved && bark!.Text.Length > 24 ? 17 : 26; speech.Opacity = approved ? Math.Clamp(bark!.Opacity, 0, 1) : 0;
        speech.IsVisible = approved;
        if (!owner.IsVisible || owner.WindowState == WindowState.Minimized) { Hide(); return; }
        var target = settings.CompanionEnabled && HasPortrait && LastError is null ? 1 : 0;
        Opacity = entrance.Sample(now, target, settings.GentleVisuals ? .75 : .25);
        if (target > 0) EnsureShown();
        else if (Opacity == 0) Hide();
        if (IsClipPlaying || !HasPortrait && CanBootstrap)
        {
            // A click plays a click emote. An approved word is a single trigger word: one expressive clip from
            // idle, never talk. Spoken lines arrive through SpeakLine.
            if (reaction > 0) { reaction = 0; director.Click(now); }
            if (approved && bark!.Id != lastBarkReaction) { lastBarkReaction = bark.Id; director.TriggerWord(now); }
            director.Step(now);
            if (director.Generation != active.Generation) Retarget();
        }
        Blend(now);
        bob.Y = IsMotionActive ? 4 * Math.Sin(now * Math.PI) : 0;
        if (pending is null && settings.CompanionEnabled && LastError is null
            && (!HasPortrait ? CanBootstrap : IsClipPlaying && now - lastRequest >= FrameSeconds - .001)) RequestFrame(now);
    }

    // Bark entry point: a spoken line with its line id, mood, text and recording length in seconds. Talk
    // clips cover the line, then one reaction by line id stem, mood or a random expressive clip.
    public void SpeakLine(string? lineId, string? mood, string? text, double seconds, double now) =>
        director.Line(Math.Max(lastNow, now), lineId, mood, text, seconds);
    // A giggle or other short non-verbal sound: one expressive clip, no mouth movement.
    public void NonVerbal(double now) => director.NonVerbal(Math.Max(lastNow, now));
    // A keyword or other repetitive trigger: one expressive clip from idle, never talk.
    public bool TriggerWord(double now) => director.TriggerWord(Math.Max(lastNow, now));
    // A stop or panic: talk and reactions end and the idle rotation returns.
    public void StopReactions(double now) => director.Stop(Math.Max(lastNow, now));

    // The clip on screen keeps playing while the next one loads; the crossfade starts on the new clip's
    // first decoded frame (CCP's load gate), so the avatar never fades into an empty layer.
    private void Retarget()
    {
        if (active.Ready)
        {
            (active, outgoing) = (outgoing, active);
            fading = false; active.Image.Opacity = 0;
        }
        active.Clip = director.Clip; active.Started = director.ClipStarted; active.Generation = director.Generation;
        active.Ready = false; lastRequest = double.NegativeInfinity;
    }
    private void BeginFade()
    {
        outgoingFrom = outgoing.Bitmap is not null ? outgoing.Image.Opacity : 0;
        fadeStarted = lastNow; fading = outgoingFrom > 0;
        if (!fading) { active.Image.Opacity = 1; outgoing.Image.Opacity = 0; outgoing.Clip = ""; }
    }
    // CCP's fadeMs crossfade; Gentle visuals only lengthens it.
    private void Blend(double now)
    {
        if (!fading) return;
        var seconds = settings.GentleVisuals ? Math.Max(1.5, director.Map.FadeSeconds) : director.Map.FadeSeconds;
        var t = seconds > 0 ? Math.Clamp((now - fadeStarted) / seconds, 0, 1) : 1;
        var blend = t * t * (3 - 2 * t);
        active.Image.Opacity = blend; outgoing.Image.Opacity = outgoingFrom * (1 - blend);
        if (t >= 1) { fading = false; outgoing.Image.Opacity = 0; outgoing.Clip = ""; }
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
            // Sized from the panel's visible height; Companion size applies docked and detached.
            var ownerScale = owner.DesktopScaling > 0 ? owner.DesktopScaling : scaling;
            var panel = PanelFrame.Visible(owner, ownerScale);
            var scale = TubeLayout.Scale(panel.Height / ownerScale, screen is null ? 0 : screen.WorkingArea.Height / scaling, options.Scale);
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
                var dock = TubeLayout.Dock(panel, screen.WorkingArea, scaling, scale);
                MoveTo(dock.Position); mirror.ScaleX = dock.Mirrored ? -1 : 1; DockSide = dock.Side;
            }
            detached = options.Detached;
        }
        finally { placing = false; }
    }
    // Ctrl+wheel: Companion size, docked or detached. A docked tube saves only its size.
    internal void ResizeBy(double step)
    {
        settings.CompanionPlacement.Scale = Math.Clamp(settings.CompanionPlacement.Scale + step, .5, 1.5);
        PlacementEdited();
    }
    private void PlacementEdited()
    {
        ApplyPlacement();
        if (settings.CompanionPlacement.Detached) PersistPlacement(); else if (!closed) PlacementChanged?.Invoke();
    }

    // Placement, size, barks and hiding apply here at once; session and settings go to the panel.
    // Nothing here touches panic or Strict Lock: Stop asks the panel, which uses the panic gate.
    internal void Choose(string item)
    {
        if (closed) return;
        var placement = settings.CompanionPlacement;
        switch (item)
        {
            case "Detach":
                // She floats from where she stands instead of jumping to a corner.
                if (prepared && IsVisible) { placement.Left = Position.X; placement.Top = Position.Y; }
                placement.Detached = true; PlacementEdited(); break;
            case "Attach": placement.Detached = false; PlacementEdited(); break;
            case "AlwaysOnTop": placement.AlwaysOnTop = !placement.AlwaysOnTop; PlacementEdited(); break;
            case "Grow": ResizeBy(CompanionMenu.ScaleStep); break;
            case "Shrink": ResizeBy(-CompanionMenu.ScaleStep); break;
            case "ResetSize": placement.Scale = 1; PlacementEdited(); break;
            case "Barks": settings.BarksEnabled = !settings.BarksEnabled; Send(CompanionMenuCommand.BarksChanged); break;
            case "Hide":
                // The Avatar switch in Companion brings her back.
                settings.CompanionEnabled = false; menu.Menu.Close(); Hide(); Send(CompanionMenuCommand.AvatarHidden); break;
            case "Session":
                var state = SessionState?.Invoke() ?? CompanionSessionState.Unavailable;
                if (state == CompanionSessionState.Idle) Send(CompanionMenuCommand.StartSession);
                else if (state == CompanionSessionState.Running) Send(CompanionMenuCommand.StopSession);
                break;
            case "Settings": Send(CompanionMenuCommand.OpenSettings); break;
            case "ShowPanel":
                if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                owner.Activate(); break;
        }
    }
    // Panel work runs once the menu has closed: hiding releases this window and its menu.
    private void Send(CompanionMenuCommand command) => Dispatcher.UIThread.Post(() => MenuCommand?.Invoke(command));
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
        // The new clip decodes from its start; the outgoing one keeps moving until its fade ends.
        var jobs = new List<FrameJob>(2);
        foreach (var layer in new[] { active, outgoing })
            if (layer.Clip.Length > 0 && (layer == active || fading || !active.Ready))
                jobs.Add(new(layer, layer.Clip, layer.Generation, CompanionEmoteDirector.PositionOf(layer.Started, now), !layer.Ready));
        lastRequest = now;
        if (jobs.Count == 0) return;
        pending = Task.Run(() =>
        {
            beforeDecode?.Invoke();
            var frames = new DecodedFrame?[jobs.Count];
            for (var index = 0; index < jobs.Count; index++)
            {
                var job = jobs[index];
                if (job.Layer.Decoder?.Name != job.Clip) { job.Layer.Decoder?.Dispose(); job.Layer.Decoder = new(job.Clip); }
                frames[index] = job.Layer.Decoder.Decode(job.Position, job.Force);
            }
            return frames;
        });
        _ = pending.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            pending = null;
            if (closed) { _ = result.Exception; return; }
            if (result.IsFaulted) { LastError = result.Exception!.GetBaseException().Message; Hide(); return; }
            if (!(HasPortrait ? IsClipPlaying : CanBootstrap)) return;
            for (var index = 0; index < jobs.Count; index++)
            {
                var job = jobs[index];
                if (result.Result[index] is { } frame && job.Layer.Generation == job.Generation && job.Layer.Clip == job.Clip)
                    Publish(job.Layer, frame);
            }
        }), TaskScheduler.Default);
    }
    private void Publish(Layer layer, DecodedFrame frame)
    {
        if (layer.Bitmap is not { } bitmap || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            var old = layer.Bitmap;
            layer.Bitmap = bitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            layer.Image.Source = bitmap; old?.Dispose();
        }
        using (var pixels = bitmap.Lock())
            for (var row = 0; row < frame.Height; row++)
                System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, row * frame.Width * 4,
                    pixels.Address + row * pixels.RowBytes, frame.Width * 4);
        DecodedFrames++; layer.Image.InvalidateVisual();
        if (!layer.Ready) { layer.Ready = true; if (layer == active) BeginFade(); }
    }

    public void FinishImmediately() { forced = true; Release(); Close(); }
    private void Release()
    {
        if (closed) return;
        closed = true; owner.PositionChanged -= OwnerMoved; owner.PropertyChanged -= OwnerChanged;
        menu.Menu.Close(); Hide(); front.Image.Source = back.Image.Source = tube.Source = null; speech.IsVisible = false; bob.Y = 0;
        front.Bitmap?.Dispose(); back.Bitmap?.Dispose(); front.Bitmap = back.Bitmap = null;
        if (pending is { } work) _ = work.ContinueWith(_ => { front.Decoder?.Dispose(); back.Decoder?.Dispose(); }, TaskScheduler.Default);
        else { front.Decoder?.Dispose(); back.Decoder?.Dispose(); }
    }

    private sealed record DecodedFrame(byte[] Pixels, int Width, int Height);
    private sealed record FrameJob(Layer Layer, string Clip, long Generation, double Position, bool Force);
    // One of the two crossfading portraits. Decoder is touched only by the decode task.
    private sealed class Layer(Image image)
    {
        public Image Image { get; } = image;
        public WriteableBitmap? Bitmap { get; set; }
        public GifClip? Decoder { get; set; }
        public string Clip { get; set; } = "";
        public double Started { get; set; }
        public long Generation { get; set; }
        public bool Ready { get; set; }
    }
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
        // A clip plays once: past its end it holds the last frame until the director moves on.
        public DecodedFrame? Decode(double seconds, bool force)
        {
            var index = FrameAt(ends, seconds);
            if (index == decoded && !force) return null;
            var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), new SKCodecOptions(index));
            if (result != SKCodecResult.Success) throw new InvalidDataException("Companion frame could not be decoded.");
            decoded = index; return new(bitmap.Bytes, bitmap.Width, bitmap.Height);
        }
        internal static int FrameAt(IReadOnlyList<double> ends, double seconds)
        {
            for (var index = 0; index < ends.Count; index++) if (seconds < ends[index]) return index;
            return Math.Max(0, ends.Count - 1);
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
