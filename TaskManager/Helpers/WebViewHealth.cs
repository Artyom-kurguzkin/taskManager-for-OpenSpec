using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace TaskManager.Helpers;

/// <summary>
/// Leaves a trace when the window's WebView2 fails (#wi178). Photino.NET keeps the CoreWebView2 inside its native layer and exposes neither it nor its ProcessFailed event, so the app watches the browser
/// process instead: once the window is created, the <c>msedgewebview2.exe</c> children of this process (the browser process; its renderer, GPU and utility processes are the browser's children, not ours)
/// are found and each one's exit is logged, at Error when it ends with a failure code, at Information when it ends with 0 (the window closing).
/// </summary>
public static class WebViewHealth
{
    private const string ProcessName = "msedgewebview2";
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(60);

    /// <summary>Starts the watch once <paramref name="window"/> is created. Without a window it does nothing.</summary>
    public static void Attach(PhotinoWindow window)
    {
        if (window is null) return;
        window.WindowCreated += (_, _) => _ = Task.Run(() => FindAndWatchAsync(Environment.ProcessId, TimeProvider.System));
    }

    /// <summary>
    /// Looks every half second for a minute (the browser process starts a little after the window) and watches each WebView2 child it has not seen. Returns when the minute is over;
    /// the watches run on until their processes end.
    /// </summary>
    internal static async Task FindAndWatchAsync(int ownPid, TimeProvider clock)
    {
        HashSet<int> seen = [];
        long start = clock.GetTimestamp();
        while (clock.GetElapsedTime(start) < GiveUp)
        {
            foreach (int pid in ChildrenOf(ownPid, ProcessName))
            {
                if (!seen.Add(pid)) continue;
                try { _ = WatchAsync(Process.GetProcessById(pid)); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException) { /* gone already */ }
            }
            await Task.Delay(Look, clock);
        }
    }

    /// <summary>Waits for the process to end and logs how it ended.</summary>
    internal static async Task WatchAsync(Process process)
    {
        int pid = process.Id;
        try
        {
            await process.WaitForExitAsync();
            int code = process.ExitCode;
            ILogger log = AppLog.Logger("WebView");
            if (code == 0) log.LogInformation("WebView2 browser process {Pid} ended with exit code 0", pid);
            else log.LogError("{Failure} (browser process {Pid}, exit code {Code})", Describe("BrowserProcessExited", "Unexpected", code), pid, code);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Logger("WebView").LogWarning("WebView2 browser process {Pid} could not be watched: {Message}", pid, e.Message);   // a process we may not query: nothing to say about it
        }
    }

    /// <summary>The line for a ProcessFailed record: kind, reason and exit code, as WebView2 names them.</summary>
    public static string Describe(string kind, string reason, int exitCode) => $"WebView2 ProcessFailed: kind {kind}, reason {reason}, exit code {exitCode}";

    /// <summary>The process ids of the direct children of <paramref name="parent"/> whose executable is <paramref name="name"/>.exe (a snapshot of the running processes).</summary>
    internal static IReadOnlyList<int> ChildrenOf(int parent, string name)
    {
        List<int> found = [];
        if (!OperatingSystem.IsWindows()) return found;
        IntPtr snapshot = CreateToolhelp32Snapshot(Snapshot.Process, 0);
        if (snapshot == new IntPtr(-1)) return found;
        try
        {
            Entry entry = new() { Size = (uint)Marshal.SizeOf<Entry>() };
            for (bool more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                if (entry.Parent == parent && string.Equals(Path.GetFileNameWithoutExtension(entry.Exe), name, StringComparison.OrdinalIgnoreCase)) found.Add((int)entry.Id);
        }
        finally { CloseHandle(snapshot); }
        return found;
    }

    private static class Snapshot { public const uint Process = 0x2; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Id;
        public UIntPtr DefaultHeap;
        public uint Modules, Threads, Parent;
        public int PriorityClass;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
