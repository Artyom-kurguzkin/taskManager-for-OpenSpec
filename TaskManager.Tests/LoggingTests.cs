using System.Diagnostics;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Radzen;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using Microsoft.Extensions.Time.Testing;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.Helpers;
using TaskManager.Mcp;

namespace TaskManager.Tests;

/// <summary>
/// The app's logs (#wi178): a file per run, one trace id from a command to the write and the scan it caused, and the levels. The clock and the logs folder are the test's own; nothing waits for real time.
/// </summary>
public sealed class LoggingTests : IDisposable
{
    private readonly string logs = Path.Combine(Path.GetTempPath(), "rtm-logs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 20, 10, 11, 12, TimeSpan.Zero));
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-logs-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        repo.Dispose();
        try { Directory.Delete(logs, recursive: true); } catch (IOException) { }
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    /// <summary>The lines of the only run file in the logs folder, once the factory has flushed and closed it.</summary>
    private string[] Lines(ILoggerFactory factory)
    {
        factory.Dispose();
        return File.ReadAllLines(Assert.Single(Directory.GetFiles(logs, "run-*.log")));
    }

    [Fact(DisplayName = "A run writes one log file named after its start and pid, with a header line")]
    public void OneFilePerRun()
    {
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 4242);
        factory.CreateLogger("Test").LogInformation("hello");
        string[] lines = Lines(factory);

        Assert.Equal("run-20261120-101112-4242.log", Path.GetFileName(Directory.GetFiles(logs).Single()));
        Assert.Contains("pid 4242", lines[0]);
        Assert.Contains("2026-11-20 10:11:12", lines[0]);
        Assert.Matches(@"^10:11:12\.000 INFO  - Test: hello$", lines[1]);   // no activity current: no trace id
    }

    [Fact(DisplayName = "Only the newest 20 run logs are kept")]
    public void NewestTwentyKept()
    {
        Directory.CreateDirectory(logs);
        for (int i = 1; i <= 25; i++) File.WriteAllText(Path.Combine(logs, $"run-202601{i:00}-000000-1.log"), "old");
        File.WriteAllText(Path.Combine(logs, "crash-2026-01-01.log"), "not a run log");

        AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 7).Dispose();

        string[] runs = [.. Directory.GetFiles(logs, "run-*.log").Select(Path.GetFileName).Order()!];
        Assert.Equal(20, runs.Length);
        Assert.Equal("run-20260107-000000-1.log", runs[0]);   // 1 to 6 went: the 19 newest of the old ones and this run's file remain
        Assert.Contains("run-20261120-101112-7.log", runs);
        Assert.True(File.Exists(Path.Combine(logs, "crash-2026-01-01.log")));
    }

    [Fact(DisplayName = "A queued write logs with the trace id of the command that queued it")]
    public async Task AQueuedWriteKeepsTheTraceOfItsCommand()
    {
        OpenSpecStoreModule.State.RealWatcher = false;
        Workspace workspace = new(new SettingsStore(settingsFile), new UiService(), time: time);   // the writes' quiet time runs on the test's clock
        Assert.True(workspace.Open(repo.Root).Success);
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Debug, time, pid: 1);
        AppLog.Use(factory);

        string trace;
        using (Activity command = AppLog.Start("ui.command", "TestPage")!)
        {
            trace = command.TraceId.ToString();
            await workspace.DoLater(() => (true, "ok"));
        }
        time.Advance(TimeSpan.FromMilliseconds(300));   // the quiet time after the write ends on a timer, outside the command (#wi213): the news and its scan still carry the command's trace
        await workspace.Resolved;   // the scan the write started
        string[] lines = Lines(factory);

        string[] mine = [.. lines.Where(l => l.Contains(trace))];
        Assert.Contains(mine, l => l.Contains("ui.command TestPage start"));
        Assert.Contains(mine, l => l.Contains("write") && l.Contains(" start"));
        Assert.Contains(mine, l => l.Contains("write") && l.Contains(" stop "));
        Assert.Contains(mine, l => l.Contains("scan") && l.Contains(" stop "));
        Assert.Contains(mine, l => l.Contains("ui.command TestPage stop "));
        Assert.Equal(lines.Count(l => l.Contains(" stop ")), mine.Count(l => l.Contains(" stop ")));   // no stop line of this run belongs to another trace
    }

    [Fact(DisplayName = "An MCP tool call logs its name, duration and trace id")]
    public async Task AToolCallIsTraced()
    {
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 1);
        AppLog.Use(factory);

        string trace;
        using (Activity outer = AppLog.Start("test.agent")!)
        {
            trace = outer.TraceId.ToString();
            await new BoardTools().BoardList(repo: repo.Root);
        }
        string[] lines = Lines(factory);

        string line = Assert.Single(lines, l => l.Contains("tool BoardList stop"));
        Assert.Contains(" INFO ", line);
        Assert.Contains(trace, line);
        Assert.Matches(@"stop \d+(\.\d+)?ms", line);
    }

    [Fact(DisplayName = "Debug lines are off by default and on with RTM_LOG=Debug")]
    public void DebugIsOptIn()
    {
        Assert.Equal(LogLevel.Information, AppLog.LevelFrom(null));
        Assert.Equal(LogLevel.Information, AppLog.LevelFrom("nonsense"));
        Assert.Equal(LogLevel.Debug, AppLog.LevelFrom("Debug"));
        Assert.Equal(LogLevel.Debug, AppLog.LevelFrom("debug"));

        ILoggerFactory quiet = AppLog.Create(logs, console: false, AppLog.LevelFrom(null), time, pid: 1);
        quiet.CreateLogger("T").LogDebug("hidden detail");
        quiet.CreateLogger("T").LogInformation("shown");
        string[] quietLines = Lines(quiet);
        Assert.DoesNotContain(quietLines, l => l.Contains("hidden detail"));
        Assert.Contains(quietLines, l => l.Contains("shown"));

        Directory.Delete(logs, recursive: true);
        ILoggerFactory verbose = AppLog.Create(logs, console: false, AppLog.LevelFrom("Debug"), time, pid: 1);
        verbose.CreateLogger("T").LogDebug("hidden detail");
        Assert.Contains(Lines(verbose), l => l.Contains(" DEBUG ") && l.Contains("hidden detail"));
    }

    [Fact(DisplayName = "A logged exception keeps its type, message and stack")]
    public void AnExceptionIsKeptWhole()
    {
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 1);
        try { ThrowIt(); }
        catch (InvalidOperationException e) { factory.CreateLogger("T").LogError(e, "the write failed"); }
        string[] lines = Lines(factory);

        int at = Array.FindIndex(lines, l => l.Contains(" ERROR ") && l.EndsWith("T: the write failed"));
        Assert.True(at > 0);
        string rest = string.Join("\n", lines[(at + 1)..]);
        Assert.Contains("System.InvalidOperationException: board is locked", rest);
        Assert.Contains("   at TaskManager.Tests.LoggingTests.ThrowIt", rest);
    }

    // ---- terminal output of a WinExe (decision 1) ----

    [Fact(DisplayName = "A WinExe with no stdout attaches to the parent terminal, allocates a console only on --console, and keeps a redirected stdout")]
    public void ConsoleModeIsDecidedFromWhatTheProcessHas()
    {
        int attaches = 0;
        Func<bool> parent = () => { attaches++; return true; };
        Func<bool> none = () => { attaches++; return false; };

        Assert.Equal(AppLog.ConsoleMode.Inherit, AppLog.DecideConsole(hasStdout: true, parent, []));   // a pipe or file someone redirected to: kept, no attach
        Assert.Equal(0, attaches);
        Assert.Equal(AppLog.ConsoleMode.Attach, AppLog.DecideConsole(hasStdout: false, parent, []));   // started from a terminal (dotnet run)
        Assert.Equal(AppLog.ConsoleMode.None, AppLog.DecideConsole(hasStdout: false, none, []));   // started from Explorer: no console, only the file
        Assert.Equal(AppLog.ConsoleMode.Alloc, AppLog.DecideConsole(hasStdout: false, none, ["--console"]));   // asked for one
        Assert.Equal(AppLog.ConsoleMode.Attach, AppLog.DecideConsole(hasStdout: false, parent, ["--console"]));   // a parent console is better than a new window
        Assert.Equal(AppLog.ConsoleMode.Inherit, AppLog.DecideConsole(hasStdout: true, none, ["--console"]));
    }

    [Fact(DisplayName = "The built app exe writes its self-test line to a redirected stdout")]
    public void TheBuiltExeKeepsARedirectedStdout()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "TaskManager.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(exe)) return;   // the WinExe apphost is the Windows build
        ProcessStartInfo start = new(exe, "--console-selftest") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using Process app = Process.Start(start)!;
        Task<string> output = app.StandardOutput.ReadToEndAsync();
        Assert.True(app.WaitForExit(30_000), "the self-test did not end");
        Assert.Equal(0, app.ExitCode);
        Assert.Equal("console ok", output.GetAwaiter().GetResult().Trim());
    }

    // ---- MCP chatter (decision 2) ----

    [Fact(DisplayName = "The MCP library logs at Warning and above, and a tool refusal it reports as an error is a warning")]
    public void McpChatterIsQuietAndARefusalIsAWarning()
    {
        ILoggerFactory factory = AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 1);
        ILogger mcp = factory.CreateLogger("ModelContextProtocol.Server.McpServer");
        mcp.LogInformation("server starting");
        mcp.LogError(new McpException("Agent name is not allowed"), "\"board_note\" threw an unhandled exception.");
        mcp.LogError(new InvalidOperationException("a real bug"), "\"board_create\" threw an unhandled exception.");
        mcp.LogWarning("slow client");
        string[] lines = Lines(factory);

        Assert.DoesNotContain(lines, l => l.Contains("server starting"));
        Assert.Contains(lines, l => l.Contains(" WARN ") && l.Contains("\"board_note\" threw an unhandled exception."));
        Assert.DoesNotContain(lines, l => l.Contains(" ERROR ") && l.Contains("board_note"));
        Assert.Contains(lines, l => l.Contains(" ERROR ") && l.Contains("\"board_create\" threw"));   // anything but an McpException stays an error
        Assert.Contains(lines, l => l.Contains(" WARN ") && l.Contains("slow client"));
    }

    // ---- build stamp (decision 6) ----

    [Fact(DisplayName = "The version in the log header carries the build time embedded in the assembly, not a file time")]
    public void TheVersionCarriesTheEmbeddedBuildTime()
    {
        string? stamp = typeof(Paths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "BuildTime")?.Value;

        Assert.Matches(@"^\d{14}$", stamp);
        Assert.EndsWith("+" + stamp, Paths.Version);
    }

    // ---- open the current log (decision 7) ----

    [Fact(DisplayName = "Open current log in the side menu opens this run's log file, in Notepad when .log has no default program")]
    public async Task OpenCurrentLogAsksForTheRunLog()
    {
        AppLog.Use(AppLog.Create(logs, console: false, LogLevel.Information, time, pid: 55));
        string expected = Path.Combine(logs, "run-20261120-101112-55.log");
        Assert.Equal(expected, AppLog.CurrentFile);

        List<string> opened = [];
        await using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        List<ProcessStartInfo> started = [];
        bool hasDefault = false;   // this PC: .log has no default program, so the shell would show the "Open with" picker
        ctx.Services.AddSingleton<FolderOpener>(path => { opened.Add(path); return FolderOpeners.Open(path, windows: true, hasDefaultProgram: _ => hasDefault, start: s => started.Add(s)); });
        OpenSpecStoreModule.State.RealWatcher = false;
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        IRenderedComponent<SideNav> nav = ctx.Render<SideNav>();
        Assert.Empty(nav.FindAll("button.nav-log"));   // the collapsed rail has no such item
        nav.ClickWhenReady("button.nav-toggle");

        nav.ClickWhenReady("button.nav-log");

        nav.WaitForAssertion(() => Assert.Equal([expected], opened));
        Assert.Equal("Open current log", nav.Find("button.nav-log").GetAttribute("aria-label"));
        ProcessStartInfo notepad = Assert.Single(started);
        Assert.Equal("notepad.exe", notepad.FileName);
        Assert.Equal($"\"{expected}\"", notepad.Arguments);
        Assert.False(notepad.UseShellExecute);

        // with a default program (or off Windows) the shell opens it, as before
        hasDefault = true;
        nav.ClickWhenReady("button.nav-log");
        nav.WaitForAssertion(() => Assert.Equal(2, started.Count));
        Assert.True(started[1].UseShellExecute);
        Assert.Equal(expected, started[1].FileName);
        Assert.True(FolderOpeners.Open(expected, windows: false, hasDefaultProgram: _ => false, start: s => started.Add(s)).Success);
        Assert.True(started[2].UseShellExecute);
    }

    private static void ThrowIt() => throw new InvalidOperationException("board is locked");
}
