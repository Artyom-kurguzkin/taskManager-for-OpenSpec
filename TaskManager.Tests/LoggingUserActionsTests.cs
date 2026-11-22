using System.Diagnostics;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Radzen;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.Helpers;
using WorkItems;

namespace TaskManager.Tests;

/// <summary>
/// What the page side of the app leaves in the run log (#wi178): one `ui` line per user action that changes something, what the error boundary logs when a component fails,
/// the page's script errors the bridge hands over (rate limited), the WebView2 process's end, and a page closed while its reload timer is pending. The log is the test's own, through AppLog's seam.
/// </summary>
public sealed class LoggingUserActionsTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-ui-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly CaptureLog log = new();

    public LoggingUserActionsTests() => AppLog.Use(log);   // this test case's own log: the code that runs from here writes to it

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext(TimeProvider? time = null, AppMode? mode = null)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);
        if (mode is not null) ctx.Services.AddSingleton(mode);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        OpenSpecStore.OpenSpecStoreModule.State.RealWatcher = false;
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        return ctx;
    }

    // ── user actions ──

    [Fact(DisplayName = "Changing a status from the list writes one ui line naming the page, the action and the item")]
    public async Task StatusChangeLogsOneUiLine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        nav.ClickWhenReady("button[aria-label=Layout]");
        nav.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "List"));
        board.WaitForAssertion(() => Assert.NotEmpty(board.FindAll("tr[data-id]")));

        board.FireWhenReady("tr[data-id] button[data-pill=status]", e => e.Click(), all => all.First(e => e.Closest("tr")!.TextContent.Contains("#wi107")));
        board.FireWhenReady(".menu-pop .menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "Blocked"));

        board.WaitForAssertion(() => Assert.Equal("Blocked", WorkItemsApi.Get(107)!.Status));
        board.WaitForAssertion(() => Assert.Single(log.Ui()), WaitThen.Bound);
        await board.InvokeAsync(() => { });
        Assert.Equal(["ui Board SetStatus #wi107"], log.Ui());
        Assert.Equal(LogLevel.Information, log.Entries.Single(e => e.Message.StartsWith("ui ")).Level);
    }

    [Fact(DisplayName = "Ticking a subtask on the item page writes one ui line naming the item")]
    public async Task SubtaskTickLogsOneUiLine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        page.InvokeAsync(() => page.Find(".item-views input[value='linked']").ChangeAsync("linked")).GetAwaiter().GetResult();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#sub-2-1")));

        page.ChangeWhenReady("#sub-2-1", true);

        page.WaitForAssertion(() => Assert.Contains("- [x] 2.1", repo.Read("openspec/changes/add-health-check/tasks.md")));
        page.WaitForAssertion(() => Assert.Single(log.Ui()), WaitThen.Bound);
        await page.InvokeAsync(() => { });
        Assert.Equal(["ui WorkItem SetSubtaskDone #wi123"], log.Ui());
    }

    [Fact(DisplayName = "Saving a note writes one ui line naming the note")]
    public async Task NoteSaveLogsOneUiLine()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<NotePage> page = ctx.Render<NotePage>(p => p.Add(c => c.Id, 123));
        page.ClickWhenReady(".note-view");
        page.InputWhenReady("textarea.desc-in", "# Ping test\n\nlogged edit");
        page.BlurWhenReady("textarea.desc-in");

        page.WaitForAssertion(() => Assert.Contains("logged edit", NotesApi_Body()));
        page.WaitForAssertion(() => Assert.Single(log.Ui()), WaitThen.Bound);
        await page.InvokeAsync(() => { });
        Assert.Equal(["ui Note SaveBody #n123"], log.Ui());
    }

    private static string NotesApi_Body() => Notes.NotesApi.Get(123)!.Body;

    private sealed class Probe : RepoPage
    {
        protected override void Load() { }
        public Task<bool> Background() => RunInBackground(() => (false, "refused in the background"));
        public bool Foreground() => Run((false, "refused in the foreground"));
    }

    [Fact(DisplayName = "A refused command writes a warning, not a ui line")]
    public async Task RefusedCommandIsAWarning()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<Probe> page = ctx.Render<Probe>();

        await page.InvokeAsync(() => page.Instance.Background());
        await page.InvokeAsync(() => page.Instance.Foreground());

        Assert.Equal(2, log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("A command was refused")));
        Assert.Empty(log.Ui());
    }

    [Fact(DisplayName = "Changing a setting or hiding a work item writes one ui line")]
    public void SettingsAndHidingLogUiLines()
    {
        SettingsStore settings = new(settingsFile);

        settings.Update(s => s with { Theme = "dark" });
        settings.SetItemsHidden(repo.Root, [107], true);

        Assert.Equal(["ui Settings Update Theme=dark", "ui Settings Hide #wi107"], log.Ui());
    }

    // ── Blazor errors ──

    private sealed class Thrower : ComponentBase
    {
        protected override void OnInitialized() => throw new InvalidOperationException("wi178 component failure");
    }

    [Fact(DisplayName = "A component that throws inside the app's error boundary logs one Error with the exception type and the page, and the boundary shows a message with a Reload link")]
    public async Task ErrorBoundaryLogsAndShowsReload()
    {
        await using BunitContext ctx = NewContext();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/item/123");

        IRenderedComponent<AppErrorBoundary> boundary = ctx.Render<AppErrorBoundary>(p => p.AddChildContent<Thrower>());

        LogEntry error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("InvalidOperationException", error.Message);
        Assert.Contains("/item/123", error.Message);
        Assert.IsType<InvalidOperationException>(error.Error);
        Assert.Contains("Reload", boundary.Find("a.error-reload").TextContent);
        Assert.NotEmpty(boundary.FindAll(".app-error"));
    }

    [Fact(DisplayName = "The error boundary lets go of the error when the address changes")]
    public async Task ErrorBoundaryRecoversOnNavigation()
    {
        await using BunitContext ctx = NewContext();
        IRenderedComponent<AppErrorBoundary> boundary = ctx.Render<AppErrorBoundary>(p => p.AddChildContent<Thrower>());
        Assert.NotEmpty(boundary.FindAll(".app-error"));

        await boundary.InvokeAsync(() => ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/board"));

        boundary.WaitForAssertion(() => Assert.Equal(2, log.Entries.Count(e => e.Level == LogLevel.Error)), WaitThen.Bound);   // it rendered the child again, which failed again, and that is logged too
    }

    // ── page script errors ──

    [Fact(DisplayName = "A page script error reaches the log as one Warning with its message, source, line, column and stack")]
    public void PageErrorIsLogged()
    {
        PageErrors errors = new(new FakeTimeProvider());

        errors.Record("Uncaught Error: wi178 page test", "http://localhost/js/app.js", 12, 7, "Error: wi178 page test\n    at app.js:12:7");

        LogEntry entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("wi178 page test", entry.Message);
        Assert.Contains("app.js:12:7", entry.Message);
        Assert.Contains("at app.js:12:7", entry.Message);
    }

    [Fact(DisplayName = "At most 20 page errors a minute are logged; the rest are counted and reported when the minute ends")]
    public void PageErrorsAreRateLimited()
    {
        FakeTimeProvider time = new();
        PageErrors errors = new(time);

        for (int i = 0; i < 30; i++) errors.Record("loop " + i, "app.js", 1, 1, null);
        Assert.Equal(21, log.Entries.Count);   // twenty errors and one line saying the rest are dropped
        Assert.Contains("dropped", log.Entries[^1].Message);
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("loop 20"));

        time.Advance(TimeSpan.FromSeconds(61));
        errors.Record("after the minute", "app.js", 1, 1, null);

        Assert.Contains("10 page errors were dropped", log.Entries[^2].Message);
        Assert.Contains("after the minute", log.Entries[^1].Message);
    }

    [Fact(DisplayName = "The page reports its errors to the app through a JSInvokable static Report")]
    public void ReportIsInvokableFromThePage()
    {
        MethodInfo report = typeof(PageErrors).GetMethod("Report", BindingFlags.Public | BindingFlags.Static)!;

        Assert.NotNull(report.GetCustomAttribute<JSInvokableAttribute>());
    }

    // ── WebView2 ──

    [Fact(DisplayName = "A WebView2 failure is described by its kind, reason and exit code")]
    public void WebViewFailureIsDescribed()
    {
        Assert.Equal("WebView2 ProcessFailed: kind RenderProcessExited, reason Crashed, exit code -1073741819", WebViewHealth.Describe("RenderProcessExited", "Crashed", -1073741819));
    }

    [Fact(DisplayName = "Attaching to no window does nothing")]
    public void AttachWithoutAWindowIsANoOp()
    {
        WebViewHealth.Attach(null!);

        Assert.Empty(log.Entries);
    }

    [Fact(DisplayName = "A watched WebView2 browser process that ends with a failure code is logged as an Error with its pid and exit code")]
    public async Task ProcessExitIsLogged()
    {
        using Process child = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 3") { CreateNoWindow = true, UseShellExecute = false })!;

        await WebViewHealth.WatchAsync(child);

        LogEntry entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("exit code 3", entry.Message);
        Assert.Contains(child.Id.ToString(), entry.Message);
    }

    [Fact(DisplayName = "Looking for WebView2 children of a process with none finds nothing")]
    public void NoWebViewChildren() => Assert.Empty(WebViewHealth.ChildrenOf(Environment.ProcessId, "msedgewebview2"));

    // ── the window timer ──

    [Fact(DisplayName = "A page closed while its window timer is pending does not throw when the timer ends")]
    public async Task WindowEndedAfterDisposeIsHarmless()
    {
        FakeTimeProvider time = new();
        BunitContext ctx = NewContext(time, new AppMode { Repo = repo.Root });
        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        WorkItemPage instance = page.Instance;
        await ctx.DisposeAsync();   // the window closed: its services are gone, the timer's callback may still be on its way

        Exception? thrown = Record.Exception(() => { instance.WindowEnded(); time.Advance(TimeSpan.FromSeconds(1)); });

        Assert.Null(thrown);
        Assert.DoesNotContain(log.Entries, e => e.Level > LogLevel.Warning);
    }
}

/// <summary>Collects what is logged, with level, category, message and exception: the test's own log (<see cref="AppLog.Use"/>).</summary>
internal sealed class CaptureLog : ILoggerFactory
{
    private readonly object gate = new();
    private readonly List<LogEntry> entries = [];

    public IReadOnlyList<LogEntry> Entries { get { lock (gate) return [.. entries]; } }

    /// <summary>The messages of the lines that start with "ui ", in order.</summary>
    public List<string> Ui() => [.. Entries.Where(e => e.Message.StartsWith("ui ", StringComparison.Ordinal)).Select(e => e.Message)];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private sealed class Logger(CaptureLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.gate) owner.entries.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
        }
    }
}

internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Error);
