using System.Diagnostics;
using System.Globalization;

namespace TaskManager.Hosting;

/// <summary>
/// A run that dies in native code (an access violation in WebView2, a hook DLL, the runtime) never reaches the managed crash log.
/// Each run therefore leaves a marker file while it lives and removes it on a clean exit; the next start finds a marker whose
/// process is gone and says so in the log, with where Windows keeps the crash dump.
/// </summary>
public static class RunMarker
{
    private const string Prefix = "run-", Suffix = ".marker";

    /// <summary>Writes this run's marker and returns one line per earlier run that did not exit cleanly (empty when none).</summary>
    public static IReadOnlyList<string> Begin(string folder, int pid, Func<int, DateTime, bool>? isAlive = null)
    {
        isAlive ??= IsRunning;
        List<string> lost = [];
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.EnumerateFiles(folder, Prefix + "*" + Suffix))
        {
            if (!int.TryParse(Path.GetFileName(file)[Prefix.Length..^Suffix.Length], out int other) || other == pid) continue;
            DateTime started = DateTime.MinValue;
            try { DateTime.TryParse(File.ReadAllText(file).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started); } catch (IOException) { continue; }
            if (isAlive(other, started)) continue;
            lost.Add($"The run with process id {other}, started {started:yyyy-MM-dd HH:mm:ss}, did not exit cleanly: a crash in native code (it leaves no managed log) or a kill. "
                + $"Windows keeps its dump in {CrashDumps}, and the Application event log names the faulting module.");
            try { File.Delete(file); } catch (IOException) { }
        }
        File.WriteAllText(Path.Combine(folder, Prefix + pid + Suffix), DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        return lost;
    }

    /// <summary>A clean exit: this run's marker goes.</summary>
    public static void End(string folder, int pid)
    {
        try { File.Delete(Path.Combine(folder, Prefix + pid + Suffix)); } catch (IOException) { }
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
