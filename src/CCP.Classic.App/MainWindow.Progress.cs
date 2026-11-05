using Avalonia.Controls;
using Avalonia.VisualTree;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private ProgressStore? progressStore;
    private DateTimeOffset lastProgressSave;
    public ProgressTracker Progress { get; private set; } = null!;

    private void InitializeProgress()
    {
        ProgressData? data = null;
        if (store is not null)
        {
            progressStore = new(ProgressStore.DefaultPath);
            data = progressStore.Load(out _);
        }
        Progress = new(data);
        Progress.Changed += UpdateProgressHeader;
        Progress.Recorded += value =>
        {
            if (value.Kind == ProgressEventKind.Time) Progress.AddXp(value.Amount / 5);
            else if (value.Kind == ProgressEventKind.Bubble) Progress.AddXp(2);
            else if (value.Kind == ProgressEventKind.Video) Progress.AddXp(50);
        };
        Session.Changed += () => { if (Session.IsRunning) Progress.StartSession(Settings.Preset); };
        Session.StopRequested += () =>
        {
            Progress.EndSession(Session.CompletedNormally, !Session.PanicEnabled,
                Settings.Effects["video"].Enabled && Settings.Effects["video"].Values["strict"] != 0);
            SaveProgress();
        };
        Progress.RefreshAchievements();
    }

    private void TickProgress()
    {
        if (!Session.IsRunning) return;
        Progress.Tick(new EffectProgressState(
            Settings.Effects["pink"].Enabled && Settings.Effects["pink"].Values["opacity"] > 0,
            Settings.Effects["spiral"].Enabled && Settings.Effects["spiral"].Values["opacity"] > 0,
            Settings.Effects["bubble"].Enabled, Settings.Effects["bounce"].Enabled,
            Settings.Effects["video"].Enabled && Settings.Effects["video"].Values["strict"] != 0, !Session.PanicEnabled));
        if (DateTimeOffset.UtcNow - lastProgressSave >= TimeSpan.FromSeconds(30)) SaveProgress();
    }
    private void UpdateProgressHeader()
    {
        if (root is null) return;
        if (root.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(item => item.Name == "HeaderLevel") is { } label)
            label.Text = text["Level"] + " " + Progress.Level + "     " + (int)Progress.XpIntoLevel + " " + text["XP"];
        if (root.GetVisualDescendants().OfType<ProgressBar>().FirstOrDefault(item => item.Name == "HeaderXp") is { } bar)
        { bar.Maximum = Progress.XpForNextLevel; bar.Value = Math.Min(bar.Maximum, Progress.XpIntoLevel); }
    }
    private void SaveProgress()
    {
        lastProgressSave = DateTimeOffset.UtcNow;
        try { progressStore?.Save(Progress.Data); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (status is not null) SetStatus("SaveFailed"); }
    }
}
