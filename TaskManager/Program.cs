using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Photino.Blazor;
using Radzen;
using TaskManager.Services;
using WorkItems;

namespace TaskManager;

/// <summary>Composition root: the only place that wires services, configures modules and owns the window.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Headless modes (no window): --serve is the one MCP server, --mcp the stdio shim agents start.
        if (args.Contains("--serve")) { Environment.ExitCode = Hosting.ServeHost.RunAsync(args).GetAwaiter().GetResult(); return; }
        if (args.Contains("--mcp")) { Environment.ExitCode = Hosting.McpShim.RunAsync(args).GetAwaiter().GetResult(); return; }

        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        PhotinoBlazorApp? app = null;

        // --repo <folder> --item <n>: a separate window on one work item (started from the item page's split menu).
        string? detachedRepo = Option(args, "--repo");
        int? detachedItem = int.TryParse(Option(args, "--item"), out int n) ? n : null;
        bool detached = detachedItem is not null && detachedRepo is not null && Directory.Exists(detachedRepo);

        // RTM_SETTINGS lets demos/tests run with their own settings file instead of the user's.
        SettingsStore settings = new(Environment.GetEnvironmentVariable("RTM_SETTINGS") ?? SettingsStore.DefaultPath);
        if (detached)
        {
            settings.ReadOnly = true;   // the main window owns the settings file; this process only reads it
            settings.Update(s => s with { LastRepo = detachedRepo, OpenRepos = [detachedRepo!] });   // memory only: this repository alone, the user's open-projects list stays as it is
        }
        builder.Services.AddRadzenComponents();   // the list view's data grid
        builder.Services.AddSingleton<UiService>();
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(new AppMode { Detached = detached, StartPath = detached ? $"item/{detachedItem}" : null });
        builder.Services.AddSingleton<Workspace>();
        builder.Services.AddSingleton<FolderPicker>(() => app?.MainWindow.ShowOpenFolder("Choose a repository with an openspec/ folder")?.FirstOrDefault());
        builder.Services.AddSingleton<FilePicker>(title => app?.MainWindow.ShowOpenFile(title)?.FirstOrDefault());
        builder.Services.AddSingleton<EditorLauncher>(EditorLaunchers.Default);
        builder.Services.AddSingleton<FolderOpener>(FolderOpeners.Default);
        builder.Services.AddSingleton<WindowLauncher>(WindowLaunchers.Default);
        builder.Services.AddSingleton(Hosting.McpServerControl.Default);   // Settings shows the headless MCP server and can start or stop it
        builder.Services.AddSingleton(new WindowFrame(() => app?.MainWindow.WindowHandle ?? IntPtr.Zero));
        builder.RootComponents.Add<App>("#app");

        app = builder.Build();
        if (detached) app.MainWindow.SetTitle("Repo tasks").SetUseOsDefaultSize(false).SetSize(960, 1000);   // a normal window beside the main one
        else app.MainWindow.SetTitle("Repo tasks").SetUseOsDefaultSize(true).SetMaximized(true);   // icons: WindowFrame
        // The exception goes to a log first: before the window exists a message box cannot be shown (it threw and hid the cause), and a crash is then silent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = e.ExceptionObject.ToString() ?? "";
            LogCrash(text);
            if (WindowIsUp(app)) try { app.MainWindow.ShowMessage("Error", text); } catch (Exception) { }
        };

        // Reopen the last repository; otherwise try the folder the app was started from.
        Workspace workspace = app.Services.GetRequiredService<Workspace>();
        string? start = detached ? detachedRepo : args.FirstOrDefault(Directory.Exists) ?? settings.Current.LastRepo ?? Environment.CurrentDirectory;
        // A repository that cannot be read cleanly (a file mid-write, a bad yaml the readers did not foresee) never stops the app from starting: it opens with what could be read.
        try { if (start is not null && Directory.Exists(start)) workspace.Open(start); }
        catch (Exception e) { LogCrash("Opening " + start + " failed at startup; the app starts anyway.\n" + e); }
        if (detached)
        {
            string? title = WorkItemsApi.Get(detachedItem!.Value)?.Title;
            app.MainWindow.SetTitle($"#wi{detachedItem} {title} — Repo tasks");
        }

        // The headless MCP server lives with the app: started here in the background (never blocks or fails the window), it ends when this process does.
        if (!detached)
            _ = Task.Run(async () =>
            {
                try { if (await Hosting.McpShim.EnsureServerAsync(Environment.ProcessId) is null) LogCrash("The MCP server did not start; the board tools will ask for the app until it does."); }
                catch (Exception e) { LogCrash("Starting the MCP server failed; the app goes on.\n" + e); }
            });

        app.Run();
        OpenSpecStoreApi.Close();
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
            Directory.CreateDirectory(Hosting.Paths.Logs);
            File.AppendAllText(Path.Combine(Hosting.Paths.Logs, $"crash-{DateTime.Now:yyyy-MM-dd}.log"), $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    private static string? Option(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
}
