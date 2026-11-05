using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace CCP.Classic.App.Input;

/// <summary>Passive desktop key listener. Callbacks are posted to the UI thread.</summary>
public sealed class GlobalPanicMonitor : IDisposable
{
    private readonly Func<string> panicKey;
    private readonly Action<string, bool> keyChanged;
    private readonly PanicKeyTransitions transitions;
    private readonly CancellationTokenSource stop = new();
    private readonly Thread? worker;
    private readonly HookProc hookProc;
    private readonly string? x11Display;
    private readonly HashSet<int> windowsPressed = [];
    private string? windowsBinding;
    private int boundWindowsCode;
    private bool resyncWindowsBinding;
    private sealed record Status(string? Key, bool Available, string? Reason);
    private Status status = new(null, false, "starting");
    private uint windowsThread;
    private int disposed;
    public bool IsAvailable
    {
        get
        {
            var current = Volatile.Read(ref status);
            return !stop.IsCancellationRequested && current.Available
                && string.Equals(current.Key, panicKey(), StringComparison.OrdinalIgnoreCase);
        }
    }
    public string? UnavailableReason
    {
        get
        {
            var current = Volatile.Read(ref status);
            return stop.IsCancellationRequested ? "stopped" : current.Key is { } name
                && !string.Equals(name, panicKey(), StringComparison.OrdinalIgnoreCase) ? "resolving_key" : current.Reason;
        }
    }

    public GlobalPanicMonitor(Func<string> panicKey, Action<string, bool> keyChanged)
    {
        ArgumentNullException.ThrowIfNull(panicKey); ArgumentNullException.ThrowIfNull(keyChanged);
        this.panicKey = panicKey; this.keyChanged = keyChanged;
        transitions = new(Deliver); hookProc = Hook;
        if (OperatingSystem.IsWindows()) worker = new(WindowsLoop);
        else if (OperatingSystem.IsLinux())
        {
            x11Display = Environment.GetEnvironmentVariable("DISPLAY");
            if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                status = new(null, false, "wayland");
            else if (!NativePanicKeys.IsLocalDisplay(x11Display)) status = new(null, false, "no_local_display");
            else worker = new(X11Loop);
        }
        else status = new(null, false, "unsupported_platform");
        if (worker is null) return;
        worker.IsBackground = true; worker.Name = "CCP Classic panic monitor"; worker.Start();
    }

    private void Deliver(string name, bool down) => Dispatcher.UIThread.Post(() =>
    {
        if (Volatile.Read(ref disposed) == 0) keyChanged(name, down);
    }, DispatcherPriority.Input);

    private void Availability(string name, bool value, string? reason) =>
        Volatile.Write(ref status, new(name, value, reason));
    private void Unavailable(string reason) =>
        Volatile.Write(ref status, Volatile.Read(ref status) with { Available = false, Reason = reason });

    private void WindowsLoop()
    {
        nint hook = 0; nuint timer = 0;
        try
        {
            Volatile.Write(ref windowsThread, GetCurrentThreadId());
            PeekMessage(out _, 0, 0, 0, 0);
            hook = SetWindowsHookEx(13, hookProc, GetModuleHandle(null), 0);
            if (hook == 0) { Availability(panicKey(), false, "hook_failed"); return; }
            timer = SetTimer(0, 0, 25, 0);
            if (timer == 0) { Availability(panicKey(), false, "timer_failed"); return; }
            ResolveWindows();
            while (!stop.IsCancellationRequested)
            {
                var result = GetMessage(out var message, 0, 0, 0);
                if (result <= 0) { if (result < 0) Availability(panicKey(), false, "message_failed"); break; }
                ResolveWindows(); TranslateMessage(ref message); DispatchMessage(ref message);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Availability(panicKey(), false, "native_unavailable"); }
        finally
        {
            if (timer != 0) KillTimer(0, timer);
            if (hook != 0) UnhookWindowsHookEx(hook);
            Unavailable(stop.IsCancellationRequested ? "stopped" : Volatile.Read(ref status).Reason ?? "listener_stopped");
        }
    }

    private void ResolveWindows(bool insideHook = false)
    {
        var name = panicKey();
        if (string.Equals(name, windowsBinding, StringComparison.OrdinalIgnoreCase)
            && (!resyncWindowsBinding || insideHook)) return;
        windowsBinding = name;
        var mapped = NativePanicKeys.TryMap(name, out boundWindowsCode, out _);
        // A key captured while already held must first come up before it can panic.
        // Async state is queried only outside the hook, after Windows has updated it.
        var down = mapped && (insideHook ? windowsPressed.Contains(boundWindowsCode)
            : (GetAsyncKeyState(boundWindowsCode) & 0x8000) != 0);
        if (down)
        {
            windowsPressed.Add(boundWindowsCode);
            transitions.Seed(boundWindowsCode, name);
        }
        else if (mapped && !insideHook)
        {
            windowsPressed.Remove(boundWindowsCode);
            transitions.Up(boundWindowsCode, name);
        }
        resyncWindowsBinding = insideHook;
        Availability(name, mapped, mapped ? null : "unsupported_key");
    }

    private nint Hook(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && !stop.IsCancellationRequested)
            {
                ResolveWindows(insideHook: true);
                var kind = (int)message;
                var key = Marshal.ReadInt32(data);
                if (kind is 0x101 or 0x105)
                {
                    windowsPressed.Remove(key);
                    transitions.Up(key, key == boundWindowsCode ? windowsBinding : null);
                }
                else if (kind is 0x100 or 0x104)
                {
                    windowsPressed.Add(key);
                    if (key == boundWindowsCode) transitions.Down(key, windowsBinding!);
                }
            }
        }
        catch (Exception)
        { Unavailable("callback_failed"); }
        return CallNextHookEx(0, code, message, data);
    }

    private void X11Loop()
    {
        nint display = 0, events = 0;
        try
        {
            display = XOpenDisplay(x11Display);
            if (display == 0) { Availability(panicKey(), false, "display_unavailable"); return; }
            events = Marshal.AllocHGlobal(192);
            var keys = new byte[32];
            string? previousName = null;
            var previousCode = 0;
            while (!stop.IsCancellationRequested)
            {
                // Refresh Xlib's keyboard cache after a desktop layout change.
                for (var count = 0; count < 16 && XPending(display) > 0; count++)
                {
                    XNextEvent(display, events);
                    if (Marshal.ReadInt32(events) == 34) XRefreshKeyboardMapping(events);
                }
                var name = panicKey();
                var mapped = NativePanicKeys.TryMap(name, out _, out var symbol);
                var key = mapped ? XKeysymToKeycode(display, XStringToKeysym(symbol!)) : (byte)0;
                XQueryKeymap(display, keys);
                if (name != previousName || key != previousCode) transitions.Bind(keys, key, name);
                previousName = name; previousCode = key;
                Availability(name, key != 0, key == 0 ? "unsupported_key" : null);
                transitions.ReadMap(keys, key, name);
                if (stop.Token.WaitHandle.WaitOne(10)) break;
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Availability(panicKey(), false, "native_unavailable"); }
        finally
        {
            if (events != 0) Marshal.FreeHGlobal(events);
            if (display != 0) XCloseDisplay(display);
            Unavailable(stop.IsCancellationRequested ? "stopped" : Volatile.Read(ref status).Reason ?? "listener_stopped");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        if (OperatingSystem.IsWindows() && Volatile.Read(ref windowsThread) is var thread and not 0)
            PostThreadMessage(thread, 0x12, 0, 0);
        if (worker?.Join(500) != false) stop.Dispose();
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)] private static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport("libX11.so.6")] private static extern int XQueryKeymap(nint display, [Out] byte[] keys);
    [DllImport("libX11.so.6")] private static extern int XPending(nint display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(nint display, nint events);
    [DllImport("libX11.so.6")] private static extern int XRefreshKeyboardMapping(nint mapping);
}
