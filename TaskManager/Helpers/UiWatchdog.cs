using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskManager.Helpers;

/// <summary>
/// Notices a window that has stopped answering. Every 5 seconds it posts a ping to the UI thread; when the answer has not come 30 seconds after the ping was posted
/// it writes a dump of the process (every thread's stack is in it) and one freeze record, once per freeze; when the window answers again it re-arms. One ping waits for its answer at a time,
/// so a stuck window never collects a queue of them. The clock, the ping and the dump are handed in: a test moves a FakeTimeProvider and answers (or not) by hand.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(5), FreezeAfter = TimeSpan.FromSeconds(30);

    private readonly TimeProvider clock;
    private readonly Action<Action> ping;
    private readonly Action<string> freeze;
    private readonly Func<string?> dump;
    private readonly Action<string>? recovered;
    private readonly object gate = new();
    private ITimer? timer;
    private DateTimeOffset? pendingSince;
    private bool reported;
    private long generation;   // which ping an answer belongs to

    /// <param name="ping">Posts the given acknowledgement to the UI thread (it must return at once: the real one queues the blocking call on the pool).</param>
    /// <param name="freeze">Gets the freeze record (an Error line in the log).</param>
    /// <param name="dump">Writes the process dump and returns where it went; a throw is said in the record.</param>
    /// <param name="recovered">Told when a window that was reported answers again.</param>
    public UiWatchdog(TimeProvider clock, Action<Action> ping, Action<string> freeze, Func<string?> dump, Action<string>? recovered)
    {
        this.clock = clock;
        this.ping = ping;
        this.freeze = freeze;
        this.dump = dump;
        this.recovered = recovered;
    }

    /// <summary>The first ping goes out now, the next ones every 5 seconds.</summary>
    public void Start()
    {
        Tick();
        lock (gate) timer ??= clock.CreateTimer(_ => Tick(), null, PingEvery, PingEvery);
    }

    private void Tick()
    {
        Action? send = null;
        string? record = null;
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (pendingSince is null)
            {
                pendingSince = now;
                long mine = ++generation;
                send = () => Answered(mine);
            }
            else if (!reported && now - pendingSince.Value >= FreezeAfter)
            {
                reported = true;
                record = "The window has not answered for " + (int)FreezeAfter.TotalSeconds + " s";
            }
        }
        if (send is not null) { try { ping(send); } catch (Exception) { } }   // a window that is not there yet (or already gone) answers nothing; the next tick tries again
        if (record is null) return;
        string where;
        try { where = dump() is { } path ? "; a dump of the process (every thread's stack) is in " + path : "; no dump was written"; }
        catch (Exception e) { where = "; the dump could not be written: " + e.Message; }
        freeze(record + where);
    }

    private void Answered(long ping)
    {
        bool wasFrozen;
        lock (gate)
        {
            if (ping != generation || pendingSince is null) return;
            wasFrozen = reported;
            pendingSince = null;
            reported = false;
        }
        if (wasFrozen) recovered?.Invoke("The window answers again");
    }

    public void Dispose()
    {
        lock (gate) { timer?.Dispose(); timer = null; }
    }

    /// <summary>The real ping: the window's Invoke blocks until the UI thread runs it, so it is called from the pool; a stuck window holds that one pool thread.</summary>
    public static Action<Action> PingThrough(Action<Action> invokeOnUiThread) =>
        ack => ThreadPool.UnsafeQueueUserWorkItem(_ => { try { invokeOnUiThread(ack); } catch (Exception) { } }, null);

    /// <summary>A minidump of this process (all threads, their stacks and the memory the stacks point to) in <paramref name="folder"/>, named <c>hang-pid-time.dmp</c>. No package: dbghelp ships with Windows.</summary>
    public static string WriteDump(string folder, TimeProvider clock)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"hang-{Environment.ProcessId}-{clock.GetLocalNow():yyyyMMdd-HHmmss}.dmp");
        using FileStream file = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();
        const int WithDataSegs = 0x1, WithHandleData = 0x4, WithUnloadedModules = 0x20, WithIndirectlyReferencedMemory = 0x40, WithProcessThreadData = 0x100, WithThreadInfo = 0x1000;
        if (!MiniDumpWriteDump(self.Handle, (uint)self.Id, file.SafeFileHandle, WithDataSegs | WithHandleData | WithUnloadedModules | WithIndirectlyReferencedMemory | WithProcessThreadData | WithThreadInfo, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            throw new IOException("MiniDumpWriteDump failed, Win32 error " + Marshal.GetLastWin32Error());
        return path;
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, int dumpType, IntPtr exception, IntPtr userStream, IntPtr callback);
}
