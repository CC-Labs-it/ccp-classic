using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CCP.Classic.App.Media;

internal sealed class AnalysisProcessBudget : IDisposable
{
    internal const long MaximumBytes = 1024L * 1024 * 1024;
    private readonly ConcurrentDictionary<int, DateTime> children = new();
    private SafeFileHandle? job;
    private Process? process;
    private nint affinity;

    internal static bool HasHeadroom(ulong total, ulong available) => total > 0 && available <= total && available > total / 4;
    internal static nuint LimitedAffinity(nuint allowed)
    {
        nuint selected = 0; var count = 0;
        for (var bit = 0; bit < IntPtr.Size * 8 && count < 16; bit++)
        { var flag = (nuint)1 << bit; if ((allowed & flag) != 0) { selected |= flag; count++; } }
        return selected;
    }
    internal static bool LinuxHeadroom(IEnumerable<string> lines)
    {
        ulong total = 0, available = 0;
        foreach (var line in lines)
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) continue;
            if (parts[0] == "MemTotal:") total = value;
            else if (parts[0] == "MemAvailable:") available = value;
        }
        return HasHeadroom(total, available);
    }
    internal static void RequireHeadroom()
    {
        bool ready;
        if (OperatingSystem.IsWindows())
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            ready = GlobalMemoryStatusEx(ref memory) && HasHeadroom(memory.TotalPhysical, memory.AvailablePhysical);
        }
        else if (OperatingSystem.IsLinux()) ready = LinuxHeadroom(File.ReadLines("/proc/meminfo").Take(128));
        else ready = false;
        if (!ready) throw new IOException("Audio analysis resource budget is unavailable.");
    }
    internal void Attach(Process child)
    {
        process = child;
        RequireHeadroom();
        using var current = Process.GetCurrentProcess();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) affinity = (nint)LimitedAffinity((nuint)current.ProcessorAffinity);
        else throw new PlatformNotSupportedException();
        if (affinity == 0) throw new IOException("Audio analysis CPU budget is unavailable.");
        if (OperatingSystem.IsWindows())
        {
            job = CreateJobObject(0, null);
            var limits = new ExtendedLimits { Basic = new() { Flags = 0x2000 | 0x200 | 0x100 | 0x10, Affinity = (nuint)affinity },
                JobMemory = (nuint)MaximumBytes, ProcessMemory = (nuint)MaximumBytes };
            if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())
                || !AssignProcessToJobObject(job, child.Handle)) throw new IOException("Audio analysis process limits were refused.");
        }
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) child.ProcessorAffinity = affinity;
        Check();
    }
    internal void Check()
    {
        RequireHeadroom();
        if (process is null || process.HasExited) return;
        long bytes = 0;
        if (OperatingSystem.IsLinux())
        {
            var pending = new Queue<int>(); pending.Enqueue(process.Id); var seen = new HashSet<int>();
            while (pending.TryDequeue(out var id))
            {
                if (!seen.Add(id)) continue;
                if (seen.Count > 32) throw new IOException("Audio analysis process budget was exceeded.");
                try
                {
                    using var member = Process.GetProcessById(id);
                    if (member.HasExited) continue;
                    if (id != process.Id) children.TryAdd(id, member.StartTime);
                    // Existing tasks and all subsequently inherited tasks share the same ceiling.
                    foreach (var task in Directory.EnumerateDirectories($"/proc/{id}/task"))
                    {
                        var tid = int.Parse(Path.GetFileName(task), CultureInfo.InvariantCulture);
                        var mask = (nuint)affinity;
                        if (sched_setaffinity(tid, (nuint)IntPtr.Size, ref mask) != 0 && Directory.Exists(task))
                            throw new IOException("Audio analysis CPU affinity was refused.");
                    }
                    bytes += WorkingSet(member);
                    foreach (var value in File.ReadAllText($"/proc/{id}/task/{id}/children").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        pending.Enqueue(int.Parse(value, CultureInfo.InvariantCulture));
                }
                catch (Exception failure) when (failure is ArgumentException or DirectoryNotFoundException or FileNotFoundException) { }
                catch (System.ComponentModel.Win32Exception) when (!Directory.Exists($"/proc/{id}")) { }
            }
        }
        else bytes = WorkingSet(process);
        if (bytes > MaximumBytes) throw new IOException("Audio analysis memory budget was exceeded.");
    }
    private static long WorkingSet(Process member)
    {
        try
        {
            member.Refresh();
            return member.HasExited ? 0 : member.WorkingSet64;
        }
        catch (InvalidOperationException) when (member.HasExited) { return 0; }
        catch (System.ComponentModel.Win32Exception) when (member.HasExited) { return 0; }
    }
    internal void Stop()
    {
        if (process is not null) Kill(process);
        if (!OperatingSystem.IsLinux()) return;
        foreach (var (id, started) in children)
        {
            try { using var child = Process.GetProcessById(id); if (child.StartTime == started) Kill(child); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { }
        }
    }
    private static void Kill(Process child)
    {
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        catch (Exception failure) when (failure is not OutOfMemoryException) { }
    }
    public void Dispose() { Stop(); job?.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint Flags; public nuint MinWorkingSet, MaxWorkingSet; public uint Active; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateJobObject(nint security, string? name);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    [DllImport("libc", SetLastError = true)] private static extern int sched_setaffinity(int pid, nuint size, ref nuint mask);
}
