using Microsoft.Extensions.Time.Testing;
using OpenSpecStore;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// Every failure leaves a trace: the process-wide handlers (unhandled exception, unobserved task, the window's dispatcher) write to the log first and never block,
/// a window that stops answering is noticed by a watchdog, and the store's watcher timer cannot end the process. Nothing waits for time: the watchdog runs on a FakeTimeProvider and a ping the test answers (or does not).
/// </summary>
public sealed class CatchingEveryFailureTests : IDisposable
{
    private readonly List<string> recorded = [];
    private readonly List<string> shown = [];
    private readonly TestRepo repo = new();
    private readonly FakeTimeProvider time = new();

    public void Dispose() => repo.Dispose();

    private Failures Handlers(int uiThread = -1, Action<string>? show = null) => new(recorded.Add, show ?? shown.Add, uiThread, TimeSpan.FromMilliseconds(200));

    // ---- handlers

    [Fact(DisplayName = "An unhandled exception is logged first, with its stack and whether the process ends")]
    public void An_unhandled_exception_is_logged()
    {
        using Failures failures = Handlers();

        failures.OnUnhandled(this, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: true));

        string line = Assert.Single(recorded);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("boom", line);
        Assert.Contains("ends the process", line);
    }

    [Fact(DisplayName = "An exception that was never observed by its task is logged as an error and marked observed")]
    public void An_unobserved_task_exception_is_logged_and_observed()
    {
        using Failures failures = Handlers();
        UnobservedTaskExceptionEventArgs args = new(new AggregateException(new TimeoutException("slow")));

        failures.OnUnobserved(this, args);

        Assert.Contains("TimeoutException", Assert.Single(recorded));
        Assert.True(args.Observed);
    }

    [Fact(DisplayName = "An exception the window's dispatcher caught is logged")]
    public void A_dispatcher_exception_is_logged()
    {
        using Failures failures = Handlers();

        failures.OnDispatcher(this, new UnhandledExceptionEventArgs(new NullReferenceException("render"), isTerminating: false));

        string line = Assert.Single(recorded);
        Assert.Contains("NullReferenceException", line);
        Assert.Contains("dispatcher", line);
    }

    private class FakeDispatcherBase { internal event UnhandledExceptionEventHandler? UnhandledException; public void Raise(Exception e) => UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(e, false)); }
    private sealed class FakeDispatcher : FakeDispatcherBase;

    [Fact(DisplayName = "The dispatcher's internal exception event, declared on its base class, is found and listened to")]
    public void The_dispatchers_internal_event_is_listened_to()
    {
        FakeDispatcher dispatcher = new();
        using Failures failures = Handlers();

        Assert.True(failures.WatchDispatcher(dispatcher));
        dispatcher.Raise(new InvalidCastException("component"));

        Assert.Contains("InvalidCastException", Assert.Single(recorded));
        Assert.False(failures.WatchDispatcher(new object()));   // nothing to listen to: said, not thrown
    }

    [Fact(DisplayName = "A message box that never returns does not hold the crash handler: it has logged and gone on")]
    public void The_crash_handler_never_blocks()
    {
        using ManualResetEventSlim release = new();
        using Failures failures = Handlers(show: _ => release.Wait());   // a message box nobody answers
        try
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            failures.OnUnhandled(this, new UnhandledExceptionEventArgs(new Exception("x"), isTerminating: true));

            Assert.Single(recorded);   // the log came first
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "the handler waited for the message box");
        }
        finally { release.Set(); }
    }

    [Fact(DisplayName = "The faulting thread is the window's own: no message box is shown from it")]
    public void No_message_box_from_the_window_thread()
    {
        using Failures failures = Handlers(uiThread: Environment.CurrentManagedThreadId);

        failures.OnUnhandled(this, new UnhandledExceptionEventArgs(new Exception("x"), isTerminating: true));

        Assert.Single(recorded);
        Assert.Empty(shown);
    }

    [Fact(DisplayName = "A message box is offered from another thread after the log line")]
    public void A_message_box_is_shown_from_another_thread()
    {
        using ManualResetEventSlim shownIt = new();
        int? thread = null;
        using Failures failures = Handlers(show: _ => { thread = Environment.CurrentManagedThreadId; shownIt.Set(); });

        failures.OnUnhandled(this, new UnhandledExceptionEventArgs(new Exception("x"), isTerminating: false));

        Assert.True(shownIt.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
    }

    // ---- the freeze watchdog

    private UiWatchdog Watchdog(List<Action> pings, List<string> freezes, List<string> recoveries) =>
        new(time, pings.Add, freezes.Add, () => "C:/logs/hang-1-test.dmp", recoveries.Add);

    [Fact(DisplayName = "A window that never answers its ping gives exactly one freeze record after 30 seconds")]
    public void A_silent_window_gives_one_freeze_record()
    {
        List<Action> pings = [];
        List<string> freezes = [], recoveries = [];
        using UiWatchdog watchdog = Watchdog(pings, freezes, recoveries);
        watchdog.Start();

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(freezes);   // not yet
        time.Advance(TimeSpan.FromSeconds(1));
        string record = Assert.Single(freezes);
        time.Advance(TimeSpan.FromMinutes(5));   // still silent: the same freeze, not a record per 5 seconds

        Assert.Single(freezes);
        Assert.Contains("has not answered for 30 s", record);
        Assert.Contains("hang-1-test.dmp", record);
        Assert.Single(pings);   // one ping waits for its answer; none piles up behind a stuck window
    }

    [Fact(DisplayName = "A window that answers is not a freeze, and one that answers again re-arms the watchdog")]
    public void An_answer_re_arms_the_watchdog()
    {
        List<Action> pings = [];
        List<string> freezes = [], recoveries = [];
        using UiWatchdog watchdog = Watchdog(pings, freezes, recoveries);
        watchdog.Start();
        for (int i = 0; i < 4; i++)   // a healthy window answers each ping at once
        {
            pings[^1]();
            time.Advance(TimeSpan.FromSeconds(10));
        }
        Assert.Empty(freezes);

        time.Advance(TimeSpan.FromSeconds(30));   // then it stops
        Assert.Single(freezes);
        pings[^1]();   // and wakes up
        Assert.Single(recoveries);
        time.Advance(TimeSpan.FromSeconds(5));   // the next ping goes out
        time.Advance(TimeSpan.FromSeconds(30));   // and is not answered either

        Assert.Equal(2, freezes.Count);
    }

    [Fact(DisplayName = "A dump that cannot be written is said in the freeze record, which is still logged")]
    public void A_failing_dump_does_not_hide_the_freeze()
    {
        List<string> freezes = [];
        using UiWatchdog watchdog = new(time, _ => { }, freezes.Add, () => throw new IOException("disk full"), null);
        watchdog.Start();

        time.Advance(TimeSpan.FromSeconds(30));

        string record = Assert.Single(freezes);
        Assert.Contains("has not answered for 30 s", record);
        Assert.Contains("disk full", record);
    }

    // ---- the store's watcher timer

    [Fact(DisplayName = "A listener that throws on the watcher's timer is logged and the timer keeps working")]
    public void A_throwing_listener_on_the_watcher_timer_is_logged()
    {
        OpenSpecStoreModule.State.Time = time;
        OpenSpecStoreModule.State.RealWatcher = false;
        time.SetUtcNow(DateTimeOffset.UtcNow.AddHours(1));
        OpenSpecStoreApi.Open(repo.Root);
        OpenSpecStoreApi.Verify();   // the first look: what the stamps are held against from here
        List<Exception> faults = [];
        OpenSpecStoreApi.BackgroundFault = faults.Add;
        int heard = 0;
        void Listener() { heard++; throw new InvalidOperationException("a listener's bug"); }   // not an IO error: it used to end the process
        OpenSpecStoreApi.Changed += Listener;
        try
        {
            string yaml = repo.PathOf("openspec/changes/add-health-check/.openspec.yaml");
            OutsideEdit(yaml, "one");
            OpenSpecStoreModule.ObserveChange(OpenSpecStoreModule.Current!, yaml);
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.IsType<InvalidOperationException>(Assert.Single(faults));

            OutsideEdit(yaml, "two, longer");   // the next outside edit: the timer is armed again and does the same
            OpenSpecStoreModule.ObserveChange(OpenSpecStoreModule.Current!, yaml);
            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.Equal(2, faults.Count);
            Assert.Equal(2, heard);
        }
        finally { OpenSpecStoreApi.Changed -= Listener; }
    }

    private static void OutsideEdit(string path, string text)
    {
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-30 - text.Length));
    }
}
