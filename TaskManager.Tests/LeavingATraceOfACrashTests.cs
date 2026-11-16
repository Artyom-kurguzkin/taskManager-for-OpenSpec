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
}
