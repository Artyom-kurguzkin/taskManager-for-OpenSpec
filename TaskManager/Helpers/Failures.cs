namespace TaskManager.Helpers;

/// <summary>
/// The process-wide failure handlers, in one place so Program wires them with one call, right after the log starts and before anything else runs:
/// an unhandled exception on any thread, a task's exception nobody observed, and an exception the window's dispatcher caught.
/// Each writes to the log first (<c>record</c>); a message box is only a courtesy and never holds the handler: it is shown from another thread
/// (not from the window's own thread, which may be the faulting one and is the one that would have to pump it), and the handler waits for it at most <c>showWait</c>.
/// </summary>
public sealed class Failures : IDisposable
{
    private readonly Action<string> record;
    private readonly Action<string>? show;
    private readonly int uiThread;
    private readonly TimeSpan showWait;
    private bool installed;

    /// <param name="record">Writes the text to the run's log (and the crash file); runs on the faulting thread, first.</param>
    /// <param name="show">Shows the text to the user (a message box); runs on its own thread, and is never waited for longer than <paramref name="showWait"/>. Null: no message box.</param>
    /// <param name="uiThread">The managed id of the window's thread: no message box is started from it.</param>
    public Failures(Action<string> record, Action<string>? show, int uiThread, TimeSpan showWait)
    {
        this.record = record;
        this.show = show;
        this.uiThread = uiThread;
        this.showWait = showWait;
    }

    /// <summary>Hooks the handlers on the process; dispose to unhook. The caller is the window's thread (it runs Main).</summary>
    public static Failures Install(Action<string> record, Action<string>? show)
    {
        Failures failures = new(record, show, Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(1)) { installed = true };
        AppDomain.CurrentDomain.UnhandledException += failures.OnUnhandled;
        TaskScheduler.UnobservedTaskException += failures.OnUnobserved;
        return failures;
    }

    /// <summary>Photino's dispatcher (the window's Blazor thread) swallows an exception that reaches it and raises its own event; this is what listens. Returns whether the event was found.</summary>
    public bool WatchDispatcher(object? dispatcher)
    {
        // the event is internal on the base class (Blazor's Dispatcher): found by name through the type's chain
        System.Reflection.EventInfo? e = null;
        for (Type? t = dispatcher?.GetType(); t is not null && e is null; t = t.BaseType)
            e = t.GetEvent("UnhandledException", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
        if (e is null) return false;
        try { e.GetAddMethod(nonPublic: true)!.Invoke(dispatcher, [new UnhandledExceptionEventHandler(OnDispatcher)]); return true; }   // AddEventHandler refuses a non-public add method
        catch (Exception) { return false; }
    }

    public void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        string text = (e.IsTerminating ? "Unhandled exception; it ends the process:\n" : "Unhandled exception (the process goes on):\n") + (e.ExceptionObject?.ToString() ?? "(no exception object)");
        Guarded(() => record(text));   // the log first: whatever follows may hang
        Show(text);
    }

    public void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Guarded(() => record("A task's exception was never observed (nobody awaited it); the app goes on:\n" + e.Exception));
        e.SetObserved();
    }

    public void OnDispatcher(object? sender, UnhandledExceptionEventArgs e) =>
        Guarded(() => record("The window's dispatcher caught an exception on the UI thread; the window goes on:\n" + (e.ExceptionObject?.ToString() ?? "(no exception object)")));

    private void Show(string text)
    {
        if (show is null || Environment.CurrentManagedThreadId == uiThread) return;
        Thread box = new(() => Guarded(() => show(text))) { IsBackground = true, Name = "failure-message" };
        box.Start();
        box.Join(showWait);   // bounded: when the process is ending, the box gets this moment to appear; a stuck one is left behind
    }

    private static void Guarded(Action action)
    {
        try { action(); } catch (Exception) { }   // a handler that throws would hide the failure it was called for
    }

    public void Dispose()
    {
        if (!installed) return;
        installed = false;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }
}
