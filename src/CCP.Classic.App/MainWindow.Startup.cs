using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CCP.Classic.Core;

namespace CCP.Classic.App;

public sealed partial class MainWindow
{
    private StartupRegistration? startupRegistration;
    private TrayIcon? startupTray;
    private TimeProvider startupTime = TimeProvider.System;
    private Func<(string Executable, string? ApplicationDll)> startupProcess = CurrentStartupProcess;
    private long startupStarted;
    private bool startupInitialized, startupPending, startupDisposed;

    private void InitializeStartup(bool startupLaunch)
    {
        if (startupInitialized || startupDisposed) return;
        var canHide = false;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Classic.App/Assets/logo.jpg"));
            using var bitmap = new Bitmap(stream);
            startupTray = new TrayIcon();
            startupTray.Icon = new WindowIcon(bitmap); startupTray.ToolTipText = "CCP Classic"; startupTray.IsVisible = true;
            startupTray.Clicked += (_, _) => ShowOwner();
            // A Linux exporter does not prove that a desktop indicator host is present.
            canHide = OperatingSystem.IsWindows() && startupTray.NativeMenuExporter is not null;
            RefreshTrayMenu();
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException or ArgumentException)
        {
            startupTray?.Dispose(); startupTray = null;
        }
        ApplyStartupPreferences(startupLaunch, canHide);
    }

    // Kept separate from native tray creation so headless tests never create an OS indicator.
    private void ApplyStartupPreferences(bool startupLaunch, bool recoverableTray)
    {
        if (startupInitialized || startupDisposed) return;
        startupInitialized = true;
        Session.StopRequested += CancelPendingStartup;
        Session.Changed += CancelStartupIfRunning;
        if (startupLaunch && Settings.StartHidden)
        {
            if (recoverableTray) Hide();
            else { ShowInTaskbar = true; WindowState = WindowState.Minimized; }
        }
        startupPending = Settings.AutoRun && !Session.IsRunning;
        startupStarted = startupTime.GetTimestamp();
        RefreshTrayMenu();
    }

    private void TickStartup()
    {
        if (!startupPending || startupDisposed) return;
        if (!Settings.AutoRun || Session.IsRunning) { CancelPendingStartup(); return; }
        var available = input.GlobalAvailable;
        if (!available && startupTime.GetElapsedTime(startupStarted).TotalSeconds < 2) return;
        CancelPendingStartup();
        ShowOwner();
        Session.Start();
        if (!available) SetStatus("GlobalPanicUnavailable");
        RefreshTrayMenu();
    }

    private void CancelPendingStartup() => startupPending = false;
    private void CancelStartupIfRunning() { if (Session.IsRunning) CancelPendingStartup(); }

    private void ShowOwner()
    {
        if (startupDisposed) return;
        ShowInTaskbar = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Focus();
    }

    private void CloseOwner() { if (!startupDisposed) Close(); }

    private void LayeredAudioStateChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTrayMenu);

    private void RefreshTrayMenu()
    {
        if (startupTray is null || startupDisposed) return;
        var menu = new NativeMenu();
        var show = new NativeMenuItem { Header = text["Studio"] };
        show.Click += (_, _) => ShowOwner();
        var stop = new NativeMenuItem { Header = text["Stop"], IsEnabled = (Session.IsRunning || startupPending || effects?.LayeredAudio.IsActive == true) && Session.PanicEnabled };
        stop.Click += (_, _) =>
        {
            CancelPendingStartup();
            if (!Session.Stop(finishUntilStopped: true)) SetStatus("DisabledByPanic");
        };
        var close = new NativeMenuItem { Header = text["Close"] };
        close.Click += (_, _) => CloseOwner();
        menu.Items.Add(show); menu.Items.Add(stop); menu.Items.Add(close); startupTray.Menu = menu;
    }

    private void ChangeStartupPreference(bool enabled)
    {
        var previous = Settings.StartWithSystem;
        var result = RegisterStartup(enabled);
        if (!result.Succeeded)
        {
            Settings.StartWithSystem = previous;
            if (SettingsWindow is { } window)
                foreach (var toggle in window.GetVisualDescendants().OfType<ToggleButton>().Where(toggle => toggle.Name == "StartWithSystem"))
                    toggle.IsChecked = previous;
            SetStatus("StartupFailed"); return;
        }
        Settings.StartWithSystem = enabled; SaveSettings();
    }

    private StartupRegistrationResult RegisterStartup(bool enabled)
    {
        startupRegistration ??= new();
        if (!enabled) return startupRegistration.SetEnabled(false);
        try
        {
            var process = startupProcess();
            return startupRegistration.SetEnabled(true, process.Executable, process.ApplicationDll);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or IOException or ArgumentException)
        { return new(false, error.Message); }
    }

    private static (string Executable, string? ApplicationDll) CurrentStartupProcess()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
        var hosted = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
        return (executable, hosted ? typeof(MainWindow).Assembly.Location : null);
    }

    private void ResetSettings()
    {
        if ((Session.IsRunning || effects?.LayeredAudio.IsActive == true) && !Session.Stop()) { SetStatus("DisabledByPanic"); return; }
        CancelPendingStartup();
        if (!RegisterStartup(false).Succeeded) { SetStatus("ResetFailed"); return; }
        CloseSheet();
        Settings.ResetToDefaults();
        haptics.InputEnabled = Settings.Devices.ToyInputEnabled;
        haptics.GlobalIntensity = Settings.Devices.GlobalIntensity;
        haptics.ConfigureRouting(Settings.Devices.Routing);
        Session.SetPanicEnabled(true);
        text.SetLanguage(Settings.Language);
        SaveSettings(); BuildShell(); effects?.RefreshSettings(); RefreshTrayMenu();
    }

    private void DisposeStartup()
    {
        startupDisposed = true; CancelPendingStartup();
        Session.StopRequested -= CancelPendingStartup;
        Session.Changed -= CancelStartupIfRunning;
        startupTray?.Dispose(); startupTray = null;
    }
}
