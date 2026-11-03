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

// Original tube clips, one decoded frame in flight. The session owner supplies the shared clock.
internal sealed class CompanionWindow : Window
{
    private readonly Window owner;
    private readonly ClassicSettings settings;
    private readonly bool requireTransparency;
    private readonly Image portrait = new() { Stretch = Stretch.Uniform };
    private readonly Image previousPortrait = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock words = new() { Name = "CompanionBark", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        Foreground = Brushes.White, FontSize = 18 };
    private readonly Border speech;
    private readonly Fade entrance = new(), dissolve = new();
    private WriteableBitmap? bitmap, previous;
    private GifClip? decoder;
    private Task<DecodedFrame?>? pending;
    private string wantedClip = "idle", displayedClip = "";
    private double lastNow, animationTime, lastRequest = double.NegativeInfinity, reaction = 7.2;
    private bool closed, prepared, hasTime, forced, stopPending;
    public string? LastError { get; private set; }
    public int DecodedFrames { get; private set; }
    internal bool IsDecodePending => pending is not null;
    private bool CanBootstrap => !closed && settings.CompanionEnabled && WindowState != WindowState.Minimized
        && owner.IsVisible && owner.WindowState != WindowState.Minimized
        && (owner.IsActive || owner.OwnedWindows.Any(window => window.IsActive));
    public bool IsMotionActive => CanBootstrap && IsVisible
        && !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode;
    public event Action? AvatarClicked;
    public event Action? StopRequested;

    public CompanionWindow(Window owner, ClassicSettings settings, Action<Window> configureInput, bool requireTransparency = true)
    {
        this.owner = owner; this.settings = settings; this.requireTransparency = requireTransparency;
        Title = "CCP Classic"; Width = 240; Height = 390;
        WindowStartupLocation = WindowStartupLocation.Manual; WindowDecorations = WindowDecorations.None;
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = Brushes.Transparent; TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var images = new Grid(); images.Children.Add(previousPortrait); images.Children.Add(portrait);
        images.PointerPressed += (_, args) =>
        {
            var pointer = args.GetCurrentPoint(images).Properties;
            if (pointer.IsLeftButtonPressed) { reaction = 7.2; AvatarClicked?.Invoke(); args.Handled = true; }
            else if (pointer.IsRightButtonPressed) { StopRequested?.Invoke(); args.Handled = true; }
        };
        speech = new Border { Child = words, Background = new SolidColorBrush(Color.Parse("#E61C1C35")),
            CornerRadius = new(12), Padding = new(12, 8), Margin = new(0, 0, 0, 8), IsHitTestVisible = false, Opacity = 0 };
        var layout = new Grid { RowDefinitions = new("Auto,*") };
        layout.Children.Add(speech); Grid.SetRow(images, 1); layout.Children.Add(images); Content = layout;
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

    public void Update(CompanionBarkCue? bark, double seconds)
    {
        if (closed) return;
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
        if (IsMotionActive)
        {
            animationTime += dt; reaction = Math.Max(0, reaction - dt);
            var clip = approved ? "talkA" : reaction > 0 ? "greeting" : "idle";
            if (clip != wantedClip) { wantedClip = clip; animationTime = 0; lastRequest = double.NegativeInfinity; }
        }
        if (pending is null && settings.CompanionEnabled && LastError is null
            && (bitmap is null ? CanBootstrap : IsMotionActive && now - lastRequest >= .05)) RequestFrame(now);
    }

    private void EnsureShown()
    {
        if (IsVisible || closed || LastError is not null) return;
        if (!prepared)
        {
            Position = new(-32000, -32000); Width = Height = 1; Show(owner);
            if (requireTransparency && ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
            { LastError = "Companion transparency is unavailable."; Hide(); return; }
            Hide(); Width = 240; Height = 390; prepared = true;
        }
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea; var scale = screen.Scaling;
            Position = new(area.Right - (int)(Width * scale) - 20, area.Bottom - (int)(Height * scale) - 20);
        }
        Show(owner);
    }

    private void RequestFrame(double now)
    {
        var clip = wantedClip; var bootstrap = bitmap is null; var position = bootstrap ? 0 : animationTime;
        lastRequest = now;
        pending = Task.Run(() =>
        {
            if (decoder?.Name != clip) { decoder?.Dispose(); decoder = new(clip); }
            return decoder.Decode(position, bootstrap);
        });
        _ = pending.ContinueWith(result => Dispatcher.UIThread.Post(() =>
        {
            pending = null;
            if (closed) { _ = result.Exception; return; }
            if (result.IsFaulted) { LastError = result.Exception!.GetBaseException().Message; Hide(); return; }
            if (result.Result is not { } frame || clip != wantedClip || !(bitmap is null ? CanBootstrap : IsMotionActive)) return;
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
        closed = true; Hide(); portrait.Source = previousPortrait.Source = null; speech.IsVisible = false;
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
