using Avalonia.Controls;
using Avalonia.Threading;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private bool closingAfterCleanup, waitingForCleanup;

    private void AwaitLocalCleanup(object? sender, WindowClosingEventArgs args)
    {
        if (closingAfterCleanup) return;
        if (waitingForCleanup) { args.Cancel = true; return; }
        Session.Dispose();
        var cleanup = Task.WhenAll(StopToysAsync(), effects?.StopWallpaperAsync() ?? Task.CompletedTask);
        if (cleanup.IsCompletedSuccessfully) return;
        args.Cancel = true; waitingForCleanup = true; IsEnabled = false;
        _ = FinishClosingAsync(cleanup);
    }

    private async Task FinishClosingAsync(Task cleanup)
    {
        try { await cleanup.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is TimeoutException or IOException or InvalidOperationException or OperationCanceledException)
        {
            // A failed wallpaper restore keeps its journal for the next launch.
        }
        Dispatcher.UIThread.Post(() => { closingAfterCleanup = true; Close(); });
    }
}
