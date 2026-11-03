using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace CCP.Classic.App.Input;

/// <summary>Passive desktop key listener. Callbacks are posted to the UI thread.</summary>
public sealed class GlobalPanicMonitor : IDisposable
{
    private readonly Func<string> panicKey;
    private readonly Func<bool> keywordEnabled;
    private readonly KeywordInput keywords;
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
    private int textAvailable;
    public event Action<string>? TextReceived;
    public bool IsTextAvailable => Volatile.Read(ref disposed) == 0 && Volatile.Read(ref textAvailable) != 0;
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

    public GlobalPanicMonitor(Func<string> panicKey, Action<string, bool> keyChanged, Func<bool>? keywordEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(panicKey); ArgumentNullException.ThrowIfNull(keyChanged);
        this.panicKey = panicKey; this.keyChanged = keyChanged;
        this.keywordEnabled = keywordEnabled ?? (() => false); keywords = new(DeliverText);
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

    private void DeliverText(string text)
    {
        var generation = keywords.Generation;
        Dispatcher.UIThread.Post(() =>
        {
            try { if (IsTextAvailable && keywordEnabled() && generation == keywords.Generation) TextReceived?.Invoke(text); }
            catch (Exception) { Volatile.Write(ref textAvailable, 0); }
        }, DispatcherPriority.Input);
    }

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
            var seed = new byte[32];
            for (var key = 1; key < 256; key++) if ((GetAsyncKeyState(key) & 0x8000) != 0) seed[key >> 3] |= (byte)(1 << (key & 7));
            keywords.Seed(seed); Volatile.Write(ref textAvailable, 1);
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
            Volatile.Write(ref textAvailable, 0);
            Unavailable(stop.IsCancellationRequested ? "stopped" : Volatile.Read(ref status).Reason ?? "listener_stopped");
        }
    }

    private void ResolveWindows(bool insideHook = false)
    {
        try { if (IsTextAvailable) keywords.SetEnabled(keywordEnabled()); }
        catch (Exception) { Volatile.Write(ref textAvailable, 0); }
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
                    ReadWindowsText(key, false, data);
                }
                else if (kind is 0x100 or 0x104)
                {
                    windowsPressed.Add(key);
                    if (key == boundWindowsCode) transitions.Down(key, windowsBinding!);
                    ReadWindowsText(key, true, data);
                }
            }
        }
        catch (Exception)
        { Unavailable("callback_failed"); }
        return CallNextHookEx(0, code, message, data);
    }

    private void ReadWindowsText(int key, bool down, nint data)
    {
        if (!IsTextAvailable) return;
        try
        {
            var optedIn = keywordEnabled();
            var foreground = optedIn ? GetForegroundWindow() : 0;
            if (optedIn) keywords.SetContext(foreground.ToInt64());
            var flags = Marshal.ReadInt32(data, 8);
            var shortcut = optedIn && ((flags & 0x32) != 0 || KeywordInput.WindowsShortcut(key,
                code => windowsPressed.Contains(code) || (GetAsyncKeyState(code) & 0x8000) != 0));
            keywords.Key(key, down, optedIn, () => TranslateWindows(key, Marshal.ReadInt32(data, 4), foreground), shortcut);
        }
        catch (Exception) { Volatile.Write(ref textAvailable, 0); }
    }

    private string? TranslateWindows(int key, int scan, nint foreground)
    {
        if (foreground == 0) return null;
        var state = new byte[256];
        state[key] = 0x80;
        state[0x10] = (byte)(windowsPressed.Contains(0x10) || windowsPressed.Contains(0xa0) || windowsPressed.Contains(0xa1)
            || (GetAsyncKeyState(0x10) & 0x8000) != 0 ? 0x80 : 0);
        state[0x14] = (byte)(GetKeyState(0x14) & 1);
        var thread = GetWindowThreadProcessId(foreground, out _);
        if (thread == 0) return null;
        var characters = new char[8];
        // Flag4 preserves the foreground application's dead-key/IME keyboard buffer.
        var count = ToUnicodeEx((uint)key, (uint)scan, state, characters, characters.Length, 4, GetKeyboardLayout(thread));
        return count is > 0 and <= 8 ? new string(characters, 0, count) : null;
    }

    private void X11Loop()
    {
        nint display = 0, events = 0;
        try
        {
            display = XOpenDisplay(x11Display);
            if (display == 0) { Availability(panicKey(), false, "display_unavailable"); return; }
            Volatile.Write(ref textAvailable, 1);
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
                ReadX11Text(display, keys);
                if (stop.Token.WaitHandle.WaitOne(10)) break;
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Availability(panicKey(), false, "native_unavailable"); }
        finally
        {
            if (events != 0) Marshal.FreeHGlobal(events);
            if (display != 0) XCloseDisplay(display);
            Volatile.Write(ref textAvailable, 0);
            Unavailable(stop.IsCancellationRequested ? "stopped" : Volatile.Read(ref status).Reason ?? "listener_stopped");
        }
    }

    private void ReadX11Text(nint display, byte[] keys)
    {
        if (!IsTextAvailable) return;
        try
        {
            var optedIn = keywordEnabled();
            if (!optedIn) { keywords.ReadMap(keys, false, _ => null, false); return; }
            if (XkbGetState(display, 0x100, out var state) != 0) { Volatile.Write(ref textAvailable, 0); return; }
            XGetInputFocus(display, out var focus, out _); keywords.SetContext(focus.ToInt64());
            keywords.ReadMap(keys, true, code => XkbLookupKeySym(display, (byte)code,
                (uint)(state.LookupMods | state.Group << 13), out _, out var symbol) != 0 ? KeywordInput.X11Text(symbol) : null,
                (state.Mods & (4 | 8 | 32 | 64 | 128)) != 0);
        }
        catch (Exception) { Volatile.Write(ref textAvailable, 0); }
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
    [StructLayout(LayoutKind.Sequential)]
    private struct XkbState
    {
        public byte Group, LockedGroup; public ushort BaseGroup, LatchedGroup;
        public byte Mods, BaseMods, LatchedMods, LockedMods, CompatState, GrabMods, CompatGrabMods, LookupMods, CompatLookupMods;
        public ushort PointerButtons;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state,
        [Out] char[] text, int count, uint flags, nint layout);
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
    [DllImport("libX11.so.6")] private static extern int XkbGetState(nint display, uint device, out XkbState state);
    [DllImport("libX11.so.6")] private static extern int XkbLookupKeySym(nint display, byte key, uint state, out uint modifiers, out nuint symbol);
    [DllImport("libX11.so.6")] private static extern int XGetInputFocus(nint display, out nint focus, out int revert);
}
