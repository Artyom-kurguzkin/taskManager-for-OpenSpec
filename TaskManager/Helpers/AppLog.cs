using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ModelContextProtocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TaskManager.Helpers;

/// <summary>
/// The app's one logging seam: Microsoft.Extensions.Logging with two sinks that share one line format, the console (only when the process has one: <c>dotnet run</c>; the published exe has none)
/// and a file per run, <c>run-yyyyMMdd-HHmmss-pid.log</c> in the logs folder, the newest 20 kept. Traces are <see cref="Activity"/> objects of the source "RepoTasks": one per UI command, queued write,
/// scan and MCP tool call. Activity.Current flows with the execution context, so every line written while one is current carries its trace id, and one id follows a button press
/// through the command, the write on the pool and the scan the write started. A line is <c>HH:mm:ss.fff LEVEL traceId category: message</c>, an exception follows on the next lines.
/// </summary>
public static class AppLog
{
    public const int KeepRuns = 20;
    public static readonly ActivitySource Source = new("RepoTasks");

    private static ILoggerFactory shared = NullLoggerFactory.Instance;
    private static readonly AsyncLocal<ILoggerFactory?> isolated = new();   // the seam the tests use (see IsolatedModuleState): code running from where it is set logs to its own factory

    /// <summary>The factory of the code that is running: the test's own when one is set, otherwise the app's (silent until <see cref="Init"/>).</summary>
    public static ILoggerFactory Factory => isolated.Value ?? shared;
    public static ILogger Logger(string category) => Factory.CreateLogger(category);
    public static ILogger For<T>() => Factory.CreateLogger(typeof(T).Name);

    static AppLog()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        // One listener for the process; it logs through the factory of the thread that starts or stops the activity.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => Trace(a, "start", LogLevel.Debug),
            ActivityStopped = a => Trace(a, $"stop {a.Duration.TotalMilliseconds:0.#}ms{(a.GetTagItem("result") is { } r ? " " + r : "")}", a.GetTagItem("level") is LogLevel l ? l : LogLevel.Information),
        });
    }

    private static void Trace(Activity a, string what, LogLevel level)
    {
        Activity? previous = Activity.Current;
        Activity.Current = a;   // when it stops, Current is already its parent: the line still carries this activity's own trace
        try { Logger("Trace").Log(level, "{Name} {What}", a.OperationName, what); }
        finally { Activity.Current = previous; }
    }

    /// <summary>
    /// Starts an activity named "name detail" (a child of the current one, else a new trace; with <paramref name="parent"/>, a child of that one: work that runs later, on a timer, under the trace of what asked for it).
    /// Stopped by Dispose; its start is logged at Debug, its stop with the duration at <paramref name="level"/>.
    /// </summary>
    public static Activity? Start(string name, string? detail = null, LogLevel level = LogLevel.Information, ActivityContext parent = default)
    {
        Activity? a = Source.StartActivity(detail is null ? name : name + " " + detail, ActivityKind.Internal, parent);   // the detail is in the name: the start line is logged before a tag could be set; a default parent is the current activity
        a?.SetTag("level", level);
        return a;
    }

    private static readonly DateTime processStart = StartOfProcess();
    private static DateTime StartOfProcess()
    {
        try { using Process self = Process.GetCurrentProcess(); return self.StartTime; }
        catch (Exception) { return DateTime.Now; }   // no process start time to read: marks count from here
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ILoggerFactory, HashSet<string>> marked = new();

    /// <summary>
    /// A timing mark of the start: one Information line <c>mark &lt;name&gt; +&lt;ms since the process started&gt;</c>, the contract the startup measurement (tests/startup-time.ps1) and every chunk of the fast start rely on.
    /// The names are fixed: <c>main</c>, <c>window-created</c>, <c>first-render</c>, <c>headers-read &lt;project&gt;</c>, <c>repo-loaded &lt;project&gt;</c>, <c>git-ready &lt;project&gt;</c>, <c>scan-done &lt;project&gt;</c>.
    /// </summary>
    public static void Mark(string name)
    {
        long ms = (long)(DateTime.Now - processStart).TotalMilliseconds;
        Logger("Startup").LogInformation("mark {Name} +{Ms}", name, ms);
    }

    /// <summary>Like <see cref="Mark"/>, once per run (per log): the second call with the same name writes nothing. The first board render asks it.</summary>
    public static void MarkOnce(string name)
    {
        HashSet<string> seen = marked.GetOrCreateValue(Factory);
        lock (seen) if (!seen.Add(name)) return;
        Mark(name);
    }

    /// <summary>RTM_LOG names the minimum level (Trace, Debug, Information, Warning, Error); Information when it is missing or not a level.</summary>
    public static LogLevel LevelFrom(string? text) => Enum.TryParse(text, ignoreCase: true, out LogLevel l) && l != LogLevel.None ? l : LogLevel.Information;

    /// <summary>How the process gets a terminal to print to (decision 1 of #wi178): the std handle it was given, the console of the terminal that started it, a console of its own, or none.</summary>
    public enum ConsoleMode { None, Inherit, Attach, Alloc }

    /// <summary>
    /// The decision, apart from the system calls: stdout that exists (a terminal under <c>dotnet run</c> on other systems, a pipe or file someone redirected to) is kept;
    /// otherwise the parent's console is attached (<paramref name="attachParent"/> says whether that worked); with none, <c>--console</c> asks for a console of its own.
    /// </summary>
    public static ConsoleMode DecideConsole(bool hasStdout, Func<bool> attachParent, IEnumerable<string> args)
    {
        if (hasStdout) return ConsoleMode.Inherit;
        if (attachParent()) return ConsoleMode.Attach;
        return args.Contains("--console") ? ConsoleMode.Alloc : ConsoleMode.None;
    }

    /// <summary>
    /// True when the process has a console to write to, after making one: a WinExe started from a terminal (<c>dotnet run</c>, a PowerShell prompt) is given no stdout by Windows, so it attaches to the
    /// terminal's console and reopens stdout and stderr on it; <c>--console</c> allocates one when there is no terminal. The published exe started from Explorer has none, and logs to its file only.
    /// </summary>
    public static bool HasConsole() => Prepare(Environment.GetCommandLineArgs().Skip(1)) != ConsoleMode.None;

    /// <summary>What <see cref="HasConsole"/> does, returning the mode it ended in; the self-test (<c>--console-selftest</c>) starts here.</summary>
    public static ConsoleMode Prepare(IEnumerable<string> args)
    {
        if (!OperatingSystem.IsWindows()) return ConsoleMode.Inherit;
        IntPtr h = GetStdHandle(-11);   // STD_OUTPUT_HANDLE
        bool hasStdout = h != IntPtr.Zero && h != new IntPtr(-1);
        ConsoleMode mode = DecideConsole(hasStdout, () => AttachConsole(-1), args);   // ATTACH_PARENT_PROCESS
        if (mode == ConsoleMode.Alloc && !AllocConsole()) return ConsoleMode.None;
        if (mode is ConsoleMode.Attach or ConsoleMode.Alloc)
        {
            try
            {
                // The std handles of a process started without them stay empty after the attach: open the console's own output and make it the process's stdout and stderr.
                SafeFileHandle conout = CreateFile("CONOUT$", 0x40000000 /* GENERIC_WRITE */, 3 /* share read and write */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
                if (conout.IsInvalid) return ConsoleMode.None;
                SetStdHandle(-11, conout.DangerousGetHandle());
                SetStdHandle(-12, conout.DangerousGetHandle());   // STD_ERROR_HANDLE
                StreamWriter writer = new(new FileStream(conout, FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
                if (mode == ConsoleMode.Attach) writer.WriteLine();   // the prompt of the terminal that started the app is on the line the first log line would glue to
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return ConsoleMode.None; }
        }
        return mode;
    }

    /// <summary><c>--console-selftest</c>: attaches the way startup does and prints "console ok" (with RTM_SELFTEST_MODE=1 also the mode), so the terminal path can be checked without starting the window.</summary>
    public static int ConsoleSelfTest(IEnumerable<string> args)
    {
        ConsoleMode mode = Prepare(args);
        if (mode == ConsoleMode.None) return 2;
        Console.WriteLine("console ok");
        if (Environment.GetEnvironmentVariable("RTM_SELFTEST_MODE") == "1") Console.WriteLine("mode " + mode);
        return 0;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int which, IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    /// <summary>The log file of this run, or null when it has none (a folder that cannot be written, a factory made without one).</summary>
    public static string? CurrentFile => Factory is Owning o ? o.File : null;

    /// <summary>The folder the run log, the crash notes and the freeze dumps go to now: the one the user chose, else the app's own (<see cref="Paths.Logs"/>).</summary>
    public static string Folder => Factory is Owning o ? o.Folder : Paths.Logs;

    /// <summary>The app's own logs folder: where the run markers and the publish-stop files always are, and where the logs go when no folder was chosen.</summary>
    public static string DefaultFolder => Factory is Owning o ? o.AppFolder : Paths.Logs;

    /// <summary>The folder the logs went to before the last <see cref="SwitchFolder"/>, or null when they were never moved in this run.</summary>
    public static string? PreviousFolder => Factory is Owning o ? o.Previous : null;

    /// <summary>
    /// The app's logging, from now on: the run's file in <paramref name="logsDir"/> and, with <paramref name="console"/>, the console. Called once, first thing in Main.
    /// <paramref name="appFolder"/> is the app's own folder (<see cref="Paths.Logs"/> unless a test says otherwise), which <paramref name="logsDir"/> differs from when the user chose another.
    /// </summary>
    public static ILoggerFactory Init(string logsDir, bool console, LogLevel min, TimeProvider? clock = null, int? pid = null, string? appFolder = null) =>
        shared = Create(logsDir, console, min, clock, pid, appFolder);

    /// <summary>A factory that writes the run's file (and the console when asked) without becoming the app's: what <see cref="Init"/> builds, and what a test uses with <see cref="Use"/>.</summary>
    public static ILoggerFactory Create(string logsDir, bool console, LogLevel min, TimeProvider? clock = null, int? pid = null, string? appFolder = null)
    {
        clock ??= TimeProvider.System;
        int processId = pid ?? Environment.ProcessId;
        LineSink sink = new(clock);
        string? file = null;
        try
        {
            Directory.CreateDirectory(logsDir);
            file = RunFile(logsDir, clock, processId);
            sink.File = Open(file);
            Prune(logsDir, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { file = null; /* a log that cannot be written changes nothing */ }
        if (console) sink.Console = Console.Out;
        sink.Header(HeaderLine("started", clock, processId, min));
        LoggerFilterOptions filters = new() { MinLevel = min };
        filters.Rules.Add(new LoggerFilterRule(null, "Microsoft", LogLevel.Warning, null));   // the host's and Kestrel's own chatter stays at warnings
        filters.Rules.Add(new LoggerFilterRule(null, "ModelContextProtocol", LogLevel.Warning, null));   // so does the MCP library's: a line per request is the trace's job (tool BoardList stop ...)
        return new Owning(new LoggerFactory([sink], filters), sink, file, logsDir, appFolder ?? Paths.Logs, clock, processId, min);
    }

    private static string RunFile(string dir, TimeProvider clock, int pid) => Path.Combine(dir, $"run-{clock.GetLocalNow():yyyyMMdd-HHmmss}-{pid}.log");
    private static StreamWriter Open(string file) => new(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
    private static string HeaderLine(string verb, TimeProvider clock, int pid, LogLevel min) =>
        $"Repo tasks {Paths.Version} pid {pid} {verb} {clock.GetLocalNow():yyyy-MM-dd HH:mm:ss} {RuntimeInformation.OSDescription} min level {min}";

    /// <summary>
    /// Moves the logging to <paramref name="folder"/> (null or empty: the app's own folder) at once: the current run file is closed with a last line that says where the log goes on, a new run file opens there
    /// whose first line names the file before it, and the newest 20 are kept in that folder. A folder that cannot be written leaves the log where it is. Nothing already written is moved (<see cref="MoveExistingLogs"/>).
    /// </summary>
    public static (bool Success, string Message) SwitchFolder(string? folder) =>
        Factory is Owning o ? o.Switch(string.IsNullOrWhiteSpace(folder) ? o.AppFolder : Path.GetFullPath(folder.Trim())) : (false, "This run has no log to move.");

    /// <summary>
    /// "Move existing logs here": the run logs, crash notes and freeze dumps (and nothing else) in the folder the logs came from (the one before the last switch, else the app's own) are copied to
    /// <see cref="Folder"/> and the copied originals deleted. A file that is already there under that name stays where it is. Without a folder to take them from, nothing happens.
    /// </summary>
    public static (bool Success, string Message, int Moved) MoveExistingLogs()
    {
        if (Factory is not Owning o) return (false, "This run has no log to move.", 0);
        string? from = o.Previous ?? (SamePath(o.Folder, o.AppFolder) ? null : o.AppFolder);
        if (from is null) return (true, "There are no earlier logs to move.", 0);
        (bool ok, string message, int moved) = MoveLogFiles(from, o.Folder, o.File);
        if (ok) o.Previous = null;
        return (ok, message, moved);
    }

    /// <summary>Appends the text to <c>crash-yyyy-MM-dd.log</c> in <see cref="Folder"/>; a note that cannot be written changes nothing.</summary>
    public static void WriteCrashNote(string text, DateTime now)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, $"crash-{now:yyyy-MM-dd}.log"), $"[{now:HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    /// <summary>The files a person reads, which the logs folder holds beside the app's bookkeeping (run markers, publish-stop files).</summary>
    private static readonly string[] ReadableFiles = ["run-*.log", "crash-*.log", "hang-*.dmp"];

    private static (bool Success, string Message, int Moved) MoveLogFiles(string from, string to, string? keep)
    {
        if (!Directory.Exists(from)) return (true, "There are no earlier logs to move.", 0);
        int moved = 0, left = 0;
        try
        {
            Directory.CreateDirectory(to);
            foreach (string file in ReadableFiles.SelectMany(pattern => Directory.EnumerateFiles(from, pattern)).ToList())
            {
                if (keep is not null && SamePath(file, keep)) continue;
                string target = Path.Combine(to, Path.GetFileName(file));
                if (File.Exists(target)) { left++; continue; }
                File.Copy(file, target);
                File.Delete(file);
                moved++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, $"The logs could not all be moved to {to}: {e.Message}", moved);
        }
        return (true, (moved == 0 ? "There are no earlier logs to move." : $"Moved {moved} file{(moved == 1 ? "" : "s")} from {from}.") + (left == 0 ? "" : $" {left} stayed: the same name is already here."), moved);
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes <paramref name="factory"/> the one for the code that runs from here (a test's own log, next to its own module state).</summary>
    public static void Use(ILoggerFactory factory) => isolated.Value = factory;

    private static void Prune(string logsDir, string current)
    {
        // The names sort by start time, so the newest are the last; the file just made is among them.
        string[] runs = [.. Directory.EnumerateFiles(logsDir, "run-*.log").Order(StringComparer.Ordinal)];
        foreach (string old in runs.Where(f => f != current).Take(Math.Max(0, runs.Length - KeepRuns)))
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A factory that closes the run's file when it is disposed (LoggerFactory does not dispose providers it was handed).</summary>
    private sealed class Owning(ILoggerFactory inner, LineSink sink, string? file, string folder, string appFolder, TimeProvider clock, int pid, LogLevel min) : ILoggerFactory
    {
        private readonly object gate = new();
        public string? File { get; private set; } = file;
        public string Folder { get; private set; } = folder;
        public string AppFolder { get; } = appFolder;
        public string? Previous { get; set; }
        public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);
        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
        public void Dispose() { inner.Dispose(); sink.Dispose(); }

        public (bool Success, string Message) Switch(string dir)
        {
            lock (gate)
            {
                if (SamePath(dir, Folder)) return (true, "The logs are already going to that folder.");
                string next = RunFile(dir, clock, pid);
                StreamWriter writer;
                try { Directory.CreateDirectory(dir); writer = Open(next); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return (false, $"{dir} cannot be written ({e.Message}); the logs stay in {Folder}.");
                }
                writer.WriteLine(File is null ? $"The logs were moved here from {Folder}" : $"Continued from {File}");   // the first line: a run split across two folders can be followed
                writer.WriteLine(HeaderLine("continued", clock, pid, min));
                sink.Swap(writer, File is null ? null : $"The logs go on in {next}");
                Previous = Folder;
                (File, Folder) = (next, dir);
                try { Prune(dir, next); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                CreateLogger("Logs").LogInformation("The logs go to {Folder}", dir);
                return (true, $"The logs go to {dir}.");
            }
        }
    }

    private sealed class LineSink(TimeProvider clock) : ILoggerProvider
    {
        public readonly TimeProvider Clock = clock;
        private readonly object gate = new();
        public StreamWriter? File;
        public TextWriter? Console;

        public void Header(string text) => Write(text);

        /// <summary>Closes the file in use after <paramref name="lastLine"/> and writes to <paramref name="next"/> from now on.</summary>
        public void Swap(StreamWriter next, string? lastLine)
        {
            lock (gate)
            {
                try { if (lastLine is not null) File?.WriteLine(lastLine); File?.Dispose(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException) { }
                File = next;
            }
        }

        private void Write(string line)
        {
            lock (gate)
            {
                try { File?.WriteLine(line); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException) { }
                try { Console?.WriteLine(line); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            }
        }

        public ILogger CreateLogger(string category) => new Line(this, category);

        public void Dispose()
        {
            lock (gate) { try { File?.Dispose(); } catch (IOException) { } File = null; Console = null; }
        }

        private sealed class Line(LineSink owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level != LogLevel.None;

            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
            {
                // The MCP library reports a refusal a tool threw on purpose (an McpException: a bad argument, a stale revision) as an error; the agent was told, nothing is broken.
                if (level == LogLevel.Error && error is McpException && category.StartsWith("ModelContextProtocol", StringComparison.Ordinal)) level = LogLevel.Warning;
                string label = level switch { LogLevel.Trace => "TRACE", LogLevel.Debug => "DEBUG", LogLevel.Information => "INFO", LogLevel.Warning => "WARN", LogLevel.Error => "ERROR", _ => "CRIT" };
                string trace = Activity.Current?.TraceId.ToString() ?? "-";
                string text = $"{owner.Clock.GetLocalNow():HH:mm:ss.fff} {label,-5} {trace} {category}: {format(state, null)}";
                owner.Write(error is null ? text : text + Environment.NewLine + error);
            }
        }
    }
}
