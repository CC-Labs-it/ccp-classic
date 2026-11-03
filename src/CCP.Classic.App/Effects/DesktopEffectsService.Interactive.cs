using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;
using CCP.Classic.App.Media;
using CCP.Classic.Core.LocalDevices;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private InteractiveEffectsTimeline interactive = null!;
    private LockEffectsTimeline lockEffects = null!;
    private InteractiveEffectsFrame interactiveFrame = InteractiveEffectsFrame.Empty;
    private LockEffectsFrame lockFrame = LockEffectsFrame.Empty;
    private readonly List<LockCardWindow> lockWindows = [];
    private string lockInput = "";
    private long lockId;
    private Avalonia.Threading.DispatcherTimer? lockPreviewTimer;
    private readonly LibVlcAudioPlayback wipeAudio = new(), cueAudio = new(), barkAudio = new();
    private AudioPlaybackGate cueGate = null!, barkGate = null!;
    private AudioPlaybackTicket? wipeTicket;
    private Task<MediaLibrarySnapshot>? audioLibrary;
    private LocalImageLibrary? selectedImages;
    private bool imagesScanned;
    private CancellationTokenSource? interactiveScan;
    private readonly RecordedBubblePops popSounds = new();
    private long playingWipe;
    private bool wipePlaying;
    private int audioFile, lockErrors, lockCorrect;
    private double lockOpenedAt;
    public event Action<InteractiveEffectEvent>? InteractiveEvent;
    public event Action<int, int, double>? LockCompleted;
    public event Action<bool>? WipePlayingChanged;

    private void InitializeInteractive()
    {
        interactive = new(settings) { BounceTextMeasurer = BouncingTextRenderer.Measure }; lockEffects = new(settings);
        cueGate = new(cueAudio); barkGate = new(barkAudio);
        interactive.EventOccurred += value =>
        {
            TriggerBubbleEffect(value);
            popSounds.Play(cueGate, value.PlaySound && settings.Effects["audio"].Enabled, settings.Bubbles.VolumePercent / 100d);
            InteractiveEvent?.Invoke(value);
            if (value.Kind == InteractiveEffectEventKind.WallHit) DeviceEvent?.Invoke(LocalDeviceEvent.BouncingText);
        };
        lockEffects.EventOccurred += value => InteractiveEvent?.Invoke(value);
    }
    private void StartInteractive()
    {
        lockPreviewTimer?.Stop(); CloseLock();
        interactive.Start(); lockEffects.Start(); audioFile = 0;
        RefreshInteractiveLibrary(force: true);
    }
    private void TickInteractive(double elapsed)
    {
        if (!imagesScanned && audioLibrary?.IsCompletedSuccessfully == true)
        {
            imagesScanned = true;
            ReplaceSelectedImages(audioLibrary.Result.Images);
        }
        var screens = owner.Screens.All;
        interactive.SetViewport(screens.Count > 0 ? screens.Max(s => s.Bounds.Width / s.Scaling) : 1600,
            screens.Count > 0 ? screens.Max(s => s.Bounds.Height / s.Scaling) : 900);
        timeline.SetViewport(screens.Count > 0 ? screens.Max(s => s.Bounds.Width / s.Scaling) : 1600,
            screens.Count > 0 ? screens.Max(s => s.Bounds.Height / s.Scaling) : 900);
        interactive.SetImageCount(selectedImages?.Count ?? images.Count);
        interactive.VideoAvailable = videos.CanTriggerBubble;
        interactive.VideoPlaying = videos.IsPlaying;
        lockEffects.VideoBlocking = videos.IsBlocking;
        interactiveFrame = interactive.Step(elapsed); lockFrame = lockEffects.Step(elapsed);
        TickWipe();
        if (lockFrame.LockCard is { } card)
        {
            if (lockWindows.Count == 0 || lockId != card.Id) OpenLock(card, elapsed);
            foreach (var window in lockWindows) window.Update(card, lockInput);
        }
        else CloseLock();
    }
    public bool PreviewLockCard()
    {
        if (running) { var shown = lockEffects.TriggerLock(preview: true); Tick(); return shown; }
        lockEffects.Start();
        if (!lockEffects.TriggerLock(preview: true)) { lockEffects.Stop(); return false; }
        started = clock.GetTimestamp();
        var initial = lockEffects.Step(0).LockCard!; OpenLock(initial, 0);
        lockPreviewTimer?.Stop();
        lockPreviewTimer = new(TimeSpan.FromMilliseconds(33), Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
        {
            var cue = lockEffects.Step(clock.GetElapsedTime(started).TotalSeconds).LockCard;
            if (cue is null) { lockPreviewTimer?.Stop(); lockEffects.Stop(); CloseLock(); return; }
            foreach (var window in lockWindows) window.Update(cue, lockInput);
        });
        lockPreviewTimer.Start(); return true;
    }
    private void OpenLock(LockCardCue card, double elapsed)
    {
        CloseLock(); lockId = card.Id; lockErrors = lockCorrect = 0; lockOpenedAt = elapsed;
        var text = new LocalizationService(settings.Language);
        var primary = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        var screens = owner.Screens.All.OrderByDescending(s => ReferenceEquals(s, primary)).ToArray();
        foreach (var screen in screens)
        {
            var child = new LockCardWindow(card, settings.LockCard, text, lockWindows.Count == 0)
            {
                Position = screen.Bounds.Position,
                Width = screen.Bounds.Width / screen.Scaling, Height = screen.Bounds.Height / screen.Scaling
            };
            child.AnswerChanged += value =>
            {
                lockInput = value;
                if (value.Length > 0 && !LockCardText.IsPrefixOf(value, card.Phrase))
                { lockErrors++; lockEffects.RegisterTypo(card.Id); }
                if (lockEffects.SubmitLock(card.Id, value))
                {
                    lockCorrect++; lockInput = "";
                    var completed = lockEffects.Step(clock.GetElapsedTime(started).TotalSeconds).LockCard;
                    if (running && completed?.Completed >= card.Repetitions)
                        LockCompleted?.Invoke(lockErrors, lockCorrect, clock.GetElapsedTime(started).TotalSeconds - lockOpenedAt);
                }
                var current = lockEffects.Step(clock.GetElapsedTime(started).TotalSeconds).LockCard;
                if (current is not null) foreach (var window in lockWindows) window.Update(current, lockInput);
            };
            child.StopRequested += () => { if (!card.Strict) session.Stop(); };
            configureInput(child); lockWindows.Add(child); child.Show(owner);
        }
        DeviceEvent?.Invoke(LocalDeviceEvent.LockCard);
    }
    private void CloseLock()
    {
        foreach (var window in lockWindows) window.Finish();
        lockWindows.Clear(); lockInput = "";
    }
    private void StopInteractive()
    {
        lockPreviewTimer?.Stop();
        interactiveScan?.Cancel(); interactiveScan?.Dispose(); interactiveScan = null;
        selectedImages?.Dispose(); selectedImages = null; timeline.SetImageCount(images.Count);
        wipeAudio.StopAll(); cueGate.Stop(); barkGate.Stop(); wipeTicket = null; playingWipe = 0; SetWipePlaying(false);
        interactive.Stop(); lockEffects.Stop(); interactiveFrame = InteractiveEffectsFrame.Empty;
        lockFrame = LockEffectsFrame.Empty; CloseLock();
    }

    private void TickWipe()
    {
        var audioEnabled = settings.Effects["audio"].Enabled && settings.Effects["audio"].Values["master"] > 0;
        var gain = audioEnabled ? settings.Effects["audio"].Values["master"] / 100 : 0;
        if (settings.Audio.DuckingEnabled && (videos.IsPlaying || KeywordDucking || FlashDucking)) gain *= 1 - settings.Effects["audio"].Values["duck"] / 100;
        cueGate.Update(audioEnabled, gain);
        barkGate.Update(audioEnabled && settings.CompanionEnabled && settings.BarksEnabled, gain);
        var cue = lockFrame.MindWipe;
        wipeAudio.SetMasterGain(gain * (cue?.Volume ?? 0));
        if (!MindWipeRunRule.CanPlay(session.IsRunning, cue is not null, audioEnabled) || cue is null || cue.Volume <= 0)
        {
            if (wipeTicket is not null) wipeAudio.StopAll();
            if (cue is not null) lockEffects.CancelMindWipe(cue.Id);
            wipeTicket = null; playingWipe = 0; SetWipePlaying(false); return;
        }
        if (wipeTicket is { } ticket)
        {
            if (ticket.PlayedSeconds > 0) { SetWipePlaying(true); HapticWipeStarted(playingWipe); }
            if (!ticket.IsFinished) return;
            if (ticket.WasSuccessful) lockEffects.CompleteMindWipe(playingWipe);
            else lockEffects.CancelMindWipe(playingWipe);
            if (!ticket.WasSuccessful || !cue.Loop) SetWipePlaying(false);
            wipeTicket = null; playingWipe = 0; return;
        }
        string? clip;
        if (!string.IsNullOrWhiteSpace(settings.MindWipe.AudioPath)) clip = LocalAudioInput.LocalPath(settings.MindWipe.AudioPath);
        else
        {
            if (audioLibrary?.IsFaulted == true) { _ = audioLibrary.Exception; lockEffects.CancelMindWipe(cue.Id); return; }
            if (audioLibrary?.IsCompletedSuccessfully != true) return;
            var files = audioLibrary.Result.Audio.Where(settings.Library.Includes).ToArray();
            clip = files.Length > 0 ? files[audioFile++ % files.Length] : starter.Phrases[2].AudioPath;
        }
        if (clip is null || (wipeTicket = wipeAudio.PlayTracked(clip, 1)) is null)
        { lockEffects.CancelMindWipe(cue.Id); SetWipePlaying(false); }
        else playingWipe = cue.Id;
    }
    private void SetWipePlaying(bool value)
    {
        if (wipePlaying == value) return;
        wipePlaying = value; WipePlayingChanged?.Invoke(value);
    }

    private void DisposeInteractiveAudio()
    {
        wipeAudio.Dispose(); cueAudio.Dispose(); barkAudio.Dispose(); keywordAudio.Dispose();
    }
}
