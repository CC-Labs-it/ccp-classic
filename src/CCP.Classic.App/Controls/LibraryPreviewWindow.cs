using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics.CodeAnalysis;
using CCP.Classic.App.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

// One selected media item at a time, following CCP's native asset-preview interaction.
public sealed class LibraryPreviewWindow : Window
{
    private readonly LibraryAsset asset;
    private readonly ClassicSettings settings;
    private readonly LocalizationService text;
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly LibraryWaveform waveform = new();
    private readonly TextBlock status = new() { FontSize = 11 };
    private readonly LibVlcAudioPlayback audio = new();
    private readonly AudioEnvelopeCache envelopes = new();
    private readonly CancellationTokenSource stop = new();
    private readonly DispatcherTimer timer;
    private readonly Button play;
    private readonly Func<string, ILocalVideoPlayback> createVideo;
    private readonly TimeProvider clock;
    private ILocalVideoPlayback? video;
    private Bitmap? still;
    private LocalImageClip? imageClip;
    private WriteableBitmap? videoImage;
    private AudioPlaybackTicket? ticket;
    private bool playing, ready, closed, loaded;
    private long videoStarted;
    private long imageTick;
    private double imageSeconds;
    private double volume = .4;
    public LibraryPreviewWindow(LibraryAsset asset, ClassicSettings settings, LocalizationService text, Action<Window> configureInput)
        : this(asset, settings, text, configureInput, path => new LocalVideoPlayback(path, 1800), TimeProvider.System) { }
    internal LibraryPreviewWindow(LibraryAsset asset, ClassicSettings settings, LocalizationService text, Action<Window> configureInput,
        Func<string, ILocalVideoPlayback> videoFactory, TimeProvider clock)
    {
        this.asset = asset; this.settings = settings; this.text = text;
        createVideo = videoFactory; this.clock = clock;
        Title = text["Preview"] + " · " + Path.GetFileName(asset.Path);
        Width = 760; Height = 600; MinWidth = 480; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), Margin = new(18), RowSpacing = 12 };
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = Path.GetFileName(asset.Path), FontSize = 19, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        heading.Children.Add(new TextBlock { Text = text[asset.Kind] + " · " + Path.GetExtension(asset.Path).TrimStart('.').ToUpperInvariant(), Foreground = Brush("#BFA6D6") });
        root.Children.Add(heading);
        var stage = new Border { CornerRadius = new(14), Background = Brush("#090C18"), BorderBrush = Brush("#594675"), BorderThickness = new(1), ClipToBounds = true };
        if (asset.Kind == "Audio")
        {
            var presentation = new Grid { RowDefinitions = new("*,100"), Margin = new(18) };
            using var artwork = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/audio.png"));
            still = new Bitmap(artwork);
            image.Source = still; presentation.Children.Add(image); Grid.SetRow(waveform, 1); presentation.Children.Add(waveform); stage.Child = presentation;
        }
        else stage.Child = image;
        Grid.SetRow(stage, 1); root.Children.Add(stage);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        play = ActionButton("Play", TogglePlay); play.Name = "LibraryPreviewPlay"; play.IsVisible = asset.Kind != "Images"; controls.Children.Add(play);
        if (asset.Kind == "Audio")
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 120 };
            Avalonia.Automation.AutomationProperties.SetName(slider, text["Volume"]);
            slider.PropertyChanged += (_, change) => { if (change.Property == Slider.ValueProperty) { volume = slider.Value / 100; audio.SetMasterGain(volume); } };
            controls.Children.Add(slider);
        }
        controls.Children.Add(ActionButton("Close", Close));
        Grid.SetRow(controls, 2); root.Children.Add(controls); Grid.SetRow(status, 3); root.Children.Add(status); Content = root;
        configureInput(this);
        timer = new(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
        Opened += (_, _) => { if (!loaded) { loaded = true; _ = LoadAsync(); } timer.Start(); };
        Closed += (_, _) => Release();
    }
    private async Task LoadAsync()
    {
        try
        {
            if (asset.Kind == "Images")
            {
                var token = stop.Token;
                var loaded = await Task.Run(() => LocalImageClip.Decode(asset.Path, token), token);
                if (closed) { loaded.Dispose(); return; }
                imageClip = loaded; image.Source = loaded; imageTick = clock.GetTimestamp();
                status.Text = $"{loaded.Size.Width} × {loaded.Size.Height}";
            }
            else if (asset.Kind == "Videos")
            { OpenVideo(); status.Text = text["PreviewMuted"]; }
            else { waveform.Envelope = envelopes.Get(asset.Path); status.Text = text["AudioPreviewHint"]; }
        }
        catch (Exception error) when (error is IOException or ArgumentException or OperationCanceledException or UnauthorizedAccessException)
        { if (!closed) status.Text = text["PreviewUnavailable"]; }
    }
    private void TogglePlay()
    {
        if (closed) return;
        if (asset.Kind == "Videos")
        {
            if (video is null || video.IsFinished)
            { OpenVideo(); }
            playing = !playing; video.SetPaused(!playing);
        }
        else if (asset.Kind == "Audio")
        {
            if (playing) { audio.StopAll(); playing = false; }
            else { audio.SetMasterGain(volume); ticket = audio.PlayTracked(asset.Path, 1); playing = ticket is not null; if (!playing) status.Text = text["PreviewUnavailable"]; }
        }
        play.Content = text[playing ? "Stop" : "Play"];
    }
    private void Tick()
    {
        if (closed) return;
        if (!IsVisible || WindowState == WindowState.Minimized)
        { video?.SetPaused(true); if (!ready) videoStarted = clock.GetTimestamp(); imageTick = clock.GetTimestamp(); audio.StopAll(); playing = false; play.Content = text["Play"]; return; }
        try { TickMedia(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        { FailVideo(); }
    }
    [MemberNotNull(nameof(video))]
    private void OpenVideo()
    {
        video?.Dispose(); video = createVideo(asset.Path); ready = false; videoStarted = clock.GetTimestamp();
        video.SetGain(0); video.Start();
    }
    private void TickMedia()
    {
        var animate = !settings.GentleVisuals && !settings.ReducedMotion && !settings.PerformanceMode;
        timer.Interval = TimeSpan.FromMilliseconds((video is not null && (playing || !ready) && !settings.PerformanceMode)
            || (imageClip is { FrameCount: > 1 } && animate) ? 33 : 100);
        if (imageClip is { } clip)
        {
            var now = clock.GetTimestamp();
            if (animate) imageSeconds += Math.Max(0, clock.GetElapsedTime(imageTick, now).TotalSeconds);
            imageTick = now; clip.Advance(imageSeconds); image.InvalidateVisual();
        }
        if (video is not null && !ready) video.SetPaused(false);
        if (video?.TryCopyFrame(out var frame) == true && frame is not null)
        {
            if (frame.Width is <= 0 or > 960 || frame.Height is <= 0 or > 540 || frame.Pixels is null
                || frame.Pixels.LongLength < (long)frame.Width * frame.Height * 4) throw new IOException("Preview frame is invalid");
            if (videoImage?.PixelSize != new PixelSize(frame.Width, frame.Height))
            { image.Source = null; videoImage?.Dispose(); videoImage = new(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque); image.Source = videoImage; }
            using var pixels = videoImage.Lock();
            for (var row = 0; row < frame.Height; row++) System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, row * frame.Width * 4, pixels.Address + row * pixels.RowBytes, frame.Width * 4);
            if (!ready) { ready = true; if (!playing) video.SetPaused(true); }
        }
        if (video is not null && !ready && (video.IsFinished || clock.GetElapsedTime(videoStarted).TotalSeconds >= 15))
        { FailVideo(); return; }
        if (video?.IsFinished == true || ticket?.IsFinished == true)
        { playing = false; play.Content = text["Play"]; ticket = null; }
        waveform.Position = audio.GetAudioSamples().FirstOrDefault()?.SourceSeconds ?? 0;
        waveform.InvalidateVisual();
        if (audio.LastError is not null || video?.LastError is not null) status.Text = text["PreviewUnavailable"];
    }
    private void FailVideo()
    {
        var old = video; video = null; playing = ready = false; image.Source = null; videoImage?.Dispose(); videoImage = null;
        try { old?.SetGain(0); } catch (Exception) { }
        try { old?.Dispose(); } catch (Exception) { }
        play.Content = text["Play"]; status.Text = text["PreviewUnavailable"];
    }
    private Button ActionButton(string key, Action action)
    { var result = new Button { Content = text[key], Padding = new(18, 8) }; result.Classes.Add("pill"); result.Click += (_, _) => action(); return result; }
    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
    private void Release()
    {
        if (closed) return; closed = true; timer.Stop(); stop.Cancel();
        image.Source = null; waveform.Envelope = null;
        try { video?.Dispose(); } catch (Exception) { }
        audio.Dispose(); envelopes.Dispose(); still?.Dispose(); imageClip?.Dispose(); videoImage?.Dispose(); stop.Dispose();
    }
}

internal sealed class LibraryWaveform : Control
{
    public RmsEnvelope? Envelope { get; set; }
    public double Position { get; set; }
    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height; var width = Bounds.Width;
        if (width <= 0 || height <= 0) return;
        var duration = Envelope?.DurationSeconds ?? 0;
        var quiet = new SolidColorBrush(Color.Parse("#584568")); var accent = new SolidColorBrush(Color.Parse("#FF69B4"));
        for (var i = 0; i < 96; i++)
        {
            var seconds = duration * i / 96;
            var amplitude = Math.Max(.025, Envelope?.Sample(seconds) ?? 0);
            var bar = Math.Clamp(amplitude * height * 2, 2, height - 8);
            context.DrawRectangle(seconds <= Position && Position > 0 ? accent : quiet, null,
                new Rect(i * width / 96, (height - bar) / 2, Math.Max(1, width / 96 - 2), bar), 2, 2);
        }
    }
}
