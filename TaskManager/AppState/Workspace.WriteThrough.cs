using TaskManager.Helpers;
using OpenSpecStore;

namespace TaskManager.AppState;

public sealed partial class Workspace
{
    private readonly TimeProvider time;
    private readonly Dictionary<string, Burst> bursts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object burstGate = new();
    private bool burstsClosed;

    /// <summary>A repository's writes not told yet: when the first of them came, the timer that tells, and the trace of the last write (the scan the news starts runs under it).</summary>
    private sealed class Burst(DateTimeOffset first, System.Diagnostics.ActivityContext trace)
    {
        public readonly DateTimeOffset First = first;
        public readonly System.Diagnostics.ActivityContext Trace = trace;
        public ITimer? Timer;
    }

    /// <summary>How long writes must stay quiet before the pages are told, and the longest a steady stream of writes can hold the news back.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300), MostHeld = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The app's own writes (a status change, a tick, an agent's call) tell the pages through the same quiet time as the file watcher's reports, one repository at a time: a burst of writes (a merge,
    /// five subtasks ticked, an agent's batch) is one <see cref="Changed"/> and one scan after the burst, not one per write. The caches are already right by then (a write updates its own item,
    /// see ItemsCache), so what is read meanwhile is what was written. The timer runs on the clock the workspace was given. The news and its scan carry the trace of the write that asked for
    /// them (a timer callback has no trace of its own, and the command's activity has stopped by then): the log shows one trace from the command to the scan.
    /// </summary>
    public void NotifyWritten() => NotifyWritten(OpenSpecStoreApi.RepoRoot ?? "");

    public void NotifyWritten(string root)
    {
        string key = root.Length == 0 ? "" : Path.GetFullPath(root);
        System.Diagnostics.ActivityContext trace = System.Diagnostics.Activity.Current?.Context ?? default;
        lock (burstGate)
        {
            if (burstsClosed) return;
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset first = now;
            if (bursts.Remove(key, out Burst? running)) { running.Timer?.Dispose(); first = running.First; }
            TimeSpan wait = TimeSpan.FromTicks(Math.Clamp((first + MostHeld - now).Ticks, 0, Quiet.Ticks));
            if (wait > TimeSpan.Zero)
            {
                Burst burst = new(first, trace);
                bursts[key] = burst;
                burst.Timer = time.CreateTimer(_ => BurstEnded(key, burst), null, wait, Timeout.InfiniteTimeSpan);
                return;
            }
        }
        Tell(trace);   // the stream has held the news the longest it may: told now, and the next write starts a new burst
    }

    private void BurstEnded(string key, Burst burst)
    {
        lock (burstGate)
        {
            // a timer that was replaced can still fire (it was already queued): only the burst's own timer ends it
            if (burstsClosed || !bursts.TryGetValue(key, out Burst? current) || !ReferenceEquals(current, burst)) return;
            bursts.Remove(key);
            burst.Timer?.Dispose();
        }
        Tell(burst.Trace);
    }

    /// <summary>Tells the pages (and starts the duplicate scan) under the trace of the write that asked for it.</summary>
    private void Tell(System.Diagnostics.ActivityContext trace)
    {
        using System.Diagnostics.Activity? told = trace == default ? null : AppLog.Start("written", level: Microsoft.Extensions.Logging.LogLevel.Debug, parent: trace);
        // a timer callback that throws ends the process; files still moving are the usual cause, and the next write tells again
        try { Notify(wait: false); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void DisposeBursts()
    {
        lock (burstGate)
        {
            burstsClosed = true;
            foreach (Burst burst in bursts.Values) burst.Timer?.Dispose();
            bursts.Clear();
        }
    }
}
