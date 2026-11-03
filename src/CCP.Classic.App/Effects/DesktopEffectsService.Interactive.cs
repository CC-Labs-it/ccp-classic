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
    private Window? lockWindow;
    private TextBlock? lockPhrase, lockCount;
    private long lockId;
    private bool closingLock;
    private readonly LibVlcAudioPlayback wipeAudio = new(), cueAudio = new(), barkAudio = new();
    private AudioPlaybackGate cueGate = null!, barkGate = null!;
    private AudioPlaybackTicket? wipeTicket;
    private Task<MediaLibrarySnapshot>? audioLibrary;
    private LocalImageLibrary? selectedImages;
    private bool imagesScanned;
    private CancellationTokenSource? interactiveScan;
    private Task<string?>? popSound;
    private long playingWipe;
    private bool wipePlaying;
    private int audioFile, lockErrors, lockCorrect;
    private double lockOpenedAt;
    public event Action<InteractiveEffectEvent>? InteractiveEvent;
    public event Action<int, int, double>? LockCompleted;
    public event Action<bool>? WipePlayingChanged;

    private void InitializeInteractive()
    {
        interactive = new(settings); lockEffects = new(settings);
        cueGate = new(cueAudio); barkGate = new(barkAudio);
        interactive.EventOccurred += value =>
        {
            TriggerBubbleEffect(value);
            if (value.PlaySound && settings.Effects["audio"].Enabled && popSound?.IsCompletedSuccessfully == true
                && popSound.Result is { } path) cueGate.Play(path, .7);
            InteractiveEvent?.Invoke(value);
            if (value.Kind == InteractiveEffectEventKind.CornerHit) DeviceEvent?.Invoke(LocalDeviceEvent.BouncingText);
        };
        lockEffects.EventOccurred += value => InteractiveEvent?.Invoke(value);
    }
    private void StartInteractive()
    {
        interactive.Start(); lockEffects.Start(); audioFile = 0;
        interactiveScan = new(); var token = interactiveScan.Token;
        var folders = settings.Library.AudioFolders.ToArray();
        var imageFolders = settings.Library.ImageFolders.ToArray(); imagesScanned = false;
        audioLibrary = Task.Run(() => MediaLibrary.Scan(imageDirectories: imageFolders, audioDirectories: folders,
            cancellationToken: token), token);
        popSound ??= Task.Run<string?>(() =>
        {
            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "CCP Classic"); Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "pop-" + Guid.NewGuid().ToString("N") + ".wav");
                File.WriteAllBytes(path, ProceduralAudio.PopWave()); return path;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        });
    }
    private void TickInteractive(double elapsed)
    {
        if (!imagesScanned && audioLibrary?.IsCompletedSuccessfully == true)
        {
            imagesScanned = true;
            var paths = audioLibrary.Result.Images.Where(settings.Library.Includes).ToArray();
            if (paths.Length > 0 && images.Count > 0)
            {
                selectedImages = new(starter.Images.Concat(paths), images[0]);
                timeline.SetImageCount(selectedImages.Count);
            }
        }
        interactive.SetImageCount(selectedImages?.Count ?? images.Count);
        interactive.VideoAvailable = videos.CanTriggerBubble;
        interactiveFrame = interactive.Step(elapsed); lockFrame = lockEffects.Step(elapsed);
        TickWipe();
        if (lockFrame.LockCard is { } card)
        {
            if (lockWindow is null || lockId != card.Id) OpenLock(card, elapsed);
            lockPhrase!.Text = card.Phrase;
            lockCount!.Text = card.Completed + " / " + card.Repetitions;
            lockWindow!.Opacity = Math.Max(.01, card.Opacity);
        }
        else CloseLock();
    }
    private void OpenLock(LockCardCue card, double elapsed)
    {
        CloseLock(); lockId = card.Id; lockErrors = lockCorrect = 0; lockOpenedAt = elapsed;
        var text = new LocalizationService(settings.Language);
        var panel = new StackPanel { Spacing = 16, Margin = new Avalonia.Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lockPhrase = new TextBlock { FontSize = 30, Foreground = Brushes.HotPink };
        lockCount = new TextBlock { FontSize = 16 };
        var entry = new TextBox { Width = 300, Name = "LockAnswer" };
        var submit = new Button { Content = text["Submit"], Name = "LockSubmit" };
        submit.Click += (_, _) =>
        {
            if (lockEffects.SubmitLock(card.Id, entry.Text ?? ""))
            {
                lockCorrect++;
                if (lockCorrect == card.Repetitions)
                    LockCompleted?.Invoke(lockErrors, lockCorrect, clock.GetElapsedTime(started).TotalSeconds - lockOpenedAt);
            }
            else lockErrors++;
            entry.Text = "";
        };
        panel.Children.Add(lockPhrase); panel.Children.Add(lockCount); panel.Children.Add(entry); panel.Children.Add(submit);
        var child = new Window { Title = text["LockCard"], Width = 420, Height = 320, CanResize = false,
            ShowInTaskbar = false, Topmost = true, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Opacity = .01 };
        child.Closing += (_, args) =>
        {
            if (closingLock) return;
            args.Cancel = true;
            if (!card.Strict) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (ReferenceEquals(child, lockWindow)) session.Stop(); });
        };
        configureInput(child); lockWindow = child; child.Show(owner); entry.Focus();
        DeviceEvent?.Invoke(LocalDeviceEvent.LockCard);
    }
    private void CloseLock()
    {
        closingLock = true; lockWindow?.Close(); lockWindow = null; lockPhrase = lockCount = null; closingLock = false;
    }
    private void StopInteractive()
    {
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
        if (videos.IsPlaying) gain *= 1 - settings.Effects["audio"].Values["duck"] / 100;
        cueGate.Update(audioEnabled, gain);
        barkGate.Update(audioEnabled && settings.CompanionEnabled && settings.BarksEnabled, gain);
        var cue = lockFrame.MindWipe;
        wipeAudio.SetMasterGain(gain * (cue?.Volume ?? 0));
        if (cue is null || !audioEnabled || cue.Volume <= 0)
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
        if (audioLibrary?.IsFaulted == true) { _ = audioLibrary.Exception; lockEffects.CancelMindWipe(cue.Id); return; }
        if (audioLibrary?.IsCompletedSuccessfully != true) return;
        var files = audioLibrary.Result.Audio.Where(settings.Library.Includes).ToArray();
        var clip = files.Length > 0 ? files[audioFile++ % files.Length] : starter.Phrases[2].AudioPath;
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
        wipeAudio.Dispose(); cueAudio.Dispose(); barkAudio.Dispose();
        if (popSound is { } task) _ = task.ContinueWith(result =>
        {
            if (result.IsCompletedSuccessfully && result.Result is { } path)
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }, TaskScheduler.Default);
    }
}
