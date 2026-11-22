using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>A run that dies in native code writes no managed crash log; the next start must say that the previous run did not exit cleanly.</summary>
public sealed class LeavingATraceOfACrashTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rtm-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    [Fact(DisplayName = "A run that never ended cleanly is reported by the next start, with where its dump is")]
    public void An_unclean_run_is_reported()
    {
        Assert.Empty(RunMarker.Begin(folder, 4100, (_, _) => false));   // the first run: nothing before it

        IReadOnlyList<string> lost = RunMarker.Begin(folder, 4200, (_, _) => false);   // its process is gone and it never called End: it crashed

        string line = Assert.Single(lost);
        Assert.Contains("4100", line);
        Assert.Contains("did not exit cleanly: killed or crashed", line);   // nothing says which: the wording does not claim a crash
        Assert.Contains("Windows' crash report would name a crash", line);
        Assert.Contains("CrashDumps", line);
        Assert.Empty(RunMarker.Begin(folder, 4300, (pid, _) => pid == 4200));   // reported once: the old marker was cleared
    }

    [Fact(DisplayName = "A run that exited cleanly, or is still running, is not reported")]
    public void A_clean_or_live_run_is_not_reported()
    {
        RunMarker.Begin(folder, 4100, (_, _) => false);
        RunMarker.End(folder, 4100);   // a clean exit
        RunMarker.Begin(folder, 4150, (_, _) => false);   // a second window, still running when the third starts

        Assert.Empty(RunMarker.Begin(folder, 4200, (pid, _) => pid == 4150));
    }

    [Fact(DisplayName = "Without a stand-in for the process check, this very process counts as running")]
    public void The_real_process_check_sees_this_process()
    {
        RunMarker.Begin(folder, Environment.ProcessId);

        Assert.Empty(RunMarker.Begin(folder, 4200));
    }

    [Fact(DisplayName = "A marker written long after the process started still counts the process as running")]
    public void A_late_marker_still_counts_the_process_as_running()
    {
        DateTime start;
        using (System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess()) start = self.StartTime;

        RunMarker.Begin(folder, Environment.ProcessId);   // what Begin records is the start time, whenever Begin itself runs (never "now", which is later than the start)

        string marker = File.ReadAllText(Directory.EnumerateFiles(folder).Single());
        Assert.Equal(start, DateTime.Parse(marker, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));
        Assert.Empty(RunMarker.Begin(folder, 4200));   // the real check, 5 minutes or 5 hours after the start alike
    }

    [Fact(DisplayName = "A run the publish stopped is reported as stopped by a publish, not as a crash, once")]
    public void A_run_stopped_by_a_publish_is_named()
    {
        RunMarker.Begin(folder, 4100, (_, _) => false);
        File.WriteAllText(Path.Combine(folder, "stopped-by-publish-4100"), "");   // what the dist-small publish writes before it stops a process

        string line = Assert.Single(RunMarker.Begin(folder, 4200, (_, _) => false));

        Assert.Contains("4100", line);
        Assert.Contains("did not exit cleanly: stopped by a publish", line);
        Assert.DoesNotContain("crash", line, StringComparison.OrdinalIgnoreCase);   // there is no evidence of one
        Assert.False(File.Exists(Path.Combine(folder, "stopped-by-publish-4100")));
        Assert.Empty(RunMarker.Begin(folder, 4300, (pid, _) => pid == 4200));
    }

    [Fact(DisplayName = "A run Windows ended with a shutdown or a sign-out is reported as that, not as a crash")]
    public void A_run_ended_by_windows_is_named()
    {
        RunMarker.Begin(folder, 4100, (_, _) => false);
        RunMarker.WindowsIsEnding(folder, 4100);   // the session-ending event of this run

        string line = Assert.Single(RunMarker.Begin(folder, 4200, (_, _) => false));

        Assert.Contains("4100", line);
        Assert.Contains("ended by a Windows shutdown", line);
        Assert.DoesNotContain("crash", line, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(folder, "ended-by-windows-*"));
    }

    [Fact(DisplayName = "A publish's stop file of a process that never left a run marker (a board connector) is cleared once it is stale")]
    public void A_stale_stop_file_without_a_run_is_cleared()
    {
        string stale = Path.Combine(folder, "stopped-by-publish-5100"), fresh = Path.Combine(folder, "stopped-by-publish-5200");
        Directory.CreateDirectory(folder);
        File.WriteAllText(stale, "");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
        File.WriteAllText(fresh, "");   // a publish that is just stopping processes: its files are not touched yet

        Assert.Empty(RunMarker.Begin(folder, 4200, (_, _) => false));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }
}
