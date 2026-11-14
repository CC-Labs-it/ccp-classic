using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // In memory only; a bug report includes it when the player ticks the box (SessionLog).
            TaskScheduler.UnobservedTaskException += (_, e) => SessionLog.Shared.Error("background task", e.Exception);
            var store = new SettingsStore(SettingsStore.DefaultPath);
            var settings = store.Load(out var recovered);
            desktop.MainWindow = new MainWindow(settings, store, desktopEffects: true,
                startupLaunch: desktop.Args?.Contains("--startup", StringComparer.Ordinal) == true) { SettingsRecovered = recovered };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
