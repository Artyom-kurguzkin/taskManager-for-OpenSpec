using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenSpecStore;
using Photino.Blazor;
using Radzen;
using TaskManager.Api;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager;

/// <summary>Composition root: the only place that wires services, configures modules and owns the windows.</summary>
internal static class Program
{
    private static readonly HashSet<PhotinoBlazorApp> focused = [];   // the windows that have focus (Photino reports focus in and out); the sync service asks whether any has
    private static PhotinoBlazorApp? mainWindow;   // for the failure handlers' message box: set once the main window is built
    private static bool AnyWindowFocused { get { lock (focused) return focused.Count > 0; } }
    private static void Unfocus(PhotinoBlazorApp window) { lock (focused) focused.Remove(window); }

    [STAThread]
    private static void Main(string[] args)
    {
        // The one headless mode: --mcp, the stdio relay agents start. It holds no logic and no board; it forwards to the app, which serves agents itself (below).
        if (args.Contains("--mcp")) { Environment.ExitCode = Mcp.McpShim.RunAsync(args).GetAwaiter().GetResult(); return; }

        // --console-selftest: attaches to the terminal that started the app as startup does, prints "console ok" and exits (a check of the terminal path without a window).
        if (args.Contains("--console-selftest")) { Environment.ExitCode = AppLog.ConsoleSelfTest(args); return; }

        // The run's log first, so everything after it is in it: a file per run, and the console when there is one (dotnet run). RTM_LOG=Debug adds the detail.
        // The settings are read before the log starts, for the one value the log needs: the folder the user chose for the logs (a folder that is missing or cannot be written falls back to the app's own, said below).
        string? chosenLogs = Environment.GetEnvironmentVariable("RTM_SETTINGS") is string ownSettings ? SettingsStore.PeekLogsDir(ownSettings) : SettingsStore.PeekLogsDir(SettingsStore.DefaultPath, SettingsStore.DefaultPointerPath);
        (string logsFolder, string? logsWarning) = Paths.LogsFor(chosenLogs);
        AppLog.Init(logsFolder, AppLog.HasConsole(), AppLog.LevelFrom(Environment.GetEnvironmentVariable("RTM_LOG")));
        AppLog.Mark("main");   // the first line of Main that can be logged: the mark carries the time since the process started, so what ran before (the runtime, the assembly load) is in it
        ILogger log = AppLog.Logger("Program");
        if (logsWarning is not null) log.LogWarning("{Text}", logsWarning);
        // The handlers right after the log, before anything else can fail: an exception that reaches no catch, a task nobody observed, the window's dispatcher. Each logs first and none blocks (the message box is shown from another thread).
        using Failures failures = Failures.Install(LogCrash, text => { if (mainWindow is { } w && WindowIsUp(w)) w.MainWindow.ShowMessage("Error", text); });
        OpenSpecStoreApi.BackgroundFault = e => LogCrash("The file watcher's timer caught an exception; the app goes on.\n" + e);   // a pool thread's throw would end the process
        log.LogInformation("Starting with args [{Args}] in {Cwd}; settings {Settings}", string.Join(' ', args), Environment.CurrentDirectory, Environment.GetEnvironmentVariable("RTM_SETTINGS") ?? "default");

        AppState.Caches.Install();   // the modules answer from the app's caches (AppState/), here and in the agent endpoint the app hosts
        AppState.ItemsCache.UseIndex(Paths.Index);   // what the app read of each repository is kept here between runs: the next start paints the board from it

        // RTM_SETTINGS lets demos/tests run with their own settings file instead of the user's.
        // Otherwise the user's file, in the folder they chose (the pointer next to the default file names it) or the default one.
        SettingsStore settings = Environment.GetEnvironmentVariable("RTM_SETTINGS") is string own ? new(own) : SettingsStore.Open(SettingsStore.DefaultPath, SettingsStore.DefaultPointerPath);

        // The main window creates the app-wide services (one set per process); a separate window is handed the same instances, and has its own dialogs, address and pickers.
        ItemWindows? separate = null;
        AgentEndpoint? agents = null;   // started below, once the first window and its workspace exist
        UiService mainUi = new();
        // The update check (Settings shows its state, the popup is the main window's): it asks GitHub for the latest release once the window is up, never before.
        AppUpdates updates = new(new HttpClient(), mainUi, UpdateSetup.ForThisRun());
        PhotinoBlazorApp main = CreateWindow(args, new AppMode(), mainUi, null, builder =>
        {
            builder.Services.AddSingleton(updates);
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton(new BoardStorageSettings(Paths.Home, StorageProviders.Builtin));   // connected storage, per project (Settings → Board storage)
            builder.Services.AddSingleton(SyncSetup.Default with { Focused = () => AnyWindowFocused });   // the idle check asks less often while no window has focus
            builder.Services.AddSingleton<BoardSyncService>();   // the main window syncs
            builder.Services.AddSingleton<Workspace>();
            builder.Services.AddSingleton<EditorLauncher>(EditorLaunchers.Default);
            builder.Services.AddSingleton<FolderOpener>(FolderOpeners.Default);
            builder.Services.AddSingleton<WindowLauncher>((root, id) => separate?.Open(root, id) ?? (false, "The app is still starting."));   // a window of this process, not another process
            builder.Services.AddSingleton<MainWindowLauncher>(path => separate?.OpenFull(path) ?? (false, "The app is still starting."));   // one more full window of this process (the one agent endpoint stays)
            builder.Services.AddSingleton<AgentStatus>(() => agents?.Status ?? new AgentEndpointStatus(false, Paths.Port, false, "Agents cannot connect yet."));   // Settings shows whether the app serves agents
        });
        main.MainWindow.SetTitle("Repo tasks").SetUseOsDefaultSize(true).SetMaximized(true);   // icons: WindowFrame
        mainWindow = main;
        // Closing the window while files are still settling asks first (Wait and sync / Close anyway). The question needs the page, so the close is cancelled here and made again once it is answered.
        bool closeAnswered = false, asking = false;
        main.MainWindow.WindowClosing += (_, _) =>
        {
            if (closeAnswered || main.Services.GetService<BoardSyncService>() is not { } syncing || syncing.Settling().Count == 0) return false;
            if (asking) return true;
            asking = true;
            _ = Task.Run(async () =>
            {
                bool close = true;
                try { close = await new CloseGuard(syncing, mainUi).MayClose(); }
                catch (Exception e) { LogCrash("Asking about files still settling failed; the app closes anyway. " + e); }
                finally { asking = false; }
                if (!close) return;
                closeAnswered = true;
                main.MainWindow.Invoke(() => main.MainWindow.Close());
            });
            return true;
        };
        updates.RestartRequested += () => main.MainWindow.Invoke(() => main.MainWindow.Close());   // "Restart now": the helper is waiting for this process to end
        log.LogInformation("The window's dispatcher is {Watched}", failures.WatchDispatcher(main.Services.GetService<Microsoft.AspNetCore.Components.Dispatcher>()) ? "watched" : "not watchable (Photino exposes no event)");

        // A window that stops answering is noticed 30 s later (log and dump); a Windows shutdown or sign-out is named, so the next start does not call it a kill.
        UiWatchdog? watchdog = null;
        main.MainWindow.WindowCreated += (_, _) =>
        {
            AppLog.Mark("window-created");
            ILogger watch = AppLog.Logger("Watchdog");
            watchdog = new UiWatchdog(TimeProvider.System, UiWatchdog.PingThrough(ack => main.MainWindow.Invoke(ack)), text => watch.LogError("{Text}", text), () => UiWatchdog.WriteDump(AppLog.Folder, TimeProvider.System), text => watch.LogInformation("{Text}", text));
            watchdog.Start();
            _ = Task.Run(() => updates.CheckAsync());   // after the window shows; never throws, never waited for
#if DEBUG
            // RTM_DEBUG_FREEZE=1: 10 s after the window is up the UI thread sleeps for 40 s, to see the watchdog's record and dump. Debug builds only.
            if (Environment.GetEnvironmentVariable("RTM_DEBUG_FREEZE") == "1")
                Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => { watch.LogWarning("RTM_DEBUG_FREEZE: sleeping on the UI thread for 40 s"); main.MainWindow.Invoke(() => Thread.Sleep(TimeSpan.FromSeconds(40))); });
#endif
        };
        if (OperatingSystem.IsWindows()) Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => RunMarker.WindowsIsEnding(Paths.Logs, Environment.ProcessId);

        // Reopen the last repository; otherwise try the folder the app was started from. Only the cheap half runs here (the project is open, the tabs are right); the reading happens after the window is up (below).
        Workspace workspace = main.Services.GetRequiredService<Workspace>();
        workspace.StartVerifying(TimeProvider.System, () => AnyWindowFocused);   // a dropped watcher event is caught within 5 s while a window has focus
        string? start = args.FirstOrDefault(Directory.Exists) ?? settings.Current.LastRepo ?? Environment.CurrentDirectory;
        // A repository that cannot be read cleanly (a file mid-write, a bad yaml the readers did not foresee) never stops the app from starting: it opens with what could be read.
        log.LogInformation("Opening {Start}", start);
        bool registered = false;
        try { registered = start is not null && Directory.Exists(start) && workspace.Register(start).Success; }
        catch (Exception e) { LogCrash("Opening " + start + " failed at startup; the app starts anyway.\n" + e); }

        // "Separate window": one more PhotinoBlazorApp in this process (Photino.Blazor has no multi-window API; this is the pattern its maintainers point to, issues 150 and 153).
        // It is built on the UI thread, which a window captures when it is created. The first window owns the message loop; Run of a later one returns at once.
        // Closing the main window ends the process. A closed window's provider is not disposed: disposing it hangs (Photino issues 149 and 158).
        separate = new ItemWindows(workspace, request =>
        {
            Exception? failed = null;
            main.MainWindow.Invoke(() =>
            {
                try
                {
                    PhotinoBlazorApp window = CreateWindow([], request.Mode, request.Ui, main, _ => { });
                    window.MainWindow.SetTitle(request.Title).SetUseOsDefaultSize(false).SetSize(request.Mode.Detached ? 960 : 1280, 1000);   // a normal window beside the main one; a full one is wider (side menu and panes)
                    window.MainWindow.WindowClosing += (_, _) => { request.Closed(); Unfocus(window); return false; };
                    window.Run();
                }
                catch (Exception e) { failed = e; }
            });
            if (failed is not null) throw failed;
        });

        // The app serves agents itself, in this process: Kestrel starts beside the repository's load, right before the first window's message loop (Run below blocks this thread), and stops when Run returns.
        // Both run on the pool, so the window does not wait for either: the pages draw a skeleton and fill in as the load tells them. An agent's call that arrives meanwhile waits for the headers.
        // A port that is taken (another copy of the app) is a status, not a failure: that window serves agents. An agent's write is the app's own write, which the file watcher ignores, so the window is told here.
        // A crash in native code leaves no managed log: a marker per run, removed on a clean exit, lets the next start say that the last one died.
        Task starting = Task.Run(async () =>
        {
            Task load = registered ? workspace.LoadAsync(start!) : Task.CompletedTask;
            try { agents = await AgentEndpoint.StartAsync(new AgentEndpointOptions(Paths.Port, Paths.Home, Paths.Token(), Paths.Version) { AfterWrite = workspace.NotifyWritten, BeforeCall = () => workspace.WaitLoaded(TimeSpan.FromSeconds(60)) }); }
            catch (Exception e) { LogCrash("Starting the agent endpoint failed; the app goes on without serving agents.\n" + e); }
            try { foreach (string lost in RunMarker.Begin(Paths.Logs, Environment.ProcessId)) log.LogWarning("{Text}", lost); }   // how the last run ended, named as far as there is evidence
            catch (Exception) { }
            try { await load; }
            catch (Exception e) { LogCrash("Loading " + start + " failed at startup; the app goes on with what could be read.\n" + e); }
            log.LogInformation("Started; agent endpoint {Status}", agents?.Status);
        });

        log.LogInformation("The window is up; the repository and the agent endpoint start behind it");
        main.Run();
        watchdog?.Dispose();   // the window is gone: a ping that cannot be answered is no freeze
        log.LogInformation("The window closed; saving and stopping");
        try { updates.ApplyOnExit(); }   // a downloaded release that "Later" left is installed by the helper once this process has ended
        catch (Exception e) { LogCrash("Starting the update on exit failed; the app closes anyway. " + e); }
        try { starting.Wait(TimeSpan.FromSeconds(3)); }   // a load or an endpoint start still running at exit is given a moment, then left: the process ends it
        catch (Exception) { }
        // What the window asked a moment before closing is written first: an arrangement still in its 250 ms pause, a status or tick still in the write queue.
        try { settings.Flush(); }
        catch (Exception e) { LogCrash("Saving the last board arrangement on exit failed; the app closes anyway. " + e); }
        try
        {
            if (!workspace.DrainAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())   // ponytail: bounded at 5 s so a stuck lock cannot keep the process open
            {
                const string late = "A write was still waiting after 5 seconds on exit; the app closes anyway.";
                Console.Error.WriteLine(late);
                LogCrash(late);
            }
        }
        catch (Exception e) { LogCrash("Waiting for the last writes on exit failed; the app closes anyway. " + e); }
        try { workspace.SaveIndexes(); }   // the last writes are in the board: what the app read and changed is kept for the next start
        catch (Exception e) { LogCrash("Saving the index on exit failed; the app closes anyway. " + e); }
        try { agents?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception e) { LogCrash("Stopping the agent endpoint failed; the app closes anyway. " + e); }
        try { main.Services.GetService<BoardSyncService>()?.SyncOnExit().GetAwaiter().GetResult(); }   // what has not been synced yet goes now, for at most 15 seconds
        catch (Exception e) { LogCrash("Syncing on exit failed; the app closes anyway. " + e); }
        RunMarker.End(Paths.Logs, Environment.ProcessId);
        OpenSpecStoreApi.Close();
        log.LogInformation("Exited cleanly");
    }

    /// <summary>
    /// One window: its own provider with its own mode, dialogs, frame and pickers. <paramref name="first"/> is the main window, whose app-wide services (settings, workspace, sync, launchers)
    /// a later window shares instead of making its own; for the main window itself <paramref name="appWide"/> registers them. WebView2 options come from the environment, so every window has the same.
    /// </summary>
    private static PhotinoBlazorApp CreateWindow(string[] args, AppMode mode, UiService ui, PhotinoBlazorApp? first, Action<PhotinoBlazorAppBuilder> appWide)
    {
        PhotinoBlazorApp? self = null;
        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddRadzenComponents();   // the list view's data grid
        builder.Services.AddSingleton(ui);
        builder.Services.AddSingleton(mode);
        builder.Services.AddSingleton<FolderPicker>(title => self?.MainWindow.ShowOpenFolder(title ?? "Choose a repository with an openspec/ folder")?.FirstOrDefault());
        builder.Services.AddSingleton<FilePicker>(title => self?.MainWindow.ShowOpenFile(title)?.FirstOrDefault());
        builder.Services.AddSingleton(new WindowFrame(() => self?.MainWindow.WindowHandle ?? IntPtr.Zero));
        if (first is null) appWide(builder);
        else
            foreach (Type shared in new[] { typeof(SettingsStore), typeof(BoardStorageSettings), typeof(SyncSetup), typeof(BoardSyncService), typeof(Workspace), typeof(EditorLauncher), typeof(FolderOpener), typeof(WindowLauncher), typeof(MainWindowLauncher), typeof(AgentStatus), typeof(AppUpdates) })
                builder.Services.AddSingleton(shared, first.Services.GetRequiredService(shared));
        builder.RootComponents.Add<App>("#app");
        self = builder.Build();
        PhotinoBlazorApp window = self;
        window.MainWindow.SetLogVerbosity(0);   // Photino prints every message it sends to the page (base64 render batches) to stdout: the terminal the app now attaches to is for the app's own log lines
        lock (focused) focused.Add(window);   // a new window has focus
        window.MainWindow.WindowFocusIn += (_, _) => { lock (focused) focused.Add(window); };
        window.MainWindow.WindowFocusOut += (_, _) => Unfocus(window);
        WebViewHealth.Attach(window.MainWindow);   // the web view's own failures (its process ending) are logged
        return window;
    }

    /// <summary>True once the native window exists (it is created when the app runs, not when it is built).</summary>
    private static bool WindowIsUp(PhotinoBlazorApp? app)
    {
        try { return app is not null && app.MainWindow.WindowHandle != IntPtr.Zero; }
        catch (Exception) { return false; }
    }

    /// <summary>Logs the text and appends it to crash-yyyy-MM-dd.log in the logs folder (%LOCALAPPDATA%/RepoTaskManager/logs unless the user chose another); a log that cannot be written changes nothing.</summary>
    private static void LogCrash(string text)
    {
        AppLog.Logger("Crash").LogError("{Text}", text);   // the run's log too: the crash file stays for what a run's log cannot hold (a run that never started one)
        AppLog.WriteCrashNote(text, DateTime.Now);
    }
}
