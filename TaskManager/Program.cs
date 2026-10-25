using Microsoft.Extensions.DependencyInjection;
using OpenSpecStore;
using Photino.Blazor;
using Radzen;
using TaskManager.Services;

namespace TaskManager;

/// <summary>Composition root: the only place that wires services, configures modules and owns the window.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        PhotinoBlazorApp? app = null;

        // RTM_SETTINGS lets demos/tests run with their own settings file instead of the user's.
        SettingsStore settings = new(Environment.GetEnvironmentVariable("RTM_SETTINGS") ?? SettingsStore.DefaultPath);
        builder.Services.AddRadzenComponents();   // the list view's data grid
        builder.Services.AddSingleton<UiService>();
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<Workspace>();
        builder.Services.AddSingleton<FolderPicker>(() => app?.MainWindow.ShowOpenFolder("Choose a repository with an openspec/ folder")?.FirstOrDefault());
        builder.Services.AddSingleton<EditorLauncher>(EditorLaunchers.Default);
        builder.Services.AddSingleton(new WindowFrame(() => app?.MainWindow.WindowHandle ?? IntPtr.Zero));
        builder.RootComponents.Add<App>("#app");

        app = builder.Build();
        app.MainWindow.SetTitle("Repo tasks").SetUseOsDefaultSize(true).SetMaximized(true);   // icons: WindowFrame
        AppDomain.CurrentDomain.UnhandledException += (_, e) => app.MainWindow.ShowMessage("Error", e.ExceptionObject.ToString());

        // Reopen the last repository; otherwise try the folder the app was started from.
        Workspace workspace = app.Services.GetRequiredService<Workspace>();
        string? start = args.FirstOrDefault(Directory.Exists) ?? settings.Current.LastRepo ?? Environment.CurrentDirectory;
        if (start is not null && Directory.Exists(start)) workspace.Open(start);

        app.Run();
        OpenSpecStoreApi.Close();
    }
}
