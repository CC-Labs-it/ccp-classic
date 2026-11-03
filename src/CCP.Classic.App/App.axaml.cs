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
            var store = new SettingsStore(SettingsStore.DefaultPath);
            var settings = store.Load(out var recovered);
            desktop.MainWindow = new MainWindow(settings, store) { SettingsRecovered = recovered };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
