using System.Diagnostics;
using System.Globalization;

namespace TaskManager.Helpers;

/// <summary>
/// A run that dies in native code (an access violation in WebView2, a hook DLL, the runtime) never reaches the managed crash log.
/// Each run therefore leaves a marker file while it lives and removes it on a clean exit; the next start finds a marker whose
/// process is gone and says so in the log. How the run ended is named only as far as there is evidence: a publish that stopped it leaves
/// <c>stopped-by-publish-pid</c>, a Windows shutdown or sign-out leaves <c>ended-by-windows-pid</c>; with neither, the run was killed or crashed and the line says
/// that it cannot tell which (Windows' crash report would name a crash).
/// </summary>
public static class RunMarker
{
    private const string Prefix = "run-", Suffix = ".marker";
    /// <summary>Written by the dist-small publish for each process it stops (the file name carries the id), and by this app when Windows ends the session.</summary>
    public const string PublishStop = "stopped-by-publish-", WindowsEnd = "ended-by-windows-";

    /// <summary>
    /// Writes this run's marker and returns one line per earlier run that did not exit cleanly (empty when none).
    /// The marker records when the process started (<paramref name="processStart"/>, by default this process's own start time), not when
    /// this call happens, so the later comparison is between two start times however long after launch the marker was written.
    /// </summary>
    public static IReadOnlyList<string> Begin(string folder, int pid, Func<int, DateTime, bool>? isAlive = null, Func<DateTime>? processStart = null)
    {
        isAlive ??= IsRunning;
        processStart ??= OwnStart;
        List<string> lost = [];
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.EnumerateFiles(folder, Prefix + "*" + Suffix))
        {
            if (!int.TryParse(Path.GetFileName(file)[Prefix.Length..^Suffix.Length], out int other) || other == pid) continue;
            DateTime started = DateTime.MinValue;
            try { DateTime.TryParse(File.ReadAllText(file).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started); } catch (IOException) { continue; }
            if (isAlive(other, started)) continue;
            lost.Add($"The run with process id {other}, started {started:yyyy-MM-dd HH:mm:ss}, " + HowItEnded(folder, other));
            DeleteQuietly(Path.Combine(folder, PublishStop + other));
            DeleteQuietly(Path.Combine(folder, WindowsEnd + other));
            try { File.Delete(file); } catch (IOException) { }
        }
        DeleteStaleStopFiles(folder);
        File.WriteAllText(Path.Combine(folder, Prefix + pid + Suffix), processStart().ToString("O", CultureInfo.InvariantCulture));
        return lost;
    }

    private static string HowItEnded(string folder, int pid)
    {
        if (File.Exists(Path.Combine(folder, PublishStop + pid))) return "did not exit cleanly: stopped by a publish (the dist-small publish stops what runs from dist).";
        if (File.Exists(Path.Combine(folder, WindowsEnd + pid))) return "ended by a Windows shutdown or sign-out.";
        return "did not exit cleanly: killed or crashed; Windows' crash report would name a crash (a crash in native code leaves no managed log). "
            + $"Windows keeps its dumps in {CrashDumps}, and the Application event log names the faulting module.";
    }

    /// <summary>Windows is ending the session (shutdown, restart, sign-out): this run's end is named, so the next start does not call it a kill or a crash.</summary>
    public static void WindowsIsEnding(string folder, int pid)
    {
        try { File.WriteAllText(Path.Combine(folder, WindowsEnd + pid), DateTime.Now.ToString("O", CultureInfo.InvariantCulture)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The stop files of a process that never left a run marker (the board connectors a publish stops are not runs of the app) would pile up: those of the last 10 minutes stay (a publish may be at work), older ones go.</summary>
    private static void DeleteStaleStopFiles(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*-*").Where(f => Path.GetFileName(f).StartsWith(PublishStop, StringComparison.Ordinal) || Path.GetFileName(f).StartsWith(WindowsEnd, StringComparison.Ordinal)))
        {
            string name = Path.GetFileName(file);
            string id = name[(name.StartsWith(PublishStop, StringComparison.Ordinal) ? PublishStop.Length : WindowsEnd.Length)..];
            if (File.Exists(Path.Combine(folder, Prefix + id + Suffix))) continue;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromMinutes(10)) DeleteQuietly(file);
        }
    }

    private static void DeleteQuietly(string file)
    {
        try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static DateTime OwnStart()
    {
        using Process p = Process.GetCurrentProcess();
        return p.StartTime;
    }

    /// <summary>A clean exit: this run's marker goes.</summary>
    public static void End(string folder, int pid)
    {
        try { File.Delete(Path.Combine(folder, Prefix + pid + Suffix)); } catch (IOException) { }
        DeleteQuietly(Path.Combine(folder, WindowsEnd + pid));   // a session end that something else cancelled: this run ended cleanly after all
    }

    public static string CrashDumps => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    /// <summary>The process exists and began about when the marker says (a reused process id is another process).</summary>
    private static bool IsRunning(int pid, DateTime started)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited && Math.Abs((p.StartTime - started).TotalSeconds) < 30;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}
