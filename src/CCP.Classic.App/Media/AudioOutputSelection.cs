using CCP.Classic.Core;
using LibVLCSharp.Shared;
using System.Runtime.InteropServices;

namespace CCP.Classic.App.Media;

public sealed record LocalAudioOutput(string Id, string Name);

public static class AudioOutputSelection
{
    private static string selected = "";
    public static void Configure(AudioOptions options)
    {
        var id = options.OutputDeviceId;
        Volatile.Write(ref selected, id is not null && id.Length <= 1024 && !id.Any(char.IsControl) ? id : "");
    }
    // Called only on the native player worker. The output module stays DirectSound on Windows.
    internal static void Apply(MediaPlayer player, ref string? previous)
        => Apply(Volatile.Read(ref selected), ref previous, id => SetNativeDevice(OperatingSystem.IsWindows(), id,
            (module, device) => player.SetOutputDevice(deviceId: device, module: module)));
    internal static void Apply(string selectedId, ref string? previous, Action<string> set)
    {
        if (previous == selectedId) return;
        set(selectedId); previous = selectedId;
    }
    internal static void SetNativeDevice(bool windows, string id, Action<string?, string> set)
    {
        // VLC3 stores module-specific choices for a future output. DirectSound's configuration prefix is directx.
        if (windows) set("directx", id);
        // An empty string restores the default. VLC treats a null device as no request at all.
        set(null, id);
    }
    public static Task<IReadOnlyList<LocalAudioOutput>> EnumerateAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<LocalAudioOutput>>(() =>
    {
        token.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows()) return EnumerateDirectSound(ReadDirectSound, token);
        NativeAudioDevice.EnsureInitialized();
        using var vlc = new LibVLC(NativeAudioDevice.Options(OperatingSystem.IsWindows()));
        var result = vlc.AudioOutputDevices("pulse").Where(item => !string.IsNullOrWhiteSpace(item.DeviceIdentifier))
            .Take(128).Select(item => new LocalAudioOutput(item.DeviceIdentifier, item.Description))
            .DistinctBy(item => item.Id, StringComparer.Ordinal).ToArray();
        token.ThrowIfCancellationRequested(); return result;
    }, token);

    // VLC3 directsound.c uses DirectSoundEnumerate and StringFromGUID2, then IIDFromString on selection.
    // Its legacy instance-level AudioOutputDevices API does not enumerate this module reliably.
    internal static IReadOnlyList<LocalAudioOutput> EnumerateDirectSound(Func<Func<Guid?, string?, bool>, int> enumerate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var result = new List<LocalAudioOutput>(); var seen = new HashSet<Guid>();
        var error = enumerate((guid, description) =>
        {
            if (token.IsCancellationRequested || result.Count >= 128) return false;
            // The null primary-driver callback duplicates the separately localized System Default option.
            if (guid is null || guid == Guid.Empty || !seen.Add(guid.Value)) return true;
            var id = guid.Value.ToString("B").ToUpperInvariant();
            var name = new string((description ?? "").Where(character => !char.IsControl(character)).Take(256).ToArray());
            result.Add(new(id, string.IsNullOrWhiteSpace(name) ? id : name)); return result.Count < 128;
        });
        token.ThrowIfCancellationRequested();
        if (error < 0) throw new IOException("Audio output enumeration is unavailable.", Marshal.GetExceptionForHR(error));
        return result.AsReadOnly();
    }
    private static int ReadDirectSound(Func<Guid?, string?, bool> receive)
    {
        DirectSoundCallback callback = (guid, description, _, _) => receive(guid == IntPtr.Zero ? null : Marshal.PtrToStructure<Guid>(guid),
            description == IntPtr.Zero ? null : Marshal.PtrToStringUni(description));
        var result = DirectSoundEnumerate(callback, IntPtr.Zero); GC.KeepAlive(callback); return result;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool DirectSoundCallback(IntPtr guid, IntPtr description, IntPtr module, IntPtr context);
    [DllImport("dsound.dll", EntryPoint = "DirectSoundEnumerateW", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DirectSoundEnumerate(DirectSoundCallback callback, IntPtr context);
}
