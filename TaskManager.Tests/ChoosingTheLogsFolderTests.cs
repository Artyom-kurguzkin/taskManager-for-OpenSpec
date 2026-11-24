using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Pages;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// Choosing where the logs are saved (#wi218): the run logs, crash notes and freeze dumps go to a folder the user picks in Settings, at once; the run markers and publish-stop files stay in the app's own folder.
/// The app's own folder, the chosen one and the clock are the test's own; nothing touches the real logs.
/// </summary>
public sealed class ChoosingTheLogsFolderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rtm-logsdir-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 23, 9, 8, 7, TimeSpan.Zero));
    private readonly TestRepo repo = new();

    private string AppFolder => Path.Combine(root, "app", "logs");
    private string Chosen => Path.Combine(root, "chosen");
    private string SettingsFile => Path.Combine(root, "settings.json");

    public ChoosingTheLogsFolderTests() => Directory.CreateDirectory(Chosen);

    public void Dispose()
    {
        repo.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    /// <summary>The app's log as the test sees it: a run file in the app's own folder, this test's own factory.</summary>
    private ILoggerFactory StartLog()
    {
        ILoggerFactory factory = AppLog.Create(AppFolder, console: false, LogLevel.Information, time, pid: 5, appFolder: AppFolder);
        AppLog.Use(factory);
        return factory;
    }

    /// <summary>The lines of a log file that the app still holds open for writing.</summary>
    private static string[] ReadLines(string file)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private BunitContext NewContext(SettingsStore store, FolderPicker? picker = null)
    {
        BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(store);
        ctx.Services.AddSingleton<Workspace>();
        if (picker is not null) ctx.Services.AddSingleton(picker);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        return ctx;
    }

    [Fact(DisplayName = "The General tab has a Logs folder row with a path, Choose folder, Open folder, Open current log, Use default and Move existing logs here")]
    public async Task TheRowIsOnTheGeneralTab()
    {
        StartLog();
        await using BunitContext ctx = NewContext(new SettingsStore(SettingsFile), picker: _ => Chosen);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        AngleSharp.Dom.IElement row = page.Find("[data-setting='logs-folder']");
        Assert.Equal("Logs folder", row.QuerySelector("th")!.TextContent.Trim());
        AngleSharp.Dom.IElement path = row.QuerySelector("input[aria-label='Logs folder']")!;
        Assert.Equal(AppFolder, path.GetAttribute("placeholder"));   // empty = the app's own folder, shown where the path goes
        Assert.Equal(["Choose folder", "Open folder", "Open current log", "Use default", "Move existing logs here"], row.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.True(row.QuerySelector("button.use-default-logs")!.HasAttribute("disabled"));   // nothing chosen: already the default
    }

    [Fact(DisplayName = "Choosing a logs folder writes the run log there at once, and the new log's first line names the previous file")]
    public async Task ChoosingSwitchesTheRunLogAtOnce()
    {
        ILoggerFactory factory = StartLog();
        string old = AppLog.CurrentFile!;
        factory.CreateLogger("Test").LogInformation("before the switch");
        SettingsStore store = new(SettingsFile);
        await using BunitContext ctx = NewContext(store, picker: _ => Chosen);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        page.ClickWhenReady("[data-setting='logs-folder'] button.pick-logs-folder");

        page.Eventually(() => Assert.Equal(Chosen, store.Current.LogsDir));
        string created = Assert.Single(Directory.GetFiles(Chosen, "run-*.log"));
        Assert.Equal(created, AppLog.CurrentFile);   // "Open current log" follows
        Assert.Equal(Chosen, AppLog.Folder);
        string[] lines = ReadLines(created);
        Assert.Contains(old, lines[0]);   // a run split across two folders can be followed
        Assert.Contains("pid 5", lines[1]);
        factory.CreateLogger("Test").LogInformation("after the switch");
        Assert.Contains(ReadLines(created), l => l.EndsWith("Test: after the switch"));
        string[] oldLines = ReadLines(old);
        Assert.Contains(oldLines, l => l.EndsWith("Test: before the switch"));
        Assert.Contains(created, oldLines[^1]);   // the old file says where the log goes on
        Assert.DoesNotContain(oldLines, l => l.Contains("after the switch"));

        // the next start reads the folder before the log starts
        Assert.Equal(Chosen, SettingsStore.PeekLogsDir(SettingsFile));
        Assert.Equal((Chosen, (string?)null), Paths.LogsFor(SettingsStore.PeekLogsDir(SettingsFile)));
    }

    [Fact(DisplayName = "Crash notes and freeze dumps go to the chosen logs folder")]
    public void CrashNotesAndDumpsFollow()
    {
        StartLog();
        Assert.Equal(AppFolder, AppLog.Folder);
        AppLog.WriteCrashNote("first", new DateTime(2026, 11, 23, 9, 0, 0));
        Assert.True(File.Exists(Path.Combine(AppFolder, "crash-2026-11-23.log")));

        Assert.True(AppLog.SwitchFolder(Chosen).Success);
        AppLog.WriteCrashNote("second", new DateTime(2026, 11, 23, 9, 5, 0));

        Assert.Equal("[09:05:00] second", File.ReadAllLines(Path.Combine(Chosen, "crash-2026-11-23.log"))[0]);
        Assert.DoesNotContain("second", File.ReadAllText(Path.Combine(AppFolder, "crash-2026-11-23.log")));
        // the watchdog is handed AppLog.Folder for its dump (Program): the chosen one now; a real dump is a copy of this process's memory, too big for a test to write
        Assert.Equal(Chosen, AppLog.Folder);
    }

    [Fact(DisplayName = "Run markers and publish-stop files stay in the app's own folder")]
    public void MarkersStayInTheAppsFolder()
    {
        StartLog();
        Assert.True(AppLog.SwitchFolder(Chosen).Success);

        Assert.Equal(AppFolder, AppLog.DefaultFolder);
        RunMarker.Begin(AppLog.DefaultFolder, 4100, (_, _) => false);
        RunMarker.WindowsIsEnding(AppLog.DefaultFolder, 4100);

        Assert.NotEmpty(Directory.GetFiles(AppFolder, "*.marker"));
        Assert.NotEmpty(Directory.GetFiles(AppFolder, RunMarker.WindowsEnd + "*"));
        Assert.Empty(Directory.GetFiles(Chosen, "*.marker"));
        Assert.Empty(Directory.GetFiles(Chosen, RunMarker.WindowsEnd + "*"));
        Assert.Empty(Directory.GetFiles(Chosen, RunMarker.PublishStop + "*"));
    }

    [Fact(DisplayName = "A logs folder that is missing or cannot be written falls back to the default with a warning")]
    public async Task AFolderThatCannotBeUsedFallsBack()
    {
        string missing = Path.Combine(root, "gone", "logs");
        string aFile = Path.Combine(root, "a-file.txt");
        File.WriteAllText(aFile, "not a folder");

        foreach (string bad in new[] { missing, aFile, "relative\\logs", "bad|name:" })
        {
            (string folder, string? warning) = Paths.LogsFor(bad);
            Assert.Equal(Paths.Logs, folder);
            Assert.NotNull(warning);
            Assert.Contains(Paths.Logs, warning);
        }
        Assert.Contains("is not there", Paths.LogsFor(missing).Warning);
        Assert.Equal((Paths.Logs, (string?)null), Paths.LogsFor(null));
        Assert.Equal((Chosen, (string?)null), Paths.LogsFor(Chosen));
        Assert.Empty(Directory.GetFiles(Chosen));   // the write probe leaves nothing behind

        // the page refuses it the same way and changes nothing
        StartLog();
        SettingsStore store = new(SettingsFile);
        await using BunitContext ctx = NewContext(store);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        page.ChangeWhenReady("input[aria-label='Logs folder']", missing);

        page.Eventually(() => Assert.Contains("is not there", page.Find(".logs-note").TextContent));
        Assert.Null(store.Current.LogsDir);
        Assert.Equal(AppFolder, AppLog.Folder);
        Assert.False(Directory.Exists(missing));

        // and a settings file that is missing or unreadable reads as none
        Assert.Null(SettingsStore.PeekLogsDir(Path.Combine(root, "no-settings.json")));
        File.WriteAllText(SettingsFile, "{ not json");
        Assert.Null(SettingsStore.PeekLogsDir(SettingsFile));
    }

    [Fact(DisplayName = "RTM_HOME still decides the logs folder for tests and demos")]
    public void RtmHomeDecidesTheDefault()
    {
        // The app's own folder is under Paths.Home, which RTM_HOME sets; nothing chosen means exactly that folder.
        Assert.Equal(Path.Combine(Paths.Home, "logs"), Paths.Logs);
        Assert.Equal((Paths.Logs, (string?)null), Paths.LogsFor(null));
        Assert.Equal((Paths.Logs, (string?)null), Paths.LogsFor("  "));
        Assert.Equal(Paths.Logs, AppLog.Folder);   // no log of the app in a test: the default
        Assert.Equal(Paths.Logs, AppLog.DefaultFolder);

        // a settings file with no logs folder (a demo's own file) changes nothing
        File.WriteAllText(SettingsFile, "{ \"Theme\": \"dark\" }");
        Assert.Equal((Paths.Logs, (string?)null), Paths.LogsFor(SettingsStore.PeekLogsDir(SettingsFile)));
    }

    [Fact(DisplayName = "Move existing logs here moves run logs, crash notes and dumps, and nothing else")]
    public async Task MovingTheLogsMovesOnlyTheThreeKinds()
    {
        StartLog();
        string[] moved = ["run-20260101-000000-1.log", "crash-2026-01-01.log", "hang-9-20260101-000000.dmp"];
        string[] stays = ["run-77.marker", "stopped-by-publish-77", "ended-by-windows-77", "server.log", "notes.txt"];
        foreach (string name in moved.Concat(stays)) File.WriteAllText(Path.Combine(AppFolder, name), name);
        string firstRun = AppLog.CurrentFile!;
        time.Advance(TimeSpan.FromSeconds(5));   // the new run file has a name of its own

        SettingsStore store = new(SettingsFile);
        await using BunitContext ctx = NewContext(store, picker: _ => Chosen);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        page.ClickWhenReady("[data-setting='logs-folder'] button.pick-logs-folder");
        page.Eventually(() => Assert.Equal(Chosen, AppLog.Folder));
        page.ClickWhenReady("[data-setting='logs-folder'] button.move-logs");

        page.Eventually(() => Assert.Contains("Moved 4 files", page.Find(".logs-note").TextContent));   // the three, and the first run's own file
        foreach (string name in moved) { Assert.True(File.Exists(Path.Combine(Chosen, name)), name); Assert.False(File.Exists(Path.Combine(AppFolder, name)), name); }
        Assert.True(File.Exists(Path.Combine(Chosen, Path.GetFileName(firstRun))));
        foreach (string name in stays) { Assert.True(File.Exists(Path.Combine(AppFolder, name)), name); Assert.False(File.Exists(Path.Combine(Chosen, name)), name); }
        Assert.Equal("crash-2026-01-01.log", File.ReadAllText(Path.Combine(Chosen, "crash-2026-01-01.log")));   // copied whole
        Assert.True(File.Exists(AppLog.CurrentFile));   // the log in use stays

        // nothing is left to move: the next press says so
        page.ClickWhenReady("[data-setting='logs-folder'] button.move-logs");
        page.Eventually(() => Assert.Contains("no earlier logs", page.Find(".logs-note").TextContent));
    }

    [Fact(DisplayName = "Use default puts the logs back in the app's folder")]
    public async Task UseDefaultGoesBack()
    {
        StartLog();
        SettingsStore store = new(SettingsFile);
        store.SetLogsDir(Chosen);
        Assert.True(AppLog.SwitchFolder(Chosen).Success);
        time.Advance(TimeSpan.FromSeconds(5));   // the new run file has a name of its own
        await using BunitContext ctx = NewContext(store);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        Assert.Equal(Chosen, page.Find("input[aria-label='Logs folder']").GetAttribute("value"));
        string chosenRun = AppLog.CurrentFile!;

        page.ClickWhenReady("[data-setting='logs-folder'] button.use-default-logs");

        page.Eventually(() => Assert.Null(store.Current.LogsDir));
        Assert.Equal(AppFolder, AppLog.Folder);
        Assert.StartsWith(AppFolder, AppLog.CurrentFile);
        Assert.Contains(chosenRun, ReadLines(AppLog.CurrentFile!)[0]);
        Assert.Null(SettingsStore.PeekLogsDir(SettingsFile));
        Assert.Empty(page.Find("input[aria-label='Logs folder']").GetAttribute("value") ?? "");
    }
}
