using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace CCP.Classic.Tests;

/// <summary>Runs controls and their input on one long-lived headless UI thread.</summary>
public static class UiDispatcher
{
    private static readonly object Gate = new();
    private static readonly CancellationTokenSource Lifetime = new();
    private static Task<Dispatcher>? startup;

    static UiDispatcher() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Lifetime.Cancel();

    public static async Task Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Task<Dispatcher> pending;
        lock (Gate)
            pending = startup ??= Start();

        var dispatcher = await pending.ConfigureAwait(false);
        if (dispatcher.CheckAccess())
            action();
        else
            await dispatcher.InvokeAsync(action);
    }

    private static Task<Dispatcher> Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<global::CCP.Classic.App.App>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = false,
                    })
                    .SetupWithoutStarting();
                ready.SetResult(Dispatcher.UIThread);
                Dispatcher.UIThread.MainLoop(Lifetime.Token);
            }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
            {
                ready.TrySetCanceled(Lifetime.Token);
            }
            catch (Exception error)
            {
                ready.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "CCP Classic headless UI",
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }
}
