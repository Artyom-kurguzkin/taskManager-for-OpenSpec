using Microsoft.Extensions.DependencyInjection;
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
    private static bool AnyWindowFocused { get { lock (focused) return focused.Count > 0; } }
    private static void Unfocus(PhotinoBlazorApp window) { lock (focused) focused.Remove(window); }

    [STAThread]
    private static void Main(string[] args)
    {
        // The one headless mode: --mcp, the stdio relay agents start. It holds no logic and no board; it forwards to the app, which serves agents itself (below).
        if (args.Contains("--mcp")) { Environment.ExitCode = Mcp.McpShim.RunAsync(args).GetAwaiter().GetResult(); return; }

        AppState.Caches.Install();   // the modules answer from the app's caches (AppState/), here and in the agent endpoint the app hosts

        // RTM_SETTINGS lets demos/tests run with their own settings file instead of the user's.
        // Otherwise the user's file, in the folder they chose (the pointer next to the default file names it) or the default one.
        SettingsStore settings = Environment.GetEnvironmentVariable("RTM_SETTINGS") is string own ? new(own) : SettingsStore.Open(SettingsStore.DefaultPath, SettingsStore.DefaultPointerPath);

        // The main window creates the app-wide services (one set per process); a separate window is handed the same instances, and has its own dialogs, address and pickers.
        ItemWindows? separate = null;
        AgentEndpoint? agents = null;   // started below, once the first window and its workspace exist
        PhotinoBlazorApp main = CreateWindow(args, new AppMode(), new UiService(), null, builder =>
        {
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton(new BoardStorageSettings(Paths.Home, StorageProviders.Builtin));   // connected storage, per project (Settings → Board storage)
            builder.Services.AddSingleton(SyncSetup.Default with { Focused = () => AnyWindowFocused });   // the idle check asks less often while no window has focus
            builder.Services.AddSingleton<BoardSyncService>();   // the main window syncs
            builder.Services.AddSingleton<Workspace>();
            builder.Services.AddSingleton<EditorLauncher>(EditorLaunchers.Default);
            builder.Services.AddSingleton<FolderOpener>(FolderOpeners.Default);
            builder.Services.AddSingleton<WindowLauncher>((root, id) => separate?.Open(root, id) ?? (false, "The app is still starting."));   // a window of this process, not another process
            builder.Services.AddSingleton<AgentStatus>(() => agents?.Status ?? new AgentEndpointStatus(false, Paths.Port, false, "Agents cannot connect yet."));   // Settings shows whether the app serves agents
        });
        main.MainWindow.SetTitle("Repo tasks").SetUseOsDefaultSize(true).SetMaximized(true);   // icons: WindowFrame
        // The exception goes to a log first: before the window exists a message box cannot be shown (it threw and hid the cause), and a crash is then silent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = e.ExceptionObject.ToString() ?? "";
            LogCrash(text);
            if (WindowIsUp(main)) try { main.MainWindow.ShowMessage("Error", text); } catch (Exception) { }
        };

        // Reopen the last repository; otherwise try the folder the app was started from.
        Workspace workspace = main.Services.GetRequiredService<Workspace>();
        string? start = args.FirstOrDefault(Directory.Exists) ?? settings.Current.LastRepo ?? Environment.CurrentDirectory;
        // A repository that cannot be read cleanly (a file mid-write, a bad yaml the readers did not foresee) never stops the app from starting: it opens with what could be read.
        try { if (start is not null && Directory.Exists(start)) workspace.Open(start); }
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
                    window.MainWindow.SetTitle(request.Title).SetUseOsDefaultSize(false).SetSize(960, 1000);   // a normal window beside the main one
                    window.MainWindow.WindowClosing += (_, _) => { request.Closed(); Unfocus(window); return false; };
                    window.Run();
                }
                catch (Exception e) { failed = e; }
            });
            if (failed is not null) throw failed;
        });

        // The app serves agents itself, in this process: Kestrel starts here, before the first window's message loop (Run below blocks this thread), and stops when Run returns.
        // A port that is taken (another copy of the app) is a status, not a failure: that window serves agents. An agent's write is the app's own write, which the file watcher ignores, so the window is told here.
        try { agents = AgentEndpoint.StartAsync(new AgentEndpointOptions(Paths.Port, Paths.Home, Paths.Token(), Paths.Version) { AfterWrite = workspace.Notify }).GetAwaiter().GetResult(); }
        catch (Exception e) { LogCrash("Starting the agent endpoint failed; the app goes on without serving agents.\n" + e); }

        // A crash in native code leaves no managed log: a marker per run, removed on a clean exit, lets the next start say that the last one died.
        try { foreach (string lost in RunMarker.Begin(Paths.Logs, Environment.ProcessId)) LogCrash(lost); }
        catch (Exception) { }

        main.Run();
        try { agents?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception e) { LogCrash("Stopping the agent endpoint failed; the app closes anyway. " + e); }
        try { main.Services.GetService<BoardSyncService>()?.SyncOnExit().GetAwaiter().GetResult(); }   // what has not been synced yet goes now, for at most 15 seconds
        catch (Exception e) { LogCrash("Syncing on exit failed; the app closes anyway. " + e); }
        RunMarker.End(Paths.Logs, Environment.ProcessId);
        OpenSpecStoreApi.Close();
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
            foreach (Type shared in new[] { typeof(SettingsStore), typeof(BoardStorageSettings), typeof(SyncSetup), typeof(BoardSyncService), typeof(Workspace), typeof(EditorLauncher), typeof(FolderOpener), typeof(WindowLauncher), typeof(AgentStatus) })
                builder.Services.AddSingleton(shared, first.Services.GetRequiredService(shared));
        builder.RootComponents.Add<App>("#app");
        self = builder.Build();
        PhotinoBlazorApp window = self;
        lock (focused) focused.Add(window);   // a new window has focus
        window.MainWindow.WindowFocusIn += (_, _) => { lock (focused) focused.Add(window); };
        window.MainWindow.WindowFocusOut += (_, _) => Unfocus(window);
        return window;
    }

    /// <summary>True once the native window exists (it is created when the app runs, not when it is built).</summary>
    private static bool WindowIsUp(PhotinoBlazorApp? app)
    {
        try { return app is not null && app.MainWindow.WindowHandle != IntPtr.Zero; }
        catch (Exception) { return false; }
    }

    /// <summary>Appends to %LOCALAPPDATA%/RepoTaskManager/logs/crash-yyyy-MM-dd.log; a log that cannot be written changes nothing.</summary>
    private static void LogCrash(string text)
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            File.AppendAllText(Path.Combine(Paths.Logs, $"crash-{DateTime.Now:yyyy-MM-dd}.log"), $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
